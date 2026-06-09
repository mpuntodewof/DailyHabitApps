using AtomicHabits.Authorization;
using AtomicHabits.Config;
using AtomicHabits.Data;
using AtomicHabits.Middleware;
using AtomicHabits.Models;
using AtomicHabits.Repositories;
using AtomicHabits.Service;
using AtomicHabits.Services;
using AtomicHabits.Validators;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Optional per-developer overrides. Loaded last so it wins over appsettings.json
// and appsettings.{Environment}.json. Gitignored — never committed.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterDtoValidator>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DbConn")));


#region DI Registration

#region DI Repositories
builder.Services.AddScoped<IUserRepositories, UserRepositories>();
builder.Services.AddScoped<IHabitRepositories, HabitRepositories>();
builder.Services.AddScoped<IHabitTrackingRepositories, HabitTrackingRepositories>();
builder.Services.AddScoped<IStreakRepositories, StreakRepositories>();
builder.Services.AddScoped<IDashboardRepositories, DashboardRepositories>();
builder.Services.AddScoped<IHabitReminderRepositories, HabitReminderRepositories>();
#endregion

#region DI Service
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IEmailSender, EmailService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IHabitTrackingService, HabitTrackingService>();
builder.Services.AddScoped<IHabitService, HabitService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IUserPreferencesService, UserPreferencesService>();
builder.Services.AddScoped<IHabitReminderService, HabitReminderService>();
builder.Services.AddScoped<ITwoFactorService, TwoFactorService>();
builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<IGoalFrameworkService, GoalFrameworkService>();
builder.Services.AddScoped<IHabitSkipService, HabitSkipService>();
builder.Services.AddScoped<IInsightService, InsightService>();
builder.Services.AddScoped<IWeeklyReportService, WeeklyReportService>();
builder.Services.AddScoped<IStripeGateway, StripeGateway>();
builder.Services.AddScoped<IBillingService, BillingService>();
#endregion

#region Hosted Services
builder.Services.AddHostedService<AtomicHabits.Scheduling.ReminderDispatcherService>();
#endregion

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

#endregion


#region ===== Options Binding =====

// JWT options: bind from "Jwt" section, then overlay env-var secret if present.
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .PostConfigure(opts =>
    {
        var envSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (!string.IsNullOrWhiteSpace(envSecret)) opts.Secret = envSecret;

        var envIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER");
        if (!string.IsNullOrWhiteSpace(envIssuer)) opts.Issuer = envIssuer;

        var envAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE");
        if (!string.IsNullOrWhiteSpace(envAudience)) opts.Audience = envAudience;
    })
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience),
              "JWT Issuer and Audience must be configured")
    .ValidateOnStart();

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.SectionName));

// SMTP options. Password is sourced from SMTP_PASSWORD env var so it never lives in config files.
builder.Services
    .AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .PostConfigure(opts =>
    {
        var envPassword = Environment.GetEnvironmentVariable("SMTP_PASSWORD");
        if (!string.IsNullOrWhiteSpace(envPassword)) opts.Password = envPassword;

        var envUsername = Environment.GetEnvironmentVariable("SMTP_USERNAME");
        if (!string.IsNullOrWhiteSpace(envUsername)) opts.Username = envUsername;
    });

#endregion


#region ===== JWT Authentication =====

bool isDesignTime = !builder.Environment.IsDevelopment()
    && AppDomain.CurrentDomain.FriendlyName.Contains("ef", StringComparison.OrdinalIgnoreCase);

if (!isDesignTime)
{
    var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
    var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
                    ?? jwtSection["Secret"];
    var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER")
                    ?? jwtSection["Issuer"];
    var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE")
                      ?? jwtSection["Audience"];

    if (string.IsNullOrWhiteSpace(jwtSecret) ||
        string.IsNullOrWhiteSpace(jwtIssuer) ||
        string.IsNullOrWhiteSpace(jwtAudience))
    {
        throw new InvalidOperationException(
            "JWT configuration is missing. Set JWT_SECRET (env var) and Jwt:Issuer / Jwt:Audience (config or env).");
    }

    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "username",
                RoleClaimType = "role"
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();
    });

    // Per-permission authorization (custom policy provider + handler).
    builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
    builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
    builder.Services.AddScoped<IAuthorizationHandler, SubscriptionAuthorizationHandler>();
}

#endregion


#region ===== Swagger Configuration =====

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "AtomicHabits API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter 'Bearer' [space] and then your valid token. Example: Bearer eyJhbGciOi...",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

#endregion


#region ===== CORS =====

var corsOrigins = builder.Configuration
    .GetSection(CorsOptions.SectionName)
    .Get<CorsOptions>()?.AllowedOrigins ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AtomicUI", policy =>
    {
        var p = policy.AllowAnyMethod().AllowAnyHeader().AllowCredentials();
        if (corsOrigins.Length > 0) p.WithOrigins(corsOrigins);
    });
});

#endregion


var app = builder.Build();

var stripeOpts = app.Services.GetRequiredService<IOptions<StripeOptions>>().Value;
if (!string.IsNullOrEmpty(stripeOpts.SecretKey))
    Stripe.StripeConfiguration.ApiKey = stripeOpts.SecretKey;

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!isDesignTime)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Loud, early warning if the database schema is behind the code. This is what would have
    // caught the 'Invalid object name UserTwoFactors' incident before it surfaced as a 500
    // on a user-facing request.
    try
    {
        var pending = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            startupLogger.LogWarning(
                "There are {Count} pending EF migration(s). Run `dotnet ef database update` before serving traffic. " +
                "Pending: {Pending}",
                pending.Count, string.Join(", ", pending));
        }
        else
        {
            startupLogger.LogInformation("Database schema is up to date.");
        }
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex,
            "Could not check pending migrations (DB likely unreachable). The API will start; " +
            "downstream DB calls will surface their own errors.");
    }

    try
    {
        await DbSeeder.SeedRolesAsync(dbContext);
        startupLogger.LogInformation("DbSeeder finished successfully.");
    }
    catch (Exception ex)
    {
        // The API still starts; downstream requests that hit the DB will fail with their own
        // (logged) exceptions, but at least the host is up so health probes and config can be
        // inspected. Re-run seeding by restarting once the DB is reachable.
        startupLogger.LogError(ex,
            "DbSeeder failed; the API will start without seeded roles/permissions. " +
            "Verify the DB is reachable and run migrations, then restart.");
    }
}

app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseCors("AtomicUI");

// HTTPS redirection only in non-Development. The "http" launch profile binds only
// http://localhost:5198, and forcing a redirect to a non-existent HTTPS listener
// would break local dev when the SPA points at the http endpoint.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed so WebApplicationFactory<Program> can boot the app in integration tests.
public partial class Program { }
