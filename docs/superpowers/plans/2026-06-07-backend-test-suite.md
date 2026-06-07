# Backend Test Suite (regression + units + integration + IDOR) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a backend test suite that (1) locks down the two auth bugs found by manual e2e this session, (2) unit-tests the pure service logic, (3) adds HTTP-level integration tests for the auth/2FA flow, and (4) proves owner-scoping (IDOR) — all on xUnit + the in-memory harness, test-first where practical.

**Architecture:** Extends the `AtomicHabits.Tests` xUnit project (created by *Performance OS Plan 1*, Task 1 — see Dependency below) with four test areas. Pure-logic tests (`StreakCalculator`) need no DB. Service tests use EF Core's **InMemory** provider via the existing `TestDbContextFactory`. **Transaction-sensitive tests** (registration commit/rollback) use a **SQLite in-memory** fixture instead, because EF's InMemory provider silently ignores transactions and therefore cannot catch the missing-`CommitAsync` bug. Integration tests use `WebApplicationFactory<Program>` with the content-root pointed at the API project and the DB swapped to SQLite.

**Tech Stack:** xUnit, FluentAssertions, `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.AspNetCore.Mvc.Testing`, `Moq` (for `ITokenService`/`IEmailSender`/`IOptions` fakes in `AuthService` unit tests).

**Bugs this suite must catch (regression targets — both fixed this session):**
- `f561677` — `ValidateTwoFactorPendingToken` read `sub` but `JwtSecurityTokenHandler` remaps it to `ClaimTypes.NameIdentifier`, so every 2FA challenge was rejected.
- `bbaffa0` — `RegisterAsync` opened a transaction but never committed it; users never persisted.

---

## Dependency: Performance OS Plan 1, Task 1

That plan creates `server/AtomicHabits.Tests/` (xUnit), adds the project reference + `Microsoft.EntityFrameworkCore.InMemory` + `FluentAssertions`, wires it into `AtomicHabits.sln`, and adds `TestDbContextFactory.Create()` (unique-named InMemory `AppDbContext` per call).

- **If that project already exists when this plan runs:** skip to Task 1 below (which only *adds packages*).
- **If it does not exist yet:** run that plan's Task 1 first (Steps 1–5), then return here. Do NOT create a second/competing test project.

Verify before starting:
```bash
ls server/AtomicHabits.Tests/AtomicHabits.Tests.csproj && ls server/AtomicHabits.Tests/TestDbContextFactory.cs
```
Both present → proceed. Missing → run Performance OS Plan 1 Task 1 first.

---

## File Structure

```
server/AtomicHabits.Tests/
├── AtomicHabits.Tests.csproj          # +Sqlite, +Mvc.Testing, +Moq (Task 1)
├── TestDbContextFactory.cs            # (exists) InMemory factory
├── SqliteDbContextFactory.cs          # NEW (Task 2) — real-transaction-capable context
├── Services/
│   ├── StreakCalculatorTests.cs       # Task 3 — pure logic, no DB
│   ├── TwoFactorServiceTests.cs       # Task 4 — recovery-code lifecycle (InMemory)
│   ├── TokenServiceTwoFactorTests.cs  # Task 5 — pending-token round-trip (regression f561677)
│   ├── AuthServiceRegistrationTests.cs# Task 6 — register persists (regression bbaffa0, SQLite)
│   └── HabitServiceSummaryTests.cs     # Task 8 — per-frequency summary math (InMemory)
└── Integration/
    ├── ApiFactory.cs                  # Task 7 — WebApplicationFactory<Program> + SQLite swap
    ├── AuthFlowIntegrationTests.cs    # Task 7 — register→login→2FA(recovery) over HTTP
    └── HabitOwnershipTests.cs         # Task 9 — IDOR: user B can't read user A's habits
```

`Program` must be visible to the test assembly. ASP.NET Core's top-level-statement `Program` is `internal`; Task 7 adds a one-line `public partial class Program { }` to the API so `WebApplicationFactory<Program>` can reference it (standard pattern, no behavioral change).

---

## Task 1: Add test packages (Sqlite, Mvc.Testing, Moq)

**Files:**
- Modify: `server/AtomicHabits.Tests/AtomicHabits.Tests.csproj`

