namespace AtomicHabits.Utils
{
    public record StreakResult(int CurrentStreak, int LongestStreak);

    public static class StreakCalculator
    {
        public static StreakResult Compute(IEnumerable<DateTime> completedDates, DateTime? today = null)
        {
            var anchor = (today ?? DateTime.UtcNow).Date;

            var distinctSorted = completedDates
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (distinctSorted.Count == 0) return new StreakResult(0, 0);

            int longest = 1;
            int run = 1;
            for (int i = 1; i < distinctSorted.Count; i++)
            {
                run = (distinctSorted[i] - distinctSorted[i - 1]).TotalDays == 1 ? run + 1 : 1;
                if (run > longest) longest = run;
            }

            int current = 0;
            var descending = distinctSorted.AsEnumerable().Reverse().ToList();
            var lastCompleted = descending[0];
            var daysSinceLast = (anchor - lastCompleted).TotalDays;

            if (daysSinceLast <= 1)
            {
                current = 1;
                for (int i = 1; i < descending.Count; i++)
                {
                    if ((descending[i - 1] - descending[i]).TotalDays == 1) current++;
                    else break;
                }
            }

            return new StreakResult(current, longest);
        }
    }
}
