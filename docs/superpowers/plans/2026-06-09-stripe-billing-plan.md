# Stripe Billing (Plan 7) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the manual `set-plan` upgrade bridge with real Stripe subscription billing — hosted Checkout to subscribe, Customer Portal to manage/cancel, and a signature-verified webhook that syncs `User.PlanTier`/`SubscriptionStatus`/`CurrentPeriodEnd`.

**Architecture:** All Stripe SDK calls sit behind `IStripeGateway` (real impl wraps `Stripe.net`); webhook signature-verify/parse sits behind `IStripeEventReader`. `BillingService` orchestrates and is unit-tested with fakes (no keys). A `BillingController` exposes create-checkout-session, create-portal-session, and the webhook. The existing `[RequiresActiveSubscription]` gate and the Pro Dashboard cards are unchanged — Stripe just drives the entitlement fields they already read. `set-plan` is demoted to Development-only.

**Tech Stack:** ASP.NET Core 8, EF Core 9, Stripe.net, xUnit + FluentAssertions, React 19.

**Spec:** `docs/superpowers/specs/2026-06-09-stripe-billing-design.md`.

**Branch:** create/confirm `feat/performance-os-stripe` (orchestrator handles branch; do NOT implement on master).

---

## Context the implementer needs

- `User` (`server/AtomicHabits/Models/RBAC.cs`) has `PlanTier` (enum Free/Pro), `SubscriptionStatus` (enum None/Active/PastDue/Canceled), `CurrentPeriodEnd (DateTime?)`, and `[NotMapped] IsProActive`. This task ADDS `StripeCustomerId (string?)` + `StripeSubscriptionId (string?)`.
- Config pattern: options classes in `Config/` with a `public const string SectionName`; bound in `Program.cs` via `builder.Services.AddOptions<T>().Bind(builder.Configuration.GetSection(T.SectionName))` or `builder.Services.Configure<T>(...)`. Consume via `IOptions<T>`.
- DI registrations (`AddScoped`) live ~line 62 of `Program.cs` (after `IWeeklyReportService`). Middleware: `app.UseAuthentication()` ~line 297.
- `ApiResponse` (`AtomicHabits.Models`): `{ bool IsSuccess; HttpStatusCode StatusCode; object Result; List<string> ErrorMessages; }`.
- `User.GetUserId()` in `AtomicHabits.Utils`.
- `[RequiresActiveSubscription]` in `AtomicHabits.Authorization` — gates Pro endpoints (returns 403 unless PlanTier.Pro && SubscriptionStatus.Active). UNCHANGED by this plan.
- `SubscriptionController` (`Controllers/SubscriptionController.cs`) has the manual `POST set-plan` — this plan demotes it to Development-only.
- Frontend: `DashboardInsights.jsx` + `DashboardWeeklyReport.jsx` each have an `UpgradePanel` whose button currently POSTs `/Subscription/set-plan {plan:'Pro'}` then calls `refreshMe()` from `useAuth()`. This plan repoints those to Stripe checkout. `api` axios instance baseURL is `/api`.
- **BUILD LOCK:** if a build/test hits MSB3027/MSB3021 on `AtomicHabits.exe`, stop the running app (Stop-Process/taskkill on the named PID) and retry.

---

### Task 1: Stripe IDs on User + migration

**Files:**
- Modify: `server/AtomicHabits/Models/RBAC.cs`
- Test: `server/AtomicHabits.Tests/Models/UserSubscriptionTests.cs` (extend the existing file)
- Migration: generated

- [ ] **Step 1: Write the failing test**

Append to `server/AtomicHabits.Tests/Models/UserSubscriptionTests.cs`:
```csharp
    [Fact]
    public async Task User_persists_stripe_ids()
    {
        using var db = TestDbContextFactory.Create();
        var user = new User
        {
            Username = "s", Email = "s@x.com",
            StripeCustomerId = "cus_123", StripeSubscriptionId = "sub_456"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var saved = db.Users.Single();
        saved.StripeCustomerId.Should().Be("cus_123");
        saved.StripeSubscriptionId.Should().Be("sub_456");
    }
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test server/AtomicHabits.Tests --filter UserSubscriptionTests`
Expected: FAIL — `StripeCustomerId`/`StripeSubscriptionId` don't exist.

