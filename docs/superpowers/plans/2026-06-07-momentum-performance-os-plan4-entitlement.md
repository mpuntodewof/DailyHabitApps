# Momentum Performance OS — Plan 4: Subscription Entitlement + `<RequirePro>` (no Stripe)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a subscription entitlement axis (PlanTier/SubscriptionStatus on the user) and a `[RequiresActiveSubscription]` gate that mirrors the existing `[Permission]` system, expose it on `/Auth/me`, and add a `<RequirePro>` frontend wrapper — all driven by a MANUAL flag (no Stripe; that's Plan 7).

**Architecture:** Entitlement is a NEW axis, kept separate from RBAC (plan ≠ role). Subscription fields go on the existing `User` entity. A `[RequiresActiveSubscription]` ASP.NET authorization attribute + handler is built exactly like the existing `PermissionAuthorizationHandler` (`server/AtomicHabits/Authorization/PermissionAuthorization.cs`), returning 402 when the user lacks an active subscription. `/Auth/me` gains `{ planTier, subscriptionStatus }`; `AuthContext` exposes `isPro`; `<RequirePro>` mirrors the existing `<RequirePermission>`. A tiny admin/dev endpoint sets a user's plan manually so the paid features (Plans 5–6) can be built and tested before Stripe exists.

**Tech Stack:** ASP.NET Core 8, EF Core 9, xUnit + FluentAssertions, React 19 + MUI 7.

**Spec:** `docs/superpowers/specs/2026-06-07-momentum-performance-os-v1-design.md` §5 (minus the Stripe billing subsection).

**Branch:** create/confirm `feat/performance-os-entitlement` (orchestrator handles branch setup; do NOT implement on master).

---

## Existing patterns the implementer MUST mirror (read first)

- `server/AtomicHabits/Authorization/PermissionAuthorization.cs` — contains `PermissionAttribute : AuthorizeAttribute` (policy prefix `perm:`), `PermissionRequirement`, `PermissionPolicyProvider` (builds policies on demand), and `PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>`. The new subscription gate follows this SAME shape.
- `server/AtomicHabits/Program.cs` — registers `IAuthorizationPolicyProvider`/`IAuthorizationHandler` (around line 167: `AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>()`) and services via `AddScoped` (~line 59).
- `server/AtomicHabits/Controllers/AuthController.cs` `Me` action (line ~100) — returns `ApiResponse` with `Result = new { Id, Username, Email, AvatarUrl, Roles, permissions }`. We add `planTier`, `subscriptionStatus`.
- `server/AtomicHabits/Models/RBAC.cs` — the `User` class (fields like `Id, Username, Email, …`). Subscription fields go here.
- `server/AtomicHabits/Utils/ClaimsPrincipalExtensions.cs` — `User.GetUserId()`.
- `server/AtomicHabits/Data/AppDbContext.cs` — `OnModelCreating` for any index.
- Frontend `client-ui/src/components/RequirePermission.jsx` — the template for `<RequirePro>`; uses `useAuth()`.
- `client-ui/src/context/AuthContext.jsx` — loads `/Auth/me` (line ~33), stores `permissions`, exposes `hasPermission`/`hasRole` in the context value (~line 158). We add `planTier`/`isPro`.

---

### Task 1: Subscription fields on User + enums + migration (TDD)

**Files:**
- Create: `server/AtomicHabits/Models/Subscription.cs` (enums only — `PlanTier`, `SubscriptionStatus`)
- Modify: `server/AtomicHabits/Models/RBAC.cs` (add fields to `User`)
- Modify: `server/AtomicHabits/Data/AppDbContext.cs` (no new table; optional index)
- Test: `server/AtomicHabits.Tests/Models/UserSubscriptionTests.cs`
- Migration: generated

- [ ] **Step 1: Write the failing test**

Create `server/AtomicHabits.Tests/Models/UserSubscriptionTests.cs`:
```csharp
using System;
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
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test server/AtomicHabits.Tests --filter UserSubscriptionTests`
Expected: FAIL — `PlanTier`/`SubscriptionStatus` don't exist.

