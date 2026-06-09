# Weekly CEO Report (v1: in-app) — Design

> Design spec for the second PAID feature: a curated weekly performance report.
> Brainstormed 2026-06-09. Refines §4 of the v1 design doc
> (`2026-06-07-momentum-performance-os-v1-design.md`) and narrows scope to the
> in-app report; the Sunday auto-email is explicitly deferred.

## Context & decisions

A weekly "CEO report" — a curated narrative of the user's week, not a dashboard —
gated behind the Pro subscription (`[RequiresActiveSubscription]` / `<RequirePro>`
from Plan 4). It's the second consumer of that gate, after the Insights panel.

Decisions locked during brainstorming:
1. **v1 = in-app report only.** The Sunday auto-email background job (the spec's
   heavier, SMTP-dependent piece) is DEFERRED to a fast-follow. Lower risk,
   verifiable in dev without working email.
2. **Performance Score = weighted completion rate.** `round(100 × Σ weighted
   completions / Σ weighted expected)` over the trailing 7 days; goal-linked
   habits (`MilestoneId != null`) weighted 1.5×, others 1.0×. Expected sessions
   come from the existing per-habit `ExpectedSessions` helper.
3. **Window = current week + prev-week delta**, computed on demand. NO persistence
   table in v1 (the `WeeklyReport` row + history come with the email job later).
4. **Surface = a "This Week" report card on the Dashboard**, gated by `<RequirePro>`
   — consistent with how the Insights panel lives (one home, no hidden tab).

### Non-goals (v1)
- Sunday auto-email + background `BackgroundService` job (deferred fast-follow).
- `WeeklyReport` persistence table / browsable history.
- LLM-written narrative (the `focusNextWeek` line is templated; it's the seam
  for the v2 Claude version).
- PDF export, report customization, configurable week-start.

---

## Section 1 — `WeeklyReportService` (computation)

A new owner-scoped service computing the current-week report on demand. No new
schema.

### Shared-helper extraction (do this first)
`ExpectedSessions(Habit h, int daysElapsed, int periodLengthDays)` currently lives
as a **private static** in `HabitService` (`HabitService.cs:366`). Extract it into
a shared static helper — `server/AtomicHabits/Utils/HabitMath.cs`,
`public static int ExpectedSessions(...)` — and have `HabitService` call the
shared one (replace its private copy). This avoids duplicating goal-frequency
math across `HabitService` and `WeeklyReportService` (the exact drift the earlier
roadmap consolidation fixed). The extraction must preserve `HabitService`'s
existing behavior (its tests must still pass).

### DTO (`WeeklyReportDto`)
```
performanceScore : int        // 0–100
scoreBand        : string     // "Strong" | "Building" | "Needs work" (from bands)
bestHabit        : string?    // habit name, null if <2 habits or no data
worstHabit       : string?    // habit name, null if <2 habits or no data
consistencyDelta : int        // signed; thisWeekRate − lastWeekRate (percentage points)
topMissReason    : string?    // humanized most-frequent HabitSkip.Reason this week; null if no skips
focusNextWeek    : string?    // templated sentence from the worst area; null if no data
weekStart        : string     // "yyyy-MM-dd" (Monday of the current ISO week)
```

### Computation rules (exact)
- **Window:** current week = Monday→today (ISO week, Monday-start, matching the
  existing `StartOfIsoWeek` helper used in `HabitSummary`). Prior week = the 7
  days before this week's Monday.
