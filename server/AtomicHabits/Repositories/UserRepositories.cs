using AtomicHabits.Data;
using AtomicHabits.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace AtomicHabits.Repositories
{
    public interface IUserRepositories
    {
        Task<User?> GetByUsernameAsync(string username);
        Task<User?> GetByEmailAsync(string email);
        Task<User?> GetByIdAsync(int id);
        Task<List<string>> GetUserRolesAsync(int userId);
        Task<bool> ValidatePasswordAsync(User user, string password);
        Task<bool> IsEmailConfirmedAsync(User user);
        Task<bool> SetPasswordResetTokenAsync(User user, string token);
        Task<User?> GetByResetTokenAsync(string token);
        Task<bool> UpdatePasswordAsync(User user, string newPassword);
    }

    public class UserRepositories : IUserRepositories
    {
        private readonly AppDbContext _context;
        private readonly ILogger<UserRepositories> _log;

        public UserRepositories(AppDbContext context, ILogger<UserRepositories> log)
        {
            _context = context;
            _log = log;
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            try
            {
                return await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[GetByUsernameAsync] Error");
                throw;
            }
        }


        public async Task<User?> GetByEmailAsync(string email)
        {
            try
            {
                return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[GetByEmailAsync] Error");
                throw;
            }
        }

        public async Task<User?> GetByIdAsync(int id)
        {
            try
            {
                return await _context.Users.FindAsync(id);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[GetByIdAsync] Error");
                throw;
            }
        }

        public async Task<List<string>> GetUserRolesAsync(int userId)
        {
            try
            {
                var roleIds = await _context.UserRoles
               .Where(ur => ur.UserId == userId)
               .Select(ur => ur.RoleId)
               .ToListAsync();

                if (roleIds == null || !roleIds.Any())
                    return new List<string>();

                // Get role names
                var roles = await _context.Roles
                    .Where(r => roleIds.Contains(r.Id))
                    .Select(r => r.Name)
                    .ToListAsync();

                return roles;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[GetUserRolesAsync] Error");
                throw;
            }           
        }

        public Task<bool> ValidatePasswordAsync(User user, string password)
        {
            try
            {
                return Task.FromResult(BCrypt.Net.BCrypt.Verify(password, user.PasswordHash));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[ValidatePasswordAsync] Error");
                throw;
            } 
        }

        public Task<bool> IsEmailConfirmedAsync(User user)
        {
            try
            {
                return Task.FromResult(true); // Placeholder for actual email confirmation logic

            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[IsEmailConfirmedAsync] Error");
                throw;
            }
        }

        public async Task<bool> SetPasswordResetTokenAsync(User user, string token)
        {
            try
            {
                user.PasswordResetToken = token;
                user.ResetTokenExpiry = DateTime.UtcNow.AddHours(1);
                _context.Users.Update(user);
                return await _context.SaveChangesAsync() > 0;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[SetPasswordResetTokenAsync] Error");
                throw;
            }          
        }

        public async Task<User?> GetByResetTokenAsync(string token)
        {
            try
            {
               return await _context.Users.FirstOrDefaultAsync(u => u.PasswordResetToken == token && u.ResetTokenExpiry > DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[GetByResetTokenAsync] Error");
                throw;
            }
        }

        public async Task<bool> UpdatePasswordAsync(User user, string newPassword)
        {
            try
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
                user.PasswordResetToken = null;
                user.ResetTokenExpiry = null;
                _context.Users.Update(user);
                return await _context.SaveChangesAsync() > 0;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[UpdatePasswordAsync] Error");
                throw;
            }
        }
    }
}