- [ ] **Step 3: Add the enums**

Create `server/AtomicHabits/Models/Subscription.cs`:
```csharp
namespace AtomicHabits.Models
{
    public enum PlanTier { Free = 0, Pro = 1 }

    // None = never subscribed; Active = paid & current; PastDue = payment failed
    // but in grace; Canceled = ended. Only Active grants Pro access.
    public enum SubscriptionStatus { None = 0, Active = 1, PastDue = 2, Canceled = 3 }
}
```

- [ ] **Step 4: Add fields to User**

In `server/AtomicHabits/Models/RBAC.cs`, add to the `User` class (after the existing scalar properties, before the nav collections):
```csharp
        public PlanTier PlanTier { get; set; } = PlanTier.Free;
        public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.None;
        public DateTime? CurrentPeriodEnd { get; set; }

        // Convenience: an active Pro entitlement. (Stripe will keep these in sync in Plan 7.)
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public bool IsProActive =>
            PlanTier == PlanTier.Pro && SubscriptionStatus == SubscriptionStatus.Active;
```
Add `using System.ComponentModel.DataAnnotations.Schema;` if not present (or use the fully-qualified attribute as shown).

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test server/AtomicHabits.Tests --filter UserSubscriptionTests`
Expected: PASS (both facts).

- [ ] **Step 6: Generate + inspect the migration**

Run (from `server/AtomicHabits/`): `dotnet ef migrations add AddSubscriptionToUser`
Inspect the generated `*.cs`: it must ONLY add three columns to `Users` (`PlanTier` int, `SubscriptionStatus` int, `CurrentPeriodEnd` datetime2 nullable). No table drops. `IsProActive` is `[NotMapped]` so it must NOT appear as a column.

- [ ] **Step 7: Apply migration + full test run**

Run (from `server/AtomicHabits/`): `dotnet ef database update` → "Done."
Run (from `server/`): `dotnet test AtomicHabits.Tests/AtomicHabits.Tests.csproj` → all pass.

> NOTE: if the build fails with a file-lock error on `AtomicHabits.exe`, a dev
> instance of the app is running — stop it, then retry.

- [ ] **Step 8: Commit**

```bash
git add server/AtomicHabits/Models/Subscription.cs server/AtomicHabits/Models/RBAC.cs server/AtomicHabits/Data/AppDbContext.cs server/AtomicHabits/Migrations server/AtomicHabits.Tests/Models/UserSubscriptionTests.cs
git commit -m "feat: add subscription entitlement fields to User"
```

---

### Task 2: `[RequiresActiveSubscription]` attribute + authorization handler (TDD)

**Files:**
- Create: `server/AtomicHabits/Authorization/SubscriptionAuthorization.cs`
- Modify: `server/AtomicHabits/Program.cs` (register the handler)
- Test: `server/AtomicHabits.Tests/Authorization/SubscriptionAuthorizationHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `server/AtomicHabits.Tests/Authorization/SubscriptionAuthorizationHandlerTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test server/AtomicHabits.Tests --filter SubscriptionAuthorizationHandlerTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Create the attribute + requirement + handler**

Create `server/AtomicHabits/Authorization/SubscriptionAuthorization.cs` (mirrors `PermissionAuthorization.cs`):
```csharp
using AtomicHabits.Data;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Authorization
{
    /// <summary>
    /// Marks a controller/action as requiring an ACTIVE Pro subscription,
    /// e.g. [RequiresActiveSubscription]. Separate axis from [Permission] (RBAC).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class RequiresActiveSubscriptionAttribute : AuthorizeAttribute
    {
        public const string PolicyName = "sub:active";
        public RequiresActiveSubscriptionAttribute() : base(PolicyName) { }
    }

    public class ActiveSubscriptionRequirement : IAuthorizationRequirement { }

    /// <summary>
    /// Adds the "sub:active" policy on demand (so it need not be pre-registered),
    /// while delegating everything else to the default provider. Mirrors
    /// PermissionPolicyProvider.
    /// </summary>
    public class SubscriptionPolicyProvider : IAuthorizationPolicyProvider
    {
        private readonly DefaultAuthorizationPolicyProvider _fallback;
        public SubscriptionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)
            => _fallback = new DefaultAuthorizationPolicyProvider(options);

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (string.Equals(policyName, RequiresActiveSubscriptionAttribute.PolicyName, StringComparison.OrdinalIgnoreCase))
            {
                var policy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .AddRequirements(new ActiveSubscriptionRequirement())
                    .Build();
                return Task.FromResult<AuthorizationPolicy?>(policy);
            }
            return _fallback.GetPolicyAsync(policyName);
        }
    }

    public class SubscriptionAuthorizationHandler : AuthorizationHandler<ActiveSubscriptionRequirement>
    {
        private readonly AppDbContext _db;
        private readonly ILogger<SubscriptionAuthorizationHandler> _logger;

        public SubscriptionAuthorizationHandler(AppDbContext db, ILogger<SubscriptionAuthorizationHandler> logger)
        {
            _db = db;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveSubscriptionRequirement requirement)
        {
            var userId = context.User.GetUserId();
            if (userId is null) return;

            var isActive = await _db.Users
                .Where(u => u.Id == userId.Value)
                .Select(u => u.PlanTier == Models.PlanTier.Pro && u.SubscriptionStatus == Models.SubscriptionStatus.Active)
                .FirstOrDefaultAsync();

            if (isActive) context.Succeed(requirement);
            else _logger.LogInformation("Active subscription required; denied for user {UserId}", userId);
        }
    }
}
```

> IMPORTANT — policy-provider composition: the app already registers a
> `PermissionPolicyProvider` as `IAuthorizationPolicyProvider`. ASP.NET allows
> only ONE `IAuthorizationPolicyProvider`. So do NOT register a second provider.
> Instead, in Step 4, FOLD the `sub:active` policy into the existing
> `PermissionPolicyProvider.GetPolicyAsync` (add an `if` branch for the
> subscription policy name) OR register a single combined provider. The cleanest
> minimal change: edit `PermissionPolicyProvider.GetPolicyAsync` to also handle
> `RequiresActiveSubscriptionAttribute.PolicyName`. Delete the
> `SubscriptionPolicyProvider` class in that case (it's included above only to
> show the policy shape). Pick ONE approach and note which.

- [ ] **Step 4: Register the handler + wire the policy**

In `server/AtomicHabits/Program.cs`, near the existing `AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>()` (~line 167):
```csharp
builder.Services.AddScoped<IAuthorizationHandler, SubscriptionAuthorizationHandler>();
```
Then wire the `sub:active` policy by adding a branch to the EXISTING `PermissionPolicyProvider.GetPolicyAsync` (in `Authorization/PermissionAuthorization.cs`), before its fallback return:
```csharp
            if (string.Equals(policyName, RequiresActiveSubscriptionAttribute.PolicyName, StringComparison.OrdinalIgnoreCase))
            {
                var policy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .AddRequirements(new ActiveSubscriptionRequirement())
                    .Build();
                return Task.FromResult<AuthorizationPolicy?>(policy);
            }
