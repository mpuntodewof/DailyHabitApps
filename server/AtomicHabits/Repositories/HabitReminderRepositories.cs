using AtomicHabits.Data;
using AtomicHabits.Models;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Repositories
{
    public interface IHabitReminderRepositories
    {
        Task<List<HabitReminder>> GetForUserAsync(int userId, CancellationToken ct);
        Task<HabitReminder?> GetByIdForUserAsync(int reminderId, int userId, CancellationToken ct);
        Task<HabitReminder> CreateAsync(HabitReminder reminder, CancellationToken ct);
        Task<bool> UpdateAsync(HabitReminder reminder, CancellationToken ct);
        Task<bool> DeleteAsync(int reminderId, int userId, CancellationToken ct);
        Task<bool> HabitBelongsToUserAsync(int habitId, int userId, CancellationToken ct);

        // Used by the scheduler — must include Habit + User for fan-out.
        Task<List<HabitReminder>> GetDueAsync(DateTime utcNow, CancellationToken ct);
        Task<bool> MarkFiredAsync(int reminderId, DateOnly utcDate, CancellationToken ct);
    }

    public class HabitReminderRepositories : IHabitReminderRepositories
    {
        private readonly AppDbContext _db;
        private readonly ILogger<HabitReminderRepositories> _logger;

        public HabitReminderRepositories(AppDbContext db, ILogger<HabitReminderRepositories> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<HabitReminder>> GetForUserAsync(int userId, CancellationToken ct) =>
            await _db.HabitReminders
                .Include(r => r.Habit)
                .Where(r => r.Habit.UserId == userId)
                .ToListAsync(ct);

        public async Task<HabitReminder?> GetByIdForUserAsync(int reminderId, int userId, CancellationToken ct) =>
            await _db.HabitReminders
                .Include(r => r.Habit)
                .FirstOrDefaultAsync(r => r.Id == reminderId && r.Habit.UserId == userId, ct);

        public async Task<HabitReminder> CreateAsync(HabitReminder reminder, CancellationToken ct)
        {
            _db.HabitReminders.Add(reminder);
            await _db.SaveChangesAsync(ct);
            return reminder;
        }

        public async Task<bool> UpdateAsync(HabitReminder reminder, CancellationToken ct)
        {
            _db.HabitReminders.Update(reminder);
            return await _db.SaveChangesAsync(ct) > 0;
        }

        public async Task<bool> DeleteAsync(int reminderId, int userId, CancellationToken ct)
        {
            var reminder = await GetByIdForUserAsync(reminderId, userId, ct);
            if (reminder == null) return false;
            _db.HabitReminders.Remove(reminder);
            return await _db.SaveChangesAsync(ct) > 0;
        }

        public async Task<bool> HabitBelongsToUserAsync(int habitId, int userId, CancellationToken ct) =>
            await _db.Habits.AnyAsync(h => h.Id == habitId && h.UserId == userId, ct);

        public async Task<List<HabitReminder>> GetDueAsync(DateTime utcNow, CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(utcNow);
            var tod = utcNow.TimeOfDay;

            // Fire reminders whose ReminderTime is <= now within a 5-minute backlog (forgiving the loop interval),
            // and that haven't already fired today.
            var lowerBound = tod - TimeSpan.FromMinutes(5);

            return await _db.HabitReminders
                .Include(r => r.Habit)
                    .ThenInclude(h => h.User)
                .Where(r => r.IsEnabled
                    && r.ReminderTime <= tod
                    && r.ReminderTime >= lowerBound
                    && (r.LastFiredOn == null || r.LastFiredOn != today)
                    && !r.Habit.IsArchived)
                .ToListAsync(ct);
        }

        public async Task<bool> MarkFiredAsync(int reminderId, DateOnly utcDate, CancellationToken ct)
        {
            var reminder = await _db.HabitReminders.FindAsync(new object[] { reminderId }, ct);
            if (reminder == null) return false;
            reminder.LastFiredOn = utcDate;
            return await _db.SaveChangesAsync(ct) > 0;
        }
    }
}
