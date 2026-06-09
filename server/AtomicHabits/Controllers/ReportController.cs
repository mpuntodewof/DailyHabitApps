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
    public class ReportController : ControllerBase
    {
        private readonly IWeeklyReportService _service;
        public ReportController(IWeeklyReportService service) => _service = service;

        [HttpGet("weekly")]
        public async Task<IActionResult> Weekly(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.GetCurrentWeekAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
