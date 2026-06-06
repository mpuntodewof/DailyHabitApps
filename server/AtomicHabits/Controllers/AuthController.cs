using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Service;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Claims;

namespace AtomicHabits.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ITokenService _tokenService;
        private readonly AppDbContext _db;
        public AuthController(IAuthService authService, ITokenService tokenService, AppDbContext db)
        {
            _authService = authService;
            _tokenService = tokenService;
            _db = db;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register(RegisterDto dto, CancellationToken cancellationToken)
        {
            var res = await _authService.RegisterAsync(dto, HttpContext);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            var res = await _authService.LoginAsync(dto, HttpContext);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost("verify-2fa")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyTwoFactor([FromBody] VerifyTwoFactorDto dto, CancellationToken ct)
        {
            var res = await _authService.VerifyTwoFactorAsync(dto.TwoFactorToken, dto.Code, HttpContext, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost("forgot-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDTO dto, CancellationToken cancellationToken)
        {
            var res = await _authService.ForgotPasswordAsync(dto, cancellationToken);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDTO dto, CancellationToken cancellationToken)
        {
            var res = await _authService.ResetPasswordAsync(dto, cancellationToken);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost("refresh-token")]
        [AllowAnonymous]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDto? dto, CancellationToken cancellationToken)
        {
            var res = await _authService.RefreshTokenAsync(HttpContext, dto, cancellationToken);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpPost("revoke-refresh-token")]
        public async Task<IActionResult> RevokeRefreshToken(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var res = await _authService.RevokeRefreshTokenAsync(userId.Value, HttpContext, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var res = await _authService.RevokeRefreshTokenAsync(userId.Value, HttpContext, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var user = await _db.Users
                .Where(u => u.Id == userId.Value)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.Email,
                    u.AvatarUrl,
                    Roles = u.UserRoles!.Select(ur => ur.Role.Name).ToList()
                })
                .FirstOrDefaultAsync(ct);

            if (user == null) return NotFound();

            var permissions = await _db.UserRoles
                .Where(ur => ur.UserId == userId.Value)
                .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
                .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p.Name)
                .Distinct()
                .ToListAsync(ct);

            return Ok(new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = new
                {
                    user.Id,
                    user.Username,
                    user.Email,
                    user.AvatarUrl,
                    user.Roles,
                    permissions
                }
            });
        }

    }
}