```
(Add `using AtomicHabits.Authorization;` if needed — same namespace, so likely not.) Then DELETE the unused `SubscriptionPolicyProvider` class from `SubscriptionAuthorization.cs`.

- [ ] **Step 5: Run to verify pass + full suite**

Run: `dotnet test server/AtomicHabits.Tests/AtomicHabits.Tests.csproj`
Expected: all pass (existing + 3 new handler tests). `dotnet build AtomicHabits.sln` → 0 errors.

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/Authorization/ server/AtomicHabits/Program.cs server/AtomicHabits.Tests/Authorization/
git commit -m "feat: add [RequiresActiveSubscription] gate (mirrors permission system)"
```

---

### Task 3: Expose entitlement on `/Auth/me` + a manual set-plan dev endpoint

**Files:**
- Modify: `server/AtomicHabits/Controllers/AuthController.cs` (`Me` action)
- Create: `server/AtomicHabits/Controllers/SubscriptionController.cs` (manual flag for testing)
- Test: `server/AtomicHabits.Tests/` — covered by Task 2 handler tests + a controller smoke is optional; no new required test here (the manual endpoint is dev-only and trivial).

- [ ] **Step 1: Extend `/Auth/me`**

In `server/AtomicHabits/Controllers/AuthController.cs`, the `Me` action: add `PlanTier`/`SubscriptionStatus` to the user projection and the returned object. Change the `.Select(...)` to include them and the result anonymous object to expose them as strings:
```csharp
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.Email,
                    u.AvatarUrl,
                    u.PlanTier,
                    u.SubscriptionStatus,
                    Roles = u.UserRoles!.Select(ur => ur.Role.Name).ToList()
                })
```
and in the returned `Result`:
```csharp
                Result = new
                {
                    user.Id,
                    user.Username,
                    user.Email,
                    user.AvatarUrl,
                    planTier = user.PlanTier.ToString(),
                    subscriptionStatus = user.SubscriptionStatus.ToString(),
                    isPro = user.PlanTier == AtomicHabits.Models.PlanTier.Pro
                            && user.SubscriptionStatus == AtomicHabits.Models.SubscriptionStatus.Active,
                    user.Roles,
                    permissions
                }
```

