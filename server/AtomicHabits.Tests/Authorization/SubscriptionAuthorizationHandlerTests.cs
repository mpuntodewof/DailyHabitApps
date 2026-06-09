using System.Security.Claims;
using System.Threading.Tasks;
using AtomicHabits.Authorization;
using AtomicHabits.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Authorization;

public class SubscriptionAuthorizationHandlerTests
{
    private static ClaimsPrincipal UserWithId(int id) =>
        new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, id.ToString())
        }, "test"));

    private static async Task<bool> Evaluate(AtomicHabits.Data.AppDbContext db, int userId)
    {
        var handler = new SubscriptionAuthorizationHandler(db, NullLogger<SubscriptionAuthorizationHandler>.Instance);
        var requirement = new ActiveSubscriptionRequirement();
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, UserWithId(userId), null);
        await handler.HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    [Fact]
    public async Task Pro_active_user_is_authorized()
    {
        using var db = TestDbContextFactory.Create();
        db.Users.Add(new User { Id = 1, Username = "p", Email = "p@x.com",
            PlanTier = PlanTier.Pro, SubscriptionStatus = SubscriptionStatus.Active });
        await db.SaveChangesAsync();
        (await Evaluate(db, 1)).Should().BeTrue();
    }

    [Fact]
    public async Task Free_user_is_not_authorized()
    {
        using var db = TestDbContextFactory.Create();
        db.Users.Add(new User { Id = 2, Username = "f", Email = "f@x.com",
            PlanTier = PlanTier.Free, SubscriptionStatus = SubscriptionStatus.None });
        await db.SaveChangesAsync();
        (await Evaluate(db, 2)).Should().BeFalse();
    }

    [Fact]
    public async Task Pro_but_pastdue_is_not_authorized()
    {
        using var db = TestDbContextFactory.Create();
        db.Users.Add(new User { Id = 3, Username = "d", Email = "d@x.com",
            PlanTier = PlanTier.Pro, SubscriptionStatus = SubscriptionStatus.PastDue });
        await db.SaveChangesAsync();
        (await Evaluate(db, 3)).Should().BeFalse();
    }
}
