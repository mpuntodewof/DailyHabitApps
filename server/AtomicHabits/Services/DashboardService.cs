using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using AtomicHabits.Utils;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IDashboardService
    {
        Task<ApiResponse> GetCardOverviews(int userId, CancellationToken ct);
        Task<ApiResponse> GetHeatmap(int userId, int days, CancellationToken ct);
    }

    public class DashboardService : IDashboardService
    {
        private readonly IDashboardRepositories _repo;
        private ApiResponse _response;
        public DashboardService(IDashboardRepositories repo)
        {
            _repo = repo;
            _response = new ApiResponse();
        }

        public async Task<ApiResponse> GetCardOverviews(int userId, CancellationToken ct)
        {
            try
            {
                var today = DateTime.UtcNow.Date;
                var startDate = DateTime.UtcNow.AddMonths(-5).Date;
                var endDate = DateTime.UtcNow.Date;

                var habitsData = await _repo.GetActiveHabits(userId);
                var getTrackings = await _repo.GetTrackings(userId);

                if (habitsData == null || getTrackings == null)
                {
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.NotFound;
                    _response.ErrorMessages = new List<string> { "No habits or trackings found." };
                    return _response;
                }

                var todayCompleted = getTrackings.Count(t => t.TrackingDate!.Value.Date == today && t.IsCompleted);
                var todayCard = new TodayCardDto
                {
                    Completed = todayCompleted,
                    Total = habitsData.Count
                };

                #region Streak Calculation

                var completedDates = getTrackings
                    .Where(t => t.IsCompleted && t.TrackingDate.HasValue)
                    .Select(t => t.TrackingDate!.Value.Date);

                var streak = StreakCalculator.Compute(completedDates, today);

                var streakCard = new StreakCardDto
                {
                    CurrentStreak = streak.CurrentStreak,
                    LongestStreak = streak.LongestStreak
                };

                #endregion

                #region Weekly Card Calculation

                var startOfWeek = today.AddDays(-(int)today.DayOfWeek + 1);
                var weeklyCompleted = getTrackings.Count(t => t.IsCompleted && t.TrackingDate!.Value.Date >= startOfWeek);
                var weeklyTotal = habitsData.Count * ((today - startOfWeek).Days + 1);

                var weeklyCard = new WeeklyCardDto
                {
                    CompletionRate = weeklyTotal == 0 ? 0 : weeklyCompleted * 100 / weeklyTotal
                };

                #endregion

                #region Monthly Trend Calculation

                var getCompletedHabit = await _repo.GetCompletedTrackingByUser(userId, startDate, endDate, ct);

                var monthlyTrends = getCompletedHabit
                    .GroupBy(t => new { t.TrackingDate!.Value.Year, t.TrackingDate!.Value.Month })
                    .Select(g =>
                    {
                        int daysInMonth = DateTime.DaysInMonth(g.Key.Year, g.Key.Month);
                        int totalPossibleSessions = habitsData.Count() * daysInMonth;

                        int completionRate = totalPossibleSessions == 0 ? 0 : (int)Math.Round((double)g.Count() / totalPossibleSessions * 100);

                        return new MonthlyTrendDto
                        {
                            Month = $"{g.Key.Month:D2}/{g.Key.Year}",
                            CompletionRate = completionRate
                        };
                    })
                    .OrderBy(x => x.Month)
                    .ToList();

                #endregion

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                _response.Result = new
                {
                    TodayCard = todayCard,
                    StreakCard = streakCard,
                    WeeklyCard = weeklyCard,
                    MonthlyTrend = monthlyTrends
                };

                return _response;
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages = new List<string> { "Post habit progress error, message: " + ex.Message };
                return _response;
            }
        }

        public async Task<ApiResponse> GetHeatmap(int userId, int days, CancellationToken ct)
        {
            var response = new ApiResponse();

            try
            {
                if (days <= 0) days = 90;
                if (days > 366) days = 366;

                var endDate = DateTime.UtcNow.Date;
                var startDate = endDate.AddDays(-(days - 1));

                var counts = await _repo.GetDailyCompletionCounts(userId, startDate, endDate, ct);

                // Compute intensity buckets relative to the user's own max in the window.
                int maxCount = counts.Count == 0 ? 0 : counts.Values.Max();

                var cells = new List<object>(days);
                for (var d = startDate; d <= endDate; d = d.AddDays(1))
                {
                    counts.TryGetValue(d, out var count);
                    int intensity = ComputeIntensity(count, maxCount);
                    cells.Add(new
                    {
                        date = d.ToString("yyyy-MM-dd"),
                        count,
                        intensity
                    });
                }

                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = new
                {
                    startDate = startDate.ToString("yyyy-MM-dd"),
                    endDate = endDate.ToString("yyyy-MM-dd"),
                    days,
                    maxCount,
                    cells
                };
                return response;
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessages = new List<string> { "Get heatmap error, message: " + ex.Message };
                return response;
            }
        }

        private static int ComputeIntensity(int count, int max)
        {
            if (count <= 0 || max <= 0) return 0;
            double ratio = (double)count / max;
            if (ratio <= 0.25) return 1;
            if (ratio <= 0.50) return 2;
            if (ratio <= 0.75) return 3;
            return 4;
        }
    }
}
