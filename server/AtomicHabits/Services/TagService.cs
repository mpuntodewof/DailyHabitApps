using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AtomicHabits.Services
{
    public interface ITagService
    {
        Task<ApiResponse> ListAsync(int userId, CancellationToken ct);
        Task<ApiResponse> CreateAsync(int userId, TagUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> UpdateAsync(int userId, int tagId, TagUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> DeleteAsync(int userId, int tagId, CancellationToken ct);

        Task<ApiResponse> AttachAsync(int userId, int habitId, int tagId, CancellationToken ct);
        Task<ApiResponse> DetachAsync(int userId, int habitId, int tagId, CancellationToken ct);
        Task<ApiResponse> ListHabitTagsAsync(int userId, int habitId, CancellationToken ct);
    }

    public class TagService : ITagService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<TagService> _logger;

        public TagService(AppDbContext db, ILogger<TagService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse> ListAsync(int userId, CancellationToken ct)
        {
            var tags = await _db.Tags
                .Where(t => t.UserId == userId)
                .OrderBy(t => t.Name)
                .Select(t => new TagDto { Id = t.Id, Name = t.Name, Color = t.Color })
                .ToListAsync(ct);

            return Ok(tags);
        }

        public async Task<ApiResponse> CreateAsync(int userId, TagUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Tag name is required");

            var name = dto.Name.Trim();
            if (await _db.Tags.AnyAsync(t => t.UserId == userId && t.Name == name, ct))
            {
                return Error(HttpStatusCode.Conflict, "Tag with this name already exists");
            }

            var tag = new Tag { UserId = userId, Name = name, Color = dto.Color };
            _db.Tags.Add(tag);
            await _db.SaveChangesAsync(ct);

            return Ok(new TagDto { Id = tag.Id, Name = tag.Name, Color = tag.Color }, HttpStatusCode.Created);
        }

        public async Task<ApiResponse> UpdateAsync(int userId, int tagId, TagUpsertDto dto, CancellationToken ct)
        {
            var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Id == tagId && t.UserId == userId, ct);
            if (tag == null) return NotFound("Tag not found");

            if (!string.IsNullOrWhiteSpace(dto.Name)) tag.Name = dto.Name.Trim();
            tag.Color = dto.Color;
            await _db.SaveChangesAsync(ct);

            return Ok(new TagDto { Id = tag.Id, Name = tag.Name, Color = tag.Color });
        }

        public async Task<ApiResponse> DeleteAsync(int userId, int tagId, CancellationToken ct)
        {
            var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Id == tagId && t.UserId == userId, ct);
            if (tag == null) return NotFound("Tag not found");

            // FK_HabitTags_Tags_TagId is NoAction (avoids SQL Server's cascade-cycle rule),
            // so we remove the join rows manually before deleting the tag.
            var links = await _db.HabitTags.Where(ht => ht.TagId == tagId).ToListAsync(ct);
            if (links.Count > 0) _db.HabitTags.RemoveRange(links);

            _db.Tags.Remove(tag);
            await _db.SaveChangesAsync(ct);
            return Ok(new { tagId });
        }

        public async Task<ApiResponse> AttachAsync(int userId, int habitId, int tagId, CancellationToken ct)
        {
            if (!await OwnsHabit(userId, habitId, ct)) return NotFound("Habit not found");
            if (!await OwnsTag(userId, tagId, ct)) return NotFound("Tag not found");

            var exists = await _db.HabitTags.AnyAsync(ht => ht.HabitId == habitId && ht.TagId == tagId, ct);
            if (!exists)
            {
                _db.HabitTags.Add(new HabitTag { HabitId = habitId, TagId = tagId });
                await _db.SaveChangesAsync(ct);
            }
            return Ok(new { habitId, tagId, attached = true });
        }

        public async Task<ApiResponse> DetachAsync(int userId, int habitId, int tagId, CancellationToken ct)
        {
            if (!await OwnsHabit(userId, habitId, ct)) return NotFound("Habit not found");

            var link = await _db.HabitTags.FirstOrDefaultAsync(ht => ht.HabitId == habitId && ht.TagId == tagId, ct);
            if (link != null)
            {
                _db.HabitTags.Remove(link);
                await _db.SaveChangesAsync(ct);
            }
            return Ok(new { habitId, tagId, attached = false });
        }

        public async Task<ApiResponse> ListHabitTagsAsync(int userId, int habitId, CancellationToken ct)
        {
            if (!await OwnsHabit(userId, habitId, ct)) return NotFound("Habit not found");

            var tags = await _db.HabitTags
                .Where(ht => ht.HabitId == habitId)
                .Select(ht => new TagDto { Id = ht.Tag.Id, Name = ht.Tag.Name, Color = ht.Tag.Color })
                .OrderBy(t => t.Name)
                .ToListAsync(ct);

            return Ok(tags);
        }

        private Task<bool> OwnsHabit(int userId, int habitId, CancellationToken ct) =>
            _db.Habits.AnyAsync(h => h.Id == habitId && h.UserId == userId, ct);

        private Task<bool> OwnsTag(int userId, int tagId, CancellationToken ct) =>
            _db.Tags.AnyAsync(t => t.Id == tagId && t.UserId == userId, ct);

        private static ApiResponse Ok(object result, HttpStatusCode status = HttpStatusCode.OK) => new()
        {
            IsSuccess = true,
            StatusCode = status,
            Result = result
        };

        private static ApiResponse NotFound(string message) => Error(HttpStatusCode.NotFound, message);
        private static ApiResponse BadRequest(string message) => Error(HttpStatusCode.BadRequest, message);
        private static ApiResponse Error(HttpStatusCode status, string message) => new()
        {
            IsSuccess = false,
            StatusCode = status,
            ErrorMessages = new List<string> { message }
        };
    }
}
