using AtomicHabits.Config;
using AtomicHabits.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace AtomicHabits.Service
{
    public interface ITokenService
    {
        Task<string> GenerateToken(User user, List<string> roles);
        Task<string> GenerateRefreshToken();
        string GetUserIdFromToken(string token);
        Dictionary<string, string>? GetClaimsFromToken(string token);

        // Short-lived token issued after password check when 2FA is required.
        string GenerateTwoFactorPendingToken(User user, TimeSpan lifetime);
        int? ValidateTwoFactorPendingToken(string token);
    }

    public class TokenService : ITokenService
    {
        private readonly JwtOptions _jwt;
        private readonly SymmetricSecurityKey _signingKey;
        private readonly ILogger<TokenService> _log;

        public TokenService(IOptions<JwtOptions> jwt, ILogger<TokenService> log)
        {
            _jwt = jwt.Value;
            _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
            _log = log;
        }

        public Task<string> GenerateToken(User user, List<string> roles)
        {
            try
            {
                var claims = new List<Claim>
                {
                    new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                    new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                    new("username", user.Username ?? string.Empty)
                };

                roles.ForEach(r => claims.Add(new Claim(ClaimTypes.Role, r)));

                var token = new JwtSecurityToken(
                    _jwt.Issuer,
                    _jwt.Audience,
                    claims,
                    expires: DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes),
                    signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
                );

                return Task.FromResult(new JwtSecurityTokenHandler().WriteToken(token));
            }
            catch (Exception ex)
            {
                _log.LogError("Generate Token error: " + ex.Message);
                throw new Exception("Generate Token error: " + ex.Message);
            }
        }

        public Task<string> GenerateRefreshToken()
        {
            var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            return Task.FromResult(rawToken);
        }

        public string GetUserIdFromToken(string token)
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value;
        }

        public Dictionary<string, string>? GetClaimsFromToken(string token)
        {
            var handler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = false,
                ValidIssuer = _jwt.Issuer,
                ValidAudience = _jwt.Audience,
                IssuerSigningKey = _signingKey
            };


            var principal = handler.ValidateToken(token, parameters, out _);
            return principal.Claims.ToDictionary(c => c.Type, c => c.Value);
        }

        public string GenerateTwoFactorPendingToken(User user, TimeSpan lifetime)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim("twofa_pending", "true"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                _jwt.Issuer,
                _jwt.Audience,
                claims,
                expires: DateTime.UtcNow.Add(lifetime),
                signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
            );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public int? ValidateTwoFactorPendingToken(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var principal = handler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = _jwt.Issuer,
                    ValidAudience = _jwt.Audience,
                    IssuerSigningKey = _signingKey,
                    // Match the main JWT bearer config (Program.cs uses ClockSkew.Zero). Without
                    // this, the default 5-min skew lets a 5-min pending token live ~10 min here —
                    // more lenient than the rest of the auth system for a security-sensitive token.
                    ClockSkew = TimeSpan.Zero
                }, out _);

                if (principal.FindFirst("twofa_pending")?.Value != "true") return null;

                // JwtSecurityTokenHandler remaps the "sub" claim to ClaimTypes.NameIdentifier by
                // default (MapInboundClaims), so look under both names — otherwise the lookup
                // silently returns null and every 2FA challenge is rejected as "invalid/expired".
                var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                          ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                return int.TryParse(sub, out var id) ? id : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
