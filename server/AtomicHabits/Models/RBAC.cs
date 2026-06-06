namespace AtomicHabits.Models
{
    public class User
    {
        public int Id { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? PasswordHash { get; set; }
        public string? AvatarUrl { get; set; }
        public bool? IsActive { get; set; }
        public bool? TwoFactorEnabled { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? PasswordResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }

        public ICollection<UserRole>? UserRoles { get; set; }
        public ICollection<Habit>? Habits { get; set; }
        public ICollection<HabitTracking>? HabitTrackings { get; set; }
        public ICollection<Streak>? Streaks { get; set; }
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    }


    public class Role
    {
        public int Id { get; set; }
        public string Name { get; set; }
        
        public ICollection<UserRole> UserRoles { get; set; }
        public ICollection<RolePermission> RolePermissions { get; set; }
    }


    public class UserRole
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }

        public int RoleId { get; set; }
        public Role Role { get; set; }
    }


    public class Module
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public ICollection<Permission> Permissions { get; set; }
    }


    public class Permission
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public int ModuleId { get; set; }
        public Module Module { get; set; }

        public ICollection<RolePermission> RolePermissions { get; set; }
    }


    public class RolePermission
    {
        public int Id { get; set; }
        public int RoleId { get; set; }
        public Role Role { get; set; }

        public int PermissionId { get; set; }
        public Permission Permission { get; set; }
    }

}
