using PaymentGateway.Application.Common;

namespace PaymentGateway.Infrastructure.Time;

/// <summary>
/// AsyncLocal-backed correlation context. The API middleware sets the correlation ID at the start
/// of a request from the X-Correlation-ID header (or generates one if missing). The value flows
/// through async/await calls without explicit parameter passing. The same correlation ID is
/// embedded in outbox messages, RabbitMQ headers, webhook payloads, and audit records.
/// </summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public string CurrentId => Current.Value ?? string.Empty;

    /// <summary>
    /// Set the correlation ID for the current async flow. Called by the correlation middleware
    /// at the start of a request. Throws if the value is null or empty.
    /// </summary>
    public static void Set(string correlationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(correlationId);
        Current.Value = correlationId;
    }

    /// <summary>
    /// Clear the correlation ID. Called by the middleware at the end of a request to avoid
    /// leaking the value into the next request on a pooled thread.
    /// </summary>
    public static void Clear()
    {
        Current.Value = null;
    }
}
