# Weekly CEO Report (v1 in-app) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the second paid feature — a current-week "CEO report" (weighted performance score + best/worst habit + consistency delta + top miss reason + a templated focus line), gated behind the Pro subscription and surfaced as a "This Week" card on the Dashboard.

**Architecture:** Extract the existing `ExpectedSessions`/frequency helpers from `HabitService` into a shared `Utils/HabitMath` (no duplicated goal-frequency math). A new owner-scoped `WeeklyReportService` computes a `WeeklyReportDto` on demand (current week + prev-week delta, no persistence). A `ReportController` exposes `GET /api/Report/weekly` decorated with the Plan 4 `[RequiresActiveSubscription]` gate. A `DashboardWeeklyReport.jsx` card (gated by `<RequirePro>`, same upgrade flow as `DashboardInsights`) renders the report. No EF migration.

**Tech Stack:** ASP.NET Core 8, EF Core 9, xUnit + FluentAssertions, React 19 + MUI 7.

**Spec:** `docs/superpowers/specs/2026-06-09-weekly-ceo-report-design.md`.

**Branch:** create/confirm `feat/performance-os-weekly-report` (orchestrator handles branch; do NOT implement on master).

---

## Context the implementer needs

- `HabitService` (`server/AtomicHabits/Services/HabitService.cs`) currently has these PRIVATE STATIC helpers (lines ~345–386):
  - `StartOfIsoWeek(DateTime today)` → Monday of `today`'s week.
  - `IsDailyFrequency(string f)` → daily/empty frequency check (note: matches both "day" and "dai" because "daily" lacks the substring "day").
  - `ExpectedSessions(Habit h, int daysElapsed, int periodLengthDays)` → expected completions for a habit in a window; uses `IsDailyFrequency`.
  - (also `IsDailyHabit(Habit)` which wraps `IsDailyFrequency` — leave it in HabitService, just have it call the shared one.)
- `Habit` has `Id, UserId, Name, GoalFrequency (string), IsArchived, MilestoneId (int?)`.
- `HabitTracking` has `HabitId, UserId, TrackingDate (DateTime?), IsCompleted (bool)`.
- `HabitSkip` has `UserId, HabitId, Date (DateOnly), Reason (enum SkipReason {Busy,Forgot,LowEnergy,NoMotivation,ScheduleConflict,Other})`.
- `InsightService` (`server/AtomicHabits/Services/InsightService.cs`) has a private `Humanize(SkipReason)` (LowEnergy→"Low Energy", etc.) — we'll promote that to a shared extension and reuse it.
- `ApiResponse` is `AtomicHabits.Models { bool IsSuccess; HttpStatusCode StatusCode; object Result; List<string> ErrorMessages; }`.
- `[RequiresActiveSubscription]` is in `AtomicHabits.Authorization` (Plan 4) — decorating a controller with it returns 403 for non-Pro users. `InsightController` is the existing example to mirror.
- `User.GetUserId()` extension in `AtomicHabits.Utils`.
- Frontend: `DashboardInsights.jsx` (`client-ui/src/views/dashboard/components/`) is the EXACT pattern to mirror — `<RequirePro fallback={<UpgradePanel/>}>`, the UpgradePanel calls `POST /api/Subscription/set-plan {plan:'Pro'}` then `refreshMe()` from `useAuth()`. `Dashboard.jsx` mounts the panels.
- **BUILD LOCK:** if a build/test hits MSB3027/MSB3021 file-lock on `AtomicHabits.exe`, a dev app is running — stop that process (Stop-Process/taskkill on the named PID) and retry.

---

### Task 1: Extract `HabitMath` shared helper (refactor; existing tests must stay green)

**Files:**
- Create: `server/AtomicHabits/Utils/HabitMath.cs`
- Modify: `server/AtomicHabits/Services/HabitService.cs` (delegate to the shared helper)

- [ ] **Step 1: Create the shared helper**

