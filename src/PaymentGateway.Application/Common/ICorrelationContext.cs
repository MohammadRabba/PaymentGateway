namespace PaymentGateway.Application.Common;

/// <summary>
/// Read-only correlation context. The value is set by the API middleware (from X-Correlation-ID
/// header or generated if missing) and propagated through the outbox to RabbitMQ and webhooks.
/// </ read-only here so services cannot accidentally clobber it.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>
    /// The correlation ID for the current request flow. Never null or empty once the middleware has run.
    /// </summary>
    string CurrentId { get; }
}
