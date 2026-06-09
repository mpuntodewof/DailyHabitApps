using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IHabitSkipService
    {
        Task<ApiResponse> CreateAsync(int userId, HabitSkipCreateDto dto, CancellationToken ct);
        Task<ApiResponse> ListForHabitAsync(int userId, int habitId, CancellationToken ct);
        Task<ApiResponse> DeleteAsync(int userId, int skipId, CancellationToken ct);
    }

    public class HabitSkipService : IHabitSkipService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<HabitSkipService> _logger;

        public HabitSkipService(AppDbContext db, ILogger<HabitSkipService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse> CreateAsync(int userId, HabitSkipCreateDto dto, CancellationToken ct)
        {
            if (!await _db.Habits.AnyAsync(h => h.Id == dto.HabitId && h.UserId == userId, ct))
                return NotFound("Habit not found");

            if (!Enum.TryParse<SkipReason>(dto.Reason, true, out var reason))
                return BadRequest("Invalid reason");

            var date = dto.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);

            // One skip per habit per day — upsert the reason.
            var existing = await _db.HabitSkips
                .FirstOrDefaultAsync(s => s.UserId == userId && s.HabitId == dto.HabitId && s.Date == date, ct);

            if (existing != null)
            {
                existing.Reason = reason;
                await _db.SaveChangesAsync(ct);
                return Ok(ToDto(existing));
            }

            var skip = new HabitSkip { UserId = userId, HabitId = dto.HabitId, Date = date, Reason = reason };
            _db.HabitSkips.Add(skip);
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(skip), HttpStatusCode.Created);
        }

        public async Task<ApiResponse> ListForHabitAsync(int userId, int habitId, CancellationToken ct)
        {
            if (!await _db.Habits.AnyAsync(h => h.Id == habitId && h.UserId == userId, ct))
                return NotFound("Habit not found");

            var items = await _db.HabitSkips
                .Where(s => s.HabitId == habitId && s.UserId == userId)
                .OrderByDescending(s => s.Date)
                .Select(s => new HabitSkipDto { Id = s.Id, HabitId = s.HabitId, Date = s.Date, Reason = s.Reason.ToString() })
                .ToListAsync(ct);
            return Ok(items);
        }

        public async Task<ApiResponse> DeleteAsync(int userId, int skipId, CancellationToken ct)
        {
            var skip = await _db.HabitSkips.FirstOrDefaultAsync(s => s.Id == skipId && s.UserId == userId, ct);
            if (skip == null) return NotFound("Skip not found");
            _db.HabitSkips.Remove(skip);
            await _db.SaveChangesAsync(ct);
            return Ok(new { skipId });
        }

        private static HabitSkipDto ToDto(HabitSkip s) => new()
        {
            Id = s.Id, HabitId = s.HabitId, Date = s.Date, Reason = s.Reason.ToString()
        };

        private static ApiResponse Ok(object result, HttpStatusCode status = HttpStatusCode.OK) =>
            new() { IsSuccess = true, StatusCode = status, Result = result };
        private static ApiResponse NotFound(string message) => Error(HttpStatusCode.NotFound, message);
        private static ApiResponse BadRequest(string message) => Error(HttpStatusCode.BadRequest, message);
        private static ApiResponse Error(HttpStatusCode status, string message) =>
            new() { IsSuccess = false, StatusCode = status, ErrorMessages = new List<string> { message } };
    }
}
