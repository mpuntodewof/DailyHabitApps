using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AtomicHabits.Tests.Services;

/// <summary>
/// Tests for linking a Habit to a Milestone (create/update validation) and exposing
/// the contribution chain (milestone -> goal -> vision titles) via
/// HabitService.GetContributionAsync.
///
/// HabitService is constructed with the REAL HabitRepositories (over an in-memory
/// AppDbContext) so the create/update path actually persists, plus a stub IAuthService
/// (not exercised by these methods).
/// </summary>
public class HabitServiceMilestoneTests
{
    private static HabitService BuildService(AppDbContext db)
    {
        var repo = new HabitRepositories(db, NullLogger<HabitTrackingRepositories>.Instance);
        var auth = new Mock<IAuthService>();
        return new HabitService(repo, db, NullLogger<HabitService>.Instance, auth.Object);
    }

    // Creates Vision -> Goal -> Milestone owned by the given user; returns the milestone id.
    private static async Task<int> SeedChainAsync(AppDbContext db, int userId,
        string visionTitle = "Become a Remote Engineer",
        string goalTitle = "Get a Remote Job",
        string milestoneTitle = "Build Portfolio")
    {
        var vision = new Vision { UserId = userId, Title = visionTitle };
        db.Visions.Add(vision);
        await db.SaveChangesAsync();

        var goal = new Goal { UserId = userId, VisionId = vision.Id, Title = goalTitle };
        db.Goals.Add(goal);
        await db.SaveChangesAsync();

        var milestone = new Milestone { UserId = userId, GoalId = goal.Id, Title = milestoneTitle };
        db.Milestones.Add(milestone);
        await db.SaveChangesAsync();

        return milestone.Id;
    }

    [Fact]
    public async Task PostHabit_with_valid_owned_milestone_persists_MilestoneId()
    {
        using var db = TestDbContextFactory.Create();
        var svc = BuildService(db);
        var milestoneId = await SeedChainAsync(db, userId: 1);

        var dto = new HabitDTO
        {
            UserId = 1,
            Name = "Code 1 hour",
            Frequency = "Daily",
            GoalUnit = "times",
            GoalFrequency = "daily",
            MilestoneId = milestoneId,
        };

        var res = await svc.PostHabit(dto);

        res.IsSuccess.Should().BeTrue();
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        db.Habits.Single().MilestoneId.Should().Be(milestoneId);
    }

    [Fact]
    public async Task PostHabit_with_null_milestone_persists_unlinked_habit()
    {
        using var db = TestDbContextFactory.Create();
        var svc = BuildService(db);

        var dto = new HabitDTO
        {
            UserId = 1,
            Name = "Code 1 hour",
            Frequency = "Daily",
            GoalUnit = "times",
            GoalFrequency = "daily",
            MilestoneId = null,
        };

        var res = await svc.PostHabit(dto);

        res.IsSuccess.Should().BeTrue();
        db.Habits.Single().MilestoneId.Should().BeNull();
    }

