using AtomicHabits.Config;
using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using AtomicHabits.Service;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace AtomicHabits.Services
{
    public interface IAuthService
    {
        Task<ApiResponse> RegisterAsync(RegisterDto dto, HttpContext? ctx = null);
        Task<ApiResponse> LoginAsync(LoginDto dto, HttpContext? ctx = null);
        Task<ApiResponse> VerifyTwoFactorAsync(string pendingToken, string code, bool isRecoveryCode, HttpContext? ctx, CancellationToken ct);
        Task<ApiResponse> ForgotPasswordAsync(ForgotPasswordDTO dto, CancellationToken cancellationToken);
        Task<ApiResponse> ResetPasswordAsync(ResetPasswordDTO dto, CancellationToken cancellationToken);
        Task<ApiResponse> RefreshTokenAsync(HttpContext? ctx, RefreshTokenDto? dto, CancellationToken ct);
        Task<string?> GeneratePasswordResetTokenAsync(string email, CancellationToken cancellationToken);

        Task<UserInfoDto?> GetCurrentUserFromJwt(string? jwtToken);
        Task<string?> GetUserIdFromJwt(string? jwtToken);
        Task<string?> GetUserIdAsync(HttpContext httpContext);
        Task<UserInfoDto?> GetCurrentUserAsync(HttpContext httpContext);
        Task<ApiResponse> RevokeRefreshTokenAsync(int userId, HttpContext? ctx, CancellationToken ct);

    }

    public class AuthService : IAuthService
    {
        private readonly AppDbContext _db;
        private readonly ITokenService _tokenService;
        private readonly IUserRepositories _userRepo;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<AuthService> _logger;
        private readonly ITwoFactorService _twoFactor;
        private ApiResponse _response;
        private readonly JwtOptions _jwt;
        private readonly AppOptions _app;

        public AuthService(
            AppDbContext db,
            ITokenService tokenService,
            IUserRepositories userRepo,
            IEmailSender emailSender,
            ILogger<AuthService> logger,
            ITwoFactorService twoFactor,
            IOptions<JwtOptions> jwt,
            IOptions<AppOptions> app)
        {
            _db = db;
            _tokenService = tokenService;
            _userRepo = userRepo;
            _emailSender = emailSender;
            _logger = logger;
            _twoFactor = twoFactor;
            _response = new ApiResponse();
            _jwt = jwt.Value;
            _app = app.Value;
        }

        #region Main Feature 

        public async Task<ApiResponse> RegisterAsync(RegisterDto dto, HttpContext? ctx = null)
        {
            using var trx = await _db.Database.BeginTransactionAsync();
            try
            {
                if (await _db.Users.AnyAsync(x => x.Username == dto.Username || x.Email == dto.Email))
                {
                    _response.StatusCode = HttpStatusCode.BadRequest;
                    _response.IsSuccess = false;
                    _response.ErrorMessages.Add("Username or Email already exists");
                    return _response;
                }

                var user = new User
                {
                    Username = dto.Username,
                    Email = dto.Email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                await _db.Users.AddAsync(user);
                await _db.SaveChangesAsync();

                // assign role if exists
                var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == dto.Role);
                if (role != null)
                {
                    await _db.UserRoles.AddAsync(new UserRole { UserId = user.Id, RoleId = role.Id });
                    await _db.SaveChangesAsync();
                }

                var result = await IssueTokensAsync(user, ctx);

                // Commit the transaction so the new user, role assignment, and refresh token
                // actually persist. Without this, the `using` disposes the open transaction and
                // rolls everything back — the caller gets a valid token for a user that was never
                // saved (identity ids are still consumed, which is why ids climb on each attempt).
                await trx.CommitAsync();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthService.RegisterAsync] Error for {Username}", dto?.Username);
                await trx.RollbackAsync();
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages.Add(ex.Message);
                return _response;
            }
        }

        public async Task<ApiResponse> LoginAsync(LoginDto dto, HttpContext? ctx = null)
        {
            var response = new ApiResponse();
            try
            {
                if (dto == null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.IsSuccess = false;
                    response.ErrorMessages.Add("Username and password are required.");
                    return response;
                }

                var identifier = dto.Email.Trim();
                var user = await _userRepo.GetByEmailAsync(identifier);
                if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                {
                    response.StatusCode = HttpStatusCode.Unauthorized;
                    response.IsSuccess = false;
                    response.ErrorMessages.Add("Invalid credentials.");
                    return response;
                }

                if (await _twoFactor.IsEnabledAsync(user.Id, CancellationToken.None))
                {
                    var pendingToken = _tokenService.GenerateTwoFactorPendingToken(user, TimeSpan.FromMinutes(5));
                    response.IsSuccess = true;
                    response.StatusCode = HttpStatusCode.OK;
                    response.Result = new
                    {
                        requiresTwoFactor = true,
                        twoFactorToken = pendingToken
                    };
                    return response;
                }

                return await IssueTokensAsync(user, ctx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthService.LoginAsync] Error login attempt for {Username}", dto?.Email);
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages.Add(ex.Message);
                return _response;
            }
        }

        public async Task<ApiResponse> VerifyTwoFactorAsync(string pendingToken, string code, bool isRecoveryCode, HttpContext? ctx, CancellationToken ct)
        {
            try
            {
                var userId = _tokenService.ValidateTwoFactorPendingToken(pendingToken);
                if (userId is null)
                {
                    return new ApiResponse
                    {
                        IsSuccess = false,
                        StatusCode = HttpStatusCode.Unauthorized,
                        ErrorMessages = new List<string> { "Invalid or expired 2FA challenge token." }
                    };
                }

                var verified = isRecoveryCode
                    ? await _twoFactor.VerifyRecoveryCodeAsync(userId.Value, code, ct)
                    : await _twoFactor.VerifyAsync(userId.Value, code, ct);
                if (!verified)
                {
                    return new ApiResponse
                    {
                        IsSuccess = false,
                        StatusCode = HttpStatusCode.BadRequest,
                        ErrorMessages = new List<string> { "Invalid code." }
                    };
                }

                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
                if (user == null)
                {
                    return new ApiResponse
                    {
                        IsSuccess = false,
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorMessages = new List<string> { "User not found." }
                    };
                }

                return await IssueTokensAsync(user, ctx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthService.VerifyTwoFactorAsync] Error");
                return new ApiResponse
                {
                    IsSuccess = false,
                    StatusCode = HttpStatusCode.InternalServerError,
                    ErrorMessages = new List<string> { "Verify 2FA error: " + ex.Message }
                };
            }
        }

        public async Task<string?> GeneratePasswordResetTokenAsync(string email, CancellationToken cancellationToken = default)
        {
            var user = await _userRepo.GetByEmailAsync(email);
            if (user == null) return null;

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var success = await _userRepo.SetPasswordResetTokenAsync(user, token);
            return success ? token : null;
        }

        public async Task<ApiResponse> ForgotPasswordAsync(ForgotPasswordDTO dto, CancellationToken cancellationToken)
        {
            try
            {
                var user = await _userRepo.GetByEmailAsync(dto.Email);
                if (user == null)
                {
                    _response.StatusCode = HttpStatusCode.BadRequest;
                    _response.IsSuccess = false;
                    _response.ErrorMessages.Add("User not found.");
                    return _response;
                }

                var token = await GeneratePasswordResetTokenAsync(dto.Email);
                if (string.IsNullOrWhiteSpace(token))
                {
                    _response.StatusCode = HttpStatusCode.InternalServerError;
                    _response.IsSuccess = false;
                    _response.ErrorMessages.Add("Failed to generate password reset token.");
                    return _response;
                }

                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var baseUrl = _app.WebBaseUrl.TrimEnd('/');
                var path = _app.ResetPasswordPath.StartsWith('/') ? _app.ResetPasswordPath : "/" + _app.ResetPasswordPath;
                var resetPath = $"{baseUrl}{path}?token={encodedToken}&email={Uri.EscapeDataString(user.Email!)}";

                await _emailSender.SendEmailAsync(dto.Email, "Reset Password", MailBody(dto.Email, HtmlEncoder.Default.Encode(resetPath)));

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                _response.Result = new ForgotPasswordDTO();
                return _response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthService.ForgotPasswordAsync] Error for {Email}", dto.Email);
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages.Add(ex.Message);
                return _response;
            }
        }

        public async Task<ApiResponse> ResetPasswordAsync(ResetPasswordDTO dto, CancellationToken cancellationToken = default)
        {
            try
            {
                if (dto == null
                    || string.IsNullOrWhiteSpace(dto.Email)
                    || string.IsNullOrWhiteSpace(dto.Token)
                    || string.IsNullOrWhiteSpace(dto.Password))
                {
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.BadRequest;
                    _response.ErrorMessages.Add("Email, token and new password are all required.");
                    return _response;
                }

                if (!string.IsNullOrEmpty(dto.ConfirmPassword) && dto.Password != dto.ConfirmPassword)
                {
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.BadRequest;
                    _response.ErrorMessages.Add("New password and confirmation do not match.");
                    return _response;
                }

                var user = await _userRepo.GetByEmailAsync(dto.Email);
                if (user == null)
                {
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.BadRequest;
                    _response.ErrorMessages.Add("User not found.");
                    return _response;
                }

                string decodedToken;
                try
                {
                    decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(dto.Token));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[AuthService.ResetPasswordAsync] Token decode failed for {Email}", dto.Email);
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.BadRequest;
                    _response.ErrorMessages.Add("Invalid or expired reset token.");
                    return _response;
                }

                if (string.IsNullOrEmpty(user.PasswordResetToken)
                    || user.PasswordResetToken != decodedToken
                    || user.ResetTokenExpiry == null
                    || user.ResetTokenExpiry < DateTime.UtcNow)
                {
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.BadRequest;
                    _response.ErrorMessages.Add("Invalid or expired reset token.");
                    return _response;
                }

                // Atomic: hash new password and consume the reset token in a single save.
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
                user.PasswordResetToken = null;
                user.ResetTokenExpiry = null;
                _db.Users.Update(user);

                var rowsAffected = await _db.SaveChangesAsync(cancellationToken);
                if (rowsAffected <= 0)
                {
                    _logger.LogError("[AuthService.ResetPasswordAsync] SaveChanges returned 0 for {Email}", dto.Email);
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.InternalServerError;
                    _response.ErrorMessages.Add("Failed to update password.");
                    return _response;
                }

                _logger.LogInformation("[AuthService.ResetPasswordAsync] Password reset for {Email}", dto.Email);

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                return _response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthService.ResetPasswordAsync] Error for {Email}", dto?.Email);
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages.Add("Failed to reset password: " + ex.Message);
                return _response;
            }
        }

        #endregion


        #region Token Section

        private const string RefreshCookieName = "refreshToken";

        private TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_jwt.RefreshTokenDays > 0 ? _jwt.RefreshTokenDays : 7);

        private void WriteRefreshTokenCookie(HttpContext? ctx, string token)
        {
            if (ctx == null) return;

            ctx.Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/Auth",
                Expires = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime),
                IsEssential = true
            });
        }

        private void ClearRefreshTokenCookie(HttpContext? ctx)
        {
            if (ctx == null) return;

            ctx.Response.Cookies.Delete(RefreshCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/Auth"
            });
        }

        private async Task<ApiResponse> IssueTokensAsync(User user, HttpContext? ctx = null)
        {
            var roles = await _db.UserRoles
                .Where(x => x.UserId == user.Id)
                .Include(x => x.Role)
                .Select(x => x.Role.Name)
                .ToListAsync();

            var accessToken = await _tokenService.GenerateToken(user, roles);
            var refreshToken = await _tokenService.GenerateRefreshToken();

            var refreshEntity = new RefreshToken
            {
                UserId = user.Id,
                TokenHash = Hash(refreshToken),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime)
            };

            _db.RefreshTokens.Add(refreshEntity);
            await _db.SaveChangesAsync();

            WriteRefreshTokenCookie(ctx, refreshToken);

            _response.StatusCode = HttpStatusCode.OK;
            _response.IsSuccess = true;
            _response.Result = new
            {
                accessToken,
                expiresAt = DateTime.UtcNow.AddHours(1)
            };

            return _response;
        }

        public async Task<ApiResponse> RefreshTokenAsync(HttpContext? ctx, RefreshTokenDto? dto, CancellationToken ct)
        {
            try
            {
                var presented = ctx?.Request.Cookies[RefreshCookieName];
                if (string.IsNullOrWhiteSpace(presented))
                {
                    presented = dto?.RefreshToken;
                }

                if (string.IsNullOrWhiteSpace(presented))
                {
                    _response.StatusCode = HttpStatusCode.Unauthorized;
                    _response.IsSuccess = false;
                    _response.ErrorMessages.Add("Refresh token is missing");
                    return _response;
                }

                var hash = Hash(presented);

                var token = await _db.RefreshTokens
                    .Include(t => t.User)
                    .ThenInclude(u => u.UserRoles)!
                    .ThenInclude(r => r.Role)
                    .FirstOrDefaultAsync(t => t.TokenHash == hash && !t.IsRevoked, ct);

                if (token == null || token.ExpiresAt < DateTime.UtcNow)
                {
                    ClearRefreshTokenCookie(ctx);
                    _response.StatusCode = HttpStatusCode.Unauthorized;
                    _response.IsSuccess = false;
                    _response.ErrorMessages.Add("Invalid refresh token");
                    return _response;
                }

                token.IsRevoked = true;
                var roles = token.User.UserRoles!.Select(r => r.Role.Name).ToList();
                var newAccess = await _tokenService.GenerateToken(token.User, roles);
                var newRefresh = await _tokenService.GenerateRefreshToken();

                _db.RefreshTokens.Add(new RefreshToken
                {
                    UserId = token.UserId,
                    TokenHash = Hash(newRefresh),
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime)
                });

                await _db.SaveChangesAsync(ct);

                WriteRefreshTokenCookie(ctx, newRefresh);

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                _response.Result = new
                {
                    accessToken = newAccess,
                    expiresAt = DateTime.UtcNow.AddHours(1)
                };
                return _response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthService.RefreshTokenAsync] Error refreshing token, message: " + ex.Message);
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.IsSuccess = false;
                _response.ErrorMessages.Add("Error refresh token, message: " + ex.Message);
                return _response;
            }
        }

        public async Task<ApiResponse> RevokeRefreshTokenAsync(int userId, HttpContext? ctx, CancellationToken ct)
        {
            try
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

                if (user == null)
                {
                    _response.StatusCode = HttpStatusCode.NotFound;
                    _response.IsSuccess = false;
                    _response.ErrorMessages.Add("User not found");
                    return _response;
                }

                var activeTokens = await _db.RefreshTokens
                    .Where(t => t.UserId == userId && !t.IsRevoked)
                    .ToListAsync(ct);

                foreach (var t in activeTokens)
                {
                    t.IsRevoked = true;
                }

                await _db.SaveChangesAsync(ct);

                ClearRefreshTokenCookie(ctx);

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                return _response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthService.RevokeRefreshTokenAsync] Error revoke token, message: " + ex.Message);

                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages.Add("Error revoke token, message: " + ex.Message);
                return _response;
            }
        }

        #endregion

        #region JWT Token functions
        public async Task<UserInfoDto?> GetCurrentUserFromJwt(string? jwtToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(jwtToken)) return null;

                var claims = _tokenService.GetClaimsFromToken(jwtToken);
                if (claims == null || claims.Count == 0) return null;

                var userId = claims.TryGetValue(ClaimTypes.NameIdentifier, out var id) ? id : null;
                if (string.IsNullOrWhiteSpace(userId)) return null;
                var email = claims.TryGetValue(ClaimTypes.Email, out var e) ? e : string.Empty;
                var username = claims.TryGetValue("username", out var u) ? u : string.Empty;

                if (string.IsNullOrWhiteSpace(userId)) return null;

                return new UserInfoDto
                {
                    UserId = userId!,
                    Email = email ?? string.Empty,
                    Name = username ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthService.GetCurrentUserFromJwt] Error parsing token");
                return null;
            }
        }

        public async Task<string?> GetUserIdFromJwt(string? jwtToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(jwtToken)) return null;
                var userId = _tokenService.GetUserIdFromToken(jwtToken);
                return string.IsNullOrWhiteSpace(userId) ? null : userId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthService.GetUserIdFromJwt] Error parsing token");
                return null;
            }
        }

        public async Task<string?> GetUserIdAsync(HttpContext httpContext)
        {
            try
            {
                var token = ExtractTokenFromHeader(httpContext);
                if (token == null) return null;
                return await GetUserIdFromJwt(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthService.GetUserIdAsync] Error");
                return null;
            }
        }

        public async Task<UserInfoDto?> GetCurrentUserAsync(HttpContext httpContext)
        {
            try
            {
                var token = ExtractTokenFromHeader(httpContext);
                if (token == null) return null;
                return await GetCurrentUserFromJwt(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthService.GetCurrentUserAsync] Error");
                return null;
            }
        }

        private string? ExtractTokenFromHeader(HttpContext httpContext)
        {
            var header = httpContext?.Request?.Headers["Authorization"].ToString();
            if (string.IsNullOrWhiteSpace(header)) return null;
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
            return header.Substring("Bearer ".Length).Trim();
        }

        #endregion

        private string MailBody(string mail, string resetLink)
        {
            return $@"
                <html>
                    <body>
                        <p>Hi {mail},</p>
                        <p>We received a request to reset your password. Click the link below to reset your password:</p>
                        <p><a href='{resetLink}'>Reset Password</a></p>
                        <p>If you did not request a password reset, please ignore this email or contact support if you have questions.</p>
                        <p>Thanks,</p>
                        <p>The Team</p>
                    </body>
                </html>";
        }

        private static string Hash(string input)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
        }

    }
}
