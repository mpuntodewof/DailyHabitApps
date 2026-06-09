using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Utils;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IInsightService
    {
        Task<ApiResponse> GetInsightsAsync(int userId, CancellationToken ct);
    }

    public class InsightService : IInsightService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<InsightService> _logger;

        public InsightService(AppDbContext db, ILogger<InsightService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse> GetInsightsAsync(int userId, CancellationToken ct)
        {
            var insights = new List<InsightDto>();

            var topReason = await TopSkipReasonAsync(userId, ct);
            if (topReason != null) insights.Add(topReason);

            var tod = await CompletionTimeOfDayAsync(userId, ct);
            if (tod != null) insights.Add(tod);

            var wk = await WeekdayVsWeekendAsync(userId, ct);
            if (wk != null) insights.Add(wk);

            var mostSkipped = await MostSkippedHabitAsync(userId, ct);
            if (mostSkipped != null) insights.Add(mostSkipped);

            return new ApiResponse { IsSuccess = true, StatusCode = HttpStatusCode.OK, Result = insights };
        }

        private async Task<InsightDto?> TopSkipReasonAsync(int userId, CancellationToken ct)
        {
            var skips = await _db.HabitSkips.Where(s => s.UserId == userId)
                .GroupBy(s => s.Reason)
                .Select(g => new { Reason = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var total = skips.Sum(s => s.Count);
            if (total < 3) return null;
            var top = skips.OrderByDescending(s => s.Count).First();
            var pct = (int)Math.Round(100.0 * top.Count / total);
            return new InsightDto { Key = "top-skip-reason",
                Text = $"Your most common reason for skipping is {top.Reason.Humanize()} ({pct}% of skips)." };
        }

        private async Task<InsightDto?> CompletionTimeOfDayAsync(int userId, CancellationToken ct)
        {
            var completedAts = await _db.HabitTrackings
                .Where(t => t.UserId == userId && t.IsCompleted && t.CompletedAt != null)
                .Select(t => t.CompletedAt!.Value)
                .ToListAsync(ct);
            if (completedAts.Count < 5) return null;
            var hours = completedAts.Select(d => d.Hour).ToList();
            int morning = hours.Count(h => h < 12);
            int afternoon = hours.Count(h => h >= 12 && h < 18);
            int evening = hours.Count(h => h >= 18);
            var buckets = new[] { ("morning", morning), ("afternoon", afternoon), ("evening", evening) };
            var top = buckets.OrderByDescending(b => b.Item2).First();
            var pct = (int)Math.Round(100.0 * top.Item2 / hours.Count);
            return new InsightDto { Key = "completion-time-of-day",
                Text = $"You complete most habits in the {top.Item1} — {pct}% of your completions." };
        }

        private async Task<InsightDto?> WeekdayVsWeekendAsync(int userId, CancellationToken ct)
        {
            var completedAts = await _db.HabitTrackings
                .Where(t => t.UserId == userId && t.IsCompleted && t.CompletedAt != null)
                .Select(t => t.CompletedAt!.Value)
                .ToListAsync(ct);
            if (completedAts.Count < 5) return null;
            var dows = completedAts.Select(d => d.DayOfWeek).ToList();
            int weekend = dows.Count(d => d == DayOfWeek.Saturday || d == DayOfWeek.Sunday);
            int weekday = dows.Count - weekend;
            if (weekday == 0 || weekend == 0) return null; // need both sides
            // normalize per available day (5 weekdays vs 2 weekend days)
            double weekdayPerDay = weekday / 5.0;
            double weekendPerDay = weekend / 2.0;
            if (weekdayPerDay >= weekendPerDay)
            {
                var ratio = Math.Round(weekdayPerDay / weekendPerDay, 1);
                return new InsightDto { Key = "weekday-vs-weekend",
                    Text = $"You complete {ratio}× more habits on weekdays than weekends." };
            }
            else
            {
                var ratio = Math.Round(weekendPerDay / weekdayPerDay, 1);
                return new InsightDto { Key = "weekday-vs-weekend",
                    Text = $"You complete {ratio}× more habits on weekends than weekdays." };
            }
        }

        private async Task<InsightDto?> MostSkippedHabitAsync(int userId, CancellationToken ct)
        {
            var grouped = await _db.HabitSkips.Where(s => s.UserId == userId)
                .GroupBy(s => s.HabitId)
                .Select(g => new { HabitId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            if (grouped.Sum(g => g.Count) < 3) return null;
            var top = grouped.OrderByDescending(g => g.Count).First();
            var name = await _db.Habits.Where(h => h.Id == top.HabitId && h.UserId == userId)
                .Select(h => h.Name).FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(name)) return null;
            return new InsightDto { Key = "most-skipped-habit",
                Text = $"{name} is your most-skipped habit ({top.Count} skips)." };
        }
    }
}
