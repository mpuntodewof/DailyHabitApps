# Momentum — Performance OS v1 Design

> Design spec for the first shippable slice of the SaaS repositioning: from
> "habit tracker" to "personal performance system." Brainstormed 2026-06-07.

## Context & Decisions

Momentum is pivoting from a habit-tracking app into an **AI-Powered Personal
Performance & Career Growth Operating System** (see
`docs/momentum-saas-product-positioning&monetizing-roadmap`). This spec covers
**v1** — the foundation slice — and the decisions made during brainstorming:

- **Model:** SaaS subscription (supersedes the earlier one-time-purchase plan).
- **AI in v1:** Rules-based / deterministic insights only — **no LLM**. The v1
  insights ("68% of misses are after 9 PM") are SQL/LINQ aggregations, not AI.
  Genuine Claude-powered coaching is a planned **v2** that will reason over the
  behavioral dataset v1 collects. Positioning note: hold the literal
  "AI-powered" claim (or use "smart insights") until v2 ships real AI.
- **v1 scope:** All three "Phase 1" features from the roadmap doc, each kept
  deliberately thin: Goal→Milestone→Habit Framework, Failure Pattern Analysis,
  Weekly CEO Report.
- **Monetization split:** Free = unlimited habits + the Goal→Habit framework
  (the hook that proves the repositioning). Paid (monthly subscription) =
  Failure Pattern Analysis insights + Weekly CEO Report. The free tier must
  *carry the repositioning*; the paywall sits on the "intelligence."

### Non-goals (explicitly deferred)

- LLM / Claude integration of any kind (v2).
- Career Growth Dashboard, Developer/Freelancer Mode, Accountability Network
  (later phases in the roadmap doc).
- Progress-percentage roll-ups, drag-and-drop reordering, Gantt views, PDF
  export, report customization.
- Renaming the .NET namespace / project / database (internal identifiers).

---

## 1. Data Model

Four new entities sit *above* the existing `Habit`. All are owner-scoped
(JWT-derived `UserId`) following existing model conventions.

```
User 1──* Vision 1──* Goal 1──* Milestone 1──* Habit   (Habit gains optional MilestoneId)
                       └─ Status, TargetDate          Habit also relates to * HabitSkip
```

### New entities

- **`Vision`** — aspirational identity ("Become a Remote Backend Engineer").
  `Id, UserId, Title, Description?, CreatedAt`. Optional; users may skip to Goals.
- **`Goal`** — concrete outcome ("Get a Remote Job").
  `Id, UserId, VisionId?, Title, Status (Active|Achieved|Abandoned), TargetDate?, CreatedAt`.
- **`Milestone`** — a step toward a goal ("Build Portfolio").
  `Id, UserId, GoalId, Title, Status (Active|Done), OrderIndex, CreatedAt`.
- **`HabitSkip`** — failure capture (see §3).
  `Id, UserId, HabitId, Date, Reason, CreatedAt`.

### Change to existing `Habit`

- Add **nullable** `MilestoneId` FK (+ `Milestone? Milestone` nav).
  **Nullable is deliberate:** existing/unlinked habits keep working with no
  breaking migration and no forced reorganization. Linkage is progressive.

### Naming note

The existing `Habit` already has `GoalValue`/`GoalUnit`/`GoalFrequency` — those
are the habit's *target* ("30 minutes"), unrelated to the new top-level `Goal`.
Keep the new entities named `Vision`/`Goal`/`Milestone` to avoid collision.

### Indexes / migration

- Owner indexes: `Goal(UserId)`, `Milestone(GoalId)`, `HabitSkip(UserId, HabitId, Date)`.
- One EF migration adds the four tables + `Habit.MilestoneId`. Additive only —
  no data backfill required.

### Delete behavior (revised during implementation, 2026-06-07)

SQL Server forbids multiple cascade/delete-action paths to the same table, so
the originally-planned `SetNull` on `Habit.MilestoneId` and `Goal.VisionId`
could not be used as-is (they created cycles with the `User→Goal→Milestone→Habit`
and `User→Vision→Goal` cascade chains). The shipped FK delete rules are:

- `Goal.UserId` → Cascade; `Goal.VisionId` → **NoAction**
- `Milestone.UserId` → **NoAction**; `Milestone.GoalId` → Cascade
- `Habit.MilestoneId` → **NoAction** (was SetNull)
- `HabitSkip.UserId` → **NoAction**; `HabitSkip.HabitId` → Cascade

Rationale: keep the `User→Goal`/`User→Habit` cascades intact so **account
deletion still cleans up all child rows** (a GDPR requirement). The cost: the
DB no longer auto-nulls children when a `Vision`/`Milestone` is deleted directly.

**Forward constraint for Plan 2 (CRUD endpoints):** the Goal/Milestone/Vision
**delete** operations MUST, in application code, first null-out or reassign
dependent rows (e.g. set `Habit.MilestoneId = null` for habits under a milestone
being deleted; set `Goal.VisionId = null` for goals under a vision) — otherwise
the delete throws an FK-constraint error. This is now a tested requirement of
those endpoints, not optional.

---

## 2. Goal → Habit Framework (FREE)

The visible, felt core of the repositioning. Three thin pieces.

### a) Goals view
New sidebar entry "Goals". Accordion/tree: Vision → Goals → Milestones → linked
Habits, with create/edit/archive at each level. Reuses existing card/dialog
patterns from the Habit page. **No** roll-up percentages, dependencies, or
drag-and-drop in v1 — just the hierarchy and a simple `OrderIndex`.

### b) Linkage on the habit
The habit create/edit form gains one optional **"Contributes to…"** dropdown
selecting a Milestone. This is the only change to the existing habit form.

### c) The payoff line
On the habit card and the daily check-in, a contextual line walking
Habit→Milestone→Goal→Vision: *"Today's habit contributes to: Remote Backend
Engineer."* This single line is the emotional core of the pivot.

### Backend
`VisionController` / `GoalController` / `MilestoneController` + matching
services + repositories, following the existing Controller→Service→Repository
pattern, all JWT-owner-scoped. CRUD only.

### Frontend
`GoalContext` mirroring `HabitContext`; `views/goals/Goals.jsx`; the dropdown +
payoff line added to existing habit components.

---

## 3. Failure Pattern Analysis (capture FREE, analysis PAID)

### a) Capture — `HabitSkip` (all tiers)
The skip prompt fires in exactly one place in v1: when the user **explicitly
marks a habit as "skip / not done"** for a given day from the habit card or
daily check-in. (v1 does **not** retroactively prompt for days that simply
passed uncompleted — that would require a background sweep and risks nagging;
deferred.) Show a one-tap reason chip:
**Busy · Forgot · Low Energy · No Motivation · Schedule Conflict · Other**.
Persist one `HabitSkip` row. Capture is **free** for everyone — it builds the
dataset the v2 AI will reason over. Frictionless: a single chip row.

### b) Analysis — insights view (PAID)
Deterministic aggregations over `HabitSkip` + `HabitTracking`, rendered as
templated plain-language strings (zero LLM, no hallucination). v1 ships a
**fixed set of ~4 insight templates**:

- Reason breakdown: "Your top miss reason is **Low Energy** (41%)."
- Time-of-day: "**68%** of misses happen after 9 PM." (from tracking timestamps)
- Day-of-week: "You complete gym **85%** on weekdays vs **20%** weekends."
- Per-habit best window: "You're most successful with Reading **before lunch**."

Each template is filled from a query result; if data is insufficient, the
template is omitted rather than shown with empty values.

### Gating
Capture: free. Insights view: gated by active subscription (§5).

---

## 4. Weekly CEO Report (PAID)

A curated weekly narrative (not a dashboard) — "users value reports more than
dashboards." Covers the trailing 7 days; viewable in-app and optionally emailed
every Sunday via the existing `IEmailSender`.

