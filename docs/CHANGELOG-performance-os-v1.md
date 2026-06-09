# Momentum Performance OS v1 — Per-Plan Changelog

> A build log of the v1 SaaS pivot, plan by plan (1→7). Each entry: what it
> delivered, the endpoints / entities / key files, tests added, and links to its
> design spec and implementation plan. For a product-overview by tier, see
> `FEATURES.md`. Tagged release: **`v1.0`**.
>
> Process: every plan was spec'd → planned → built test-first by fresh subagents
> with two-stage review (spec compliance, then code quality), then merged to master.

---

## Plan 1 — Foundation (data model)
*Spec: `docs/superpowers/specs/2026-06-07-momentum-performance-os-v1-design.md` §1 · Plan: `docs/superpowers/plans/2026-06-07-momentum-performance-os-plan1-foundation.md`*

The schema that turns a tracker into a performance system.

- **Entities added:** `Vision`, `Goal` (+`GoalStatus`), `Milestone` (+`MilestoneStatus`), `HabitSkip` (+`SkipReason`); nullable `Habit.MilestoneId` link.
- **Hierarchy:** Vision → Goal → Milestone → Habit.
- **Migration:** `AddPerformanceOsFoundation` (applied to SQL Server).
- **Notable:** review caught SQL-Server **multiple-cascade-path cycles** the in-memory tests couldn't see → FK delete behavior tuned (account deletion still cascades; Vision/Milestone deletes null children in app code).
- **Tests:** 6 model tests (started from a pre-existing harness; ~42 total at this point).

## Plan 2 — Goal→Habit Framework (FREE)
*Spec: …v1-design §2 · Plan: `…-plan2-goal-habit-framework.md`*

The visible hook proving the repositioning.

- **Endpoints:** `GET/POST /api/Vision`, `PUT/DELETE /api/Vision/{visionId}`; same shape for `/api/Goal`; `POST /api/Milestone`, `PUT/DELETE /api/Milestone/{milestoneId}`, `GET /api/Milestone/goal/{goalId}`.
- **Backend:** `GoalFrameworkService` (owner-scoped CRUD); habit↔milestone link + `GET /api/Habit/{id}/contribution` (resolves the Milestone→Goal→Vision chain).
- **Frontend:** `GoalContext`, `Goals.jsx` view (+ `/goals` route + sidebar), "Contributes to…" dropdown on the habit form, "Contributes to: {goal}" payoff line on cards.
- **Tests:** ~55. Review added cross-user mutation tests + defensive owner-scoping on delete child-queries.

## Goal↔Habit Motivation Redesign (mid-build pivot)
*Spec: `docs/superpowers/specs/2026-06-07-goal-habit-motivation-redesign.md` · Plan: `…-goal-habit-motivation-redesign-plan.md`*

Rebuilt the linkage from "bookkeeping" into real motivation.

- **Identity-vote hit on check-in:** "✓ {habit} — A vote for {identity} 🗳️" (Atomic Habits framing), resolving identity up the chain (Vision → else Goal → else Milestone) via `IdentityTitle` on the contribution DTO.
- **Seamless flow:** "+ Add a habit toward this" from a milestone, pre-linked.
- **Tests:** SQLite-backed translation test + the identity resolver tests.

## Plan 3 — Failure Capture (FREE)
*Spec: …v1-design §3a · Plan: `…-plan3-failure-capture.md`*

- **Endpoints:** `POST /api/HabitSkip`, `GET /api/HabitSkip/habit/{habitId}`, `DELETE /api/HabitSkip/{skipId}`.
- **Backend:** `HabitSkipService` — one skip per habit/day (upsert), reason validated against the enum (`Enum.IsDefined`), owner-scoped.
- **Frontend:** "Skip today" menu action + reason-chip dialog (chips send enum **names**, not labels). Free for everyone — builds the dataset Plan 5 analyzes.
- **Tests:** ~71.

## Plan 4 — Subscription Entitlement + `<RequirePro>`
*Spec: …v1-design §5 (minus Stripe) · Plan: `…-plan4-entitlement.md`*

The paywall *mechanism* (no billing yet).

- **Schema:** `PlanTier` (Free/Pro), `SubscriptionStatus` (None/Active/PastDue/Canceled), `CurrentPeriodEnd` on `User` (+ `IsProActive`). Migration `AddSubscriptionToUser`. A separate axis from RBAC.
- **Gate:** `[RequiresActiveSubscription]` attribute + handler, folded into the existing single `PermissionPolicyProvider` (only one `IAuthorizationPolicyProvider` allowed). Authorizes only Pro+Active.
- **Endpoints:** `/api/Auth/me` extended with `planTier`/`subscriptionStatus`/`isPro`; `POST /api/Subscription/set-plan` (a manual dev bridge).
- **Frontend:** `isPro` in `AuthContext`, `<RequirePro fallback=…>` wrapper.
- **Tests:** ~79 (handler covers active/free/pastdue).

