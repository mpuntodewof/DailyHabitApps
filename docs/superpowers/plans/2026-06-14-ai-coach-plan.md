# AI Coach v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a paid AI Coach feature that returns a Claude-written 3-part coaching narrative (summary / patterns / actions) over a verified, backend-computed fact sheet for a selectable window (1/2/3 weeks or a month).

**Architecture:** `CoachController` (gated by `[RequiresActiveSubscription]`) → `CoachService` (cache → rate-limit → fact sheet → gateway → persist) → `IClaudeCoachGateway` (the only unit that calls Claude, behind an interface like `IStripeGateway`). The fact sheet is built by `CoachFactSheetBuilder` from windowed aggregations; `InsightService`/`WeeklyReportService` math is generalized to a `(from,to)` range. Claude writes prose only — it never computes or sees raw rows — so a templated fallback covers any Claude error.

**Tech Stack:** ASP.NET Core 8, EF Core (SQL Server prod / SQLite + InMemory tests), the official `Anthropic` C# SDK, xUnit + FluentAssertions + the existing `SqliteTestDb` / `TestDbContextFactory` harness.

**Design spec:** `docs/superpowers/specs/2026-06-14-ai-coach-design.md`

---

## Conventions used throughout

- **Namespaces:** services in `AtomicHabits.Services`, DTOs in `AtomicHabits.Models.DTO`, entities in `AtomicHabits.Models`, config in `AtomicHabits.Config`, utils in `AtomicHabits.Utils`, controllers in `AtomicHabits.Controllers`.
- **Test harness:** `AtomicHabits.Tests` (xUnit). Use `SqliteTestDb` (real SQLite, honors transactions and `DateOnly`/`DayOfWeek` translation) for service/builder tests; `NullLogger<T>.Instance` for loggers; `FluentAssertions`.
- **Commit prefix:** `feat:` for features, `test:` for test-only, `refactor:` for the windowing extraction, `chore:` for wiring.
- **Run all backend tests:** `dotnet test server/AtomicHabits.Tests` (from repo root). Run a single test: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~TestClass.TestMethod"`.

---

## File Structure

**Created:**
- `server/AtomicHabits/Config/CoachOptions.cs` — config (model, cache TTL, rate limit, API key).
- `server/AtomicHabits/Models/AiCoachReport.cs` — persisted entity (cache + ledger + history).
- `server/AtomicHabits/Models/DTO/CoachFactSheet.cs` — verified facts passed to Claude.
- `server/AtomicHabits/Models/DTO/CoachNarrative.cs` — Claude's structured output (summary/patterns/actions).
- `server/AtomicHabits/Models/DTO/CoachReportDto.cs` — API response to the frontend.
- `server/AtomicHabits/Utils/CoachWindow.cs` — resolves `weeks`/`month` query into a `(from,to)` range + label + cache key.
- `server/AtomicHabits/Services/CoachFactSheetBuilder.cs` — windowed stats → `CoachFactSheet`.
- `server/AtomicHabits/Services/IClaudeCoachGateway.cs` — gateway interface.
- `server/AtomicHabits/Services/ClaudeCoachGateway.cs` — Anthropic SDK impl.
- `server/AtomicHabits/Services/CoachService.cs` — orchestration.
- `server/AtomicHabits/Services/CoachFallback.cs` — templated narrative from a fact sheet.
- `server/AtomicHabits/Controllers/CoachController.cs` — `GET /api/Coach`.
- `server/AtomicHabits/Scheduling/MonthlyCoachService.cs` — monthly background generation.
- Migration: `AddAiCoachReport` (generated, not hand-written).
- Tests: `CoachWindowTests`, `CoachFactSheetBuilderTests`, `ClaudeCoachGatewayTests`, `CoachServiceTests` under `server/AtomicHabits.Tests/...`.
- Frontend: `client-ui/src/views/dashboard/DashboardAiCoach.jsx`.

**Modified:**
- `server/AtomicHabits/Services/InsightService.cs` — add windowed internal aggregations.
- `server/AtomicHabits/Services/WeeklyReportService.cs` — extract a windowed scoring method.
- `server/AtomicHabits/Data/AppDbContext.cs` — add `DbSet<AiCoachReport>` + FK config.
- `server/AtomicHabits/Program.cs` — DI + options registration.
- `server/AtomicHabits/appsettings.json` + `appsettings.Development.json` — `Coach` section.
- `server/AtomicHabits/AtomicHabits.csproj` — add the `Anthropic` package.
- `client-ui/src/views/dashboard/Dashboard.jsx` (or the dashboard compose file) — mount the card.

---

## Task 1: CoachOptions config

**Files:**
- Create: `server/AtomicHabits/Config/CoachOptions.cs`

- [ ] **Step 1: Create the options class**

```csharp
namespace AtomicHabits.Config
{
    public class CoachOptions
    {
        public const string SectionName = "Coach";

        // Swappable model: claude-sonnet-4-6 (v1 default) | claude-haiku-4-5 | claude-opus-4-8
        public string Model { get; set; } = "claude-sonnet-4-6";

        // Repeat views of the same (user, window) within this many hours return the cached row.
        // 0 = always regenerate (Development).
        public int CacheTtlHours { get; set; } = 24;

        // Max generations per user per UTC day. 0 = unlimited (Development).
        public int MaxGenerationsPerDay { get; set; } = 10;

        // Anthropic API key — from user-secrets, never source. Empty key disables live calls
        // (gateway will surface an error → CoachService uses the templated fallback).
        public string ApiKey { get; set; } = string.Empty;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add server/AtomicHabits/Config/CoachOptions.cs
git commit -m "feat: add CoachOptions config for AI Coach"
```

---

## Task 2: AiCoachReport entity + DbContext

**Files:**
- Create: `server/AtomicHabits/Models/AiCoachReport.cs`
- Modify: `server/AtomicHabits/Data/AppDbContext.cs`

- [ ] **Step 1: Create the entity**

```csharp
using System.ComponentModel.DataAnnotations;

namespace AtomicHabits.Models
{
    public enum CoachWindowKind { Weeks, Month }

    public class AiCoachReport
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        public CoachWindowKind WindowKind { get; set; }

        // "1"/"2"/"3" for Weeks, "yyyy-MM" for Month. Cache-key discriminator.
        [MaxLength(16)]
        public string WindowValue { get; set; } = string.Empty;

        public DateTime RangeStart { get; set; }
        public DateTime RangeEnd { get; set; }

        public string Summary { get; set; } = string.Empty;
        public string PatternsJson { get; set; } = "[]";  // JSON array of strings
        public string ActionsJson { get; set; } = "[]";   // JSON array of strings

        [MaxLength(64)]
        public string Model { get; set; } = string.Empty;

        public bool IsFallback { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
```

- [ ] **Step 2: Register the DbSet + FK in AppDbContext**

In `server/AtomicHabits/Data/AppDbContext.cs`, add the `DbSet` alongside the others:

```csharp
public DbSet<AiCoachReport> AiCoachReports => Set<AiCoachReport>();
```

In `OnModelCreating(...)`, add (place near the other entity configs, following the existing style):

```csharp
modelBuilder.Entity<AiCoachReport>(e =>
{
    e.HasIndex(r => new { r.UserId, r.WindowKind, r.WindowValue, r.CreatedAt });
    e.HasOne<User>()
        .WithMany()
        .HasForeignKey(r => r.UserId)
        .OnDelete(DeleteBehavior.Cascade); // account deletion cleans up coach reports (GDPR)
});
```

> If `AppDbContext` uses a different `using`/namespace style or already imports `AtomicHabits.Models`, match the file. Verify `User` is the user entity name before writing the FK.

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build server/AtomicHabits`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add server/AtomicHabits/Models/AiCoachReport.cs server/AtomicHabits/Data/AppDbContext.cs
git commit -m "feat: add AiCoachReport entity + DbContext mapping"
```

---

## Task 3: EF migration AddAiCoachReport

**Files:**
- Create: migration files under `server/AtomicHabits/Migrations/` (generated).

- [ ] **Step 1: Generate the migration**

Run (from repo root):
```bash
dotnet ef migrations add AddAiCoachReport --project server/AtomicHabits
```
Expected: a new `*_AddAiCoachReport.cs` + designer file under `server/AtomicHabits/Migrations/`.

> If `dotnet ef` is not installed: `dotnet tool install --global dotnet-ef` first.

- [ ] **Step 2: Inspect the migration**

Open the generated `Up()` and confirm it creates an `AiCoachReports` table with the columns from Task 2 and the composite index. No data backfill should be present (additive only).

- [ ] **Step 3: Apply to the local dev database**

Run: `dotnet ef database update --project server/AtomicHabits`
Expected: "Done." and the table exists.

- [ ] **Step 4: Commit**