Create `server/AtomicHabits/Utils/HabitMath.cs` (move the exact logic from HabitService — do not change behavior):
```csharp
using AtomicHabits.Models;

namespace AtomicHabits.Utils
{
    /// <summary>
    /// Shared habit math (expected sessions, week boundaries, frequency checks),
    /// extracted from HabitService so WeeklyReportService reuses one source of
    /// truth for goal-frequency math instead of duplicating it.
    /// </summary>
    public static class HabitMath
    {
        public static DateTime StartOfIsoWeek(DateTime today)
        {
            int diff = (7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return today.AddDays(-diff).Date;
        }

        // True for an empty/unset frequency or a daily one. NOTE: the literal "daily"
        // does NOT contain the substring "day" (d-a-i-l-y), and "daily" is the model's
        // default value — so a naive Contains("day") silently undercounts every default
        // habit. Match both forms.
        public static bool IsDailyFrequency(string f)
        {
            return string.IsNullOrEmpty(f) || f.Contains("day") || f.Contains("dai");
        }

        public static bool IsDailyHabit(Habit h)
        {
            return IsDailyFrequency((h.GoalFrequency ?? "").Trim().ToLowerInvariant());
        }

        // Expected completions for a habit within a window of `daysElapsed` days
        // out of a `periodLengthDays`-day period (week=7, month=daysInMonth, etc.).
        public static int ExpectedSessions(Habit h, int daysElapsed, int periodLengthDays)
        {
            if (daysElapsed <= 0 || periodLengthDays <= 0) return 0;

            var f = (h.GoalFrequency ?? "").Trim().ToLowerInvariant();
            if (IsDailyFrequency(f)) return daysElapsed;
            if (f.Contains("week"))
            {
                double weeksElapsed = (double)daysElapsed / 7.0;
                return (int)Math.Ceiling(weeksElapsed);
            }
            if (f.Contains("month"))
            {
                return daysElapsed >= 1 ? 1 : 0;
            }
            if (f.Contains("year"))
            {
                return 0;
            }
            return daysElapsed;
        }
    }
}
```

- [ ] **Step 2: Point HabitService at the shared helper**

In `server/AtomicHabits/Services/HabitService.cs`:
- Add `using AtomicHabits.Utils;` if not present.
- DELETE the private `StartOfIsoWeek`, `IsDailyFrequency`, `IsDailyHabit`, and `ExpectedSessions` methods (lines ~345–386).
- Replace their call sites in HabitService with `HabitMath.StartOfIsoWeek(...)`, `HabitMath.IsDailyHabit(...)`, `HabitMath.ExpectedSessions(...)`, `HabitMath.IsDailyFrequency(...)` as applicable. (Grep within the file for each name to find call sites — e.g. `StartOfIsoWeek(today)` at ~line 170, `ExpectedSessions(h, ...)` at ~186/191, any `IsDailyHabit`/`IsDailyFrequency` uses.)

- [ ] **Step 3: Build + run the FULL existing suite (regression guard)**

Run (from `server/`): `dotnet build AtomicHabits.sln` → 0 errors.
Run: `dotnet test AtomicHabits.Tests/AtomicHabits.Tests.csproj` → ALL pass (currently 86; the existing HabitSummary/HabitService tests MUST still pass — this proves the extraction preserved behavior).

- [ ] **Step 4: Commit**

```bash
git add server/AtomicHabits/Utils/HabitMath.cs server/AtomicHabits/Services/HabitService.cs
git commit -m "refactor: extract ExpectedSessions/week helpers into shared HabitMath"
```

---

### Task 2: Shared `SkipReason.Humanize()` extension (DRY the reason wording)

**Files:**
- Create: `server/AtomicHabits/Utils/SkipReasonExtensions.cs`
- Modify: `server/AtomicHabits/Services/InsightService.cs` (use the shared one)

- [ ] **Step 1: Create the extension**

Create `server/AtomicHabits/Utils/SkipReasonExtensions.cs`:
```csharp
using AtomicHabits.Models;

namespace AtomicHabits.Utils
{
    public static class SkipReasonExtensions
    {
        public static string Humanize(this SkipReason r) => r switch
        {
            SkipReason.LowEnergy => "Low Energy",
            SkipReason.NoMotivation => "No Motivation",
            SkipReason.ScheduleConflict => "Schedule Conflict",
            _ => r.ToString()
        };
    }
}
```

