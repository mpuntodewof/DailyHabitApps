using AtomicHabits.Models;

namespace AtomicHabits.Utils
{
    public static class SkipReasonExtensions
    {
        public static string Humanize(this SkipReason r) => r switch
        {
            SkipReason.LowEnergy => "Low Energy",
            SkipReason.NoMotivation => "No Motivation",
            SkipReason.ScheduleConflict => "Schedule Conflict",
            _ => r.ToString()
        };
    }
}
