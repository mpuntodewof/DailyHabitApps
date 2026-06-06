using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Repositories
{
    public interface IStreakRepositories
    {
        Task<Streak> UpsertStreakAfterTracking(HabitTrackingDTO dto);
    }

    public class StreakRepositories : IStreakRepositories
    {
        private readonly AppDbContext _db;
        private readonly ILogger<StreakRepositories> _logger;

        public StreakRepositories(AppDbContext db, ILogger<StreakRepositories> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Streak> UpsertStreakAfterTracking(HabitTrackingDTO dto)
        {
            try
            {
                var streak = await _db.Streaks
                    .FirstOrDefaultAsync(s => s.HabitId == dto.HabitId && s.UserId == dto.UserId);

                var isCompleted = dto.IsCompleted;
                var date = dto.TrackingDate.Date;

                if (streak == null)
                {
                    streak = new Streak
                    {
                        HabitId = dto.HabitId,
                        UserId = dto.UserId,
                        CurrentStreak = isCompleted ? 1 : 0,
                        CurrentStreakStartDate = isCompleted ? date : null,
                        BestStreak = isCompleted ? 1 : 0,
                        BestStreakStartDate = isCompleted ? date : null,
                        CompletionRate = isCompleted ? 1f : 0f
                    };

                    await _db.Streaks.AddAsync(streak);
                }
                else
                {
                    if (isCompleted)
                    {
                        var lastTracking = await _db.HabitTrackings
                            .Where(ht => ht.HabitId == dto.HabitId &&
                                         ht.UserId == dto.UserId &&
                                         ht.IsCompleted)
                            .OrderByDescending(ht => ht.TrackingDate)
                            .FirstOrDefaultAsync();

                        if (lastTracking != null &&
                            lastTracking.TrackingDate!.Value.Date == date.AddDays(-1))
                        {
                            streak.CurrentStreak += 1;
                        }
                        else
                        {
                            streak.CurrentStreak = 1;
                            streak.CurrentStreakStartDate = date;
                        }

                        if (streak.CurrentStreak > streak.BestStreak)
                        {
                            streak.BestStreak = streak.CurrentStreak;
                            streak.BestStreakStartDate = streak.CurrentStreakStartDate;
                            streak.BestStreakEndDate = date;
                        }
                    }
                    else
                    {
                        streak.CurrentStreak = 0;
                        streak.CurrentStreakStartDate = null;
                    }

                    var total = await _db.HabitTrackings.CountAsync(ht =>
                        ht.HabitId == dto.HabitId && ht.UserId == dto.UserId);

                    var completed = await _db.HabitTrackings.CountAsync(ht =>
                        ht.HabitId == dto.HabitId &&
                        ht.UserId == dto.UserId &&
                        ht.IsCompleted);

                    streak.CompletionRate = total > 0 ? (float)completed / total : 0f;

                    _db.Streaks.Update(streak);
                }

                await _db.SaveChangesAsync();
                return streak;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UpsertStreakAfterTracking] Error");
                throw;
            }
        }
    }

}
