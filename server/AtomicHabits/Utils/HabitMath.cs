using AtomicHabits.Models;

namespace AtomicHabits.Utils
{
    /// <summary>
    /// Shared habit math (expected sessions, week boundaries, frequency checks),
    /// extracted from HabitService so WeeklyReportService reuses one source of
    /// truth for goal-frequency math instead of duplicating it.
    /// </summary>
    public static class HabitMath
    {
        public static DateTime StartOfIsoWeek(DateTime today)
        {
            int diff = (7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return today.AddDays(-diff).Date;
        }

        // True for an empty/unset frequency or a daily one. NOTE: the literal "daily"
        // does NOT contain the substring "day" (d-a-i-l-y), and "daily" is the model's
        // default value — so a naive Contains("day") silently undercounts every default
        // habit. Match both forms.
        public static bool IsDailyFrequency(string f)
        {
            return string.IsNullOrEmpty(f) || f.Contains("day") || f.Contains("dai");
        }

        public static bool IsDailyHabit(Habit h)
        {
            return IsDailyFrequency((h.GoalFrequency ?? "").Trim().ToLowerInvariant());
        }

        // Expected completions for a habit within a window of `daysElapsed` days
        // out of a `periodLengthDays`-day period (week=7, month=daysInMonth, etc.).
        public static int ExpectedSessions(Habit h, int daysElapsed, int periodLengthDays)
        {
            if (daysElapsed <= 0 || periodLengthDays <= 0) return 0;

            var f = (h.GoalFrequency ?? "").Trim().ToLowerInvariant();
            if (IsDailyFrequency(f)) return daysElapsed;
            if (f.Contains("week"))
            {
                double weeksElapsed = (double)daysElapsed / 7.0;
                return (int)Math.Ceiling(weeksElapsed);
            }
            if (f.Contains("month"))
            {
                return daysElapsed >= 1 ? 1 : 0;
            }
            if (f.Contains("year"))
            {
                return 0;
            }
            return daysElapsed;
        }
    }
}
