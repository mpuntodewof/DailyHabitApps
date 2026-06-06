using AtomicHabits.Data;
using AtomicHabits.Models;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Repositories
{
    public interface IDashboardRepositories
    {
        Task<List<Habit>> GetActiveHabits(int userId);
        Task<List<HabitTracking>> GetTrackings(int userId);
        Task<List<HabitTracking>> GetCompletedTrackingByUser(int userId, DateTime startDate, DateTime endDate, CancellationToken ct);
        Task<Dictionary<DateTime, int>> GetDailyCompletionCounts(int userId, DateTime startDate, DateTime endDate, CancellationToken ct);
    }

    public class DashboardRepositories : IDashboardRepositories
    {
        private readonly AppDbContext _db;
        private readonly ILogger<DashboardRepositories> _logger;

        public DashboardRepositories(AppDbContext db, ILogger<DashboardRepositories> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<Habit>> GetActiveHabits(int userId)
        {
            try
            {
                return await _db.Habits
                    .Where(h => h.UserId == userId)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[GetActiveHabits] Error");
                throw;
            }           
        }

        public async Task<List<HabitTracking>> GetTrackings(int userId)
        {
            try
            {
                return await _db.HabitTrackings
                   .Where(t => t.Habit.UserId == userId)
                   .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[GetTrackings] Error");
                throw;
            }
        }

        public async Task<List<HabitTracking>> GetCompletedTrackingByUser(int userId, DateTime startDate, DateTime endDate, CancellationToken ct)
        {
            try
            {
                return await _db.HabitTrackings
                    .Where(t => t.UserId == userId &&
                        t.IsCompleted &&
                        t.TrackingDate >= startDate &&
                        t.TrackingDate <= endDate)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[GetCompletedTrackingByUser] Error");
                throw;
            }
        }

        public async Task<Dictionary<DateTime, int>> GetDailyCompletionCounts(int userId, DateTime startDate, DateTime endDate, CancellationToken ct)
        {
            try
            {
                var rows = await _db.HabitTrackings
                    .Where(t => t.UserId == userId
                        && t.IsCompleted
                        && t.TrackingDate.HasValue
                        && t.TrackingDate.Value >= startDate
                        && t.TrackingDate.Value <= endDate)
                    .GroupBy(t => t.TrackingDate!.Value.Date)
                    .Select(g => new { Date = g.Key, Count = g.Count() })
                    .ToListAsync(ct);

                return rows.ToDictionary(r => r.Date, r => r.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[GetDailyCompletionCounts] Error");
                throw;
            }
        }
    }
}
