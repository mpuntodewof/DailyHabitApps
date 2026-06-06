using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtomicHabits.Models
{
    public class Habit
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        [MaxLength(10)]
        public string? Color { get; set; } // Hexacolor CSS

        public string? Description { get; set; }

        [Required]
        public string Frequency { get; set; } // get from selection field ['Daily', 'Weekly', 'Monthly']

        // Goal Config
        public int GoalValue { get; set; } = 1; // 'Target amount (1 time, 30 minutes, 1 Hour, etc.)'

        [MaxLength(20)]
        public string GoalUnit { get; set; } = "times"; // 'times, minutes, hours, pages, glasses, custom, etc...'

        [MaxLength(20)]
        public string GoalFrequency { get; set; } = "daily"; // ['Daily', 'Weekly', 'Monthly']

        // Archive
        public bool IsArchived { get; set; } = false;

        // Metadata
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Foreign key relationship
        [ForeignKey("UserId")]
        public User User { get; set; }
        public ICollection<HabitReminder> HabitReminders { get; set; }
        public Streak Streak { get; set; }
        public ICollection<HabitTracking> HabitTrackings { get; set; }
        public ICollection<HabitTag> HabitTags { get; set; } = new List<HabitTag>();
    }
}