- [ ] **Step 1: Add the packages**

Run (from `server/`):
```bash
dotnet add AtomicHabits.Tests/AtomicHabits.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite
dotnet add AtomicHabits.Tests/AtomicHabits.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add AtomicHabits.Tests/AtomicHabits.Tests.csproj package Moq
```
Expected: three packages added.

- [ ] **Step 2: Make the test project target the web SDK for Mvc.Testing**

`Microsoft.AspNetCore.Mvc.Testing` requires the test project to reference the ASP.NET Core shared framework. Open `server/AtomicHabits.Tests/AtomicHabits.Tests.csproj` and add this `ItemGroup` (inside `<Project>`):
```xml
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
```

- [ ] **Step 3: Verify it builds**

Run (from `server/`): `dotnet build AtomicHabits.Tests/AtomicHabits.Tests.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add server/AtomicHabits.Tests/AtomicHabits.Tests.csproj
git commit -m "test: add Sqlite, Mvc.Testing, and Moq test packages"
```

---

## Task 2: SQLite in-memory context factory (for transaction-sensitive tests)

**Files:**
- Create: `server/AtomicHabits.Tests/SqliteDbContextFactory.cs`

- [ ] **Step 1: Create the factory**

EF's InMemory provider ignores transactions (so it can't catch the registration-commit bug). SQLite in-memory is a real relational engine that honors `BeginTransaction`/`Commit`/`Rollback`. The connection must stay open for the lifetime of the context (closing it drops the in-memory DB).

Create `server/AtomicHabits.Tests/SqliteDbContextFactory.cs`:
```csharp
using AtomicHabits.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Tests;

/// <summary>
/// Builds an AppDbContext backed by a real SQLite in-memory database. Unlike the EF
/// InMemory provider, SQLite honors transactions — required for tests that assert
/// commit/rollback behavior (e.g. registration). Hold the returned handle for the test's
/// lifetime and Dispose it; closing the connection drops the database.
/// </summary>
public sealed class SqliteTestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public AppDbContext Context { get; }

    public SqliteTestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new AppDbContext(options);
        Context.Database.EnsureCreated();
    }

    // A fresh context over the SAME connection/database — use to assert persistence
    // across a unit boundary without the first context's change-tracker masking a rollback.
    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
```

- [ ] **Step 2: Verify it builds**

