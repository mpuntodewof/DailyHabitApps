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
