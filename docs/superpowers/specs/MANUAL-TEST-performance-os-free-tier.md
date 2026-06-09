# Manual Test Script — Performance OS FREE Tier (Plans 1–3)

> Run-through to verify the merged free-tier features against the live app before
> starting Plan 4. Check each box as you go. If anything fails, note the step #
> and what you saw — that's enough for me to debug.

## What you're verifying
- **Plan 1** — new DB schema (Vision/Goal/Milestone/HabitSkip) applied & working.
- **Plan 2** — Goal→Milestone→Habit hierarchy: CRUD + linking a habit + the "Contributes to…" payoff line.
- **Plan 3** — failure capture: "Skip today" with a reason chip.

---

## 0. Start the app (two terminals)

**Terminal A — API** (from repo root):
```
cd "server/AtomicHabits"
dotnet run
```
Wait for `Now listening on: http://localhost:5198`. (Migration `AddPerformanceOsFoundation` is already applied — the startup log should NOT warn about pending migrations.)

**Terminal B — client** (from repo root):
```
cd "client-ui"
npm run dev
```
Open the URL Vite prints (usually `http://localhost:5173`).

- [ ] **0.1** API started, listening on 5198, no pending-migration warning in the log.
- [ ] **0.2** Client loaded; you see the **Momentum** landing page at `/`.
- [ ] **0.3** Log in (or register) and reach the dashboard. (Use an existing account; auth is unchanged by Plans 1–3.)

> If 0.1 fails with a DB/object error: the migration may not be applied to the DB your
> connection string points at — tell me and I'll help.

---

## 1. Plan 2 — Goals view exists & hierarchy CRUD

- [ ] **1.1** A **"Goals"** item appears in the left sidebar (target icon). Click it → lands on `/goals`.
- [ ] **1.2** The Goals page renders without error (empty state is fine if you have no goals yet).

**Create a Vision:**
- [ ] **1.3** Create a Vision titled `Become a Senior Engineer`. It appears in the list.

**Create a Goal under that Vision:**
- [ ] **1.4** Create a Goal titled `Get promoted in 2026`, linked to the `Become a Senior Engineer` vision. It shows under that vision (and shows a **status chip** = `Active`).
- [ ] **1.5** Create a second Goal `Learn system design` with **no** vision selected → it appears in an **"Unassigned"** group (or ungrouped), not crashing.

**Create a Milestone under a Goal:**
- [ ] **1.6** Expand `Get promoted in 2026` and create a Milestone `Ship the billing feature`. It appears under that goal.
- [ ] **1.7** Create a second Milestone `Mentor a junior` with an order; both list in order.

**Edit:**
- [ ] **1.8** Edit the Goal title to `Get promoted by Q4 2026` → the change persists after a page refresh.
- [ ] **1.9** Change the Goal's status (e.g. to `Achieved`) → the chip updates.

---

## 2. Plan 2 — Link a habit to a milestone + payoff line

- [ ] **2.1** Go to **Habits**. Create a new habit `Read system design book`, and in the form use the **"Contributes to…"** dropdown to select `Get promoted by Q4 2026 › Ship the billing feature` (Goal › Milestone). Save.
- [ ] **2.2** The habit is created. On its card, a muted line reads **"Contributes to: Get promoted by Q4 2026"** (the goal title).
- [ ] **2.3** Create another habit `Drink water` and leave "Contributes to" empty. Its card shows **no** contribution line.
- [ ] **2.4** Edit `Read system design book`, change the dropdown to **(none)** / clear it, save → the contribution line disappears.
- [ ] **2.5** Edit it again, re-link to a milestone → the line comes back.

---

## 3. Plan 2 — Delete behavior (the FK-constraint fix from Plan 1)

This verifies the critical fix: deleting a Vision/Milestone must NOT delete or orphan the habits/goals under it — it nulls the link.

- [ ] **3.1** Make sure `Read system design book` is linked to milestone `Ship the billing feature` (from step 2.5).
- [ ] **3.2** Go to Goals, **delete the milestone** `Ship the billing feature`.
- [ ] **3.3** Go back to Habits → `Read system design book` **still exists** (was NOT deleted), and its "Contributes to" line is now **gone** (the link was nulled). ✅ This is the key behavior.
- [ ] **3.4** Delete the Vision `Become a Senior Engineer` → the Goal `Get promoted by Q4 2026` **still exists** (now just unlinked from any vision / shows in Unassigned), not deleted.
- [ ] **3.5** Delete a whole Goal that has a milestone with a linked habit (set one up: goal → milestone → link a habit). After deleting the goal: the goal + its milestones are gone, but the **habit still exists** with its contribution line gone.

---

## 4. Plan 3 — Failure capture (Skip today)

- [ ] **4.1** On a habit card, open the menu (⋮) → you see a **"Skip today"** item.
- [ ] **4.2** Click it → a dialog opens: *"Why did you miss {habit name}?"* with six chips: **Busy, Forgot, Low Energy, No Motivation, Schedule Conflict, Other**.
- [ ] **4.3** "Record skip" is **disabled** until you pick a chip.
- [ ] **4.4** Pick **Low Energy** → click **Record skip** → success toast, dialog closes.
- [ ] **4.5** Open "Skip today" again for the **same habit, same day**, pick a **different** reason (e.g. Busy) → record. (This should *update* the reason, not create a duplicate — you won't see this in the UI directly; verify in step 5 if you want.)

---

## 5. (Optional) Backend spot-check via Swagger / API

If the API exposes Swagger (`http://localhost:5198/swagger`), or use the browser devtools Network tab while clicking around:

- [ ] **5.1** `GET /api/HabitSkip/habit/{habitId}` (use the habit you skipped) returns **one** skip row with `reason: "Busy"` (the updated reason from 4.5) — confirming the one-per-day upsert (not two rows).
- [ ] **5.2** `GET /api/Habit/{habitId}/contribution` for a linked habit returns `{ goalTitle, milestoneTitle, visionTitle }` populated; for an unlinked habit returns nulls.
- [ ] **5.3** `GET /api/Goal` returns your goals with `status` as a string (`Active`/`Achieved`).

> Network-tab alternative: while on the Goals page, watch the calls to `/api/Vision`,
> `/api/Goal`, `/api/Milestone/goal/{id}` — all should return 200 with a `result` array.

---

## 6. Cross-cutting sanity

- [ ] **6.1** Refresh the browser on `/goals` and `/habits` → state reloads correctly (data persisted server-side, not just local).
- [ ] **6.2** No console errors in the browser devtools during the above (a 404 on a *contribution* fetch for an unlinked habit should not happen — it shouldn't fetch; flag if you see stray errors).
- [ ] **6.3** Everything you created is owner-scoped: it's all under your account (no other user's data leaks in).

---

## Result

- [ ] **All critical boxes pass** (sections 1–4 especially; 5 is optional confirmation).

If all green → tell me and we'll proceed to **Plan 4 (entitlement / `<RequirePro>`)**.
If anything fails → note the step number + what you saw (and any browser-console / API-log error text), and I'll debug it before we move on.
```
