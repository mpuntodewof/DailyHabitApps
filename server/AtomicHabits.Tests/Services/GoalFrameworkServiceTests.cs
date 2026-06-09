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

    [Fact]
    public async Task UpdateGoal_by_non_owner_returns_NotFound()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var g = await svc.CreateGoalAsync(1, new GoalUpsertDto { Title = "G" }, CancellationToken.None);
        var goalId = ((GoalDto)g.Result!).Id;

        var res = await svc.UpdateGoalAsync(2, goalId, new GoalUpsertDto { Title = "Hacked" }, CancellationToken.None);
        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteGoal_by_non_owner_returns_NotFound()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var g = await svc.CreateGoalAsync(1, new GoalUpsertDto { Title = "G" }, CancellationToken.None);
        var goalId = ((GoalDto)g.Result!).Id;

        var res = await svc.DeleteGoalAsync(2, goalId, CancellationToken.None);
        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        db.Goals.Should().ContainSingle(x => x.Id == goalId);
    }

    [Fact]
    public async Task UpdateMilestone_by_non_owner_returns_NotFound()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var g = await svc.CreateGoalAsync(1, new GoalUpsertDto { Title = "G" }, CancellationToken.None);
        var goalId = ((GoalDto)g.Result!).Id;
        var m = await svc.CreateMilestoneAsync(1, new MilestoneUpsertDto { GoalId = goalId, Title = "M" }, CancellationToken.None);
        var milestoneId = ((MilestoneDto)m.Result!).Id;

        var res = await svc.UpdateMilestoneAsync(2, milestoneId, new MilestoneUpsertDto { GoalId = goalId, Title = "Hacked" }, CancellationToken.None);
        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteMilestone_by_non_owner_returns_NotFound()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var g = await svc.CreateGoalAsync(1, new GoalUpsertDto { Title = "G" }, CancellationToken.None);
        var goalId = ((GoalDto)g.Result!).Id;
        var m = await svc.CreateMilestoneAsync(1, new MilestoneUpsertDto { GoalId = goalId, Title = "M" }, CancellationToken.None);
        var milestoneId = ((MilestoneDto)m.Result!).Id;

        var res = await svc.DeleteMilestoneAsync(2, milestoneId, CancellationToken.None);
        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        db.Milestones.Should().ContainSingle(x => x.Id == milestoneId);
    }

    [Fact]
    public async Task UpdateGoal_with_invalid_status_returns_BadRequest()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var g = await svc.CreateGoalAsync(1, new GoalUpsertDto { Title = "G" }, CancellationToken.None);
        var goalId = ((GoalDto)g.Result!).Id;

        var res = await svc.UpdateGoalAsync(1, goalId, new GoalUpsertDto { Title = "G", Status = "Nonsense" }, CancellationToken.None);
        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        db.Goals.Single(x => x.Id == goalId).Status.Should().Be(GoalStatus.Active);
    }
}
