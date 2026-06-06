using AtomicHabits.Services;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AtomicHabits.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _service;
        public DashboardController(IDashboardService service)
        {
            _service = service;
        }

        [Authorize]
        [HttpGet("habit-card-overviews")]
        public async Task<IActionResult> GetCardOverview(int userId, CancellationToken ct)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();

            var res = await _service.GetCardOverviews(authUserId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpGet("heatmap")]
        public async Task<IActionResult> GetHeatmap([FromQuery] int days, CancellationToken ct)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();

            var res = await _service.GetHeatmap(authUserId.Value, days, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