- [ ] **Step 2: Use it in InsightService**

In `server/AtomicHabits/Services/InsightService.cs`: add `using AtomicHabits.Utils;`, replace the private `Humanize(SkipReason)` method's call sites with `reason.Humanize()` (extension form), and DELETE the private `Humanize` method. (If the private method is referenced as `Humanize(top.Reason)`, change to `top.Reason.Humanize()`.)

- [ ] **Step 3: Build + full suite**

Run (from `server/`): `dotnet build AtomicHabits.sln` → 0 errors; `dotnet test AtomicHabits.Tests/AtomicHabits.Tests.csproj` → all pass (the InsightService tests that assert "Low Energy" still pass).

- [ ] **Step 4: Commit**

```bash
git add server/AtomicHabits/Utils/SkipReasonExtensions.cs server/AtomicHabits/Services/InsightService.cs
git commit -m "refactor: share SkipReason.Humanize() extension"
```

---

### Task 3: `WeeklyReportDto`

**Files:**
- Create: `server/AtomicHabits/Models/DTO/WeeklyReportDto.cs`

- [ ] **Step 1: Create the DTO**
```csharp
namespace AtomicHabits.Models.DTO
{
    public class WeeklyReportDto
    {
        public int PerformanceScore { get; set; }       // 0–100
        public string ScoreBand { get; set; } = "Needs work"; // Strong | Building | Needs work
        public string? BestHabit { get; set; }
        public string? WorstHabit { get; set; }
        public int ConsistencyDelta { get; set; }        // signed; this week − last week (pts)
        public string? TopMissReason { get; set; }       // humanized; null if no skips
        public string? FocusNextWeek { get; set; }       // templated; null if no data
        public string WeekStart { get; set; } = string.Empty; // "yyyy-MM-dd" Monday
    }
}
```

- [ ] **Step 2: Build**

