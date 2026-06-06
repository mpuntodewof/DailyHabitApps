using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtomicHabits.Models
{
    public class UserPreferences
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        // General toggles
        public bool Notifications { get; set; } = true;
        public bool DarkMode { get; set; } = false;
        public bool EmailUpdates { get; set; } = true;
        public bool DeviceSync { get; set; } = true;

        // Theme
        [MaxLength(10)]
        public string PrimaryColor { get; set; } = "#2196f3";

        [MaxLength(40)]
        public string FontFamily { get; set; } = "Inter";

        public int BorderRadius { get; set; } = 8;

        public int Spacing { get; set; } = 8;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }
    }
}
