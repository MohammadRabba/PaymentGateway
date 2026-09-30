using PaymentGateway.Infrastructure.Time;

namespace PaymentGateway.Api.Middleware;

/// <summary>
/// Reads the X-Correlation-ID header from the incoming request. If absent, generates a new GUID.
/// Sets the value in the CorrelationContext (AsyncLocal) so it flows through the entire request
/// (handlers, outbox, audit records). Adds the correlation ID to the response headers so the
/// client can track requests across the system.
/// </summary>
public sealed class CorrelationMiddleware
{
    public const string CorrelationHeader = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public CorrelationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(CorrelationHeader, out var headerValue)
            && !string.IsNullOrWhiteSpace(headerValue.ToString())
                ? headerValue.ToString()
                : Guid.NewGuid().ToString("N");

        CorrelationContext.Set(correlationId);

        // Add to response so the client can correlate the response back to the request.
        context.Response.Headers[CorrelationHeader] = correlationId;

        // Also stamp the trace identifier so ProblemDetails can include it.
        if (string.IsNullOrEmpty(context.TraceIdentifier) || context.TraceIdentifier == Guid.Empty.ToString())
        {
            context.TraceIdentifier = correlationId;
        }

        try
        {
            await _next(context);
        }
        finally
        {
            CorrelationContext.Clear();
        }
    }
}
