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
