namespace AtomicHabits.Config
{
    public class StripeOptions
    {
        public const string SectionName = "Stripe";
        public string SecretKey { get; set; } = string.Empty;
        public string WebhookSecret { get; set; } = string.Empty;
        public string PriceId { get; set; } = string.Empty;
        public string SuccessUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
        public string PortalReturnUrl { get; set; } = string.Empty;
    }
}
