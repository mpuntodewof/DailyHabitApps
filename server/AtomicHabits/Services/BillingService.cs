using AtomicHabits.Config;
using AtomicHabits.Data;
using AtomicHabits.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IBillingService
    {
        Task<ApiResponse> CreateCheckoutSessionAsync(int userId, CancellationToken ct);
        Task<ApiResponse> CreatePortalSessionAsync(int userId, CancellationToken ct);
    }

    public class BillingService : IBillingService
    {
        private readonly AppDbContext _db;
        private readonly IStripeGateway _stripe;
        private readonly StripeOptions _opts;
        private readonly ILogger<BillingService> _logger;

        public BillingService(AppDbContext db, IStripeGateway stripe, IOptions<StripeOptions> opts, ILogger<BillingService> logger)
        {
            _db = db;
            _stripe = stripe;
            _opts = opts.Value;
            _logger = logger;
        }

        public async Task<ApiResponse> CreateCheckoutSessionAsync(int userId, CancellationToken ct)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null) return Error(HttpStatusCode.NotFound, "User not found");

            var customerId = await _stripe.EnsureCustomerAsync(user.StripeCustomerId, user.Email ?? "", ct);
            if (user.StripeCustomerId != customerId)
            {
                user.StripeCustomerId = customerId;
                await _db.SaveChangesAsync(ct);
            }

            var url = await _stripe.CreateCheckoutSessionUrlAsync(customerId, _opts.PriceId, _opts.SuccessUrl, _opts.CancelUrl, ct);
            return Ok(new { url });
        }

        public async Task<ApiResponse> CreatePortalSessionAsync(int userId, CancellationToken ct)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null) return Error(HttpStatusCode.NotFound, "User not found");
            if (string.IsNullOrEmpty(user.StripeCustomerId))
                return Error(HttpStatusCode.BadRequest, "No subscription to manage");

            var url = await _stripe.CreatePortalSessionUrlAsync(user.StripeCustomerId, _opts.PortalReturnUrl, ct);
            return Ok(new { url });
        }

        private static ApiResponse Ok(object result) =>
            new() { IsSuccess = true, StatusCode = HttpStatusCode.OK, Result = result };
        private static ApiResponse Error(HttpStatusCode status, string message) =>
            new() { IsSuccess = false, StatusCode = status, ErrorMessages = new List<string> { message } };
    }
}
