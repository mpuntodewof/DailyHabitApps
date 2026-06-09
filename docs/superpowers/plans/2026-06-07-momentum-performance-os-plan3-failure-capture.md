# Momentum Performance OS — Plan 3: Failure Capture (FREE)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user record *why* they skipped a habit on a given day via a one-tap reason chip, persisted as a `HabitSkip` row. This is FREE for all tiers — it builds the behavioral dataset that the PAID Failure Pattern Analysis (Plan 5) and the future AI coach will reason over.

**Architecture:** A standalone `HabitSkip` write/read slice, decoupled from the existing `HabitTracking` (completion) flow. `HabitSkip` entity already exists (Plan 1). Backend follows the service-direct `TagService`/`GoalFrameworkService` pattern (service → `AppDbContext`, `ApiResponse` envelope, owner-scoped). Frontend adds a "Skip today" action on the habit card that opens a small reason-chip dialog and posts the skip.

**Tech Stack:** ASP.NET Core 8, EF Core 9, xUnit + FluentAssertions, React 19 + MUI 7 + axios.

**Spec:** `docs/superpowers/specs/2026-06-07-momentum-performance-os-v1-design.md` §3a (capture only; §3b analysis is Plan 5 and is OUT OF SCOPE here).

**Branch:** `feat/performance-os-foundation` (continues; do not switch).

---

## Scope discipline

- **In scope:** create a skip (with reason), list a habit's skips, delete/undo a skip; the chip UI; owner-scoping; dedupe (one skip per habit per day — upsert the reason if re-submitted).
- **OUT of scope (Plan 5):** any aggregation/insight ("68% after 9 PM"), any paid gating, any analytics view. Capture only.
- `HabitSkip` entity + `SkipReason` enum (Busy/Forgot/LowEnergy/NoMotivation/ScheduleConflict/Other) + `DbSet<HabitSkip>` + index already exist from Plan 1 — do NOT recreate; no migration needed.

---

## Entity reference (already exists — for context only)

```csharp
public enum SkipReason { Busy=0, Forgot=1, LowEnergy=2, NoMotivation=3, ScheduleConflict=4, Other=5 }
public class HabitSkip {
    public int Id; public int UserId; public int HabitId;
    public DateOnly Date; public SkipReason Reason; public DateTime CreatedAt;
    // nav: User?, Habit?
}
```
`AppDbContext` has `DbSet<HabitSkip> HabitSkips` and `IX_HabitSkips_UserId_HabitId_Date`.

---

## File Structure

**Backend (create):**
- `server/AtomicHabits/Models/DTO/HabitSkipDto.cs` — `HabitSkipDto`, `HabitSkipCreateDto`.
- `server/AtomicHabits/Services/HabitSkipService.cs` — `IHabitSkipService` + impl.
- `server/AtomicHabits/Controllers/HabitSkipController.cs`.
- Test: `server/AtomicHabits.Tests/Services/HabitSkipServiceTests.cs`.

**Backend (modify):**
- `server/AtomicHabits/Program.cs` — register `IHabitSkipService`.

**Frontend (create):**
- `client-ui/src/views/habit/components/SkipHabitDialog.jsx` — the reason-chip dialog.