    [Fact]
    public async Task PostHabit_with_milestone_owned_by_another_user_is_rejected()
    {
        using var db = TestDbContextFactory.Create();
        var svc = BuildService(db);
        var otherUsersMilestone = await SeedChainAsync(db, userId: 2);

        var dto = new HabitDTO
        {
            UserId = 1, // different user
            Name = "Code 1 hour",
            Frequency = "Daily",
            GoalUnit = "times",
            GoalFrequency = "daily",
            MilestoneId = otherUsersMilestone,
        };

        var res = await svc.PostHabit(dto);

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        db.Habits.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateHabit_can_link_to_valid_owned_milestone()
    {
        using var db = TestDbContextFactory.Create();
        var svc = BuildService(db);
        var milestoneId = await SeedChainAsync(db, userId: 1);

        var habit = new Habit { UserId = 1, Name = "Code", Frequency = "Daily" };
        db.Habits.Add(habit);
        await db.SaveChangesAsync();

        var dto = new HabitDTO
        {
            UserId = 1,
            Name = "Code",
            Frequency = "Daily",
            GoalUnit = "times",
            GoalFrequency = "daily",
            MilestoneId = milestoneId,
        };

        var res = await svc.UpdateHabit(habit.Id, dto);

        res.IsSuccess.Should().BeTrue();
        db.Habits.Single().MilestoneId.Should().Be(milestoneId);
    }

    [Fact]
    public async Task UpdateHabit_can_unlink_milestone_by_setting_null()
    {
        using var db = TestDbContextFactory.Create();
        var svc = BuildService(db);
        var milestoneId = await SeedChainAsync(db, userId: 1);

        var habit = new Habit { UserId = 1, Name = "Code", Frequency = "Daily", MilestoneId = milestoneId };
        db.Habits.Add(habit);
        await db.SaveChangesAsync();

        var dto = new HabitDTO
        {
            UserId = 1,
            Name = "Code",
            Frequency = "Daily",
            GoalUnit = "times",
            GoalFrequency = "daily",
            MilestoneId = null,
        };

        var res = await svc.UpdateHabit(habit.Id, dto);

        res.IsSuccess.Should().BeTrue();
        db.Habits.Single().MilestoneId.Should().BeNull();
    }

    [Fact]
    public async Task UpdateHabit_with_milestone_owned_by_another_user_is_rejected()
    {
        using var db = TestDbContextFactory.Create();
        var svc = BuildService(db);
        var otherUsersMilestone = await SeedChainAsync(db, userId: 2);

        var habit = new Habit { UserId = 1, Name = "Code", Frequency = "Daily" };
        db.Habits.Add(habit);
        await db.SaveChangesAsync();

        var dto = new HabitDTO
        {
            UserId = 1,
            Name = "Code",
            Frequency = "Daily",
            GoalUnit = "times",
            GoalFrequency = "daily",
            MilestoneId = otherUsersMilestone,
        };

        var res = await svc.UpdateHabit(habit.Id, dto);

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        db.Habits.Single().MilestoneId.Should().BeNull();
    }

    [Fact]
    public async Task GetContribution_returns_full_chain_for_fully_linked_habit()
    {
        using var db = TestDbContextFactory.Create();
        var svc = BuildService(db);
        var milestoneId = await SeedChainAsync(db, userId: 1,
            visionTitle: "Become a Remote Engineer",
            goalTitle: "Get a Remote Job",
            milestoneTitle: "Build Portfolio");

        var habit = new Habit { UserId = 1, Name = "Code", Frequency = "Daily", MilestoneId = milestoneId };
        db.Habits.Add(habit);
        await db.SaveChangesAsync();

        var res = await svc.GetContributionAsync(1, habit.Id);

        res.IsSuccess.Should().BeTrue();
        var dto = res.Result.Should().BeOfType<HabitContributionDto>().Subject;
        dto.MilestoneId.Should().Be(milestoneId);
        dto.MilestoneTitle.Should().Be("Build Portfolio");
        dto.GoalTitle.Should().Be("Get a Remote Job");
        dto.VisionTitle.Should().Be("Become a Remote Engineer");
    }

    [Fact]
    public async Task GetContribution_returns_nulls_for_unlinked_habit()
    {
        using var db = TestDbContextFactory.Create();
        var svc = BuildService(db);

        var habit = new Habit { UserId = 1, Name = "Code", Frequency = "Daily", MilestoneId = null };
        db.Habits.Add(habit);
        await db.SaveChangesAsync();

        var res = await svc.GetContributionAsync(1, habit.Id);

        res.IsSuccess.Should().BeTrue();
        var dto = res.Result.Should().BeOfType<HabitContributionDto>().Subject;
        dto.MilestoneId.Should().BeNull();
        dto.MilestoneTitle.Should().BeNull();
        dto.GoalTitle.Should().BeNull();
        dto.VisionTitle.Should().BeNull();
    }

    [Fact]
    public async Task GetContribution_for_unknown_habit_returns_not_found()
    {
        using var db = TestDbContextFactory.Create();
        var svc = BuildService(db);

        var res = await svc.GetContributionAsync(1, habitId: 9999);

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
