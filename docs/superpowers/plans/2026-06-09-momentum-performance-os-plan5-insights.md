# Momentum Performance OS — Plan 5: Failure Analysis Insights (PAID)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The first PAID feature — a deterministic (no-LLM) "Insights" view that turns the collected `HabitSkip` + `HabitTracking` data into ~4 plain-language insight strings, gated behind the `[RequiresActiveSubscription]` backend gate and the `<RequirePro>` frontend wrapper built in Plan 4.

**Architecture:** A new `InsightService` (service-direct to `AppDbContext`, like `GoalFrameworkService`/`HabitSkipService`) computes a fixed set of insight templates via LINQ aggregation and returns them as a list of `{ key, text }` DTOs — empty templates omitted when data is insufficient. An `InsightController` exposes `GET /api/Insight` decorated with `[RequiresActiveSubscription]` (the Plan 4 gate → 403 for non-Pro). A frontend `Insights.jsx` view (new `/insights` route + sidebar entry) fetches it, wrapped in `<RequirePro>` showing an upgrade prompt to free users. Rules-based only — zero LLM (that's v2).

**Tech Stack:** ASP.NET Core 8, EF Core 9, xUnit + FluentAssertions, React 19 + MUI 7.

**Spec:** `docs/superpowers/specs/2026-06-07-momentum-performance-os-v1-design.md` §3b.

**Branch:** create/confirm `feat/performance-os-insights` (orchestrator handles branch; do NOT implement on master).

---

## IMPORTANT — spec-vs-reality adaptation (read before implementing)

The spec lists four example insights. Checked against the ACTUAL schema:
- `HabitSkip` has `{ UserId, HabitId, Date (DateOnly), Reason (enum), CreatedAt }` — a skip has **no time-of-day**, only a calendar date.
- `HabitTracking` has `{ HabitId, UserId, TrackingDate (DateTime?), IsCompleted, TimeSpentMinutes, CompletedAt (DateTime?), CreatedAt }` — completions DO carry a `CompletedAt` timestamp.

Therefore the spec's "**68% of misses happen after 9 PM**" is **not computable** (skips have no time). We adapt that insight honestly to a **completion** time-of-day pattern from `CompletedAt`, which is real data. The four insights this plan ships:

1. **Top miss reason** (`HabitSkip.Reason`): "Your most common reason for skipping is **Low Energy** (41% of skips)."
2. **Completion time-of-day** (`HabitTracking.CompletedAt`): "You complete most habits in the **morning** (before noon) — 64% of your completions." (buckets: morning <12, afternoon 12–18, evening ≥18)
3. **Weekday vs weekend completions** (`HabitTracking`): "You complete **2.3×** more habits on weekdays than weekends." (or weekend-stronger, whichever is true)
4. **Most-skipped habit** (`HabitSkip` grouped by habit): "**Exercise** is your most-skipped habit (7 skips)." 

Each insight is computed independently; if its data threshold isn't met (see per-insight minimums), it's omitted from the response (never shown with empty/zero values). If NO insight qualifies, the response is an empty list (the UI shows a "keep logging to unlock insights" empty state).

---

## File Structure

**Backend (create):**
- `server/AtomicHabits/Models/DTO/InsightDto.cs` — `InsightDto { string Key; string Text; }`.
- `server/AtomicHabits/Services/InsightService.cs` — `IInsightService` + impl.
- `server/AtomicHabits/Controllers/InsightController.cs` — gated endpoint.
- Test: `server/AtomicHabits.Tests/Services/InsightServiceTests.cs`.

**Backend (modify):**
- `server/AtomicHabits/Program.cs` — register `IInsightService`.

**Frontend (create):**
- `client-ui/src/views/insights/Insights.jsx` — the view (wrapped in `<RequirePro>`).

**Frontend (modify):**
- `client-ui/src/routes/Router.jsx` — `/insights` protected route.
- `client-ui/src/layouts/sidebar/SidebarItems.jsx` — "Insights" nav entry.

---

## DTO reference

`InsightDto.cs`:
```csharp
namespace AtomicHabits.Models.DTO
{
    public class InsightDto
    {
        public string Key { get; set; } = string.Empty;   // stable id, e.g. "top-skip-reason"
        public string Text { get; set; } = string.Empty;   // plain-language sentence
    }
}
```

---

### Task 1: DTO

**Files:**
- Create: `server/AtomicHabits/Models/DTO/InsightDto.cs`

- [ ] **Step 1: Create the DTO** with the content from the "DTO reference" block above.

- [ ] **Step 2: Build**

Run (from `server/`): `dotnet build AtomicHabits/AtomicHabits.csproj`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits/Models/DTO/InsightDto.cs
git commit -m "feat: add InsightDto"
```

---

### Task 2: InsightService (test-first)

**Files:**
- Create: `server/AtomicHabits/Services/InsightService.cs`
- Test: `server/AtomicHabits.Tests/Services/InsightServiceTests.cs`

Build the service with FOUR private insight computations + a public `GetInsightsAsync(userId, ct)` that runs each, collecting non-null `InsightDto`s into a list. Each computation returns `InsightDto?` (null when its threshold isn't met). All queries owner-scoped by `userId`. Follow `HabitSkipService`'s `ApiResponse Ok/Error` helper style for the public method's return (it returns `ApiResponse` with `Result = List<InsightDto>`).

**Per-insight rules (exact):**
- **top-skip-reason:** group the user's `HabitSkips` by `Reason`; need ≥3 total skips. Text: `$"Your most common reason for skipping is {Humanize(reason)} ({pct}% of skips)."` where `pct = round(100 * topCount / totalSkips)`. `Humanize` maps enum → words (e.g. `LowEnergy` → "Low Energy"; reuse a small switch).
- **completion-time-of-day:** over the user's completed `HabitTrackings` with non-null `CompletedAt`; need ≥5 such completions. Bucket each `CompletedAt.Value.Hour`: morning (<12), afternoon (12–17), evening (≥18). Find the top bucket. Text: `$"You complete most habits in the {bucket} — {pct}% of your completions."`
- **weekday-vs-weekend:** over completed `HabitTrackings` with non-null `CompletedAt` (or `TrackingDate`); need ≥5 completions AND at least 1 on each side. Compute weekday count vs weekend count (Sat/Sun). If weekday/perWeekdayRate higher: `$"You complete {ratio}× more habits on weekdays than weekends."` (ratio = round(weekdayPerDay / weekendPerDay, 1), guard divide-by-zero); else the weekend-stronger phrasing. Keep it simple: compare raw counts normalized per available day (5 weekdays vs 2 weekend days) — document the formula in a comment.
- **most-skipped-habit:** group `HabitSkips` by `HabitId`, join to `Habits` for the name; need ≥3 skips total and a clear top habit. Text: `$"{habitName} is your most-skipped habit ({count} skips)."`

- [ ] **Step 1: Write the failing tests**

Create `server/AtomicHabits.Tests/Services/InsightServiceTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class InsightServiceTests
{
    private static InsightService NewService(AtomicHabits.Data.AppDbContext db) =>
        new InsightService(db, NullLogger<InsightService>.Instance);

    private static async Task<int> SeedHabit(AtomicHabits.Data.AppDbContext db, int userId, string name)
    {
        var h = new Habit { UserId = userId, Name = name, Frequency = "Daily" };
        db.Habits.Add(h); await db.SaveChangesAsync(); return h.Id;
    }

    [Fact]
    public async Task No_data_returns_empty_insight_list()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var res = await svc.GetInsightsAsync(1, CancellationToken.None);
        res.IsSuccess.Should().BeTrue();
        ((IEnumerable<InsightDto>)res.Result!).Should().BeEmpty();
    }

    [Fact]
    public async Task Top_skip_reason_insight_appears_with_enough_skips()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var hid = await SeedHabit(db, 1, "Gym");
        db.HabitSkips.AddRange(
            new HabitSkip { UserId = 1, HabitId = hid, Date = new DateOnly(2026, 6, 1), Reason = SkipReason.LowEnergy },
            new HabitSkip { UserId = 1, HabitId = hid, Date = new DateOnly(2026, 6, 2), Reason = SkipReason.LowEnergy },
            new HabitSkip { UserId = 1, HabitId = hid, Date = new DateOnly(2026, 6, 3), Reason = SkipReason.Busy }
        );
        await db.SaveChangesAsync();

        var res = await svc.GetInsightsAsync(1, CancellationToken.None);
        var insights = ((IEnumerable<InsightDto>)res.Result!).ToList();
        var skipReason = insights.FirstOrDefault(i => i.Key == "top-skip-reason");
        skipReason.Should().NotBeNull();
        skipReason!.Text.Should().Contain("Low Energy");
        skipReason.Text.Should().Contain("67%"); // 2 of 3
    }

    [Fact]
    public async Task Top_skip_reason_omitted_below_threshold()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var hid = await SeedHabit(db, 1, "Gym");
        db.HabitSkips.Add(new HabitSkip { UserId = 1, HabitId = hid, Date = new DateOnly(2026, 6, 1), Reason = SkipReason.Busy });
        await db.SaveChangesAsync(); // only 1 skip (<3)

        var res = await svc.GetInsightsAsync(1, CancellationToken.None);
        ((IEnumerable<InsightDto>)res.Result!).Any(i => i.Key == "top-skip-reason").Should().BeFalse();
    }

    [Fact]
    public async Task Completion_time_of_day_insight_appears()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var hid = await SeedHabit(db, 1, "Read");
        // 5 morning completions (hour 8)
        for (int d = 1; d <= 5; d++)
            db.HabitTrackings.Add(new HabitTracking {
                UserId = 1, HabitId = hid, IsCompleted = true,
                CompletedAt = new DateTime(2026, 6, d, 8, 0, 0, DateTimeKind.Utc),
                TrackingDate = new DateTime(2026, 6, d, 8, 0, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();

        var res = await svc.GetInsightsAsync(1, CancellationToken.None);
        var tod = ((IEnumerable<InsightDto>)res.Result!).FirstOrDefault(i => i.Key == "completion-time-of-day");
        tod.Should().NotBeNull();
        tod!.Text.Should().Contain("morning");
    }

    [Fact]
    public async Task Most_skipped_habit_insight_names_the_habit()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var gym = await SeedHabit(db, 1, "Exercise");
        var read = await SeedHabit(db, 1, "Read");
        db.HabitSkips.AddRange(
            new HabitSkip { UserId = 1, HabitId = gym, Date = new DateOnly(2026,6,1), Reason = SkipReason.Busy },
            new HabitSkip { UserId = 1, HabitId = gym, Date = new DateOnly(2026,6,2), Reason = SkipReason.Busy },
            new HabitSkip { UserId = 1, HabitId = gym, Date = new DateOnly(2026,6,3), Reason = SkipReason.LowEnergy },
            new HabitSkip { UserId = 1, HabitId = read, Date = new DateOnly(2026,6,1), Reason = SkipReason.Forgot }
        );
        await db.SaveChangesAsync();

        var res = await svc.GetInsightsAsync(1, CancellationToken.None);
        var top = ((IEnumerable<InsightDto>)res.Result!).FirstOrDefault(i => i.Key == "most-skipped-habit");
        top.Should().NotBeNull();
        top!.Text.Should().Contain("Exercise");
    }

    [Fact]
    public async Task Insights_are_owner_scoped()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var hid = await SeedHabit(db, 2, "Gym"); // belongs to user 2
        db.HabitSkips.AddRange(
            new HabitSkip { UserId = 2, HabitId = hid, Date = new DateOnly(2026,6,1), Reason = SkipReason.Busy },
            new HabitSkip { UserId = 2, HabitId = hid, Date = new DateOnly(2026,6,2), Reason = SkipReason.Busy },
            new HabitSkip { UserId = 2, HabitId = hid, Date = new DateOnly(2026,6,3), Reason = SkipReason.Busy }
        );
        await db.SaveChangesAsync();

        var res = await svc.GetInsightsAsync(1, CancellationToken.None); // user 1 sees nothing
        ((IEnumerable<InsightDto>)res.Result!).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test server/AtomicHabits.Tests --filter InsightServiceTests`
Expected: FAIL — `InsightService` doesn't exist.

- [ ] **Step 3: Implement `InsightService`**

Create `server/AtomicHabits/Services/InsightService.cs`. Structure:
```csharp
using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IInsightService
    {
        Task<ApiResponse> GetInsightsAsync(int userId, CancellationToken ct);
    }

    public class InsightService : IInsightService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<InsightService> _logger;

        public InsightService(AppDbContext db, ILogger<InsightService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse> GetInsightsAsync(int userId, CancellationToken ct)
        {
            var insights = new List<InsightDto>();

            var topReason = await TopSkipReasonAsync(userId, ct);
            if (topReason != null) insights.Add(topReason);

            var tod = await CompletionTimeOfDayAsync(userId, ct);
            if (tod != null) insights.Add(tod);

            var wk = await WeekdayVsWeekendAsync(userId, ct);
            if (wk != null) insights.Add(wk);

            var mostSkipped = await MostSkippedHabitAsync(userId, ct);
            if (mostSkipped != null) insights.Add(mostSkipped);

            return new ApiResponse { IsSuccess = true, StatusCode = HttpStatusCode.OK, Result = insights };
        }

        private async Task<InsightDto?> TopSkipReasonAsync(int userId, CancellationToken ct)
        {
            var skips = await _db.HabitSkips.Where(s => s.UserId == userId)
                .GroupBy(s => s.Reason)
                .Select(g => new { Reason = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var total = skips.Sum(s => s.Count);
            if (total < 3) return null;
            var top = skips.OrderByDescending(s => s.Count).First();
            var pct = (int)Math.Round(100.0 * top.Count / total);
            return new InsightDto { Key = "top-skip-reason",
                Text = $"Your most common reason for skipping is {Humanize(top.Reason)} ({pct}% of skips)." };
        }

        private async Task<InsightDto?> CompletionTimeOfDayAsync(int userId, CancellationToken ct)
        {
            var hours = await _db.HabitTrackings
                .Where(t => t.UserId == userId && t.IsCompleted && t.CompletedAt != null)
                .Select(t => t.CompletedAt!.Value.Hour)
                .ToListAsync(ct);
            if (hours.Count < 5) return null;
            int morning = hours.Count(h => h < 12);
            int afternoon = hours.Count(h => h >= 12 && h < 18);
            int evening = hours.Count(h => h >= 18);
            var buckets = new[] { ("morning", morning), ("afternoon", afternoon), ("evening", evening) };
            var top = buckets.OrderByDescending(b => b.Item2).First();
            var pct = (int)Math.Round(100.0 * top.Item2 / hours.Count);
            return new InsightDto { Key = "completion-time-of-day",
                Text = $"You complete most habits in the {top.Item1} — {pct}% of your completions." };
        }

        private async Task<InsightDto?> WeekdayVsWeekendAsync(int userId, CancellationToken ct)
        {
            var dows = await _db.HabitTrackings
                .Where(t => t.UserId == userId && t.IsCompleted && t.CompletedAt != null)
                .Select(t => t.CompletedAt!.Value.DayOfWeek)
                .ToListAsync(ct);
            if (dows.Count < 5) return null;
            int weekend = dows.Count(d => d == DayOfWeek.Saturday || d == DayOfWeek.Sunday);
            int weekday = dows.Count - weekend;
            if (weekday == 0 || weekend == 0) return null; // need both sides
            // normalize per available day (5 weekdays vs 2 weekend days)
            double weekdayPerDay = weekday / 5.0;
            double weekendPerDay = weekend / 2.0;
            if (weekdayPerDay >= weekendPerDay)
            {
                var ratio = Math.Round(weekdayPerDay / weekendPerDay, 1);
                return new InsightDto { Key = "weekday-vs-weekend",
                    Text = $"You complete {ratio}× more habits on weekdays than weekends." };
            }
            else
            {
                var ratio = Math.Round(weekendPerDay / weekdayPerDay, 1);
                return new InsightDto { Key = "weekday-vs-weekend",
                    Text = $"You complete {ratio}× more habits on weekends than weekdays." };
            }
        }

        private async Task<InsightDto?> MostSkippedHabitAsync(int userId, CancellationToken ct)
        {
            var grouped = await _db.HabitSkips.Where(s => s.UserId == userId)
                .GroupBy(s => s.HabitId)
                .Select(g => new { HabitId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            if (grouped.Sum(g => g.Count) < 3) return null;
            var top = grouped.OrderByDescending(g => g.Count).First();
            var name = await _db.Habits.Where(h => h.Id == top.HabitId && h.UserId == userId)
                .Select(h => h.Name).FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(name)) return null;
            return new InsightDto { Key = "most-skipped-habit",
                Text = $"{name} is your most-skipped habit ({top.Count} skips)." };
        }

        private static string Humanize(SkipReason r) => r switch
        {
            SkipReason.LowEnergy => "Low Energy",
            SkipReason.NoMotivation => "No Motivation",
            SkipReason.ScheduleConflict => "Schedule Conflict",
            _ => r.ToString()
        };
    }
}
```
> EF note: the time-of-day/day-of-week computations pull the raw hours/days to
> memory (`ToListAsync`) then bucket in C#. This avoids EF trying to translate
> `.Hour`/`.DayOfWeek` (provider-dependent) and works identically on the
> in-memory test provider and SQL Server. Counts are small (one user's
> completions), so this is fine.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test server/AtomicHabits.Tests --filter InsightServiceTests`
Expected: PASS (all 6 facts).

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Services/InsightService.cs server/AtomicHabits.Tests/Services/InsightServiceTests.cs
git commit -m "feat: add InsightService (rules-based failure analysis)"
```

---

### Task 3: Gated controller + DI

**Files:**
- Create: `server/AtomicHabits/Controllers/InsightController.cs`
- Modify: `server/AtomicHabits/Program.cs`

- [ ] **Step 1: Register the service**

In `server/AtomicHabits/Program.cs`, after the `IHabitSkipService` registration (or near the other `AddScoped` services):
```csharp
builder.Services.AddScoped<IInsightService, InsightService>();
```

- [ ] **Step 2: Create the gated controller**

Create `server/AtomicHabits/Controllers/InsightController.cs`. Mirror `TagController` BUT add the Plan 4 gate `[RequiresActiveSubscription]` (from `AtomicHabits.Authorization`) so only Pro users reach it:
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
    public class InsightController : ControllerBase
    {
        private readonly IInsightService _service;
        public InsightController(IInsightService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.GetInsightsAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
```

- [ ] **Step 3: Build + full test run**

Run (from `server/`): `dotnet build AtomicHabits.sln` → 0 errors; `dotnet test AtomicHabits.Tests/AtomicHabits.Tests.csproj` → all pass (79 + 6 = 85).
> If a build file-lock error on `AtomicHabits.exe` occurs, a dev app instance is running — stop it and retry.

- [ ] **Step 4: Commit**

```bash
git add server/AtomicHabits/Controllers/InsightController.cs server/AtomicHabits/Program.cs
git commit -m "feat: add gated GET /api/Insight endpoint (Pro only)"
```

---

### Task 4: Frontend — Insights view behind `<RequirePro>`

**Files:**
- Create: `client-ui/src/views/insights/Insights.jsx`
- Modify: `client-ui/src/routes/Router.jsx`, `client-ui/src/layouts/sidebar/SidebarItems.jsx`

- [ ] **Step 1: Build the Insights view**

Create `client-ui/src/views/insights/Insights.jsx` using MUI + `PageContainer` (match `client-ui/src/views/goals/Goals.jsx` for the page-wrapper pattern). Behavior:
- Wrap the whole content in `<RequirePro fallback={<UpgradeCard />}>` (import from `../../components/RequirePro`). `<UpgradeCard>` is a small inline component: a Card saying "Insights are a Pro feature" + a button "Upgrade to Pro" that, for now (no Stripe yet), calls `POST /api/Subscription/set-plan {plan:'Pro'}` then refreshes the page/auth so Pro unlocks. (This doubles as the manual test path — note in a comment that Stripe replaces this in Plan 7.)
- When Pro: on mount, `GET /api/Insight`, read `res.data?.result` (array of `{key,text}`), render each as a Card/list item with an icon. If the array is empty, show an empty state: "Keep logging your habits and skips — your insights will appear here as patterns emerge."
- Use `api` from `../../api/axiosInstance` and the app's MUI conventions.

> NOTE: to make the "Upgrade to Pro" button actually flip the UI, after the
> set-plan POST you must refresh the auth/me state. Check how `AuthContext`
> exposes a refresh (e.g. a `refreshMe`/`restoreSession`-type function) — if one
> exists, call it; otherwise `window.location.reload()` is an acceptable
> stopgap for this manual-test phase (note it in a comment). Inspect AuthContext
> first and use the cleanest available option.

- [ ] **Step 2: Add the route**

In `client-ui/src/routes/Router.jsx`, add the lazy import + protected route (same children group as `/goals`):
```jsx
const Insights = lazy(() => import('../views/insights/Insights'));
```
```jsx
      { path: '/insights', exact: true, element: <Insights /> },
```

- [ ] **Step 3: Add the sidebar entry**

In `client-ui/src/layouts/sidebar/SidebarItems.jsx`, add an "Insights" item pointing to `/insights` (use a Tabler icon present in the import set, e.g. `IconBulb` or `IconChartHistogram` — confirm/add to the tabler import). Place it near "Stats"/"Goals".

- [ ] **Step 4: Build**

Run (from `client-ui/`): `npx vite build`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add client-ui/src/views/insights/Insights.jsx client-ui/src/routes/Router.jsx client-ui/src/layouts/sidebar/SidebarItems.jsx
git commit -m "feat: add Insights view gated behind RequirePro"
```

---

## Self-Review (completed by plan author)

- **Spec coverage (§3b):** rules-based insights from HabitSkip + HabitTracking (Task 2, 4 templates), gated PAID via `[RequiresActiveSubscription]` + `<RequirePro>` (Tasks 3–4), templates omitted when data insufficient (per-insight thresholds + tested `..._omitted_below_threshold`), no LLM. ✓
- **Spec-vs-reality adaptation:** the spec's "misses after 9 PM" is NOT computable (HabitSkip has no time-of-day) — adapted to a **completion** time-of-day insight from `HabitTracking.CompletedAt`, flagged loudly at the top of the plan. This is the most important judgment call here. ✓
- **Reuses Plan 4 gate:** the controller uses the real `[RequiresActiveSubscription]` attribute (built + tested in Plan 4) — this is the first consumer, and the manual `set-plan` from Plan 4 is wired into the upgrade button for end-to-end testing. ✓
- **Owner-scoping:** every query filters by `userId`; tested (`Insights_are_owner_scoped`). ✓
- **EF translation trap:** flagged — `.Hour`/`.DayOfWeek` pulled to memory before bucketing (provider-safe). ✓
- **Placeholders:** Task 4 says "inspect AuthContext for a refresh function" rather than inventing one — gives a concrete fallback (`window.location.reload()`). Not a placeholder failure. ✓
- **Type consistency:** `InsightDto {Key,Text}`, `IInsightService.GetInsightsAsync`, insight keys (`top-skip-reason`/`completion-time-of-day`/`weekday-vs-weekend`/`most-skipped-habit`) consistent across service, tests, and frontend reads. ✓

## Subsequent plans
Plan 6 (Weekly CEO Report — second paid feature, same gate + a Sunday background job), Plan 7 (Stripe — replaces the manual set-plan upgrade button with real billing).
