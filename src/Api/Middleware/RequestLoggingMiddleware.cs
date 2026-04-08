using System.Diagnostics;

namespace ProjectOS.Api.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        context.Response.Headers["X-Correlation-Id"] = correlationId;

        var sw = Stopwatch.StartNew();

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
            sw.Stop();

            _logger.LogInformation(
                "{Method} {Path} from {IP} => {StatusCode} in {Elapsed}ms",
                context.Request.Method,
                context.Request.Path,
                context.Connection.RemoteIpAddress,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds);
        }
    }
}
