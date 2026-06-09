# Momentum — Features (v1 SaaS Build)

> What was built across Plans 1–7: the pivot from a habit tracker into an
> **AI-Powered Personal Performance & Career Growth OS**, monetized as a SaaS
> subscription. Each plan was spec'd → planned → built test-first (fresh
> subagents, two-stage spec + code-quality review) → merged.
>
> Design specs live in `docs/superpowers/specs/`, plans in `docs/superpowers/plans/`.

---

## The repositioning

From **"another habit tracker"** to **"a system for who you're becoming."** Habits
connect to a Vision → Goal → Milestone hierarchy; the free tier proves the idea,
the paid tier sells the intelligence (insights + weekly report) behind a real
Stripe paywall.

```
Vision ("Become a Remote Backend Engineer")
  └── Goal ("Get a remote job in 2026")
        └── Milestone ("Build a portfolio")
              └── Habit ("Code 1 hour daily")  ← the recurring vehicle
```

---

## Free tier

### Goal→Habit framework (Plan 2)
- Owner-scoped CRUD for **Vision / Goal / Milestone** + a **Goals** page.
- Habits link to a milestone; cards show a **"Contributes to: {goal}"** payoff line.
- Seamless **"+ Add a habit toward this"** flow from a milestone (pre-linked).

### Identity-vote motivation (Goal↔Habit redesign)
- Completing a habit fires an **identity-vote hit**: *"✓ Read 10 pages — A vote for
  Become a Remote Backend Engineer 🗳️"* (Atomic Habits framing).
- Identity resolves up the chain: Vision → else Goal → else Milestone.

### Failure capture (Plan 3)
- **"Skip today"** with one-tap reason chips (Busy / Forgot / Low Energy /
  No Motivation / Schedule Conflict / Other), stored as `HabitSkip`.
- Free for everyone — builds the behavioral dataset the paid insights analyze.

### Dashboard
- Real, live-refreshing summary stats (today / weekly / monthly / health score).
- **GitHub-style contribution heatmap** (green-ascending, full trailing year).
- Habit completion-rate charts (weekly / monthly / yearly).

---

## Paid tier (Pro subscription)

Gated by `[RequiresActiveSubscription]` (backend) + `<RequirePro>` (frontend);
free users see an upgrade prompt in place of the content.

### Failure Analysis Insights (Plan 5) — rules-based, no LLM
A Dashboard panel of ~4 plain-language insights computed from skip + tracking data:
- "Your most common reason for skipping is **Low Energy** (41% of skips)."
- "You complete most habits in the **morning** — 64% of your completions."
- "You complete **2.3×** more habits on weekdays than weekends."
- "**Exercise** is your most-skipped habit (7 skips)."
(Templates omitted until there's enough data.)

### Weekly CEO Report (Plan 6) — in-app
A "This Week" Dashboard card with a curated weekly narrative:
- **Performance Score (0–100)** + band — weighted so goal-linked habits count 1.5×.
- Best / worst habit, consistency delta vs last week, top miss reason, a templated
  "Focus next week" line.
- *(Sunday auto-email deferred to a future fast-follow.)*

---

## Subscription & billing

### Entitlement model (Plan 4)
- `PlanTier` (Free/Pro), `SubscriptionStatus` (None/Active/PastDue/Canceled),
  `CurrentPeriodEnd` on the user — a separate axis from RBAC roles.
- `[RequiresActiveSubscription]` authorization gate (mirrors the existing
  permission system); `/Auth/me` exposes `isPro`; `<RequirePro>` UI wrapper.

### Stripe billing (Plan 7)
- Hosted **Stripe Checkout** to subscribe; **Customer Portal** to manage/cancel.
- A **signature-verified webhook** that keeps entitlement in sync with Stripe
  (activate / update / cancel / payment-failed→PastDue) — fail-closed and idempotent.
- Built behind abstractions so it's fully unit-tested without keys; live
  verification via `docs/superpowers/specs/STRIPE-LIVE-TEST-RUNBOOK.md`.
- A Development-only manual `set-plan` endpoint remains as a dev shortcut.

---

## Foundation & cross-cutting

- **Data model (Plan 1):** Vision/Goal/Milestone/HabitSkip entities + the nullable
  `Habit.MilestoneId` link. FK delete behavior tuned for SQL Server (no
  multiple-cascade-path cycles); account deletion still cascades cleanly.
- **Shared math:** `HabitMath` (expected-sessions, ISO week) and
  `SkipReason.Humanize()` extracted to avoid duplication.
- **Testing:** xUnit backend suite (106 tests as of Plan 7), incl. SQLite-backed
  tests that catch SQL-translation bugs the in-memory provider hides.

## Branding & polish (alongside the plans)
- Renamed the product **Atomic Habits → Momentum** (display text only; namespace/DB
  left as `AtomicHabits` internally).
- Public **landing page** (the validate-first marketing artifact) merged into the SPA at `/`.
- Bug fixes: hardcoded dashboard stats → live data; refresh-only completion checkbox
  → instant; MUI 7 `Grid` `size` migration; crash-proofed summary cards; balanced
  Dashboard layout.

---

## Status & what's next

- **Merged to master (private GitHub repo):** Plans 1–6 + the redesign + all UI fixes.
- **Branch `feat/performance-os-stripe` (unmerged):** Plan 7 — awaiting the live
  Stripe test (runbook), then merge.
- **Optional leftovers (not required for v1):** Weekly Report Sunday auto-email,
  dunning emails on failed payment.

**Net result:** a free tier that proves the repositioning and a paid tier (Insights
+ Weekly Report) behind a real Stripe subscription — the complete v1 SaaS.
