using AtomicHabits.Models.DTO;
using FluentValidation;

namespace AtomicHabits.Validators
{
    public class HabitDtoValidator : AbstractValidator<HabitDTO>
    {
        private static readonly string[] AllowedFrequencies = { "Daily", "Weekly", "Monthly" };
        private static readonly string[] AllowedGoalFrequencies = { "per day", "per week", "per month", "per year", "daily", "weekly", "monthly", "yearly" };

        public HabitDtoValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Habit name is required")
                .MaximumLength(100);

            RuleFor(x => x.Frequency)
                .NotEmpty().WithMessage("Frequency is required")
                .Must(f => AllowedFrequencies.Contains(f, StringComparer.OrdinalIgnoreCase))
                .WithMessage("Frequency must be one of: Daily, Weekly, Monthly");

            RuleFor(x => x.GoalValue)
                .GreaterThan(0).WithMessage("Goal value must be positive");

            RuleFor(x => x.GoalUnit)
                .NotEmpty().WithMessage("Goal unit is required")
                .MaximumLength(20);

            RuleFor(x => x.GoalFrequency)
                .NotEmpty().WithMessage("Goal frequency is required")
                .Must(gf => AllowedGoalFrequencies.Any(allowed => gf.Contains(allowed, StringComparison.OrdinalIgnoreCase)))
                .WithMessage("Goal frequency must contain one of: per day / per week / per month / per year");

            RuleFor(x => x.Color)
                .Matches("^#?[0-9A-Fa-f]{3,8}$")
                .When(x => !string.IsNullOrWhiteSpace(x.Color))
                .WithMessage("Color must be a hex color");
        }
    }

    public class HabitTrackingDtoValidator : AbstractValidator<HabitTrackingDTO>
    {
        public HabitTrackingDtoValidator()
        {
            RuleFor(x => x.HabitId).GreaterThan(0);
            RuleFor(x => x.TrackingDate).NotEmpty();
            RuleFor(x => x.TimeSpentMinutes).GreaterThanOrEqualTo(0);
        }
    }
}
