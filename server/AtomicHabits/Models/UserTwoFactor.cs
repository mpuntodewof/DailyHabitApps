using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtomicHabits.Models
{
    public class UserTwoFactor
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        // Base32-encoded TOTP secret. Stored once, used for code verification.
        [Required]
        [MaxLength(128)]
        public string SecretBase32 { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EnabledAt { get; set; }
        public DateTime? DisabledAt { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }
    }
}
