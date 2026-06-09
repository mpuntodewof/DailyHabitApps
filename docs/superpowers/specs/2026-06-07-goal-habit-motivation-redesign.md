# Goal↔Habit Linkage — Motivation Redesign

> Design spec for making the Goal→Habit linkage actually motivating, instead of
> a separate filing cabinet with a faint label. Brainstormed 2026-06-07, after
> manual testing of the merged free tier surfaced that the feature felt like
> bookkeeping. Builds on the entities already on master (Plans 1–3).

## Problem

The shipped Goal→Habit framework (Plans 1–2) created the data hierarchy
(Vision→Goal→Milestone→Habit) but **not the feedback loop**. Symptoms:

- **High, abstract setup cost:** create Vision, Goal, Milestone, then go to a
  *different* page and link a habit — 4 steps across 2 screens before any payoff.
- **Passive, invisible payoff:** the only reward is a static line on a card
  ("Contributes to: X"). It doesn't respond to completing the habit.
- **The daily loop is untouched:** checking off a habit — the moment that should
  feel meaningful — does nothing with the goal.

The user (product owner) flagged this directly: "I still don't get the value …
why is this separated from the habit tracker?" Correct instinct.

## Design decisions (locked during brainstorming)

1. **The payoff is "meaning in the moment"** — not a progress bar. No completion
   math, no % toward goal.
2. **It fires as a confirmation hit on check-in** — a brief, self-dismissing
   celebration when you mark a habit done; nothing persistent cluttering cards.
3. **Framing = the Atomic Habits "identity vote":** "A vote for {identity}."
4. **Milestone = one-time destination; Habit = recurring vehicle that drives it.**
   They stay distinct shapes (a milestone like "Build a portfolio" is not a daily
   checkbox). They are NOT merged.
5. **Seamless flow instead of merging:** creating a habit *from* a milestone is
   one inline action, pre-linked — so the layers flow together without double setup.
6. **No new schema.** Reuse the existing `Habit.MilestoneId` link; resolve identity
   up the existing Habit→Milestone→Goal→Vision chain. No migration.

### Non-goals
- Progress bars / goal-completion percentages.
- A direct `Habit→Vision` FK (considered, dropped — the milestone chain suffices).
- Merging Milestone and Habit into one entity.
- Any paid gating (this is free-tier polish; gating is Plan 4).
- Streak-combo or other framings of the hit (identity vote only).

---

## Section 1 — Identity resolver (backend, no schema change)

We already have `HabitService.GetContributionAsync(userId, habitId)` returning
`HabitContributionDto { MilestoneId?, MilestoneTitle?, GoalTitle?, VisionTitle? }`
by walking Habit→Milestone→Goal→Vision (all null-safe).

**Change:** add a computed `IdentityTitle` to that resolution, defined as the best
available rung of the ladder:

```
IdentityTitle = VisionTitle ?? GoalTitle ?? MilestoneTitle ?? null
```

So a habit linked to a milestone whose goal has no vision still yields a
meaningful identity string (the goal or milestone title), and a fully-laddered
habit yields the top identity (the Vision). Null only when the habit has no
milestone link at all.

- Add `IdentityTitle` (nullable string) to `HabitContributionDto`, computed in the
  existing projection (no DB change).
- This is the single source of truth for "what identity does this habit serve."

---

## Section 2 — Seamless milestone → habit flow (frontend)

Kills the setup ceremony. On the Goals page, under each **Milestone**:

1. **"+ Add a habit toward this" action.** Opens the existing habit create dialog
   (`HabitDialogForm`) **pre-linked to that milestone**: the "Contributes to" /
   milestone field is pre-filled with that milestone AND shown read-only/disabled
   in this entry path (the user came *from* that milestone, so it shouldn't be
   re-pickable here). The user can still change linkage later via the normal habit
   edit form. On save it
   uses the existing `POST /Habit/post-habit` with `milestoneId` already set; the
   habit then appears on the Habit Tracker page, already linked.
2. **Show linked habits under the milestone.** A simple list of the habit names
   already linked to that milestone, so the milestone visibly "has vehicles
   driving it." Source: filter the habits already loaded in the client by
   `milestoneId` (no new endpoint). If the Goals page doesn't already have the
   habit list in context, it can read it from `HabitContext` (the habit list
   carries `milestoneId`); only if that proves awkward, add a small
   `GET /Habit/by-milestone/{milestoneId}` — but prefer the client-side filter.

Result: the flow is one continuous motion — Vision → Goal → Milestone →
"+ add habit toward this" → done — with no separate trip to build the habit from
scratch and no manual re-linking. The standalone habit-create flow (with its own
optional "Contributes to" picker) still works unchanged.

---

## Section 3 — Identity-vote check-in hit (the payoff)

When a habit is marked **done**, fire a brief celebratory confirmation, then fade.

- **Trigger:** the existing habit completion / daily check-in submit.
- **What fires:** a transient toast via the app's existing `useSnackbar`:
  > ✓ {habitName}
  > **A vote for {IdentityTitle}** 🗳️
- **No identity → no change:** if the habit has no milestone link (IdentityTitle
  null), check-in behaves exactly as today (normal success feedback, no identity
  line). The hit is purely additive — never blocks or alters existing completion.
- **Brief & self-dismissing** (a few seconds). No persistent banner.
- **No** progress bar, **no** streak combo — identity vote only.
- **Copy:** default "A vote for {IdentityTitle}" (tunable later).

**How the frontend gets the identity at check-in (no extra round-trip):**
- **Preferred (b):** include the resolved `IdentityTitle` (Section 1) in the
  habit completion / daily-submit response, so the hit fires directly from the
  response the moment the server confirms completion.
- **Fallback (a):** if extending the submit response is awkward, resolve identity
  client-side from data already loaded (the habit's `milestoneId` + a cached
  contribution lookup), or call `GetContributionAsync` once on completion.

The implementation plan will inspect the actual completion endpoint/response shape
first and choose (b) if clean, else (a).

---

## Why this is the right fix (rationale, for future readers)

- Attacks the *cause* (setup ceremony + dead daily loop), not just the symptom.
- Reuses everything already shipped — no schema, no migration, no wasted work.
- Keeps Milestone and Habit as correctly-shaped concepts (destination vs. vehicle).
- Delivers the exact felt payoff the owner chose: identity meaning at the moment
  of action, framed as an Atomic Habits "vote."

## Affected files (anticipated — plan will confirm exact paths)
- Backend: `HabitContributionDto` (+`IdentityTitle`), the resolver in
  `HabitService` (`GetContributionAsync` projection), and possibly the
  completion/daily-submit response in `HabitTrackingService`/controller (option b).
- Frontend: `Goals.jsx` (the "+ add habit toward this" action + linked-habit
  list under milestones), `HabitDialogForm.jsx` (accept a pre-filled/locked
  milestone), the check-in handler (fire the snackbar hit), `HabitContext` /
  `HabitTrackingContext` as needed to surface `IdentityTitle`.

## Out of scope / next
This is free-tier polish. After it lands and is verified, the roadmap resumes at
Plan 4 (entitlement + `<RequirePro>`).
