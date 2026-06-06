using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Repositories
{
    public interface IHabitRepositories
    {
        Task<Habit?> GetHabitById(int habitId);
        Task<Habit?> GetHabitbyUserHabitId(int userId, int habitId);
        Task<List<Habit>> GetHabitByUserId(int userId, bool includeArchived = false);
        Task<(List<Habit> Items, int Total)> SearchAsync(int userId, string? search, int? tagId, bool includeArchived, int page, int pageSize, CancellationToken ct);
        Task<bool> PostHabit(HabitDTO habitDto);
        Task<bool> UpdateHabit(int habitId, HabitDTO habitDto);
        Task<bool> DeleteHabit(int habitId);
        Task<bool> SetArchivedAsync(int habitId, int userId, bool archived, CancellationToken ct);

        Task<List<Habit>> GetActiveHabits(int userId, CancellationToken ct);
        Task<List<HabitTracking>> GetTodayTrackings(List<int> habitIds, DateTime today, CancellationToken ct);
        Task<List<HabitTracking>> GetWeeklyTrackings(List<int> habitIds, DateTime startOfWeek, CancellationToken ct);
        Task<List<HabitTracking>> GetMonthlyTrackings(List<int> habitIds, DateTime startOfMonth, CancellationToken ct);
    }

    public class HabitRepositories : IHabitRepositories
    {
        private readonly AppDbContext _db;
        private readonly ILogger<HabitTrackingRepositories> _logger;
        public HabitRepositories(AppDbContext db, ILogger<HabitTrackingRepositories> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Habit?> GetHabitById(int habitId)
        {
            try
            {
                return await _db.Habits.FindAsync(habitId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GetHabitById] Error");
                throw;
            }
        }

        public async Task<Habit?> GetHabitbyUserHabitId(int userId, int habitId)
        {
            try
            {
                return await _db.Habits
                    .Where(h => h.Id == habitId && h.UserId == userId)
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GetHabitByUserId] Error");
                throw;
            }
        }

        public async Task<List<Habit>> GetHabitByUserId(int userId, bool includeArchived = false)
        {
            try
            {
                var query = _db.Habits
                    .Include(h => h.HabitTags)
                        .ThenInclude(ht => ht.Tag)
                    .Where(h => h.UserId == userId);
                if (!includeArchived) query = query.Where(h => !h.IsArchived);

                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GetHabitByUserId] Error");
                throw;
            }
        }

        public async Task<(List<Habit> Items, int Total)> SearchAsync(int userId, string? search, int? tagId, bool includeArchived, int page, int pageSize, CancellationToken ct)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 20;
                if (pageSize > 100) pageSize = 100;

                var query = _db.Habits.AsQueryable().Where(h => h.UserId == userId);
                if (!includeArchived) query = query.Where(h => !h.IsArchived);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var s = search.Trim();
                    query = query.Where(h => EF.Functions.Like(h.Name, $"%{s}%")
                        || (h.Description != null && EF.Functions.Like(h.Description, $"%{s}%")));
                }

                if (tagId.HasValue)
                {
                    var tid = tagId.Value;
                    query = query.Where(h => h.HabitTags.Any(ht => ht.TagId == tid));
                }

                var total = await query.CountAsync(ct);

                var items = await query
                    .Include(h => h.HabitTags)
                        .ThenInclude(ht => ht.Tag)
                    .OrderByDescending(h => h.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(ct);

                return (items, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SearchAsync] Error");
                throw;
            }
        }

        public async Task<bool> SetArchivedAsync(int habitId, int userId, bool archived, CancellationToken ct)
        {
            try
            {
                var habit = await _db.Habits
                    .FirstOrDefaultAsync(h => h.Id == habitId && h.UserId == userId, ct);
                if (habit == null) return false;

                habit.IsArchived = archived;
                habit.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SetArchivedAsync] Error");
                throw;
            }
        }

        public async Task<bool> PostHabit(HabitDTO habitDto)
        {
            try
            {
                var habitRequest = new Habit
                {
                    UserId = habitDto.UserId,
                    Name = habitDto.Name,
                    Color = habitDto.Color,
                    Description = habitDto.Description,
                    Frequency = habitDto.Frequency,
                    GoalValue = habitDto.GoalValue,
                    GoalUnit = habitDto.GoalUnit,
                    GoalFrequency = habitDto.GoalFrequency,
                    CreatedAt = habitDto.CreatedAt,
                };

                await _db.Habits.AddAsync(habitRequest);
                await _db.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[GetHabitByUserId] Error");
                throw;
            }
        }

        public async Task<bool> UpdateHabit(int habitId, HabitDTO habitDto)
        {
            try
            {
                var habit = await _db.Habits.FindAsync(habitId);
                if (habit == null)
                {
                    _logger.LogError($"[UpdateHabit] Error, habit with id: {habitId} not found");
                    return false;
                }

                habit.Name = habitDto.Name;
                habit.Color = habitDto.Color;
                habit.Description = habitDto.Description;
                habit.Frequency = habitDto.Frequency;
                habit.GoalValue = habitDto.GoalValue;
                habit.GoalUnit = habitDto.GoalUnit;
                habit.GoalFrequency = habitDto.GoalFrequency;
                habit.UpdatedAt = habitDto.UpdatedAt;

                _db.Habits.Update(habit);
                await _db.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[UpdateHabit] Error");
                throw;
            }
        }

        public async Task<bool> DeleteHabit(int habitId)
        {
            try
            {
                var habit = await _db.Habits.FindAsync(habitId);
                if (habit == null)
                {
                    _logger.LogError($"Habit with id: {habitId} not found");
                    return false;
                }

                _db.Habits.Remove(habit);
                await _db.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[DeleteHabit] Error");
                throw;
            }
        }

        public async Task<List<Habit>> GetActiveHabits(int userId, CancellationToken ct)
        {
            try
            {
                return await _db.Habits
                      .Where(h => h.UserId == userId && !h.IsArchived)
                      .ToListAsync(ct);
            }
            catch (Exception ex)    
            {                 
                _logger.LogError(ex.Message, "[GetActiveHabits] Error");
                throw;
            }          
        }

        public async Task<List<HabitTracking>> GetTodayTrackings(List<int> habitIds, DateTime today, CancellationToken ct)
        {
            try
            {
                return await _db.HabitTrackings
                    .Where(t => habitIds.Contains(t.HabitId) && t.TrackingDate == today)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[GetTodayTrackings] Error");
                throw;
            }           
        }

        public async Task<List<HabitTracking>> GetWeeklyTrackings(List<int> habitIds, DateTime startOfWeek, CancellationToken ct)
        {
            try
            {
                return await _db.HabitTrackings
                    .Where(t => habitIds.Contains(t.HabitId) && t.TrackingDate >= startOfWeek)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[GetWeeklyTrackings] Error");
                throw;
            }          
        }

        public async Task<List<HabitTracking>> GetMonthlyTrackings(List<int> habitIds, DateTime startOfMonth, CancellationToken ct)
        {
            try
            {
                return await _db.HabitTrackings
                    .Where(t => habitIds.Contains(t.HabitId) && t.TrackingDate >= startOfMonth)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[GetMonthlyTrackings] Error");
                throw;
            }            
        }
    }
}
