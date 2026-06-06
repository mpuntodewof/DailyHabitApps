using AtomicHabits.Data;
using AtomicHabits.Models;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using System.Net;
using System.Security.Cryptography;

namespace AtomicHabits.Services
{
    public interface ITwoFactorService
    {
        Task<ApiResponse> StartEnrollmentAsync(int userId, CancellationToken ct);
        Task<ApiResponse> ConfirmEnrollmentAsync(int userId, string code, CancellationToken ct);
        Task<ApiResponse> DisableAsync(int userId, string code, CancellationToken ct);
        Task<bool> IsEnabledAsync(int userId, CancellationToken ct);
        Task<bool> VerifyAsync(int userId, string code, CancellationToken ct);
    }

    public class TwoFactorService : ITwoFactorService
    {
        // 30-second window with ±1 step tolerance handles minor clock skew between phone and server.
        private const int VerificationWindowSize = 1;
        private const string Issuer = "AtomicHabits";

        private readonly AppDbContext _db;
        private readonly ILogger<TwoFactorService> _logger;

        public TwoFactorService(AppDbContext db, ILogger<TwoFactorService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse> StartEnrollmentAsync(int userId, CancellationToken ct)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null) return Error(HttpStatusCode.NotFound, "User not found");

            // Generate (or replace) the secret. If already enabled, refuse — they should disable first.
            var existing = await _db.UserTwoFactors.FirstOrDefaultAsync(t => t.UserId == userId, ct);
            if (existing?.IsEnabled == true)
            {
                return Error(HttpStatusCode.Conflict, "2FA is already enabled. Disable first to re-enroll.");
            }

            var secretBytes = RandomNumberGenerator.GetBytes(20);
            var secretBase32 = Base32Encoding.ToString(secretBytes).TrimEnd('=');

            if (existing == null)
            {
                _db.UserTwoFactors.Add(new UserTwoFactor
                {
                    UserId = userId,
                    SecretBase32 = secretBase32,
                    IsEnabled = false,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.SecretBase32 = secretBase32;
                existing.IsEnabled = false;
                existing.EnabledAt = null;
                existing.DisabledAt = null;
            }
            await _db.SaveChangesAsync(ct);

            var label = Uri.EscapeDataString($"{Issuer}:{user.Email ?? user.Username ?? "user"}");
            var otpauthUri = $"otpauth://totp/{label}?secret={secretBase32}&issuer={Issuer}&algorithm=SHA1&digits=6&period=30";

            return new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = new { secret = secretBase32, otpauthUri }
            };
        }

        public async Task<ApiResponse> ConfirmEnrollmentAsync(int userId, string code, CancellationToken ct)
        {
            var record = await _db.UserTwoFactors.FirstOrDefaultAsync(t => t.UserId == userId, ct);
            if (record == null) return Error(HttpStatusCode.NotFound, "No enrollment in progress. Call enable-init first.");

            if (!VerifyTotp(record.SecretBase32, code))
            {
                return Error(HttpStatusCode.BadRequest, "Invalid code");
            }

            record.IsEnabled = true;
            record.EnabledAt = DateTime.UtcNow;
            record.DisabledAt = null;

            // Mirror the flag on User for quick reads.
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user != null) user.TwoFactorEnabled = true;

            await _db.SaveChangesAsync(ct);

            return new ApiResponse { IsSuccess = true, StatusCode = HttpStatusCode.OK, Result = new { enabled = true } };
        }

        public async Task<ApiResponse> DisableAsync(int userId, string code, CancellationToken ct)
        {
            var record = await _db.UserTwoFactors.FirstOrDefaultAsync(t => t.UserId == userId, ct);
            if (record == null || !record.IsEnabled) return Error(HttpStatusCode.BadRequest, "2FA is not enabled");

            if (!VerifyTotp(record.SecretBase32, code))
            {
                return Error(HttpStatusCode.BadRequest, "Invalid code");
            }

            record.IsEnabled = false;
            record.DisabledAt = DateTime.UtcNow;

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user != null) user.TwoFactorEnabled = false;

            await _db.SaveChangesAsync(ct);
            return new ApiResponse { IsSuccess = true, StatusCode = HttpStatusCode.OK, Result = new { enabled = false } };
        }

        public async Task<bool> IsEnabledAsync(int userId, CancellationToken ct)
        {
            return await _db.UserTwoFactors.AnyAsync(t => t.UserId == userId && t.IsEnabled, ct);
        }

        public async Task<bool> VerifyAsync(int userId, string code, CancellationToken ct)
        {
            var record = await _db.UserTwoFactors.FirstOrDefaultAsync(t => t.UserId == userId && t.IsEnabled, ct);
            if (record == null) return false;
            return VerifyTotp(record.SecretBase32, code);
        }

        private static bool VerifyTotp(string secretBase32, string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            try
            {
                var bytes = Base32Encoding.ToBytes(secretBase32);
                var totp = new Totp(bytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
                return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(VerificationWindowSize, VerificationWindowSize));
            }
            catch
            {
                return false;
            }
        }

        private static ApiResponse Error(HttpStatusCode status, string message) => new()
        {
            IsSuccess = false,
            StatusCode = status,
            ErrorMessages = new List<string> { message }
        };
    }
}