- [ ] **Step 3: Add the fields**

In `server/AtomicHabits/Models/RBAC.cs`, in the `User` class next to the existing subscription fields (`PlanTier`/`SubscriptionStatus`/`CurrentPeriodEnd`):
```csharp
        public string? StripeCustomerId { get; set; }
        public string? StripeSubscriptionId { get; set; }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test server/AtomicHabits.Tests --filter UserSubscriptionTests`
Expected: PASS.

- [ ] **Step 5: Generate + inspect migration**

Run (from `server/AtomicHabits/`): `dotnet ef migrations add AddStripeIdsToUser`
Inspect the generated `*.cs`: `Up()` must ONLY add two nullable `nvarchar` columns (`StripeCustomerId`, `StripeSubscriptionId`) to `Users`. No drops.

- [ ] **Step 6: Apply + full suite**

Run (from `server/AtomicHabits/`): `dotnet ef database update` → "Done."
Run (from `server/`): `dotnet test AtomicHabits.Tests/AtomicHabits.Tests.csproj` → all pass.

- [ ] **Step 7: Commit**

```bash
git add server/AtomicHabits/Models/RBAC.cs server/AtomicHabits/Migrations server/AtomicHabits.Tests/Models/UserSubscriptionTests.cs
git commit -m "feat: add Stripe customer/subscription ids to User"
```

---

### Task 2: Stripe package + `StripeOptions` config

**Files:**
- Modify: `server/AtomicHabits/AtomicHabits.csproj` (add Stripe.net)
- Create: `server/AtomicHabits/Config/StripeOptions.cs`
- Modify: `server/AtomicHabits/Program.cs` (bind options + set API key)
- Modify: `server/AtomicHabits/appsettings.json` (non-secret placeholders)

- [ ] **Step 1: Add the Stripe.net package**

