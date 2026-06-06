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
    public class HabitReminderController : ControllerBase
    {
        private readonly IHabitReminderService _service;

        public HabitReminderController(IHabitReminderService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var res = await _service.ListAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] HabitReminderDTO dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var res = await _service.CreateAsync(userId.Value, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPut("{reminderId}")]
        public async Task<IActionResult> Update(int reminderId, [FromBody] HabitReminderDTO dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var res = await _service.UpdateAsync(userId.Value, reminderId, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpDelete("{reminderId}")]
        public async Task<IActionResult> Delete(int reminderId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var res = await _service.DeleteAsync(userId.Value, reminderId, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
