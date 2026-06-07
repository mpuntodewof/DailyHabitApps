using AtomicHabits.Data;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Tests;

/// <summary>
/// Builds an isolated in-memory AppDbContext per test. Each call uses a unique
/// database name so tests never share state.
///
/// Use this for pure model/CRUD tests that don't exercise transactions or raw-SQL
/// features. NOTE: the EF InMemory provider does NOT support transactions or
/// <c>ExecuteUpdate(Async)</c> — for those (e.g. recovery-code single-use, the
/// registration commit guard) use <see cref="SqliteTestDb"/> instead. The current
/// suite leans on SqliteTestDb for that reason; this factory is kept as the lighter
/// option for transaction-free tests (e.g. the Performance OS model tests).
/// </summary>
public static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
