using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Utils;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IWeeklyReportService
    {
        Task<ApiResponse> GetCurrentWeekAsync(int userId, CancellationToken ct);
    }

    public class WeeklyReportService : IWeeklyReportService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<WeeklyReportService> _logger;

        public WeeklyReportService(AppDbContext db, ILogger<WeeklyReportService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse> GetCurrentWeekAsync(int userId, CancellationToken ct)
        {
            var today = DateTime.UtcNow.Date;
            var weekStart = HabitMath.StartOfIsoWeek(today);                 // Monday this week
            var lastWeekStart = weekStart.AddDays(-7);
            int daysElapsedThisWeek = (int)(today - weekStart).TotalDays + 1; // inclusive of today
            const int lastWeekDays = 7;

            var habits = await _db.Habits
                .Where(h => h.UserId == userId && !h.IsArchived)
                .ToListAsync(ct);

            var dto = new WeeklyReportDto
            {
                WeekStart = weekStart.ToString("yyyy-MM-dd"),
                ScoreBand = "Needs work"
            };

            if (habits.Count == 0)
                return Ok(dto); // score 0, no habits

            // pull this-week + last-week completed trackings once
            var completions = await _db.HabitTrackings
                .Where(t => t.UserId == userId && t.IsCompleted && t.TrackingDate != null
                            && t.TrackingDate >= lastWeekStart && t.TrackingDate < weekStart.AddDays(7))
                .Select(t => new { t.HabitId, Date = t.TrackingDate!.Value.Date })
                .ToListAsync(ct);

            // per-habit completed counts within a [start,endExclusive) window
            int CompletedFor(int habitId, DateTime start, DateTime endExclusive) =>
                completions.Count(c => c.HabitId == habitId && c.Date >= start && c.Date < endExclusive);

            // ---- weighted performance score (this week) ----
            double weightedCompleted = 0, weightedExpected = 0;
            var perHabitRate = new List<(string Name, double Rate)>();
            foreach (var h in habits)
            {
                double weight = h.MilestoneId != null ? 1.5 : 1.0;
                int expected = HabitMath.ExpectedSessions(h, daysElapsedThisWeek, 7);
                if (expected <= 0) continue;
                int completed = Math.Min(CompletedFor(h.Id, weekStart, weekStart.AddDays(7)), expected); // cap so >100% impossible
                weightedExpected += weight * expected;
                weightedCompleted += weight * completed;
                perHabitRate.Add((h.Name, (double)completed / expected));
            }

            int score = weightedExpected <= 0 ? 0
                : (int)Math.Round(100.0 * weightedCompleted / weightedExpected);
            score = Math.Clamp(score, 0, 100);
            dto.PerformanceScore = score;
            dto.ScoreBand = score >= 75 ? "Strong" : score >= 40 ? "Building" : "Needs work";

            // ---- best / worst habit ----
            if (perHabitRate.Count >= 2)
            {
                dto.BestHabit = perHabitRate.OrderByDescending(p => p.Rate).First().Name;
                var worst = perHabitRate.OrderBy(p => p.Rate).First();
                dto.WorstHabit = worst.Name;
                dto.FocusNextWeek = $"Focus next week: {worst.Name} — your lowest completion at {(int)Math.Round(worst.Rate * 100)}%.";
            }

            // ---- consistency delta (UNWEIGHTED, simple rate this vs last week) ----
            int ExpectedSum(int daysElapsed) => habits.Sum(h => HabitMath.ExpectedSessions(h, daysElapsed, 7));
            int thisExpected = ExpectedSum(daysElapsedThisWeek);
            int thisCompleted = habits.Sum(h => Math.Min(CompletedFor(h.Id, weekStart, weekStart.AddDays(7)),
                                                          HabitMath.ExpectedSessions(h, daysElapsedThisWeek, 7)));
            int lastExpected = ExpectedSum(lastWeekDays);
            int lastCompleted = habits.Sum(h => Math.Min(CompletedFor(h.Id, lastWeekStart, weekStart),
                                                          HabitMath.ExpectedSessions(h, lastWeekDays, 7)));
            int thisRate = thisExpected <= 0 ? 0 : (int)Math.Round(100.0 * thisCompleted / thisExpected);
            int lastRate = lastExpected <= 0 ? 0 : (int)Math.Round(100.0 * lastCompleted / lastExpected);
            dto.ConsistencyDelta = lastExpected <= 0 ? 0 : thisRate - lastRate;

            // ---- top miss reason (this week) ----
            var weekStartDate = DateOnly.FromDateTime(weekStart);
            var weekEndDate = DateOnly.FromDateTime(weekStart.AddDays(7));
            var topReason = await _db.HabitSkips
                .Where(s => s.UserId == userId && s.Date >= weekStartDate && s.Date < weekEndDate)
                .GroupBy(s => s.Reason)
                .Select(g => new { Reason = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .FirstOrDefaultAsync(ct);
            if (topReason != null) dto.TopMissReason = topReason.Reason.Humanize();

            return Ok(dto);
        }

        private static ApiResponse Ok(object result) =>
            new() { IsSuccess = true, StatusCode = HttpStatusCode.OK, Result = result };
    }
}
