using AtomicHabits.Authorization;
using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AtomicHabits.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AdminController(AppDbContext db)
        {
            _db = db;
        }

        [Permission("Users.Read")]
        [HttpGet("users")]
        public async Task<IActionResult> ListUsers(CancellationToken ct)
        {
            var users = await _db.Users
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.Email,
                    u.IsActive,
                    u.CreatedAt,
                    Roles = u.UserRoles!.Select(ur => ur.Role.Name).ToList()
                })
                .ToListAsync(ct);

            return Ok(new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = users
            });
        }

        [Permission("Roles.Read")]
        [HttpGet("roles")]
        public async Task<IActionResult> ListRoles(CancellationToken ct)
        {
            var roles = await _db.Roles
                .OrderBy(r => r.Name)
                .Select(r => new { r.Id, r.Name })
                .ToListAsync(ct);

            return Ok(new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = roles
            });
        }

        [Permission("Roles.Manage")]
        [HttpPost("users/{userId}/roles/{roleName}")]
        public async Task<IActionResult> AssignRole(int userId, string roleName, CancellationToken ct)
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName, ct);
            if (role == null) return NotFound(Error("Role not found"));

            var userExists = await _db.Users.AnyAsync(u => u.Id == userId, ct);
            if (!userExists) return NotFound(Error("User not found"));

            var existing = await _db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == role.Id, ct);
            if (!existing)
            {
                _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
                await _db.SaveChangesAsync(ct);
            }

            return Ok(new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = new { userId, roleName, assigned = true }
            });
        }

        [Permission("Roles.Manage")]
        [HttpDelete("users/{userId}/roles/{roleName}")]
        public async Task<IActionResult> RevokeRole(int userId, string roleName, CancellationToken ct)
        {
            // Don't let an admin remove their own Admin role — easy footgun.
            if (string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                var caller = User.GetUserId();
                if (caller == userId)
                {
                    return BadRequest(Error("You cannot remove your own Admin role"));
                }
            }

            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName, ct);
            if (role == null) return NotFound(Error("Role not found"));

            var link = await _db.UserRoles
                .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == role.Id, ct);
            if (link != null)
            {
                _db.UserRoles.Remove(link);
                await _db.SaveChangesAsync(ct);
            }

            return Ok(new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = new { userId, roleName, assigned = false }
            });
        }

        private static ApiResponse Error(string message) => new()
        {
            IsSuccess = false,
            ErrorMessages = new List<string> { message }
        };
    }
}