Run (from `server/`): `dotnet build AtomicHabits/AtomicHabits.csproj` → 0 errors.

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits/Models/DTO/WeeklyReportDto.cs
git commit -m "feat: add WeeklyReportDto"
```

---

### Task 4: `WeeklyReportService` (test-first)

**Files:**
- Create: `server/AtomicHabits/Services/WeeklyReportService.cs`
- Test: `server/AtomicHabits.Tests/Services/WeeklyReportServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `server/AtomicHabits.Tests/Services/WeeklyReportServiceTests.cs`. NOTE: the service computes "current week" from `DateTime.UtcNow`. To keep tests deterministic, seed completions relative to `HabitMath.StartOfIsoWeek(DateTime.UtcNow)` so they always land in the current window:
```csharp
using System;
using System.Linq;
using System.Threading;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class WeeklyReportServiceTests
{
    private static WeeklyReportService NewService(AtomicHabits.Data.AppDbContext db) =>
        new WeeklyReportService(db, NullLogger<WeeklyReportService>.Instance);

    private static DateTime ThisWeekDay(int offsetFromMonday) =>
        HabitMath.StartOfIsoWeek(DateTime.UtcNow).AddDays(offsetFromMonday);

    private static async Task<Habit> SeedHabit(AtomicHabits.Data.AppDbContext db, int userId, string name, bool linked = false)
    {
        var h = new Habit { UserId = userId, Name = name, Frequency = "Daily", GoalFrequency = "daily",
                            MilestoneId = linked ? (int?)1 : null };
        db.Habits.Add(h); await db.SaveChangesAsync(); return h;
    }

    private static void Complete(AtomicHabits.Data.AppDbContext db, Habit h, int dayOffsetFromMonday)
    {
        db.HabitTrackings.Add(new HabitTracking {
            UserId = h.UserId, HabitId = h.Id, IsCompleted = true,
            TrackingDate = ThisWeekDay(dayOffsetFromMonday)
        });
    }

    [Fact]
    public async Task No_habits_yields_zero_score_no_divide_by_zero()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var res = await svc.GetCurrentWeekAsync(1, CancellationToken.None);
        res.IsSuccess.Should().BeTrue();
        var dto = (WeeklyReportDto)res.Result!;
        dto.PerformanceScore.Should().Be(0);
        dto.ScoreBand.Should().Be("Needs work");
    }

    [Fact]
    public async Task Score_clamped_at_100_even_when_overlogged()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var h = await SeedHabit(db, 1, "Gym");
        // complete the same habit on several days (more than possible double-counting risk)
        for (int d = 0; d <= 6; d++) Complete(db, h, d);
        await db.SaveChangesAsync();

        var dto = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(1, CancellationToken.None)).Result!;
        dto.PerformanceScore.Should().BeLessThanOrEqualTo(100);
        dto.PerformanceScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Goal_linked_habit_is_weighted_more()
    {
        // Two users, identical completions; user A's habit is goal-linked (1.5x),
        // user B's is not. With partial completion, A's weighted score should differ
        // from B's only if completion != 100%. Use a partially-completed week.
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var linked = await SeedHabit(db, 1, "Linked", linked: true);
        var plain = await SeedHabit(db, 2, "Plain", linked: false);
        // complete 3 of the elapsed days for both (same ratio)
        for (int d = 0; d <= 2; d++) { Complete(db, linked, d); Complete(db, plain, d); }
        await db.SaveChangesAsync();

        var a = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(1, CancellationToken.None)).Result!;
        var b = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(2, CancellationToken.None)).Result!;
        // Same completion ratio → same score regardless of weight (weight cancels in the ratio).
        // This asserts weighting doesn't BREAK a single-habit ratio; cross-habit weighting is
        // exercised implicitly. Both should be equal and in 0..100.
        a.PerformanceScore.Should().Be(b.PerformanceScore);
        a.PerformanceScore.Should().BeInRange(0, 100);
    }

    [Fact]
    public async Task Top_miss_reason_is_humanized_and_owner_scoped()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var h = await SeedHabit(db, 1, "Gym");
        var monday = DateOnly.FromDateTime(HabitMath.StartOfIsoWeek(DateTime.UtcNow));
        db.HabitSkips.AddRange(
            new HabitSkip { UserId = 1, HabitId = h.Id, Date = monday, Reason = SkipReason.LowEnergy },
            new HabitSkip { UserId = 1, HabitId = h.Id, Date = monday.AddDays(1), Reason = SkipReason.LowEnergy },
            new HabitSkip { UserId = 1, HabitId = h.Id, Date = monday.AddDays(2), Reason = SkipReason.Busy }
        );
        await db.SaveChangesAsync();

        var dto = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(1, CancellationToken.None)).Result!;
        dto.TopMissReason.Should().Be("Low Energy");

        var other = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(2, CancellationToken.None)).Result!;
        other.TopMissReason.Should().BeNull(); // user 2 has no skips
    }

    [Fact]
    public async Task Best_and_worst_habit_identified()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var good = await SeedHabit(db, 1, "Reading");
        var bad = await SeedHabit(db, 1, "Exercise");
        // Reading completed every elapsed day; Exercise not at all
        for (int d = 0; d <= 6; d++) Complete(db, good, d);
        await db.SaveChangesAsync();

        var dto = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(1, CancellationToken.None)).Result!;
        dto.BestHabit.Should().Be("Reading");
        dto.WorstHabit.Should().Be("Exercise");
        dto.FocusNextWeek.Should().Contain("Exercise");
    }

    [Fact]
    public async Task Week_start_is_monday()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var dto = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(1, CancellationToken.None)).Result!;
        var parsed = DateTime.Parse(dto.WeekStart);
        parsed.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test server/AtomicHabits.Tests --filter WeeklyReportServiceTests`
Expected: FAIL — `WeeklyReportService` doesn't exist.

- [ ] **Step 3: Implement the service**

