using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AtomicHabits.Models
{
    public enum MilestoneStatus { Active = 0, Done = 1 }

    public class Milestone
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int GoalId { get; set; }

        [Required]
        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        public MilestoneStatus Status { get; set; } = MilestoneStatus.Active;

        public int OrderIndex { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        [JsonIgnore]
        public User? User { get; set; }

        [ForeignKey("GoalId")]
        [JsonIgnore]
        public Goal? Goal { get; set; }

        [JsonIgnore]
        public ICollection<Habit> Habits { get; set; } = new List<Habit>();
    }
}
