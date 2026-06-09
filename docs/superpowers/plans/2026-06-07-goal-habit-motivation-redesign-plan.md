# Goal↔Habit Motivation Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make goal↔habit linkage motivating: an identity-vote confirmation hit when a habit is checked off, plus a seamless "add a habit toward this milestone" flow — with no new schema.

**Architecture:** One tiny backend change (add a computed `IdentityTitle` to the existing `HabitContributionDto`, resolved up the Habit→Milestone→Goal→Vision chain). Everything else is additive frontend: the Goals page gains an inline "+ Add a habit toward this" action under each milestone (pre-linked, reusing the existing habit-create dialog) and shows linked habits; the habit check-in handler fires a brief identity-vote snackbar using the already-available contribution data. No EF migration.

**Tech Stack:** ASP.NET Core 8, EF Core 9, xUnit + FluentAssertions, React 19 + MUI 7 + axios.

**Spec:** `docs/superpowers/specs/2026-06-07-goal-habit-motivation-redesign.md`.

**Branch:** work on `master` is NOT allowed for implementation — create/confirm a feature branch `feat/goal-habit-motivation` first (the orchestrator handles branch setup before dispatching).

---

## Context the implementer needs

- `HabitContributionDto` lives in `server/AtomicHabits/Models/DTO/GoalFrameworkDto.cs` and currently is `{ int? MilestoneId; string? MilestoneTitle; string? GoalTitle; string? VisionTitle; }`.
- `HabitService.GetContributionAsync(int userId, int habitId)` (in `server/AtomicHabits/Services/HabitService.cs`, ~line 292) projects that DTO via null-safe navigation Habit→Milestone→Goal→Vision and returns it in an `ApiResponse`. Exposed at `GET /api/Habit/{habitId}/contribution`.
- Frontend `client-ui/src/views/habit/Habit.jsx` ALREADY fetches `/Habit/{id}/contribution` for linked habits (for the Plan 2 "Contributes to:" line) and stores results in a `contributions` state map keyed by habit id (see the payoff-line code added in Plan 2). The habit list objects carry `milestoneId`.
- The check-in / completion happens in `client-ui/src/views/habit/` (a tracking dialog or a "mark done" action) and posts to `HabitTracking` endpoints (`submit-daily` / `submit-habit-progress`). The implementer MUST inspect the actual completion handler before wiring the hit.
- `useSnackbar()` (from `client-ui/src/context/SnackbarContext.jsx`) exposes `{ showSnackbar, showError, showSuccess, showWarning, showInfo }`.
- `useGoals()` (from `client-ui/src/context/GoalContext.jsx`) exposes vision/goal/milestone CRUD + `listMilestones(goalId)`. `useHabits()` (from `HabitContext.jsx`) exposes `createHabit`, the `habits` list, etc.
- The habit create dialog is `client-ui/src/views/habit/components/HabitDialogForm.jsx`; it has a "Contributes to" milestone `Select` (added in Plan 2) bound to `milestoneId` in the payload.

---

### Task 1: Backend — add `IdentityTitle` to the contribution resolver (TDD)

**Files:**
- Modify: `server/AtomicHabits/Models/DTO/GoalFrameworkDto.cs` (add one property)
- Modify: `server/AtomicHabits/Services/HabitService.cs` (`GetContributionAsync` projection)
- Test: `server/AtomicHabits.Tests/Services/HabitServiceMilestoneTests.cs` (extend — this is the existing file with contribution tests)

- [ ] **Step 1: Write the failing tests**

