using System.Collections.Generic;
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

/// <summary>
/// Unit tests for HabitService.HabitSummary(int userId).
///
/// Method signature: Task&lt;ApiResponse&gt; HabitSummary(int userId)
/// - Takes userId directly — no JWT / IAuthService call.
/// - Calls IHabitRepositories for:
///     GetActiveHabits(userId, ct)
///     GetTodayTrackings(habitIds, today, ct)
///     GetWeeklyTrackings(habitIds, startOfWeek, ct)
///     GetMonthlyTrackings(habitIds, startOfMonth, ct)
/// - AppDbContext is NOT used inside HabitSummary; passed as null! safely.
///
/// GoalFrequency matching (bug #17 fix — per-frequency ExpectedSessions):
///   IsDailyHabit(h):   f.Contains("day") || IsNullOrEmpty(f)
///     "daily"  → Contains("day") = FALSE  ← "daily" lacks the substring "day"
///     ""       → IsNullOrEmpty  = TRUE
///     "per day"→ Contains("day") = TRUE
///   So habits with GoalFrequency == "daily" are NOT counted as daily habits.
///   Habits with GoalFrequency == "" (or null) ARE counted as daily habits.
///   This is a testability finding documented below.
///
///   ExpectedSessions(h, daysElapsed, periodLengthDays):
///     Contains("day")    → daysElapsed                     (e.g. "" or "per day")
///     Contains("week")   → Ceiling(daysElapsed / 7.0)     (e.g. "weekly")
///     Contains("month")  → 1 (if daysElapsed >= 1)         (e.g. "monthly")
///     Contains("year")   → 0                               (e.g. "yearly")
///     else               → daysElapsed (fallthrough)        (e.g. "daily" — not caught above)
/// </summary>
public class HabitServiceSummaryTests
{
    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private static HabitService BuildService(Mock<IHabitRepositories> repoMock)
    {
        var authMock = new Mock<IAuthService>();
        return new HabitService(
            repoMock.Object,
            null!,                                          // AppDbContext — not used by HabitSummary
            NullLogger<HabitService>.Instance,
            authMock.Object);
    }

