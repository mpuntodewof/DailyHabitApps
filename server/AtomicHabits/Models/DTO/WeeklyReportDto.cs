namespace AtomicHabits.Models.DTO
{
    public class WeeklyReportDto
    {
        public int PerformanceScore { get; set; }       // 0–100
        public string ScoreBand { get; set; } = "Needs work"; // Strong | Building | Needs work
        public string? BestHabit { get; set; }
        public string? WorstHabit { get; set; }
        public int ConsistencyDelta { get; set; }        // signed; this week − last week (pts)
        public string? TopMissReason { get; set; }       // humanized; null if no skips
        public string? FocusNextWeek { get; set; }       // templated; null if no data
        public string WeekStart { get; set; } = string.Empty; // "yyyy-MM-dd" Monday
    }
}
