# Momentum — AI Coach v1 Design

> Design spec for the first AI-powered feature: an LLM-written coaching narrative
> over the deterministic stats the app already computes. This is the "v2 AI" the
> Performance OS v1 design (`CHANGELOG-performance-os-v1.md`) anticipated, and
> Feature 1 ("AI Accountability Coach") from the positioning roadmap. Brainstormed
> 2026-06-14.

## Context & Decisions

Momentum already collects a rich behavioral dataset (habits, skips with reasons,
completion timestamps, goal linkage) and computes deterministic insights over it
(`InsightService`) plus a weighted Weekly Report (`WeeklyReportService`). The AI
Coach is the **interpretation layer on top of that data**: Claude reasons over the
already-computed facts and writes a short coaching narrative, surfacing
cross-pattern insights the fixed templates can't.

Key decisions from brainstorming:

- **Interaction shape:** a **generated narrative/summary** (not a chat, not proactive
  nudges). Lowest-risk, lowest-cost way to ship real AI value; fits the existing
  card pattern (`DashboardInsights`, `DashboardWeeklyReport`).
- **Trigger:** **on-demand with a selectable window** (last 1 / 2 / 3 weeks, or this
  month) **plus an automated monthly** generation. Weekly cadence stays user-driven
  and flexible; monthly is the one hands-off heartbeat.
- **Grounding — the central decision:** **Claude writes, never computes.** The backend
  computes a verified "fact sheet" and passes it in the prompt; Claude only
  interprets, connects, and writes prose. It never sees raw rows and never derives a
  statistic — eliminating the #1 LLM risk (hallucinated numbers) and enabling a clean
  templated fallback.
- **Output:** a consistent **3-part structure** — summary, 2-3 observed patterns, 2-3
  concrete actions — returned as discrete structured fields (not parsed from prose),
  with light identity framing where goal-linked data exists.
- **Model:** **`claude-sonnet-4-6`** for v1 (balanced cost/quality), set via config
  (`CoachOptions.Model`) so Haiku (cheaper) or Opus (more capable) is a one-line swap
  after evaluation. `AiCoachReport.Model` records what produced each report.
- **Cost controls:** **24h cache + 10/day rate limit**, both config-driven and relaxed
  in Development.
- **Monetization:** **paid** — gated by the existing `[RequiresActiveSubscription]`.
  Tested via the existing Development-only `set-plan` bridge; no live Stripe required.

### Non-goals (explicitly deferred)

- Conversational chat / follow-up Q&A (a possible v2; v1 is one-directional).
- Proactive at-check-in nudges.
- Claude computing its own statistics or seeing raw tracking rows.
- A `CoachReportItem` child table — patterns/actions are stored as JSON (read/written
  as a whole set, never queried individually).
- Replacing `InsightService` / `WeeklyReportService` — the coach reuses them.

---

## 1. Architecture & Data Flow

The coach reuses the existing Controller → Service → (gateway/builder) pattern, all
owner-scoped via the JWT-derived `UserId`.

```
Frontend (Dashboard "AI Coach" card, behind <RequirePro>)
   │  GET /api/Coach?weeks=1|2|3   (or ?month=YYYY-MM)   [RequiresActiveSubscription]
   ▼
CoachController → CoachService
   │  1. Cache: fresh AiCoachReport for (userId, window) within TTL?  → return it (fromCache)
   │  2. Rate limit: < MaxGenerationsPerDay today for this user?      → else 429
   │  3. Build CoachFactSheet for the window  (windowed stats)
   │        ├── generalized InsightService aggregations   (from→to)
   │        └── generalized WeeklyReportService scoring    (from→to)
   │  4. Insufficient data? → return "keep logging" message, NO Claude call
   │  5. IClaudeCoachGateway.GenerateAsync(factSheet)      → Claude (structured output)
   │        └── on error/timeout → templated fallback from the same fact sheet
   │  6. Persist AiCoachReport (cache + history + rate-limit ledger)
   ▼
Returns { window, summary, patterns[], actions[], generatedAt, model, isFallback, fromCache }
```

New units, each with one job:

- **`CoachFactSheetBuilder`** — pure data assembly: windowed stats → a `CoachFactSheet`
  DTO of verified facts. No Claude, no HTTP. Reuses the shared `HabitMath` /
  `SkipReason.Humanize()` helpers.
- **`IClaudeCoachGateway` / `ClaudeCoachGateway`** — the only unit that talks to Claude.
  Behind an interface (mirrors `IStripeGateway`) so `CoachService` is fully testable
  with a fake gateway and Claude being down never breaks tests.
- **`CoachService`** — orchestrates cache → rate-limit → fact sheet → gateway → persist.
  Owner-scoped.

