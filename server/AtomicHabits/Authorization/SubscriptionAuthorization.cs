using AtomicHabits.Data;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Authorization
{
    /// <summary>
    /// Requires an ACTIVE Pro subscription. Separate axis from [Permission] (RBAC).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class RequiresActiveSubscriptionAttribute : AuthorizeAttribute
    {
        public const string PolicyName = "sub:active";
        public RequiresActiveSubscriptionAttribute() : base(PolicyName) { }
    }

    public class ActiveSubscriptionRequirement : IAuthorizationRequirement { }

    public class SubscriptionAuthorizationHandler : AuthorizationHandler<ActiveSubscriptionRequirement>
    {
        private readonly AppDbContext _db;
        private readonly ILogger<SubscriptionAuthorizationHandler> _logger;

        public SubscriptionAuthorizationHandler(AppDbContext db, ILogger<SubscriptionAuthorizationHandler> logger)
        {
            _db = db;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveSubscriptionRequirement requirement)
        {
            var userId = context.User.GetUserId();
            if (userId is null) return;

            var isActive = await _db.Users
                .Where(u => u.Id == userId.Value)
                .Select(u => u.PlanTier == Models.PlanTier.Pro && u.SubscriptionStatus == Models.SubscriptionStatus.Active)
                .FirstOrDefaultAsync();

            if (isActive) context.Succeed(requirement);
            else _logger.LogInformation("Active subscription required; denied for user {UserId}", userId);
        }
    }
}