```bash
git add server/AtomicHabits/Migrations/
git commit -m "feat: add AddAiCoachReport migration"
```

---

## Task 4: CoachWindow resolver (TDD)

Resolves the `weeks`/`month` request into a date range, label, `WindowKind`, and `WindowValue`. Pure, no DB — easy to test exhaustively.

**Files:**
- Create: `server/AtomicHabits/Utils/CoachWindow.cs`
- Test: `server/AtomicHabits.Tests/Utils/CoachWindowTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using AtomicHabits.Models;
using AtomicHabits.Utils;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Utils;

public class CoachWindowTests
{
    private static readonly DateTime Today = new(2026, 06, 14); // a Sunday

    [Fact]
    public void Weeks_resolves_inclusive_day_count_ending_today()
    {
        var w = CoachWindow.ForWeeks(2, Today);
        w.Kind.Should().Be(CoachWindowKind.Weeks);
        w.Value.Should().Be("2");
        w.To.Should().Be(Today.Date.AddDays(1));         // exclusive end = tomorrow midnight
        w.From.Should().Be(Today.Date.AddDays(1).AddDays(-14)); // 14-day window
        w.Label.Should().Be("the last 2 weeks");
    }

    [Fact]
    public void Weeks_one_uses_singular_label()
    {
        CoachWindow.ForWeeks(1, Today).Label.Should().Be("the last week");
    }

    [Fact]
    public void Weeks_clamps_to_supported_range()
    {
        // only 1..3 supported; out-of-range throws
        var act = () => CoachWindow.ForWeeks(4, Today);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Month_resolves_calendar_month_bounds()
    {
        var w = CoachWindow.ForMonth("2026-06", Today);
        w.Kind.Should().Be(CoachWindowKind.Month);
        w.Value.Should().Be("2026-06");
        w.From.Should().Be(new DateTime(2026, 06, 01));
        w.To.Should().Be(new DateTime(2026, 07, 01));     // exclusive
        w.Label.Should().Be("June 2026");
    }

    [Fact]
    public void Month_rejects_bad_format()
    {
        var act = () => CoachWindow.ForMonth("2026/06", Today);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void PriorWindow_is_an_equal_length_range_immediately_before()
    {
        var w = CoachWindow.ForWeeks(2, Today);
        var prior = w.PriorWindow();
        prior.To.Should().Be(w.From);
        (prior.To - prior.From).Should().Be(w.To - w.From);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~CoachWindowTests"`
Expected: FAIL — `CoachWindow` does not exist.

- [ ] **Step 3: Implement CoachWindow**

```csharp
using System;
using System.Globalization;
using AtomicHabits.Models;

namespace AtomicHabits.Utils
{
    // Resolves a coach request into a concrete [From, To) date window plus display/cache metadata.
    // To is EXCLUSIVE (matches the existing tracking-query convention: >= start && < end).
    public sealed class CoachWindow
    {
        public CoachWindowKind Kind { get; }
        public string Value { get; }
        public DateTime From { get; }
        public DateTime To { get; }     // exclusive
        public string Label { get; }

        private CoachWindow(CoachWindowKind kind, string value, DateTime from, DateTime to, string label)
        {
            Kind = kind; Value = value; From = from; To = to; Label = label;
        }

        public static CoachWindow ForWeeks(int weeks, DateTime today)
        {
            if (weeks < 1 || weeks > 3)
                throw new ArgumentOutOfRangeException(nameof(weeks), "Only 1..3 weeks are supported.");
            var to = today.Date.AddDays(1);            // include all of today
            var from = to.AddDays(-7 * weeks);
            var label = weeks == 1 ? "the last week" : $"the last {weeks} weeks";
            return new CoachWindow(CoachWindowKind.Weeks, weeks.ToString(), from, to, label);
        }

        public static CoachWindow ForMonth(string month, DateTime today)
        {
            // month must be exactly "yyyy-MM"
            if (!DateTime.TryParseExact(month + "-01", "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
                throw new FormatException($"Month must be 'yyyy-MM', got '{month}'.");
            var from = new DateTime(first.Year, first.Month, 1);
            var to = from.AddMonths(1);
            var label = from.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            return new CoachWindow(CoachWindowKind.Month, month, from, to, label);
        }

        // An equal-length window immediately before this one (for consistency deltas).
        public CoachWindow PriorWindow()
        {
            var span = To - From;
            return new CoachWindow(Kind, Value + "-prior", From - span, From, "the prior period");
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~CoachWindowTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Utils/CoachWindow.cs server/AtomicHabits.Tests/Utils/CoachWindowTests.cs
git commit -m "feat: add CoachWindow resolver with tests"
```

---

## Task 5: Generalize WeeklyReportService scoring to a window (refactor, TDD)

Extract the windowed performance-score computation so the fact sheet builder can reuse it for any `(from,to)`. Keep `GetCurrentWeekAsync` working unchanged (it delegates to the new method).

**Files:**
- Modify: `server/AtomicHabits/Services/WeeklyReportService.cs`
- Test: `server/AtomicHabits.Tests/Services/WeeklyReportWindowTests.cs`

- [ ] **Step 1: Write the failing test for the windowed method**

```csharp
using System;
using System.Threading;
using AtomicHabits.Models;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class WeeklyReportWindowTests
{
    private static WeeklyReportService NewService(AtomicHabits.Data.AppDbContext db) =>
        new WeeklyReportService(db, NullLogger<WeeklyReportService>.Instance);

    [Fact]
    public async Task Window_score_counts_only_completions_in_range()
    {
        using var sqlite = new SqliteTestDb();
        var db = sqlite.Context;
        var h = new Habit { UserId = 1, Name = "Gym", Frequency = "Daily", GoalFrequency = "daily" };
        db.Habits.Add(h);
        await db.SaveChangesAsync();

        var from = new DateTime(2026, 06, 01);
        var to = new DateTime(2026, 06, 08); // 7-day window
        // one completion inside, one outside
        db.HabitTrackings.Add(new HabitTracking { UserId = 1, HabitId = h.Id, IsCompleted = true, TrackingDate = new DateTime(2026, 06, 03) });
        db.HabitTrackings.Add(new HabitTracking { UserId = 1, HabitId = h.Id, IsCompleted = true, TrackingDate = new DateTime(2026, 05, 30) });
        await db.SaveChangesAsync();

        var svc = NewService(db);
        var stats = await svc.ComputeWindowStatsAsync(1, from, to, CancellationToken.None);

        // 1 completion / 7 expected daily sessions in the window
        stats.PerformanceScore.Should().BeGreaterThan(0).And.BeLessThan(100);
        stats.BestHabit.Should().Be("Gym");
    }

    [Fact]
    public async Task Window_score_zero_when_no_habits()
    {
        using var sqlite = new SqliteTestDb();
        var svc = NewService(sqlite.Context);
        var stats = await svc.ComputeWindowStatsAsync(1,
            new DateTime(2026, 06, 01), new DateTime(2026, 06, 08), CancellationToken.None);
        stats.PerformanceScore.Should().Be(0);
        stats.ScoreBand.Should().Be("Needs work");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~WeeklyReportWindowTests"`
Expected: FAIL — `ComputeWindowStatsAsync` / `WindowStats` do not exist.

- [ ] **Step 3: Add the windowed method + a small result record**

In `server/AtomicHabits/Services/WeeklyReportService.cs`:

1. Add to the `IWeeklyReportService` interface:
```csharp
Task<WindowStats> ComputeWindowStatsAsync(int userId, DateTime from, DateTime to, CancellationToken ct);
```

2. Add the record (top of the namespace, above the interface):
```csharp
public record WindowStats(
    int PerformanceScore,
    string ScoreBand,
    string? BestHabit,
    double? BestHabitRate,
    string? WorstHabit,
    double? WorstHabitRate,
    int ConsistencyDelta,
    double CompletionRate,
    int GoalLinkedHabitCount,
    int TotalHabitCount);
```

3. Implement `ComputeWindowStatsAsync` by generalizing the existing body. The existing method hardcodes `weekStart`/`weekStart.AddDays(7)`; the new one uses `from`/`to` and computes `daysElapsed = (int)(to - from).TotalDays`, prior window `[from-span, from)`. Reuse `HabitMath.ExpectedSessions(h, daysElapsed, daysElapsed)` (period length = window length so a daily habit expects one per day).

