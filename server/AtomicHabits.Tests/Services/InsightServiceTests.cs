using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class InsightServiceTests
{
    private static InsightService NewService(AtomicHabits.Data.AppDbContext db) =>
        new InsightService(db, NullLogger<InsightService>.Instance);

    private static async Task<int> SeedHabit(AtomicHabits.Data.AppDbContext db, int userId, string name)
    {
        var h = new Habit { UserId = userId, Name = name, Frequency = "Daily" };
        db.Habits.Add(h); await db.SaveChangesAsync(); return h.Id;
    }

    [Fact]
    public async Task No_data_returns_empty_insight_list()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var res = await svc.GetInsightsAsync(1, CancellationToken.None);
        res.IsSuccess.Should().BeTrue();
        ((IEnumerable<InsightDto>)res.Result!).Should().BeEmpty();
    }

    [Fact]
    public async Task Top_skip_reason_insight_appears_with_enough_skips()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var hid = await SeedHabit(db, 1, "Gym");
        db.HabitSkips.AddRange(
            new HabitSkip { UserId = 1, HabitId = hid, Date = new DateOnly(2026, 6, 1), Reason = SkipReason.LowEnergy },
            new HabitSkip { UserId = 1, HabitId = hid, Date = new DateOnly(2026, 6, 2), Reason = SkipReason.LowEnergy },
            new HabitSkip { UserId = 1, HabitId = hid, Date = new DateOnly(2026, 6, 3), Reason = SkipReason.Busy }
        );
        await db.SaveChangesAsync();

        var res = await svc.GetInsightsAsync(1, CancellationToken.None);
        var insights = ((IEnumerable<InsightDto>)res.Result!).ToList();
        var skipReason = insights.FirstOrDefault(i => i.Key == "top-skip-reason");
        skipReason.Should().NotBeNull();
        skipReason!.Text.Should().Contain("Low Energy");
        skipReason.Text.Should().Contain("67%"); // 2 of 3
    }

    [Fact]
    public async Task Top_skip_reason_omitted_below_threshold()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var hid = await SeedHabit(db, 1, "Gym");
        db.HabitSkips.Add(new HabitSkip { UserId = 1, HabitId = hid, Date = new DateOnly(2026, 6, 1), Reason = SkipReason.Busy });
        await db.SaveChangesAsync(); // only 1 skip (<3)

        var res = await svc.GetInsightsAsync(1, CancellationToken.None);
        ((IEnumerable<InsightDto>)res.Result!).Any(i => i.Key == "top-skip-reason").Should().BeFalse();
    }

    [Fact]
    public async Task Completion_time_of_day_insight_appears()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var hid = await SeedHabit(db, 1, "Read");
        // 5 morning completions (hour 8)
        for (int d = 1; d <= 5; d++)
            db.HabitTrackings.Add(new HabitTracking {
                UserId = 1, HabitId = hid, IsCompleted = true,
                CompletedAt = new DateTime(2026, 6, d, 8, 0, 0, DateTimeKind.Utc),
                TrackingDate = new DateTime(2026, 6, d, 8, 0, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();

        var res = await svc.GetInsightsAsync(1, CancellationToken.None);
        var tod = ((IEnumerable<InsightDto>)res.Result!).FirstOrDefault(i => i.Key == "completion-time-of-day");
        tod.Should().NotBeNull();
        tod!.Text.Should().Contain("morning");
    }

    [Fact]
    public async Task Most_skipped_habit_insight_names_the_habit()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var gym = await SeedHabit(db, 1, "Exercise");
        var read = await SeedHabit(db, 1, "Read");
        db.HabitSkips.AddRange(
            new HabitSkip { UserId = 1, HabitId = gym, Date = new DateOnly(2026,6,1), Reason = SkipReason.Busy },
            new HabitSkip { UserId = 1, HabitId = gym, Date = new DateOnly(2026,6,2), Reason = SkipReason.Busy },
            new HabitSkip { UserId = 1, HabitId = gym, Date = new DateOnly(2026,6,3), Reason = SkipReason.LowEnergy },
            new HabitSkip { UserId = 1, HabitId = read, Date = new DateOnly(2026,6,1), Reason = SkipReason.Forgot }
        );
        await db.SaveChangesAsync();

        var res = await svc.GetInsightsAsync(1, CancellationToken.None);
        var top = ((IEnumerable<InsightDto>)res.Result!).FirstOrDefault(i => i.Key == "most-skipped-habit");
        top.Should().NotBeNull();
        top!.Text.Should().Contain("Exercise");
    }

    [Fact]
    public async Task Insights_are_owner_scoped()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var hid = await SeedHabit(db, 2, "Gym"); // belongs to user 2
        db.HabitSkips.AddRange(
            new HabitSkip { UserId = 2, HabitId = hid, Date = new DateOnly(2026,6,1), Reason = SkipReason.Busy },
            new HabitSkip { UserId = 2, HabitId = hid, Date = new DateOnly(2026,6,2), Reason = SkipReason.Busy },
            new HabitSkip { UserId = 2, HabitId = hid, Date = new DateOnly(2026,6,3), Reason = SkipReason.Busy }
        );
        await db.SaveChangesAsync();

        var res = await svc.GetInsightsAsync(1, CancellationToken.None); // user 1 sees nothing
        ((IEnumerable<InsightDto>)res.Result!).Should().BeEmpty();
    }
}