The automated monthly generation reuses the existing background-job pattern
(`ReminderDispatcherService` / the deferred Weekly Report email job): once a month,
for each Pro+Active user, generate the month's report (same `CoachService` path) so
it's cached and ready. (In-app surfacing first; an emailed monthly is an optional
fast-follow, consistent with the Weekly Report's deferred email.)

---

## 2. Data Model

One new entity, owner-scoped, following the `WeeklyReport`-style persisted-row pattern.

### `AiCoachReport`

| Field | Type | Purpose |
|---|---|---|
| `Id` | int | PK |
| `UserId` | int | Owner scope (FK → User, cascade with the user's other child rows) |
| `WindowKind` | enum `Weeks` \| `Month` | Window type analyzed |
| `WindowValue` | string | `"1"`/`"2"`/`"3"` for weeks, or `"2026-06"` for a month — cache-key discriminator |
| `RangeStart` / `RangeEnd` | DateTime | Resolved date window (display + audit) |
| `Summary` | string | Claude's part 1 |
| `PatternsJson` | string | Part 2 — 2-3 patterns, JSON array |
| `ActionsJson` | string | Part 3 — 2-3 actions, JSON array |
| `Model` | string | e.g. `claude-sonnet-4-6` — provenance for evaluation |
| `IsFallback` | bool | True if produced by the templated fallback (Claude errored) |
| `CreatedAt` | DateTime | Cache freshness + rate-limit ledger |

**One row, triple duty:** cache (lookup by `UserId + WindowKind + WindowValue`, fresh
if `CreatedAt` within TTL), rate-limit ledger (count today's rows per user), and
history list.

- **Migration:** one additive EF migration, `AddAiCoachReport`. No backfill.
- **Index:** `AiCoachReport(UserId, WindowKind, WindowValue, CreatedAt)` — serves the
  cache lookup and the daily-count query.
- **Patterns/actions as JSON strings:** always read/written as a whole set, never
  queried individually → JSON column, not a child table (YAGNI).
- **No changes to existing entities** — the coach only *reads* `Habit`, `HabitSkip`,
  `HabitTracking`, `Goal`/`Milestone`/`Vision`.

---

## 3. The CoachFactSheet (what Claude receives)

The heart of "Claude writes, never computes." The backend computes every number;
Claude receives a compact, already-true DTO (a few hundred tokens).

```jsonc
{
  "window": { "kind": "Weeks", "value": "2", "from": "2026-06-01", "to": "2026-06-14", "label": "the last 2 weeks" },
  "performanceScore": 62,          // WeeklyReportService scoring, windowed
  "scoreBand": "Building",
  "consistencyDelta": -8,          // vs the prior equal-length window
  "completionRate": 0.58,
  "bestHabit":  { "name": "Reading", "rate": 0.9 },
  "worstHabit": { "name": "Gym", "rate": 0.3 },
  "topSkipReason":    { "reason": "Low Energy", "pct": 41 },   // InsightService, windowed
  "mostSkippedHabit": { "name": "Gym", "skips": 7 },
  "completionTimeOfDay": { "bucket": "morning", "pct": 68 },
  "weekdayVsWeekend": { "moreOn": "weekday", "ratio": 2.3 },
  "identity": { "title": "Remote Backend Engineer", "source": "Vision" },
  "goalLinkedHabitCount": 3,
  "totalHabitCount": 5,
  "dataSufficiency": { "enoughForScore": true, "enoughForSkipPatterns": true }
}
```

- Every field is already computed and true. Claude interprets and connects them
  (e.g. "your Low-Energy skips cluster on the same evenings your Gym rate drops — try
  mornings"), never derives them.
- **`dataSufficiency` flags** mirror how `InsightService` omits a template below its
  threshold. If overall data is too thin, `CoachService` **skips the Claude call**
  and returns a "keep logging" message — no tokens spent.
- **Windowing is the one substantive new backend piece:** `InsightService` is
  currently all-time and `WeeklyReportService` is hardcoded to the current ISO week.
  v1 generalizes both to accept a `(from, to)` range, extracting shared math to avoid
  duplication (continuing the `HabitMath` / `SkipReason.Humanize()` extraction the v1
  build already did).

This fact sheet plus a system prompt (3-part structure + identity framing) is the
entire Claude input.

---

## 4. Gating, Bypass & Cost Controls

**Production gate (the real paywall):**
- `GET /api/Coach` carries `[RequiresActiveSubscription]` — only Pro+Active reach it
  (same mechanism as Insights / Weekly Report).
- Frontend wraps the card in `<RequirePro>` — free users see an upgrade prompt.

**Testing bypass (no Stripe):**
- Use the existing **Development-only `POST /api/Subscription/set-plan`** bridge to
  flip to Pro. No new code; same path Plans 5/6 are tested through.

**Cost & abuse controls — config-driven via `CoachOptions`:**

```jsonc
CoachOptions {
  Model: "claude-sonnet-4-6",   // swappable: claude-haiku-4-5 | claude-opus-4-8
  CacheTtlHours: 24,            // repeat views of same (user,window) return cached row
  MaxGenerationsPerDay: 10,     // ledger = count of today's AiCoachReport rows per user
  ApiKey: <user-secrets>        // never in source (mirrors StripeOptions)
}
```

- **Development relaxes throttling** so testing isn't blocked: `CacheTtlHours: 0`
  (always regenerate) and `MaxGenerationsPerDay: 0` ⇒ unlimited in Development.
  Production keeps 24h / 10.
- Over the daily cap → `429` with a clear message; frontend shows "today's coaching
  limit reached."
- API key via **user-secrets**, respecting the leaked-credential lesson from the v1
  design spec.

---

## 5. Output Contract, Error Handling & Testing

**Structured output Claude returns** (via `OutputConfig.Format` — forced shape, no
prose parsing):

```jsonc
{
  "summary": "part 1: how you did this window",
  "patterns": ["...", "..."],   // part 2: 2-3 observed patterns, grounded in the fact sheet
  "actions":  ["...", "..."]    // part 3: 2-3 concrete actions for next window
}
```

`CoachService` maps this into the `AiCoachReport` row.

**API response to the frontend:**

```jsonc
{
  "window": { "label": "the last 2 weeks", "from": "...", "to": "..." },
  "summary": "...", "patterns": [...], "actions": [...],
  "generatedAt": "...", "model": "claude-sonnet-4-6",
  "isFallback": false, "fromCache": true
}
```

**Error handling — graceful degradation:**
- Claude error/timeout → fall back to a **templated narrative built from the same
  fact sheet** (the Weekly Report's templated lines, assembled into the 3-part shape),
  persisted with `IsFallback: true`. The user always gets something; the UI can label
  it subtly. Claude being unreachable never breaks the request or the tests.
- Insufficient data → skip the Claude call, return the "keep logging" message.

**Claude integration (API specifics):**
- Official `Anthropic` C# SDK (`dotnet add package Anthropic`, `AnthropicClient`).
- Model `claude-sonnet-4-6`; adaptive thinking (`thinking: {type: "adaptive"}`),
  structured output via `OutputConfig.Format`.
- Key from user-secrets via `CoachOptions`.

**Frontend:** a `DashboardAiCoach` card behind `<RequirePro>` — window selector
(1/2/3 weeks · this month), the three sections as distinct blocks, a refresh action,
and a small history affordance. Mirrors `DashboardInsights` / `DashboardWeeklyReport`.

**Testing (no API key required):**
- `CoachFactSheetBuilder` — SQLite-backed `AppDbContext` with seeded data; asserts
  windowing math (1/2/3 weeks, month boundaries, prior-window delta) and
  `dataSufficiency` flags.
- `CoachService` — fake `IClaudeCoachGateway`: cache hit/miss, rate-limit enforcement,
  fallback-on-error, insufficient-data skip. No real Claude call.
- `ClaudeCoachGateway` — prompt assembly + response mapping against a canned
  structured response.
- Follows the established test-first, fake-the-external-gateway pattern (how
  `StripeWebhookHandler` is tested without keys).

---

## Suggested build order

1. **Windowed aggregations** — generalize `InsightService` + `WeeklyReportService` to
   a `(from, to)` range; extract shared math. (Unblocks the fact sheet; pure backend,
   fully unit-testable.)
2. **`AiCoachReport` entity + migration.**
3. **`CoachFactSheetBuilder`** — windowed stats → DTO, with `dataSufficiency`.
4. **`IClaudeCoachGateway` + `ClaudeCoachGateway`** — Anthropic SDK, structured output,
   `CoachOptions`. Unit-tested with a canned response.
5. **`CoachService`** — cache + rate-limit + fallback + persist; fake gateway in tests.
6. **`CoachController`** — `GET /api/Coach`, `[RequiresActiveSubscription]`.
7. **Frontend `DashboardAiCoach` card** behind `<RequirePro>`.
8. **Monthly background generation** — reuse the reminder-dispatcher pattern.

## Open items for later (out of scope here)

- Conversational follow-up chat over the same grounding.
- Emailed monthly review (optional fast-follow, like the deferred Weekly Report email).
- Model evaluation: compare Sonnet 4.6 vs Haiku 4.5 vs Opus 4.8 output quality on real
  fact sheets, then set `CoachOptions.Model` accordingly.
