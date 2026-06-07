using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AtomicHabits.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AtomicHabits.Tests.Integration;

/// <summary>
/// Boots the real API in-process with the database swapped to a per-factory SQLite
/// in-memory connection, and JWT config supplied so auth works without env vars.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    // JWT values used consistently for token issuance (IOptions&lt;JwtOptions&gt;) and
    // for the bearer middleware validation parameters. Program.cs reads the middleware
    // config from configuration at startup time (before ConfigureAppConfiguration runs),
    // so we patch the bearer options via PostConfigure instead of relying on the config
    // pipeline alone.
    internal const string TestJwtSecret   = "integration-signing-key-at-least-32-bytes-0123456789";
    internal const string TestJwtIssuer   = "TestIssuer";
    internal const string TestJwtAudience = "TestAudience";

    private readonly SqliteConnection _connection;

    public ApiFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Supply JWT values via configuration so that IOptions<JwtOptions> (used by
        // TokenService for token signing) picks up the test values.
        // NOTE: Program.cs reads the bearer-middleware ValidIssuer / ValidAudience directly
        // from builder.Configuration before ConfigureAppConfiguration callbacks fire, so
        // those reads may see null. We correct that below via PostConfigure.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]   = TestJwtSecret,
                ["Jwt:Issuer"]   = TestJwtIssuer,
                ["Jwt:Audience"] = TestJwtAudience,
            });
        });

        builder.ConfigureServices(services =>
        {
            // ----------------------------------------------------------------
            // JWT Bearer middleware fix:
            // Program.cs reads ValidIssuer / ValidAudience / IssuerSigningKey
            // directly from IConfiguration at build time, before the test's
            // ConfigureAppConfiguration callback runs.  We override them here,
            // where ConfigureServices always runs after the app's service
            // registrations, so our PostConfigure wins.
            // ----------------------------------------------------------------
            // Resolve the actual signing secret: mirror the same priority chain that
            // TokenService uses (env var wins over test config) so the bearer middleware
            // validates with exactly the same key that signs the tokens.
            var rawSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? TestJwtSecret;
            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(rawSecret));
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
            {
                opts.TokenValidationParameters.ValidIssuer              = TestJwtIssuer;
                opts.TokenValidationParameters.ValidAudience            = TestJwtAudience;
                opts.TokenValidationParameters.IssuerSigningKey         = signingKey;
                opts.TokenValidationParameters.ValidateIssuer           = true;
                opts.TokenValidationParameters.ValidateAudience         = true;
                opts.TokenValidationParameters.ValidateIssuerSigningKey = true;
                opts.TokenValidationParameters.ValidateLifetime         = true;
                opts.TokenValidationParameters.ClockSkew                = TimeSpan.Zero;
            });

            // ----------------------------------------------------------------
            // Remove ALL EF Core / SqlServer descriptors so the SqlServer provider
            // registered in Program.cs doesn't conflict with SQLite.
            // ----------------------------------------------------------------
            var toRemove = services
                .Where(d =>
                {
                    var typeName    = d.ServiceType.FullName ?? string.Empty;
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
