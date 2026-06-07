# Momentum Performance OS — Plan 1: Test Harness + Data Model Foundation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up an xUnit backend test project, then add the Vision/Goal/Milestone/HabitSkip entities and the nullable `Habit.MilestoneId` FK, test-first, with one EF migration.

**Architecture:** Establishes TDD for the backend via a new `AtomicHabits.Tests` xUnit project using EF Core's InMemory provider for fast model/repository tests. Adds four owner-scoped entities above `Habit`, following the existing `[Key]/[Required]/[ForeignKey]/[JsonIgnore]` data-annotation conventions and the `OnModelCreating` named-index style. This plan is the foundation — Plans 2–7 (controllers, features, billing) build on it.

**Tech Stack:** ASP.NET Core 8, EF Core 9, xUnit, `Microsoft.EntityFrameworkCore.InMemory`, FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-06-07-momentum-performance-os-v1-design.md` §1.

---

### Task 1: Create the xUnit test project

**Files:**
- Create: `server/AtomicHabits.Tests/AtomicHabits.Tests.csproj`
- Create: `server/AtomicHabits.Tests/TestDbContextFactory.cs`
- Modify: `server/AtomicHabits.sln`

- [ ] **Step 1: Create the test project**

Run (from `server/`):
```bash
dotnet new xunit -n AtomicHabits.Tests -o AtomicHabits.Tests
dotnet add AtomicHabits.Tests/AtomicHabits.Tests.csproj reference AtomicHabits/AtomicHabits.csproj
dotnet add AtomicHabits.Tests/AtomicHabits.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory
dotnet add AtomicHabits.Tests/AtomicHabits.Tests.csproj package FluentAssertions
dotnet sln AtomicHabits.sln add AtomicHabits.Tests/AtomicHabits.Tests.csproj
```
Expected: project created, reference + packages added, solution updated.

- [ ] **Step 2: Add an in-memory DbContext factory for tests**

Create `server/AtomicHabits.Tests/TestDbContextFactory.cs`:
```csharp
using AtomicHabits.Data;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Tests;

/// <summary>
/// Builds an isolated in-memory AppDbContext per test. Each call uses a unique
/// database name so tests never share state.
/// </summary>
public static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
```

- [ ] **Step 3: Delete the template placeholder test**

Run: `rm server/AtomicHabits.Tests/UnitTest1.cs`
Expected: file removed (the `dotnet new xunit` template stub).

- [ ] **Step 4: Verify the test project builds and runs**

Run (from `server/`): `dotnet test AtomicHabits.Tests/AtomicHabits.Tests.csproj`
Expected: build succeeds, `Passed! - Failed: 0, Passed: 0` (no tests yet).

- [ ] **Step 5: Commit**

```bash
git add server/AtomicHabits.Tests server/AtomicHabits.sln
git commit -m "test: add xUnit test project with in-memory DbContext factory"
```

---

### Task 2: Vision entity

**Files:**
- Create: `server/AtomicHabits/Models/Vision.cs`
- Modify: `server/AtomicHabits/Data/AppDbContext.cs` (add DbSet + index)
- Test: `server/AtomicHabits.Tests/Models/VisionTests.cs`

- [ ] **Step 1: Write the failing test**

Create `server/AtomicHabits.Tests/Models/VisionTests.cs`:
```csharp
using AtomicHabits.Models;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Models;

