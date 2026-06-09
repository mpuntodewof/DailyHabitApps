using System;
using System.Linq;
using AtomicHabits.Models;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Models;

public class UserSubscriptionTests
{
    [Fact]
    public async Task User_defaults_to_Free_with_no_active_subscription()
    {
        using var db = TestDbContextFactory.Create();
        var user = new User { Username = "u1", Email = "u1@x.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var saved = db.Users.Single();
        saved.PlanTier.Should().Be(PlanTier.Free);
        saved.SubscriptionStatus.Should().Be(SubscriptionStatus.None);
        saved.CurrentPeriodEnd.Should().BeNull();
        saved.IsProActive.Should().BeFalse();
    }

    [Fact]
    public async Task User_can_be_set_Pro_active_with_period_end()
    {
        using var db = TestDbContextFactory.Create();
        var end = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var user = new User
        {
            Username = "u2", Email = "u2@x.com",
            PlanTier = PlanTier.Pro,
            SubscriptionStatus = SubscriptionStatus.Active,
            CurrentPeriodEnd = end
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var saved = db.Users.Single();
        saved.PlanTier.Should().Be(PlanTier.Pro);
        saved.SubscriptionStatus.Should().Be(SubscriptionStatus.Active);
        saved.CurrentPeriodEnd.Should().Be(end);
        saved.IsProActive.Should().BeTrue();
    }
}
