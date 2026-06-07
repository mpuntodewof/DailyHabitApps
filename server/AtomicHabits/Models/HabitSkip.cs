using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AtomicHabits.Models
{
    public enum SkipReason
    {
        Busy = 0,
        Forgot = 1,
        LowEnergy = 2,
        NoMotivation = 3,
        ScheduleConflict = 4,
        Other = 5
    }

    public class HabitSkip
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int HabitId { get; set; }

        [Required]
        public DateOnly Date { get; set; }

        [Required]
        public SkipReason Reason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        [JsonIgnore]
        public User? User { get; set; }

        [ForeignKey("HabitId")]
        [JsonIgnore]
        public Habit? Habit { get; set; }
    }
}
