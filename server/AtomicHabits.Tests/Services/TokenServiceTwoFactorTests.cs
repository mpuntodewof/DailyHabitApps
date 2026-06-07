using System;
using AtomicHabits.Config;
using AtomicHabits.Models;
using AtomicHabits.Service;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class TokenServiceTwoFactorTests
{
    private static TokenService NewService()
    {
        var opts = Options.Create(new JwtOptions
        {
            Secret = "test-signing-key-at-least-32-bytes-long-0123456789",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenMinutes = 60,
            RefreshTokenDays = 7
        });
        return new TokenService(opts, NullLogger<TokenService>.Instance);
    }

    private static User TestUser => new() { Id = 99, Username = "u", Email = "u@test.local" };

    [Fact]
    public void Pending_token_round_trips_the_user_id()
    {
        var svc = NewService();
        var token = svc.GenerateTwoFactorPendingToken(TestUser, TimeSpan.FromMinutes(5));

        var userId = svc.ValidateTwoFactorPendingToken(token);

        userId.Should().Be(99); // regression: was null before the sub/NameIdentifier fix
    }

    [Fact]
    public void Expired_pending_token_is_rejected()
    {
        var svc = NewService();
        var token = svc.GenerateTwoFactorPendingToken(TestUser, TimeSpan.FromMinutes(-10));

        svc.ValidateTwoFactorPendingToken(token).Should().BeNull();
    }

    [Fact]
    public void Garbage_token_is_rejected()
    {
        var svc = NewService();
        svc.ValidateTwoFactorPendingToken("not-a-jwt").Should().BeNull();
    }
}
