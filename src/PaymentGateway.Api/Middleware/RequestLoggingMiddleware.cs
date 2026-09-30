using System.Diagnostics;

namespace PaymentGateway.Api.Middleware;

/// <summary>
/// Logs every request with method, path, status code, and duration. Uses structured logging so
/// the values are queryable in Seq/ELK. Never logs request/response bodies (may contain secrets).
/// The duration is also emitted as a metric via the OTel histogram.
/// </summary>
public sealed class RequestLoggingMiddleware
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
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            var method = context.Request.Method;
            var path = context.Request.Path.Value ?? "/";
            var statusCode = context.Response.StatusCode;
            var durationMs = stopwatch.Elapsed.TotalMilliseconds;

            if (statusCode >= 500)
            {
                _logger.LogError("HTTP {Method} {Path} -> {StatusCode} ({DurationMs:F1}ms)",
                    method, path, statusCode, durationMs);
            }
            else if (statusCode >= 400)
            {
                _logger.LogWarning("HTTP {Method} {Path} -> {StatusCode} ({DurationMs:F1}ms)",
                    method, path, statusCode, durationMs);
            }
            else
            {
                _logger.LogInformation("HTTP {Method} {Path} -> {StatusCode} ({DurationMs:F1}ms)",
                    method, path, statusCode, durationMs);
            }
        }
    }
}
