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
    public class TwoFactorController : ControllerBase
    {
        private readonly ITwoFactorService _service;

        public TwoFactorController(ITwoFactorService service)
        {
            _service = service;
        }

        [HttpGet("status")]
        public async Task<IActionResult> Status(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var enabled = await _service.IsEnabledAsync(userId.Value, ct);
            return Ok(new
            {
                isSuccess = true,
                statusCode = 200,
                result = new { enabled }
            });
        }

        [HttpPost("enable-init")]
        public async Task<IActionResult> EnableInit(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var res = await _service.StartEnrollmentAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost("enable-confirm")]
        public async Task<IActionResult> EnableConfirm([FromBody] TwoFactorCodeDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var res = await _service.ConfirmEnrollmentAsync(userId.Value, dto.Code, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost("disable")]
        public async Task<IActionResult> Disable([FromBody] TwoFactorCodeDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var res = await _service.DisableAsync(userId.Value, dto.Code, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
