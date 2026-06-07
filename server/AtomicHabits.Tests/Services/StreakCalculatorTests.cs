using System;
using AtomicHabits.Utils;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class StreakCalculatorTests
{
    private static readonly DateTime Today = new(2026, 6, 7); // a Sunday — guards the ISO-week edge

    [Fact]
    public void Empty_input_yields_zero_zero()
    {
        var r = StreakCalculator.Compute(Array.Empty<DateTime>(), Today);
        r.CurrentStreak.Should().Be(0);
        r.LongestStreak.Should().Be(0);
    }

    [Fact]
    public void Consecutive_days_ending_today_count_as_current_streak()
    {
        var dates = new[] { Today.AddDays(-2), Today.AddDays(-1), Today };
        var r = StreakCalculator.Compute(dates, Today);
        r.CurrentStreak.Should().Be(3);
        r.LongestStreak.Should().Be(3);
    }

    [Fact]
    public void Streak_completed_yesterday_is_still_current()
    {
        var dates = new[] { Today.AddDays(-2), Today.AddDays(-1) };
        var r = StreakCalculator.Compute(dates, Today);
        r.CurrentStreak.Should().Be(2);
    }

    [Fact]
    public void Gap_of_two_days_breaks_the_current_streak()
    {
        var dates = new[] { Today.AddDays(-3), Today.AddDays(-2) }; // last completion 2 days ago
        var r = StreakCalculator.Compute(dates, Today);
        r.CurrentStreak.Should().Be(0);
        r.LongestStreak.Should().Be(2);
    }

    [Fact]
    public void Longest_streak_is_found_even_when_not_current()
    {
        var dates = new[]
        {
            Today.AddDays(-10), Today.AddDays(-9), Today.AddDays(-8), Today.AddDays(-7), // run of 4
            Today.AddDays(-1), Today                                                     // current run of 2
        };
        var r = StreakCalculator.Compute(dates, Today);
        r.LongestStreak.Should().Be(4);
        r.CurrentStreak.Should().Be(2);
    }

    [Fact]
    public void Duplicate_dates_are_collapsed()
    {
        var dates = new[] { Today, Today, Today.AddDays(-1) };
        var r = StreakCalculator.Compute(dates, Today);
        r.CurrentStreak.Should().Be(2);
        r.LongestStreak.Should().Be(2);
    }
}
