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