    private static Mock<IHabitRepositories> RepoReturning(
        List<Habit> habits,
        List<HabitTracking> todayTrackings,
        List<HabitTracking> weekTrackings,
        List<HabitTracking> monthTrackings)
    {
        var capturedHabits = habits;
        var capturedToday  = todayTrackings;
        var capturedWeek   = weekTrackings;
        var capturedMonth  = monthTrackings;

        var mock = new Mock<IHabitRepositories>(MockBehavior.Loose);
        mock.Setup(r => r.GetActiveHabits(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<int, CancellationToken>((_, __) => Task.FromResult(capturedHabits));
        mock.Setup(r => r.GetTodayTrackings(It.IsAny<List<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns<List<int>, DateTime, CancellationToken>((_, __, ___) => Task.FromResult(capturedToday));
        mock.Setup(r => r.GetWeeklyTrackings(It.IsAny<List<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns<List<int>, DateTime, CancellationToken>((_, __, ___) => Task.FromResult(capturedWeek));
        mock.Setup(r => r.GetMonthlyTrackings(It.IsAny<List<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns<List<int>, DateTime, CancellationToken>((_, __, ___) => Task.FromResult(capturedMonth));
        return mock;
    }

    // Builds a Habit that IsDailyHabit() will classify as "daily".
    // GoalFrequency = "" matches the IsNullOrEmpty branch in IsDailyHabit.
    // NOTE: GoalFrequency = "daily" does NOT match (Contains("day") is false for "daily").
    private static Habit DailyHabit(int id, int userId, string name = "Test Habit") =>
        new Habit
        {
            Id            = id,
            UserId        = userId,
            Name          = name,
            Frequency     = "Daily",
            GoalFrequency = "",          // empty → IsDailyHabit=true via IsNullOrEmpty branch
            GoalValue     = 1,
            GoalUnit      = "times",
        };

    private static Habit WeeklyHabit(int id, int userId, string name = "Test Habit") =>
        new Habit
        {
            Id            = id,
            UserId        = userId,
            Name          = name,
            Frequency     = "Weekly",
            GoalFrequency = "weekly",    // Contains("week") → true
            GoalValue     = 1,
            GoalUnit      = "times",
        };

    // ────────────────────────────────────────────────────────────────────────
    // Test 1: Zero habits → success with a fully-zeroed summary
    //
    // Hand-computed:
    //   dailyHabitCount = 0 → todayRate guard = 0
    //   expectedThisWeek = 0 → weeklyRate guard = 0
    //   expectedThisMonth = 0 → monthlyRate guard = 0
    //   healthScore = (0+0+0)/3 = 0
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ZeroHabits_ReturnsSuccess_WithZeroedSummary()
    {
        // Arrange: no active habits → all tracking lists also empty
        var repo = RepoReturning(
            habits:         new List<Habit>(),
            todayTrackings: new List<HabitTracking>(),
            weekTrackings:  new List<HabitTracking>(),
            monthTrackings: new List<HabitTracking>());

        var svc = BuildService(repo);

        // Act
        var response = await svc.HabitSummary(userId: 1);

        // Assert — call succeeded
        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.ErrorMessages.Should().BeNullOrEmpty();

        var dto = response.Result.Should().BeOfType<HabitSummaryDto>().Subject;

        // No habits → every rate and count is 0
        dto.TodaySummary.HabitsToday.Should().Be(0);
        dto.TodaySummary.CompletedToday.Should().Be(0);
        dto.TodaySummary.TodayCompletionRate.Should().Be(0);

        dto.WeeklySummary.TotalCompletedThisWeek.Should().Be(0);
        dto.WeeklySummary.WeeklyCompletionRate.Should().Be(0);

        dto.MonthlySummary.TotalMonthlySessions.Should().Be(0);
        dto.MonthlySummary.MonthlyCompletionRate.Should().Be(0);

        dto.HabitHealthScore.Should().Be(0);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 2: One "daily" habit (GoalFrequency = "") completed today → TodayRate == 100
    //
    // IsDailyHabit uses string.IsNullOrEmpty check, so GoalFrequency = "" qualifies.
    //
    // Hand-computed (date-agnostic):
    //   dailyHabitCount = 1 (GoalFrequency = "" → IsNullOrEmpty = true)
    //   completedToday  = 1
    //   todayRate       = 1 * 100 / 1 = 100
    //
    //   Weekly/monthly rates depend on daysElapsed (varies by run date).
    //   ExpectedSessions("", D, _) falls through to the `else` branch = D.
    //   With 1 completion and D ≥ 1 expected: rate = min(100, 1*100/D) ≥ 1.
    //   We assert > 0 to be calendar-agnostic.
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OneDailyHabit_CompletedToday_TodayRateIs100()
    {
        // Arrange
        var habit = DailyHabit(id: 10, userId: 1, name: "Morning Run");

        var tracking = new HabitTracking
        {
            Id          = 1,
            HabitId     = habit.Id,
            UserId      = habit.UserId,
            IsCompleted = true,
        };

        // Return the same tracking for today, week, and month
        var repo = RepoReturning(
            habits:         new List<Habit> { habit },
            todayTrackings: new List<HabitTracking> { tracking },
            weekTrackings:  new List<HabitTracking> { tracking },
            monthTrackings: new List<HabitTracking> { tracking });

        var svc = BuildService(repo);

        // Act
        var response = await svc.HabitSummary(userId: 1);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = response.Result.Should().BeOfType<HabitSummaryDto>().Subject;

        // Today: 1 daily habit, 1 completed → 100 %
        dto.TodaySummary.HabitsToday.Should().Be(1,
            because: "GoalFrequency='' triggers the IsNullOrEmpty branch in IsDailyHabit");
        dto.TodaySummary.CompletedToday.Should().Be(1);
        dto.TodaySummary.TodayCompletionRate.Should().Be(100);

        // Weekly/monthly: 1 completion, expected ≥ 1 → rate is in [1, 100]
        dto.WeeklySummary.TotalCompletedThisWeek.Should().Be(1);
        dto.WeeklySummary.WeeklyCompletionRate.Should().BeInRange(1, 100);

        dto.MonthlySummary.TotalMonthlySessions.Should().Be(1);
        dto.MonthlySummary.MonthlyCompletionRate.Should().BeInRange(1, 100);

        // Health score: (100 + positive + positive) / 3 > 0
        dto.HabitHealthScore.Should().BeGreaterThan(0);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 3: Weekly habit with no completions → zeroed rates, HabitsToday == 0
    //
    // This test protects the GoalFrequency-aware ExpectedSessions logic (bug #17).
    // A weekly habit fails IsDailyHabit → dailyHabitCount = 0 → HabitsToday = 0.
    //
    // Hand-computed:
    //   dailyHabitCount = 0 → todayRate guard = 0, HabitsToday = 0
    //   ExpectedSessions("weekly", D, 7) = Ceiling(D/7.0) ≥ 1
    //   completedThisWeek = 0 → weeklyRate = 0
    //   completedThisMonth = 0 → monthlyRate = 0
    //   healthScore = 0
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OneWeeklyHabit_NoCompletions_DailyCountIsZero_RatesAreZero()
    {
        // Arrange
        var habit = WeeklyHabit(id: 20, userId: 2, name: "Weekly Review");

        var repo = RepoReturning(
            habits:         new List<Habit> { habit },
            todayTrackings: new List<HabitTracking>(),   // nothing completed
            weekTrackings:  new List<HabitTracking>(),
            monthTrackings: new List<HabitTracking>());

        var svc = BuildService(repo);

        // Act
        var response = await svc.HabitSummary(userId: 2);

        // Assert
        response.IsSuccess.Should().BeTrue();

        var dto = response.Result.Should().BeOfType<HabitSummaryDto>().Subject;

        // Weekly habit must NOT contribute to the daily-habit count (HabitsToday)
        dto.TodaySummary.HabitsToday.Should().Be(0,
            because: "GoalFrequency='weekly' fails IsDailyHabit; only empty/null or 'day'-containing frequencies qualify");
        dto.TodaySummary.TodayCompletionRate.Should().Be(0);
        dto.TodaySummary.CompletedToday.Should().Be(0);

        // No completions → all rates are 0 (expectedThisWeek ≥ 1, but numerator is 0)
        dto.WeeklySummary.WeeklyCompletionRate.Should().Be(0);
        dto.WeeklySummary.TotalCompletedThisWeek.Should().Be(0);

        dto.MonthlySummary.MonthlyCompletionRate.Should().Be(0);
        dto.MonthlySummary.TotalMonthlySessions.Should().Be(0);

        dto.HabitHealthScore.Should().Be(0);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 4: Mixed daily (GoalFrequency="") + weekly habits — daily contributes
    //          to HabitsToday, weekly does not; both contribute to weekly/monthly
    //          expected sessions (per-frequency math, bug #17's fix).
    //
    // Hand-computed (date-agnostic parts only):
    //   dailyHabitCount = 1 (only the "" habit)
    //   HabitsToday     = 1 (not 2)
    //   completedToday  = 1 (only the daily tracking)
    //   todayRate       = 100
    //
    //   expectedThisWeek = ExpectedSessions("", D, 7)     [fallthrough → D]
    //                    + ExpectedSessions("weekly", D, 7) [→ Ceiling(D/7.0)]
    //                    = D + Ceiling(D/7.0)  ≥ 2 (since D ≥ 1)
    //   completedThisWeek = 2 (both trackings)
    //   weeklyRate = min(100, 2*100/(D + Ceiling(D/7.0))) ≥ 1
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MixedDailyAndWeeklyHabits_OnlyDailyCountsTowardHabitsToday()
    {
        // Arrange
        var dailyHabit  = DailyHabit(id: 30, userId: 3, name: "Meditate");
        var weeklyHabit = WeeklyHabit(id: 31, userId: 3, name: "Long Run");

        var dailyTracking  = new HabitTracking { Id = 10, HabitId = 30, UserId = 3, IsCompleted = true };
        var weeklyTracking = new HabitTracking { Id = 11, HabitId = 31, UserId = 3, IsCompleted = true };

        var repo = RepoReturning(
            habits:         new List<Habit> { dailyHabit, weeklyHabit },
            todayTrackings: new List<HabitTracking> { dailyTracking },            // only daily habit tracked today
            weekTrackings:  new List<HabitTracking> { dailyTracking, weeklyTracking },
            monthTrackings: new List<HabitTracking> { dailyTracking, weeklyTracking });

        var svc = BuildService(repo);

        // Act
        var response = await svc.HabitSummary(userId: 3);

        // Assert
        response.IsSuccess.Should().BeTrue();

        var dto = response.Result.Should().BeOfType<HabitSummaryDto>().Subject;

        // Only the daily (GoalFrequency="") habit contributes to today's count
        dto.TodaySummary.HabitsToday.Should().Be(1,
            because: "weekly habits must not inflate the daily-habit count (IsDailyHabit guard)");
        dto.TodaySummary.CompletedToday.Should().Be(1);
        dto.TodaySummary.TodayCompletionRate.Should().Be(100);

        // Both habits contribute to weekly expected (per-frequency calculation);
        // 2 completions → rate > 0 (and ≤ 100 due to Math.Min cap)
        dto.WeeklySummary.TotalCompletedThisWeek.Should().Be(2);
        dto.WeeklySummary.WeeklyCompletionRate.Should().BeInRange(1, 100);

        // Same for monthly
        dto.MonthlySummary.TotalMonthlySessions.Should().Be(2);
        dto.MonthlySummary.MonthlyCompletionRate.Should().BeInRange(1, 100);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 5 (regression guard): a habit with the LITERAL GoalFrequency = "daily"
    // — the model's default value — must be counted as a daily habit.
    //
    // This guards the bug fixed alongside this suite: IsDailyHabit/ExpectedSessions
    // used Contains("day"), but "daily" (d-a-i-l-y) does NOT contain "day", so every
    // habit created with the default frequency was silently excluded from the today
    // rate. With the fix (IsDailyFrequency also matches "dai"), HabitsToday must be 1.
    // Before the fix this test FAILS (HabitsToday == 0); after, it passes.
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task LiteralDailyFrequency_CountsAsDailyHabit()
    {
        var habit = new Habit
        {
            Id            = 20,
            UserId        = 1,
            Name          = "Read",
            Frequency     = "Daily",
            GoalFrequency = "daily",   // the literal model default — the bug trigger
            GoalValue     = 1,
            GoalUnit      = "times",
        };
        var tracking = new HabitTracking
        {
            Id = 1, HabitId = habit.Id, UserId = habit.UserId, IsCompleted = true,
        };

        var repo = RepoReturning(
            habits:         new List<Habit> { habit },
            todayTrackings: new List<HabitTracking> { tracking },
            weekTrackings:  new List<HabitTracking> { tracking },
            monthTrackings: new List<HabitTracking> { tracking });

        var svc = BuildService(repo);

        var response = await svc.HabitSummary(userId: 1);

        response.IsSuccess.Should().BeTrue();
        var dto = response.Result.Should().BeOfType<HabitSummaryDto>().Subject;

        dto.TodaySummary.HabitsToday.Should().Be(1,
            because: "GoalFrequency='daily' (the default) must be recognized as a daily habit");
        dto.TodaySummary.CompletedToday.Should().Be(1);
        dto.TodaySummary.TodayCompletionRate.Should().Be(100);
    }
}