- [ ] **Step 2: Create the manual set-plan endpoint (dev/testing bridge until Stripe)**

Create `server/AtomicHabits/Controllers/SubscriptionController.cs`. This lets you flip your own plan to Pro/Free to test the paid features (Plans 5–6) before Stripe exists. It sets the CURRENT user's plan (JWT-derived), so it can't touch other users.
```csharp
using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AtomicHabits.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SubscriptionController : ControllerBase
    {
        private readonly AppDbContext _db;
        public SubscriptionController(AppDbContext db) => _db = db;

        public class SetPlanDto { public string Plan { get; set; } = "Free"; }

        // Manual plan toggle for the CURRENT user. Temporary bridge until Stripe (Plan 7).
        [HttpPost("set-plan")]
        public async Task<IActionResult> SetPlan([FromBody] SetPlanDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
            if (user == null) return NotFound();

            if (string.Equals(dto.Plan, "Pro", StringComparison.OrdinalIgnoreCase))
            {
                user.PlanTier = PlanTier.Pro;
                user.SubscriptionStatus = SubscriptionStatus.Active;
                user.CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1);
            }
            else
            {
                user.PlanTier = PlanTier.Free;
                user.SubscriptionStatus = SubscriptionStatus.None;
                user.CurrentPeriodEnd = null;
            }
            await _db.SaveChangesAsync(ct);

            return Ok(new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = new { planTier = user.PlanTier.ToString(), subscriptionStatus = user.SubscriptionStatus.ToString() }
            });
        }
    }
}
```

- [ ] **Step 3: Build + full test run**

Run (from `server/`): `dotnet build AtomicHabits.sln` → 0 errors; `dotnet test AtomicHabits.Tests/AtomicHabits.Tests.csproj` → all pass.

- [ ] **Step 4: Commit**

```bash
git add server/AtomicHabits/Controllers/AuthController.cs server/AtomicHabits/Controllers/SubscriptionController.cs
git commit -m "feat: expose entitlement on /Auth/me + manual set-plan endpoint"
```

---

### Task 4: Frontend — `isPro` in AuthContext + `<RequirePro>` wrapper

**Files:**
- Modify: `client-ui/src/context/AuthContext.jsx`
- Create: `client-ui/src/components/RequirePro.jsx`

- [ ] **Step 1: Inspect AuthContext**

Read `client-ui/src/context/AuthContext.jsx`. Note where it calls `/Auth/me` (~line 33), how it stores `permissions`, and the context `value` object (~line 158) so you add `planTier`/`isPro` consistently.

- [ ] **Step 2: Store + expose entitlement**