### Contents (assembled from §2/§3 data)
- **Performance Score (0–100)** — from completion rate, weighted so
  goal-linked habits count more (reinforces the repositioning). Formula is
  documented, not a black box: `score = round(100 * (Σ weighted completions) /
  (Σ weighted expected))`, where goal-linked habits carry weight 1.5 and
  unlinked 1.0. (Tunable; recorded here so it's reviewable.)
- **Best / Worst habit** — highest / lowest completion this week.
- **Consistency delta** — this week vs last (e.g. `+12%`).
- **Top miss reason** — from `HabitSkip` (ties the features together).
- **Focus next week** — *templated* suggestion from the worst-performing area
  (rules-based). This is the seam where the v2 Claude-written report slots in.

### Architecture
- `WeeklyReportService` — computes a report for `(user, week)`.
- `ReportController` — `GET /api/Report/weekly?week=` (on-demand, in-app view).
- `WeeklyReport` persisted row for history + email dedupe.
- A `BackgroundService` for Sunday generation + email — **reuses the existing
  `ReminderDispatcherService` pattern** (already this exact shape).

### Scope discipline
One report layout, simple weighted formula, plain templated HTML email (like
the reminder emails). No PDF, no customization in v1. Gated: paid (§5).

---

## 5. Subscription Entitlement & Gating (cross-cutting)

Adapts the planned one-time-Pro entitlement into a **recurring subscription**.
The gating *mechanism* is identical to the existing `[Permission]` system; only
the lifecycle differs (status can lapse).

### Entitlement model
- Subscription state on the user: `PlanTier (Free|Pro)`,
  `SubscriptionStatus (Active|PastDue|Canceled)`, `CurrentPeriodEnd`.
- The gate checks **"active subscription"**, not "ever purchased."
- **Kept separate from RBAC** — plan ≠ role; orthogonal to `User`/`Admin`. The
  existing `[Permission]`/`PermissionAuthorizationHandler` system is untouched.

### Gating mechanism (mirrors existing `[Permission]`)
- `[RequiresActiveSubscription]` attribute + authorization handler, built like
  `PermissionAuthorizationHandler` — checks subscription status, returns 402/403
  when not active. Applied to the insights and report endpoints.
- `/Auth/me` extended with `{ planTier, subscriptionStatus }`.
- Frontend `<RequirePro>` wrapper mirroring `<RequirePermission>`, showing an
  upgrade prompt instead of the locked feature. **Backend stays authoritative.**

### Billing — Stripe subscription
- Stripe **subscription** Checkout (not one-time) + webhook handling
  `customer.subscription.created/updated/deleted` and `invoice.payment_failed`
  to keep `SubscriptionStatus` in sync. Failed-payment/dunning is now real.

### Sequencing (important)
Build and behaviorally test §2–§4 with a **manual `PlanTier` flag** first; wire
Stripe as its **own slice last**, so the product is fully testable before
payment complexity is introduced.

---

## Suggested build order

1. **Data model + migration** (§1) — additive, unblocks everything.
2. **Goal→Habit framework** (§2) — the free hook; prove the repositioning.
3. **Failure capture** `HabitSkip` (§3a) — free, starts collecting data immediately.
4. **Entitlement model + manual flag + `<RequirePro>`** (§5, minus Stripe).
5. **Failure analysis insights** (§3b) — first paid feature, behind the manual flag.
6. **Weekly CEO Report** (§4) — second paid feature + Sunday background job.
7. **Stripe subscription + webhook** (§5 billing) — flip the manual flag to real.

## Pre-public prerequisite (carried from prior work)

Rotate the leaked Gmail app password + JWT key flagged in the Phase 7 changelog
before anything public ships. Hard blocker, independent of this spec.

## Open items for v2 (out of scope here)

- Claude-powered AI Accountability Coach (reasons over `HabitSkip` + tracking).
- Claude-written Weekly CEO Report (replaces the templated "Focus next week").
- Career Growth Dashboard, Developer/Freelancer Mode, Accountability Network.
