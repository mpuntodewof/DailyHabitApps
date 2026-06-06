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
    public class HabitTrackingController : Controller
    {
        private readonly IAuthService _authservice;
        private readonly IHabitTrackingService _habitTracService;
        public HabitTrackingController(IAuthService authservice, IHabitTrackingService habitTracService)
        {
            _authservice = authservice;
            _habitTracService = habitTracService;
        }

        [Authorize]
        [HttpGet("habit-tracking-dates")]
        public async Task<IActionResult> GetHabitTrackingDates(int habitId, int userId, CancellationToken cancellationToken)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();

            var res = await _habitTracService.GetHabitTrackingDates(habitId, authUserId.Value, cancellationToken);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpGet("get-habit-stats/{habitId}/{userId}")]
        public async Task<IActionResult> GetHabitStats(int habitId, int userId, CancellationToken cancellationToken)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();

            var res = await _habitTracService.GetHabitStats(habitId, authUserId.Value, cancellationToken);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpGet("get-weekly")]
        public async Task<ActionResult<ApiResponse>> GetWeeklyDist([FromQuery] WeeklyDistributionDTO dto, CancellationToken ct)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();
            dto.UserId = authUserId.Value;

            var rawAuth = Request.Headers["Authorization"].ToString();
            var token = rawAuth?.Replace("Bearer ", "");

            var res = await _habitTracService.GetWeeklyAsync(dto, token, ct);

            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpGet("get-monthly")]
        public async Task<ActionResult<ApiResponse>> GetMonthlyDist([FromQuery] MonthlyDistributionDTO dto, CancellationToken ct)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();
            dto.UserId = authUserId.Value;

            var rawAuth = Request.Headers["Authorization"].ToString();
            var token = rawAuth?.Replace("Bearer ", "");

            var res = await _habitTracService.GetMonthlyAsync(dto, token, ct);

            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpGet("get-yearly")]
        public async Task<ActionResult<ApiResponse>> GetYearlyDist([FromQuery] YearlyDistributionDTO dto, CancellationToken ct)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();
            dto.UserId = authUserId.Value;

            var rawAuth = Request.Headers["Authorization"].ToString();
            var token = rawAuth?.Replace("Bearer ", "");

            var res = await _habitTracService.GetYearlyAsync(dto, token, ct);

            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpPost("submit-habit-progress")]
        public async Task<IActionResult> PostHabitProgress([FromBody] HabitTrackingDTO trackDto, CancellationToken cancellationToken)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();
            trackDto.UserId = authUserId.Value;

            var res = await _habitTracService.PostHabitProgress(trackDto, cancellationToken);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpPost("{habitId}/submit-daily")]
        public async Task<ActionResult<ApiResponse>> SubmitDailyHabit(int habitId, int minutes, CancellationToken cancellationToken)
        {
            var rawAuth = Request.Headers["Authorization"].ToString();
            var token = rawAuth?.Replace("Bearer ", "");

            var res = await _habitTracService.PostDailyHabit(habitId, minutes, cancellationToken, token);
            return StatusCode((int)res.StatusCode, res);
        }



    }
}
