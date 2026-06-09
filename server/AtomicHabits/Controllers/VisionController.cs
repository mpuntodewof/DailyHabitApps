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
    public class VisionController : ControllerBase
    {
        private readonly IGoalFrameworkService _service;
        public VisionController(IGoalFrameworkService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.ListVisionsAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] VisionUpsertDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.CreateVisionAsync(userId.Value, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPut("{visionId}")]
        public async Task<IActionResult> Update(int visionId, [FromBody] VisionUpsertDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.UpdateVisionAsync(userId.Value, visionId, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpDelete("{visionId}")]
        public async Task<IActionResult> Delete(int visionId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.DeleteVisionAsync(userId.Value, visionId, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
