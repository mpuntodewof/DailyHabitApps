using AtomicHabits.Data;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AtomicHabits.Services
{
    public interface IGoalFrameworkService
    {
        // Vision
        Task<ApiResponse> ListVisionsAsync(int userId, CancellationToken ct);
        Task<ApiResponse> CreateVisionAsync(int userId, VisionUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> UpdateVisionAsync(int userId, int visionId, VisionUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> DeleteVisionAsync(int userId, int visionId, CancellationToken ct);
        // Goal
        Task<ApiResponse> ListGoalsAsync(int userId, CancellationToken ct);
        Task<ApiResponse> CreateGoalAsync(int userId, GoalUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> UpdateGoalAsync(int userId, int goalId, GoalUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> DeleteGoalAsync(int userId, int goalId, CancellationToken ct);
        // Milestone
        Task<ApiResponse> ListMilestonesAsync(int userId, int goalId, CancellationToken ct);
        Task<ApiResponse> CreateMilestoneAsync(int userId, MilestoneUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> UpdateMilestoneAsync(int userId, int milestoneId, MilestoneUpsertDto dto, CancellationToken ct);
        Task<ApiResponse> DeleteMilestoneAsync(int userId, int milestoneId, CancellationToken ct);
    }

    public class GoalFrameworkService : IGoalFrameworkService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<GoalFrameworkService> _logger;

        public GoalFrameworkService(AppDbContext db, ILogger<GoalFrameworkService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ---------- Vision ----------
        public async Task<ApiResponse> ListVisionsAsync(int userId, CancellationToken ct)
        {
            var visions = await _db.Visions
                .Where(v => v.UserId == userId)
                .OrderByDescending(v => v.CreatedAt)
                .Select(v => new VisionDto { Id = v.Id, Title = v.Title, Description = v.Description })
                .ToListAsync(ct);
            return Ok(visions);
        }

        public async Task<ApiResponse> CreateVisionAsync(int userId, VisionUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest("Title is required");
            var vision = new Vision { UserId = userId, Title = dto.Title.Trim(), Description = dto.Description };
            _db.Visions.Add(vision);
            await _db.SaveChangesAsync(ct);
            return Ok(new VisionDto { Id = vision.Id, Title = vision.Title, Description = vision.Description }, HttpStatusCode.Created);
        }

        public async Task<ApiResponse> UpdateVisionAsync(int userId, int visionId, VisionUpsertDto dto, CancellationToken ct)
        {
            var vision = await _db.Visions.FirstOrDefaultAsync(v => v.Id == visionId && v.UserId == userId, ct);
            if (vision == null) return NotFound("Vision not found");
            if (!string.IsNullOrWhiteSpace(dto.Title)) vision.Title = dto.Title.Trim();
            vision.Description = dto.Description;
            await _db.SaveChangesAsync(ct);
            return Ok(new VisionDto { Id = vision.Id, Title = vision.Title, Description = vision.Description });
        }

        public async Task<ApiResponse> DeleteVisionAsync(int userId, int visionId, CancellationToken ct)
        {
            var vision = await _db.Visions.FirstOrDefaultAsync(v => v.Id == visionId && v.UserId == userId, ct);
            if (vision == null) return NotFound("Vision not found");

            // FK_Goals_Visions_VisionId is NoAction — null child goals' VisionId before delete.
            var childGoals = await _db.Goals.Where(g => g.VisionId == visionId && g.UserId == userId).ToListAsync(ct);
            foreach (var g in childGoals) g.VisionId = null;

            _db.Visions.Remove(vision);
            await _db.SaveChangesAsync(ct);
            return Ok(new { visionId });
        }

        // ---------- Goal ----------
        public async Task<ApiResponse> ListGoalsAsync(int userId, CancellationToken ct)
        {
            var goals = await _db.Goals
                .Where(g => g.UserId == userId)
                .OrderByDescending(g => g.CreatedAt)
                .Select(g => new GoalDto
                {
                    Id = g.Id, VisionId = g.VisionId, Title = g.Title,
                    Status = g.Status.ToString(), TargetDate = g.TargetDate
                })
                .ToListAsync(ct);
            return Ok(goals);
        }

        public async Task<ApiResponse> CreateGoalAsync(int userId, GoalUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest("Title is required");
            if (dto.VisionId is int vid && !await _db.Visions.AnyAsync(v => v.Id == vid && v.UserId == userId, ct))
                return BadRequest("Vision not found");

            var goal = new Goal
            {
                UserId = userId,
                VisionId = dto.VisionId,
                Title = dto.Title.Trim(),
                Status = ParseGoalStatus(dto.Status),
                TargetDate = dto.TargetDate
            };
            _db.Goals.Add(goal);
            await _db.SaveChangesAsync(ct);
            return Ok(ToGoalDto(goal), HttpStatusCode.Created);
        }

        public async Task<ApiResponse> UpdateGoalAsync(int userId, int goalId, GoalUpsertDto dto, CancellationToken ct)
        {
            var goal = await _db.Goals.FirstOrDefaultAsync(g => g.Id == goalId && g.UserId == userId, ct);
            if (goal == null) return NotFound("Goal not found");
            if (dto.VisionId is int vid && !await _db.Visions.AnyAsync(v => v.Id == vid && v.UserId == userId, ct))
                return BadRequest("Vision not found");

            if (!string.IsNullOrWhiteSpace(dto.Title)) goal.Title = dto.Title.Trim();
            goal.VisionId = dto.VisionId;
            if (!string.IsNullOrWhiteSpace(dto.Status)) goal.Status = ParseGoalStatus(dto.Status);
            goal.TargetDate = dto.TargetDate;
            await _db.SaveChangesAsync(ct);
            return Ok(ToGoalDto(goal));
        }

        public async Task<ApiResponse> DeleteGoalAsync(int userId, int goalId, CancellationToken ct)
        {
            var goal = await _db.Goals.FirstOrDefaultAsync(g => g.Id == goalId && g.UserId == userId, ct);
            if (goal == null) return NotFound("Goal not found");

            // Milestones cascade with the goal, but habits under those milestones do NOT
            // auto-null (FK_Habits_Milestones is NoAction). Null them first.
            var milestoneIds = await _db.Milestones.Where(m => m.GoalId == goalId).Select(m => m.Id).ToListAsync(ct);
            if (milestoneIds.Count > 0)
            {
                var habits = await _db.Habits.Where(h => h.MilestoneId != null && milestoneIds.Contains(h.MilestoneId!.Value)).ToListAsync(ct);
                foreach (var h in habits) h.MilestoneId = null;
            }
            // EF InMemory does NOT cascade-delete Milestones; remove them explicitly
            // (also safer on SQL Server). FK_Milestones_Goals is Cascade on SQL Server.
            var milestones = await _db.Milestones.Where(m => m.GoalId == goalId).ToListAsync(ct);
            if (milestones.Count > 0) _db.Milestones.RemoveRange(milestones);

            _db.Goals.Remove(goal);
            await _db.SaveChangesAsync(ct);
            return Ok(new { goalId });
        }

        // ---------- Milestone (full impl in Task 4) ----------
        public Task<ApiResponse> ListMilestonesAsync(int userId, int goalId, CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResponse> CreateMilestoneAsync(int userId, MilestoneUpsertDto dto, CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResponse> UpdateMilestoneAsync(int userId, int milestoneId, MilestoneUpsertDto dto, CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResponse> DeleteMilestoneAsync(int userId, int milestoneId, CancellationToken ct) => throw new NotImplementedException();

        // ---------- helpers ----------
        private static GoalDto ToGoalDto(Goal g) => new()
        {
            Id = g.Id, VisionId = g.VisionId, Title = g.Title, Status = g.Status.ToString(), TargetDate = g.TargetDate
        };
        private static GoalStatus ParseGoalStatus(string? s) =>
            Enum.TryParse<GoalStatus>(s, true, out var v) ? v : GoalStatus.Active;

        private static ApiResponse Ok(object result, HttpStatusCode status = HttpStatusCode.OK) =>
            new() { IsSuccess = true, StatusCode = status, Result = result };
        private static ApiResponse NotFound(string message) => Error(HttpStatusCode.NotFound, message);
        private static ApiResponse BadRequest(string message) => Error(HttpStatusCode.BadRequest, message);
        private static ApiResponse Error(HttpStatusCode status, string message) =>
            new() { IsSuccess = false, StatusCode = status, ErrorMessages = new List<string> { message } };
    }
}