- **Performance score:**
  - For each non-archived habit: `weight = MilestoneId != null ? 1.5 : 1.0`.
  - `weightedExpected += weight × ExpectedSessions(habit, daysElapsedThisWeek, 7)`.
  - `weightedCompleted += weight × (completed sessions for that habit this week)`
    (a completed session = a `HabitTracking` with `IsCompleted` and `TrackingDate`
    in the window; cap per-habit completed at its expected so an over-logged habit
    can't push the score >100).
  - `performanceScore = weightedExpected <= 0 ? 0 : round(100 × weightedCompleted / weightedExpected)`, clamped 0–100.
  - **scoreBand:** ≥75 "Strong", 40–74 "Building", <40 "Needs work".
- **best/worst habit:** per-habit completion rate (completed/expected) this week;
  best = max, worst = min. Omitted (null) if fewer than 2 habits have any expected
  sessions this week.
- **consistencyDelta:** `thisWeekRate − lastWeekRate`, each = `round(100 ×
  completed / expected)` over its window. **Deliberately UNWEIGHTED** (plain
  completion rate), unlike the weighted `performanceScore` — the delta is a
  simple week-over-week consistency signal, not a restatement of the score, so it
  uses the straightforward rate. (Documented so the weighted-score /
  unweighted-delta difference is a conscious choice, not an inconsistency.) Signed
  int. If last week had no expected sessions, delta = 0 (avoid a misleading +100).
- **topMissReason:** group this week's `HabitSkip` rows by `Reason`, take the top;
  humanize (`LowEnergy`→"Low Energy", etc., reuse the same map as InsightService —
  consider a shared `SkipReason.Humanize()` extension to avoid a third copy). Null
  if no skips this week.
- **focusNextWeek:** if a worst habit exists, `$"Focus next week: {worstHabit} —
  your lowest completion at {rate}%."` Null if no worst habit.
- **Empties:** any null/omitted field renders as absent in the UI (no empty rows).

### Interface
`IWeeklyReportService.GetCurrentWeekAsync(int userId, CancellationToken ct)` →
`ApiResponse` with `Result = WeeklyReportDto`. Service-direct to `AppDbContext`,
owner-scoped, `ApiResponse` helper style like `InsightService`.

### Tests (`WeeklyReportServiceTests`)
- score reflects weighted completion (goal-linked habit weighted 1.5× shifts the
  score vs an equivalent unlinked habit);
- score is 0 with no habits / no expected sessions (no divide-by-zero);
- score clamped ≤100 even if a habit is over-logged;
- consistencyDelta computes this-vs-last and is 0 when last week had no expected;
- topMissReason picks the most frequent skip reason (humanized); null when no skips;
- best/worst habit identified; omitted with <2 qualifying habits;
- everything owner-scoped (another user's data never bleeds in).
- Extraction safety: existing `HabitService`/`HabitSummary` tests still pass after
  `ExpectedSessions` moves to `HabitMath`.

---

## Section 2 — Gated endpoint + Dashboard "This Week" card

### Backend
- `ReportController`: `[Route("api/[controller]")] [ApiController] [Authorize]
  [RequiresActiveSubscription]`. `GET /api/Report/weekly` →
  `_service.GetCurrentWeekAsync(userId, ct)` → `StatusCode((int)res.StatusCode, res)`.
  Mirrors `InsightController`. Non-Pro users get 403 (the Plan 4 gate).
- DI: `builder.Services.AddScoped<IWeeklyReportService, WeeklyReportService>();`.

### Frontend — `DashboardWeeklyReport.jsx`
A new Dashboard card, gated exactly like `DashboardInsights.jsx`:
- `<RequirePro fallback={<UpgradePanel/>}>` — free users see an inline "This is a
  Pro feature / Upgrade to Pro" prompt (reuse the same set-plan upgrade flow as
  DashboardInsights; Stripe replaces it in Plan 7).
- Pro users: on mount `GET /api/Report/weekly`, render a **report-style** card:
  - Hero: the big **Performance Score** number (0–100) + `scoreBand` label.
  - Supporting lines: Best habit, Worst habit, Consistency delta (signed, with
    up/down arrow + success/error color), Top miss reason, and the "Focus next
    week" sentence. Each line omitted when its field is null.
  - Empty state (Pro but no data yet): "Your first weekly report builds as you log
    this week — check back as the week fills in."
  - Loading spinner while fetching.
- Title: "This Week" (with the week-start date as a subtitle).

### Dashboard placement
Add `DashboardWeeklyReport` to `Dashboard.jsx` alongside the existing panels.
Proposed layout: the **This Week report card** sits in the right column near the
Insights panel (both are the Pro narrative surfaces), above/beside the
contribution heatmap. Exact grid placement is a layout detail for implementation;
keep it visually balanced with the existing TopCards / completion-rate / heatmap.

---

## Affected files (anticipated — plan confirms exact paths)
- Backend create: `Utils/HabitMath.cs` (extracted `ExpectedSessions`),
  `Models/DTO/WeeklyReportDto.cs`, `Services/WeeklyReportService.cs`,
  `Controllers/ReportController.cs`.
- Backend modify: `Services/HabitService.cs` (use the shared `ExpectedSessions`),
  `Program.cs` (DI). Possibly a shared `SkipReason.Humanize()` extension (+ have
  `InsightService` use it) to avoid a third humanize copy.
- Frontend create: `views/dashboard/components/DashboardWeeklyReport.jsx`.
- Frontend modify: `views/dashboard/Dashboard.jsx` (mount the card).
- Tests: `AtomicHabits.Tests/Services/WeeklyReportServiceTests.cs`.

## Deferred fast-follow (separate plan)
Sunday auto-email: a `WeeklyReportDispatcherService : BackgroundService`
(reusing the `ReminderDispatcherService` `PeriodicTimer` + scoped-resolution
pattern) that, once a week, renders each Pro user's report as templated HTML and
sends via `IEmailSender`; a `WeeklyReport` persisted row for history + send-dedupe.
Requires SMTP configured. Out of scope for this spec.

## Subsequent
Plan 7 (Stripe) replaces the manual `set-plan` upgrade flow used by both Pro
Dashboard cards with real billing.