**Frontend (modify):**
- `client-ui/src/context/HabitContext.jsx` — add `skipHabit(habitId, reason, date)` calling the new endpoint (check this file's existing style first).
- The habit card/menu (`client-ui/src/views/habit/Habit.jsx` and/or its menu button component) — add a "Skip today" action that opens the dialog.

---

## DTO reference

`HabitSkipDto.cs`:
```csharp
using System;

namespace AtomicHabits.Models.DTO
{
    public class HabitSkipDto
    {
        public int Id { get; set; }
        public int HabitId { get; set; }
        public DateOnly Date { get; set; }
        public string Reason { get; set; } = "Other"; // SkipReason name
    }

    public class HabitSkipCreateDto
    {
        public int HabitId { get; set; }
        public DateOnly? Date { get; set; }   // null => today (server uses UTC today)
        public string Reason { get; set; } = "Other";
    }
}
```

---

### Task 1: DTOs

**Files:**
- Create: `server/AtomicHabits/Models/DTO/HabitSkipDto.cs`

- [ ] **Step 1: Create the DTO file** with the exact content from the "DTO reference" block.

- [ ] **Step 2: Build**

Run (from `server/`): `dotnet build AtomicHabits/AtomicHabits.csproj`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits/Models/DTO/HabitSkipDto.cs
git commit -m "feat: add HabitSkip DTOs"
```

---

### Task 2: HabitSkipService (test-first)

**Files:**
- Create: `server/AtomicHabits/Services/HabitSkipService.cs`
- Test: `server/AtomicHabits.Tests/Services/HabitSkipServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `server/AtomicHabits.Tests/Services/HabitSkipServiceTests.cs`:
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

public class HabitSkipServiceTests
{
    private static HabitSkipService NewService(AtomicHabits.Data.AppDbContext db) =>
        new HabitSkipService(db, NullLogger<HabitSkipService>.Instance);

    private static async Task<int> SeedHabit(AtomicHabits.Data.AppDbContext db, int userId)
    {
        var h = new Habit { UserId = userId, Name = "Gym", Frequency = "Daily" };
        db.Habits.Add(h);
        await db.SaveChangesAsync();
        return h.Id;
    }

    [Fact]
    public async Task Create_persists_skip_with_reason_for_owned_habit()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var habitId = await SeedHabit(db, 1);

        var res = await svc.CreateAsync(1, new HabitSkipCreateDto
        {
            HabitId = habitId, Date = new DateOnly(2026, 6, 7), Reason = "LowEnergy"
        }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        var saved = db.HabitSkips.Single();
        saved.Reason.Should().Be(SkipReason.LowEnergy);
        saved.Date.Should().Be(new DateOnly(2026, 6, 7));
        saved.UserId.Should().Be(1);
    }

    [Fact]
    public async Task Create_for_foreign_habit_is_rejected()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var habitId = await SeedHabit(db, 2); // owned by user 2

        var res = await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Reason = "Busy" }, CancellationToken.None);

        res.IsSuccess.Should().BeFalse();
        db.HabitSkips.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_twice_same_day_updates_reason_not_duplicates()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var habitId = await SeedHabit(db, 1);
        var date = new DateOnly(2026, 6, 7);

        await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Date = date, Reason = "Busy" }, CancellationToken.None);
        await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Date = date, Reason = "Forgot" }, CancellationToken.None);

        db.HabitSkips.Should().HaveCount(1);
        db.HabitSkips.Single().Reason.Should().Be(SkipReason.Forgot);
    }

    [Fact]
    public async Task Create_with_invalid_reason_is_rejected()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var habitId = await SeedHabit(db, 1);

        var res = await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Reason = "Nonsense" }, CancellationToken.None);
        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_returns_only_owners_skips_for_habit_newest_first()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var habitId = await SeedHabit(db, 1);
        await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Date = new DateOnly(2026, 6, 1), Reason = "Busy" }, CancellationToken.None);
        await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Date = new DateOnly(2026, 6, 5), Reason = "Forgot" }, CancellationToken.None);

        var res = await svc.ListForHabitAsync(1, habitId, CancellationToken.None);
        var items = ((IEnumerable<HabitSkipDto>)res.Result!).ToList();
        items.Should().HaveCount(2);
        items.First().Date.Should().Be(new DateOnly(2026, 6, 5)); // newest first

        var foreign = await svc.ListForHabitAsync(2, habitId, CancellationToken.None);
        foreign.IsSuccess.Should().BeFalse(); // not their habit
    }

    [Fact]
    public async Task Delete_removes_owned_skip()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var habitId = await SeedHabit(db, 1);
        var create = await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Reason = "Busy" }, CancellationToken.None);
        var skipId = ((HabitSkipDto)create.Result!).Id;

        var del = await svc.DeleteAsync(1, skipId, CancellationToken.None);
        del.IsSuccess.Should().BeTrue();
        db.HabitSkips.Should().BeEmpty();

        // deleting a non-owned / unknown skip -> NotFound
        var create2 = await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Reason = "Busy" }, CancellationToken.None);
        var skip2 = ((HabitSkipDto)create2.Result!).Id;
        var delForeign = await svc.DeleteAsync(2, skip2, CancellationToken.None);
        delForeign.IsSuccess.Should().BeFalse();
        db.HabitSkips.Should().HaveCount(1);
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test server/AtomicHabits.Tests --filter HabitSkipServiceTests`
Expected: FAIL — `HabitSkipService` does not exist.

- [ ] **Step 3: Implement the service**

Create `server/AtomicHabits/Services/HabitSkipService.cs` (mirror `GoalFrameworkService` helper style exactly):
```csharp
using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IHabitSkipService
    {
        Task<ApiResponse> CreateAsync(int userId, HabitSkipCreateDto dto, CancellationToken ct);
        Task<ApiResponse> ListForHabitAsync(int userId, int habitId, CancellationToken ct);
        Task<ApiResponse> DeleteAsync(int userId, int skipId, CancellationToken ct);
    }

    public class HabitSkipService : IHabitSkipService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<HabitSkipService> _logger;

        public HabitSkipService(AppDbContext db, ILogger<HabitSkipService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse> CreateAsync(int userId, HabitSkipCreateDto dto, CancellationToken ct)
        {
            if (!await _db.Habits.AnyAsync(h => h.Id == dto.HabitId && h.UserId == userId, ct))
                return NotFound("Habit not found");

            if (!Enum.TryParse<SkipReason>(dto.Reason, true, out var reason))
                return BadRequest("Invalid reason");

            var date = dto.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);

            // One skip per habit per day — upsert the reason.
            var existing = await _db.HabitSkips
                .FirstOrDefaultAsync(s => s.UserId == userId && s.HabitId == dto.HabitId && s.Date == date, ct);

            if (existing != null)
            {
                existing.Reason = reason;
                await _db.SaveChangesAsync(ct);
                return Ok(ToDto(existing));
            }

            var skip = new HabitSkip { UserId = userId, HabitId = dto.HabitId, Date = date, Reason = reason };
            _db.HabitSkips.Add(skip);
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(skip), HttpStatusCode.Created);
        }

        public async Task<ApiResponse> ListForHabitAsync(int userId, int habitId, CancellationToken ct)
        {
            if (!await _db.Habits.AnyAsync(h => h.Id == habitId && h.UserId == userId, ct))
                return NotFound("Habit not found");

            var items = await _db.HabitSkips
                .Where(s => s.HabitId == habitId && s.UserId == userId)
                .OrderByDescending(s => s.Date)
                .Select(s => new HabitSkipDto { Id = s.Id, HabitId = s.HabitId, Date = s.Date, Reason = s.Reason.ToString() })
                .ToListAsync(ct);
            return Ok(items);
        }

        public async Task<ApiResponse> DeleteAsync(int userId, int skipId, CancellationToken ct)
        {
            var skip = await _db.HabitSkips.FirstOrDefaultAsync(s => s.Id == skipId && s.UserId == userId, ct);
            if (skip == null) return NotFound("Skip not found");
            _db.HabitSkips.Remove(skip);
            await _db.SaveChangesAsync(ct);
            return Ok(new { skipId });
        }

        private static HabitSkipDto ToDto(HabitSkip s) => new()
        {
            Id = s.Id, HabitId = s.HabitId, Date = s.Date, Reason = s.Reason.ToString()
        };

        private static ApiResponse Ok(object result, HttpStatusCode status = HttpStatusCode.OK) =>
            new() { IsSuccess = true, StatusCode = status, Result = result };
        private static ApiResponse NotFound(string message) => Error(HttpStatusCode.NotFound, message);
        private static ApiResponse BadRequest(string message) => Error(HttpStatusCode.BadRequest, message);
        private static ApiResponse Error(HttpStatusCode status, string message) =>
            new() { IsSuccess = false, StatusCode = status, ErrorMessages = new List<string> { message } };
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test server/AtomicHabits.Tests --filter HabitSkipServiceTests`
Expected: PASS (all 6 facts).

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Services/HabitSkipService.cs server/AtomicHabits.Tests/Services/HabitSkipServiceTests.cs
git commit -m "feat: add HabitSkipService (failure capture)"
```

