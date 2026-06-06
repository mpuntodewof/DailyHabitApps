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
    public class HabitController : ControllerBase
    {
        private readonly IHabitService _habitService;
        private readonly IAuthService _authservice;

        public HabitController(IHabitService habitService, IAuthService authservice)
        {
            _habitService = habitService;
            _authservice = authservice;
        }

        [Authorize]
        [HttpGet("get-habits/{userId}")]
        public async Task<IActionResult> GetHabits(int userId, CancellationToken ct, [FromQuery] bool includeArchived = false)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();

            var rawAuth = Request.Headers["Authorization"].ToString();
            var token = rawAuth?.Replace("Bearer ", "");

            var res = await _habitService.GetHabits(authUserId.Value, token, ct, includeArchived);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpPost("post-habit")]
        public async Task<IActionResult> PostHabit(HabitDTO habitDto)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();

            habitDto.UserId = authUserId.Value;

            var res = await _habitService.PostHabit(habitDto);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpPut("update-habit/{habitId}")]
        public async Task<IActionResult> UpdateHabit(int habitId, [FromBody] HabitDTO habitDto)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();

            habitDto.UserId = authUserId.Value;

            var res = await _habitService.UpdateHabit(habitId, habitDto);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpDelete("delete-habit/{habitId}")]
        public async Task<IActionResult> DeleteHabit(int habitId)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();

            var res = await _habitService.DeleteHabit(habitId);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpGet("habits-summary/{userId}")]
        public async Task<IActionResult> GetHabitsSummary(int userId)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();

            var res = await _habitService.HabitSummary(authUserId.Value);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpGet("search")]
        public async Task<IActionResult> Search(
            CancellationToken ct,
            [FromQuery] string? search = null,
            [FromQuery] int? tagId = null,
            [FromQuery] bool includeArchived = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();

            var res = await _habitService.SearchAsync(authUserId.Value, search, tagId, includeArchived, page, pageSize, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpPost("archive/{habitId}")]
        public async Task<IActionResult> Archive(int habitId, CancellationToken ct)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();

            var res = await _habitService.SetArchivedAsync(habitId, authUserId.Value, true, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [Authorize]
        [HttpPost("restore/{habitId}")]
        public async Task<IActionResult> Restore(int habitId, CancellationToken ct)
        {
            var authUserId = User.GetUserId();
            if (authUserId is null) return Unauthorized();

            var res = await _habitService.SetArchivedAsync(habitId, authUserId.Value, false, ct);
            return StatusCode((int)res.StatusCode, res);
        }

    }
}
