namespace ProjectOS.Api.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    private static readonly HashSet<string> PublicPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/health/detailed",
        "/api/gmail/ping"
    };

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Allow public paths and static files
        if (PublicPaths.Contains(path) || !path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var expectedKey = Environment.GetEnvironmentVariable("API_ACCESS_KEY") ?? "";

        // If no key configured, allow access (development mode)
        if (string.IsNullOrEmpty(expectedKey))
        {
            await _next(context);
            return;
        }

        var providedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(providedKey) || providedKey != expectedKey)
        {
            _logger.LogWarning("Unauthorized API request to {Path} from {IP}",
                path, context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Unauthorized. Provide valid X-Api-Key header.\"}");
            return;
        }

        await _next(context);
    }
}