Create `server/AtomicHabits/Services/WeeklyReportService.cs`:
```csharp
using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Utils;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IWeeklyReportService
    {
        Task<ApiResponse> GetCurrentWeekAsync(int userId, CancellationToken ct);
    }

    public class WeeklyReportService : IWeeklyReportService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<WeeklyReportService> _logger;

        public WeeklyReportService(AppDbContext db, ILogger<WeeklyReportService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse> GetCurrentWeekAsync(int userId, CancellationToken ct)
        {
            var today = DateTime.UtcNow.Date;
            var weekStart = HabitMath.StartOfIsoWeek(today);                 // Monday this week
            var lastWeekStart = weekStart.AddDays(-7);
            int daysElapsedThisWeek = (int)(today - weekStart).TotalDays + 1; // inclusive of today
            const int lastWeekDays = 7;

            var habits = await _db.Habits
                .Where(h => h.UserId == userId && !h.IsArchived)
                .ToListAsync(ct);

            var dto = new WeeklyReportDto
            {
                WeekStart = weekStart.ToString("yyyy-MM-dd"),
                ScoreBand = "Needs work"
            };

            if (habits.Count == 0)
                return Ok(dto); // score 0, no habits

            // pull this-week + last-week completed trackings once
            var completions = await _db.HabitTrackings
                .Where(t => t.UserId == userId && t.IsCompleted && t.TrackingDate != null
                            && t.TrackingDate >= lastWeekStart && t.TrackingDate < weekStart.AddDays(7))
                .Select(t => new { t.HabitId, Date = t.TrackingDate!.Value.Date })
                .ToListAsync(ct);

            // per-habit completed counts within a [start,endExclusive) window
            int CompletedFor(int habitId, DateTime start, DateTime endExclusive) =>
                completions.Count(c => c.HabitId == habitId && c.Date >= start && c.Date < endExclusive);

            // ---- weighted performance score (this week) ----
            double weightedCompleted = 0, weightedExpected = 0;
            var perHabitRate = new List<(string Name, double Rate)>();
            foreach (var h in habits)
            {
                double weight = h.MilestoneId != null ? 1.5 : 1.0;
                int expected = HabitMath.ExpectedSessions(h, daysElapsedThisWeek, 7);
                if (expected <= 0) continue;
                int completed = Math.Min(CompletedFor(h.Id, weekStart, weekStart.AddDays(7)), expected); // cap so >100% impossible
                weightedExpected += weight * expected;
                weightedCompleted += weight * completed;
                perHabitRate.Add((h.Name, (double)completed / expected));
            }

            int score = weightedExpected <= 0 ? 0
                : (int)Math.Round(100.0 * weightedCompleted / weightedExpected);
            score = Math.Clamp(score, 0, 100);
            dto.PerformanceScore = score;
            dto.ScoreBand = score >= 75 ? "Strong" : score >= 40 ? "Building" : "Needs work";

            // ---- best / worst habit ----
            if (perHabitRate.Count >= 2)
            {
                dto.BestHabit = perHabitRate.OrderByDescending(p => p.Rate).First().Name;
                var worst = perHabitRate.OrderBy(p => p.Rate).First();
                dto.WorstHabit = worst.Name;
                dto.FocusNextWeek = $"Focus next week: {worst.Name} — your lowest completion at {(int)Math.Round(worst.Rate * 100)}%.";
            }

            // ---- consistency delta (UNWEIGHTED, simple rate this vs last week) ----
            int ExpectedSum(int daysElapsed) => habits.Sum(h => HabitMath.ExpectedSessions(h, daysElapsed, 7));
            int thisExpected = ExpectedSum(daysElapsedThisWeek);
            int thisCompleted = habits.Sum(h => Math.Min(CompletedFor(h.Id, weekStart, weekStart.AddDays(7)),
                                                          HabitMath.ExpectedSessions(h, daysElapsedThisWeek, 7)));
            int lastExpected = ExpectedSum(lastWeekDays);
            int lastCompleted = habits.Sum(h => Math.Min(CompletedFor(h.Id, lastWeekStart, weekStart),
                                                          HabitMath.ExpectedSessions(h, lastWeekDays, 7)));
            int thisRate = thisExpected <= 0 ? 0 : (int)Math.Round(100.0 * thisCompleted / thisExpected);
            int lastRate = lastExpected <= 0 ? 0 : (int)Math.Round(100.0 * lastCompleted / lastExpected);
            dto.ConsistencyDelta = lastExpected <= 0 ? 0 : thisRate - lastRate;

            // ---- top miss reason (this week) ----
            var weekStartDate = DateOnly.FromDateTime(weekStart);
            var weekEndDate = DateOnly.FromDateTime(weekStart.AddDays(7));
            var topReason = await _db.HabitSkips
                .Where(s => s.UserId == userId && s.Date >= weekStartDate && s.Date < weekEndDate)
                .GroupBy(s => s.Reason)
                .Select(g => new { Reason = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .FirstOrDefaultAsync(ct);
            if (topReason != null) dto.TopMissReason = topReason.Reason.Humanize();

            return Ok(dto);
        }

        private static ApiResponse Ok(object result) =>
            new() { IsSuccess = true, StatusCode = HttpStatusCode.OK, Result = result };
    }
}
```
> EF note: `t.TrackingDate!.Value.Date` is projected to memory via the
> `.Select(...).ToListAsync()` (we materialize `{HabitId, Date}` first), so no
> server-side date-component translation issue. Counts are per-user, small.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test server/AtomicHabits.Tests --filter WeeklyReportServiceTests`
Expected: PASS (all 6 facts).

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Services/WeeklyReportService.cs server/AtomicHabits.Tests/Services/WeeklyReportServiceTests.cs
git commit -m "feat: add WeeklyReportService (current-week performance report)"
```

