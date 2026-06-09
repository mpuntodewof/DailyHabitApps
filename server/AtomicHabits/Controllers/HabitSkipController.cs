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
    public class HabitSkipController : ControllerBase
    {
        private readonly IHabitSkipService _service;
        public HabitSkipController(IHabitSkipService service) => _service = service;

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] HabitSkipCreateDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.CreateAsync(userId.Value, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpGet("habit/{habitId}")]
        public async Task<IActionResult> ListForHabit(int habitId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.ListForHabitAsync(userId.Value, habitId, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpDelete("{skipId}")]
        public async Task<IActionResult> Delete(int skipId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.DeleteAsync(userId.Value, skipId, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
