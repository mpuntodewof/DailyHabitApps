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
