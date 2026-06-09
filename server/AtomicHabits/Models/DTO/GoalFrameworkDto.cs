namespace AtomicHabits.Models.DTO
{
    // ----- Vision -----
    public class VisionDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
    public class VisionUpsertDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    // ----- Goal -----
    public class GoalDto
    {
        public int Id { get; set; }
        public int? VisionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = "Active"; // GoalStatus name
        public DateTime? TargetDate { get; set; }
    }
    public class GoalUpsertDto
    {
        public int? VisionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Status { get; set; }      // optional; defaults Active on create
        public DateTime? TargetDate { get; set; }
    }

    // ----- Milestone -----
    public class MilestoneDto
    {
        public int Id { get; set; }
        public int GoalId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = "Active"; // MilestoneStatus name
        public int OrderIndex { get; set; }
    }
    public class MilestoneUpsertDto
    {
        public int GoalId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Status { get; set; }
        public int OrderIndex { get; set; }
    }

    // ----- Habit payoff line (read model) -----
    public class HabitContributionDto
    {
        public int? MilestoneId { get; set; }
        public string? MilestoneTitle { get; set; }
        public string? GoalTitle { get; set; }
        public string? VisionTitle { get; set; }
        public string? IdentityTitle { get; set; }
    }
}
