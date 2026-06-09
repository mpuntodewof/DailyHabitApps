using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IHabitService
    {
        Task<ApiResponse> GetHabits(int userId, string token, CancellationToken cancellationToken, bool includeArchived = false);
        Task<ApiResponse> SearchAsync(int userId, string? search, int? tagId, bool includeArchived, int page, int pageSize, CancellationToken ct);
        Task<ApiResponse> PostHabit(HabitDTO habitDto);
        Task<ApiResponse> UpdateHabit(int habitId, HabitDTO habitDto);
        Task<ApiResponse> DeleteHabit(int habitId);
        Task<ApiResponse> HabitSummary(int userId);
        Task<ApiResponse> SetArchivedAsync(int habitId, int userId, bool archived, CancellationToken ct);
        Task<ApiResponse> GetContributionAsync(int userId, int habitId);
    }

    public class HabitService : IHabitService
    {
        private readonly IHabitRepositories _repo;
        private readonly AppDbContext _db;
        private ApiResponse _response;
        private readonly ILogger<HabitService> _log;
        private readonly IAuthService _authService;
        public HabitService(IHabitRepositories repo, AppDbContext db, ILogger<HabitService> log, IAuthService authService) 
        { 
            _repo = repo;
            _db = db;
            _response = new ApiResponse();
            _log = log;
            _authService = authService;
        }

        public async Task<ApiResponse> GetHabits(int userId, string token, CancellationToken ct, bool includeArchived = false)
        {
            try
            {
                var user = await _authService.GetCurrentUserFromJwt(token);
                if (user == null)
                {
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.Unauthorized;
                    _response.ErrorMessages = new List<string> { $"user is unahtorized " };
                    return _response;
                }

                var habits = await _repo.GetHabitByUserId(userId, includeArchived);

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                _response.Result = habits ?? new List<Habit>();
                return _response;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"GetHabits failed for userId {userId}", userId);
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages = new List<string> { "GetAn unexpected error occurred while retrieving habits. " + ex.Message };
                return _response;
            }
        }

        public async Task<ApiResponse> PostHabit(HabitDTO habitDto)
        {
            try
            {
                if (habitDto == null)
                {
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.BadRequest;
                    _response.ErrorMessages = new List<string> { "Habit data is required." };
                    return _response;
                }

                if (habitDto.MilestoneId.HasValue && !await OwnsMilestone(habitDto.UserId, habitDto.MilestoneId.Value))
                {
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.BadRequest;
                    _response.ErrorMessages = new List<string> { "Milestone not found or doesn't belong to user." };
                    return _response;
                }

               var createdHabit = await _repo.PostHabit(habitDto);

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                _response.Result = new { HabitDTO = habitDto };
                return _response;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Post habit failed, payload: {habitDto}");
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages = new List<string> { "Post habit error, message: " + ex.Message };
                return _response;
            }
        }

        public async Task<ApiResponse> UpdateHabit(int habitId, HabitDTO habitDto)
        {
            try
            {
                var habit = await _repo.GetHabitById(habitId);
                if (habit == null)
                {
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.NotFound;
                    _response.ErrorMessages = new List<string> { "Habit not found." };
                    return _response;
                }

                if (habitDto.MilestoneId.HasValue && !await OwnsMilestone(habit.UserId, habitDto.MilestoneId.Value))
                {
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.BadRequest;
                    _response.ErrorMessages = new List<string> { "Milestone not found or doesn't belong to user." };
                    return _response;
                }

                var updHabit = await _repo.UpdateHabit(habitId, habitDto);

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                _response.Result = habit;
                return _response;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Update habit failed, payload: {habitDto}");
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages = new List<string> { "Update habit error, message: " + ex.Message };
                return _response;
            }
        }

        public async Task<ApiResponse> DeleteHabit(int habitId)
        {
            try
            {
                var habit = await _repo.DeleteHabit(habitId);

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                _response.Result = $"Habit with ID {habitId} deleted successfully.";
                return _response;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Delete habit failed, habit id: {habitId}");
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages = new List<string> { "Delete habit error, message: " + ex.Message };
                return _response;
            }
        }

        public async Task<ApiResponse> HabitSummary(int userId)
        {
            try
            {
                var today = DateTime.UtcNow.Date;
                var startOfWeek = StartOfIsoWeek(today);
                var startOfMonth = new DateTime(today.Year, today.Month, 1);
                var daysElapsedThisWeek = (today - startOfWeek).Days + 1;
                var daysElapsedThisMonth = today.Day;

                var habits = await _repo.GetActiveHabits(userId, CancellationToken.None);
                var habitIds = habits.Select(h => h.Id).ToList();

                var todayTrackings = await _repo.GetTodayTrackings(habitIds, today, CancellationToken.None);
                var weekTrackings = await _repo.GetWeeklyTrackings(habitIds, startOfWeek, CancellationToken.None);
                var monthTrackings = await _repo.GetMonthlyTrackings(habitIds, startOfMonth, CancellationToken.None);

                int dailyHabitCount = habits.Count(h => IsDailyHabit(h));
                int completedToday = todayTrackings.Count(t => t.IsCompleted);
                int todayRate = dailyHabitCount == 0 ? 0 : (completedToday * 100 / dailyHabitCount);

                int expectedThisWeek = habits.Sum(h => ExpectedSessions(h, daysElapsedThisWeek, periodLengthDays: 7));
                int completedThisWeek = weekTrackings.Count(t => t.IsCompleted);
                int weeklyRate = expectedThisWeek == 0 ? 0 : Math.Min(100, completedThisWeek * 100 / expectedThisWeek);

                int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
                int expectedThisMonth = habits.Sum(h => ExpectedSessions(h, daysElapsedThisMonth, periodLengthDays: daysInMonth));
                int completedThisMonth = monthTrackings.Count(t => t.IsCompleted);
                int monthlyRate = expectedThisMonth == 0 ? 0 : Math.Min(100, completedThisMonth * 100 / expectedThisMonth);

                int healthScore = (todayRate + weeklyRate + monthlyRate) / 3;
                int habitsToday = dailyHabitCount;

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                _response.Result = new HabitSummaryDto
                {
                    TodaySummary = new TodaySummaryDto
                    {
                        HabitsToday = habitsToday,
                        CompletedToday = completedToday,
                        TodayCompletionRate = todayRate
                    },
                    WeeklySummary = new WeeklySummaryDto
                    {
                        WeeklyCompletionRate = weeklyRate,
                        TotalCompletedThisWeek = completedThisWeek
                    },
                    MonthlySummary = new MonthlySummaryDto
                    {
                        MonthlyCompletionRate = monthlyRate,
                        TotalMonthlySessions = completedThisMonth
                    },
                    HabitHealthScore = healthScore
                };


                return _response;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Get habit summary failed, message: " + ex.Message);
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages = new List<string>
                {
                    "Get habits summary error, message: " + ex.Message
                };
                return _response;
            }
        }

        public async Task<ApiResponse> SearchAsync(int userId, string? search, int? tagId, bool includeArchived, int page, int pageSize, CancellationToken ct)
        {
            try
            {
                var (items, total) = await _repo.SearchAsync(userId, search, tagId, includeArchived, page, pageSize, ct);

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                _response.Result = new
                {
                    items,
                    total,
                    page = page < 1 ? 1 : page,
                    pageSize = pageSize < 1 ? 20 : (pageSize > 100 ? 100 : pageSize)
                };
                return _response;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[HabitService.SearchAsync] Error");
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages = new List<string> { "Search habits error: " + ex.Message };
                return _response;
            }
        }

        public async Task<ApiResponse> SetArchivedAsync(int habitId, int userId, bool archived, CancellationToken ct)
        {
            try
            {
                var ok = await _repo.SetArchivedAsync(habitId, userId, archived, ct);
                if (!ok)
                {
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.NotFound;
                    _response.ErrorMessages = new List<string> { "Habit not found or doesn't belong to user" };
                    return _response;
                }

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                _response.Result = new { habitId, archived };
                return _response;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[HabitService.SetArchivedAsync] Error");
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages = new List<string> { "Set archived error: " + ex.Message };
                return _response;
            }
        }

        public async Task<ApiResponse> GetContributionAsync(int userId, int habitId)
        {
            try
            {
                // Owner-scoped read; null-safe navigation across Habit->Milestone->Goal->Vision.
                // EF translates the projection into the necessary joins (no Include needed).
                var contribution = await _db.Habits
                    .Where(h => h.Id == habitId && h.UserId == userId)
                    .Select(h => new HabitContributionDto
                    {
                        MilestoneId = h.MilestoneId,
                        MilestoneTitle = h.Milestone != null ? h.Milestone.Title : null,
                        GoalTitle = (h.Milestone != null && h.Milestone.Goal != null) ? h.Milestone.Goal.Title : null,
                        VisionTitle = (h.Milestone != null && h.Milestone.Goal != null && h.Milestone.Goal.Vision != null)
                            ? h.Milestone.Goal.Vision.Title
                            : null,
                        IdentityTitle =
                            (h.Milestone != null && h.Milestone.Goal != null && h.Milestone.Goal.Vision != null)
                                ? h.Milestone.Goal.Vision.Title
                            : (h.Milestone != null && h.Milestone.Goal != null)
                                ? h.Milestone.Goal.Title
                            : (h.Milestone != null)
                                ? h.Milestone.Title
                            : null,
                    })
                    .FirstOrDefaultAsync();

                if (contribution == null)
                {
                    _response.IsSuccess = false;
                    _response.StatusCode = HttpStatusCode.NotFound;
                    _response.ErrorMessages = new List<string> { "Habit not found or doesn't belong to user." };
                    return _response;
                }

                _response.IsSuccess = true;
                _response.StatusCode = HttpStatusCode.OK;
                _response.Result = contribution;
                return _response;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[HabitService.GetContributionAsync] Error");
                _response.IsSuccess = false;
                _response.StatusCode = HttpStatusCode.InternalServerError;
                _response.ErrorMessages = new List<string> { "Get habit contribution error: " + ex.Message };
                return _response;
            }
        }

        private Task<bool> OwnsMilestone(int userId, int milestoneId) =>
            _db.Milestones.AnyAsync(m => m.Id == milestoneId && m.UserId == userId);

        private static DateTime StartOfIsoWeek(DateTime today)
        {
            int diff = (7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return today.AddDays(-diff).Date;
        }

        private static bool IsDailyHabit(Habit h)
        {
            return IsDailyFrequency((h.GoalFrequency ?? "").Trim().ToLowerInvariant());
        }

        // True for an empty/unset frequency or a daily one. NOTE: the literal "daily" does NOT
        // contain the substring "day" (d-a-i-l-y), and "daily" is the model's default value —
        // so a naive Contains("day") silently undercounts every default habit. Match both forms.
        private static bool IsDailyFrequency(string f)
        {
            return string.IsNullOrEmpty(f) || f.Contains("day") || f.Contains("dai");
        }

        // Expected completions for a habit within a window of `daysElapsed` days
        // out of a `periodLengthDays`-day period (week=7, month=daysInMonth, etc.).
        private static int ExpectedSessions(Habit h, int daysElapsed, int periodLengthDays)
        {
            if (daysElapsed <= 0 || periodLengthDays <= 0) return 0;

            var f = (h.GoalFrequency ?? "").Trim().ToLowerInvariant();
            if (IsDailyFrequency(f)) return daysElapsed;
            if (f.Contains("week"))
            {
                double weeksElapsed = (double)daysElapsed / 7.0;
                return (int)Math.Ceiling(weeksElapsed);
            }
            if (f.Contains("month"))
            {
                return daysElapsed >= 1 ? 1 : 0;
            }
            if (f.Contains("year"))
            {
                return 0;
            }
            return daysElapsed;
        }
    }
}
