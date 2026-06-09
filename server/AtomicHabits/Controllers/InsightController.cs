using AtomicHabits.Authorization;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtomicHabits.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [RequiresActiveSubscription] // PAID — Plan 4 gate; non-Pro users get 403
    public class InsightController : ControllerBase
    {
        private readonly IInsightService _service;
        public InsightController(IInsightService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.GetInsightsAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
