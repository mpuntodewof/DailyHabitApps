using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Repositories;
using AtomicHabits.Utils;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IHabitTrackingService
    {
        Task<ApiResponse> GetHabitTrackingDates(int habitId, int userId, CancellationToken cancellation);
        Task<ApiResponse> GetHabitStats(int habitId, int userId, CancellationToken cancellationToken);
        Task<ApiResponse> PostHabitProgress([FromBody] HabitTrackingDTO request, CancellationToken cancellationToken);
        Task<ApiResponse> PostDailyHabit(int habitId, int minutes, CancellationToken cancellation, string token);
        
        Task<ApiResponse> GetWeeklyAsync(WeeklyDistributionDTO request, string token, CancellationToken ct);
        Task<ApiResponse> GetMonthlyAsync(MonthlyDistributionDTO request, string token, CancellationToken ct);
        Task<ApiResponse> GetYearlyAsync(YearlyDistributionDTO request, string token, CancellationToken ct);
    }

    public class HabitTrackingService : IHabitTrackingService
    {
        private readonly IAuthService _authService;
        private readonly IHabitTrackingRepositories _repo;
        private readonly IStreakRepositories _streakRepo;
        private readonly AppDbContext _db;
        private readonly IHabitRepositories _repoHabit;
        public HabitTrackingService(IAuthService authService, IHabitTrackingRepositories repo, IStreakRepositories streakRepo, AppDbContext db, IHabitRepositories repoHabit)
        {
            _authService = authService;
            _repo = repo;
            _streakRepo = streakRepo;
            _db = db;
            _repoHabit = repoHabit;
        }

        public async Task<ApiResponse> GetHabitTrackingDates(int habitId, int userId, CancellationToken cancellation)
        {
            var response = new ApiResponse();

            try
            {
                var checkHabitExists = await _repo.GetHabitTrackingsDate(habitId, userId);

                if (!checkHabitExists.Any())
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.NotFound;
                    response.ErrorMessages = new List<string> { "No habits found for this user." };
                    return response;
                }

                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = checkHabitExists;
                return response;
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessages = new List<string> { "Get habits error, message: " + ex.Message };
                return response;
            }
        }

        public async Task<ApiResponse> GetHabitStats(int habitId, int userId, CancellationToken cancellationToken)
        {
            var response = new ApiResponse();

            try
            {
                // Use current month by default
                var now = DateTime.UtcNow;
                var year = now.Year;
                var month = now.Month;
                var daysInMonth = DateTime.DaysInMonth(year, month);
                var isCompletedToday = false;
                var today = DateTime.UtcNow.Date;

                var habit = await _repoHabit.GetHabitById(habitId);
                if (habit == null)
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.NotFound;
                    response.ErrorMessages = new List<string> { "Habit not found." };
                    return response;
                }

                var existingTracking = await _repo.GetHabitTrackingDateByHabitId(habitId, DateOnly.FromDateTime(today));

                if (existingTracking == null)
                {
                    isCompletedToday = false;
                }
                else
                {
                    isCompletedToday = true;
                }

                #region Compute Completion Section

                // Get completed habit tracking dates (distinct date portion)
                var completedTrackings = await _repo.GetCompletedTrackings(habitId, userId);

                // days completed in current month
                var daysCompletedThisMonth = completedTrackings.Count(d => d.Year == year && d.Month == month);

                #region Monthly goal calculation (Option C: hybrid rule)

                double monthlyGoal = 0;
                var gv = habit.GoalValue;
                var gf = (habit.GoalFrequency ?? "").Trim().ToLowerInvariant();

                if (gf.Contains("per day") || gf.Contains("perday") || gf == "per day")
                {
                    // treat boolean daily tasks as 1 per day
                    monthlyGoal = daysInMonth;
                }
                else if (gf.Contains("per week") || gf.Contains("perweek") || gf == "per week")
                {
                    // 4 weeks per month approximation
                    monthlyGoal = gv * 4;
                }
                else if (gf.Contains("per month") || gf.Contains("permonth") || gf == "per month")
                {
                    monthlyGoal = gv;
                }
                else if (gf.Contains("per year") || gf.Contains("peryear") || gf == "per year")
                {
                    // distribute yearly goal to per-month approximation
                    monthlyGoal = Math.Ceiling(gv / 12.0);
                }
                else
                {
                    // fallback: if unknown, assume per day
                    monthlyGoal = daysInMonth;
                }

                #endregion

                // Completion rate percentage (cap at 100)
                double completionRate = monthlyGoal > 0
                    ? Math.Min(100.0, (daysCompletedThisMonth / monthlyGoal) * 100.0)
                    : 0.0;

                var sortedDates = completedTrackings.OrderBy(d => d).ToList();

                #endregion

                #region Streak Computation Section

                var streak = StreakCalculator.Compute(sortedDates);
                var currentStreak = streak.CurrentStreak;
                var longestStreak = streak.LongestStreak;

                #endregion

                var result = new
                {
                    HabitId = habitId,
                    Year = year,
                    Month = month,
                    DaysInMonth = daysInMonth,
                    MonthlyGoal = monthlyGoal,
                    DaysCompletedThisMonth = daysCompletedThisMonth,
                    CompletionRate = Math.Round(completionRate, 2), // percentage with 2 decimals
                    CurrentStreak = currentStreak,
                    LongestStreak = longestStreak,
                    TotalCompletions = sortedDates.Count,
                    CompletedToday = isCompletedToday
                };

                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = result;
                return response;
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessages = new List<string> { "Get habit stats error, message: " + ex.Message };
                return response;
            }
        }

        public async Task<ApiResponse> PostHabitProgress([FromBody] HabitTrackingDTO request, CancellationToken cancellationToken)
        {
            var response = new ApiResponse();

            using var trx = await _db.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var habit = await _repoHabit.GetHabitById(request.HabitId);
                if (habit == null)
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.NotFound;
                    response.ErrorMessages = new List<string> { $"Habit with id: {request.HabitId} is not found" };
                    return response;
                }

                var exists = await _repo.GetExistingDate(request);
                if (exists != null)
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.Conflict;
                    response.ErrorMessages = new List<string> { $"Habit tracking on this date is already exists" };
                    return response;
                }

                var newTracking = await _repo.CreateTracking(request);
                var streak = await _streakRepo.UpsertStreakAfterTracking(request);
                await trx.CommitAsync(cancellationToken);

                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                return response;
            }
            catch (Exception ex)
            {
                await trx.RollbackAsync(cancellationToken);
                response.IsSuccess = false;
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessages = new List<string> { "Post habit progress error, message: " + ex.Message };
                return response;
            }
        }

        public async Task<ApiResponse> PostDailyHabit(int habitId, int minutes, CancellationToken cancellation, string token)
        {
            var response = new ApiResponse();

            try
            {
                var user = await _authService.GetCurrentUserFromJwt(token);
                if (user == null)
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.Unauthorized;
                    response.ErrorMessages = new List<string> { $"user is unahtorized " };
                    return response;
                }

                var userId = Convert.ToInt32(user.UserId);
                var today = DateTime.UtcNow.Date;

                var habit = await _repoHabit.GetHabitbyUserHabitId(userId, habitId);
                if (habit == null)
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.NotFound;
                    response.ErrorMessages = new List<string> { $"Habit not found or doesn't belong to user" };
                    return response;
                }

                var existingTracking = await _repo.GetHabitTrackingDateByHabitId(habitId, DateOnly.FromDateTime(today));
                if (existingTracking != null)
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.Conflict;
                    response.ErrorMessages = new List<string> { $"Today's progress already submitted" };
                    return response;
                }

                var dto = new HabitTrackingDTO
                {
                    HabitId = habitId,
                    UserId = userId,
                    TrackingDate = today,
                    IsCompleted = true,
                    TimeSpentMinutes = minutes
                };
                var newTracking = await _repo.CreateTracking(dto);
                var streak = await _streakRepo.UpsertStreakAfterTracking(dto);

                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = new
                {
                    HabitTracking = new
                    {
                        newTracking.Id,
                        newTracking.HabitId,
                        newTracking.UserId,
                        TrackingDate = newTracking.TrackingDate!.Value.Date.ToString("yyyy-MM-dd"),
                        newTracking.IsCompleted
                    },
                    Streak = new
                    {
                        streak.Id,
                        streak.HabitId,
                        streak.UserId,
                        streak.CurrentStreak,
                        streak.CurrentStreakStartDate,
                        streak.BestStreak,
                        streak.BestStreakStartDate,
                        streak.BestStreakEndDate,
                        streak.CompletionRate
                    }
                };
                return response;
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessages = new List<string> { "Post daily habit error, message: " + ex.Message };
                return response;
            }
        }


        #region Habit Distribution Chart [Completion Rate]
        
        public async Task<ApiResponse> GetWeeklyAsync(WeeklyDistributionDTO request, string token, CancellationToken ct)
        {
            var response = new ApiResponse();
         
            try
            {
                var user = await _authService.GetCurrentUserFromJwt(token);
                if (user == null)
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.Unauthorized;
                    response.ErrorMessages = new List<string> { $"user is unahtorized " };
                    return response;
                }

                var habit = await _repoHabit.GetHabitbyUserHabitId(request.UserId, request.HabitId);
                if (habit == null)
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.NotFound;
                    response.ErrorMessages = new List<string> { "Habit not found or doesn't belong to user" };
                    return response;
                }

                var data = await _repo.GetWeeklyDistribution(request, ct);

                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = new
                {
                    request.HabitId,
                    request.Year,
                    request.Month,
                    range = "weekly",
                    labels = new[] { "Week 1", "Week 2", "Week 3", "Week 4" },
                    values = data
                };

                return response;
            }
            catch(Exception ex)
            {
                response.IsSuccess = false;
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessages = new List<string>
                {
                    "Get Weekly distribution error, message: " + ex.Message
                };
                return response;
            }
        }

        public async Task<ApiResponse> GetMonthlyAsync(MonthlyDistributionDTO request, string token, CancellationToken ct)
        {
            var response = new ApiResponse();

            try
            {
                var user = await _authService.GetCurrentUserFromJwt(token);
                if (user == null)
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.Unauthorized;
                    response.ErrorMessages = new List<string> { $"user is unahtorized " };
                    return response;
                }

                var habit = await _repoHabit.GetHabitbyUserHabitId(request.UserId, request.HabitId);
                if (habit == null)
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.NotFound;
                    response.ErrorMessages = new List<string> { "Habit not found or doesn't belong to user" };
                    return response;
                }

                var data = await _repo.GetMonthlyDistribution(request, ct);

                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = new
                {
                    request.HabitId,
                    request.Year,
                    range   = "monthly",
                    labels  = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedMonthNames.Take(12),
                    values  = data
                };

                return response;
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessages = new List<string>
                {
                    "Get Monthly distribution error, message: " + ex.Message
                };
                return response;
            }
        }

        public async Task<ApiResponse> GetYearlyAsync(YearlyDistributionDTO request, string token, CancellationToken ct)
        {
            var response = new ApiResponse();

            try
            {
                var user = await _authService.GetCurrentUserFromJwt(token);
                if (user == null)
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.Unauthorized;
                    response.ErrorMessages = new List<string> { $"user is unahtorized " };
                    return response;
                }

                var habit = await _repoHabit.GetHabitbyUserHabitId(request.UserId, request.HabitId);
                if (habit == null)  
                {
                    response.IsSuccess = false;
                    response.StatusCode = HttpStatusCode.NotFound;
                    response.ErrorMessages = new List<string> { "Habit not found or doesn't belong to user" };
                    return response;
                }

                var data = await _repo.GetYearlyDistribution(request, ct);

                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = new
                {
                    request.HabitId,
                    request.StartYear,
                    request.EndYear,
                    range   = "yearly",
                    labels  = Enumerable.Range(request.StartYear, request.EndYear - request.StartYear + 1),
                    values  = data
                };

                return response;
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessages = new List<string>
                {
                    "Get Yearly distribution error, message: " + ex.Message
                };
                return response;
            }
        }

        #endregion

    }
}