---

### Task 3: Controller + DI

**Files:**
- Create: `server/AtomicHabits/Controllers/HabitSkipController.cs`
- Modify: `server/AtomicHabits/Program.cs`

- [ ] **Step 1: Register the service**

In `server/AtomicHabits/Program.cs`, after `IGoalFrameworkService` registration:
```csharp
builder.Services.AddScoped<IHabitSkipService, HabitSkipService>();
```

- [ ] **Step 2: Create the controller** (mirror `TagController` / `VisionController`):
```csharp
using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtomicHabits.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class HabitSkipController : ControllerBase
    {
        private readonly IHabitSkipService _service;
        public HabitSkipController(IHabitSkipService service) => _service = service;

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] HabitSkipCreateDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.CreateAsync(userId.Value, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpGet("habit/{habitId}")]
        public async Task<IActionResult> ListForHabit(int habitId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.ListForHabitAsync(userId.Value, habitId, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpDelete("{skipId}")]
        public async Task<IActionResult> Delete(int skipId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.DeleteAsync(userId.Value, skipId, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
```

- [ ] **Step 3: Build + full test run**

Run (from `server/`): `dotnet build AtomicHabits.sln` → 0 errors; `dotnet test AtomicHabits.Tests/AtomicHabits.Tests.csproj` → all pass (64 + 6 = 70).

