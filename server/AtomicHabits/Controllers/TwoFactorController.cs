using AtomicHabits.Models;
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

        [HttpPost("recovery-codes/regenerate")]
        public async Task<IActionResult> RegenerateRecoveryCodes([FromBody] TwoFactorCodeDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            // Require a current TOTP code — same authorization bar as disabling.
            if (!await _service.VerifyAsync(userId.Value, dto.Code, ct))
            {
                return BadRequest(new ApiResponse
                {
                    IsSuccess = false,
                    StatusCode = System.Net.HttpStatusCode.BadRequest,
                    ErrorMessages = new List<string> { "Invalid code" }
                });
            }

            var codes = await _service.GenerateRecoveryCodesAsync(userId.Value, ct);
            return Ok(new ApiResponse
            {
                IsSuccess = true,
                StatusCode = System.Net.HttpStatusCode.OK,
                Result = new { recoveryCodes = codes }
            });
        }

        [HttpGet("recovery-codes/count")]
        public async Task<IActionResult> RecoveryCodesCount(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var remaining = await _service.CountRemainingRecoveryCodesAsync(userId.Value, ct);
            return Ok(new ApiResponse
            {
                IsSuccess = true,
                StatusCode = System.Net.HttpStatusCode.OK,
                Result = new { remaining }
            });
        }
    }
}
