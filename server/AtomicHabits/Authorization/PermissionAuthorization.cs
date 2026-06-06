using AtomicHabits.Data;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AtomicHabits.Authorization
{
    /// <summary>
    /// Marks a controller / action with the permission code required to access it,
    /// e.g. [Permission("Users.Manage")]. Backed by <see cref="PermissionPolicyProvider"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class PermissionAttribute : AuthorizeAttribute
    {
        public const string PolicyPrefix = "perm:";

        public PermissionAttribute(string code) : base($"{PolicyPrefix}{code}") { }
    }

    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string Code { get; }
        public PermissionRequirement(string code) => Code = code;
    }

    /// <summary>
    /// Builds an authorization policy on demand for any "perm:CODE" policy name,
    /// so we don't have to pre-register every permission.
    /// </summary>
    public class PermissionPolicyProvider : IAuthorizationPolicyProvider
    {
        private readonly DefaultAuthorizationPolicyProvider _fallback;

        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        {
            _fallback = new DefaultAuthorizationPolicyProvider(options);
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (policyName.StartsWith(PermissionAttribute.PolicyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var code = policyName.Substring(PermissionAttribute.PolicyPrefix.Length);
                var policy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(code))
                    .Build();
                return Task.FromResult<AuthorizationPolicy?>(policy);
            }

            return _fallback.GetPolicyAsync(policyName);
        }
    }

    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly AppDbContext _db;
        private readonly ILogger<PermissionAuthorizationHandler> _logger;

        public PermissionAuthorizationHandler(AppDbContext db, ILogger<PermissionAuthorizationHandler> logger)
        {
            _db = db;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var userId = context.User.GetUserId();
            if (userId is null) return;

            var hasPermission = await _db.UserRoles
                .Where(ur => ur.UserId == userId.Value)
                .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
                .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p.Name)
                .AnyAsync(name => name == requirement.Code);

            if (hasPermission)
            {
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogInformation("Permission {Code} denied for user {UserId}", requirement.Code, userId);
            }
        }
    }
}