## Plan 5 — Failure Analysis Insights (PAID)
*Spec: …v1-design §3b · Plan: `2026-06-09-momentum-performance-os-plan5-insights.md`*

The first paid feature — rules-based, no LLM.

- **Endpoint:** `GET /api/Insight` — gated by `[RequiresActiveSubscription]`.
- **Backend:** `InsightService` computes 4 templated insights from `HabitSkip`+`HabitTracking` (top skip reason, completion time-of-day, weekday-vs-weekend, most-skipped habit); each omitted below a data threshold.
- **Frontend:** `DashboardInsights` panel behind `<RequirePro>` (upgrade prompt for free users). *(The standalone /insights page was removed — one home on the Dashboard.)*
- **Notable:** review caught a `DayOfWeek` **SQL-translation bug** (would 500 in prod) → fixed by materializing before bucketing, + a SQLite regression test.
- **Tests:** ~86.

## Plan 6 — Weekly CEO Report (PAID, in-app)
*Spec: `docs/superpowers/specs/2026-06-09-weekly-ceo-report-design.md` · Plan: `…-weekly-ceo-report-plan.md`*

The second paid feature — a curated weekly narrative.

- **Endpoint:** `GET /api/Report/weekly` — gated.
- **Backend:** `WeeklyReportService` — weighted **Performance Score** (goal-linked habits ×1.5), score band, best/worst habit, consistency delta (this vs last week), top miss reason, templated "focus next week" line. Reuses shared `HabitMath` + `SkipReason.Humanize()` (extracted from `HabitService`/`InsightService` to kill duplication).
- **Frontend:** "This Week" `DashboardWeeklyReport` card behind `<RequirePro>`.
- **Scope:** in-app only; the **Sunday auto-email is deferred** (a future background-job fast-follow).
- **Tests:** ~95 (incl. a SQLite-backed test + a real cross-habit weighting test).

## Plan 7 — Stripe Billing (the real paywall)
*Spec: `docs/superpowers/specs/2026-06-09-stripe-billing-design.md` · Plan: `2026-06-09-stripe-billing-plan.md` · Runbook: `docs/superpowers/specs/STRIPE-LIVE-TEST-RUNBOOK.md`*

Replaces the manual toggle with real subscription billing.

- **Schema:** `User.StripeCustomerId`/`StripeSubscriptionId`. Migration `AddStripeIdsToUser`. Package: **Stripe.net 52.0.0** + `StripeOptions` (secrets via user-secrets).
- **Endpoints:**
  - `POST /api/Billing/create-checkout-session` `[Authorize]` → hosted Stripe Checkout (subscribe).
  - `POST /api/Billing/create-portal-session` `[Authorize]` → Stripe Customer Portal (manage/cancel).
  - `POST /api/Billing/webhook` `[AllowAnonymous]` → **signature-verified** (bad sig → 400, no mutation); syncs entitlement on checkout.completed / subscription.updated / subscription.deleted / invoice.payment_failed.
- **Architecture:** Stripe behind `IStripeGateway`; webhook sync in `StripeWebhookHandler` over a normalized event (fail-closed status map, idempotent, lookup-by-customer-id). Fully unit-tested without keys.
- **Changes:** `set-plan` demoted to **Development-only**; frontend Upgrade buttons → Checkout, "Manage subscription" → Portal, Dashboard refreshes on `?checkout=success`.
- **Notable:** review caught the Stripe.net-52 API change (`CurrentPeriodEnd` moved to `SubscriptionItem`) → `sub.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd`; webhook security (verify-before-mutate) validated.
- **Tests:** 106. *Live Stripe round-trip not yet run — verify via the runbook.*

---

## Cross-cutting & polish (alongside the plans)
- Product rename **Atomic Habits → Momentum** (display text only; namespace/DB stay `AtomicHabits`).
- Public **landing page** merged into the SPA at `/`.
- **GitHub-style contribution heatmap** (replaced amber/red ApexCharts version).
- Bug fixes: hardcoded dashboard stats → live; refresh-only completion checkbox → instant; **MUI 7 `Grid` `size`** migration across Dashboard + Habit Tracker; crash-proofed summary cards; balanced Dashboard layout (Insights + This Week share a full-width row).

## Status
- **Shipped & tagged `v1.0`** — Plans 1–7 on master, pushed to origin (private repo).
- **Optional leftovers:** Weekly Report Sunday auto-email; dunning emails on failed payment; the live Stripe verification.
