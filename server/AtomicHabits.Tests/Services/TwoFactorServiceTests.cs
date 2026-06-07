using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AtomicHabits.Models;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class TwoFactorServiceTests : IDisposable
{
    private readonly SqliteTestDb _sqlite = new();
    private int _userId;

    private TwoFactorService NewService(out AtomicHabits.Data.AppDbContext db)
    {
        db = _sqlite.Context;
        var user = new User { Username = "u", Email = "u@test.local", PasswordHash = "x" };
        db.Users.Add(user);
        db.SaveChanges();
        _userId = user.Id;
        return new TwoFactorService(db, NullLogger<TwoFactorService>.Instance);
    }

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task GenerateRecoveryCodesAsync_returns_10_unique_codes_and_stores_hashes()
    {
        var svc = NewService(out var db);

        var codes = await svc.GenerateRecoveryCodesAsync(_userId, CancellationToken.None);

        codes.Should().HaveCount(10);
        codes.Should().OnlyHaveUniqueItems();
        codes.Should().AllSatisfy(c => c.Should().MatchRegex(@"^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$"));
        db.TwoFactorRecoveryCodes.Should().HaveCount(10);
        db.TwoFactorRecoveryCodes.Select(c => c.CodeHash)
            .Should().NotContain(codes.First());
    }

    [Fact]
    public async Task VerifyRecoveryCodeAsync_accepts_a_valid_code_once_then_rejects_reuse()
    {
        var svc = NewService(out _);
        var codes = await svc.GenerateRecoveryCodesAsync(_userId, CancellationToken.None);
        var code = codes.First();

        (await svc.VerifyRecoveryCodeAsync(_userId, code, CancellationToken.None)).Should().BeTrue();
        (await svc.VerifyRecoveryCodeAsync(_userId, code, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyRecoveryCodeAsync_normalizes_dashes_and_case()
    {
        var svc = NewService(out _);
        var codes = await svc.GenerateRecoveryCodesAsync(_userId, CancellationToken.None);
        var dashless = codes.First().Replace("-", "").ToLowerInvariant();

        (await svc.VerifyRecoveryCodeAsync(_userId, dashless, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task CountRemainingRecoveryCodesAsync_reflects_consumption()
    {
        var svc = NewService(out _);
        var codes = await svc.GenerateRecoveryCodesAsync(_userId, CancellationToken.None);

        (await svc.CountRemainingRecoveryCodesAsync(_userId, CancellationToken.None)).Should().Be(10);
        await svc.VerifyRecoveryCodeAsync(_userId, codes.First(), CancellationToken.None);
        (await svc.CountRemainingRecoveryCodesAsync(_userId, CancellationToken.None)).Should().Be(9);
    }

    [Fact]
    public async Task GenerateRecoveryCodesAsync_replaces_the_previous_set()
    {
        var svc = NewService(out _);
        var first = await svc.GenerateRecoveryCodesAsync(_userId, CancellationToken.None);
        await svc.GenerateRecoveryCodesAsync(_userId, CancellationToken.None);

        (await svc.VerifyRecoveryCodeAsync(_userId, first.First(), CancellationToken.None)).Should().BeFalse();
        (await svc.CountRemainingRecoveryCodesAsync(_userId, CancellationToken.None)).Should().Be(10);
    }
}
