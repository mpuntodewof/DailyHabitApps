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
    public class TagController : ControllerBase
    {
        private readonly ITagService _service;

        public TagController(ITagService service)
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
        public async Task<IActionResult> Create([FromBody] TagUpsertDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.CreateAsync(userId.Value, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPut("{tagId}")]
        public async Task<IActionResult> Update(int tagId, [FromBody] TagUpsertDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.UpdateAsync(userId.Value, tagId, dto, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpDelete("{tagId}")]
        public async Task<IActionResult> Delete(int tagId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.DeleteAsync(userId.Value, tagId, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpGet("habit/{habitId}")]
        public async Task<IActionResult> ListHabitTags(int habitId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.ListHabitTagsAsync(userId.Value, habitId, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpPost("habit/{habitId}/{tagId}")]
        public async Task<IActionResult> Attach(int habitId, int tagId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.AttachAsync(userId.Value, habitId, tagId, ct);
            return StatusCode((int)res.StatusCode, res);
        }

        [HttpDelete("habit/{habitId}/{tagId}")]
        public async Task<IActionResult> Detach(int habitId, int tagId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            var res = await _service.DetachAsync(userId.Value, habitId, tagId, ct);
            return StatusCode((int)res.StatusCode, res);
        }
    }
}
