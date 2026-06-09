using AtomicHabits.Config;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stripe;

namespace AtomicHabits.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BillingController : ControllerBase
    {
        private readonly IBillingService _billing;
        private readonly IStripeWebhookHandler _webhook;
        private readonly StripeOptions _opts;
        private readonly ILogger<BillingController> _logger;

        public BillingController(IBillingService billing, IStripeWebhookHandler webhook,
            IOptions<StripeOptions> opts, ILogger<BillingController> logger)
        {
            _billing = billing;
            _webhook = webhook;
            _opts = opts.Value;
            _logger = logger;
        }

        [Authorize]
        [HttpPost("create-checkout-session")]
        public async Task<IActionResult> CreateCheckout(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _billing.CreateCheckoutSessionAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpPost("create-portal-session")]
        public async Task<IActionResult> CreatePortal(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _billing.CreatePortalSessionAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [AllowAnonymous]
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook(CancellationToken ct)
        {
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync(ct);
            var signature = Request.Headers["Stripe-Signature"].ToString();

            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(json, signature, _opts.WebhookSecret);
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Stripe webhook signature verification failed");
                return BadRequest();
            }

            var normalized = MapEvent(stripeEvent);
            if (normalized != null)
                await _webhook.HandleAsync(normalized, ct);

            return Ok();
        }

        private static StripeWebhookEvent? MapEvent(Event e)
        {
            switch (e.Type)
            {
                case "checkout.session.completed":
                {
                    if (e.Data.Object is not Stripe.Checkout.Session s) return null;
                    return new StripeWebhookEvent
                    {
                        Type = e.Type,
                        CustomerId = s.CustomerId,
                        SubscriptionId = s.SubscriptionId,
                        Status = "active"
                    };
                }
                case "customer.subscription.updated":
                case "customer.subscription.deleted":
                {
                    if (e.Data.Object is not Subscription sub) return null;
                    return new StripeWebhookEvent
                    {
                        Type = e.Type,
                        CustomerId = sub.CustomerId,
                        SubscriptionId = sub.Id,
                        Status = sub.Status,
                        // Stripe.net 52: CurrentPeriodEnd moved off Subscription onto each SubscriptionItem.
                        CurrentPeriodEnd = sub.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd
                    };
                }
                case "invoice.payment_failed":
                {
                    if (e.Data.Object is not Invoice inv) return null;
                    return new StripeWebhookEvent { Type = e.Type, CustomerId = inv.CustomerId };
                }
                default:
                    return null;
            }
        }
    }
}
