using AtomicHabits.Models;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Data
{
    public static class DbSeeder
    {
        public const string AdminRole = "Admin";
        public const string UserRole = "User";

        // Module → Permission seed (codes are stable and used by [Permission("...")])
        private static readonly (string Module, string Description, string[] Permissions)[] Catalog =
        {
            ("Users",  "User management",            new[] { "Users.Read", "Users.Manage" }),
            ("Roles",  "Role / permission management", new[] { "Roles.Read", "Roles.Manage" }),
            ("Habits", "Habit data",                 new[] { "Habits.Read", "Habits.Write" }),
        };

        public static async Task SeedRolesAsync(AppDbContext db)
        {
            await SeedRoleSetAsync(db);
            await SeedModuleCatalogAsync(db);
            await SeedRolePermissionsAsync(db);
        }

        private static async Task SeedRoleSetAsync(AppDbContext db)
        {
            var roleNames = new[] { AdminRole, UserRole };

            foreach (var roleName in roleNames)
            {
                var exists = await db.Roles.AnyAsync(r => r.Name == roleName);
                if (!exists) db.Roles.Add(new Role { Name = roleName });
            }
            await db.SaveChangesAsync();
        }

        private static async Task SeedModuleCatalogAsync(AppDbContext db)
        {
            foreach (var (moduleName, description, permissions) in Catalog)
            {
                var module = await db.Modules.FirstOrDefaultAsync(m => m.Name == moduleName);
                if (module == null)
                {
                    module = new Module { Name = moduleName, Description = description };
                    db.Modules.Add(module);
                    await db.SaveChangesAsync();
                }

                foreach (var permName in permissions)
                {
                    var permExists = await db.Permissions
                        .AnyAsync(p => p.Name == permName && p.ModuleId == module.Id);
                    if (!permExists)
                    {
                        db.Permissions.Add(new Permission
                        {
                            Name = permName,
                            Description = permName,
                            ModuleId = module.Id
                        });
                    }
                }
            }
            await db.SaveChangesAsync();
        }

        private static async Task SeedRolePermissionsAsync(AppDbContext db)
        {
            var admin = await db.Roles.FirstOrDefaultAsync(r => r.Name == AdminRole);
            var user = await db.Roles.FirstOrDefaultAsync(r => r.Name == UserRole);
            if (admin == null || user == null) return;

            var allPermissions = await db.Permissions.ToListAsync();

            // Admin: every permission. User: only Habits.* (the app's own data).
            foreach (var perm in allPermissions)
            {
                if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == admin.Id && rp.PermissionId == perm.Id))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = admin.Id, PermissionId = perm.Id });
                }

                if (perm.Name.StartsWith("Habits."))
                {
                    if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == user.Id && rp.PermissionId == perm.Id))
                    {
                        db.RolePermissions.Add(new RolePermission { RoleId = user.Id, PermissionId = perm.Id });
                    }
                }
            }

            await db.SaveChangesAsync();
        }
    }
}