Append to `server/AtomicHabits.Tests/Services/HabitServiceMilestoneTests.cs` (match the existing test style in that file — it already constructs the real `HabitService` and tests `GetContributionAsync`; reuse its existing setup helpers/patterns):
```csharp
    [Fact]
    public async Task Contribution_IdentityTitle_prefers_vision_then_goal_then_milestone()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewHabitService(db); // use the same factory/helper the existing tests use

        // full chain: vision -> goal -> milestone -> habit
        var vision = new AtomicHabits.Models.Vision { UserId = 1, Title = "Become a Remote Backend Engineer" };
        db.Visions.Add(vision); await db.SaveChangesAsync();
        var goal = new AtomicHabits.Models.Goal { UserId = 1, VisionId = vision.Id, Title = "Get a remote job" };
        db.Goals.Add(goal); await db.SaveChangesAsync();
        var milestone = new AtomicHabits.Models.Milestone { UserId = 1, GoalId = goal.Id, Title = "Build portfolio" };
        db.Milestones.Add(milestone); await db.SaveChangesAsync();
        var habit = new AtomicHabits.Models.Habit { UserId = 1, Name = "Code 1h", Frequency = "Daily", MilestoneId = milestone.Id };
        db.Habits.Add(habit); await db.SaveChangesAsync();

        var res = await svc.GetContributionAsync(1, habit.Id);
        var dto = (AtomicHabits.Models.DTO.HabitContributionDto)res.Result!;
        dto.IdentityTitle.Should().Be("Become a Remote Backend Engineer");
    }

    [Fact]
    public async Task Contribution_IdentityTitle_falls_back_to_goal_when_no_vision()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewHabitService(db);
        var goal = new AtomicHabits.Models.Goal { UserId = 1, Title = "Get a remote job" }; // no vision
        db.Goals.Add(goal); await db.SaveChangesAsync();
        var milestone = new AtomicHabits.Models.Milestone { UserId = 1, GoalId = goal.Id, Title = "Build portfolio" };
        db.Milestones.Add(milestone); await db.SaveChangesAsync();
        var habit = new AtomicHabits.Models.Habit { UserId = 1, Name = "Code 1h", Frequency = "Daily", MilestoneId = milestone.Id };
        db.Habits.Add(habit); await db.SaveChangesAsync();

        var res = await svc.GetContributionAsync(1, habit.Id);
        var dto = (AtomicHabits.Models.DTO.HabitContributionDto)res.Result!;
        dto.IdentityTitle.Should().Be("Get a remote job");
    }

    [Fact]
    public async Task Contribution_IdentityTitle_is_null_when_habit_unlinked()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewHabitService(db);
        var habit = new AtomicHabits.Models.Habit { UserId = 1, Name = "Drink water", Frequency = "Daily" };
        db.Habits.Add(habit); await db.SaveChangesAsync();

        var res = await svc.GetContributionAsync(1, habit.Id);
        var dto = (AtomicHabits.Models.DTO.HabitContributionDto)res.Result!;
        dto.IdentityTitle.Should().BeNull();
    }
```
NOTE: the existing test file already has a helper to build a `HabitService` (it tested `GetContributionAsync` in Plan 2). Use that SAME helper name — inspect the file and replace `NewHabitService(db)` with whatever it actually uses. Do not invent a new constructor call.

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test server/AtomicHabits.Tests --filter HabitServiceMilestoneTests`
Expected: FAIL — `HabitContributionDto` has no `IdentityTitle`.

- [ ] **Step 3: Add the property**

In `server/AtomicHabits/Models/DTO/GoalFrameworkDto.cs`, add to `HabitContributionDto`:
```csharp
        public string? IdentityTitle { get; set; }
```

- [ ] **Step 4: Populate it in the projection**

In `server/AtomicHabits/Services/HabitService.cs`, in the `GetContributionAsync` `.Select(...)` projection (right after the `VisionTitle = ...` line, ~line 305), add:
```csharp
                        IdentityTitle =
                            (h.Milestone != null && h.Milestone.Goal != null && h.Milestone.Goal.Vision != null)
                                ? h.Milestone.Goal.Vision.Title
                            : (h.Milestone != null && h.Milestone.Goal != null)
                                ? h.Milestone.Goal.Title
                            : (h.Milestone != null)
                                ? h.Milestone.Title
                            : null,
```
(This mirrors the existing null-safe navigation already used for `GoalTitle`/`VisionTitle`, so it translates in EF the same way.)

- [ ] **Step 5: Run to verify pass + full suite**

Run: `dotnet test server/AtomicHabits.Tests/AtomicHabits.Tests.csproj`
Expected: all pass (existing + 3 new).

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/Models/DTO/GoalFrameworkDto.cs server/AtomicHabits/Services/HabitService.cs server/AtomicHabits.Tests/Services/HabitServiceMilestoneTests.cs
git commit -m "feat: resolve IdentityTitle (vision>goal>milestone) on habit contribution"
```

---

### Task 2: Frontend — identity-vote hit on check-in

**Files:**
- Modify: the habit completion / check-in handler in `client-ui/src/views/habit/` (inspect to find it — likely `Habit.jsx` or a tracking dialog component under `components/`)

- [ ] **Step 1: Inspect the completion flow**

Read `client-ui/src/views/habit/Habit.jsx` and any tracking dialog (e.g. `HabitTrackingDialog.jsx` under `components/`). Identify: (a) the function that runs when a habit is marked done / progress submitted, (b) whether `Habit.jsx` already has the `contributions` map (keyed by habit id, from the Plan 2 payoff line) and how it's populated, (c) how `useSnackbar` is already used elsewhere in this view (import + call). Report what you find before editing.

- [ ] **Step 2: Fire the identity-vote snackbar on successful completion**

After a habit is successfully marked done, resolve its identity title and, if present, show the hit. Use the contribution data already available client-side (the `contributions` map / the `/Habit/{id}/contribution` response now includes `identityTitle`). If the completing component doesn't already have the contribution for that habit, fetch it once: `const c = (await api.get(`/Habit/${habitId}/contribution`)).data?.result;`.

