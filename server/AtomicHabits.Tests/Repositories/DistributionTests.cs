using System;
using System.Threading;
using System.Threading.Tasks;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Repositories;

public class DistributionTests
{
    private const int HabitId = 1;
    private const int UserId = 1;

    private static void SeedParents(SqliteTestDb s)
    {
        s.Context.Set<User>().Add(new User
        {
            Id = UserId, Username = "u", Email = "u@test.local",
            PasswordHash = "x", IsActive = true, CreatedAt = DateTime.UtcNow
        });
        s.Context.Set<Habit>().Add(new Habit
        {
            Id = HabitId, UserId = UserId, Name = "Gym",
            Frequency = "Daily", GoalFrequency = "daily",
            GoalValue = 1, GoalUnit = "times"
        });
        s.Context.SaveChanges();
    }

    private static void SeedTracking(SqliteTestDb s, DateTime date)
    {
        s.Context.HabitTrackings.Add(new HabitTracking
        {
            HabitId = HabitId, UserId = UserId, TrackingDate = date,
            IsCompleted = true, CreatedAt = date, UpdatedAt = date
        });
        s.Context.SaveChanges();
    }

    private static HabitTrackingRepositories Repo(SqliteTestDb s) =>
        new(s.Context, NullLogger<HabitTrackingRepositories>.Instance);

    [Fact]
    public async Task Weekly_distribution_buckets_by_day_of_month()
    {
        using var s = new SqliteTestDb();
        SeedParents(s);
        SeedTracking(s, new DateTime(2026, 6, 3));   // day 3  → bucket 0
        SeedTracking(s, new DateTime(2026, 6, 10));  // day 10 → bucket 1
        SeedTracking(s, new DateTime(2026, 6, 25));  // day 25 → bucket 3

        var result = await Repo(s).GetWeeklyDistribution(
            new WeeklyDistributionDTO { HabitId = HabitId, Year = 2026, Month = 6 },
            CancellationToken.None);

        result.Should().HaveCount(4);
        result[0].Should().Be(1); // day 3
        result[1].Should().Be(1); // day 10
        result[2].Should().Be(0); // no day in 15-21
        result[3].Should().Be(1); // day 25
    }

    [Fact]
    public async Task Weekly_distribution_returns_zeros_for_empty_month()
    {
        using var s = new SqliteTestDb();
        SeedParents(s);
        // No tracking rows for 2026-06 at all

        var result = await Repo(s).GetWeeklyDistribution(
            new WeeklyDistributionDTO { HabitId = HabitId, Year = 2026, Month = 6 },
            CancellationToken.None);

        result.Should().HaveCount(4);
        result.Should().AllBeEquivalentTo(0);
    }

    [Fact]
    public async Task Monthly_distribution_indexes_by_month_minus_one()
    {
        using var s = new SqliteTestDb();
        SeedParents(s);
        SeedTracking(s, new DateTime(2026, 1, 15)); // Jan → index 0
        SeedTracking(s, new DateTime(2026, 6, 15)); // Jun → index 5
        SeedTracking(s, new DateTime(2026, 6, 20)); // Jun → index 5 (total 2)

        var result = await Repo(s).GetMonthlyDistribution(
            new MonthlyDistributionDTO { HabitId = HabitId, Year = 2026 },
            CancellationToken.None);

        result.Should().HaveCount(12);
        result[0].Should().Be(1);  // January
        result[5].Should().Be(2);  // June
        result[11].Should().Be(0); // December — no entries
    }

    [Fact]
    public async Task Monthly_distribution_returns_all_zeros_for_empty_year()
    {
        using var s = new SqliteTestDb();
        SeedParents(s);
        // No tracking rows for 2026 at all

        var result = await Repo(s).GetMonthlyDistribution(
            new MonthlyDistributionDTO { HabitId = HabitId, Year = 2026 },
            CancellationToken.None);

        result.Should().HaveCount(12);
        result.Should().AllBeEquivalentTo(0);
    }
}
