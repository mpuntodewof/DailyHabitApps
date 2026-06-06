namespace AtomicHabits.Models
{
    public class HabitReminder
    {
        public int Id { get; set; }

        public int HabitId { get; set; }
        public Habit Habit { get; set; }

        public TimeSpan ReminderTime { get; set; }
        public string DaysOfWeek { get; set; } // Example: "Mon,Wed,Fri"
        public bool IsEnabled { get; set; } = true;

        // Last UTC date this reminder was fired — used by the scheduler to avoid double-fires.
        public DateOnly? LastFiredOn { get; set; }
    }
}
