using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AtomicHabits.Models
{
    public enum GoalStatus { Active = 0, Achieved = 1, Abandoned = 2 }

    public class Goal
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        public int? VisionId { get; set; }

        [Required]
        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        public GoalStatus Status { get; set; } = GoalStatus.Active;

        public DateTime? TargetDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        [JsonIgnore]
        public User? User { get; set; }

        [ForeignKey("VisionId")]
        [JsonIgnore]
        public Vision? Vision { get; set; }

        [JsonIgnore]
        public ICollection<Milestone> Milestones { get; set; } = new List<Milestone>();
    }
}
