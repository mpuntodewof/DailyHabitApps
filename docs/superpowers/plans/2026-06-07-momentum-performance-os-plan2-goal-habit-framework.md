# Momentum Performance OS — Plan 2: Goal→Habit Framework (FREE)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the Vision→Goal→Milestone hierarchy (from Plan 1) through owner-scoped CRUD APIs and a React UI, let a habit link to a Milestone, and show a "today's habit contributes to X" payoff line. This is the FREE tier hook that proves the "performance system, not a tracker" repositioning.

**Architecture:** Backend follows the **service-direct** pattern of the existing `TagService`/`TagController` (service talks to `AppDbContext` directly — NO separate repository layer; that's the current convention for newer services). All responses use the `ApiResponse` envelope with the private `Ok/NotFound/BadRequest/Error` helpers. Controllers derive `userId` from `User.GetUserId()` and pass it down; every query is owner-scoped. Frontend adds a `GoalContext` mirroring `TagContext`, a `Goals.jsx` view, a milestone dropdown on the habit form, and a payoff line on habit cards.

**Tech Stack:** ASP.NET Core 8, EF Core 9, xUnit + FluentAssertions (existing `server/AtomicHabits.Tests`), React 19 + MUI 7 + axios.

**Spec:** `docs/superpowers/specs/2026-06-07-momentum-performance-os-v1-design.md` §2.

**Branch:** `feat/performance-os-foundation` (continues from Plan 1; do not switch).

---

## CRITICAL constraint carried from Plan 1 (FK delete behavior)

SQL Server cascade-cycle limits forced **NoAction** (not SetNull) on `Goal.VisionId`, `Habit.MilestoneId`, `Milestone.UserId`, `HabitSkip.UserId`. Therefore the delete operations in this plan MUST manually clear children first, exactly like `TagService.DeleteAsync` already does for `HabitTags`:

- **Delete Vision** → set `Goal.VisionId = null` for all goals under it, save, then delete the vision.
- **Delete Goal** → its Milestones cascade (FK_Milestones_Goals = Cascade), but each milestone's habits do NOT cascade-null, so first set `Habit.MilestoneId = null` for all habits whose `MilestoneId` is in that goal's milestones, then delete the goal (milestones cascade away).
- **Delete Milestone** → set `Habit.MilestoneId = null` for all habits under it, then delete the milestone.

Each delete has a test asserting the child rows survive with nulled FKs (not deleted, not orphaned).

---

## File Structure

**Backend (create):**
- `server/AtomicHabits/Models/DTO/GoalFrameworkDto.cs` — all DTOs for this feature (one file, like `TagDto.cs` holds Tag DTOs).
- `server/AtomicHabits/Services/GoalFrameworkService.cs` — `IGoalFrameworkService` + impl. One service covering Vision+Goal+Milestone (they're one cohesive subsystem; avoids three near-identical files).
- `server/AtomicHabits/Controllers/VisionController.cs`, `GoalController.cs`, `MilestoneController.cs` — thin controllers over the one service.
- Tests: `server/AtomicHabits.Tests/Services/GoalFrameworkServiceTests.cs`.

**Backend (modify):**
- `server/AtomicHabits/Program.cs` — register `IGoalFrameworkService` (near line 59, with the other `AddScoped` services).
- `server/AtomicHabits/Services/HabitService.cs` + DTO — accept/return `MilestoneId` on habit create/update; include milestone→goal→vision in habit reads for the payoff line.

**Frontend (create):**
- `client-ui/src/context/GoalContext.jsx` — mirrors `TagContext.jsx`.
- `client-ui/src/views/goals/Goals.jsx` — the hierarchy view.

**Frontend (modify):**
- `client-ui/src/App.jsx` — mount `GoalProvider`.
- `client-ui/src/routes/Router.jsx` — add `/goals` protected route.
- `client-ui/src/layouts/sidebar/SidebarItems.jsx` — add "Goals" nav entry.
- The habit form component — add "Contributes to…" milestone dropdown.
- The habit card component — render the payoff line.

---

## DTO reference (used across tasks)

These are the exact shapes. `GoalFrameworkDto.cs`:
```csharp
namespace AtomicHabits.Models.DTO
{
    // ----- Vision -----
    public class VisionDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
    public class VisionUpsertDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    // ----- Goal -----
    public class GoalDto
    {
        public int Id { get; set; }
        public int? VisionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = "Active"; // GoalStatus name
        public DateTime? TargetDate { get; set; }
    }
    public class GoalUpsertDto
    {
        public int? VisionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Status { get; set; }      // optional; defaults Active on create
        public DateTime? TargetDate { get; set; }
    }

    // ----- Milestone -----
    public class MilestoneDto
    {
        public int Id { get; set; }
        public int GoalId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = "Active"; // MilestoneStatus name
        public int OrderIndex { get; set; }
    }
    public class MilestoneUpsertDto
    {
        public int GoalId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Status { get; set; }
        public int OrderIndex { get; set; }
    }

    // ----- Habit payoff line (read model) -----
    public class HabitContributionDto
    {
        public int? MilestoneId { get; set; }
        public string? MilestoneTitle { get; set; }
        public string? GoalTitle { get; set; }
        public string? VisionTitle { get; set; }
    }
}
```

---

### Task 1: DTOs

**Files:**
- Create: `server/AtomicHabits/Models/DTO/GoalFrameworkDto.cs`

- [ ] **Step 1: Create the DTO file**

Create `server/AtomicHabits/Models/DTO/GoalFrameworkDto.cs` with the exact content from the "DTO reference" block above.

- [ ] **Step 2: Verify it builds**

Run (from `server/`): `dotnet build AtomicHabits/AtomicHabits.csproj`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits/Models/DTO/GoalFrameworkDto.cs
git commit -m "feat: add Goal-framework DTOs"
```

---

### Task 2: GoalFrameworkService — Vision CRUD (test-first)

**Files:**
- Create: `server/AtomicHabits/Services/GoalFrameworkService.cs`
- Test: `server/AtomicHabits.Tests/Services/GoalFrameworkServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Create `server/AtomicHabits.Tests/Services/GoalFrameworkServiceTests.cs`:
```csharp
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Threading;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class GoalFrameworkServiceTests
{
    private static GoalFrameworkService NewService(AtomicHabits.Data.AppDbContext db) =>
        new GoalFrameworkService(db, NullLogger<GoalFrameworkService>.Instance);

    [Fact]
    public async Task CreateVision_then_ListVisions_returns_it_for_owner_only()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);

        var created = await svc.CreateVisionAsync(1, new VisionUpsertDto { Title = "Remote Engineer" }, CancellationToken.None);
        created.IsSuccess.Should().BeTrue();

        var mine = await svc.ListVisionsAsync(1, CancellationToken.None);
        ((IEnumerable<VisionDto>)mine.Result!).Should().HaveCount(1);

        var others = await svc.ListVisionsAsync(2, CancellationToken.None);
        ((IEnumerable<VisionDto>)others.Result!).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteVision_nulls_child_goal_vision_id_but_keeps_the_goal()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var v = await svc.CreateVisionAsync(1, new VisionUpsertDto { Title = "V" }, CancellationToken.None);
        var visionId = ((VisionDto)v.Result!).Id;
        await svc.CreateGoalAsync(1, new GoalUpsertDto { Title = "G", VisionId = visionId }, CancellationToken.None);

        var del = await svc.DeleteVisionAsync(1, visionId, CancellationToken.None);
        del.IsSuccess.Should().BeTrue();

        db.Visions.Should().BeEmpty();
        var goal = db.Goals.Single();
        goal.VisionId.Should().BeNull(); // goal survives, FK nulled
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter GoalFrameworkServiceTests`
Expected: FAIL — `GoalFrameworkService` does not exist.

- [ ] **Step 3: Create the service with Vision + Goal CRUD**

Create `server/AtomicHabits/Services/GoalFrameworkService.cs`. Model the `ApiResponse` helpers and owner-scoping on `TagService` exactly. Include Vision CRUD AND the Goal create needed by the test (Goal CRUD is fully fleshed in Task 3 — implement both now so the service compiles and the delete test passes):
```csharp
using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IGoalFrameworkService
    {
        // Vision
        Task<ApiResponse> ListVisionsAsync(int userId, CancellationToken ct);
        Task<ApiResponse> CreateVisionAsync(int userId, VisionUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> UpdateVisionAsync(int userId, int visionId, VisionUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> DeleteVisionAsync(int userId, int visionId, CancellationToken ct);
        // Goal
        Task<ApiResponse> ListGoalsAsync(int userId, CancellationToken ct);
        Task<ApiResponse> CreateGoalAsync(int userId, GoalUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> UpdateGoalAsync(int userId, int goalId, GoalUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> DeleteGoalAsync(int userId, int goalId, CancellationToken ct);
        // Milestone
        Task<ApiResponse> ListMilestonesAsync(int userId, int goalId, CancellationToken ct);
        Task<ApiResponse> CreateMilestoneAsync(int userId, MilestoneUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> UpdateMilestoneAsync(int userId, int milestoneId, MilestoneUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> DeleteMilestoneAsync(int userId, int milestoneId, CancellationToken ct);
    }

    public class GoalFrameworkService : IGoalFrameworkService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<GoalFrameworkService> _logger;

        public GoalFrameworkService(AppDbContext db, ILogger<GoalFrameworkService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ---------- Vision ----------
        public async Task<ApiResponse> ListVisionsAsync(int userId, CancellationToken ct)
        {
            var visions = await _db.Visions
                .Where(v => v.UserId == userId)
                .OrderByDescending(v => v.CreatedAt)
                .Select(v => new VisionDto { Id = v.Id, Title = v.Title, Description = v.Description })
                .ToListAsync(ct);
            return Ok(visions);
        }

        public async Task<ApiResponse> CreateVisionAsync(int userId, VisionUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest("Title is required");
            var vision = new Vision { UserId = userId, Title = dto.Title.Trim(), Description = dto.Description };
            _db.Visions.Add(vision);
            await _db.SaveChangesAsync(ct);
            return Ok(new VisionDto { Id = vision.Id, Title = vision.Title, Description = vision.Description }, HttpStatusCode.Created);
        }

        public async Task<ApiResponse> UpdateVisionAsync(int userId, int visionId, VisionUpsertDto dto, CancellationToken ct)
        {
            var vision = await _db.Visions.FirstOrDefaultAsync(v => v.Id == visionId && v.UserId == userId, ct);
            if (vision == null) return NotFound("Vision not found");
            if (!string.IsNullOrWhiteSpace(dto.Title)) vision.Title = dto.Title.Trim();
            vision.Description = dto.Description;
            await _db.SaveChangesAsync(ct);
            return Ok(new VisionDto { Id = vision.Id, Title = vision.Title, Description = vision.Description });
        }

        public async Task<ApiResponse> DeleteVisionAsync(int userId, int visionId, CancellationToken ct)
        {
            var vision = await _db.Visions.FirstOrDefaultAsync(v => v.Id == visionId && v.UserId == userId, ct);
            if (vision == null) return NotFound("Vision not found");

            // FK_Goals_Visions_VisionId is NoAction — null child goals' VisionId before delete.
            var childGoals = await _db.Goals.Where(g => g.VisionId == visionId && g.UserId == userId).ToListAsync(ct);
            foreach (var g in childGoals) g.VisionId = null;

            _db.Visions.Remove(vision);
            await _db.SaveChangesAsync(ct);
            return Ok(new { visionId });
        }

        // ---------- Goal ----------
        public async Task<ApiResponse> ListGoalsAsync(int userId, CancellationToken ct)
        {
            var goals = await _db.Goals
                .Where(g => g.UserId == userId)
                .OrderByDescending(g => g.CreatedAt)
                .Select(g => new GoalDto
                {
                    Id = g.Id, VisionId = g.VisionId, Title = g.Title,
                    Status = g.Status.ToString(), TargetDate = g.TargetDate
                })
                .ToListAsync(ct);
            return Ok(goals);
        }

        public async Task<ApiResponse> CreateGoalAsync(int userId, GoalUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest("Title is required");
            if (dto.VisionId is int vid && !await _db.Visions.AnyAsync(v => v.Id == vid && v.UserId == userId, ct))
                return BadRequest("Vision not found");

            var goal = new Goal
            {
                UserId = userId,
                VisionId = dto.VisionId,
                Title = dto.Title.Trim(),
                Status = ParseGoalStatus(dto.Status),
                TargetDate = dto.TargetDate
            };
            _db.Goals.Add(goal);
            await _db.SaveChangesAsync(ct);
            return Ok(ToGoalDto(goal), HttpStatusCode.Created);
        }

        public async Task<ApiResponse> UpdateGoalAsync(int userId, int goalId, GoalUpsertDto dto, CancellationToken ct)
        {
            var goal = await _db.Goals.FirstOrDefaultAsync(g => g.Id == goalId && g.UserId == userId, ct);
            if (goal == null) return NotFound("Goal not found");
            if (dto.VisionId is int vid && !await _db.Visions.AnyAsync(v => v.Id == vid && v.UserId == userId, ct))
                return BadRequest("Vision not found");

            if (!string.IsNullOrWhiteSpace(dto.Title)) goal.Title = dto.Title.Trim();
            goal.VisionId = dto.VisionId;
            if (!string.IsNullOrWhiteSpace(dto.Status)) goal.Status = ParseGoalStatus(dto.Status);
            goal.TargetDate = dto.TargetDate;
            await _db.SaveChangesAsync(ct);
            return Ok(ToGoalDto(goal));
        }

        public async Task<ApiResponse> DeleteGoalAsync(int userId, int goalId, CancellationToken ct)
        {
            var goal = await _db.Goals.FirstOrDefaultAsync(g => g.Id == goalId && g.UserId == userId, ct);
            if (goal == null) return NotFound("Goal not found");

            // Milestones cascade with the goal, but habits under those milestones do NOT
            // auto-null (FK_Habits_Milestones is NoAction). Null them first.
            var milestoneIds = await _db.Milestones.Where(m => m.GoalId == goalId).Select(m => m.Id).ToListAsync(ct);
            if (milestoneIds.Count > 0)
            {
                var habits = await _db.Habits.Where(h => h.MilestoneId != null && milestoneIds.Contains(h.MilestoneId!.Value)).ToListAsync(ct);
                foreach (var h in habits) h.MilestoneId = null;
            }
            _db.Goals.Remove(goal);
            await _db.SaveChangesAsync(ct);
            return Ok(new { goalId });
        }

        // ---------- Milestone (full impl in Task 4) ----------
        public Task<ApiResponse> ListMilestonesAsync(int userId, int goalId, CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResponse> CreateMilestoneAsync(int userId, MilestoneUpsertDto dto, CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResponse> UpdateMilestoneAsync(int userId, int milestoneId, MilestoneUpsertDto dto, CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResponse> DeleteMilestoneAsync(int userId, int milestoneId, CancellationToken ct) => throw new NotImplementedException();

        // ---------- helpers ----------
        private static GoalDto ToGoalDto(Goal g) => new()
        {
            Id = g.Id, VisionId = g.VisionId, Title = g.Title, Status = g.Status.ToString(), TargetDate = g.TargetDate
        };
        private static GoalStatus ParseGoalStatus(string? s) =>
            Enum.TryParse<GoalStatus>(s, true, out var v) ? v : GoalStatus.Active;

        private static ApiResponse Ok(object result, HttpStatusCode status = HttpStatusCode.OK) =>
            new() { IsSuccess = true, StatusCode = status, Result = result };
        private static ApiResponse NotFound(string message) => Error(HttpStatusCode.NotFound, message);
        private static ApiResponse BadRequest(string message) => Error(HttpStatusCode.BadRequest, message);
        private static ApiResponse Error(HttpStatusCode status, string message) =>
            new() { IsSuccess = false, StatusCode = status, ErrorMessages = new List<string> { message } };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test server/AtomicHabits.Tests --filter GoalFrameworkServiceTests`
Expected: PASS (both facts).

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Services/GoalFrameworkService.cs server/AtomicHabits.Tests/Services/GoalFrameworkServiceTests.cs
git commit -m "feat: add GoalFrameworkService with Vision + Goal CRUD"
```

---

### Task 3: Goal CRUD tests (lock in behavior)

**Files:**
- Modify: `server/AtomicHabits.Tests/Services/GoalFrameworkServiceTests.cs`

- [ ] **Step 1: Add Goal tests**

Append to the test class:
```csharp
    [Fact]
    public async Task CreateGoal_defaults_status_Active_and_is_owner_scoped()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var res = await svc.CreateGoalAsync(1, new GoalUpsertDto { Title = "Get a job" }, CancellationToken.None);
        ((GoalDto)res.Result!).Status.Should().Be("Active");

        var others = await svc.ListGoalsAsync(2, CancellationToken.None);
        ((IEnumerable<GoalDto>)others.Result!).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateGoal_with_foreign_vision_is_rejected()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        // vision owned by user 2
        var v = await svc.CreateVisionAsync(2, new VisionUpsertDto { Title = "theirs" }, CancellationToken.None);
        var visionId = ((VisionDto)v.Result!).Id;

        var res = await svc.CreateGoalAsync(1, new GoalUpsertDto { Title = "G", VisionId = visionId }, CancellationToken.None);
        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteGoal_nulls_milestone_habits_and_removes_milestones()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var g = await svc.CreateGoalAsync(1, new GoalUpsertDto { Title = "G" }, CancellationToken.None);
        var goalId = ((GoalDto)g.Result!).Id;
        var milestone = new Milestone { UserId = 1, GoalId = goalId, Title = "M" };
        db.Milestones.Add(milestone);
        await db.SaveChangesAsync();
        var habit = new Habit { UserId = 1, Name = "H", Frequency = "Daily", MilestoneId = milestone.Id };
        db.Habits.Add(habit);
        await db.SaveChangesAsync();

        var del = await svc.DeleteGoalAsync(1, goalId, CancellationToken.None);
        del.IsSuccess.Should().BeTrue();

        db.Goals.Should().BeEmpty();
        db.Milestones.Should().BeEmpty();          // cascaded with goal
        db.Habits.Single().MilestoneId.Should().BeNull(); // habit survives, FK nulled
    }
```

- [ ] **Step 2: Run to verify they pass**

Run: `dotnet test server/AtomicHabits.Tests --filter GoalFrameworkServiceTests`
Expected: PASS (all 5 facts). The Goal CRUD was implemented in Task 2; these tests lock it in.

> Note: EF InMemory does NOT cascade-delete Milestones when the Goal is removed.
> To make `DeleteGoal_nulls_milestone_habits_and_removes_milestones` accurate on
> InMemory, the service's `DeleteGoalAsync` must explicitly remove the goal's
> milestones too (InMemory won't do it for us, and being explicit is also safer
> on SQL Server). UPDATE `DeleteGoalAsync` to add, before `_db.Goals.Remove(goal)`:
> ```csharp
>             var milestones = await _db.Milestones.Where(m => m.GoalId == goalId).ToListAsync(ct);
>             if (milestones.Count > 0) _db.Milestones.RemoveRange(milestones);
> ```
> (Re-run the test after this change; commit the service tweak with the tests.)

- [ ] **Step 3: Commit**

```bash
git add server/AtomicHabits/Services/GoalFrameworkService.cs server/AtomicHabits.Tests/Services/GoalFrameworkServiceTests.cs
git commit -m "test: lock in Goal CRUD + explicit milestone cleanup on goal delete"
```

---

### Task 4: Milestone CRUD (test-first)

**Files:**
- Modify: `server/AtomicHabits/Services/GoalFrameworkService.cs` (replace the 4 NotImplementedException stubs)
- Modify: `server/AtomicHabits.Tests/Services/GoalFrameworkServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to the test class:
```csharp
    [Fact]
    public async Task CreateMilestone_requires_owned_goal_and_defaults_active()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var g = await svc.CreateGoalAsync(1, new GoalUpsertDto { Title = "G" }, CancellationToken.None);
        var goalId = ((GoalDto)g.Result!).Id;

        var ok = await svc.CreateMilestoneAsync(1, new MilestoneUpsertDto { GoalId = goalId, Title = "Build portfolio" }, CancellationToken.None);
        ((MilestoneDto)ok.Result!).Status.Should().Be("Active");

        // goal owned by someone else -> rejected
        var bad = await svc.CreateMilestoneAsync(2, new MilestoneUpsertDto { GoalId = goalId, Title = "x" }, CancellationToken.None);
        bad.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ListMilestones_returns_goal_milestones_ordered_by_OrderIndex()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var g = await svc.CreateGoalAsync(1, new GoalUpsertDto { Title = "G" }, CancellationToken.None);
        var goalId = ((GoalDto)g.Result!).Id;
        await svc.CreateMilestoneAsync(1, new MilestoneUpsertDto { GoalId = goalId, Title = "second", OrderIndex = 1 }, CancellationToken.None);
        await svc.CreateMilestoneAsync(1, new MilestoneUpsertDto { GoalId = goalId, Title = "first", OrderIndex = 0 }, CancellationToken.None);

        var list = await svc.ListMilestonesAsync(1, goalId, CancellationToken.None);
        var items = ((IEnumerable<MilestoneDto>)list.Result!).ToList();
        items.Select(m => m.Title).Should().ContainInOrder("first", "second");
    }

    [Fact]
    public async Task DeleteMilestone_nulls_linked_habits_but_keeps_them()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var g = await svc.CreateGoalAsync(1, new GoalUpsertDto { Title = "G" }, CancellationToken.None);
        var goalId = ((GoalDto)g.Result!).Id;
        var m = await svc.CreateMilestoneAsync(1, new MilestoneUpsertDto { GoalId = goalId, Title = "M" }, CancellationToken.None);
        var milestoneId = ((MilestoneDto)m.Result!).Id;
        db.Habits.Add(new Habit { UserId = 1, Name = "H", Frequency = "Daily", MilestoneId = milestoneId });
        await db.SaveChangesAsync();

        var del = await svc.DeleteMilestoneAsync(1, milestoneId, CancellationToken.None);
        del.IsSuccess.Should().BeTrue();
        db.Milestones.Should().BeEmpty();
        db.Habits.Single().MilestoneId.Should().BeNull();
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test server/AtomicHabits.Tests --filter GoalFrameworkServiceTests`
Expected: FAIL — milestone methods throw `NotImplementedException`.

- [ ] **Step 3: Implement the 4 milestone methods**

In `GoalFrameworkService.cs`, replace the four milestone stubs with:
```csharp
        public async Task<ApiResponse> ListMilestonesAsync(int userId, int goalId, CancellationToken ct)
        {
            if (!await _db.Goals.AnyAsync(g => g.Id == goalId && g.UserId == userId, ct))
                return NotFound("Goal not found");
            var items = await _db.Milestones
                .Where(m => m.GoalId == goalId && m.UserId == userId)
                .OrderBy(m => m.OrderIndex)
                .Select(m => new MilestoneDto
                {
                    Id = m.Id, GoalId = m.GoalId, Title = m.Title,
                    Status = m.Status.ToString(), OrderIndex = m.OrderIndex
                })
                .ToListAsync(ct);
            return Ok(items);
        }

        public async Task<ApiResponse> CreateMilestoneAsync(int userId, MilestoneUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest("Title is required");
            if (!await _db.Goals.AnyAsync(g => g.Id == dto.GoalId && g.UserId == userId, ct))
                return BadRequest("Goal not found");

            var m = new Milestone
            {
                UserId = userId,
                GoalId = dto.GoalId,
                Title = dto.Title.Trim(),
                Status = ParseMilestoneStatus(dto.Status),
                OrderIndex = dto.OrderIndex
            };
            _db.Milestones.Add(m);
            await _db.SaveChangesAsync(ct);
            return Ok(ToMilestoneDto(m), HttpStatusCode.Created);
        }

        public async Task<ApiResponse> UpdateMilestoneAsync(int userId, int milestoneId, MilestoneUpsertDto dto, CancellationToken ct)
        {
            var m = await _db.Milestones.FirstOrDefaultAsync(x => x.Id == milestoneId && x.UserId == userId, ct);
            if (m == null) return NotFound("Milestone not found");
            if (!string.IsNullOrWhiteSpace(dto.Title)) m.Title = dto.Title.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Status)) m.Status = ParseMilestoneStatus(dto.Status);
            m.OrderIndex = dto.OrderIndex;
            await _db.SaveChangesAsync(ct);
            return Ok(ToMilestoneDto(m));
        }

        public async Task<ApiResponse> DeleteMilestoneAsync(int userId, int milestoneId, CancellationToken ct)
        {
            var m = await _db.Milestones.FirstOrDefaultAsync(x => x.Id == milestoneId && x.UserId == userId, ct);
            if (m == null) return NotFound("Milestone not found");

            // FK_Habits_Milestones is NoAction — null linked habits before delete.
            var habits = await _db.Habits.Where(h => h.MilestoneId == milestoneId).ToListAsync(ct);
            foreach (var h in habits) h.MilestoneId = null;

            _db.Milestones.Remove(m);
            await _db.SaveChangesAsync(ct);
            return Ok(new { milestoneId });
        }
```
And add these helpers next to `ToGoalDto`:
```csharp
        private static MilestoneDto ToMilestoneDto(Milestone m) => new()
        {
            Id = m.Id, GoalId = m.GoalId, Title = m.Title, Status = m.Status.ToString(), OrderIndex = m.OrderIndex
        };
        private static MilestoneStatus ParseMilestoneStatus(string? s) =>
            Enum.TryParse<MilestoneStatus>(s, true, out var v) ? v : MilestoneStatus.Active;
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test server/AtomicHabits.Tests --filter GoalFrameworkServiceTests`
Expected: PASS (all 8 facts).

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits/Services/GoalFrameworkService.cs server/AtomicHabits.Tests/Services/GoalFrameworkServiceTests.cs
git commit -m "feat: add Milestone CRUD to GoalFrameworkService"
```

---

### Task 5: Controllers + DI registration

**Files:**
- Create: `server/AtomicHabits/Controllers/VisionController.cs`, `GoalController.cs`, `MilestoneController.cs`
- Modify: `server/AtomicHabits/Program.cs`

- [ ] **Step 1: Register the service**

In `server/AtomicHabits/Program.cs`, after the `ITagService` registration (line ~59):
```csharp
builder.Services.AddScoped<IGoalFrameworkService, GoalFrameworkService>();
```

- [ ] **Step 2: Create VisionController**

Create `server/AtomicHabits/Controllers/VisionController.cs` (mirror `TagController` exactly — `[Authorize]`, `User.GetUserId()`, `StatusCode((int)res.StatusCode, res)`):
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
    public class VisionController : ControllerBase
    {
        private readonly IGoalFrameworkService _service;
        public VisionController(IGoalFrameworkService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.ListVisionsAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] VisionUpsertDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.CreateVisionAsync(userId.Value, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPut("{visionId}")]
        public async Task<IActionResult> Update(int visionId, [FromBody] VisionUpsertDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.UpdateVisionAsync(userId.Value, visionId, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpDelete("{visionId}")]
        public async Task<IActionResult> Delete(int visionId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.DeleteVisionAsync(userId.Value, visionId, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
```

- [ ] **Step 3: Create GoalController**

Create `server/AtomicHabits/Controllers/GoalController.cs` — same shape, methods `ListGoalsAsync`/`CreateGoalAsync`/`UpdateGoalAsync`/`DeleteGoalAsync`, route param `{goalId}`, DTO `GoalUpsertDto`.

- [ ] **Step 4: Create MilestoneController**

Create `server/AtomicHabits/Controllers/MilestoneController.cs` — methods `CreateMilestoneAsync`/`UpdateMilestoneAsync`/`DeleteMilestoneAsync` with `{milestoneId}` + `MilestoneUpsertDto`, AND a list route nested under goal:
```csharp
        [HttpGet("goal/{goalId}")]
        public async Task<IActionResult> ListForGoal(int goalId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.ListMilestonesAsync(userId.Value, goalId, ct);
            return StatusCode((int)res.StatusCode, res);
        }
```

- [ ] **Step 5: Build**

Run (from `server/`): `dotnet build AtomicHabits.sln`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/Controllers/VisionController.cs server/AtomicHabits/Controllers/GoalController.cs server/AtomicHabits/Controllers/MilestoneController.cs server/AtomicHabits/Program.cs
git commit -m "feat: add Vision/Goal/Milestone controllers + DI registration"
```

---

### Task 6: Habit ↔ Milestone link in HabitService + payoff data

**Files:**
- Modify: `server/AtomicHabits/Services/HabitService.cs` and the Habit DTO it uses
- Test: add to `server/AtomicHabits.Tests/Services/` (find the existing Habit service test file; if none, create `HabitMilestoneServiceTests.cs`)

- [ ] **Step 1: Inspect the existing HabitService + Habit DTO**

Read `server/AtomicHabits/Services/HabitService.cs` and `server/AtomicHabits/Models/DTO/HabitDTO.cs`. Identify the create and update methods and the DTO used for create/update and for reads. Note exact method names and DTO property style (this plan can't hardcode them — they predate it).

- [ ] **Step 2: Add `MilestoneId` to the habit create/update DTO**

Add a nullable `public int? MilestoneId { get; set; }` to the habit create/update DTO. In `HabitService` create/update, set `habit.MilestoneId = dto.MilestoneId;` (validate ownership: if `dto.MilestoneId` is set, confirm `_db.Milestones.AnyAsync(m => m.Id == dto.MilestoneId && m.UserId == userId)`, else reject with BadRequest — match the service's existing error style).

- [ ] **Step 3: Write a failing test for the link + payoff**

Create/extend a test asserting: creating a habit with a valid owned `MilestoneId` persists it; creating with a foreign milestone is rejected; and a read method returns the contribution chain (milestone/goal/vision titles) for a linked habit. Use `TestDbContextFactory` and the real `HabitService` constructor (inspect its dependencies first — provide `NullLogger` and any required collaborators; if `HabitService` has heavy dependencies, test the new logic at the service method you added rather than the whole pipeline).

- [ ] **Step 4: Add a payoff read**

Add a method (e.g. `GetContributionAsync(userId, habitId)`) returning `HabitContributionDto` by walking `Habit.MilestoneId → Milestone → Goal → Vision`, OR include those titles in the existing habit read projection. Prefer extending the existing habit read so the UI gets it for free. Implementation walks:
```csharp
// pseudo within a projection:
MilestoneTitle = h.Milestone != null ? h.Milestone.Title : null,
GoalTitle = h.Milestone != null && h.Milestone.Goal != null ? h.Milestone.Goal.Title : null,
VisionTitle = h.Milestone != null && h.Milestone.Goal != null && h.Milestone.Goal.Vision != null ? h.Milestone.Goal.Vision.Title : null,
```
Ensure the read `.Include(...)` or projection navigates Milestone→Goal→Vision.

- [ ] **Step 5: Run tests + full build**

Run (from `server/`): `dotnet test AtomicHabits.Tests/AtomicHabits.Tests.csproj`
Expected: all pass (new + existing). `dotnet build AtomicHabits.sln` → 0 errors.

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/Services/HabitService.cs server/AtomicHabits/Models/DTO/HabitDTO.cs server/AtomicHabits.Tests/Services/
git commit -m "feat: link habit to milestone + expose contribution chain"
```

---

### Task 7: Frontend — GoalContext

**Files:**
- Create: `client-ui/src/context/GoalContext.jsx`
- Modify: `client-ui/src/App.jsx`

- [ ] **Step 1: Create GoalContext**

Create `client-ui/src/context/GoalContext.jsx`, mirroring `TagContext.jsx` (same `getAccessToken()` guard, axios `api` instance, `res.data?.result` unwrap). Expose: `visions, goals, loading, fetchAll, createVision, updateVision, deleteVision, createGoal, updateGoal, deleteGoal, listMilestones, createMilestone, updateMilestone, deleteMilestone`. Endpoints: `/Vision`, `/Goal`, `/Milestone` and `/Milestone/goal/{goalId}` for listing.
```jsx
import React, { createContext, useCallback, useContext, useEffect, useState } from 'react';
import api from '../api/axiosInstance';
import { getAccessToken } from '../utils/tokenUtils';

const GoalContext = createContext(undefined);

export const useGoals = () => {
  const ctx = useContext(GoalContext);
  if (!ctx) throw new Error('useGoals must be used within GoalProvider');
  return ctx;
};

export const GoalProvider = ({ children }) => {
  const [visions, setVisions] = useState([]);
  const [goals, setGoals] = useState([]);
  const [loading, setLoading] = useState(false);

  const fetchAll = useCallback(async () => {
    if (!getAccessToken()) return;
    setLoading(true);
    try {
      const [v, g] = await Promise.all([api.get('/Vision'), api.get('/Goal')]);
      setVisions(v.data?.result || []);
      setGoals(g.data?.result || []);
    } catch (err) {
      console.error('Failed to load goals:', err.message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchAll(); }, [fetchAll]);

  const createVision = useCallback(async (dto) => {
    const res = await api.post('/Vision', dto);
    const created = res.data?.result;
    if (created) setVisions((p) => [created, ...p]);
    return created;
  }, []);
  const updateVision = useCallback(async (id, dto) => {
    const res = await api.put(`/Vision/${id}`, dto);
    const u = res.data?.result;
    if (u) setVisions((p) => p.map((x) => (x.id === id ? u : x)));
    return u;
  }, []);
  const deleteVision = useCallback(async (id) => {
    await api.delete(`/Vision/${id}`);
    setVisions((p) => p.filter((x) => x.id !== id));
    // goals under it lose their visionId server-side; refresh goals
    const g = await api.get('/Goal');
    setGoals(g.data?.result || []);
  }, []);

  const createGoal = useCallback(async (dto) => {
    const res = await api.post('/Goal', dto);
    const created = res.data?.result;
    if (created) setGoals((p) => [created, ...p]);
    return created;
  }, []);
  const updateGoal = useCallback(async (id, dto) => {
    const res = await api.put(`/Goal/${id}`, dto);
    const u = res.data?.result;
    if (u) setGoals((p) => p.map((x) => (x.id === id ? u : x)));
    return u;
  }, []);
  const deleteGoal = useCallback(async (id) => {
    await api.delete(`/Goal/${id}`);
    setGoals((p) => p.filter((x) => x.id !== id));
  }, []);

  const listMilestones = useCallback(async (goalId) => {
    const res = await api.get(`/Milestone/goal/${goalId}`);
    return res.data?.result || [];
  }, []);
  const createMilestone = useCallback(async (dto) => {
    const res = await api.post('/Milestone', dto);
    return res.data?.result;
  }, []);
  const updateMilestone = useCallback(async (id, dto) => {
    const res = await api.put(`/Milestone/${id}`, dto);
    return res.data?.result;
  }, []);
  const deleteMilestone = useCallback(async (id) => {
    await api.delete(`/Milestone/${id}`);
  }, []);

  return (
    <GoalContext.Provider value={{
      visions, goals, loading, fetchAll,
      createVision, updateVision, deleteVision,
      createGoal, updateGoal, deleteGoal,
      listMilestones, createMilestone, updateMilestone, deleteMilestone,
    }}>
      {children}
    </GoalContext.Provider>
  );
};
```

- [ ] **Step 2: Mount the provider**

In `client-ui/src/App.jsx`, import `GoalProvider` and wrap it inside the existing provider tree alongside `TagProvider` (same nesting level).

- [ ] **Step 3: Build**

Run (from `client-ui/`): `npx vite build`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add client-ui/src/context/GoalContext.jsx client-ui/src/App.jsx
git commit -m "feat: add GoalContext provider"
```

---

### Task 8: Frontend — Goals view + route + sidebar

**Files:**
- Create: `client-ui/src/views/goals/Goals.jsx`
- Modify: `client-ui/src/routes/Router.jsx`, `client-ui/src/layouts/sidebar/SidebarItems.jsx`

- [ ] **Step 1: Build the Goals view**

Create `client-ui/src/views/goals/Goals.jsx` using MUI (match existing views' import style and `PageContainer` usage — check `client-ui/src/views/settings/Settings.jsx` for the page wrapper pattern). Render an accordion/tree: Visions (each expandable), Goals (grouped by vision + an "Unassigned" group for goals with null visionId), and under each goal its Milestones (lazy-loaded via `listMilestones(goalId)` on expand). Provide create/edit/delete dialogs for each level using `useGoals()`. Keep it simple — no drag-and-drop, no progress bars. Each goal shows its `status` as a small chip.

- [ ] **Step 2: Add the route**

In `client-ui/src/routes/Router.jsx`, add to the protected children array (the pathless layout group that holds `/dashboard`, `/habits`, etc.):
```jsx
      { path: '/goals', exact: true, element: <Goals /> },
```
And add the lazy import near the others:
```jsx
const Goals = lazy(() => import('../views/goals/Goals'));
```

- [ ] **Step 3: Add the sidebar entry**

In `client-ui/src/layouts/sidebar/SidebarItems.jsx`, add a "Goals" menu item (use an appropriate Tabler icon already imported there, e.g. `IconTargetArrow` — confirm it exists in the import set or add it) pointing to `/goals`. Place it near the Habits entry.

- [ ] **Step 4: Build**

Run (from `client-ui/`): `npx vite build`
Expected: build succeeds. (No frontend test harness exists — verification is build + manual.)

- [ ] **Step 5: Commit**

```bash
git add client-ui/src/views/goals/Goals.jsx client-ui/src/routes/Router.jsx client-ui/src/layouts/sidebar/SidebarItems.jsx
git commit -m "feat: add Goals view, route, and sidebar entry"
```

---

### Task 9: Frontend — milestone dropdown on habit form + payoff line on habit card

**Files:**
- Modify: the habit create/edit form component, and the habit card component (locate under `client-ui/src/views/habit/`)

- [ ] **Step 1: Locate the components**

Find the habit form (e.g. `client-ui/src/views/habit/components/HabitDialogForm.jsx`) and the habit card/list in `client-ui/src/views/habit/Habit.jsx`. Read them to learn the field + props patterns.

- [ ] **Step 2: Add the "Contributes to…" dropdown**

In the habit form, add an optional MUI `Select` labeled "Contributes to" populated from `useGoals()` milestones. Since milestones are per-goal, present a two-level choice (Goal → Milestone) or a flattened list "GoalTitle › MilestoneTitle". Load options by iterating goals and calling `listMilestones`. Bind the selected milestone id to the habit payload's `milestoneId`. Empty selection = no link (sends null).

- [ ] **Step 3: Render the payoff line**

On the habit card, when the habit's read data includes a milestone/goal/vision chain (from Task 6), render a small line: `Contributes to: {goalTitle}` (prefer goal title; fall back to milestone or vision title). Style it muted/secondary. If no link, render nothing.

- [ ] **Step 4: Build**

Run (from `client-ui/`): `npx vite build`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add client-ui/src/views/habit/
git commit -m "feat: habit milestone dropdown + contribution payoff line"
```

---

## Self-Review (completed by plan author)

- **Spec coverage (§2):** Goals view (T8), Vision/Goal/Milestone CRUD backend (T2–T5), `GoalContext` (T7), "Contributes to…" dropdown (T9), payoff line (T6 data + T9 UI). All §2 pieces covered. ✓
- **FK delete constraint:** DeleteVision/DeleteGoal/DeleteMilestone each null children first, each with a test asserting children survive with null FK (T2, T3, T4). Directly honors the Plan 1 carried constraint. ✓
- **Convention match:** service-direct (no repo) like `TagService`; `ApiResponse` + private helpers; `User.GetUserId()` controllers; `GoalContext` mirrors `TagContext`. ✓
- **Placeholders:** Tasks 6 and 9 intentionally say "inspect the existing X first" rather than hardcoding pre-existing method/prop names this plan can't know — the steps give exact behavior + code patterns, just not invented signatures. This is correct (avoids referencing types that may not match reality), not a placeholder failure.
- **Type consistency:** DTO names (`VisionDto`/`VisionUpsertDto`/`GoalDto`/`GoalUpsertDto`/`MilestoneDto`/`MilestoneUpsertDto`/`HabitContributionDto`), service interface method names, and controller calls all match across T1–T6. `ParseGoalStatus`/`ParseMilestoneStatus` + `ToGoalDto`/`ToMilestoneDto` defined where used. ✓
- **Known InMemory caveat:** flagged in T3 that the service must explicitly remove milestones on goal delete (InMemory won't cascade) — both correct on SQL Server and necessary for the test.

## Subsequent plans (unchanged from Plan 1 roadmap)
Plan 3 (failure capture), Plan 4 (entitlement + manual flag), Plan 5 (insights), Plan 6 (CEO report), Plan 7 (Stripe).
