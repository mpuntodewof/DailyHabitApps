namespace AtomicHabits.Services
{
    public interface IStripeGateway
    {
        Task<string> EnsureCustomerAsync(string? existingCustomerId, string email, CancellationToken ct);
        Task<string> CreateCheckoutSessionUrlAsync(string customerId, string priceId, string successUrl, string cancelUrl, CancellationToken ct);
        Task<string> CreatePortalSessionUrlAsync(string customerId, string returnUrl, CancellationToken ct);
    }
}
