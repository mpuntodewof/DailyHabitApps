using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IUserPreferencesService
    {
        Task<ApiResponse> GetAsync(int userId, CancellationToken ct);
        Task<ApiResponse> UpsertAsync(int userId, UserPreferencesDto dto, CancellationToken ct);
    }

    public class UserPreferencesService : IUserPreferencesService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<UserPreferencesService> _logger;

        public UserPreferencesService(AppDbContext db, ILogger<UserPreferencesService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse> GetAsync(int userId, CancellationToken ct)
        {
            var prefs = await _db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            var dto = prefs == null ? new UserPreferencesDto() : ToDto(prefs);

            return new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = dto
            };
        }

        public async Task<ApiResponse> UpsertAsync(int userId, UserPreferencesDto dto, CancellationToken ct)
        {
            var prefs = await _db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            var now = DateTime.UtcNow;

            if (prefs == null)
            {
                prefs = new UserPreferences { UserId = userId, CreatedAt = now };
                ApplyDto(prefs, dto);
                _db.UserPreferences.Add(prefs);
            }
            else
            {
                ApplyDto(prefs, dto);
                prefs.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(ct);

            return new ApiResponse
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = ToDto(prefs)
            };
        }

        private static UserPreferencesDto ToDto(UserPreferences p) => new()
        {
            Notifications = p.Notifications,
            DarkMode = p.DarkMode,
            EmailUpdates = p.EmailUpdates,
            DeviceSync = p.DeviceSync,
            PrimaryColor = p.PrimaryColor,
            FontFamily = p.FontFamily,
            BorderRadius = p.BorderRadius,
            Spacing = p.Spacing
        };

        private static void ApplyDto(UserPreferences p, UserPreferencesDto dto)
        {
            p.Notifications = dto.Notifications;
            p.DarkMode = dto.DarkMode;
            p.EmailUpdates = dto.EmailUpdates;
            p.DeviceSync = dto.DeviceSync;
            p.PrimaryColor = string.IsNullOrWhiteSpace(dto.PrimaryColor) ? "#2196f3" : dto.PrimaryColor;
            p.FontFamily = string.IsNullOrWhiteSpace(dto.FontFamily) ? "Inter" : dto.FontFamily;
            p.BorderRadius = dto.BorderRadius;
            p.Spacing = dto.Spacing;
        }
    }
}
