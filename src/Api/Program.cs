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

    // Bind to PORT env var (Render provides this)
    var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

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

    // Fail-fast: require database in non-Development environments
    if (string.IsNullOrEmpty(connectionString) && !builder.Environment.IsDevelopment())
        throw new InvalidOperationException("DATABASE_URL or ConnectionStrings:DefaultConnection must be configured for production");

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
    var allowedOrigin = builder.Configuration["AllowedOrigin"]
        ?? Environment.GetEnvironmentVariable("ALLOWED_ORIGIN");

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (!string.IsNullOrEmpty(allowedOrigin))
            {
                policy.WithOrigins(allowedOrigin.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            }
            else
            {
                // Development fallback only
                policy.WithOrigins("http://localhost:5000", "http://localhost:5001", "https://localhost:5001")
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            }
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

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseAuthentication();
    app.UseAuthorization();

    // Health endpoints
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate = _ => false
    });

    app.MapHealthChecks("/health/detailed");

    app.MapControllers();

    // Auto-migrate on startup — handle corrupted migration state
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Check if critical tables actually exist despite migration history
        var tablesExist = false;
        try
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'Projects')";
            var result = await cmd.ExecuteScalarAsync();
            tablesExist = result is true or (bool)true;
            if (result is bool b) tablesExist = b;
        }
        catch { /* connection issue — will handle below */ }

        if (!tablesExist)
        {
            Log.Warning("Critical tables missing — database reset triggered");
            db.Database.EnsureDeleted();
            Log.Information("Old database state cleared");
            db.Database.EnsureCreated();
            Log.Information("Database created from scratch — all tables ready");
        }
        else
        {
            Log.Information("Tables exist — checking for schema updates...");

            // Add missing columns directly (safe for EnsureCreated DBs that can't use Migrate)
            try
            {
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

                var alterStatements = new[]
                {
                    "ALTER TABLE \"EmailMessages\" ADD COLUMN IF NOT EXISTS \"AiSummary\" character varying(2000)",
                    "ALTER TABLE \"EmailMessages\" ADD COLUMN IF NOT EXISTS \"AiSuggestedReply\" character varying(2000)",
                    "ALTER TABLE \"EmailMessages\" ADD COLUMN IF NOT EXISTS \"AiCategory\" character varying(50)",
                    "ALTER TABLE \"EmailMessages\" ADD COLUMN IF NOT EXISTS \"AiPriority\" character varying(20)"
                };

                foreach (var sql in alterStatements)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    await cmd.ExecuteNonQueryAsync();
                }

                Log.Information("Schema update check complete — AI columns ensured");
            }
            catch (Exception schemaEx)
            {
                Log.Warning(schemaEx, "Schema update failed — new columns may be missing");
            }
        }
    }
    catch (Exception migrationEx)
    {
        Log.Error(migrationEx, "Database setup failed — app will continue but may have errors");
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
