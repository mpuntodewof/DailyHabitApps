using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IHabitReminderService
    {
        Task<ApiResponse> ListAsync(int userId, CancellationToken ct);
        Task<ApiResponse> CreateAsync(int userId, HabitReminderDTO dto, CancellationToken ct);
        Task<ApiResponse> UpdateAsync(int userId, int reminderId, HabitReminderDTO dto, CancellationToken ct);
        Task<ApiResponse> DeleteAsync(int userId, int reminderId, CancellationToken ct);
    }

    public class HabitReminderService : IHabitReminderService
    {
        private readonly IHabitReminderRepositories _repo;
        private readonly ILogger<HabitReminderService> _logger;

        public HabitReminderService(IHabitReminderRepositories repo, ILogger<HabitReminderService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse> ListAsync(int userId, CancellationToken ct)
        {
            var items = await _repo.GetForUserAsync(userId, ct);
            return new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = items.Select(ToDto).ToList()
            };
        }

        public async Task<ApiResponse> CreateAsync(int userId, HabitReminderDTO dto, CancellationToken ct)
        {
            if (!await _repo.HabitBelongsToUserAsync(dto.HabitId, userId, ct))
            {
                return NotFound("Habit not found or doesn't belong to user");
            }

            var reminder = new HabitReminder
            {
                HabitId = dto.HabitId,
                ReminderTime = dto.ReminderTime,
                DaysOfWeek = dto.DaysOfWeek ?? string.Empty,
                IsEnabled = dto.IsEnabled
            };

            var created = await _repo.CreateAsync(reminder, ct);
            return new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.Created,
                Result = ToDto(created)
            };
        }

        public async Task<ApiResponse> UpdateAsync(int userId, int reminderId, HabitReminderDTO dto, CancellationToken ct)
        {
            var existing = await _repo.GetByIdForUserAsync(reminderId, userId, ct);
            if (existing == null) return NotFound("Reminder not found");

            existing.ReminderTime = dto.ReminderTime;
            existing.DaysOfWeek = dto.DaysOfWeek ?? existing.DaysOfWeek;
            existing.IsEnabled = dto.IsEnabled;

            await _repo.UpdateAsync(existing, ct);

            return new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = ToDto(existing)
            };
        }

        public async Task<ApiResponse> DeleteAsync(int userId, int reminderId, CancellationToken ct)
        {
            var ok = await _repo.DeleteAsync(reminderId, userId, ct);
            if (!ok) return NotFound("Reminder not found");

            return new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = new { reminderId }
            };
        }

        private static object ToDto(HabitReminder r) => new
        {
            id = r.Id,
            habitId = r.HabitId,
            reminderTime = r.ReminderTime.ToString(@"hh\:mm"),
            daysOfWeek = r.DaysOfWeek,
            isEnabled = r.IsEnabled,
            lastFiredOn = r.LastFiredOn?.ToString("yyyy-MM-dd")
        };

        private static ApiResponse NotFound(string message) => new()
        {
            IsSuccess = false,
            StatusCode = HttpStatusCode.NotFound,
            ErrorMessages = new List<string> { message }
        };
    }
}
