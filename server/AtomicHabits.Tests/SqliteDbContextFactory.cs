using AtomicHabits.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AtomicHabits.Tests;

/// <summary>
/// Builds an AppDbContext backed by a real SQLite in-memory database. Unlike the EF
/// InMemory provider, SQLite honors transactions — required for tests that assert
/// commit/rollback behavior (e.g. registration). Hold the returned handle for the test's
/// lifetime and Dispose it; closing the connection drops the database.
/// </summary>
public sealed class SqliteTestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public AppDbContext Context { get; }

    public SqliteTestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new AppDbContext(options);
        Context.Database.EnsureCreated();
    }

    // A fresh context over the SAME connection/database — use to assert persistence
    // across a unit boundary without the first context's change-tracker masking a rollback.
    // The caller owns the returned context and must dispose it (e.g. `using var v = db.NewContext();`).
    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options);
    }

    // Null-safe so a double Dispose() is harmless.
    public void Dispose()
    {
        Context?.Dispose();
        _connection?.Dispose();
    }
}