public class VisionTests
{
    [Fact]
    public async Task Vision_persists_and_round_trips_with_owner()
    {
        using var db = TestDbContextFactory.Create();
        var vision = new Vision { UserId = 7, Title = "Become a Remote Backend Engineer" };

        db.Visions.Add(vision);
        await db.SaveChangesAsync();

        var saved = db.Visions.Single();
        saved.UserId.Should().Be(7);
        saved.Title.Should().Be("Become a Remote Backend Engineer");
        saved.CreatedAt.Should().NotBe(default);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter VisionTests`
Expected: FAIL — compile error, `Vision` and `db.Visions` do not exist.

- [ ] **Step 3: Create the Vision model**

Create `server/AtomicHabits/Models/Vision.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AtomicHabits.Models
{
    public class Vision
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        [JsonIgnore]
        public User? User { get; set; }

        [JsonIgnore]
        public ICollection<Goal> Goals { get; set; } = new List<Goal>();
    }
}
```

> Note: this references `Goal` (Task 3). If implementing strictly task-by-task,
> the `Goals` navigation will not compile until Task 3's model exists. Implement
> Tasks 2 and 3 together, or temporarily comment the `Goals` line and uncomment
> in Task 3. The plan assumes you implement 2–5 (the models) before running the
> combined model test in Task 6.

- [ ] **Step 4: Register the DbSet + index**

In `server/AtomicHabits/Data/AppDbContext.cs`, add after the `TwoFactorRecoveryCodes` DbSet (line ~27):
```csharp
        public DbSet<Vision> Visions { get; set; }
```
And in `OnModelCreating`, after the `TwoFactorRecoveryCode` index block (line ~119):
```csharp
            modelBuilder.Entity<Vision>()
                .HasIndex(v => v.UserId)
                .HasDatabaseName("IX_Visions_UserId");
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test server/AtomicHabits.Tests --filter VisionTests`
Expected: PASS (after Task 3 exists, so `Goal` resolves).

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/Models/Vision.cs server/AtomicHabits/Data/AppDbContext.cs server/AtomicHabits.Tests/Models/VisionTests.cs
git commit -m "feat: add Vision entity"
```

---

### Task 3: Goal entity

**Files:**
- Create: `server/AtomicHabits/Models/Goal.cs`
- Modify: `server/AtomicHabits/Data/AppDbContext.cs`
- Test: `server/AtomicHabits.Tests/Models/GoalTests.cs`

- [ ] **Step 1: Write the failing test**

Create `server/AtomicHabits.Tests/Models/GoalTests.cs`:
```csharp
using AtomicHabits.Models;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Models;

public class GoalTests
{
    [Fact]
    public async Task Goal_defaults_to_Active_and_links_to_vision()
    {
        using var db = TestDbContextFactory.Create();
        var vision = new Vision { UserId = 1, Title = "Remote Engineer" };
        db.Visions.Add(vision);
        await db.SaveChangesAsync();

        var goal = new Goal { UserId = 1, VisionId = vision.Id, Title = "Get a Remote Job" };
        db.Goals.Add(goal);
        await db.SaveChangesAsync();

        var saved = db.Goals.Single();
        saved.Status.Should().Be(GoalStatus.Active);
        saved.VisionId.Should().Be(vision.Id);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter GoalTests`
Expected: FAIL — `Goal` / `GoalStatus` / `db.Goals` do not exist.

- [ ] **Step 3: Create the Goal model + status enum**

Create `server/AtomicHabits/Models/Goal.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AtomicHabits.Models
{
    public enum GoalStatus { Active = 0, Achieved = 1, Abandoned = 2 }

    public class Goal
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        public int? VisionId { get; set; }

        [Required]
        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        public GoalStatus Status { get; set; } = GoalStatus.Active;

        public DateTime? TargetDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        [JsonIgnore]
        public User? User { get; set; }

        [ForeignKey("VisionId")]
        [JsonIgnore]
        public Vision? Vision { get; set; }

        [JsonIgnore]
        public ICollection<Milestone> Milestones { get; set; } = new List<Milestone>();
    }
}
```

- [ ] **Step 4: Register the DbSet + indexes**

In `AppDbContext.cs` add DbSet:
```csharp
        public DbSet<Goal> Goals { get; set; }
```
In `OnModelCreating`:
```csharp
            modelBuilder.Entity<Goal>()
                .HasIndex(g => g.UserId)
                .HasDatabaseName("IX_Goals_UserId");

            modelBuilder.Entity<Goal>()
                .HasOne(g => g.Vision)
                .WithMany(v => v.Goals)
                .HasForeignKey(g => g.VisionId)
                .OnDelete(DeleteBehavior.SetNull);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test server/AtomicHabits.Tests --filter GoalTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/Models/Goal.cs server/AtomicHabits/Data/AppDbContext.cs server/AtomicHabits.Tests/Models/GoalTests.cs
git commit -m "feat: add Goal entity with status enum"
```

---

### Task 4: Milestone entity

**Files:**
- Create: `server/AtomicHabits/Models/Milestone.cs`
- Modify: `server/AtomicHabits/Data/AppDbContext.cs`
- Test: `server/AtomicHabits.Tests/Models/MilestoneTests.cs`

- [ ] **Step 1: Write the failing test**

Create `server/AtomicHabits.Tests/Models/MilestoneTests.cs`:
```csharp
using AtomicHabits.Models;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Models;

public class MilestoneTests
{
    [Fact]
    public async Task Milestone_links_to_goal_and_defaults_active()
    {
        using var db = TestDbContextFactory.Create();
        var goal = new Goal { UserId = 1, Title = "Get a Remote Job" };
        db.Goals.Add(goal);
        await db.SaveChangesAsync();

        var milestone = new Milestone { UserId = 1, GoalId = goal.Id, Title = "Build Portfolio", OrderIndex = 0 };
        db.Milestones.Add(milestone);
        await db.SaveChangesAsync();

        var saved = db.Milestones.Single();
        saved.GoalId.Should().Be(goal.Id);
        saved.Status.Should().Be(MilestoneStatus.Active);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter MilestoneTests`
Expected: FAIL — `Milestone` / `MilestoneStatus` / `db.Milestones` missing.

- [ ] **Step 3: Create the Milestone model + status enum**

Create `server/AtomicHabits/Models/Milestone.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AtomicHabits.Models
{
    public enum MilestoneStatus { Active = 0, Done = 1 }

    public class Milestone
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int GoalId { get; set; }

        [Required]
        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        public MilestoneStatus Status { get; set; } = MilestoneStatus.Active;

        public int OrderIndex { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        [JsonIgnore]
        public User? User { get; set; }

        [ForeignKey("GoalId")]
        [JsonIgnore]
        public Goal? Goal { get; set; }

        [JsonIgnore]
        public ICollection<Habit> Habits { get; set; } = new List<Habit>();
    }
}
```

- [ ] **Step 4: Register the DbSet + indexes**

In `AppDbContext.cs` add DbSet:
```csharp
        public DbSet<Milestone> Milestones { get; set; }
```
In `OnModelCreating`:
```csharp
            modelBuilder.Entity<Milestone>()
                .HasIndex(m => m.GoalId)
                .HasDatabaseName("IX_Milestones_GoalId");

            modelBuilder.Entity<Milestone>()
                .HasOne(m => m.Goal)
                .WithMany(g => g.Milestones)
                .HasForeignKey(m => m.GoalId)
                .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test server/AtomicHabits.Tests --filter MilestoneTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/Models/Milestone.cs server/AtomicHabits/Data/AppDbContext.cs server/AtomicHabits.Tests/Models/MilestoneTests.cs
git commit -m "feat: add Milestone entity"
```

---

### Task 5: Link Habit → Milestone (nullable FK)

**Files:**
- Modify: `server/AtomicHabits/Models/Habit.cs` (add `MilestoneId` + nav)
- Modify: `server/AtomicHabits/Data/AppDbContext.cs`
- Test: `server/AtomicHabits.Tests/Models/HabitMilestoneLinkTests.cs`

- [ ] **Step 1: Write the failing test**

Create `server/AtomicHabits.Tests/Models/HabitMilestoneLinkTests.cs`:
```csharp
using AtomicHabits.Models;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Models;

public class HabitMilestoneLinkTests
{
    [Fact]
    public async Task Habit_can_exist_without_a_milestone()
    {
        using var db = TestDbContextFactory.Create();
        var habit = new Habit { UserId = 1, Name = "Code 1 hour", Frequency = "Daily" };
        db.Habits.Add(habit);
        await db.SaveChangesAsync();

        db.Habits.Single().MilestoneId.Should().BeNull();
    }

    [Fact]
    public async Task Habit_can_link_to_a_milestone()
    {
        using var db = TestDbContextFactory.Create();
        var goal = new Goal { UserId = 1, Title = "Get a Remote Job" };
        db.Goals.Add(goal);
        await db.SaveChangesAsync();
        var milestone = new Milestone { UserId = 1, GoalId = goal.Id, Title = "Build Portfolio" };
        db.Milestones.Add(milestone);
        await db.SaveChangesAsync();

        var habit = new Habit { UserId = 1, Name = "Code 1 hour", Frequency = "Daily", MilestoneId = milestone.Id };
        db.Habits.Add(habit);
        await db.SaveChangesAsync();

        db.Habits.Single().MilestoneId.Should().Be(milestone.Id);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter HabitMilestoneLinkTests`
Expected: FAIL — `Habit.MilestoneId` does not exist.

- [ ] **Step 3: Add the nullable FK + nav to Habit**

In `server/AtomicHabits/Models/Habit.cs`, after the `GoalFrequency` property (line ~33), add:
```csharp
        // Optional link to the Goal→Milestone→Habit hierarchy. Nullable on purpose:
        // existing/unlinked habits keep working; linkage is progressive.
        public int? MilestoneId { get; set; }
```
And in the navigation section (after line ~49, the `HabitTags` collection), add:
```csharp
        [ForeignKey("MilestoneId")]
        [JsonIgnore]
        public Milestone? Milestone { get; set; }
```
Add `using System.Text.Json.Serialization;` at the top if not present.

- [ ] **Step 4: Configure the relationship (SetNull on milestone delete)**

In `AppDbContext.OnModelCreating`:
```csharp
            modelBuilder.Entity<Habit>()
                .HasOne(h => h.Milestone)
                .WithMany(m => m.Habits)
                .HasForeignKey(h => h.MilestoneId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Habit>()
                .HasIndex(h => h.MilestoneId)
                .HasDatabaseName("IX_Habits_MilestoneId");
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test server/AtomicHabits.Tests --filter HabitMilestoneLinkTests`
Expected: PASS (both facts).

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/Models/Habit.cs server/AtomicHabits/Data/AppDbContext.cs server/AtomicHabits.Tests/Models/HabitMilestoneLinkTests.cs
git commit -m "feat: link Habit to Milestone via nullable FK"
```

---

### Task 6: HabitSkip entity (failure-capture storage)

**Files:**
- Create: `server/AtomicHabits/Models/HabitSkip.cs`
- Modify: `server/AtomicHabits/Data/AppDbContext.cs`
- Test: `server/AtomicHabits.Tests/Models/HabitSkipTests.cs`

- [ ] **Step 1: Write the failing test**

Create `server/AtomicHabits.Tests/Models/HabitSkipTests.cs`:
```csharp
using System;
using AtomicHabits.Models;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Models;

public class HabitSkipTests
{
    [Fact]
    public async Task HabitSkip_stores_reason_and_date()
    {
        using var db = TestDbContextFactory.Create();
        var habit = new Habit { UserId = 1, Name = "Gym", Frequency = "Daily" };
        db.Habits.Add(habit);
        await db.SaveChangesAsync();

        var skip = new HabitSkip
        {
            UserId = 1,
            HabitId = habit.Id,
            Date = new DateOnly(2026, 6, 7),
            Reason = SkipReason.LowEnergy
        };
        db.HabitSkips.Add(skip);
        await db.SaveChangesAsync();

        var saved = db.HabitSkips.Single();
        saved.Reason.Should().Be(SkipReason.LowEnergy);
        saved.Date.Should().Be(new DateOnly(2026, 6, 7));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test server/AtomicHabits.Tests --filter HabitSkipTests`
Expected: FAIL — `HabitSkip` / `SkipReason` / `db.HabitSkips` missing.

- [ ] **Step 3: Create the HabitSkip model + reason enum**

Create `server/AtomicHabits/Models/HabitSkip.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AtomicHabits.Models
{
    public enum SkipReason
    {
        Busy = 0,
        Forgot = 1,
        LowEnergy = 2,
        NoMotivation = 3,
        ScheduleConflict = 4,
        Other = 5
    }

    public class HabitSkip
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int HabitId { get; set; }

        [Required]
        public DateOnly Date { get; set; }

        [Required]
        public SkipReason Reason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        [JsonIgnore]
        public User? User { get; set; }

        [ForeignKey("HabitId")]
        [JsonIgnore]
        public Habit? Habit { get; set; }
    }
}
```

- [ ] **Step 4: Register the DbSet + index**

In `AppDbContext.cs` add DbSet:
```csharp
        public DbSet<HabitSkip> HabitSkips { get; set; }
```
In `OnModelCreating`:
```csharp
            modelBuilder.Entity<HabitSkip>()
                .HasIndex(s => new { s.UserId, s.HabitId, s.Date })
                .HasDatabaseName("IX_HabitSkips_UserId_HabitId_Date");

