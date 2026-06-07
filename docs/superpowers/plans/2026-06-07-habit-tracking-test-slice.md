# HabitTrackingService Test Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Cover the daily-write path — duplicate-day rejection, the streak upsert, and the distribution bucketing — and in doing so pin down a suspected streak-reset bug in `StreakRepositories.UpsertStreakAfterTracking`.

**Architecture:** Adds tests to the existing `server/AtomicHabits.Tests` project. Streak/tracking repos use transactions and direct EF — so these tests use the **SQLite** factory (`SqliteTestDb`), not the EF InMemory provider. Tests target the real repository/service classes over a real (SQLite) `AppDbContext` with seeded data; `IAuthService` is mocked where a method resolves the user from a token.

**Tech Stack:** xUnit, FluentAssertions, `Microsoft.EntityFrameworkCore.Sqlite`, Moq, `NullLogger`.

**Suspected bug this slice should expose (verify, then fix):**
`UpsertStreakAfterTracking` finds the "last completed tracking" by querying `HabitTrackings` ordered by date desc. But both callers (`PostHabitProgress`, `PostDailyHabit`) call `CreateTracking` — which **`SaveChanges`es the new row** — *before* calling the upsert. So the "last completed" row is **today's own row**, and the check `lastTracking.TrackingDate == date.AddDays(-1)` compares today to yesterday → false → `CurrentStreak` resets to 1 every day. Net: the **persisted** `Streak.CurrentStreak`/`BestStreak` never exceed 1, even though `GetHabitStats` (which uses `StreakCalculator` over the date list) reports the correct streak. Task 1 asserts the CORRECT behavior so it fails first, proving the bug; Task 2 fixes it.

---

## Reference (confirmed signatures)

- `StreakRepositories(AppDbContext db, ILogger<StreakRepositories> logger)`; `Task<Streak> UpsertStreakAfterTracking(HabitTrackingDTO dto)`.
- `HabitTrackingRepositories(AppDbContext db, ILogger<…> logger)`; `Task<HabitTracking> CreateTracking(HabitTrackingDTO)`, `Task<HabitTracking?> GetExistingDate(HabitTrackingDTO)`, `int[] GetWeeklyDistribution(WeeklyDistributionDTO, ct)`, `GetMonthlyDistribution(MonthlyDistributionDTO, ct)`.
- `HabitTrackingService(IAuthService, IHabitTrackingRepositories, IStreakRepositories, AppDbContext, IHabitRepositories)`. `PostHabitProgress(HabitTrackingDTO, ct)`; `PostDailyHabit(int habitId, int minutes, ct, string token)`.
- `HabitTrackingDTO { int HabitId; int UserId; DateTime TrackingDate; bool IsCompleted; string Notes; int TimeSpentMinutes; … }`.
- `Streak { int CurrentStreak; int BestStreak; DateTime? CurrentStreakStartDate; … float CompletionRate; }`.
- `WeeklyDistributionDTO { int HabitId; int Year; int Month; }` (verify field names in `Models/DTO/HabitDistributionDTO.cs`).
- Namespaces: repos in `AtomicHabits.Repositories`; `HabitTrackingService` in `AtomicHabits.Services`; `IAuthService` in `AtomicHabits.Services`; `UserInfoDto` (returned by `GetCurrentUserFromJwt`) in `AtomicHabits.Models.DTO` with a `UserId` string. **Confirm each before asserting.**

---

## Task 1: StreakRepositories upsert tests (exposes the reset bug)

**Files:**
- Create: `server/AtomicHabits.Tests/Repositories/StreakRepositoriesTests.cs`

- [ ] **Step 1: Read first**
Read `server/AtomicHabits/Repositories/StreakRepositories.cs` and `Models/Streak.cs`. Confirm the `Streak` field names used below.

- [ ] **Step 2: Write the tests**

