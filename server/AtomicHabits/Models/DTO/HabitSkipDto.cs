using System;

namespace AtomicHabits.Models.DTO
{
    public class HabitSkipDto
    {
        public int Id { get; set; }
        public int HabitId { get; set; }
        public DateOnly Date { get; set; }
        public string Reason { get; set; } = "Other"; // SkipReason name
    }

    public class HabitSkipCreateDto
    {
        public int HabitId { get; set; }
        public DateOnly? Date { get; set; }   // null => today (server uses UTC today)
        public string Reason { get; set; } = "Other";
    }
}
