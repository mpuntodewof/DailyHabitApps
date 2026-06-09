using AtomicHabits.Data;
using AtomicHabits.Models;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Services
{
    public class StripeWebhookEvent
    {
        public string Type { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public string? SubscriptionId { get; set; }
        public string? Status { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
    }

    public interface IStripeWebhookHandler
    {
        Task HandleAsync(StripeWebhookEvent ev, CancellationToken ct);
    }

    public class StripeWebhookHandler : IStripeWebhookHandler
    {
        private readonly AppDbContext _db;
        private readonly ILogger<StripeWebhookHandler> _logger;

        public StripeWebhookHandler(AppDbContext db, ILogger<StripeWebhookHandler> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task HandleAsync(StripeWebhookEvent ev, CancellationToken ct)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == ev.CustomerId, ct);
            if (user == null)
            {
                _logger.LogInformation("Stripe webhook {Type} for unknown customer {Cus} — ignored", ev.Type, ev.CustomerId);
                return;
            }

            switch (ev.Type)
            {
                case "checkout.session.completed":
                    user.StripeSubscriptionId = ev.SubscriptionId ?? user.StripeSubscriptionId;
                    user.PlanTier = PlanTier.Pro;
                    user.SubscriptionStatus = SubscriptionStatus.Active;
                    if (ev.CurrentPeriodEnd.HasValue) user.CurrentPeriodEnd = ev.CurrentPeriodEnd;
                    break;

                case "customer.subscription.updated":
                    user.SubscriptionStatus = MapStatus(ev.Status);
                    user.PlanTier = user.SubscriptionStatus == SubscriptionStatus.Canceled ? PlanTier.Free : PlanTier.Pro;
                    if (ev.CurrentPeriodEnd.HasValue) user.CurrentPeriodEnd = ev.CurrentPeriodEnd;
                    break;

                case "customer.subscription.deleted":
                    user.PlanTier = PlanTier.Free;
                    user.SubscriptionStatus = SubscriptionStatus.Canceled;
                    user.StripeSubscriptionId = null;
                    user.CurrentPeriodEnd = null;
                    break;

                case "invoice.payment_failed":
                    user.SubscriptionStatus = SubscriptionStatus.PastDue;
                    break;

                default:
                    _logger.LogInformation("Stripe webhook {Type} not handled — ignored", ev.Type);
                    return;
            }

            await _db.SaveChangesAsync(ct);
        }

        private static SubscriptionStatus MapStatus(string? stripeStatus) => stripeStatus switch
        {
            "active" or "trialing" => SubscriptionStatus.Active,
            "past_due" or "unpaid" => SubscriptionStatus.PastDue,
            "canceled" or "incomplete_expired" => SubscriptionStatus.Canceled,
            _ => SubscriptionStatus.PastDue
        };
    }
}