            modelBuilder.Entity<HabitSkip>()
                .HasOne(s => s.Habit)
                .WithMany()
                .HasForeignKey(s => s.HabitId)
                .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test server/AtomicHabits.Tests --filter HabitSkipTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/Models/HabitSkip.cs server/AtomicHabits/Data/AppDbContext.cs server/AtomicHabits.Tests/Models/HabitSkipTests.cs
git commit -m "feat: add HabitSkip entity for failure capture"
```

---

### Task 7: EF migration for all new entities

**Files:**
- Create: `server/AtomicHabits/Migrations/<timestamp>_AddPerformanceOsFoundation.cs` (generated)

- [ ] **Step 1: Verify the full solution builds**

Run (from `server/`): `dotnet build AtomicHabits.sln`
Expected: build succeeds, 0 errors.

- [ ] **Step 2: Run the full test suite**

Run (from `server/`): `dotnet test AtomicHabits.Tests/AtomicHabits.Tests.csproj`
Expected: all model tests PASS (Vision, Goal, Milestone, HabitMilestoneLink ×2, HabitSkip).

- [ ] **Step 3: Generate the migration**

Run (from `server/AtomicHabits/`):
```bash
dotnet ef migrations add AddPerformanceOsFoundation
```
Expected: a new migration + designer file under `Migrations/`. It should create
`Visions`, `Goals`, `Milestones`, `HabitSkips` tables, add `Habits.MilestoneId`
nullable column + FK, and the six new indexes (`IX_Visions_UserId`,
`IX_Goals_UserId`, `IX_Milestones_GoalId`, `IX_Habits_MilestoneId`,
`IX_HabitSkips_UserId_HabitId_Date`).

- [ ] **Step 4: Inspect the migration for safety**

Open the generated `*_AddPerformanceOsFoundation.cs`. Confirm:
- It only **adds** tables/columns/indexes — no `DropColumn`/`DropTable`.
- `Habits.MilestoneId` is added as `nullable: true`.
- FK on `Habits.MilestoneId` uses `onDelete: ReferentialAction.SetNull`.

If anything looks destructive, do NOT apply; re-check the model changes.

- [ ] **Step 5: Apply the migration to the dev database**

Run (from `server/AtomicHabits/`): `dotnet ef database update`
Expected: "Done." — tables created, existing data untouched (additive only).

- [ ] **Step 6: Commit**

```bash
git add server/AtomicHabits/Migrations
git commit -m "feat: add EF migration for Performance OS foundation entities"
```

---

## Self-Review (completed by plan author)

- **Spec coverage (§1):** Vision (T2), Goal (T3), Milestone (T4), Habit.MilestoneId nullable (T5), HabitSkip (T6), indexes + migration (T7). All §1 entities covered. ✓
- **Placeholders:** None — every step has concrete code/commands. ✓
- **Type consistency:** `Visions`/`Goals`/`Milestones`/`HabitSkips` DbSet names used consistently; `GoalStatus`/`MilestoneStatus`/`SkipReason` enums defined where first referenced; `MilestoneId` spelled consistently across T4/T5. ✓
- **Cross-task dependency flagged:** Vision's `Goals` nav references the Task-3 `Goal` type — noted explicitly in Task 2 Step 3 (implement models 2–6 before running combined build/tests in T7). ✓

## Subsequent plans (decomposition roadmap — each gets its own plan when reached)

This is Plan 1 of 7. Each builds on this foundation and produces working,
testable software on its own:

- **Plan 2 — Goal→Habit framework** (spec §2): Vision/Goal/Milestone controllers + services + repos (Controller→Service→Repository), `GoalContext`, `Goals.jsx`, "Contributes to…" dropdown, payoff line. FREE tier.
- **Plan 3 — Failure capture** (spec §3a): skip-reason chip UI + `HabitSkip` write endpoint. FREE.
- **Plan 4 — Entitlement model + manual flag** (spec §5 minus Stripe): subscription fields on user, `[RequiresActiveSubscription]` handler (mirrors `PermissionAuthorizationHandler`), `/Auth/me` extension, `<RequirePro>`.
- **Plan 5 — Failure analysis insights** (spec §3b): the ~4 templated stat insights + paid gate.
- **Plan 6 — Weekly CEO Report** (spec §4): `WeeklyReportService`, `ReportController`, `WeeklyReport` row, Sunday `BackgroundService` (reuses `ReminderDispatcherService` pattern). Paid.
- **Plan 7 — Stripe subscription + webhook** (spec §5 billing): replaces the manual flag with real subscription lifecycle.
