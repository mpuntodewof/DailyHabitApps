namespace AtomicHabits.Models.DTO
{
    public class HabitDTO
    {
        public int UserId { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }
        public string Description { get; set; }
        public string Frequency { get; set; }
        public int GoalValue { get; set; }
        public string GoalUnit { get; set; }
        public string GoalFrequency { get; set; }

        // Optional link into the Goal->Milestone->Habit hierarchy. Nullable: when null the
        // habit is unlinked. When set, the service validates the milestone belongs to the
        // same user before persisting.
        public int? MilestoneId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