Run (from `server/`): `dotnet build AtomicHabits.Tests/AtomicHabits.Tests.csproj`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits.Tests/SqliteDbContextFactory.cs
git commit -m "test: add SQLite in-memory context factory for transaction tests"
```

---

## Task 3: StreakCalculator unit tests (pure logic)

**Files:**
- Create: `server/AtomicHabits.Tests/Services/StreakCalculatorTests.cs`

`StreakCalculator.Compute(IEnumerable<DateTime> completedDates, DateTime? today = null)` returns `record StreakResult(int CurrentStreak, int LongestStreak)`. `today` is injectable, so tests are deterministic (no clock dependency).

- [ ] **Step 1: Write the tests**

Create `server/AtomicHabits.Tests/Services/StreakCalculatorTests.cs`:
```csharp
using System;
using AtomicHabits.Utils;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class StreakCalculatorTests
{
    private static readonly DateTime Today = new(2026, 6, 7); // a Sunday — guards the ISO-week edge

    [Fact]
    public void Empty_input_yields_zero_zero()
    {
        var r = StreakCalculator.Compute(Array.Empty<DateTime>(), Today);
        r.CurrentStreak.Should().Be(0);
        r.LongestStreak.Should().Be(0);
    }

    [Fact]
    public void Consecutive_days_ending_today_count_as_current_streak()
    {
        var dates = new[] { Today.AddDays(-2), Today.AddDays(-1), Today };
        var r = StreakCalculator.Compute(dates, Today);
        r.CurrentStreak.Should().Be(3);
        r.LongestStreak.Should().Be(3);
    }

    [Fact]
    public void Streak_completed_yesterday_is_still_current()
    {
        // daysSinceLast == 1 keeps the streak alive.
        var dates = new[] { Today.AddDays(-2), Today.AddDays(-1) };
        var r = StreakCalculator.Compute(dates, Today);
        r.CurrentStreak.Should().Be(2);
    }

    [Fact]
    public void Gap_of_two_days_breaks_the_current_streak()
    {
        var dates = new[] { Today.AddDays(-3), Today.AddDays(-2) }; // last completion 2 days ago
        var r = StreakCalculator.Compute(dates, Today);
        r.CurrentStreak.Should().Be(0);
        r.LongestStreak.Should().Be(2);
    }

    [Fact]
    public void Longest_streak_is_found_even_when_not_current()
    {
        var dates = new[]
        {
            Today.AddDays(-10), Today.AddDays(-9), Today.AddDays(-8), Today.AddDays(-7), // run of 4
            Today.AddDays(-1), Today                                                     // current run of 2
        };
        var r = StreakCalculator.Compute(dates, Today);
        r.LongestStreak.Should().Be(4);
        r.CurrentStreak.Should().Be(2);
    }

    [Fact]
    public void Duplicate_dates_are_collapsed()
    {
        var dates = new[] { Today, Today, Today.AddDays(-1) };
        var r = StreakCalculator.Compute(dates, Today);
        r.CurrentStreak.Should().Be(2);
        r.LongestStreak.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run the tests**

Run (from `server/`): `dotnet test AtomicHabits.Tests --filter StreakCalculatorTests`
Expected: PASS (6 tests). These assert existing behavior — they should pass without touching `StreakCalculator`.

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits.Tests/Services/StreakCalculatorTests.cs
git commit -m "test: cover StreakCalculator current/longest streak logic"
```

---

## Task 4: TwoFactorService recovery-code lifecycle tests (InMemory)

**Files:**
- Create: `server/AtomicHabits.Tests/Services/TwoFactorServiceTests.cs`

`TwoFactorService(AppDbContext db, ILogger<TwoFactorService> logger)`. Use `TestDbContextFactory.Create()` and `NullLogger`. These lock the recovery-code behaviors verified manually this session.

> Note on the atomic single-use: `VerifyRecoveryCodeAsync` uses `ExecuteUpdateAsync`. EF's **InMemory provider supports `ExecuteUpdateAsync`** (EF Core 7+), so these tests run on InMemory. (The concurrency/race aspect isn't unit-tested here — single-threaded use-then-reuse is sufficient to prove single-use semantics.)

- [ ] **Step 1: Write the tests**

Create `server/AtomicHabits.Tests/Services/TwoFactorServiceTests.cs`:
```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AtomicHabits.Models;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class TwoFactorServiceTests
{
    private const int UserId = 42;

    private static TwoFactorService NewService(out AtomicHabits.Data.AppDbContext db)
    {
        db = TestDbContextFactory.Create();
        db.Users.Add(new User { Id = UserId, Username = "u", Email = "u@test.local", PasswordHash = "x" });
        db.SaveChanges();
        return new TwoFactorService(db, NullLogger<TwoFactorService>.Instance);
    }

    [Fact]
    public async Task GenerateRecoveryCodesAsync_returns_10_unique_codes_and_stores_hashes()
    {
        var svc = NewService(out var db);

        var codes = await svc.GenerateRecoveryCodesAsync(UserId, CancellationToken.None);

        codes.Should().HaveCount(10);
        codes.Should().OnlyHaveUniqueItems();
        codes.Should().AllSatisfy(c => c.Should().MatchRegex(@"^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$"));
        // stored as hashes, never plaintext
        db.TwoFactorRecoveryCodes.Should().HaveCount(10);
        db.TwoFactorRecoveryCodes.Select(c => c.CodeHash)
            .Should().NotContain(codes.First());
    }

    [Fact]
    public async Task VerifyRecoveryCodeAsync_accepts_a_valid_code_once_then_rejects_reuse()
    {
        var svc = NewService(out _);
        var codes = await svc.GenerateRecoveryCodesAsync(UserId, CancellationToken.None);
        var code = codes.First();

        (await svc.VerifyRecoveryCodeAsync(UserId, code, CancellationToken.None)).Should().BeTrue();
        (await svc.VerifyRecoveryCodeAsync(UserId, code, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyRecoveryCodeAsync_normalizes_dashes_and_case()
    {
        var svc = NewService(out _);
        var codes = await svc.GenerateRecoveryCodesAsync(UserId, CancellationToken.None);
        var dashless = codes.First().Replace("-", "").ToLowerInvariant();

        (await svc.VerifyRecoveryCodeAsync(UserId, dashless, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task CountRemainingRecoveryCodesAsync_reflects_consumption()
    {
        var svc = NewService(out _);
        var codes = await svc.GenerateRecoveryCodesAsync(UserId, CancellationToken.None);

        (await svc.CountRemainingRecoveryCodesAsync(UserId, CancellationToken.None)).Should().Be(10);
        await svc.VerifyRecoveryCodeAsync(UserId, codes.First(), CancellationToken.None);
        (await svc.CountRemainingRecoveryCodesAsync(UserId, CancellationToken.None)).Should().Be(9);
    }

    [Fact]
    public async Task GenerateRecoveryCodesAsync_replaces_the_previous_set()
    {
        var svc = NewService(out _);
        var first = await svc.GenerateRecoveryCodesAsync(UserId, CancellationToken.None);
        await svc.GenerateRecoveryCodesAsync(UserId, CancellationToken.None); // regenerate

        // an unused code from the FIRST set must no longer verify
        (await svc.VerifyRecoveryCodeAsync(UserId, first.First(), CancellationToken.None)).Should().BeFalse();
        (await svc.CountRemainingRecoveryCodesAsync(UserId, CancellationToken.None)).Should().Be(10);
    }
}
```

- [ ] **Step 2: Run the tests**

Run (from `server/`): `dotnet test AtomicHabits.Tests --filter TwoFactorServiceTests`
Expected: PASS (5 tests).

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits.Tests/Services/TwoFactorServiceTests.cs
git commit -m "test: cover TwoFactorService recovery-code lifecycle"
```

---

## Task 5: TokenService pending-token regression test (bug f561677)

**Files:**
- Create: `server/AtomicHabits.Tests/Services/TokenServiceTwoFactorTests.cs`

This is the **regression test for the 2FA-pending-token bug**: `GenerateTwoFactorPendingToken` then `ValidateTwoFactorPendingToken` must round-trip the user id. Before the fix, validation returned null because `sub` was remapped to `ClaimTypes.NameIdentifier`.

`TokenService` constructor takes `IOptions<JwtOptions>` + `ILogger<TokenService>`. The token validation requires a non-empty `Secret`, `Issuer`, `Audience`.

- [ ] **Step 1: Write the tests**

Create `server/AtomicHabits.Tests/Services/TokenServiceTwoFactorTests.cs`:
```csharp
using System;
using AtomicHabits.Config;
using AtomicHabits.Models;
using AtomicHabits.Services;
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
        var token = svc.GenerateTwoFactorPendingToken(TestUser, TimeSpan.FromMinutes(-1)); // already expired

        svc.ValidateTwoFactorPendingToken(token).Should().BeNull();
    }

    [Fact]
    public void Garbage_token_is_rejected()
    {
        var svc = NewService();
        svc.ValidateTwoFactorPendingToken("not-a-jwt").Should().BeNull();
    }
}
```

> If `TokenService`'s constructor signature differs (e.g. it takes additional dependencies),
> adapt `NewService()` accordingly — read `server/AtomicHabits/Services/TokenService.cs`
> constructor first. Likewise confirm `JwtOptions` property names match.

- [ ] **Step 2: Run the tests**

Run (from `server/`): `dotnet test AtomicHabits.Tests --filter TokenServiceTwoFactorTests`
Expected: PASS (3 tests). The first one is the regression guard — if someone reverts `f561677`, it fails.

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits.Tests/Services/TokenServiceTwoFactorTests.cs
git commit -m "test: regression-guard 2FA pending-token validation (sub claim)"
```

---

## Task 6: AuthService registration regression test (bug bbaffa0, SQLite)

**Files:**
- Create: `server/AtomicHabits.Tests/Services/AuthServiceRegistrationTests.cs`

**Regression test for the registration-commit bug.** Must use the **SQLite** factory (Task 2): the EF InMemory provider ignores `BeginTransactionAsync`, so the missing-`CommitAsync` would *appear* to persist there and the bug would slip through. SQLite honors the transaction, so a missing commit = no row, which is what we assert against.

`AuthService` constructor (8 args): `AppDbContext, ITokenService, IUserRepositories, IEmailSender, ILogger<AuthService>, ITwoFactorService, IOptions<JwtOptions>, IOptions<AppOptions>`. We use a real `TokenService` and real `TwoFactorService`, mock `IUserRepositories`/`IEmailSender` (not exercised by register), and real options.

- [ ] **Step 1: Write the tests**

Create `server/AtomicHabits.Tests/Services/AuthServiceRegistrationTests.cs`:
```csharp
using System.Linq;
using System.Threading.Tasks;
using AtomicHabits.Config;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
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
        var tokenSvc = new TokenService(jwt, NullLogger<TokenService>.Instance);
        var twoFactor = new TwoFactorService(db, NullLogger<TwoFactorService>.Instance);
        var userRepo = new Mock<IUserRepositories>();
        var emailSender = new Mock<IEmailSender>();

        return new AuthService(db, tokenSvc, userRepo.Object, emailSender.Object,
            NullLogger<AuthService>.Instance, twoFactor, jwt, app);
    }

    [Fact]
    public async Task RegisterAsync_persists_the_user()
    {
        using var sqlite = new SqliteTestDb();
        // seed the "User" role so the role-assignment path runs
        sqlite.Context.Roles.Add(new Role { Name = "User" });
        await sqlite.Context.SaveChangesAsync();

        var svc = NewService(sqlite.Context);
        var dto = new RegisterDto { Username = "alice", Email = "alice@test.local", Password = "Secret@123", Role = "User" };

        var res = await svc.RegisterAsync(dto, ctx: null);

        res.IsSuccess.Should().BeTrue();
        // assert via a FRESH context over the same connection — proves it actually COMMITTED,
        // not just that it sits in the first context's change tracker. This is the regression guard.
        using var verify = sqlite.NewContext();
        verify.Users.Should().ContainSingle(u => u.Email == "alice@test.local");
    }

    [Fact]
    public async Task RegisterAsync_rejects_duplicate_email()
    {
        using var sqlite = new SqliteTestDb();
        sqlite.Context.Users.Add(new User { Username = "bob", Email = "dupe@test.local", PasswordHash = "x" });
        await sqlite.Context.SaveChangesAsync();

        var svc = NewService(sqlite.Context);
        var dto = new RegisterDto { Username = "bob2", Email = "dupe@test.local", Password = "Secret@123", Role = "User" };

        var res = await svc.RegisterAsync(dto, ctx: null);

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }
}
```

> Confirm `AppOptions` property names (`WebBaseUrl`/`ResetPasswordPath`) and the `AuthService`
> ctor arg order against the source before running; adapt if they differ. `IssueTokensAsync`
> writes a refresh token and reads `HttpContext` for the cookie — passing `ctx: null` must be
> tolerated by the code (it is: the cookie helpers guard on a null context). If a NRE surfaces
> from a null `HttpContext`, that's a real finding — report it rather than masking it.

- [ ] **Step 2: Run the tests**

Run (from `server/`): `dotnet test AtomicHabits.Tests --filter AuthServiceRegistrationTests`
Expected: PASS (2 tests). `RegisterAsync_persists_the_user` is the regression guard for `bbaffa0` — revert the `CommitAsync` and it fails (0 users in the fresh context).

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits.Tests/Services/AuthServiceRegistrationTests.cs
git commit -m "test: regression-guard registration persistence (transaction commit)"
```

---

## Task 7: API integration tests — auth/2FA flow over HTTP

**Files:**
- Modify: `server/AtomicHabits/Program.cs` (expose `Program` to tests)
- Create: `server/AtomicHabits.Tests/Integration/ApiFactory.cs`
- Create: `server/AtomicHabits.Tests/Integration/AuthFlowIntegrationTests.cs`

- [ ] **Step 1: Expose Program to the test assembly**

At the very end of `server/AtomicHabits/Program.cs`, after `app.Run();`, add:
```csharp

// Exposed so WebApplicationFactory<Program> can boot the app in integration tests.
public partial class Program { }
```
No behavioral change — top-level statements already generate a `Program`; this just makes it `public`.

- [ ] **Step 2: Build a WebApplicationFactory that swaps the DB to SQLite and sets JWT config**

Create `server/AtomicHabits.Tests/Integration/ApiFactory.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AtomicHabits.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AtomicHabits.Tests.Integration;

/// <summary>
/// Boots the real API in-process with the database swapped to a per-factory SQLite
/// in-memory connection, and JWT config supplied so auth works without env vars.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public ApiFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "integration-signing-key-at-least-32-bytes-0123456789",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the app's SQL Server DbContext registration and re-add SQLite.
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));

            // Create the schema once the provider is swapped.
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
```

> The env override `JWT_SECRET`/`JWT_ISSUER`/`JWT_AUDIENCE` in `Program.cs` `PostConfigure`
> only applies when those env vars are set; in tests they are not, so the `Jwt:*` config
> above is used. If the integration host fails to start complaining about JWT, confirm the
> config keys match what `Program.cs` reads (`Jwt:Secret`/`Jwt:Issuer`/`Jwt:Audience`).
> The startup pending-migrations check calls `GetPendingMigrationsAsync()`; on a freshly
> `EnsureCreated` SQLite DB this returns empty and is harmless. The `ReminderDispatcherService`
> background loop starts but has nothing due — harmless in-process.

- [ ] **Step 3: Write the auth-flow integration test**

Create `server/AtomicHabits.Tests/Integration/AuthFlowIntegrationTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Integration;

public class AuthFlowIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AuthFlowIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_then_login_succeeds_over_http()
    {
        var client = _factory.CreateClient();

        var register = await client.PostAsJsonAsync("/api/Auth/register", new
        {
            username = "intuser",
            email = "intuser@test.local",
            password = "Secret@123",
            role = "User"
        });
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            email = "intuser@test.local",
            password = "Secret@123"
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await login.Content.ReadFromJsonAsync<LoginEnvelope>();
        body!.IsSuccess.Should().BeTrue();
        body.Result!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/Auth/register", new
        {
            username = "intuser2", email = "intuser2@test.local", password = "Secret@123", role = "User"
        });

        var login = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            email = "intuser2@test.local", password = "WRONG"
        });

        // service returns a non-success envelope; assert the body, not just the status
        var body = await login.Content.ReadFromJsonAsync<LoginEnvelope>();
        body!.IsSuccess.Should().BeFalse();
    }

    // Minimal envelope shapes for deserialization (camelCase via default options).
    private class LoginEnvelope
    {
        public bool IsSuccess { get; set; }
        public LoginResult? Result { get; set; }
    }
    private class LoginResult
    {
        public string? AccessToken { get; set; }
    }
}
```

> This proves the register→login HTTP path end-to-end (the exact flow that exposed the
> registration bug). A full register→enable-2FA→verify-with-recovery HTTP test would need a
> server-side TOTP code; that's deferred — the recovery-code *logic* is already covered at the
> service layer (Task 4) and the pending-token validation at Task 5. Note this omission rather
> than silently skipping it.

- [ ] **Step 4: Run the integration tests**

Run (from `server/`): `dotnet test AtomicHabits.Tests --filter AuthFlowIntegrationTests`
Expected: PASS (2 tests). If the host fails to boot, read the exception — likely a JWT config key or DbContext-registration mismatch; fix per the notes above. Do NOT weaken assertions to make it pass.

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Program.cs server/AtomicHabits.Tests/Integration/ApiFactory.cs server/AtomicHabits.Tests/Integration/AuthFlowIntegrationTests.cs
git commit -m "test: HTTP integration tests for register/login via WebApplicationFactory"
```

---

## Task 8: HabitService summary math unit tests (InMemory)

**Files:**
- Create: `server/AtomicHabits.Tests/Services/HabitServiceSummaryTests.cs`

`HabitService(IHabitRepositories repo, AppDbContext db, ILogger<HabitService> log, IAuthService authService)`. The summary endpoint computes per-`GoalFrequency` expected sessions (the #17 fix). These tests guard that math.

- [ ] **Step 1: Read the method first**

Open `server/AtomicHabits/Services/HabitService.cs` and read `HabitSummary` (and its `ExpectedSessions` helper). Confirm the exact public method name/signature used by the controller (e.g. `HabitSummary(int userId, ...)`), what it returns inside `ApiResponse.Result`, and which repository methods it calls (so the test can seed via `AppDbContext` or stub `IHabitRepositories`). **Adapt the test below to the real signature** — the shape here is the intent, not a guess to paste blindly.

- [ ] **Step 2: Write the tests**

Create `server/AtomicHabits.Tests/Services/HabitServiceSummaryTests.cs`:
```csharp
using System.Threading;
using System.Threading.Tasks;
using AtomicHabits.Models;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class HabitServiceSummaryTests
{
    // NOTE: align constructor + method names with HabitService.cs (read it in Step 1).
    private static HabitService NewService(AtomicHabits.Data.AppDbContext db)
    {
        var repo = new Mock<IHabitRepositories>();
        var auth = new Mock<IAuthService>();
        // If HabitSummary resolves the user via IAuthService.GetCurrentUserFromJwt, set it up:
        // auth.Setup(a => a.GetCurrentUserFromJwt(It.IsAny<string>())).Returns(new User { Id = 1 });
        return new HabitService(repo.Object, db, NullLogger<HabitService>.Instance, auth.Object);
    }

    [Fact]
    public async Task Summary_returns_success_with_zero_habits()
    {
        using var db = TestDbContextFactory.Create();
        db.Users.Add(new User { Id = 1, Username = "u", Email = "u@test.local", PasswordHash = "x" });
        await db.SaveChangesAsync();

        var svc = NewService(db);

        // Replace with the real call signature confirmed in Step 1, e.g.:
        // var res = await svc.HabitSummary(1, CancellationToken.None);
        // res.IsSuccess.Should().BeTrue();
        // Placeholder assertion removed — fill in per the real method.
        true.Should().BeTrue();
    }
}
```

> This task is intentionally lighter: the summary math has several private helpers and
> its public entry point must be read before asserting. Step 1 is mandatory. Write at least
> two real assertions: (a) zero-habits → success with a zeroed/empty summary, and (b) a daily
> habit completed today contributes to "today" rate while a weekly habit is weighted by its
> `GoalFrequency`. If the method is hard to drive without heavy stubbing, report it as a
> testability finding (candidate to extract the pure `ExpectedSessions` math into a static
> helper like `StreakCalculator`, which would make it trivially unit-testable).

- [ ] **Step 3: Run the tests**

Run (from `server/`): `dotnet test AtomicHabits.Tests --filter HabitServiceSummaryTests`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add server/AtomicHabits.Tests/Services/HabitServiceSummaryTests.cs
git commit -m "test: cover HabitService summary math (per-frequency expectations)"
```

---

## Task 9: Ownership / IDOR integration tests

**Files:**
- Create: `server/AtomicHabits.Tests/Integration/HabitOwnershipTests.cs`

Proves the JWT-derived-userId enforcement: user B cannot read user A's habits even when supplying A's id in the route (the #1/#2 IDOR fixes). Uses the `ApiFactory` from Task 7.

- [ ] **Step 1: Write the test**

Create `server/AtomicHabits.Tests/Integration/HabitOwnershipTests.cs`:
```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Integration;

public class HabitOwnershipTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public HabitOwnershipTests(ApiFactory factory) => _factory = factory;

    private async Task<(string token, int userId)> RegisterAndLogin(System.Net.Http.HttpClient client, string email)
    {
        await client.PostAsJsonAsync("/api/Auth/register", new
        {
            username = email.Split('@')[0], email, password = "Secret@123", role = "User"
        });
        var login = await client.PostAsJsonAsync("/api/Auth/login", new { email, password = "Secret@123" });
        var env = await login.Content.ReadFromJsonAsync<Env>();
        return (env!.Result!.AccessToken!, 0);
    }

    [Fact]
    public async Task User_B_cannot_read_user_A_habits_via_route_id()
    {
        var client = _factory.CreateClient();
        var (tokenA, _) = await RegisterAndLogin(client, "owner-a@test.local");
        var (tokenB, _) = await RegisterAndLogin(client, "intruder-b@test.local");

        // A creates a habit
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var create = await client.PostAsJsonAsync("/api/Habit/post-habit", new
        {
            name = "A's private habit", frequency = "Daily", color = "#fff",
            goalValue = 1, goalUnit = "times", goalFrequency = "per day"
        });
        create.EnsureSuccessStatusCode();

        // B asks for habits — even if B guesses A's userId in the route, the API must scope to B (JWT sub).
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var asB = await client.GetAsync("/api/Habit/get-habits/1"); // route userId ignored server-side
        asB.EnsureSuccessStatusCode();
        var body = await asB.Content.ReadAsStringAsync();

        body.Should().NotContain("A's private habit"); // B never sees A's data
    }

    private class Env { public Result? Result { get; set; } }
    private class Result { public string? AccessToken { get; set; } }
}
```

> Confirm the create-habit DTO field names against `HabitDTO`/the controller before running
> (read `Models/DTO/HabitDTO.cs`); adapt the POST body to match. The assertion is the security
> property: B's response must not contain A's habit regardless of the route id.

- [ ] **Step 2: Run the test**

Run (from `server/`): `dotnet test AtomicHabits.Tests --filter HabitOwnershipTests`
Expected: PASS — proves owner-scoping holds.

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits.Tests/Integration/HabitOwnershipTests.cs
git commit -m "test: IDOR guard — users cannot read others' habits via route id"
```

---

## Task 10: Full suite green + CI-ready note

**Files:**
- Modify: `ARCHITECTURE_AND_ROADMAP.md` (mark "Automated tests" progress)

- [ ] **Step 1: Run the whole suite**

Run (from `server/`): `dotnet test AtomicHabits.sln`
Expected: all tests PASS across both projects (Performance OS model tests + this suite). Capture the summary line (Passed/Failed/Skipped).

- [ ] **Step 2: Note coverage + gaps in the roadmap**

In `ARCHITECTURE_AND_ROADMAP.md`, under the Platform/DevOps "Automated tests" item, record what now has coverage (StreakCalculator, TwoFactorService recovery codes, 2FA pending-token regression, registration regression, auth HTTP integration, habit IDOR) and what's still uncovered (HabitTrackingService duplicate-day/streak-upsert, DashboardService heatmap buckets, reminder dispatcher, frontend — no JS test runner yet). Add a one-line pointer that two of these tests are **regression guards** for the bugs fixed in commits `f561677` and `bbaffa0`.

> Stage ONLY the roadmap file with `git add ARCHITECTURE_AND_ROADMAP.md` — the working tree
> contains unrelated in-progress "Momentum"/landing edits to this same file; use `git add -p`
> to stage only your hunk if needed (as was done previously).

- [ ] **Step 3: Commit**

```bash
git add ARCHITECTURE_AND_ROADMAP.md
git commit -m "docs: record backend test coverage and remaining gaps"
```

---

## Self-Review (plan author)

- **Coverage of the four requested areas:** auth/2FA regression (T4 recovery codes, T5 pending-token, T6 registration), service-layer units (T3 StreakCalculator, T4 TwoFactorService, T8 HabitService), API integration (T7 register/login HTTP), ownership/IDOR (T9). ✓
- **Both session bugs get regression guards:** T5 (`f561677`), T6 (`bbaffa0`). ✓
- **Key constraint handled:** registration test uses SQLite (T2), not InMemory, because InMemory ignores transactions and would mask the commit bug. Stated explicitly. ✓
- **Dependency on Performance OS Plan 1 Task 1** (the test project itself) is called out up front with a presence check and a "run that first / don't duplicate" instruction. ✓
- **Placeholders / honesty:** T8 and T9 require reading the real method/DTO signatures before asserting (flagged as mandatory Step 1s) rather than pasting guessed signatures; T8 carries a deliberately minimal body with instructions to fill in real assertions and a testability-finding escape hatch. The skipped full-HTTP-2FA-with-TOTP path is noted, not hidden. These are honest scope edges, not silent gaps.
- **Working-tree hygiene:** every commit stages explicit paths; T10 reuses the `git add -p` approach for the shared roadmap file. ✓

## Notes / deferred

- **No frontend test runner** (Vitest + React Testing Library) — separate plan; this one is backend-only.
- **Concurrency/race test** for `VerifyRecoveryCodeAsync`'s atomic `ExecuteUpdateAsync` is not included (hard to make deterministic on SQLite in-memory); the single-use semantics are covered single-threaded.
- **HabitTrackingService** (duplicate-day rejection, streak upsert) and **DashboardService** (heatmap intensity buckets) are good next units — add in a follow-up once the harness patterns here are established.
