using System;
using System.Collections.Generic;
using System.Linq;
using AtomicHabits.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AtomicHabits.Tests.Integration;

/// <summary>
/// Boots the real API in-process with the database swapped to a per-factory SQLite
/// in-memory connection, and JWT config supplied so auth works without env vars.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public ApiFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "integration-signing-key-at-least-32-bytes-0123456789",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove ALL EF Core / SqlServer descriptors so the SqlServer provider
            // registered in Program.cs doesn't conflict with SQLite.
            // EF Core registers provider services under internal types; the reliable way
            // is to remove every descriptor whose implementation or service type comes
            // from the SqlServer or EntityFrameworkCore assemblies, plus AppDbContext itself.
            var efAssemblyPrefixes = new[]
            {
                "Microsoft.EntityFrameworkCore",
                "AtomicHabits.Data.AppDbContext"
            };

            var toRemove = services
                .Where(d =>
                {
                    var typeName = d.ServiceType.FullName ?? string.Empty;
                    var implTypeName = d.ImplementationType?.FullName
                        ?? d.ImplementationInstance?.GetType().FullName
                        ?? string.Empty;
                    return d.ServiceType == typeof(AppDbContext)
                        || d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                        || d.ServiceType == typeof(DbContextOptions)
                        || typeName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                        || implTypeName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal);
                })
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
