namespace PaymentGateway.Infrastructure.Webhooks;

/// <summary>
/// Webhook delivery configuration. Used by WebhookDeliveryService for HTTP timeouts, retry policy,
/// and replay tolerance. The webhook secret is per-merchant (stored in Merchant.WebhookSecret)
/// and never logged.
/// </summary>
public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>HTTP timeout for each webhook delivery attempt.</summary>
    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum number of delivery attempts before the message is moved to the DLQ.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Initial retry delay. Doubles on each failure (capped at MaxRetryDelay).</summary>
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Maximum retry delay (cap for exponential backoff).</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Replay protection tolerance in seconds. Webhook timestamps older than now - tolerance are rejected by the merchant.</summary>
    public long ReplayToleranceSeconds { get; init; } = 300;

    /// <summary>Permanent 4xx status codes that should NOT be retried (immediately dead-lettered).</summary>
    public int[] PermanentFailureCodes { get; init; } = { 400, 401, 403, 404, 410, 422 };

    /// <summary>Retryable status codes that trigger retry with backoff.</summary>
    public int[] RetryableCodes { get; init; } = { 408, 425, 429, 500, 502, 503, 504 };
}