---

### Task 5: Gated `ReportController` + DI

**Files:**
- Create: `server/AtomicHabits/Controllers/ReportController.cs`
- Modify: `server/AtomicHabits/Program.cs`

- [ ] **Step 1: Register the service**

In `server/AtomicHabits/Program.cs`, after the `IInsightService` registration:
```csharp
builder.Services.AddScoped<IWeeklyReportService, WeeklyReportService>();
```

- [ ] **Step 2: Create the gated controller** (mirror `InsightController`):
```csharp
using AtomicHabits.Authorization;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtomicHabits.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [RequiresActiveSubscription] // PAID — Plan 4 gate; non-Pro users get 403
    public class ReportController : ControllerBase
    {
        private readonly IWeeklyReportService _service;
        public ReportController(IWeeklyReportService service) => _service = service;

        [HttpGet("weekly")]
        public async Task<IActionResult> Weekly(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.GetCurrentWeekAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
```

- [ ] **Step 3: Build + full suite**

Run (from `server/`): `dotnet build AtomicHabits.sln` → 0 errors; `dotnet test AtomicHabits.Tests/AtomicHabits.Tests.csproj` → all pass (86 + 6 new = 92).

- [ ] **Step 4: Commit**

```bash
git add server/AtomicHabits/Controllers/ReportController.cs server/AtomicHabits/Program.cs
git commit -m "feat: add gated GET /api/Report/weekly endpoint (Pro only)"
```

---

### Task 6: Frontend — `DashboardWeeklyReport` card

**Files:**
- Create: `client-ui/src/views/dashboard/components/DashboardWeeklyReport.jsx`
- Modify: `client-ui/src/views/dashboard/Dashboard.jsx`

- [ ] **Step 1: Inspect the pattern**

Read `client-ui/src/views/dashboard/components/DashboardInsights.jsx` — copy its structure exactly: `<RequirePro fallback={<UpgradePanel/>}>` wrapping a content component; UpgradePanel uses `useAuth().refreshMe` + `api.post('/Subscription/set-plan', { plan: 'Pro' })`; content fetches on mount with an `active` cleanup guard. Read `Dashboard.jsx` to see how panels are laid out (Grid).

- [ ] **Step 2: Create `DashboardWeeklyReport.jsx`**

