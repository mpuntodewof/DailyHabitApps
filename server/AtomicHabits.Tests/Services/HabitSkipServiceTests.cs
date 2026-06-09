using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AtomicHabits.Models;
using AtomicHabits.Models.DTO;
using AtomicHabits.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtomicHabits.Tests.Services;

public class HabitSkipServiceTests
{
    private static HabitSkipService NewService(AtomicHabits.Data.AppDbContext db) =>
        new HabitSkipService(db, NullLogger<HabitSkipService>.Instance);

    private static async Task<int> SeedHabit(AtomicHabits.Data.AppDbContext db, int userId)
    {
        var h = new Habit { UserId = userId, Name = "Gym", Frequency = "Daily" };
        db.Habits.Add(h);
        await db.SaveChangesAsync();
        return h.Id;
    }

    [Fact]
    public async Task Create_persists_skip_with_reason_for_owned_habit()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var habitId = await SeedHabit(db, 1);

        var res = await svc.CreateAsync(1, new HabitSkipCreateDto
        {
            HabitId = habitId, Date = new DateOnly(2026, 6, 7), Reason = "LowEnergy"
        }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        var saved = db.HabitSkips.Single();
        saved.Reason.Should().Be(SkipReason.LowEnergy);
        saved.Date.Should().Be(new DateOnly(2026, 6, 7));
        saved.UserId.Should().Be(1);
    }

    [Fact]
    public async Task Create_for_foreign_habit_is_rejected()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var habitId = await SeedHabit(db, 2); // owned by user 2

        var res = await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Reason = "Busy" }, CancellationToken.None);

        res.IsSuccess.Should().BeFalse();
        db.HabitSkips.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_twice_same_day_updates_reason_not_duplicates()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var habitId = await SeedHabit(db, 1);
        var date = new DateOnly(2026, 6, 7);

        await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Date = date, Reason = "Busy" }, CancellationToken.None);
        await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Date = date, Reason = "Forgot" }, CancellationToken.None);

        db.HabitSkips.Should().HaveCount(1);
        db.HabitSkips.Single().Reason.Should().Be(SkipReason.Forgot);
    }

    [Fact]
    public async Task Create_with_invalid_reason_is_rejected()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var habitId = await SeedHabit(db, 1);

        var res = await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Reason = "Nonsense" }, CancellationToken.None);
        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_returns_only_owners_skips_for_habit_newest_first()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var habitId = await SeedHabit(db, 1);
        await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Date = new DateOnly(2026, 6, 1), Reason = "Busy" }, CancellationToken.None);
        await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Date = new DateOnly(2026, 6, 5), Reason = "Forgot" }, CancellationToken.None);

        var res = await svc.ListForHabitAsync(1, habitId, CancellationToken.None);
        var items = ((IEnumerable<HabitSkipDto>)res.Result!).ToList();
        items.Should().HaveCount(2);
        items.First().Date.Should().Be(new DateOnly(2026, 6, 5)); // newest first

        var foreign = await svc.ListForHabitAsync(2, habitId, CancellationToken.None);
        foreign.IsSuccess.Should().BeFalse(); // not their habit
    }

    [Fact]
    public async Task Delete_removes_owned_skip()
    {
        using var db = TestDbContextFactory.Create();
        var svc = NewService(db);
        var habitId = await SeedHabit(db, 1);
        var create = await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Reason = "Busy" }, CancellationToken.None);
        var skipId = ((HabitSkipDto)create.Result!).Id;

        var del = await svc.DeleteAsync(1, skipId, CancellationToken.None);
        del.IsSuccess.Should().BeTrue();
        db.HabitSkips.Should().BeEmpty();

        // deleting a non-owned / unknown skip -> NotFound
        var create2 = await svc.CreateAsync(1, new HabitSkipCreateDto { HabitId = habitId, Reason = "Busy" }, CancellationToken.None);
        var skip2 = ((HabitSkipDto)create2.Result!).Id;
        var delForeign = await svc.DeleteAsync(2, skip2, CancellationToken.None);
        delForeign.IsSuccess.Should().BeFalse();
        db.HabitSkips.Should().HaveCount(1);
    }
}