Create `server/AtomicHabits.Tests/Repositories/StreakRepositoriesTests.cs`:
```csharp
using System;
using System.Threading.Tasks;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Repositories;

public class StreakRepositoriesTests
{
    // Mirrors how the SERVICE uses the repo: a tracking row for the day is created
    // (and saved) BEFORE the streak upsert runs. We replicate that ordering so the
    // test reflects production reality.
    private static async Task SeedTrackingThenUpsert(
        SqliteTestDb sqlite, int habitId, int userId, DateTime date, bool isCompleted)
    {
        sqlite.Context.HabitTrackings.Add(new HabitTracking
        {
            HabitId = habitId, UserId = userId, TrackingDate = date,
            IsCompleted = isCompleted, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await sqlite.Context.SaveChangesAsync();

        var repo = new StreakRepositories(sqlite.Context, NullLogger<StreakRepositories>.Instance);
        await repo.UpsertStreakAfterTracking(new HabitTrackingDTO
        {
            HabitId = habitId, UserId = userId, TrackingDate = date, IsCompleted = isCompleted
        });
    }

    [Fact]
    public async Task First_completion_creates_streak_of_one()
    {
        using var sqlite = new SqliteTestDb();
        await SeedTrackingThenUpsert(sqlite, habitId: 1, userId: 1, new DateTime(2026, 6, 1), isCompleted: true);

        using var verify = sqlite.NewContext();
        var streak = verify.Streaks.Single();
        streak.CurrentStreak.Should().Be(1);
        streak.BestStreak.Should().Be(1);
    }

    [Fact]
    public async Task Two_consecutive_completed_days_yield_current_streak_of_two()
    {
        using var sqlite = new SqliteTestDb();
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 1), true);
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 2), true);

        using var verify = sqlite.NewContext();
        var streak = verify.Streaks.Single();
        // CORRECT behavior: two consecutive completed days = a current streak of 2.
        streak.CurrentStreak.Should().Be(2);
        streak.BestStreak.Should().Be(2);
    }

    [Fact]
    public async Task A_missed_day_breaks_the_current_streak_but_keeps_best()
    {
        using var sqlite = new SqliteTestDb();
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 1), true);
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 2), true); // best reaches 2
        // skip 6/3, then complete 6/4 — non-consecutive, current resets to 1
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 4), true);

        using var verify = sqlite.NewContext();
        var streak = verify.Streaks.Single();
        streak.CurrentStreak.Should().Be(1);
        streak.BestStreak.Should().Be(2);
    }

    [Fact]
    public async Task Completion_rate_reflects_completed_over_total()
    {
        using var sqlite = new SqliteTestDb();
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 1), true);
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 2), false); // not completed

        using var verify = sqlite.NewContext();
        var streak = verify.Streaks.Single();
        streak.CompletionRate.Should().BeApproximately(0.5f, 0.001f); // 1 completed / 2 total
    }
}
```

- [ ] **Step 3: Run — EXPECT a failure on the consecutive-days test**
Run: `dotnet test server/AtomicHabits.Tests --filter StreakRepositoriesTests`
Expected: `First_completion…`, `…missed_day…` (current resets anyway so it may pass), and `Completion_rate…` likely PASS; **`Two_consecutive_completed_days_yield_current_streak_of_two` is EXPECTED TO FAIL** (actual CurrentStreak = 1, not 2) — this demonstrates the bug.

**This is the one task where a red test is the success condition.** Do NOT change the test to match the buggy behavior. Record the actual values observed (e.g. "CurrentStreak was 1, expected 2"). Report **DONE_WITH_CONCERNS** with the failing assertion's actual-vs-expected; the controller (you) will confirm the diagnosis and the fix lands in Task 2. Do NOT commit a red suite — leave the file uncommitted and report; the controller will sequence the fix+commit.

> If, surprisingly, `Two_consecutive…` PASSES, then the bug diagnosis is wrong — report that as a finding with the observed values; we'll re-evaluate before changing any production code.

---

## Task 2: Fix the streak-reset bug, then commit Task 1 + fix together

**Files:**
- Modify: `server/AtomicHabits/Repositories/StreakRepositories.cs`

> Dispatched only after Task 1 confirms the failure. The controller provides the confirmed diagnosis.

- [ ] **Step 1: Fix the "last completed tracking" lookup**
The query must find the most recent completed tracking **excluding the current day's row** (which `CreateTracking` already persisted). In `UpsertStreakAfterTracking`, change the `lastTracking` query to exclude `ht.TrackingDate.Value.Date == date`:
```csharp
                        var lastTracking = await _db.HabitTrackings
                            .Where(ht => ht.HabitId == dto.HabitId &&
                                         ht.UserId == dto.UserId &&
                                         ht.IsCompleted &&
                                         ht.TrackingDate!.Value.Date != date)   // exclude today's just-saved row
                            .OrderByDescending(ht => ht.TrackingDate)
                            .FirstOrDefaultAsync();
```
This makes "was the previous completion exactly yesterday?" compare against the prior day's row, not today's, so consecutive days increment correctly.

