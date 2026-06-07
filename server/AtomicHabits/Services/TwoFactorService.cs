using AtomicHabits.Data;
using AtomicHabits.Models;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AtomicHabits.Services
{
    public interface ITwoFactorService
    {
        Task<ApiResponse> StartEnrollmentAsync(int userId, CancellationToken ct);
        Task<ApiResponse> ConfirmEnrollmentAsync(int userId, string code, CancellationToken ct);
        Task<ApiResponse> DisableAsync(int userId, string code, CancellationToken ct);
        Task<bool> IsEnabledAsync(int userId, CancellationToken ct);
        Task<bool> VerifyAsync(int userId, string code, CancellationToken ct);
        Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(int userId, CancellationToken ct);
        Task<bool> VerifyRecoveryCodeAsync(int userId, string code, CancellationToken ct);
        Task<int> CountRemainingRecoveryCodesAsync(int userId, CancellationToken ct);
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

            // Issue recovery codes now — returned once, never retrievable again.
            var recoveryCodes = await GenerateRecoveryCodesAsync(userId, ct);

            return new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = new { enabled = true, recoveryCodes }
            };
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

            var codes = await _db.TwoFactorRecoveryCodes.Where(c => c.UserId == userId).ToListAsync(ct);
            if (codes.Count > 0) _db.TwoFactorRecoveryCodes.RemoveRange(codes);

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

        // Crockford base32 alphabet minus ambiguous I, L, O, U.
        private const string CodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        private const int CodeCount = 10;
        private const int CodeGroups = 3;
        private const int CodeGroupLen = 4;

        public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(int userId, CancellationToken ct)
        {
            // Replace any existing codes — regeneration invalidates the old set.
            var existing = await _db.TwoFactorRecoveryCodes.Where(c => c.UserId == userId).ToListAsync(ct);
            if (existing.Count > 0) _db.TwoFactorRecoveryCodes.RemoveRange(existing);

            var plaintext = new List<string>(CodeCount);
            for (int i = 0; i < CodeCount; i++)
            {
                var code = GenerateCode();
                plaintext.Add(code);
                _db.TwoFactorRecoveryCodes.Add(new TwoFactorRecoveryCode
                {
                    UserId = userId,
                    CodeHash = HashCode(code),
                    IsUsed = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync(ct);
            return plaintext;
        }

        public async Task<bool> VerifyRecoveryCodeAsync(int userId, string code, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            var hash = HashCode(code);

            // Guarded single-use: the UPDATE only flips a row that is still unused, so two
            // concurrent verifications of the same code cannot both succeed (the second affects
            // 0 rows). This closes the read-then-write double-spend race. ExecuteUpdate runs as a
            // single atomic SQL UPDATE. EF Core 7+ (project is on EF Core 9).
            var affected = await _db.TwoFactorRecoveryCodes
                .Where(c => c.UserId == userId && !c.IsUsed && c.CodeHash == hash)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(c => c.IsUsed, true)
                          .SetProperty(c => c.UsedAt, DateTime.UtcNow),
                    ct);

            return affected > 0;
        }

        public async Task<int> CountRemainingRecoveryCodesAsync(int userId, CancellationToken ct)
        {
            return await _db.TwoFactorRecoveryCodes.CountAsync(c => c.UserId == userId && !c.IsUsed, ct);
        }

        private static string GenerateCode()
        {
            var chars = new char[CodeGroups * CodeGroupLen];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            }
            // Group as XXXX-XXXX-XXXX.
            return string.Join("-", Enumerable.Range(0, CodeGroups)
                .Select(g => new string(chars, g * CodeGroupLen, CodeGroupLen)));
        }

        // Normalize (uppercase, strip dashes/whitespace) then SHA-256 hex. Matches RefreshToken hashing style.
        private static string HashCode(string code)
        {
            var normalized = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return Convert.ToHexString(bytes);
        }

        private static ApiResponse Error(HttpStatusCode status, string message) => new()
        {
            IsSuccess = false,
            StatusCode = status,
            ErrorMessages = new List<string> { message }
        };
    }
}
