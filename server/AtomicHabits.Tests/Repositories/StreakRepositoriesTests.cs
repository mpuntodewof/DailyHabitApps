using System;
using System.Linq;
using System.Threading.Tasks;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Repositories;

public class StreakRepositoriesTests
{
    // Seed the minimum parent rows required by FK constraints, then return.
    // Idempotent: skips if already present (same context is reused across calls).
    private static async Task EnsureParentsSeeded(SqliteTestDb sqlite, int habitId, int userId)
    {
        if (!sqlite.Context.Set<User>().Any(u => u.Id == userId))
        {
            sqlite.Context.Set<User>().Add(new User
            {
                Id = userId, Username = $"user{userId}", Email = $"user{userId}@test.com",
                PasswordHash = "x", IsActive = true, CreatedAt = DateTime.UtcNow
            });
        }

        if (!sqlite.Context.Set<Habit>().Any(h => h.Id == habitId))
        {
            sqlite.Context.Set<Habit>().Add(new Habit
            {
                Id = habitId, UserId = userId, Name = $"Habit {habitId}",
                Frequency = "Daily", GoalUnit = "times", GoalFrequency = "daily",
                CreatedAt = DateTime.UtcNow
            });
        }

        await sqlite.Context.SaveChangesAsync();
    }

    // Mirrors how the SERVICE uses the repo: the day's tracking row is created AND SAVED
    // before the streak upsert runs (CreateTracking does its own SaveChanges in production).
    private static async Task SeedTrackingThenUpsert(
        SqliteTestDb sqlite, int habitId, int userId, DateTime date, bool isCompleted)
    {
        await EnsureParentsSeeded(sqlite, habitId, userId);

        sqlite.Context.HabitTrackings.Add(new HabitTracking
        {
            HabitId = habitId, UserId = userId, TrackingDate = date,
            IsCompleted = isCompleted, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await sqlite.Context.SaveChangesAsync();

        var repo = new StreakRepositories(sqlite.Context, NullLogger<StreakRepositories>.Instance);
        await repo.UpsertStreakAfterTracking(new HabitTrackingDTO
        {
            HabitId = habitId, UserId = userId, TrackingDate = date, IsCompleted = isCompleted
        });
    }

    [Fact]
    public async Task First_completion_creates_streak_of_one()
    {
        using var sqlite = new SqliteTestDb();
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 1), true);

        using var verify = sqlite.NewContext();
        var streak = verify.Streaks.Single();
        streak.CurrentStreak.Should().Be(1);
        streak.BestStreak.Should().Be(1);
    }

    [Fact]
    public async Task Two_consecutive_completed_days_yield_current_streak_of_two()
    {
        using var sqlite = new SqliteTestDb();
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 1), true);
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 2), true);

        using var verify = sqlite.NewContext();
        var streak = verify.Streaks.Single();
        // CORRECT behavior: two consecutive completed days = current streak of 2.
        streak.CurrentStreak.Should().Be(2);
        streak.BestStreak.Should().Be(2);
    }

    [Fact]
    public async Task A_missed_day_breaks_the_current_streak_but_keeps_best()
    {
        using var sqlite = new SqliteTestDb();
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 1), true);
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 2), true);
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 4), true); // skip 6/3

        using var verify = sqlite.NewContext();
        var streak = verify.Streaks.Single();
        streak.CurrentStreak.Should().Be(1);
        streak.BestStreak.Should().Be(2);
    }

    [Fact]
    public async Task Completion_rate_reflects_completed_over_total()
    {
        using var sqlite = new SqliteTestDb();
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 1), true);
        await SeedTrackingThenUpsert(sqlite, 1, 1, new DateTime(2026, 6, 2), false);

        using var verify = sqlite.NewContext();
        var streak = verify.Streaks.Single();
        streak.CompletionRate.Should().BeApproximately(0.5f, 0.001f);
    }
}