```csharp
public async Task<WindowStats> ComputeWindowStatsAsync(int userId, DateTime from, DateTime to, CancellationToken ct)
{
    int windowDays = Math.Max(0, (int)(to - from).TotalDays);
    var priorFrom = from.AddDays(-windowDays);

    var habits = await _db.Habits
        .Where(h => h.UserId == userId && !h.IsArchived)
        .ToListAsync(ct);

    if (habits.Count == 0)
        return new WindowStats(0, "Needs work", null, null, null, null, 0, 0, 0, 0);

    var completions = await _db.HabitTrackings
        .Where(t => t.UserId == userId && t.IsCompleted && t.TrackingDate != null
                    && t.TrackingDate >= priorFrom && t.TrackingDate < to)
        .Select(t => new { t.HabitId, Date = t.TrackingDate!.Value.Date })
        .ToListAsync(ct);

    int CompletedFor(int habitId, DateTime start, DateTime endExclusive) =>
        completions.Count(c => c.HabitId == habitId && c.Date >= start && c.Date < endExclusive);

    double weightedCompleted = 0, weightedExpected = 0;
    var perHabitRate = new List<(string Name, double Rate)>();
    foreach (var h in habits)
    {
        double weight = h.MilestoneId != null ? 1.5 : 1.0;
        int expected = HabitMath.ExpectedSessions(h, windowDays, windowDays);
        if (expected <= 0) continue;
        int completed = Math.Min(CompletedFor(h.Id, from, to), expected);
        weightedExpected += weight * expected;
        weightedCompleted += weight * completed;
        perHabitRate.Add((h.Name, (double)completed / expected));
    }

    int score = weightedExpected <= 0 ? 0 : (int)Math.Round(100.0 * weightedCompleted / weightedExpected);
    score = Math.Clamp(score, 0, 100);
    string band = score >= 75 ? "Strong" : score >= 40 ? "Building" : "Needs work";

    string? best = null, worst = null;
    double? bestRate = null, worstRate = null;
    if (perHabitRate.Count >= 1)
    {
        var b = perHabitRate.OrderByDescending(p => p.Rate).First();
        var w = perHabitRate.OrderBy(p => p.Rate).First();
        best = b.Name; bestRate = b.Rate; worst = w.Name; worstRate = w.Rate;
    }

    int ExpectedSum() => habits.Sum(h => HabitMath.ExpectedSessions(h, windowDays, windowDays));
    int thisExpected = ExpectedSum();
    int thisCompleted = habits.Sum(h => Math.Min(CompletedFor(h.Id, from, to),
                                                 HabitMath.ExpectedSessions(h, windowDays, windowDays)));
    int priorExpected = ExpectedSum();
    int priorCompleted = habits.Sum(h => Math.Min(CompletedFor(h.Id, priorFrom, from),
                                                  HabitMath.ExpectedSessions(h, windowDays, windowDays)));
    int thisRate = thisExpected <= 0 ? 0 : (int)Math.Round(100.0 * thisCompleted / thisExpected);
    int priorRate = priorExpected <= 0 ? 0 : (int)Math.Round(100.0 * priorCompleted / priorExpected);
    int delta = priorExpected <= 0 ? 0 : thisRate - priorRate;
    double completionRate = thisExpected <= 0 ? 0 : (double)thisCompleted / thisExpected;

    return new WindowStats(score, band, best, bestRate, worst, worstRate, delta, completionRate,
        habits.Count(h => h.MilestoneId != null), habits.Count);
}
```

> Leave `GetCurrentWeekAsync` as-is (Plan 6 tests still cover it). The two methods coexist; the fact sheet builder uses the new one.

- [ ] **Step 4: Run both the new and existing report tests**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~WeeklyReport"`
Expected: PASS — both `WeeklyReportWindowTests` (new) and `WeeklyReportServiceTests` (unchanged) green.

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Services/WeeklyReportService.cs server/AtomicHabits.Tests/Services/WeeklyReportWindowTests.cs
git commit -m "feat: add windowed ComputeWindowStatsAsync to WeeklyReportService"
```

---

## Task 6: Generalize InsightService aggregations to a window (refactor, TDD)

Add a windowed method returning the skip/time-of-day facts the fact sheet needs. Keep `GetInsightsAsync` (all-time) working.

**Files:**
- Modify: `server/AtomicHabits/Services/InsightService.cs`
- Test: `server/AtomicHabits.Tests/Services/InsightWindowTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Threading;
using AtomicHabits.Models;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class InsightWindowTests
{
    private static InsightService NewService(AtomicHabits.Data.AppDbContext db) =>
        new InsightService(db, NullLogger<InsightService>.Instance);

    [Fact]
    public async Task Window_facts_report_top_skip_reason_in_range()
    {
        using var sqlite = new SqliteTestDb();
        var db = sqlite.Context;
        var h = new Habit { UserId = 1, Name = "Gym", Frequency = "Daily", GoalFrequency = "daily" };
        db.Habits.Add(h);
        await db.SaveChangesAsync();

        // 3 skips in range, all LowEnergy
        for (int d = 1; d <= 3; d++)
            db.HabitSkips.Add(new HabitSkip { UserId = 1, HabitId = h.Id,
                Reason = SkipReason.LowEnergy, Date = new DateOnly(2026, 06, 0 + d + 1) });
        await db.SaveChangesAsync();

        var svc = NewService(db);
        var facts = await svc.ComputeWindowFactsAsync(1,
            new DateTime(2026, 06, 01), new DateTime(2026, 06, 08), CancellationToken.None);

        facts.TopSkipReason.Should().NotBeNull();
        facts.TopSkipReason!.Reason.Should().Be("Low Energy");
        facts.MostSkippedHabit!.Name.Should().Be("Gym");
    }

    [Fact]
    public async Task Window_facts_null_below_threshold()
    {
        using var sqlite = new SqliteTestDb();
        var svc = NewService(sqlite.Context);
        var facts = await svc.ComputeWindowFactsAsync(1,
            new DateTime(2026, 06, 01), new DateTime(2026, 06, 08), CancellationToken.None);
        facts.TopSkipReason.Should().BeNull();           // no data → null, mirrors GetInsightsAsync threshold
        facts.EnoughForSkipPatterns.Should().BeFalse();
    }
}
```

> Confirm the `SkipReason` enum member name (`LowEnergy`) and the `HabitSkip.Date` type (`DateOnly`) against `server/AtomicHabits/Models/HabitSkip.cs` before running — adjust the seed if the names differ.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~InsightWindowTests"`
Expected: FAIL — `ComputeWindowFactsAsync` / `WindowFacts` do not exist.

- [ ] **Step 3: Add the windowed facts method**

In `server/AtomicHabits/Services/InsightService.cs`:

1. Add the result record (top of namespace):
```csharp
public record InsightWindowFacts(
    (string Reason, int Pct)? TopSkipReason,
    (string Name, int Skips)? MostSkippedHabit,
    (string Bucket, int Pct)? CompletionTimeOfDay,
    (string MoreOn, double Ratio)? WeekdayVsWeekend,
    bool EnoughForSkipPatterns)
{
    public (string Reason, int Pct)? TopSkipReason { get; init; } = TopSkipReason;
    public (string Name, int Skips)? MostSkippedHabit { get; init; } = MostSkippedHabit;
    public (string Bucket, int Pct)? CompletionTimeOfDay { get; init; } = CompletionTimeOfDay;
    public (string MoreOn, double Ratio)? WeekdayVsWeekend { get; init; } = WeekdayVsWeekend;
    public bool EnoughForSkipPatterns { get; init; } = EnoughForSkipPatterns;
}
```
(If tuple-typed records are awkward in the codebase's C# version, use a plain class with the same properties.)

2. Add to `IInsightService`:
```csharp
Task<InsightWindowFacts> ComputeWindowFactsAsync(int userId, DateTime from, DateTime to, CancellationToken ct);
```

3. Implement it by adapting the four private methods to take `from`/`to` (a `DateOnly` range for skips, a `DateTime` range for trackings) and the same thresholds (skip total < 3 → null; completions < 5 → null). **Reuse `SkipReason.Humanize()`** for the reason text. Materialize the `DayOfWeek`/`Hour` data before bucketing (the same SQL-translation fix the changelog records for `GetInsightsAsync`):

```csharp
public async Task<InsightWindowFacts> ComputeWindowFactsAsync(int userId, DateTime from, DateTime to, CancellationToken ct)
{
    var fromDate = DateOnly.FromDateTime(from);
    var toDate = DateOnly.FromDateTime(to);

    var skips = await _db.HabitSkips
        .Where(s => s.UserId == userId && s.Date >= fromDate && s.Date < toDate)
        .Select(s => new { s.Reason, s.HabitId })
        .ToListAsync(ct);

    (string, int)? topReason = null;
    (string, int)? mostSkipped = null;
    bool enoughSkips = skips.Count >= 3;
    if (enoughSkips)
    {
        var byReason = skips.GroupBy(s => s.Reason)
            .Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).First();
        topReason = (byReason.Key.Humanize(), (int)Math.Round(100.0 * byReason.Count / skips.Count));

        var byHabit = skips.GroupBy(s => s.HabitId)
            .Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).First();
        var name = await _db.Habits.Where(h => h.Id == byHabit.Key && h.UserId == userId)
            .Select(h => h.Name).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrEmpty(name)) mostSkipped = (name, byHabit.Count);
    }

    var completedAts = await _db.HabitTrackings
        .Where(t => t.UserId == userId && t.IsCompleted && t.CompletedAt != null
                    && t.TrackingDate >= from && t.TrackingDate < to)
        .Select(t => t.CompletedAt!.Value)
        .ToListAsync(ct);

    (string, int)? tod = null;
    (string, double)? wk = null;
    if (completedAts.Count >= 5)
    {
        var hours = completedAts.Select(d => d.Hour).ToList();
        int morning = hours.Count(h => h < 12), afternoon = hours.Count(h => h >= 12 && h < 18), evening = hours.Count(h => h >= 18);
        var top = new[] { ("morning", morning), ("afternoon", afternoon), ("evening", evening) }
            .OrderByDescending(b => b.Item2).First();
        tod = (top.Item1, (int)Math.Round(100.0 * top.Item2 / hours.Count));

        var dows = completedAts.Select(d => d.DayOfWeek).ToList();
        int weekend = dows.Count(d => d == DayOfWeek.Saturday || d == DayOfWeek.Sunday);
        int weekday = dows.Count - weekend;
        if (weekday > 0 && weekend > 0)
        {
            double wkdayPerDay = weekday / 5.0, wkendPerDay = weekend / 2.0;
            wk = wkdayPerDay >= wkendPerDay
                ? ("weekday", Math.Round(wkdayPerDay / wkendPerDay, 1))
                : ("weekend", Math.Round(wkendPerDay / wkdayPerDay, 1));
        }
    }

    return new InsightWindowFacts(topReason, mostSkipped, tod, wk, enoughSkips);
}
```

> Verify `HabitTracking.CompletedAt` and `TrackingDate` property names/types against `server/AtomicHabits/Models/HabitTracking.cs` before relying on them — the existing `GetInsightsAsync` uses both, so they exist; just confirm spelling.

- [ ] **Step 4: Run new + existing insight tests**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~Insight"`
Expected: PASS — `InsightWindowTests` (new) and `InsightServiceTests` (unchanged) green.

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Services/InsightService.cs server/AtomicHabits.Tests/Services/InsightWindowTests.cs
git commit -m "feat: add windowed ComputeWindowFactsAsync to InsightService"
```

---

## Task 7: CoachFactSheet DTO + CoachFactSheetBuilder (TDD)

**Files:**
- Create: `server/AtomicHabits/Models/DTO/CoachFactSheet.cs`
- Create: `server/AtomicHabits/Services/CoachFactSheetBuilder.cs`
- Test: `server/AtomicHabits.Tests/Services/CoachFactSheetBuilderTests.cs`

- [ ] **Step 1: Create the DTO**

```csharp
namespace AtomicHabits.Models.DTO
{
    public class CoachFactSheet
    {
        public string WindowLabel { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;  // yyyy-MM-dd
        public string To { get; set; } = string.Empty;    // yyyy-MM-dd (exclusive)

        public int PerformanceScore { get; set; }
        public string ScoreBand { get; set; } = "Needs work";
        public int ConsistencyDelta { get; set; }
        public double CompletionRate { get; set; }

        public string? BestHabit { get; set; }
        public double? BestHabitRate { get; set; }
        public string? WorstHabit { get; set; }
        public double? WorstHabitRate { get; set; }

        public string? TopSkipReason { get; set; }
        public int? TopSkipReasonPct { get; set; }
        public string? MostSkippedHabit { get; set; }
        public int? MostSkippedHabitCount { get; set; }
        public string? CompletionTimeBucket { get; set; }
        public int? CompletionTimePct { get; set; }
        public string? WeekdayVsWeekendMoreOn { get; set; }
        public double? WeekdayVsWeekendRatio { get; set; }

        public string? IdentityTitle { get; set; }
        public string? IdentitySource { get; set; }   // "Vision" | "Goal" | "Milestone"
        public int GoalLinkedHabitCount { get; set; }
        public int TotalHabitCount { get; set; }

        // Overall gate: enough data to bother calling Claude at all.
        public bool EnoughForReport { get; set; }
    }
}
```

- [ ] **Step 2: Write the failing test**

```csharp
using System;
using System.Threading;
using AtomicHabits.Models;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class CoachFactSheetBuilderTests
{
    private static CoachFactSheetBuilder NewBuilder(AtomicHabits.Data.AppDbContext db) =>
        new CoachFactSheetBuilder(
            new WeeklyReportService(db, NullLogger<WeeklyReportService>.Instance),
            new InsightService(db, NullLogger<InsightService>.Instance),
            db,
            NullLogger<CoachFactSheetBuilder>.Instance);

    [Fact]
    public async Task Empty_account_is_not_enough_for_report()
    {
        using var sqlite = new SqliteTestDb();
        var builder = NewBuilder(sqlite.Context);
        var window = CoachWindow.ForWeeks(2, new DateTime(2026, 06, 14));
        var sheet = await builder.BuildAsync(1, window, CancellationToken.None);
        sheet.EnoughForReport.Should().BeFalse();
        sheet.TotalHabitCount.Should().Be(0);
    }

    [Fact]
    public async Task Builds_facts_and_marks_enough_when_data_present()
    {
        using var sqlite = new SqliteTestDb();
        var db = sqlite.Context;
        var h = new Habit { UserId = 1, Name = "Gym", Frequency = "Daily", GoalFrequency = "daily" };
        db.Habits.Add(h);
        await db.SaveChangesAsync();
        for (int d = 1; d <= 5; d++)
            db.HabitTrackings.Add(new HabitTracking { UserId = 1, HabitId = h.Id, IsCompleted = true,
                TrackingDate = new DateTime(2026, 06, 1 + d), CompletedAt = new DateTime(2026, 06, 1 + d, 8, 0, 0) });
        await db.SaveChangesAsync();

        var builder = NewBuilder(db);
        var window = CoachWindow.ForMonth("2026-06", new DateTime(2026, 06, 14));
        var sheet = await builder.BuildAsync(1, window, CancellationToken.None);

        sheet.EnoughForReport.Should().BeTrue();
        sheet.TotalHabitCount.Should().Be(1);
        sheet.WindowLabel.Should().Be("June 2026");
        sheet.BestHabit.Should().Be("Gym");
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~CoachFactSheetBuilderTests"`
Expected: FAIL — `CoachFactSheetBuilder` does not exist.

- [ ] **Step 4: Implement the builder**

```csharp
using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Utils;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Services
{
    public interface ICoachFactSheetBuilder
    {
        Task<CoachFactSheet> BuildAsync(int userId, CoachWindow window, CancellationToken ct);
    }

    public class CoachFactSheetBuilder : ICoachFactSheetBuilder
    {
        private readonly IWeeklyReportService _report;
        private readonly IInsightService _insight;
        private readonly AppDbContext _db;
        private readonly ILogger<CoachFactSheetBuilder> _logger;

        public CoachFactSheetBuilder(IWeeklyReportService report, IInsightService insight,
            AppDbContext db, ILogger<CoachFactSheetBuilder> logger)
        {
            _report = report; _insight = insight; _db = db; _logger = logger;
        }

        public async Task<CoachFactSheet> BuildAsync(int userId, CoachWindow window, CancellationToken ct)
        {
            var stats = await _report.ComputeWindowStatsAsync(userId, window.From, window.To, ct);
            var facts = await _insight.ComputeWindowFactsAsync(userId, window.From, window.To, ct);
            var identity = await ResolveIdentityAsync(userId, ct);

            var sheet = new CoachFactSheet
            {
                WindowLabel = window.Label,
                From = window.From.ToString("yyyy-MM-dd"),
                To = window.To.ToString("yyyy-MM-dd"),
                PerformanceScore = stats.PerformanceScore,
                ScoreBand = stats.ScoreBand,
                ConsistencyDelta = stats.ConsistencyDelta,
                CompletionRate = stats.CompletionRate,
                BestHabit = stats.BestHabit,
                BestHabitRate = stats.BestHabitRate,
                WorstHabit = stats.WorstHabit,
                WorstHabitRate = stats.WorstHabitRate,
                TopSkipReason = facts.TopSkipReason?.Reason,
                TopSkipReasonPct = facts.TopSkipReason?.Pct,
                MostSkippedHabit = facts.MostSkippedHabit?.Name,
                MostSkippedHabitCount = facts.MostSkippedHabit?.Skips,
                CompletionTimeBucket = facts.CompletionTimeOfDay?.Bucket,
                CompletionTimePct = facts.CompletionTimeOfDay?.Pct,
                WeekdayVsWeekendMoreOn = facts.WeekdayVsWeekend?.MoreOn,
                WeekdayVsWeekendRatio = facts.WeekdayVsWeekend?.Ratio,
                IdentityTitle = identity.Title,
                IdentitySource = identity.Source,
                GoalLinkedHabitCount = stats.GoalLinkedHabitCount,
                TotalHabitCount = stats.TotalHabitCount,
            };

            // Enough to coach on: at least one habit AND (a real score OR enough skip data).
            sheet.EnoughForReport = stats.TotalHabitCount > 0
                && (stats.PerformanceScore > 0 || facts.EnoughForSkipPatterns);
            return sheet;
        }

        private async Task<(string? Title, string? Source)> ResolveIdentityAsync(int userId, CancellationToken ct)
        {
            // Prefer a Vision title, else the most recent Goal title. Mirrors the
            // identity-resolution chain used by the goal-habit motivation redesign.
            var vision = await _db.Visions.Where(v => v.UserId == userId)
                .OrderByDescending(v => v.CreatedAt).Select(v => v.Title).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrEmpty(vision)) return (vision, "Vision");

            var goal = await _db.Goals.Where(g => g.UserId == userId)
                .OrderByDescending(g => g.CreatedAt).Select(g => g.Title).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrEmpty(goal)) return (goal, "Goal");

            return (null, null);
        }
    }
}
```

> Adjust the `InsightWindowFacts` member access (`.Reason`/`.Pct` etc.) to match whatever shape you used in Task 6 (tuple-record vs class). Confirm `_db.Visions` / `_db.Goals` DbSet names and `Vision.Title` / `Goal.Title` / `CreatedAt` against `AppDbContext` + the entity files.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~CoachFactSheetBuilderTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/Models/DTO/CoachFactSheet.cs server/AtomicHabits/Services/CoachFactSheetBuilder.cs server/AtomicHabits.Tests/Services/CoachFactSheetBuilderTests.cs
git commit -m "feat: add CoachFactSheet DTO + builder with tests"
```

---

## Task 8: CoachNarrative DTO + templated fallback (TDD)

**Files:**
- Create: `server/AtomicHabits/Models/DTO/CoachNarrative.cs`
- Create: `server/AtomicHabits/Services/CoachFallback.cs`
- Test: `server/AtomicHabits.Tests/Services/CoachFallbackTests.cs`

- [ ] **Step 1: Create the narrative DTO**

```csharp
using System.Collections.Generic;

namespace AtomicHabits.Models.DTO
{
    // Claude's structured output AND the fallback's output share this shape.
    public class CoachNarrative
    {
        public string Summary { get; set; } = string.Empty;
        public List<string> Patterns { get; set; } = new();
        public List<string> Actions { get; set; } = new();
    }
}
```

- [ ] **Step 2: Write the failing test**

```csharp
using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class CoachFallbackTests
{
    [Fact]
    public void Builds_three_parts_from_fact_sheet()
    {
        var sheet = new CoachFactSheet
        {
            WindowLabel = "the last 2 weeks",
            PerformanceScore = 62, ScoreBand = "Building", ConsistencyDelta = -8,
            BestHabit = "Reading", WorstHabit = "Gym", WorstHabitRate = 0.3,
            TopSkipReason = "Low Energy", TopSkipReasonPct = 41,
            EnoughForReport = true
        };
        var n = CoachFallback.Build(sheet);
        n.Summary.Should().Contain("62").And.Contain("Building");
        n.Patterns.Should().NotBeEmpty();
        n.Actions.Should().NotBeEmpty();
        // grounded only in provided facts — no invented habit names
        n.Patterns.Should().Contain(p => p.Contains("Low Energy"));
        n.Actions.Should().Contain(a => a.Contains("Gym"));
    }
}
```

- [ ] **Step 3: Implement the fallback**

```csharp
using AtomicHabits.Models.DTO;

namespace AtomicHabits.Services
{
    // Deterministic templated narrative built ONLY from the fact sheet — used when Claude
    // errors or no API key is configured. Same 3-part shape as the Claude output.
    public static class CoachFallback
    {
        public static CoachNarrative Build(CoachFactSheet s)
        {
            var n = new CoachNarrative
            {
                Summary = $"Over {s.WindowLabel} you scored {s.PerformanceScore}/100 ({s.ScoreBand})"
                          + (s.ConsistencyDelta != 0
                              ? $", {(s.ConsistencyDelta > 0 ? "up" : "down")} {System.Math.Abs(s.ConsistencyDelta)} points vs the prior period."
                              : ".")
            };

            if (s.TopSkipReason != null && s.TopSkipReasonPct != null)
                n.Patterns.Add($"Your most common reason for skipping was {s.TopSkipReason} ({s.TopSkipReasonPct}% of skips).");
            if (s.CompletionTimeBucket != null && s.CompletionTimePct != null)
                n.Patterns.Add($"You completed most habits in the {s.CompletionTimeBucket} ({s.CompletionTimePct}%).");
            if (s.WeekdayVsWeekendMoreOn != null && s.WeekdayVsWeekendRatio != null)
                n.Patterns.Add($"You were {s.WeekdayVsWeekendRatio}× more consistent on {s.WeekdayVsWeekendMoreOn}s.");
            if (n.Patterns.Count == 0)
                n.Patterns.Add("Keep logging — a clearer pattern will emerge as more data comes in.");

            if (s.WorstHabit != null && s.WorstHabitRate != null)
                n.Actions.Add($"Focus next period on {s.WorstHabit} — your lowest completion at {(int)System.Math.Round(s.WorstHabitRate.Value * 100)}%.");
            if (s.TopSkipReason != null)
                n.Actions.Add($"Plan around \"{s.TopSkipReason}\" — schedule those habits when you have the most energy.");
            if (n.Actions.Count == 0)
                n.Actions.Add("Pick one habit to protect this week and build from there.");

            return n;
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~CoachFallbackTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Models/DTO/CoachNarrative.cs server/AtomicHabits/Services/CoachFallback.cs server/AtomicHabits.Tests/Services/CoachFallbackTests.cs
git commit -m "feat: add CoachNarrative DTO + templated fallback with tests"
```

---

## Task 9: IClaudeCoachGateway + ClaudeCoachGateway

The gateway is the only unit that calls Claude. Behind an interface so `CoachService` is testable with a fake. The live impl uses the official `Anthropic` SDK with `claude-sonnet-4-6` and structured output.

**Files:**
- Modify: `server/AtomicHabits/AtomicHabits.csproj` (add package)
- Create: `server/AtomicHabits/Services/IClaudeCoachGateway.cs`
- Create: `server/AtomicHabits/Services/ClaudeCoachGateway.cs`
- Test: `server/AtomicHabits.Tests/Services/ClaudeCoachGatewayTests.cs`

- [ ] **Step 1: Add the Anthropic SDK package**

Run: `dotnet add server/AtomicHabits package Anthropic`
Expected: package added to `AtomicHabits.csproj`.

- [ ] **Step 2: Define the interface**

```csharp
using AtomicHabits.Models.DTO;

namespace AtomicHabits.Services
{
    public interface IClaudeCoachGateway
    {
        // Returns the model id used, or null on any failure (caller falls back to templated).
        Task<(CoachNarrative? Narrative, string Model)> GenerateAsync(CoachFactSheet sheet, CancellationToken ct);
    }
}
```

- [ ] **Step 3: Write the failing test (prompt assembly + JSON mapping, no network)**

`ClaudeCoachGateway` is split so the prompt assembly and response parsing are testable without a live call. Expose two internal static helpers and test those.

```csharp
using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class ClaudeCoachGatewayTests
{
    [Fact]
    public void Prompt_includes_window_label_and_facts()
    {
        var sheet = new CoachFactSheet { WindowLabel = "the last 2 weeks", PerformanceScore = 62,
            ScoreBand = "Building", TopSkipReason = "Low Energy", TopSkipReasonPct = 41 };
        var prompt = ClaudeCoachGateway.BuildUserPrompt(sheet);
        prompt.Should().Contain("the last 2 weeks").And.Contain("62").And.Contain("Low Energy");
    }

    [Fact]
    public void Parses_structured_json_into_narrative()
    {
        var json = "{\"summary\":\"You did well.\",\"patterns\":[\"P1\",\"P2\"],\"actions\":[\"A1\"]}";
        var n = ClaudeCoachGateway.ParseNarrative(json);
        n.Should().NotBeNull();
        n!.Summary.Should().Be("You did well.");
        n.Patterns.Should().HaveCount(2);
        n.Actions.Should().ContainSingle().Which.Should().Be("A1");
    }

    [Fact]
    public void Parse_returns_null_on_garbage()
    {
        ClaudeCoachGateway.ParseNarrative("not json").Should().BeNull();
    }
}
```

- [ ] **Step 4: Run to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~ClaudeCoachGatewayTests"`
Expected: FAIL — `ClaudeCoachGateway` does not exist.

- [ ] **Step 5: Implement the gateway**

```csharp
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using AtomicHabits.Config;
using AtomicHabits.Models.DTO;
using Microsoft.Extensions.Options;

namespace AtomicHabits.Services
{
    public class ClaudeCoachGateway : IClaudeCoachGateway
    {
        private readonly CoachOptions _opts;
        private readonly ILogger<ClaudeCoachGateway> _logger;

        public ClaudeCoachGateway(IOptions<CoachOptions> opts, ILogger<ClaudeCoachGateway> logger)
        {
            _opts = opts.Value; _logger = logger;
        }

        private const string SystemPrompt =
            "You are a supportive but direct performance coach inside a habit app. " +
            "You are given VERIFIED statistics about a user's habits for a time window. " +
            "Every number you state MUST come from the provided facts — never invent or estimate. " +
            "Write three parts: a one-paragraph summary of how they did; 2-3 observed patterns that " +
            "connect the facts (e.g. link a skip reason to a low-completion habit); and 2-3 concrete, " +
            "specific actions for the next period. Where an identity/goal is provided, frame growth around it. " +
            "Return ONLY the structured fields requested.";

        public static string BuildUserPrompt(CoachFactSheet s) =>
            "Here are the verified facts for " + s.WindowLabel + ":\n" +
            JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });

        public static CoachNarrative? ParseNarrative(string json)
        {
            try
            {
                var n = JsonSerializer.Deserialize<CoachNarrative>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (n == null || string.IsNullOrWhiteSpace(n.Summary)) return null;
                return n;
            }
            catch (JsonException) { return null; }
        }

        public async Task<(CoachNarrative?, string)> GenerateAsync(CoachFactSheet sheet, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_opts.ApiKey))
            {
                _logger.LogWarning("Coach API key not configured; using fallback.");
                return (null, _opts.Model);
            }

            try
            {
                var client = new AnthropicClient { ApiKey = _opts.ApiKey };
                var schema = new Dictionary<string, JsonElement>
                {
                    ["type"] = JsonSerializer.SerializeToElement("object"),
                    ["properties"] = JsonSerializer.SerializeToElement(new
                    {
                        summary = new { type = "string" },
                        patterns = new { type = "array", items = new { type = "string" } },
                        actions = new { type = "array", items = new { type = "string" } }
                    }),
                    ["required"] = JsonSerializer.SerializeToElement(new[] { "summary", "patterns", "actions" }),
                    ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
                };

                var parameters = new MessageCreateParams
                {
                    Model = _opts.Model,           // "claude-sonnet-4-6"
                    MaxTokens = 1024,
                    System = SystemPrompt,
                    OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } },
                    Messages = [new() { Role = Role.User, Content = BuildUserPrompt(sheet) }]
                };

                var response = await client.Messages.Create(parameters);
                var text = response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text;
                if (string.IsNullOrEmpty(text)) return (null, _opts.Model);
                return (ParseNarrative(text), _opts.Model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Claude coach generation failed; using fallback.");
                return (null, _opts.Model);
            }
        }
    }
}
```

> The `Anthropic` SDK surface (`AnthropicClient`, `MessageCreateParams`, `OutputConfig`, `JsonOutputFormat`, `TextBlock`, `Role`) is from the official C# SDK. If a symbol name differs in the installed package version, check the SDK's `Models/Messages` namespace and adjust — the shapes (model id string, structured-output schema dict, text-block unwrap) are stable. Keep the two static helpers exactly as named (`BuildUserPrompt`, `ParseNarrative`) so the tests pass.

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~ClaudeCoachGatewayTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add server/AtomicHabits/AtomicHabits.csproj server/AtomicHabits/Services/IClaudeCoachGateway.cs server/AtomicHabits/Services/ClaudeCoachGateway.cs server/AtomicHabits.Tests/Services/ClaudeCoachGatewayTests.cs
git commit -m "feat: add ClaudeCoachGateway (Anthropic SDK, structured output) with tests"
```

---

## Task 10: CoachReportDto + CoachService (TDD)

Orchestration: cache → rate-limit → fact sheet → gateway → fallback → persist. Tested with a **fake gateway** — no network, no key.

**Files:**
- Create: `server/AtomicHabits/Models/DTO/CoachReportDto.cs`
- Create: `server/AtomicHabits/Services/CoachService.cs`
- Test: `server/AtomicHabits.Tests/Services/CoachServiceTests.cs`

- [ ] **Step 1: Create the response DTO**

```csharp
using System.Collections.Generic;

namespace AtomicHabits.Models.DTO
{
    public class CoachReportDto
    {
        public string WindowLabel { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<string> Patterns { get; set; } = new();
        public List<string> Actions { get; set; } = new();
        public string GeneratedAt { get; set; } = string.Empty; // ISO-8601
        public string Model { get; set; } = string.Empty;
        public bool IsFallback { get; set; }
        public bool FromCache { get; set; }
        public bool EnoughData { get; set; } = true;            // false → "keep logging" message
        public string? Message { get; set; }                     // set when EnoughData == false
    }
}
```

- [ ] **Step 2: Write the failing tests (fake gateway)**

```csharp
using System;
using System.Linq;
using System.Threading;
using AtomicHabits.Config;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class CoachServiceTests
{
    private sealed class FakeGateway : IClaudeCoachGateway
    {
        public int Calls;
        public CoachNarrative? ToReturn = new() { Summary = "AI summary", Patterns = { "p" }, Actions = { "a" } };
        public Task<(CoachNarrative?, string)> GenerateAsync(CoachFactSheet sheet, CancellationToken ct)
        { Calls++; return Task.FromResult((ToReturn, "claude-sonnet-4-6")); }
    }

    private static CoachService NewService(AtomicHabits.Data.AppDbContext db, IClaudeCoachGateway gw, CoachOptions opts)
    {
        var builder = new CoachFactSheetBuilder(
            new WeeklyReportService(db, NullLogger<WeeklyReportService>.Instance),
            new InsightService(db, NullLogger<InsightService>.Instance),
            db, NullLogger<CoachFactSheetBuilder>.Instance);
        return new CoachService(db, builder, gw, Options.Create(opts), NullLogger<CoachService>.Instance);
    }

    private static async Task SeedActiveHabit(AtomicHabits.Data.AppDbContext db)
    {
        var h = new Habit { UserId = 1, Name = "Gym", Frequency = "Daily", GoalFrequency = "daily" };
        db.Habits.Add(h);
        await db.SaveChangesAsync();
        for (int d = 1; d <= 5; d++)
            db.HabitTrackings.Add(new HabitTracking { UserId = 1, HabitId = h.Id, IsCompleted = true,
                TrackingDate = DateTime.UtcNow.Date.AddDays(-d), CompletedAt = DateTime.UtcNow.Date.AddDays(-d).AddHours(8) });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Insufficient_data_skips_gateway_and_returns_keep_logging()
    {
        using var sqlite = new SqliteTestDb();
        var gw = new FakeGateway();
        var svc = NewService(sqlite.Context, gw, new CoachOptions());
        var dto = await svc.GetReportAsync(1, CoachWindow.ForWeeks(2, DateTime.UtcNow), CancellationToken.None);
        dto.EnoughData.Should().BeFalse();
        dto.Message.Should().NotBeNullOrEmpty();
        gw.Calls.Should().Be(0); // no tokens spent
    }

    [Fact]
    public async Task Generates_persists_and_caches()
    {
        using var sqlite = new SqliteTestDb();
        var db = sqlite.Context;
        await SeedActiveHabit(db);
        var gw = new FakeGateway();
        var svc = NewService(db, gw, new CoachOptions { CacheTtlHours = 24, MaxGenerationsPerDay = 10 });
        var window = CoachWindow.ForWeeks(2, DateTime.UtcNow);

        var first = await svc.GetReportAsync(1, window, CancellationToken.None);
        first.Summary.Should().Be("AI summary");
        first.FromCache.Should().BeFalse();
        gw.Calls.Should().Be(1);
        db.AiCoachReports.Count().Should().Be(1);

        var second = await svc.GetReportAsync(1, window, CancellationToken.None);
        second.FromCache.Should().BeTrue();
        gw.Calls.Should().Be(1); // served from cache, no new call
    }

    [Fact]
    public async Task Gateway_failure_uses_fallback_and_flags_it()
    {
        using var sqlite = new SqliteTestDb();
        var db = sqlite.Context;
        await SeedActiveHabit(db);
        var gw = new FakeGateway { ToReturn = null }; // simulate Claude error
        var svc = NewService(db, gw, new CoachOptions { CacheTtlHours = 24 });
        var dto = await svc.GetReportAsync(1, CoachWindow.ForWeeks(2, DateTime.UtcNow), CancellationToken.None);
        dto.IsFallback.Should().BeTrue();
        dto.Summary.Should().NotBeNullOrEmpty(); // user still gets something
    }

    [Fact]
    public async Task Rate_limit_blocks_when_cap_reached()
    {
        using var sqlite = new SqliteTestDb();
        var db = sqlite.Context;
        await SeedActiveHabit(db);
        var gw = new FakeGateway();
        var svc = NewService(db, gw, new CoachOptions { CacheTtlHours = 0, MaxGenerationsPerDay = 1 });

        await svc.GetReportAsync(1, CoachWindow.ForWeeks(1, DateTime.UtcNow), CancellationToken.None);
        var act = async () => await svc.GetReportAsync(1, CoachWindow.ForWeeks(2, DateTime.UtcNow), CancellationToken.None);
        await act.Should().ThrowAsync<CoachRateLimitException>();
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~CoachServiceTests"`
Expected: FAIL — `CoachService` / `CoachRateLimitException` do not exist.

- [ ] **Step 4: Implement CoachService**

```csharp
using System.Text.Json;
using AtomicHabits.Config;
using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AtomicHabits.Services
{
    public class CoachRateLimitException : Exception
    {
        public CoachRateLimitException(string message) : base(message) { }
    }

    public interface ICoachService
    {
        Task<CoachReportDto> GetReportAsync(int userId, CoachWindow window, CancellationToken ct);
    }

    public class CoachService : ICoachService
    {
        private readonly AppDbContext _db;
        private readonly ICoachFactSheetBuilder _builder;
        private readonly IClaudeCoachGateway _gateway;
        private readonly CoachOptions _opts;
        private readonly ILogger<CoachService> _logger;

        public CoachService(AppDbContext db, ICoachFactSheetBuilder builder, IClaudeCoachGateway gateway,
            IOptions<CoachOptions> opts, ILogger<CoachService> logger)
        {
            _db = db; _builder = builder; _gateway = gateway; _opts = opts.Value; _logger = logger;
        }

        public async Task<CoachReportDto> GetReportAsync(int userId, CoachWindow window, CancellationToken ct)
        {
            // 1. Cache
            if (_opts.CacheTtlHours > 0)
            {
                var cutoff = DateTime.UtcNow.AddHours(-_opts.CacheTtlHours);
                var cached = await _db.AiCoachReports
                    .Where(r => r.UserId == userId && r.WindowKind == window.Kind
                                && r.WindowValue == window.Value && r.CreatedAt >= cutoff)
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync(ct);
                if (cached != null) return ToDto(cached, window, fromCache: true);
            }

            // 2. Rate limit (0 = unlimited)
            if (_opts.MaxGenerationsPerDay > 0)
            {
                var since = DateTime.UtcNow.Date;
                int today = await _db.AiCoachReports.CountAsync(
                    r => r.UserId == userId && r.CreatedAt >= since, ct);
                if (today >= _opts.MaxGenerationsPerDay)
                    throw new CoachRateLimitException("Daily coaching limit reached.");
            }

            // 3. Fact sheet
            var sheet = await _builder.BuildAsync(userId, window, ct);
            if (!sheet.EnoughForReport)
                return new CoachReportDto
                {
                    WindowLabel = window.Label, From = sheet.From, To = sheet.To,
                    EnoughData = false,
                    Message = "Keep logging — your first coaching review unlocks once there's enough data."
                };

            // 4. Generate (Claude) → fallback on null
            var (narrative, model) = await _gateway.GenerateAsync(sheet, ct);
            bool isFallback = narrative == null;
            narrative ??= CoachFallback.Build(sheet);

            // 5. Persist (cache + ledger + history)
            var row = new AiCoachReport
            {
                UserId = userId,
                WindowKind = window.Kind,
                WindowValue = window.Value,
                RangeStart = window.From,
                RangeEnd = window.To,
                Summary = narrative.Summary,
                PatternsJson = JsonSerializer.Serialize(narrative.Patterns),
                ActionsJson = JsonSerializer.Serialize(narrative.Actions),
                Model = model,
                IsFallback = isFallback,
                CreatedAt = DateTime.UtcNow
            };
            _db.AiCoachReports.Add(row);
            await _db.SaveChangesAsync(ct);

            return ToDto(row, window, fromCache: false);
        }

        private static CoachReportDto ToDto(AiCoachReport r, CoachWindow window, bool fromCache) => new()
        {
            WindowLabel = window.Label,
            From = r.RangeStart.ToString("yyyy-MM-dd"),
            To = r.RangeEnd.ToString("yyyy-MM-dd"),
            Summary = r.Summary,
            Patterns = JsonSerializer.Deserialize<List<string>>(r.PatternsJson) ?? new(),
            Actions = JsonSerializer.Deserialize<List<string>>(r.ActionsJson) ?? new(),
            GeneratedAt = r.CreatedAt.ToString("o"),
            Model = r.Model,
            IsFallback = r.IsFallback,
            FromCache = fromCache,
            EnoughData = true
        };
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~CoachServiceTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/Models/DTO/CoachReportDto.cs server/AtomicHabits/Services/CoachService.cs server/AtomicHabits.Tests/Services/CoachServiceTests.cs
git commit -m "feat: add CoachService orchestration (cache, rate-limit, fallback) with tests"
```

---

## Task 11: CoachController + DI wiring

**Files:**
- Create: `server/AtomicHabits/Controllers/CoachController.cs`
- Modify: `server/AtomicHabits/Program.cs`
- Modify: `server/AtomicHabits/appsettings.json`, `server/AtomicHabits/appsettings.Development.json`

- [ ] **Step 1: Create the controller**

```csharp
using AtomicHabits.Authorization;
using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace AtomicHabits.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [RequiresActiveSubscription] // PAID — Pro+Active only; Dev set-plan bridge flips you to Pro for testing
    public class CoachController : ControllerBase
    {
        private readonly ICoachService _service;
        public CoachController(ICoachService service) => _service = service;

        // GET /api/Coach?weeks=2   or   GET /api/Coach?month=2026-06
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] int? weeks, [FromQuery] string? month, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            CoachWindow window;
            try
            {
                window = !string.IsNullOrEmpty(month)
                    ? CoachWindow.ForMonth(month, DateTime.UtcNow)
                    : CoachWindow.ForWeeks(weeks ?? 1, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException)
            {
                return BadRequest(new ApiResponse { IsSuccess = false,
                    StatusCode = HttpStatusCode.BadRequest, Result = "Invalid window. Use weeks=1|2|3 or month=YYYY-MM." });
            }

            try
            {
                var dto = await _service.GetReportAsync(userId.Value, window, ct);
                return Ok(new ApiResponse { IsSuccess = true, StatusCode = HttpStatusCode.OK, Result = dto });
            }
            catch (CoachRateLimitException ex)
            {
                return StatusCode((int)HttpStatusCode.TooManyRequests,
                    new ApiResponse { IsSuccess = false, StatusCode = HttpStatusCode.TooManyRequests, Result = ex.Message });
            }
        }
    }
}
```

> Confirm `User.GetUserId()` and `ApiResponse` are imported the way `ReportController` does (they are in `AtomicHabits.Utils` / `AtomicHabits.Models`). Match `ReportController`'s usings.

- [ ] **Step 2: Register DI + options in Program.cs**

In `server/AtomicHabits/Program.cs`, next to the existing `AddScoped<IWeeklyReportService...>` block (around line 63-67), add:

```csharp
builder.Services.AddScoped<ICoachFactSheetBuilder, CoachFactSheetBuilder>();
builder.Services.AddScoped<IClaudeCoachGateway, ClaudeCoachGateway>();
builder.Services.AddScoped<ICoachService, CoachService>();
```

Next to the `Configure<StripeOptions>` line (around line 102), add:

```csharp
builder.Services.Configure<CoachOptions>(builder.Configuration.GetSection(CoachOptions.SectionName));
```

Add `using AtomicHabits.Config;` if not already present.

- [ ] **Step 3: Add config sections**

In `server/AtomicHabits/appsettings.json`:

```json
"Coach": {
  "Model": "claude-sonnet-4-6",
  "CacheTtlHours": 24,
  "MaxGenerationsPerDay": 10,
  "ApiKey": ""
}
```

In `server/AtomicHabits/appsettings.Development.json` (relax throttling for testing):

```json
"Coach": {
  "CacheTtlHours": 0,
  "MaxGenerationsPerDay": 0
}
```

Set the real key out-of-band via user-secrets (do NOT commit it):
```bash
dotnet user-secrets set "Coach:ApiKey" "sk-ant-..." --project server/AtomicHabits
```

- [ ] **Step 4: Build + run the full backend test suite**

Run: `dotnet build server/AtomicHabits`
Expected: Build succeeded.
Run: `dotnet test server/AtomicHabits.Tests`
Expected: PASS — all prior tests plus the new coach tests green.

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Controllers/CoachController.cs server/AtomicHabits/Program.cs server/AtomicHabits/appsettings.json server/AtomicHabits/appsettings.Development.json
git commit -m "feat: add CoachController + DI wiring for AI Coach"
```

---

## Task 12: Frontend DashboardAiCoach card

Mirrors the existing `DashboardInsights` / `DashboardWeeklyReport` cards, behind `<RequirePro>`.

**Files:**
- Create: `client-ui/src/views/dashboard/DashboardAiCoach.jsx`
- Modify: the dashboard compose file that renders `DashboardInsights` (find it first).

- [ ] **Step 1: Find how the existing paid cards are rendered**

Run: `grep -rn "DashboardInsights\|DashboardWeeklyReport\|RequirePro" client-ui/src`
Expected: locate the dashboard file that imports those cards and the `RequirePro` wrapper + the API client usage. Read that file and one existing card (`DashboardWeeklyReport.jsx`) to match the data-fetch pattern, MUI components, and styling.

- [ ] **Step 2: Create the card**

Write `client-ui/src/views/dashboard/DashboardAiCoach.jsx` following the pattern observed in Step 1. It must:
- Render inside `<RequirePro fallback={...upgrade prompt...}>`.
- Show a window selector (1 week / 2 weeks / 3 weeks / this month) — buttons or a select.
- Fetch `GET /api/Coach?weeks=N` (or `?month=YYYY-MM`) via the same API client the other cards use.
- Render the three sections (Summary paragraph; Patterns list; Actions list) as distinct blocks.
- Show a subtle "generated from your stats" note when `isFallback` is true.
- Show the `message` text (not the three sections) when `enoughData` is false.
- Have a "Refresh" affordance.

Match the exact MUI imports, card chrome, and loading/error handling of `DashboardWeeklyReport.jsx` — do not invent a new visual style.

- [ ] **Step 3: Mount the card on the dashboard**

In the dashboard compose file from Step 1, import `DashboardAiCoach` and place it in the layout next to `DashboardWeeklyReport` (the changelog notes Insights + This Week share a full-width row; add the Coach card in a sensible adjacent slot — match the existing Grid `size` usage).

- [ ] **Step 4: Run the frontend build + start the app to verify it renders**

Run: `cd client-ui && npm run build`
Expected: build succeeds with no errors referencing the new file.

Then start both servers (per the project's run pattern) and verify manually:
1. As a Free user, the card shows the upgrade prompt.
2. Flip to Pro via the Dev bridge: `POST /api/Subscription/set-plan` with body `{ "Plan": "Pro" }`.
3. Reload — the card now fetches and renders summary/patterns/actions for the selected window.
4. Switch windows (1/2/3 weeks, month) and confirm the content updates.

- [ ] **Step 5: Commit**

```bash
git add client-ui/src/views/dashboard/DashboardAiCoach.jsx <dashboard-compose-file>
git commit -m "feat: add DashboardAiCoach card behind RequirePro"
```

---

## Task 13: Monthly background generation

Reuse the existing background-service pattern (`ReminderDispatcherService`) to pre-generate each Pro user's monthly report so it's cached and ready. In-app only (no email in v1).

**Files:**
- Create: `server/AtomicHabits/Scheduling/MonthlyCoachService.cs`
- Modify: `server/AtomicHabits/Program.cs` (register the hosted service)
- Test: `server/AtomicHabits.Tests/Services/MonthlyCoachServiceTests.cs` (logic-only)

- [ ] **Step 1: Read the existing background service for the pattern**

Run: `cat server/AtomicHabits/Scheduling/ReminderDispatcherService.cs`
Expected: see how it derives `BackgroundService`, uses an `IServiceScopeFactory` to resolve scoped services, and its timer/loop shape. Match it.

- [ ] **Step 2: Write a failing test for the selection logic**

The schedulable surface that's worth testing is "which users get a monthly report and for which month." Extract that into a static/pure helper and test it.

```csharp
using System;
using AtomicHabits.Scheduling;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class MonthlyCoachServiceTests
{
    [Fact]
    public void Target_month_is_the_just_completed_month()
    {
        // On 2026-07-01, the monthly run should target June 2026.
        MonthlyCoachService.TargetMonthValue(new DateTime(2026, 07, 01)).Should().Be("2026-06");
    }

    [Fact]
    public void Target_month_handles_january_rollover()
    {
        MonthlyCoachService.TargetMonthValue(new DateTime(2026, 01, 02)).Should().Be("2025-12");
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~MonthlyCoachServiceTests"`
Expected: FAIL — `MonthlyCoachService` does not exist.

- [ ] **Step 4: Implement the hosted service**

```csharp
using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Scheduling
{
    // Once a day, on the 1st of the month, generate the just-completed month's coach
    // report for each Pro+Active user so it's cached. In-app only; email is a fast-follow.
    public class MonthlyCoachService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MonthlyCoachService> _logger;

        public MonthlyCoachService(IServiceScopeFactory scopeFactory, ILogger<MonthlyCoachService> logger)
        {
            _scopeFactory = scopeFactory; _logger = logger;
        }

        // The month just completed relative to `now` (e.g. 2026-07-xx → "2026-06").
        public static string TargetMonthValue(DateTime now)
        {
            var firstOfThisMonth = new DateTime(now.Year, now.Month, 1);
            var lastMonth = firstOfThisMonth.AddMonths(-1);
            return lastMonth.ToString("yyyy-MM");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    if (now.Day == 1)   // run on the 1st
                        await GenerateForAllProUsersAsync(TargetMonthValue(now), now, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Monthly coach generation tick failed.");
                }
                // check once per day
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        private async Task GenerateForAllProUsersAsync(string month, DateTime now, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var coach = scope.ServiceProvider.GetRequiredService<ICoachService>();

            var proUserIds = await db.Users
                .Where(u => u.PlanTier == PlanTier.Pro && u.SubscriptionStatus == SubscriptionStatus.Active)
                .Select(u => u.Id)
                .ToListAsync(ct);

            var window = CoachWindow.ForMonth(month, now);
            foreach (var userId in proUserIds)
            {
                try { await coach.GetReportAsync(userId, window, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Monthly coach gen failed for user {UserId}.", userId); }
            }
        }
    }
}
```

> Confirm `PlanTier.Pro` / `SubscriptionStatus.Active` enum names and the `User` DbSet (`db.Users`) — they're used by `SubscriptionController`, so they exist; match the spelling.

- [ ] **Step 5: Register the hosted service in Program.cs**

Next to where `ReminderDispatcherService` is registered (search `AddHostedService`), add:

```csharp
builder.Services.AddHostedService<MonthlyCoachService>();
```

- [ ] **Step 6: Run the test + full suite**

Run: `dotnet test server/AtomicHabits.Tests --filter "FullyQualifiedName~MonthlyCoachServiceTests"`
Expected: PASS (2 tests).
Run: `dotnet test server/AtomicHabits.Tests`
Expected: full suite green.

- [ ] **Step 7: Commit**

```bash
git add server/AtomicHabits/Scheduling/MonthlyCoachService.cs server/AtomicHabits/Program.cs server/AtomicHabits.Tests/Services/MonthlyCoachServiceTests.cs
git commit -m "feat: add MonthlyCoachService background generation with tests"
```

---

## Task 14: Final verification

- [ ] **Step 1: Full backend build + test**

Run: `dotnet build server/AtomicHabits && dotnet test server/AtomicHabits.Tests`
Expected: Build succeeded; all tests pass (the existing ~106 plus the new coach tests).

- [ ] **Step 2: Frontend build**

Run: `cd client-ui && npm run build`
Expected: succeeds.

- [ ] **Step 3: Manual end-to-end (Dev bypass, no Stripe)**

1. Start API + UI.
2. Register/log in; `POST /api/Subscription/set-plan` `{ "Plan": "Pro" }`.
3. Seed some habits + a few completions + a couple of skips.
4. Open the dashboard → AI Coach card → select "2 weeks" → confirm a summary/patterns/actions render.
   - With a real `Coach:ApiKey` set, output is Claude-written.
   - With no key set, output is the templated fallback (card subtly notes it) — proves graceful degradation.
5. Verify a Free user (set-plan `{ "Plan": "Free" }`) sees the upgrade prompt instead.

- [ ] **Step 4: Confirm no secret committed**

Run: `git log -p -1 | grep -i "sk-ant" || echo "no key in last commit"`
Expected: "no key in last commit". The API key lives only in user-secrets.
