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
    public class MilestoneController : ControllerBase
    {
        private readonly IGoalFrameworkService _service;
        public MilestoneController(IGoalFrameworkService service) => _service = service;

        [HttpGet("goal/{goalId}")]
        public async Task<IActionResult> ListForGoal(int goalId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.ListMilestonesAsync(userId.Value, goalId, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] MilestoneUpsertDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.CreateMilestoneAsync(userId.Value, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPut("{milestoneId}")]
        public async Task<IActionResult> Update(int milestoneId, [FromBody] MilestoneUpsertDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.UpdateMilestoneAsync(userId.Value, milestoneId, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpDelete("{milestoneId}")]
        public async Task<IActionResult> Delete(int milestoneId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.DeleteMilestoneAsync(userId.Value, milestoneId, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