Then:
```jsx
const identity = contribution?.identityTitle;
if (identity) {
  showSuccess(`✓ ${habitName} — A vote for ${identity} 🗳️`);
} else {
  // existing success feedback unchanged
  showSuccess(`✓ ${habitName} completed`); // or whatever the current message is — keep current behavior
}
```
IMPORTANT: do NOT change the existing completion behavior when there's no identity — only ADD the identity line when `identityTitle` is present. Match the real snackbar method name (`showSuccess`) and the real habit field names (`habit.name`, `habit.id`) found in Step 1. If the view already shows a success toast on completion, REPLACE its text with the identity version only when an identity exists; otherwise leave the existing toast as-is.

- [ ] **Step 3: Build**

Run (from `client-ui/`): `npx vite build`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add client-ui/src/views/habit/
git commit -m "feat: identity-vote confirmation hit on habit check-in"
```

---

### Task 3: Frontend — "+ Add a habit toward this" under each milestone

**Files:**
- Modify: `client-ui/src/views/goals/Goals.jsx`
- Modify: `client-ui/src/views/habit/components/HabitDialogForm.jsx` (accept a pre-filled, locked milestone)

- [ ] **Step 1: Let HabitDialogForm accept a pre-set, locked milestone**

Read `HabitDialogForm.jsx`. It has a "Contributes to" milestone `Select` bound to `milestoneId`. Add an optional prop `lockedMilestoneId` (and optionally `lockedMilestoneLabel`): when provided, the form initializes `milestoneId` to it AND renders the milestone field as read-only/disabled (the user arrived from that milestone, so it's fixed in this entry path). When the prop is absent, the form behaves exactly as today (free milestone picker). Do not break existing usages — the prop is optional with a default of `null`.

- [ ] **Step 2: Add the action + linked-habit list under each milestone in Goals.jsx**

Read `Goals.jsx` (it renders milestones under goals, lazy-loaded via `listMilestones(goalId)`). For each milestone, add:
- A **"+ Add a habit toward this"** button that opens `HabitDialogForm` with `lockedMilestoneId={milestone.id}` (and a label like `"{goalTitle} › {milestone.title}"`). On submit it calls the existing `createHabit` from `useHabits()` (the same path the Habits page uses) with the milestone pre-linked, then closes. Show a success toast via `useSnackbar`.
- A **list of habits already linked to this milestone**: filter `useHabits().habits` (the loaded habit list; each item has `milestoneId`) by `h.milestoneId === milestone.id` and render their names. If `habits` isn't populated in this view yet, call the habits fetch (`fetchHabits`/`searchHabits` from `useHabits()`) on mount so the list is available. Keep it simple — just names, no actions.

- [ ] **Step 3: Build**

Run (from `client-ui/`): `npx vite build`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add client-ui/src/views/goals/Goals.jsx client-ui/src/views/habit/components/HabitDialogForm.jsx
git commit -m "feat: add-habit-toward-milestone flow + linked habits on Goals page"
```

---

## Self-Review (completed by plan author)

- **Spec coverage:** §1 identity resolver (Task 1, `IdentityTitle` = vision>goal>milestone, tested incl. null case). §2 seamless milestone→habit flow + linked-habit list (Task 3, locked-milestone dialog reusing `createHabit`). §3 identity-vote check-in hit (Task 2, additive snackbar, no-identity path unchanged). All three sections covered. ✓
- **No schema/migration:** confirmed — Task 1 adds only a computed DTO field + projection expression; no entity/DbContext change. ✓
- **Placeholders:** Tasks 2 & 3 say "inspect the real handler/field/method names first" rather than inventing `NewHabitService`/snackbar/`createHabit`/menu signatures the plan can't verify — each gives the exact behavior + code shape, just not invented identifiers. Task 1 flags the existing test helper must be reused (not invented). This is correct, not a placeholder failure.
- **Type consistency:** `IdentityTitle` (C# `string?`) ↔ `identityTitle` (camelCase JSON) used consistently across Task 1 (backend) and Task 2 (frontend reads `contribution.identityTitle`). `lockedMilestoneId` consistent across Task 3 steps. ✓
- **Behavior preservation:** Task 2 explicitly preserves existing completion behavior when no identity; Task 3's new dialog prop is optional/defaulted so existing `HabitDialogForm` usages don't break. ✓

## Execution note
Small plan (3 tasks). Task 1 is backend-TDD (fast/cheap). Tasks 2–3 are frontend (no test harness → build + manual verify; the final review must check the snackbar contract reads `identityTitle` and existing completion behavior is preserved).
