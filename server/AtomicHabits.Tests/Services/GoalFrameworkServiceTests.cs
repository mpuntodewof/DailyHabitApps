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
}
