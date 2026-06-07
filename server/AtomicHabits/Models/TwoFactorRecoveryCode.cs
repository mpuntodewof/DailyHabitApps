using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtomicHabits.Models
{
    // One row per recovery code. Codes are single-use; we store only the SHA-256 hash
    // of the normalized code (uppercased, dashes stripped), matching the RefreshToken.TokenHash pattern.
    public class TwoFactorRecoveryCode
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [MaxLength(128)]
        public string CodeHash { get; set; } = string.Empty;

        public bool IsUsed { get; set; } = false;

        public DateTime? UsedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public User? User { get; set; }
    }
}
