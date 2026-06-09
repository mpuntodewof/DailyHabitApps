using Stripe;
using Stripe.Checkout;

namespace AtomicHabits.Services
{
    public class StripeGateway : IStripeGateway
    {
        public async Task<string> EnsureCustomerAsync(string? existingCustomerId, string email, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(existingCustomerId)) return existingCustomerId;
            var customer = await new CustomerService().CreateAsync(
                new CustomerCreateOptions { Email = email }, cancellationToken: ct);
            return customer.Id;
        }

        public async Task<string> CreateCheckoutSessionUrlAsync(string customerId, string priceId, string successUrl, string cancelUrl, CancellationToken ct)
        {
            var session = await new SessionService().CreateAsync(new SessionCreateOptions
            {
                Mode = "subscription",
                Customer = customerId,
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions { Price = priceId, Quantity = 1 }
                },
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
            }, cancellationToken: ct);
            return session.Url;
        }

        public async Task<string> CreatePortalSessionUrlAsync(string customerId, string returnUrl, CancellationToken ct)
        {
            var session = await new Stripe.BillingPortal.SessionService().CreateAsync(
                new Stripe.BillingPortal.SessionCreateOptions { Customer = customerId, ReturnUrl = returnUrl },
                cancellationToken: ct);
            return session.Url;
        }
    }
}
