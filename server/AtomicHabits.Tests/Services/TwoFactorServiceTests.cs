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
        // Shape: XXXX-XXXX-XXXX, and every character is from the Crockford alphabet
        // (0-9 A-Z minus the ambiguous I/L/O/U) — catches an accidental alphabet regression.
        const string allowed = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        codes.Should().AllSatisfy(c =>
        {
            c.Should().MatchRegex(@"^.{4}-.{4}-.{4}$");
            c.Replace("-", "").All(ch => allowed.Contains(ch)).Should().BeTrue();
        });
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
        var second = await svc.GenerateRecoveryCodesAsync(_userId, CancellationToken.None);

        // old set is invalidated...
        (await svc.VerifyRecoveryCodeAsync(_userId, first.First(), CancellationToken.None)).Should().BeFalse();
        // ...and the freshly issued set is usable
        (await svc.VerifyRecoveryCodeAsync(_userId, second.First(), CancellationToken.None)).Should().BeTrue();
        // count reflects the new set minus the one just consumed
        (await svc.CountRemainingRecoveryCodesAsync(_userId, CancellationToken.None)).Should().Be(9);
    }
}
