using System.Net;
using System.Threading;
using AtomicHabits.Config;
using AtomicHabits.Models;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class BillingServiceTests
{
    private sealed class FakeGateway : IStripeGateway
    {
        public string? LastEnsuredCustomer;
        public Task<string> EnsureCustomerAsync(string? existingCustomerId, string email, CancellationToken ct)
        { LastEnsuredCustomer = existingCustomerId; return Task.FromResult(existingCustomerId ?? "cus_new"); }
        public Task<string> CreateCheckoutSessionUrlAsync(string customerId, string priceId, string s, string c, CancellationToken ct)
            => Task.FromResult($"https://checkout.stripe/{customerId}");
        public Task<string> CreatePortalSessionUrlAsync(string customerId, string returnUrl, CancellationToken ct)
            => Task.FromResult($"https://portal.stripe/{customerId}");
    }

    private static BillingService NewService(AtomicHabits.Data.AppDbContext db, IStripeGateway gw)
    {
        var opts = Options.Create(new StripeOptions { PriceId = "price_1", SuccessUrl = "s", CancelUrl = "c", PortalReturnUrl = "r" });
        return new BillingService(db, gw, opts, NullLogger<BillingService>.Instance);
    }

    [Fact]
    public async Task CreateCheckout_creates_customer_when_absent_and_persists_it()
    {
        using var db = TestDbContextFactory.Create();
        var gw = new FakeGateway();
        var svc = NewService(db, gw);
        db.Users.Add(new User { Id = 1, Username = "u", Email = "u@x.com" });
        await db.SaveChangesAsync();

        var res = await svc.CreateCheckoutSessionAsync(1, CancellationToken.None);
        res.IsSuccess.Should().BeTrue();
        gw.LastEnsuredCustomer.Should().BeNull();
        db.Users.Single().StripeCustomerId.Should().Be("cus_new");
    }

    [Fact]
    public async Task CreateCheckout_reuses_existing_customer()
    {
        using var db = TestDbContextFactory.Create();
        var gw = new FakeGateway();
        var svc = NewService(db, gw);
        db.Users.Add(new User { Id = 1, Username = "u", Email = "u@x.com", StripeCustomerId = "cus_existing" });
        await db.SaveChangesAsync();

        var res = await svc.CreateCheckoutSessionAsync(1, CancellationToken.None);
        res.IsSuccess.Should().BeTrue();
        gw.LastEnsuredCustomer.Should().Be("cus_existing");
        db.Users.Single().StripeCustomerId.Should().Be("cus_existing");
    }

    [Fact]
    public async Task CreatePortal_requires_a_customer()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db, new FakeGateway());
        db.Users.Add(new User { Id = 1, Username = "u", Email = "u@x.com" });
        await db.SaveChangesAsync();

        var res = await svc.CreatePortalSessionAsync(1, CancellationToken.None);
        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatePortal_returns_url_for_customer()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db, new FakeGateway());
        db.Users.Add(new User { Id = 1, Username = "u", Email = "u@x.com", StripeCustomerId = "cus_1" });
        await db.SaveChangesAsync();

        var res = await svc.CreatePortalSessionAsync(1, CancellationToken.None);
        res.IsSuccess.Should().BeTrue();
        res.Result!.ToString().Should().Contain("portal.stripe/cus_1");
    }
}
