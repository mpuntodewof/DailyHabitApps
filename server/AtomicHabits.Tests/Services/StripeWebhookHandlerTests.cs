using System;
using System.Threading;
using AtomicHabits.Models;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class StripeWebhookHandlerTests
{
    private static StripeWebhookHandler NewHandler(AtomicHabits.Data.AppDbContext db) =>
        new StripeWebhookHandler(db, NullLogger<StripeWebhookHandler>.Instance);

    private static async Task SeedUser(AtomicHabits.Data.AppDbContext db, int id, string cus,
        PlanTier tier = PlanTier.Free, SubscriptionStatus status = SubscriptionStatus.None)
    {
        db.Users.Add(new User { Id = id, Username = $"u{id}", Email = $"u{id}@x.com",
            StripeCustomerId = cus, PlanTier = tier, SubscriptionStatus = status });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Checkout_completed_activates_pro()
    {
        using var db = TestDbContextFactory.Create();
        await SeedUser(db, 1, "cus_1");
        var end = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        await NewHandler(db).HandleAsync(new StripeWebhookEvent
        { Type = "checkout.session.completed", CustomerId = "cus_1", SubscriptionId = "sub_1", Status = "active", CurrentPeriodEnd = end }, CancellationToken.None);
        var u = db.Users.Single();
        u.PlanTier.Should().Be(PlanTier.Pro);
        u.SubscriptionStatus.Should().Be(SubscriptionStatus.Active);
        u.StripeSubscriptionId.Should().Be("sub_1");
        u.CurrentPeriodEnd.Should().Be(end);
    }

    [Fact]
    public async Task Payment_failed_marks_pastdue()
    {
        using var db = TestDbContextFactory.Create();
        await SeedUser(db, 1, "cus_1", PlanTier.Pro, SubscriptionStatus.Active);
        await NewHandler(db).HandleAsync(new StripeWebhookEvent { Type = "invoice.payment_failed", CustomerId = "cus_1" }, CancellationToken.None);
        db.Users.Single().SubscriptionStatus.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public async Task Subscription_deleted_reverts_to_free()
    {
        using var db = TestDbContextFactory.Create();
        await SeedUser(db, 1, "cus_1", PlanTier.Pro, SubscriptionStatus.Active);
        await NewHandler(db).HandleAsync(new StripeWebhookEvent { Type = "customer.subscription.deleted", CustomerId = "cus_1" }, CancellationToken.None);
        var u = db.Users.Single();
        u.PlanTier.Should().Be(PlanTier.Free);
        u.SubscriptionStatus.Should().Be(SubscriptionStatus.Canceled);
        u.StripeSubscriptionId.Should().BeNull();
        u.CurrentPeriodEnd.Should().BeNull();
    }

    [Fact]
    public async Task Subscription_updated_syncs_status()
    {
        using var db = TestDbContextFactory.Create();
        await SeedUser(db, 1, "cus_1", PlanTier.Pro, SubscriptionStatus.Active);
        await NewHandler(db).HandleAsync(new StripeWebhookEvent { Type = "customer.subscription.updated", CustomerId = "cus_1", Status = "past_due" }, CancellationToken.None);
        db.Users.Single().SubscriptionStatus.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public async Task Unknown_customer_is_ignored_no_throw()
    {
        using var db = TestDbContextFactory.Create();
        await SeedUser(db, 1, "cus_1");
        var act = async () => await NewHandler(db).HandleAsync(new StripeWebhookEvent { Type = "checkout.session.completed", CustomerId = "cus_OTHER", SubscriptionId = "sub_x", Status = "active" }, CancellationToken.None);
        await act.Should().NotThrowAsync();
        db.Users.Single().PlanTier.Should().Be(PlanTier.Free);
    }

    [Fact]
    public async Task Idempotent_duplicate_event_same_result()
    {
        using var db = TestDbContextFactory.Create();
        await SeedUser(db, 1, "cus_1");
        var ev = new StripeWebhookEvent { Type = "checkout.session.completed", CustomerId = "cus_1", SubscriptionId = "sub_1", Status = "active" };
        await NewHandler(db).HandleAsync(ev, CancellationToken.None);
        await NewHandler(db).HandleAsync(ev, CancellationToken.None);
        db.Users.Single().PlanTier.Should().Be(PlanTier.Pro);
    }
}
