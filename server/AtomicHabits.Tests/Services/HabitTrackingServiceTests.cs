using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class HabitTrackingServiceTests
{
    private const int UserId = 1;

    // Seeds User + Habit (FK parents), returns the habit. Mocks return this habit.
    private static (HabitTrackingService svc, Habit habit) Build(SqliteTestDb sqlite)
    {
        var user = new User { Id = UserId, Username = "u", Email = "u@test.local", PasswordHash = "x" };
        sqlite.Context.Users.Add(user);
        var habit = new Habit
        {
            Id = 1,
            UserId = UserId,
            Name = "Gym",
            Frequency = "Daily",
            GoalFrequency = "daily",
            GoalValue = 1,
            GoalUnit = "times"
        };
        sqlite.Context.Habits.Add(habit);
        sqlite.Context.SaveChanges();

        var trackingRepo = new HabitTrackingRepositories(sqlite.Context, NullLogger<HabitTrackingRepositories>.Instance);
        var streakRepo   = new StreakRepositories(sqlite.Context, NullLogger<StreakRepositories>.Instance);

        var habitRepo = new Mock<IHabitRepositories>();
        habitRepo.Setup(r => r.GetHabitById(habit.Id)).ReturnsAsync(habit);
        habitRepo.Setup(r => r.GetHabitbyUserHabitId(UserId, habit.Id)).ReturnsAsync(habit);

        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.GetCurrentUserFromJwt(It.IsAny<string>()))
            .ReturnsAsync(new UserInfoDto { UserId = UserId.ToString() });

        var svc = new HabitTrackingService(auth.Object, trackingRepo, streakRepo, sqlite.Context, habitRepo.Object);
        return (svc, habit);
    }

    [Fact]
    public async Task PostHabitProgress_creates_tracking_and_streak()
    {
        using var sqlite = new SqliteTestDb();
        var (svc, habit) = Build(sqlite);

        var res = await svc.PostHabitProgress(new HabitTrackingDTO
        {
            HabitId = habit.Id,
            UserId = UserId,
            TrackingDate = new DateTime(2026, 6, 1),
            IsCompleted = true
        }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        using var verify = sqlite.NewContext();
        verify.HabitTrackings.Should().ContainSingle();
        verify.Streaks.Should().ContainSingle();
    }

    [Fact]
    public async Task PostHabitProgress_rejects_duplicate_same_day()
    {
        using var sqlite = new SqliteTestDb();
        var (svc, habit) = Build(sqlite);
        var dto = new HabitTrackingDTO
        {
            HabitId = habit.Id,
            UserId = UserId,
            TrackingDate = new DateTime(2026, 6, 1),
            IsCompleted = true
        };

        (await svc.PostHabitProgress(dto, CancellationToken.None)).IsSuccess.Should().BeTrue();
        var second = await svc.PostHabitProgress(dto, CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var verify = sqlite.NewContext();
        verify.HabitTrackings.Should().ContainSingle();
    }

    [Fact]
    public async Task PostHabitProgress_404_when_habit_missing()
    {
        using var sqlite = new SqliteTestDb();
        var (svc, _) = Build(sqlite);

        var res = await svc.PostHabitProgress(new HabitTrackingDTO
        {
            HabitId = 999,
            UserId = UserId,
            TrackingDate = new DateTime(2026, 6, 1),
            IsCompleted = true
        }, CancellationToken.None);

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostDailyHabit_rejects_second_submission_same_day()
    {
        using var sqlite = new SqliteTestDb();
        var (svc, habit) = Build(sqlite);

        var first = await svc.PostDailyHabit(habit.Id, 30, CancellationToken.None, "fake-token");
        first.IsSuccess.Should().BeTrue();

        var second = await svc.PostDailyHabit(habit.Id, 30, CancellationToken.None, "fake-token");
        second.IsSuccess.Should().BeFalse();
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
