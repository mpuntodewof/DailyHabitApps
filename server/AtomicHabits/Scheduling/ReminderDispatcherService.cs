using AtomicHabits.Repositories;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace AtomicHabits.Scheduling
{
    /// <summary>
    /// Ticks every minute, fans out reminders that are due *now* (within a 5-minute backlog
    /// to forgive the loop interval), and marks them fired-today so they don't repeat.
    /// Days-of-week filtering is honored — empty/blank means "all days".
    /// </summary>
    public class ReminderDispatcherService : BackgroundService
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

        private static readonly Dictionary<DayOfWeek, string> DayCodes = new()
        {
            { DayOfWeek.Sunday, "Sun" },
            { DayOfWeek.Monday, "Mon" },
            { DayOfWeek.Tuesday, "Tue" },
            { DayOfWeek.Wednesday, "Wed" },
            { DayOfWeek.Thursday, "Thu" },
            { DayOfWeek.Friday, "Fri" },
            { DayOfWeek.Saturday, "Sat" }
        };

        private readonly IServiceProvider _services;
        private readonly ILogger<ReminderDispatcherService> _logger;

        public ReminderDispatcherService(IServiceProvider services, ILogger<ReminderDispatcherService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReminderDispatcherService started");

            using var timer = new PeriodicTimer(TickInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        await TickAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Reminder tick failed");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // graceful shutdown
            }
        }

        private async Task TickAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IHabitReminderRepositories>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            var nowUtc = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(nowUtc);
            var todayCode = DayCodes[nowUtc.DayOfWeek];

            var due = await repo.GetDueAsync(nowUtc, ct);
            if (due.Count == 0) return;

            foreach (var r in due)
            {
                if (!IsTodayInDaysOfWeek(r.DaysOfWeek, todayCode)) continue;

                var email = r.Habit?.User?.Email;
                if (string.IsNullOrWhiteSpace(email))
                {
                    await repo.MarkFiredAsync(r.Id, today, ct);
                    continue;
                }

                try
                {
                    var subject = $"Reminder: {r.Habit?.Name}";
                    var body = BuildEmailBody(r.Habit?.Name ?? "your habit");
                    await emailSender.SendEmailAsync(email, subject, body);
                    _logger.LogInformation("Reminder {Id} dispatched to {Email}", r.Id, email);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch reminder {Id}", r.Id);
                    // Don't mark as fired — let the next tick retry.
                    continue;
                }

                await repo.MarkFiredAsync(r.Id, today, ct);
            }
        }

        private static bool IsTodayInDaysOfWeek(string? daysOfWeek, string todayCode)
        {
            if (string.IsNullOrWhiteSpace(daysOfWeek)) return true; // empty = all days
            return daysOfWeek
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(code => string.Equals(code, todayCode, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildEmailBody(string habitName) =>
            $@"<html><body>
                <p>Hi,</p>
                <p>This is a reminder to complete your habit: <strong>{habitName}</strong>.</p>
                <p>Keep the streak going!</p>
                <p>— Momentum</p>
            </body></html>";
    }
}
