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
