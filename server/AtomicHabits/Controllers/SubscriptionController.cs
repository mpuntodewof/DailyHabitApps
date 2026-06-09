using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace AtomicHabits.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SubscriptionController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        public SubscriptionController(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        public class SetPlanDto { public string Plan { get; set; } = "Free"; }

        // Manual plan toggle for the CURRENT user. Temporary bridge until Stripe.
        [HttpPost("set-plan")]
        public async Task<IActionResult> SetPlan([FromBody] SetPlanDto dto, CancellationToken ct)
        {
            if (!_env.IsDevelopment())
                return NotFound(); // manual plan toggle is a dev-only bridge; Stripe drives prod

            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
            if (user == null) return NotFound();

            if (string.Equals(dto.Plan, "Pro", StringComparison.OrdinalIgnoreCase))
            {
                user.PlanTier = PlanTier.Pro;
                user.SubscriptionStatus = SubscriptionStatus.Active;
                user.CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1);
            }
            else
            {
                user.PlanTier = PlanTier.Free;
                user.SubscriptionStatus = SubscriptionStatus.None;
                user.CurrentPeriodEnd = null;
            }
            await _db.SaveChangesAsync(ct);

            return Ok(new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = new { planTier = user.PlanTier.ToString(), subscriptionStatus = user.SubscriptionStatus.ToString() }
            });
        }
    }
}