In `AuthContext`, add state `const [planTier, setPlanTier] = useState('Free');` and `const [isPro, setIsPro] = useState(false);`. Where `/Auth/me` result is processed (where `setPermissions(me.permissions || [])` is), also set:
```jsx
        setPlanTier(me.planTier || 'Free');
        setIsPro(me.isPro === true);
```
Add `planTier`, `isPro` to the context `value` object (next to `permissions`/`hasPermission`). Reset them to defaults on logout (wherever `permissions`/user are cleared).

- [ ] **Step 3: Create `<RequirePro>`**

Create `client-ui/src/components/RequirePro.jsx` (mirrors `RequirePermission.jsx`):
```jsx
import { useAuth } from '../context/AuthContext';

/**
 * Renders children only when the current user has an ACTIVE Pro subscription.
 * UI-only — the backend [RequiresActiveSubscription] gate is authoritative.
 *
 * Usage:
 *   <RequirePro fallback={<UpgradePrompt />}>...</RequirePro>
 */
const RequirePro = ({ children, fallback = null }) => {
  const { isPro } = useAuth();
  if (!isPro) return fallback;
  return children;
};

export default RequirePro;
```

- [ ] **Step 4: Build**

Run (from `client-ui/`): `npx vite build`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add client-ui/src/context/AuthContext.jsx client-ui/src/components/RequirePro.jsx
git commit -m "feat: expose isPro in AuthContext + RequirePro wrapper"
```

---

## Self-Review (completed by plan author)

- **Spec coverage (§5 minus Stripe):** entitlement model on user — PlanTier/SubscriptionStatus/CurrentPeriodEnd (Task 1, tested). `[RequiresActiveSubscription]` attribute + handler mirroring the permission system, returns deny (→ 403/402) when not active (Task 2, tested incl. PastDue). `/Auth/me` extended with planTier/subscriptionStatus/isPro (Task 3). `<RequirePro>` mirroring `<RequirePermission>` (Task 4). Manual flag (Task 3 `set-plan`) per the spec's "build/test with a manual PlanTier flag first." Stripe explicitly OUT (Plan 7). ✓
- **Separate-from-RBAC:** new enums + handler are a distinct axis; the existing `[Permission]` system is untouched. ✓
- **Single-policy-provider trap:** explicitly flagged in Task 2 — fold `sub:active` into the existing `PermissionPolicyProvider` rather than registering a second `IAuthorizationPolicyProvider` (ASP.NET allows one). This is the most likely failure if done naively; the plan calls it out with the exact fix. ✓
- **Placeholders:** Task 4 says "inspect AuthContext first" for exact line placement (it can't hardcode unseen surrounding code) but gives the exact state + value additions. Not a placeholder failure. ✓
- **Type consistency:** `PlanTier`/`SubscriptionStatus` enums + values (`Free/Pro`, `None/Active/PastDue/Canceled`), `RequiresActiveSubscriptionAttribute.PolicyName = "sub:active"`, `ActiveSubscriptionRequirement`, `isPro` boolean — all consistent across backend Tasks 1–3 and frontend Task 4 (`me.isPro`, `me.planTier`). ✓
- **402 vs 403:** the spec says "402/403". ASP.NET's authorization failure returns 403 by default (not 402). The plan accepts the default 403 (a custom 402 result would need extra middleware — YAGNI for the manual-flag phase; note for Plan 7 if a distinct "payment required" signal is wanted). Documented here so it's a conscious choice, not a gap.

## Note on the 402
The handler denies → ASP.NET returns **403 Forbidden** by default. The spec wrote "402/403"; we use the framework default 403 for now. If a distinct 402 "Payment Required" is desired for the upgrade UX, that's a small follow-up (custom `IAuthorizationMiddlewareResultHandler`) best done with Stripe in Plan 7. The frontend `<RequirePro>` gates the UI regardless, so users see an upgrade prompt rather than a raw 403 in normal flow.

## Subsequent plans
Plan 5 (failure analysis insights — first feature behind `[RequiresActiveSubscription]` + `<RequirePro>`), Plan 6 (CEO report), Plan 7 (Stripe — flips the manual flag to real billing).
