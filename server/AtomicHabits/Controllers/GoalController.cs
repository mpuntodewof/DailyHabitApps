using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtomicHabits.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class GoalController : ControllerBase
    {
        private readonly IGoalFrameworkService _service;
        public GoalController(IGoalFrameworkService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.ListGoalsAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] GoalUpsertDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.CreateGoalAsync(userId.Value, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPut("{goalId}")]
        public async Task<IActionResult> Update(int goalId, [FromBody] GoalUpsertDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.UpdateGoalAsync(userId.Value, goalId, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpDelete("{goalId}")]
        public async Task<IActionResult> Delete(int goalId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.DeleteGoalAsync(userId.Value, goalId, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