Run (from `server/AtomicHabits/`): `dotnet add package Stripe.net`
(Use the latest stable; it targets netstandard2.0 so it's compatible with net8.0.)

- [ ] **Step 2: Create `StripeOptions`**

Create `server/AtomicHabits/Config/StripeOptions.cs` (mirrors `JwtOptions`):
```csharp
namespace AtomicHabits.Config
{
    public class StripeOptions
    {
        public const string SectionName = "Stripe";

        public string SecretKey { get; set; } = string.Empty;       // sk_test_… (user-secrets/env)
        public string WebhookSecret { get; set; } = string.Empty;   // whsec_…
        public string PriceId { get; set; } = string.Empty;         // price_…
        public string SuccessUrl { get; set; } = string.Empty;      // {WebBaseUrl}/dashboard?checkout=success
        public string CancelUrl { get; set; } = string.Empty;       // {WebBaseUrl}/dashboard?checkout=cancel
        public string PortalReturnUrl { get; set; } = string.Empty; // {WebBaseUrl}/dashboard
    }
}
```

- [ ] **Step 3: Bind options + set the Stripe API key in Program.cs**

In `server/AtomicHabits/Program.cs`, near the other `Configure<>` bindings (~line 96):
```csharp
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.SectionName));
```
And after `builder.Build()` (or right before `app.Run()`, where other startup config runs), set the global API key once:
```csharp
var stripeOpts = app.Services.GetRequiredService<IOptions<StripeOptions>>().Value;
if (!string.IsNullOrEmpty(stripeOpts.SecretKey))
    Stripe.StripeConfiguration.ApiKey = stripeOpts.SecretKey;
```
(Add `using Microsoft.Extensions.Options;` and `using AtomicHabits.Config;` if needed. The null-check lets the app still boot in environments without Stripe keys — the billing endpoints will simply fail at call time, which is fine for non-billing dev.)

- [ ] **Step 4: Add non-secret placeholders to appsettings.json**

In `server/AtomicHabits/appsettings.json`, add a `Stripe` section with the NON-secret URL fields filled and the secrets EMPTY (secrets come from user-secrets/env, never committed):
```json
  "Stripe": {
    "SecretKey": "",
    "WebhookSecret": "",
    "PriceId": "",
    "SuccessUrl": "http://localhost:5173/dashboard?checkout=success",
    "CancelUrl": "http://localhost:5173/dashboard?checkout=cancel",
    "PortalReturnUrl": "http://localhost:5173/dashboard"
  }
```

- [ ] **Step 5: Build**

Run (from `server/`): `dotnet build AtomicHabits.sln` → 0 errors.

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/AtomicHabits.csproj server/AtomicHabits/Config/StripeOptions.cs server/AtomicHabits/Program.cs server/AtomicHabits/appsettings.json
git commit -m "feat: add Stripe.net + StripeOptions config"
```

---

### Task 3: `IStripeGateway` + real implementation

**Files:**
- Create: `server/AtomicHabits/Services/IStripeGateway.cs` + `StripeGateway.cs`
- Modify: `server/AtomicHabits/Program.cs` (DI)

This wraps the Stripe SDK so `BillingService` is testable without keys. No unit tests for the real `StripeGateway` itself (it's a thin SDK adapter — covered by the live verification runbook); `BillingService` tests use a fake.

- [ ] **Step 1: Create the interface**

Create `server/AtomicHabits/Services/IStripeGateway.cs`:
```csharp
namespace AtomicHabits.Services
{
    public interface IStripeGateway
    {
        Task<string> EnsureCustomerAsync(string? existingCustomerId, string email, CancellationToken ct);
        Task<string> CreateCheckoutSessionUrlAsync(string customerId, string priceId, string successUrl, string cancelUrl, CancellationToken ct);
        Task<string> CreatePortalSessionUrlAsync(string customerId, string returnUrl, CancellationToken ct);
    }
}
```

- [ ] **Step 2: Create the real implementation**

Create `server/AtomicHabits/Services/StripeGateway.cs`:
```csharp
using Stripe;
using Stripe.BillingPortal;
using Stripe.Checkout;

namespace AtomicHabits.Services
{
    public class StripeGateway : IStripeGateway
    {
        public async Task<string> EnsureCustomerAsync(string? existingCustomerId, string email, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(existingCustomerId)) return existingCustomerId;
            var customer = await new CustomerService().CreateAsync(
                new CustomerCreateOptions { Email = email }, cancellationToken: ct);
            return customer.Id;
        }

        public async Task<string> CreateCheckoutSessionUrlAsync(string customerId, string priceId, string successUrl, string cancelUrl, CancellationToken ct)
        {
            var session = await new SessionService().CreateAsync(new SessionCreateOptions
            {
                Mode = "subscription",
                Customer = customerId,
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions { Price = priceId, Quantity = 1 }
                },
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
            }, cancellationToken: ct);
            return session.Url;
        }

        public async Task<string> CreatePortalSessionUrlAsync(string customerId, string returnUrl, CancellationToken ct)
        {
            var session = await new Stripe.BillingPortal.SessionService().CreateAsync(
                new Stripe.BillingPortal.SessionCreateOptions { Customer = customerId, ReturnUrl = returnUrl },
                cancellationToken: ct);
            return session.Url;
        }
    }
}
```
> Note: `Stripe.Checkout.SessionService` and `Stripe.BillingPortal.SessionService` are different types — the fully-qualified `Stripe.BillingPortal.*` in the portal method avoids the name clash. Verify exact class/option names against the installed Stripe.net version during impl; adjust if the SDK differs (the shape is stable across recent versions).

- [ ] **Step 3: Register in DI**

In `Program.cs` near the other `AddScoped` services:
```csharp
builder.Services.AddScoped<IStripeGateway, StripeGateway>();
```

- [ ] **Step 4: Build**

Run (from `server/`): `dotnet build AtomicHabits.sln` → 0 errors.

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Services/IStripeGateway.cs server/AtomicHabits/Services/StripeGateway.cs server/AtomicHabits/Program.cs
git commit -m "feat: add IStripeGateway + Stripe.net implementation"
```

---

### Task 4: `BillingService` — checkout + portal (test-first, fake gateway)

**Files:**
- Create: `server/AtomicHabits/Services/BillingService.cs`
- Test: `server/AtomicHabits.Tests/Services/BillingServiceTests.cs`

- [ ] **Step 1: Write the failing tests (with a fake gateway)**