- [ ] **Step 2: Run — the whole class must now be green**
Run: `dotnet test server/AtomicHabits.Tests --filter StreakRepositoriesTests`
Expected: all 4 PASS (including `Two_consecutive…` now = 2).

- [ ] **Step 3: Confirm no regression to dependent tests**
Run: `dotnet test server/AtomicHabits.sln`
Expected: full suite green (prior 24 + the 4 new = 28).

- [ ] **Step 4: Commit the fix + the tests together**
```bash
git add server/AtomicHabits/Repositories/StreakRepositories.cs server/AtomicHabits.Tests/Repositories/StreakRepositoriesTests.cs
git commit -m "Fix persisted streak never advancing past 1; add StreakRepositories tests"
```
Stage ONLY those two paths.

---

## Task 3: PostHabitProgress / PostDailyHabit duplicate-day + ownership tests

**Files:**
- Create: `server/AtomicHabits.Tests/Services/HabitTrackingServiceTests.cs`

- [ ] **Step 1: Read first**
Read `HabitTrackingService.PostHabitProgress` and `PostDailyHabit`, and the repo methods they call (`GetHabitById`, `GetHabitbyUserHabitId`, `GetExistingDate`, `GetHabitTrackingDateByHabitId`, `CreateTracking`). Decide which collaborators to mock vs. use real over SQLite. Recommended: use a real `SqliteTestDb` `AppDbContext` + real `HabitTrackingRepositories` + real `StreakRepositories`, and mock only `IHabitRepositories` (to return a seeded habit) and `IAuthService` (for `PostDailyHabit`'s `GetCurrentUserFromJwt` → a `UserInfoDto` with the seeded user id). Confirm `UserInfoDto`'s shape (it has a string `UserId`).

- [ ] **Step 2: Write the tests** (adapt names/fields to what Step 1 confirms)
```csharp
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class HabitTrackingServiceTests
{
    private static HabitTrackingService Build(SqliteTestDb sqlite, Habit seededHabit, out Mock<IAuthService> auth)
    {
        // real repos over SQLite
        var trackingRepo = new HabitTrackingRepositories(sqlite.Context, NullLogger<HabitTrackingRepositories>.Instance);
        var streakRepo   = new StreakRepositories(sqlite.Context, NullLogger<StreakRepositories>.Instance);

        var habitRepo = new Mock<IHabitRepositories>();
        habitRepo.Setup(r => r.GetHabitById(seededHabit.Id)).ReturnsAsync(seededHabit);
        habitRepo.Setup(r => r.GetHabitbyUserHabitId(seededHabit.UserId, seededHabit.Id)).ReturnsAsync(seededHabit);

        auth = new Mock<IAuthService>();
        auth.Setup(a => a.GetCurrentUserFromJwt(It.IsAny<string>()))
            .ReturnsAsync(new UserInfoDto { UserId = seededHabit.UserId.ToString() });

        return new HabitTrackingService(auth.Object, trackingRepo, streakRepo, sqlite.Context, habitRepo.Object);
    }

    private static Habit SeedHabit(SqliteTestDb sqlite, int id = 1, int userId = 1)
    {
        var h = new Habit { Id = id, UserId = userId, Name = "Gym", Frequency = "Daily", GoalFrequency = "daily", GoalValue = 1, GoalUnit = "times" };
        sqlite.Context.Habits.Add(h);
        sqlite.Context.SaveChanges();
        return h;
    }

    [Fact]
    public async Task PostHabitProgress_creates_tracking_and_streak()
    {
        using var sqlite = new SqliteTestDb();
        var habit = SeedHabit(sqlite);
        var svc = Build(sqlite, habit, out _);

        var res = await svc.PostHabitProgress(new HabitTrackingDTO
        {
            HabitId = habit.Id, UserId = habit.UserId, TrackingDate = new DateTime(2026, 6, 1), IsCompleted = true
        }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        using var verify = sqlite.NewContext();
        verify.HabitTrackings.Should().ContainSingle();
        verify.Streaks.Should().ContainSingle();
    }

    [Fact]
    public async Task PostHabitProgress_rejects_duplicate_same_day()
    {
        using var sqlite = new SqliteTestDb();
        var habit = SeedHabit(sqlite);
        var svc = Build(sqlite, habit, out _);
        var dto = new HabitTrackingDTO { HabitId = habit.Id, UserId = habit.UserId, TrackingDate = new DateTime(2026, 6, 1), IsCompleted = true };

        (await svc.PostHabitProgress(dto, CancellationToken.None)).IsSuccess.Should().BeTrue();
        var second = await svc.PostHabitProgress(dto, CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var verify = sqlite.NewContext();
        verify.HabitTrackings.Should().ContainSingle(); // no duplicate row written
    }

    [Fact]
    public async Task PostHabitProgress_404_when_habit_missing()
    {
        using var sqlite = new SqliteTestDb();
        var habit = SeedHabit(sqlite);
        var svc = Build(sqlite, habit, out _);

        var res = await svc.PostHabitProgress(new HabitTrackingDTO
        {
            HabitId = 999, UserId = habit.UserId, TrackingDate = new DateTime(2026, 6, 1), IsCompleted = true
        }, CancellationToken.None);

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostDailyHabit_rejects_second_submission_same_day()
    {
        using var sqlite = new SqliteTestDb();
        var habit = SeedHabit(sqlite);
        var svc = Build(sqlite, habit, out _);

        var first = await svc.PostDailyHabit(habit.Id, minutes: 30, CancellationToken.None, token: "fake");
        first.IsSuccess.Should().BeTrue();

        var second = await svc.PostDailyHabit(habit.Id, minutes: 30, CancellationToken.None, token: "fake");
        second.IsSuccess.Should().BeFalse();
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
```

> NOTE: `PostDailyHabit` uses `DateTime.UtcNow.Date` internally for "today" and the duplicate check, so the first call writes today's row and the second is rejected — deterministic regardless of run date. `PostHabitProgress` takes the date from the DTO, so use a fixed date there.
> If `GetHabitById`/`GetHabitbyUserHabitId` aren't on `IHabitRepositories` under those exact names, fix the mock setups to the real method names found in Step 1.

- [ ] **Step 3: Run**
Run: `dotnet test server/AtomicHabits.Tests --filter HabitTrackingServiceTests`
Expected: 4 PASS. (These assert behavior that already works — duplicate rejection + create — so they should pass on the post-Task-2 code. If `PostHabitProgress_creates_tracking_and_streak` interacts with the streak fix, that's fine; it only asserts a streak row exists.)

- [ ] **Step 4: Commit (stage ONLY this file)**
```bash
git add server/AtomicHabits.Tests/Services/HabitTrackingServiceTests.cs
git commit -m "test: cover HabitTracking duplicate-day rejection and create path"
```

---

## Task 4: Distribution bucketing tests

**Files:**
- Create: `server/AtomicHabits.Tests/Repositories/DistributionTests.cs`

- [ ] **Step 1: Read first**
Read `GetWeeklyDistribution` and `GetMonthlyDistribution` in `HabitTrackingRepositories.cs` and the DTOs in `Models/DTO/HabitDistributionDTO.cs`. Confirm `WeeklyDistributionDTO`/`MonthlyDistributionDTO` field names (HabitId, Year, Month) and that weekly buckets are day≤7→[0], ≤14→[1], ≤21→[2], else→[3], and monthly is index = month-1.

- [ ] **Step 2: Write the tests** (SQLite-backed; seed tracking rows on specific dates)
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Repositories;

public class DistributionTests
{
    private static HabitTrackingRepositories Repo(SqliteTestDb s) =>
        new(s.Context, NullLogger<HabitTrackingRepositories>.Instance);

    private static void Seed(SqliteTestDb s, int habitId, DateTime date)
    {
        s.Context.HabitTrackings.Add(new HabitTracking
        {
            HabitId = habitId, UserId = 1, TrackingDate = date, IsCompleted = true,
            CreatedAt = date, UpdatedAt = date
        });
        s.Context.SaveChanges();
    }

    [Fact]
    public async Task Weekly_distribution_buckets_by_day_of_month()
    {
        using var s = new SqliteTestDb();
        Seed(s, 1, new DateTime(2026, 6, 3));   // day 3  → bucket 0
        Seed(s, 1, new DateTime(2026, 6, 10));  // day 10 → bucket 1
        Seed(s, 1, new DateTime(2026, 6, 25));  // day 25 → bucket 3

        var result = await Repo(s).GetWeeklyDistribution(
            new WeeklyDistributionDTO { HabitId = 1, Year = 2026, Month = 6 }, CancellationToken.None);

        result.Should().HaveCount(4);
        result[0].Should().Be(1);
        result[1].Should().Be(1);
        result[2].Should().Be(0);
        result[3].Should().Be(1);
    }

    [Fact]
    public async Task Monthly_distribution_indexes_by_month_minus_one()
    {
        using var s = new SqliteTestDb();
        Seed(s, 1, new DateTime(2026, 1, 15)); // January → index 0
        Seed(s, 1, new DateTime(2026, 6, 15)); // June    → index 5
        Seed(s, 1, new DateTime(2026, 6, 20)); // June    → index 5 (count 2)

        var result = await Repo(s).GetMonthlyDistribution(
            new MonthlyDistributionDTO { HabitId = 1, Year = 2026 }, CancellationToken.None);

        result.Should().HaveCount(12);
        result[0].Should().Be(1);
        result[5].Should().Be(2);
        result[11].Should().Be(0);
    }
}
```
> Adapt DTO construction if `MonthlyDistributionDTO` lacks a `Month` field (monthly only needs Year). If SQLite can't translate the weekly `GroupBy` ternary (it's a complex key), the query may throw — if so, report it as a DONE_WITH_CONCERNS finding (the bucketing may only work on SQL Server); we'd then either client-evaluate in a test or note the provider limitation. Try first.

- [ ] **Step 3: Run**
Run: `dotnet test server/AtomicHabits.Tests --filter DistributionTests`
Expected: 2 PASS. If the weekly `GroupBy` ternary doesn't translate on SQLite, report it (real finding — the production query might be SQL-Server-specific).

- [ ] **Step 4: Commit (stage ONLY this file)**
```bash
git add server/AtomicHabits.Tests/Repositories/DistributionTests.cs
git commit -m "test: cover weekly/monthly distribution bucketing"
```

---

## Task 5: Full suite green + roadmap update

- [ ] **Step 1:** `dotnet test server/AtomicHabits.sln` — expect all green; record the count (~30).
- [ ] **Step 2:** Update the "Automated tests" line + add a short changelog entry in `ARCHITECTURE_AND_ROADMAP.md`: HabitTrackingService/StreakRepositories/distribution now covered; note the **streak-reset bug found & fixed**. Stage ONLY the roadmap file (use `git add -p` if it carries unrelated Momentum edits).
- [ ] **Step 3:** Commit `docs: record HabitTracking test coverage + streak-reset fix`.

---

## Self-Review (plan author)

- **Spec coverage:** streak upsert (T1+T2, incl. the suspected bug), duplicate-day + create + 404 (T3), distribution bucketing (T4), suite+docs (T5). ✓
- **The bug is handled honestly:** T1 asserts CORRECT behavior and is EXPECTED to fail (red), reported not committed; T2 fixes then commits both together — so the repo never holds a committed red suite, and the test is a genuine guard (fails pre-fix). ✓
- **SQLite throughout** (transactions + raw EF). InMemory not used here. ✓
- **Determinism:** fixed dates everywhere; `PostDailyHabit` uses UtcNow.Date internally but the duplicate test only relies on "two calls same run = same day", which holds. Noted. ✓
- **Known risk flagged honestly:** the weekly `GroupBy` ternary may not translate on SQLite (T4 Step 2/3) — reported as a finding rather than worked around silently. ✓
- **Every commit stages explicit paths;** roadmap uses `git add -p`. ✓

## Notes / deferred
- `PostDailyHabit` is not wrapped in a transaction (create + upsert are two saves) while `PostHabitProgress` is — a consistency asymmetry. Out of scope here (no data-loss in the happy path); note for a follow-up.
- `PostHabitProgress` early-return guards return inside the `using var trx` without an explicit commit — harmless (nothing written; transaction rolls back), unlike the registration bug. No change needed.
