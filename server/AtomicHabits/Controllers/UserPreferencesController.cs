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
    public class UserPreferencesController : ControllerBase
    {
        private readonly IUserPreferencesService _service;

        public UserPreferencesController(IUserPreferencesService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var res = await _service.GetAsync(userId.Value, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPut]
        public async Task<IActionResult> Upsert([FromBody] UserPreferencesDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var res = await _service.UpsertAsync(userId.Value, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
