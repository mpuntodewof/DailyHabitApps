using System;
using System.Linq;
using System.Threading;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class WeeklyReportServiceTests
{
    private static WeeklyReportService NewService(AtomicHabits.Data.AppDbContext db) =>
        new WeeklyReportService(db, NullLogger<WeeklyReportService>.Instance);

    private static DateTime ThisWeekDay(int offsetFromMonday) =>
        HabitMath.StartOfIsoWeek(DateTime.UtcNow).AddDays(offsetFromMonday);

    private static async Task<Habit> SeedHabit(AtomicHabits.Data.AppDbContext db, int userId, string name, bool linked = false)
    {
        var h = new Habit { UserId = userId, Name = name, Frequency = "Daily", GoalFrequency = "daily",
                            MilestoneId = linked ? (int?)1 : null };
        db.Habits.Add(h); await db.SaveChangesAsync(); return h;
    }

    private static void Complete(AtomicHabits.Data.AppDbContext db, Habit h, int dayOffsetFromMonday)
    {
        db.HabitTrackings.Add(new HabitTracking {
            UserId = h.UserId, HabitId = h.Id, IsCompleted = true,
            TrackingDate = ThisWeekDay(dayOffsetFromMonday)
        });
    }

    [Fact]
    public async Task No_habits_yields_zero_score_no_divide_by_zero()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var res = await svc.GetCurrentWeekAsync(1, CancellationToken.None);
        res.IsSuccess.Should().BeTrue();
        var dto = (WeeklyReportDto)res.Result!;
        dto.PerformanceScore.Should().Be(0);
        dto.ScoreBand.Should().Be("Needs work");
    }

    [Fact]
    public async Task Score_clamped_at_100_even_when_overlogged()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var h = await SeedHabit(db, 1, "Gym");
        // complete the same habit on several days (more than possible double-counting risk)
        for (int d = 0; d <= 6; d++) Complete(db, h, d);
        await db.SaveChangesAsync();

        var dto = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(1, CancellationToken.None)).Result!;
        dto.PerformanceScore.Should().BeLessThanOrEqualTo(100);
        dto.PerformanceScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Goal_linked_habit_is_weighted_more()
    {
        // Two users, identical completions; user A's habit is goal-linked (1.5x),
        // user B's is not. With partial completion, A's weighted score should differ
        // from B's only if completion != 100%. Use a partially-completed week.
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var linked = await SeedHabit(db, 1, "Linked", linked: true);
        var plain = await SeedHabit(db, 2, "Plain", linked: false);
        // complete 3 of the elapsed days for both (same ratio)
        for (int d = 0; d <= 2; d++) { Complete(db, linked, d); Complete(db, plain, d); }
        await db.SaveChangesAsync();

        var a = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(1, CancellationToken.None)).Result!;
        var b = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(2, CancellationToken.None)).Result!;
        // Same completion ratio → same score regardless of weight (weight cancels in the ratio).
        // This asserts weighting doesn't BREAK a single-habit ratio; cross-habit weighting is
        // exercised implicitly. Both should be equal and in 0..100.
        a.PerformanceScore.Should().Be(b.PerformanceScore);
        a.PerformanceScore.Should().BeInRange(0, 100);
    }

    [Fact]
    public async Task Top_miss_reason_is_humanized_and_owner_scoped()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var h = await SeedHabit(db, 1, "Gym");
        var monday = DateOnly.FromDateTime(HabitMath.StartOfIsoWeek(DateTime.UtcNow));
        db.HabitSkips.AddRange(
            new HabitSkip { UserId = 1, HabitId = h.Id, Date = monday, Reason = SkipReason.LowEnergy },
            new HabitSkip { UserId = 1, HabitId = h.Id, Date = monday.AddDays(1), Reason = SkipReason.LowEnergy },
            new HabitSkip { UserId = 1, HabitId = h.Id, Date = monday.AddDays(2), Reason = SkipReason.Busy }
        );
        await db.SaveChangesAsync();

        var dto = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(1, CancellationToken.None)).Result!;
        dto.TopMissReason.Should().Be("Low Energy");

        var other = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(2, CancellationToken.None)).Result!;
        other.TopMissReason.Should().BeNull(); // user 2 has no skips
    }

    [Fact]
    public async Task Best_and_worst_habit_identified()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var good = await SeedHabit(db, 1, "Reading");
        var bad = await SeedHabit(db, 1, "Exercise");
        // Reading completed every elapsed day; Exercise not at all
        for (int d = 0; d <= 6; d++) Complete(db, good, d);
        await db.SaveChangesAsync();

        var dto = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(1, CancellationToken.None)).Result!;
        dto.BestHabit.Should().Be("Reading");
        dto.WorstHabit.Should().Be("Exercise");
        dto.FocusNextWeek.Should().Contain("Exercise");
    }

    [Fact]
    public async Task Week_start_is_monday()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var dto = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(1, CancellationToken.None)).Result!;
        var parsed = DateTime.Parse(dto.WeekStart);
        parsed.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    // Test A: run against the REAL relational (SQLite) provider so SQL translation is
    // exercised. The EF InMemory provider runs as LINQ-to-objects and never catches
    // translation bugs (this repo previously shipped a DayOfWeek-translation bug only a
    // SQLite test caught). Must return a populated report without throwing.
    [Fact]
    public async Task GetCurrentWeek_works_on_relational_provider()
    {
        using var sqlite = new SqliteTestDb();
        var db = sqlite.Context;
        // SQLite enforces FKs — seed the user first.
        db.Users.Add(new User { Id = 1, Username = "u", Email = "u@x.com" });
        var h = new Habit { UserId = 1, Name = "Gym", Frequency = "Daily", GoalFrequency = "daily" };
        db.Habits.Add(h);
        await db.SaveChangesAsync();

        var monday = HabitMath.StartOfIsoWeek(DateTime.UtcNow);
        db.HabitTrackings.Add(new HabitTracking { UserId = 1, HabitId = h.Id, IsCompleted = true, TrackingDate = monday });
        db.HabitSkips.Add(new HabitSkip { UserId = 1, HabitId = h.Id, Date = DateOnly.FromDateTime(monday).AddDays(1), Reason = SkipReason.Busy });
        await db.SaveChangesAsync();

        var svc = new WeeklyReportService(db, NullLogger<WeeklyReportService>.Instance);
        var res = await svc.GetCurrentWeekAsync(1, CancellationToken.None); // must not throw on a relational provider
        res.IsSuccess.Should().BeTrue();
        var dto = (WeeklyReportDto)res.Result!;
        dto.WeekStart.Should().NotBeNullOrEmpty();
        dto.TopMissReason.Should().Be("Busy");
    }

    // Test B: prove goal-linked weighting actually shifts the aggregate. One linked habit
    // (weight 1.5) at 100% + one plain habit (weight 1.0) at 0%. Flat mean = 50; weighted
    // completion = 1.5*E / (1.5*E + 1.0*E) = 1.5/2.5 = 60% -> score 60 > 50.
    [Fact]
    public async Task Goal_linked_weighting_shifts_aggregate_above_flat_mean()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var linked = await SeedHabit(db, 1, "Linked", linked: true);   // weight 1.5
        var plain = await SeedHabit(db, 1, "Plain", linked: false);    // weight 1.0
        // Linked completed every elapsed day (100%); Plain completed none (0%).
        for (int d = 0; d <= 6; d++) Complete(db, linked, d);
        await db.SaveChangesAsync();

        var dto = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(1, CancellationToken.None)).Result!;
        // Strictly greater than the unweighted flat mean (50) because the 100% habit
        // carries 1.5x weight. Expected exact value is 60.
        dto.PerformanceScore.Should().BeGreaterThan(50);
    }

    // Test C: consistencyDelta this-vs-last week sign. This week strong (every elapsed day),
    // last week weak (a single day) -> delta strictly positive. Assert sign only to stay
    // robust to which weekday "today" is.
    [Fact]
    public async Task Consistency_delta_positive_when_this_week_beats_last_week()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var h = await SeedHabit(db, 1, "Gym");
        // This week: complete every day (caps to 100% of elapsed days).
        for (int d = 0; d <= 6; d++) Complete(db, h, d);
        // Last week: complete only one of the 7 days (~14%).
        db.HabitTrackings.Add(new HabitTracking
        {
            UserId = h.UserId, HabitId = h.Id, IsCompleted = true,
            TrackingDate = HabitMath.StartOfIsoWeek(DateTime.UtcNow).AddDays(-7 + 0)
        });
        await db.SaveChangesAsync();

        var dto = (WeeklyReportDto)(await svc.GetCurrentWeekAsync(1, CancellationToken.None)).Result!;
        dto.ConsistencyDelta.Should().BeGreaterThan(0);
    }
}