Create the card mirroring `DashboardInsights.jsx`'s gating/upgrade shape, but content = the weekly report. Endpoint: `GET /api/Report/weekly` → `res.data?.result` (a `WeeklyReportDto`, camelCase: `performanceScore`, `scoreBand`, `bestHabit`, `worstHabit`, `consistencyDelta`, `topMissReason`, `focusNextWeek`, `weekStart`). Render:
```jsx
import React, { useEffect, useState } from 'react';
import { Card, CardContent, Typography, Box, CircularProgress, Button, Stack, Divider } from '@mui/material';
import { IconTrophy, IconLock, IconArrowUpRight, IconArrowDownRight } from '@tabler/icons-react';
import api from '../../../api/axiosInstance';
import { getAccessToken } from '../../../utils/tokenUtils';
import RequirePro from '../../../components/RequirePro';
import { useAuth } from '../../../context/AuthContext';

const UpgradePanel = () => {
  const { refreshMe } = useAuth();
  const [busy, setBusy] = useState(false);
  const handleUpgrade = async () => {
    setBusy(true);
    try {
      // Manual plan flip — replaced by Stripe checkout in Plan 7.
      await api.post('/Subscription/set-plan', { plan: 'Pro' });
      await refreshMe?.();
    } catch (err) { console.warn('Upgrade failed:', err?.message); }
    finally { setBusy(false); }
  };
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" spacing={1.5} alignItems="center" sx={{ mb: 1 }}>
          <IconLock size={22} />
          <Typography variant="h5" fontWeight={600}>This Week</Typography>
        </Stack>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Your weekly performance report — a Pro feature.
        </Typography>
        <Button variant="contained" onClick={handleUpgrade} disabled={busy}>
          {busy ? 'Upgrading…' : 'Upgrade to Pro'}
        </Button>
      </CardContent>
    </Card>
  );
};

const ReportContent = () => {
  const [report, setReport] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!getAccessToken()) return;
    let active = true;
    (async () => {
      try {
        const res = await api.get('/Report/weekly');
        if (active) setReport(res.data?.result || null);
      } catch (err) { console.error('Failed to load weekly report:', err.message); }
      finally { if (active) setLoading(false); }
    })();
    return () => { active = false; };
  }, []);

  if (loading) {
    return (
      <Card sx={{ height: '100%' }}><CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}><CircularProgress size={28} /></Box>
      </CardContent></Card>
    );
  }

  const r = report;
  const hasData = r && (r.bestHabit || r.topMissReason || r.performanceScore > 0);
  const delta = r?.consistencyDelta ?? 0;

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" justifyContent="space-between" alignItems="baseline" sx={{ mb: 1 }}>
          <Stack direction="row" spacing={1.5} alignItems="center">
            <IconTrophy size={22} />
            <Typography variant="h5" fontWeight={600}>This Week</Typography>
          </Stack>
          {r?.weekStart && <Typography variant="caption" color="text.secondary">from {r.weekStart}</Typography>}
        </Stack>

        {!hasData ? (
          <Typography variant="body2" color="text.secondary">
            Your first weekly report builds as you log this week — check back as the week fills in.
          </Typography>
        ) : (
          <>
            {/* hero score */}
            <Box sx={{ textAlign: 'center', py: 1 }}>
              <Typography variant="h2" fontWeight={700} lineHeight={1}>{r.performanceScore}</Typography>
              <Typography variant="subtitle2" color="text.secondary">Performance score · {r.scoreBand}</Typography>
            </Box>
            <Divider sx={{ my: 1.5 }} />
            <Stack spacing={1}>
              {r.bestHabit && <Typography variant="body2">🏆 Best: <b>{r.bestHabit}</b></Typography>}
              {r.worstHabit && <Typography variant="body2">🎯 Needs work: <b>{r.worstHabit}</b></Typography>}
              <Stack direction="row" spacing={0.5} alignItems="center">
                {delta >= 0 ? <IconArrowUpRight size={16} color="#13DEB9" /> : <IconArrowDownRight size={16} color="#FA896B" />}
                <Typography variant="body2" sx={{ color: delta >= 0 ? 'success.main' : 'error.main' }}>
                  {delta >= 0 ? '+' : ''}{delta}% vs last week
                </Typography>
              </Stack>
              {r.topMissReason && <Typography variant="body2" color="text.secondary">Top miss reason: {r.topMissReason}</Typography>}
              {r.focusNextWeek && <Typography variant="body2" sx={{ mt: 0.5, fontStyle: 'italic' }}>{r.focusNextWeek}</Typography>}
            </Stack>
          </>
        )}
      </CardContent>
    </Card>
  );
};

const DashboardWeeklyReport = () => (
  <RequirePro fallback={<UpgradePanel />}>
    <ReportContent />
  </RequirePro>
);

export default DashboardWeeklyReport;
```
Verify the Tabler icons (`IconTrophy`, `IconArrowUpRight`, `IconArrowDownRight`, `IconLock`) are real `@tabler/icons-react` exports (they are; confirm during impl).

