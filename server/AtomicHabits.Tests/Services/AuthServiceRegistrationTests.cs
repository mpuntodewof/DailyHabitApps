using System.Linq;
using System.Threading.Tasks;
using AtomicHabits.Config;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using AtomicHabits.Service;      // TokenService / ITokenService live here (singular)
using AtomicHabits.Services;    // AuthService, TwoFactorService live here (plural)
using FluentAssertions;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class AuthServiceRegistrationTests
{
    private static AuthService NewService(AtomicHabits.Data.AppDbContext db)
    {
        var jwt = Options.Create(new JwtOptions
        {
            Secret = "test-signing-key-at-least-32-bytes-long-0123456789",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenMinutes = 60,
            RefreshTokenDays = 7
        });
        var app = Options.Create(new AppOptions
        {
            WebBaseUrl = "http://localhost:5173",
            ResetPasswordPath = "/auth/reset-password"
        });

        // TokenService is in AtomicHabits.Service (singular namespace)
        var tokenSvc = new TokenService(jwt, NullLogger<TokenService>.Instance);

        // TwoFactorService is in AtomicHabits.Services (plural namespace)
        var twoFactor = new TwoFactorService(db, NullLogger<TwoFactorService>.Instance);

        // RegisterAsync talks to _db directly and does not call IUserRepositories or
        // IEmailSender, so default (un-set-up) mocks are safe here. They satisfy the
        // constructor only. (If registration ever starts using them, set them up.)
        var userRepo = new Mock<IUserRepositories>();
        var emailSender = new Mock<IEmailSender>();

        // Constructor order (confirmed from AuthService.cs lines 51-59):
        //   db, tokenService, userRepo, emailSender, logger, twoFactor, jwt, app
        return new AuthService(
            db,
            tokenSvc,
            userRepo.Object,
            emailSender.Object,
            NullLogger<AuthService>.Instance,
            twoFactor,
            jwt,
            app);
    }

    /// <summary>
    /// Regression guard for bug bbaffa0: RegisterAsync opened a transaction but never
    /// called CommitAsync, so every registered user was silently rolled back on dispose.
    /// The assertion uses a FRESH context (sqlite.NewContext()) over the same SQLite
    /// connection — if CommitAsync were absent the transaction would roll back on dispose
    /// and the fresh context would see zero users, failing the test.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_persists_the_user()
    {
        using var sqlite = new SqliteTestDb();
        sqlite.Context.Roles.Add(new Role { Name = "User" });
        await sqlite.Context.SaveChangesAsync();

        var svc = NewService(sqlite.Context);
        var dto = new RegisterDto
        {
            Username = "alice",
            Email = "alice@test.local",
            Password = "Secret@123",
            Role = "User"
        };

        var res = await svc.RegisterAsync(dto, ctx: null);

        res.IsSuccess.Should().BeTrue();
        // a real access token comes back (guards against a silent token-generation regression)
        res.Result.Should().NotBeNull();

        // Assert via a FRESH context over the same connection — proves the transaction
        // was committed, not just that the entity lives in the first context's change
        // tracker. This is the genuine regression guard.
        using var verify = sqlite.NewContext();
        var saved = verify.Users.SingleOrDefault(u => u.Email == "alice@test.local");
        saved.Should().NotBeNull();

        // The role assignment is part of the SAME transaction — verifying the UserRole row
        // committed too confirms the whole transaction (user + role) persisted, not just the user.
        verify.UserRoles.Should().ContainSingle(ur => ur.UserId == saved!.Id);
    }

    [Fact]
    public async Task RegisterAsync_rejects_duplicate_email()
    {
        using var sqlite = new SqliteTestDb();

        // Seed an existing user (no explicit Id — let SQLite auto-generate it)
        sqlite.Context.Users.Add(new User
        {
            Username = "bob",
            Email = "dupe@test.local",
            PasswordHash = "x"
        });
        await sqlite.Context.SaveChangesAsync();

        var svc = NewService(sqlite.Context);
        var dto = new RegisterDto
        {
            Username = "bob2",
            Email = "dupe@test.local",
            Password = "Secret@123",
            Role = "User"
        };

        var res = await svc.RegisterAsync(dto, ctx: null);

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }
}
