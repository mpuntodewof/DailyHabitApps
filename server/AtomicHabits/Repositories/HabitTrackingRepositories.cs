using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Repositories
{
    public interface IHabitTrackingRepositories
    {
       
        Task<HabitTracking?> GetExistingDate(HabitTrackingDTO dto);
        Task<HabitTracking> CreateTracking(HabitTrackingDTO dto);
        Task<List<string>> GetHabitTrackingsDate(int habitId, int userId);
        Task<List<DateTime>> GetCompletedTrackings(int habitId, int userId);
        Task<HabitTracking> GetHabitTrackingDateByHabitId(int habitId, DateOnly trackingDate);
        Task<List<DateTime?>> GetCompletionsAsync(int habitId, int userId, DateTime startDate, DateTime endDate);

        Task<int[]> GetWeeklyDistribution(WeeklyDistributionDTO request, CancellationToken ct);
        Task<int[]> GetMonthlyDistribution(MonthlyDistributionDTO request, CancellationToken ct);
        Task<int[]> GetYearlyDistribution(YearlyDistributionDTO request, CancellationToken ct);

    }
    public class HabitTrackingRepositories : IHabitTrackingRepositories
    {
        private readonly AppDbContext _db;
        private readonly ILogger<HabitTrackingRepositories> _logger;

        public HabitTrackingRepositories(AppDbContext db, ILogger<HabitTrackingRepositories> logger)
        {
            _db = db;
            _logger = logger;
        }      

        public async Task<HabitTracking?> GetExistingDate(HabitTrackingDTO dto)
        {
            try
            {
                return await _db.HabitTrackings.FirstOrDefaultAsync(ht =>
                    ht.HabitId == dto.HabitId &&
                    ht.UserId == dto.UserId &&
                    ht.TrackingDate!.Value.Date == dto.TrackingDate.Date
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GetExistingDate] Error");
                throw;
            }
        }

        public async Task<HabitTracking> CreateTracking(HabitTrackingDTO dto)
        {
            try
            {
                var tracking = new HabitTracking
                {
                    HabitId = dto.HabitId,
                    TrackingDate = dto.TrackingDate,
                    IsCompleted = dto.IsCompleted,
                    UserId = dto.UserId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    TimeSpentMinutes = dto.TimeSpentMinutes,
                };

                await _db.HabitTrackings.AddAsync(tracking);
                await _db.SaveChangesAsync();

                return tracking;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CreateTracking] Error inserting habit tracking");
                throw;
            }
        }

        public async Task<List<string>> GetHabitTrackingsDate(int habitId, int userId)
        {
            try
            {
                return await _db.HabitTrackings
                    .Where(h => h.HabitId == habitId && h.UserId == userId)
                    .Select(h => h.TrackingDate!.Value.Date.ToString("yyyy-MM-dd"))
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GetHabitTrackingsDate] Error");
                throw;
            }
        }

        public async Task<List<DateTime>> GetCompletedTrackings(int habitId, int userId)
        {
            try
            {
                return await _db.HabitTrackings.Where(ht => ht.HabitId == habitId && ht.UserId == userId && ht.IsCompleted).Select(ht => ht.TrackingDate!.Value.Date).Distinct().ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GetCompletedTrackings] Error");
                throw;
            }
        }

        public async Task<HabitTracking> GetHabitTrackingDateByHabitId(int habitId, DateOnly trackingDate)
        {            
            try
            {
                var today = DateTime.UtcNow.Date;

                return await _db.HabitTrackings.Where(ht => ht.HabitId == habitId && ht.TrackingDate!.Value.Date == today).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GeatHabitTrackingDateByHabitId] Error");
                throw;
            }
        }

        public async Task<List<DateTime?>> GetCompletionsAsync(int habitId, int userId, DateTime startDate, DateTime endDate)
        {
            try
            {
                return await _db.HabitTrackings
                    .Where(x =>
                        x.HabitId == habitId &&
                        x.UserId == userId &&
                        x.CreatedAt >= startDate &&
                        x.CreatedAt <= endDate
                    )
                    .Select(x => x.CreatedAt)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[GetCompletionsAsync] Error");
                throw;
            }
        }


        public async Task<int[]> GetWeeklyDistribution(WeeklyDistributionDTO request, CancellationToken ct)
        {
            try
            {
                var start = new DateTime(request.Year, request.Month, 1);
                var end = start.AddMonths(1);

                var data = await _db.HabitTrackings
                    .Where(x => x.HabitId == request.HabitId && x.TrackingDate >= start && x.TrackingDate <= end)
                    .GroupBy(x => x.TrackingDate!.Value.Day <= 7 ? 0 : x.TrackingDate.Value.Day <= 14 ? 1 : x.TrackingDate.Value.Day <= 21 ? 2 : 3)
                    .Select(g => new { Week = g.Key, Count = g.Count() })
                    .ToListAsync(ct);

                var result = new int[4];
                foreach (var d in data)
                {
                    result[d.Week] = d.Count;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[GetWeeklyDistribution] Error");
                throw;
            }
        }

        public async Task<int[]> GetMonthlyDistribution(MonthlyDistributionDTO request, CancellationToken ct)
        {
            try
            {
                var data = await _db.HabitTrackings
                    .Where(x => x.HabitId == request.HabitId && x.TrackingDate!.Value.Year == request.Year)
                    .GroupBy(x => x.TrackingDate!.Value.Month)
                    .Select(g => new { Month = g.Key, Count = g.Count() })
                    .ToListAsync(ct);

                var result = new int[12];

                foreach(var d in data)
                {
                    if (d.Month < 1 || d.Month > 12)
                    {
                        _logger.LogWarning("Invalid month value: {Month}", d.Month);
                        continue;
                    }

                    result[d.Month - 1] = d.Count;
                }

                return result;                            
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[GetMonthlyDistribution] Error");
                throw;
            }
        }

        public async Task<int[]> GetYearlyDistribution(YearlyDistributionDTO request, CancellationToken ct)
        {
            try
            {
                var data = await _db.HabitTrackings
                    .Where(x => x.HabitId == request.HabitId && x.CreatedAt!.Value.Year >= request.StartYear && x.CreatedAt.Value.Year <= request.EndYear)
                    .GroupBy(x => x.CreatedAt!.Value.Year)
                    .Select(g => new { Year = g.Key, Count = g.Count() })
                    .ToListAsync(ct);

                var length = request.EndYear - request.StartYear + 1;
                var result = new int[length];

                foreach (var d in data)
                    result[d.Year - request.StartYear] = d.Count;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "[GetYearlyDistribution] Error");
                throw;
            }
        }

    }
}