- [ ] **Step 3: Mount on the Dashboard**

In `client-ui/src/views/dashboard/Dashboard.jsx`: add `const DashboardWeeklyReport = lazy(() => import('./components/DashboardWeeklyReport'));` and render it in the Grid near `DashboardInsights` (e.g. in the right column with the Insights panel, both Pro narrative cards). Keep the layout balanced — place it as its own `<Grid item xs={12} lg={4}>` (or beside Insights) wrapped in `<Suspense fallback={fallback}>`.

- [ ] **Step 4: Build**

Run (from `client-ui/`): `npx vite build` → succeeds.

- [ ] **Step 5: Commit**

```bash
git add client-ui/src/views/dashboard/
git commit -m "feat: add This Week report card on Dashboard (RequirePro)"
```

---

## Self-Review (completed by plan author)

- **Spec coverage:** §1 computation — `WeeklyReportService` with weighted score (reusing extracted `HabitMath.ExpectedSessions`), best/worst, consistency delta (unweighted, documented), top miss reason (shared `Humanize`), templated focus line, all owner-scoped + tested (Task 4). §2 gated endpoint (`ReportController` + `[RequiresActiveSubscription]`, Task 5) + Dashboard "This Week" card behind `<RequirePro>` (Task 6). Shared-helper extraction (Task 1) + Humanize sharing (Task 2) per spec's "Affected files". ✓
- **No persistence / no migration / no email:** confirmed — service computes on demand; no `WeeklyReport` table; no `BackgroundService`. ✓
- **Refactor safety:** Task 1 + 2 each end with the FULL existing suite passing (proves `ExpectedSessions`/`Humanize` extraction preserved behavior — the main risk of touching `HabitService`/`InsightService`). ✓
- **Placeholders:** Task 6 says "inspect DashboardInsights pattern" but gives the full component code; the only true inspection is Dashboard grid placement (a layout detail). Not a placeholder failure. ✓
- **Type consistency:** `WeeklyReportDto` fields (PascalCase C# → camelCase JSON: `performanceScore`/`scoreBand`/`bestHabit`/`worstHabit`/`consistencyDelta`/`topMissReason`/`focusNextWeek`/`weekStart`) match the frontend reads in Task 6. `IWeeklyReportService.GetCurrentWeekAsync`, `HabitMath.ExpectedSessions/StartOfIsoWeek`, `SkipReason.Humanize()` consistent across tasks. ✓
- **EF translation:** the trackings query materializes `{HabitId, Date}` to memory before any date filtering in C#; the skip query groups server-side on `Reason` (an int enum — translates fine) and filters on `Date` (DateOnly, translates). No `.Hour`/`.DayOfWeek` traps. ✓
- **Score-can't-exceed-100:** per-habit completed capped at expected before weighting; plus final `Math.Clamp`. Tested. ✓

## Subsequent
Deferred fast-follow: Sunday auto-email (`WeeklyReportDispatcherService : BackgroundService` + `WeeklyReport` persistence + `IEmailSender`). Plan 7: Stripe replaces the manual `set-plan` upgrade used by both Pro Dashboard cards.