- [ ] **Step 4: Commit**

```bash
git add server/AtomicHabits/Controllers/HabitSkipController.cs server/AtomicHabits/Program.cs
git commit -m "feat: add HabitSkip controller + DI registration"
```

---

### Task 4: Frontend — skip action in HabitContext + reason-chip dialog

**Files:**
- Modify: `client-ui/src/context/HabitContext.jsx`
- Create: `client-ui/src/views/habit/components/SkipHabitDialog.jsx`

- [ ] **Step 1: Inspect HabitContext**

Read `client-ui/src/context/HabitContext.jsx` to match its exact style (it predates this plan — note how it calls `api`, unwraps results, and exposes methods).

- [ ] **Step 2: Add `skipHabit` to HabitContext**

Add a method that POSTs to `/HabitSkip`:
```jsx
  const skipHabit = useCallback(async (habitId, reason, date = null) => {
    const res = await api.post('/HabitSkip', { habitId, reason, date });
    return res.data?.result;
  }, []);
```
Expose it in the context value object alongside the existing methods.

- [ ] **Step 3: Create the SkipHabitDialog**

Create `client-ui/src/views/habit/components/SkipHabitDialog.jsx` — an MUI `Dialog` that shows the six reasons as selectable `Chip`s (`Busy`, `Forgot`, `Low Energy`, `No Motivation`, `Schedule Conflict`, `Other` — map display labels to the enum names `Busy/Forgot/LowEnergy/NoMotivation/ScheduleConflict/Other`), a confirm button that calls `skipHabit(habitId, selectedReason)` then closes, and a cancel. Props: `open`, `onClose`, `habit`. Use the app's MUI conventions (check `client-ui/src/views/habit/components/` for an existing dialog's structure, e.g. `HabitRemindersDialog.jsx`).
```jsx
import React, { useState } from 'react';
import { Dialog, DialogTitle, DialogContent, DialogActions, Button, Chip, Stack, Typography } from '@mui/material';
import { useHabit } from '../../../context/HabitContext';
import { useSnackbar } from '../../../context/SnackbarContext';

const REASONS = [
  { value: 'Busy', label: 'Busy' },
  { value: 'Forgot', label: 'Forgot' },
  { value: 'LowEnergy', label: 'Low Energy' },
  { value: 'NoMotivation', label: 'No Motivation' },
  { value: 'ScheduleConflict', label: 'Schedule Conflict' },
  { value: 'Other', label: 'Other' },
];

const SkipHabitDialog = ({ open, onClose, habit }) => {
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);
  const { skipHabit } = useHabit();
  const snackbar = useSnackbar?.();

  const handleConfirm = async () => {
    if (!reason || !habit) return;
    setSaving(true);
    try {
      await skipHabit(habit.id, reason);
      snackbar?.show?.('Skip recorded', 'success');
      setReason('');
      onClose?.();
    } catch (e) {
      snackbar?.show?.('Could not record skip', 'error');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Why did you miss {habit?.name}?</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          A quick note helps spot what gets in your way.
        </Typography>
        <Stack direction="row" flexWrap="wrap" gap={1}>
          {REASONS.map((r) => (
            <Chip
              key={r.value}
              label={r.label}
              color={reason === r.value ? 'primary' : 'default'}
              variant={reason === r.value ? 'filled' : 'outlined'}
              onClick={() => setReason(r.value)}
            />
          ))}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button onClick={handleConfirm} variant="contained" disabled={!reason || saving}>
          Record skip
        </Button>
      </DialogActions>
    </Dialog>
  );
};

export default SkipHabitDialog;
```
NOTE: verify the actual hook names — `useHabit` (the HabitContext hook) and `useSnackbar`/`show` may differ in this codebase. Inspect `HabitContext.jsx` and `SnackbarContext.jsx` and adjust the imports/calls to the real exports. Adjust `habit?.name`/`habit.id` to the real habit object field names if different.