Create `server/AtomicHabits.Tests/Services/BillingServiceTests.cs`:
```csharp
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
        var opts = Options.Create(new StripeOptions
        {
            PriceId = "price_1", SuccessUrl = "s", CancelUrl = "c", PortalReturnUrl = "r"
        });
        return new BillingService(db, gw, opts, NullLogger<BillingService>.Instance);
    }

    [Fact]
    public async Task CreateCheckout_creates_customer_when_absent_and_persists_it()
    {
        using var db = TestDbContextFactory.Create();
        var gw = new FakeGateway();
        var svc = NewService(db, gw);
        db.Users.Add(new User { Id = 1, Username = "u", Email = "u@x.com" }); // no StripeCustomerId
        await db.SaveChangesAsync();

        var res = await svc.CreateCheckoutSessionAsync(1, CancellationToken.None);
        res.IsSuccess.Should().BeTrue();
        gw.LastEnsuredCustomer.Should().BeNull(); // was absent
        db.Users.Single().StripeCustomerId.Should().Be("cus_new"); // persisted
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
        db.Users.Add(new User { Id = 1, Username = "u", Email = "u@x.com" }); // no customer id
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
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test server/AtomicHabits.Tests --filter BillingServiceTests`
Expected: FAIL — `BillingService` doesn't exist.

- [ ] **Step 3: Implement `BillingService` (checkout + portal only; webhook is Task 5)**

Create `server/AtomicHabits/Services/BillingService.cs`:
```csharp
using AtomicHabits.Config;
using AtomicHabits.Data;
using AtomicHabits.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IBillingService
    {
        Task<ApiResponse> CreateCheckoutSessionAsync(int userId, CancellationToken ct);
        Task<ApiResponse> CreatePortalSessionAsync(int userId, CancellationToken ct);
    }

    public class BillingService : IBillingService
    {
        private readonly AppDbContext _db;
        private readonly IStripeGateway _stripe;
        private readonly StripeOptions _opts;
        private readonly ILogger<BillingService> _logger;

        public BillingService(AppDbContext db, IStripeGateway stripe, IOptions<StripeOptions> opts, ILogger<BillingService> logger)
        {
            _db = db;
            _stripe = stripe;
            _opts = opts.Value;
            _logger = logger;
        }

        public async Task<ApiResponse> CreateCheckoutSessionAsync(int userId, CancellationToken ct)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null) return Error(HttpStatusCode.NotFound, "User not found");

            var customerId = await _stripe.EnsureCustomerAsync(user.StripeCustomerId, user.Email ?? "", ct);
            if (user.StripeCustomerId != customerId)
            {
                user.StripeCustomerId = customerId;
                await _db.SaveChangesAsync(ct);
            }

            var url = await _stripe.CreateCheckoutSessionUrlAsync(customerId, _opts.PriceId, _opts.SuccessUrl, _opts.CancelUrl, ct);
            return Ok(new { url });
        }

        public async Task<ApiResponse> CreatePortalSessionAsync(int userId, CancellationToken ct)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null) return Error(HttpStatusCode.NotFound, "User not found");
            if (string.IsNullOrEmpty(user.StripeCustomerId))
                return Error(HttpStatusCode.BadRequest, "No subscription to manage");

            var url = await _stripe.CreatePortalSessionUrlAsync(user.StripeCustomerId, _opts.PortalReturnUrl, ct);
            return Ok(new { url });
        }

        private static ApiResponse Ok(object result) =>
            new() { IsSuccess = true, StatusCode = HttpStatusCode.OK, Result = result };
        private static ApiResponse Error(HttpStatusCode status, string message) =>
            new() { IsSuccess = false, StatusCode = status, ErrorMessages = new List<string> { message } };
    }
}
```

- [ ] **Step 4: Run to verify pass + register DI**

