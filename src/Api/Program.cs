using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProjectOS.Api.Middleware;
using ProjectOS.Api.Services;
using ProjectOS.Infrastructure.Extensions;
using ProjectOS.Infrastructure.Persistence;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/projectos-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting ProjectOS API");

    var builder = WebApplication.CreateBuilder(args);

    // Serilog
    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .WriteTo.Console()
        .WriteTo.File("logs/projectos-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30));

    // Database - PostgreSQL
    var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? builder.Configuration.GetConnectionString("DefaultConnection");

    if (!string.IsNullOrEmpty(connectionString))
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Api");
                npgsql.EnableRetryOnFailure(3);
            }));
    }
    else
    {
        Log.Warning("No database connection string configured. Using in-memory database for development.");
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase("ProjectOS_Dev"));
    }

    // JWT Authentication
    var jwtSecret = builder.Configuration["Jwt:Secret"]
        ?? Environment.GetEnvironmentVariable("JWT_SECRET")
        ?? string.Empty;

    if (!string.IsNullOrEmpty(jwtSecret))
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ValidateIssuer = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ProjectOS",
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };
            });
    }
    else
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
    }

    builder.Services.AddAuthorization();

    // Infrastructure DI
    builder.Services.AddInfrastructure(builder.Configuration);

    // Services
    builder.Services.AddSingleton<JwtService>();

    // Health Checks
    builder.Services.AddHealthChecks();

    // Controllers
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
    });

    // Rate Limiting
    builder.Services.AddRateLimiter(options =>
    {
        options.AddPolicy("api", _ => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: "api",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1)
            }));

        options.AddPolicy("login", _ => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: "login",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));

        options.RejectionStatusCode = 429;
    });

    var app = builder.Build();

    // Middleware pipeline
    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();

    app.UseSerilogRequestLogging();
    app.UseRateLimiter();
    app.UseCors();

    app.UseAuthentication();
    app.UseAuthorization();

    // Health endpoints
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate = _ => false
    });

    app.MapHealthChecks("/health/detailed");

    app.MapControllers();

    // Auto-migrate on startup (non-production)
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational())
        {
            await db.Database.MigrateAsync();
        }
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
