using AtomicHabits.Data;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Tests;

/// <summary>
/// Builds an isolated in-memory AppDbContext per test. Each call uses a unique
/// database name so tests never share state.
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
