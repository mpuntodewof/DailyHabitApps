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
