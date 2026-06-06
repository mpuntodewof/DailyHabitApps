using AtomicHabits.Models;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // DbSets
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<Module> Modules { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<JwtKeys> JwtKeys { get; set; }
        public DbSet<Habit> Habits { get; set; }
        public DbSet<HabitTracking> HabitTrackings { get; set; }
        public DbSet<HabitReminder> HabitReminders { get; set; }
        public DbSet<Streak> Streaks { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<UserPreferences> UserPreferences { get; set; }
        public DbSet<UserTwoFactor> UserTwoFactors { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<HabitTag> HabitTags { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<RefreshToken>()
                .HasOne(r => r.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(r => r.UserId);

            modelBuilder.Entity<RefreshToken>()
                .HasIndex(r => r.TokenHash)
                .IsUnique()
                .HasDatabaseName("IX_RefreshTokens_TokenHash");

            modelBuilder.Entity<RefreshToken>()
                .HasIndex(r => new { r.UserId, r.IsRevoked })
                .HasDatabaseName("IX_RefreshTokens_UserId_IsRevoked");

            modelBuilder.Entity<HabitTracking>()
                .HasIndex(t => new { t.HabitId, t.TrackingDate })
                .HasDatabaseName("IX_HabitTrackings_HabitId_TrackingDate");

            modelBuilder.Entity<HabitTracking>()
                .HasIndex(t => new { t.UserId, t.TrackingDate })
                .HasDatabaseName("IX_HabitTrackings_UserId_TrackingDate");

            modelBuilder.Entity<Habit>()
                .HasIndex(h => new { h.UserId, h.IsArchived })
                .HasDatabaseName("IX_Habits_UserId_IsArchived");

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique()
                .HasDatabaseName("IX_Users_Email");

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique()
                .HasDatabaseName("IX_Users_Username");

            modelBuilder.Entity<UserRole>()
                .HasIndex(ur => new { ur.UserId, ur.RoleId })
                .IsUnique()
                .HasDatabaseName("IX_UserRoles_UserId_RoleId");

            modelBuilder.Entity<UserPreferences>()
                .HasIndex(up => up.UserId)
                .IsUnique()
                .HasDatabaseName("IX_UserPreferences_UserId");

            modelBuilder.Entity<UserTwoFactor>()
                .HasIndex(t => t.UserId)
                .IsUnique()
                .HasDatabaseName("IX_UserTwoFactors_UserId");

            modelBuilder.Entity<HabitReminder>()
                .HasIndex(r => new { r.IsEnabled, r.LastFiredOn })
                .HasDatabaseName("IX_HabitReminders_IsEnabled_LastFiredOn");

            modelBuilder.Entity<Tag>()
                .HasIndex(t => new { t.UserId, t.Name })
                .IsUnique()
                .HasDatabaseName("IX_Tags_UserId_Name");

            modelBuilder.Entity<HabitTag>()
                .HasKey(ht => new { ht.HabitId, ht.TagId });

            modelBuilder.Entity<HabitTag>()
                .HasOne(ht => ht.Habit)
                .WithMany(h => h.HabitTags)
                .HasForeignKey(ht => ht.HabitId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<HabitTag>()
                .HasOne(ht => ht.Tag)
                .WithMany(t => t.HabitTags)
                .HasForeignKey(ht => ht.TagId)
                // NoAction breaks the multiple-cascade-paths cycle (Users → Habits → HabitTags
                // and Users → Tags → HabitTags). The Habit side keeps cascade — that's the more
                // useful direction. Tag deletes still work because Tag has no FK back to Users
                // through a cascade path that isn't already broken.
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<HabitTag>()
                .HasIndex(ht => ht.TagId)
                .HasDatabaseName("IX_HabitTags_TagId");
        }
    }
}