- [ ] **Step 4: Build**

Run (from `client-ui/`): `npx vite build`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add client-ui/src/context/HabitContext.jsx client-ui/src/views/habit/components/SkipHabitDialog.jsx
git commit -m "feat: add skip-habit action + reason-chip dialog"
```

---

### Task 5: Frontend — wire "Skip today" into the habit card menu

**Files:**
- Modify: the habit card menu component (e.g. `client-ui/src/views/habit/components/HabitMenuButton.jsx`) and `client-ui/src/views/habit/Habit.jsx`

- [ ] **Step 1: Inspect the menu**

Read `client-ui/src/views/habit/Habit.jsx` and the menu-button component it uses (Plan 2 / earlier work added Archive/Restore/Reminders menu items there — follow that exact prop pattern, e.g. `onReminders`).

- [ ] **Step 2: Add a "Skip today" menu item**

Add an optional `onSkip` prop to the menu component rendering a "Skip today" item (use a Tabler icon already imported, e.g. `IconCircleX` or `IconPlayerSkipForward` — confirm it's importable). In `Habit.jsx`, wire `onSkip={() => openSkipDialog(habit)}`, add the dialog state (`skipHabitTarget`, open/close), and render `<SkipHabitDialog open={...} habit={skipHabitTarget} onClose={...} />` once at the list level (mirror how the Reminders dialog is wired).

- [ ] **Step 3: Build**

Run (from `client-ui/`): `npx vite build`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add client-ui/src/views/habit/
git commit -m "feat: wire Skip today into habit card menu"
```

---

## Self-Review (completed by plan author)

- **Spec coverage (§3a):** one-tap reason capture (Task 4 chip dialog + Task 5 menu wiring), `HabitSkip` persistence (Task 2), six reasons matching the enum, FREE/no gating. ✓ Analysis (§3b) correctly excluded (Plan 5).
- **Dedupe:** "one skip per habit per day → upsert reason" implemented + tested (Task 2, `Create_twice_same_day_updates_reason_not_duplicates`). ✓
- **Owner-scoping:** every service method checks habit/skip ownership; tests cover foreign-habit create + foreign list + foreign delete. ✓
- **Convention match:** service-direct + `ApiResponse` helpers like `GoalFrameworkService`; controller like `TagController`; DI after `IGoalFrameworkService`. ✓
- **Placeholders:** Tasks 4-5 say "inspect the real hook/field names first" rather than inventing `useHabit`/`useSnackbar`/menu-prop signatures this plan can't verify — the behavior + reference code are given. Correct (not a placeholder failure).
- **No migration:** `HabitSkip` table already exists from Plan 1 — confirmed; no schema change. ✓
- **Type consistency:** `HabitSkipDto`/`HabitSkipCreateDto`, `IHabitSkipService` methods, controller routes (`POST /HabitSkip`, `GET /HabitSkip/habit/{id}`, `DELETE /HabitSkip/{id}`), and the frontend `skipHabit` POST `/HabitSkip` all align. ✓

## Subsequent plans
Plan 4 (entitlement + manual flag + `<RequirePro>`), Plan 5 (failure analysis insights — consumes this data), Plan 6 (CEO report), Plan 7 (Stripe).