Run: `dotnet test server/AtomicHabits.Tests --filter BillingServiceTests` → all 4 pass.
Add to `Program.cs`: `builder.Services.AddScoped<IBillingService, BillingService>();`

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Services/BillingService.cs server/AtomicHabits/Program.cs server/AtomicHabits.Tests/Services/BillingServiceTests.cs
git commit -m "feat: add BillingService (checkout + portal) with fake-gateway tests"
```

---

### Task 5: Webhook handler — entitlement sync (test-first)

**Files:**
- Create: `server/AtomicHabits/Services/StripeWebhookHandler.cs` (the testable sync logic)
- Test: `server/AtomicHabits.Tests/Services/StripeWebhookHandlerTests.cs`

The handler operates on already-parsed data (signature verify/parse is the controller's job in Task 6, via the Stripe SDK). To keep the handler fully unit-testable without Stripe types being awkward, the handler takes a **small normalized struct** the controller fills from the Stripe event.

- [ ] **Step 1: Write the failing tests**

Create `server/AtomicHabits.Tests/Services/StripeWebhookHandlerTests.cs`:
```csharp
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
        {
            Type = "checkout.session.completed",
            CustomerId = "cus_1",
            SubscriptionId = "sub_1",
            Status = "active",
            CurrentPeriodEnd = end
        }, CancellationToken.None);

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

        await NewHandler(db).HandleAsync(new StripeWebhookEvent
        { Type = "invoice.payment_failed", CustomerId = "cus_1" }, CancellationToken.None);

        db.Users.Single().SubscriptionStatus.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public async Task Subscription_deleted_reverts_to_free()
    {
        using var db = TestDbContextFactory.Create();
        await SeedUser(db, 1, "cus_1", PlanTier.Pro, SubscriptionStatus.Active);

        await NewHandler(db).HandleAsync(new StripeWebhookEvent
        { Type = "customer.subscription.deleted", CustomerId = "cus_1" }, CancellationToken.None);

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

        await NewHandler(db).HandleAsync(new StripeWebhookEvent
        { Type = "customer.subscription.updated", CustomerId = "cus_1", Status = "past_due" }, CancellationToken.None);

        db.Users.Single().SubscriptionStatus.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public async Task Unknown_customer_is_ignored_no_throw()
    {
        using var db = TestDbContextFactory.Create();
        await SeedUser(db, 1, "cus_1");
        var act = async () => await NewHandler(db).HandleAsync(new StripeWebhookEvent
        { Type = "checkout.session.completed", CustomerId = "cus_OTHER", SubscriptionId = "sub_x", Status = "active" }, CancellationToken.None);
        await act.Should().NotThrowAsync();
        db.Users.Single().PlanTier.Should().Be(PlanTier.Free); // unchanged
    }

    [Fact]
    public async Task Idempotent_duplicate_event_same_result()
    {
        using var db = TestDbContextFactory.Create();
        await SeedUser(db, 1, "cus_1");
        var ev = new StripeWebhookEvent { Type = "checkout.session.completed", CustomerId = "cus_1", SubscriptionId = "sub_1", Status = "active" };
        await NewHandler(db).HandleAsync(ev, CancellationToken.None);
        await NewHandler(db).HandleAsync(ev, CancellationToken.None); // duplicate
        db.Users.Single().PlanTier.Should().Be(PlanTier.Pro);
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test server/AtomicHabits.Tests --filter StripeWebhookHandlerTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement the handler + the normalized event struct**

Create `server/AtomicHabits/Services/StripeWebhookHandler.cs`:
```csharp
using AtomicHabits.Data;
using AtomicHabits.Models;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Services
{
    /// <summary>Normalized fields the controller extracts from a parsed Stripe event,
    /// so the sync logic is testable without Stripe SDK types.</summary>
    public class StripeWebhookEvent
    {
        public string Type { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public string? SubscriptionId { get; set; }
        public string? Status { get; set; }            // Stripe subscription status string
        public DateTime? CurrentPeriodEnd { get; set; }
    }

    public interface IStripeWebhookHandler
    {
        Task HandleAsync(StripeWebhookEvent ev, CancellationToken ct);
    }

    public class StripeWebhookHandler : IStripeWebhookHandler
    {
        private readonly AppDbContext _db;
        private readonly ILogger<StripeWebhookHandler> _logger;

        public StripeWebhookHandler(AppDbContext db, ILogger<StripeWebhookHandler> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task HandleAsync(StripeWebhookEvent ev, CancellationToken ct)
        {
            // Always match by customer id (every relevant event carries it; the
            // subscription id isn't stored yet at checkout.session.completed).
            var user = await _db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == ev.CustomerId, ct);
            if (user == null)
            {
                _logger.LogInformation("Stripe webhook {Type} for unknown customer {Cus} — ignored", ev.Type, ev.CustomerId);
                return; // not our customer — ignore (return 200 at the controller)
            }

            switch (ev.Type)
            {
                case "checkout.session.completed":
                    user.StripeSubscriptionId = ev.SubscriptionId ?? user.StripeSubscriptionId;
                    user.PlanTier = PlanTier.Pro;
                    user.SubscriptionStatus = SubscriptionStatus.Active;
                    if (ev.CurrentPeriodEnd.HasValue) user.CurrentPeriodEnd = ev.CurrentPeriodEnd;
                    break;

                case "customer.subscription.updated":
                    user.SubscriptionStatus = MapStatus(ev.Status);
                    user.PlanTier = user.SubscriptionStatus == SubscriptionStatus.Canceled ? PlanTier.Free : PlanTier.Pro;
                    if (ev.CurrentPeriodEnd.HasValue) user.CurrentPeriodEnd = ev.CurrentPeriodEnd;
                    break;

                case "customer.subscription.deleted":
                    user.PlanTier = PlanTier.Free;
                    user.SubscriptionStatus = SubscriptionStatus.Canceled;
                    user.StripeSubscriptionId = null;
                    user.CurrentPeriodEnd = null;
                    break;

                case "invoice.payment_failed":
                    user.SubscriptionStatus = SubscriptionStatus.PastDue;
                    break;

                default:
                    _logger.LogInformation("Stripe webhook {Type} not handled — ignored", ev.Type);
                    return; // no change
            }

            await _db.SaveChangesAsync(ct);
        }

        private static SubscriptionStatus MapStatus(string? stripeStatus) => stripeStatus switch
        {
            "active" or "trialing" => SubscriptionStatus.Active,
            "past_due" or "unpaid" => SubscriptionStatus.PastDue,
            "canceled" or "incomplete_expired" => SubscriptionStatus.Canceled,
            _ => SubscriptionStatus.PastDue // unknown non-active → treat as not-entitled
        };
    }
}
```

- [ ] **Step 4: Run to verify pass + register DI**

Run: `dotnet test server/AtomicHabits.Tests --filter StripeWebhookHandlerTests` → all 6 pass.
Add to `Program.cs`: `builder.Services.AddScoped<IStripeWebhookHandler, StripeWebhookHandler>();`

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Services/StripeWebhookHandler.cs server/AtomicHabits/Program.cs server/AtomicHabits.Tests/Services/StripeWebhookHandlerTests.cs
git commit -m "feat: add Stripe webhook entitlement-sync handler (tested)"
```

---

### Task 6: `BillingController` — 3 endpoints (incl. signature-verified webhook)

**Files:**
- Create: `server/AtomicHabits/Controllers/BillingController.cs`

- [ ] **Step 1: Create the controller**

Create `server/AtomicHabits/Controllers/BillingController.cs`. The webhook reads the RAW body (required for Stripe signature verification) and uses `EventUtility.ConstructEvent` to verify + parse, then maps to `StripeWebhookEvent` and calls the handler:
```csharp
using AtomicHabits.Config;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stripe;

namespace AtomicHabits.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BillingController : ControllerBase
    {
        private readonly IBillingService _billing;
        private readonly IStripeWebhookHandler _webhook;
        private readonly StripeOptions _opts;
        private readonly ILogger<BillingController> _logger;

        public BillingController(IBillingService billing, IStripeWebhookHandler webhook,
            IOptions<StripeOptions> opts, ILogger<BillingController> logger)
        {
            _billing = billing;
            _webhook = webhook;
            _opts = opts.Value;
            _logger = logger;
        }

        [Authorize]
        [HttpPost("create-checkout-session")]
        public async Task<IActionResult> CreateCheckout(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _billing.CreateCheckoutSessionAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpPost("create-portal-session")]
        public async Task<IActionResult> CreatePortal(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _billing.CreatePortalSessionAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [AllowAnonymous]
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook(CancellationToken ct)
        {
            // Read the RAW body — required for signature verification.
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync(ct);
            var signature = Request.Headers["Stripe-Signature"];

            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(json, signature, _opts.WebhookSecret);
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Stripe webhook signature verification failed");
                return BadRequest(); // forged/invalid — reject, do not mutate
            }

            var normalized = MapEvent(stripeEvent);
            if (normalized != null)
                await _webhook.HandleAsync(normalized, ct);

            return Ok(); // 200 so Stripe stops retrying
        }

        private static StripeWebhookEvent? MapEvent(Event e)
        {
            switch (e.Type)
            {
                case "checkout.session.completed":
                {
                    var s = e.Data.Object as Stripe.Checkout.Session;
                    if (s == null) return null;
                    return new StripeWebhookEvent
                    {
                        Type = e.Type,
                        CustomerId = s.CustomerId,
                        SubscriptionId = s.SubscriptionId,
                        Status = "active"
                    };
                }
                case "customer.subscription.updated":
                case "customer.subscription.deleted":
                {
                    var sub = e.Data.Object as Subscription;
                    if (sub == null) return null;
                    return new StripeWebhookEvent
                    {
                        Type = e.Type,
                        CustomerId = sub.CustomerId,
                        SubscriptionId = sub.Id,
                        Status = sub.Status,
                        CurrentPeriodEnd = sub.CurrentPeriodEnd
                    };
                }
                case "invoice.payment_failed":
                {
                    var inv = e.Data.Object as Invoice;
                    if (inv == null) return null;
                    return new StripeWebhookEvent { Type = e.Type, CustomerId = inv.CustomerId };
                }
                default:
                    return null; // ignored
            }
        }
    }
}
```
> Note: property names on Stripe objects (`s.SubscriptionId`, `sub.CurrentPeriodEnd`, `inv.CustomerId`) can vary slightly by Stripe.net major version. During impl, verify against the installed version and adjust (e.g. `sub.Items.Data[0].CurrentPeriodEnd` in some versions). The handler/tests don't depend on these — only this mapping does.

- [ ] **Step 2: Build + full suite**

Run (from `server/`): `dotnet build AtomicHabits.sln` → 0 errors; `dotnet test AtomicHabits.Tests/AtomicHabits.Tests.csproj` → all pass (95 + Task1(1) + Task4(4) + Task5(6) = 106).

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits/Controllers/BillingController.cs
git commit -m "feat: add BillingController (checkout, portal, signed webhook)"
```

---

### Task 7: Demote `set-plan` to Development-only

**Files:**
- Modify: `server/AtomicHabits/Controllers/SubscriptionController.cs`

- [ ] **Step 1: Guard the endpoint**

In `SubscriptionController`, inject `IWebHostEnvironment env` (constructor) and at the top of the `SetPlan` action, return 404 outside Development:
```csharp
            if (!_env.IsDevelopment())
                return NotFound(); // manual plan toggle is a dev-only bridge; Stripe drives prod
```
Add the `IWebHostEnvironment _env` field + ctor param + `using Microsoft.AspNetCore.Hosting;` / `using Microsoft.Extensions.Hosting;` as needed.

- [ ] **Step 2: Build + full suite**

Run (from `server/`): `dotnet build AtomicHabits.sln` → 0 errors; `dotnet test` → all pass.

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits/Controllers/SubscriptionController.cs
git commit -m "refactor: gate manual set-plan to Development-only (Stripe drives prod)"
```

---

### Task 8: Frontend — checkout + manage-subscription wiring

**Files:**
- Create: `client-ui/src/utils/billing.js` (shared helpers)
- Modify: `client-ui/src/views/dashboard/components/DashboardInsights.jsx`, `DashboardWeeklyReport.jsx`

- [ ] **Step 1: Create shared billing helpers**

Create `client-ui/src/utils/billing.js`:
```js
import api from '../api/axiosInstance';

// Redirects the browser to Stripe Checkout. Replaces the manual set-plan upgrade.
export async function startCheckout() {
  const res = await api.post('/Billing/create-checkout-session');
  const url = res.data?.result?.url;
  if (url) window.location.href = url;
}

// Redirects to the Stripe Customer Portal (manage/cancel).
export async function openBillingPortal() {
  const res = await api.post('/Billing/create-portal-session');
  const url = res.data?.result?.url;
  if (url) window.location.href = url;
}
```

- [ ] **Step 2: Repoint the upgrade buttons**

In BOTH `DashboardInsights.jsx` and `DashboardWeeklyReport.jsx`, in their `UpgradePanel`'s `handleUpgrade`: replace the `api.post('/Subscription/set-plan', { plan: 'Pro' })` + `refreshMe()` body with:
```jsx
import { startCheckout } from '../../../utils/billing';
// ...
  const handleUpgrade = async () => {
    setBusy(true);
    try {
      await startCheckout(); // redirects to Stripe; control leaves the page
    } catch (err) {
      console.warn('Checkout failed:', err?.message);
      setBusy(false); // only reached if the redirect didn't happen
    }
  };
```
(Keep the `busy` state + button. On success the browser navigates away to Stripe, so no `refreshMe` needed here — the webhook + the return-to-dashboard refresh handle Pro state.)

- [ ] **Step 3: Optional "Manage subscription" affordance (Pro users)**

In each Pro card's *content* (the non-fallback branch shown to Pro users), or simplest: add a small "Manage subscription" text button in `DashboardWeeklyReport`'s report card footer that calls `openBillingPortal()`. Keep it minimal — one button, only rendered when the user is Pro (the card content already only renders for Pro via `<RequirePro>`).

- [ ] **Step 4: Refresh on return from Stripe**

In `Dashboard.jsx`, add an effect: if `window.location.search` contains `checkout=success`, call `refreshMe()` from `useAuth()` once on mount so `isPro` reflects the new subscription (the webhook is the source of truth; this just nudges the UI). Strip the query param afterward (optional). Keep simple — no polling.
```jsx
// in Dashboard component:
const { refreshMe } = useAuth();
useEffect(() => {
  if (typeof window !== 'undefined' && window.location.search.includes('checkout=success')) {
    refreshMe?.();
  }
}, [refreshMe]);
```
(Add the `useAuth` import. This requires Dashboard to be inside the AuthProvider — it is.)

- [ ] **Step 5: Build**

Run (from `client-ui/`): `npx vite build` → succeeds.

- [ ] **Step 6: Commit**

```bash
git add client-ui/src/utils/billing.js client-ui/src/views/dashboard/
git commit -m "feat: wire Stripe checkout + portal into Dashboard Pro cards"
```

---

## Self-Review (completed by plan author)

- **Spec coverage:** §1 schema (Task 1) + config/package (Task 2) + set-plan demotion (Task 7). §2 three endpoints — checkout/portal (Task 4 service + Task 6 controller), webhook (Task 5 handler + Task 6 controller signature-verify + event mapping; all 4 events). §3 testability — `IStripeGateway` fake in Task 4, normalized `StripeWebhookEvent` makes the handler key-free testable in Task 5; frontend wiring Task 8; live runbook = separate doc (controller-orchestrator writes it post-build). ✓
- **Gate unchanged:** `[RequiresActiveSubscription]` + Pro cards untouched — Stripe drives the fields they read. ✓
- **Webhook security:** signature verification via `EventUtility.ConstructEvent`; failure → 400, no mutation (Task 6). Raw-body read handled explicitly (the ASP.NET gotcha). ✓
- **Idempotency + unknown-customer:** tested (Task 5). User lookup by customer id for all events (matches spec's tightened rule). ✓
- **Placeholders:** Stripe SDK property-name notes (Tasks 3, 6) flag that exact names may vary by SDK version and must be verified during impl — this is honest (can't pin a version's API from here), not a placeholder; the testable logic (handler) is fully specified and doesn't depend on them.
- **Type consistency:** `StripeWebhookEvent` fields, `IStripeGateway`/`IBillingService`/`IStripeWebhookHandler` signatures, `StripeOptions` fields, and the frontend `result.url` shape are consistent across tasks + the `{ url }` returned by BillingService. ✓
- **Live-verification dependency:** the only steps needing real keys are deferred to the runbook (post-implementation, user-owned). All code is unit-tested without keys.

## Subsequent
After this lands + live-verified: optional dunning emails, the deferred Weekly Report Sunday auto-email. This is the last v1 plan.
