using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AtomicHabits.Utils
{
    public static class ClaimsPrincipalExtensions
    {
        public static int? GetUserId(this ClaimsPrincipal principal)
        {
            if (principal == null) return null;

            var raw = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                   ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

            return int.TryParse(raw, out var id) ? id : null;
        }
    }
}
