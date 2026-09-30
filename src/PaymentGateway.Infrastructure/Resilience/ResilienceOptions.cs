namespace PaymentGateway.Infrastructure.Resilience;

/// <summary>
/// Resilience configuration for outbound calls. Used by ResiliencePipelineFactory to build Polly v8
/// pipelines for the acquirer and webhook clients.
/// </summary>
public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public AcquirerResilienceOptions Acquirer { get; init; } = new();
    public WebhookResilienceOptions Webhook { get; init; } = new();
}

public sealed class AcquirerResilienceOptions
{
    /// <summary>Max retry attempts for transient acquirer failures.</summary>
    public int RetryAttempts { get; init; } = 5;

    /// <summary>Initial retry delay. Doubles on each attempt (capped at MaxDelay).</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Maximum retry delay (cap for exponential backoff).</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Per-request timeout (applied to the entire pipeline).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Circuit breaker: open after this many consecutive failures.</summary>
    public int CircuitBreakerFailureThreshold { get; init; } = 5;

    /// <summary>Circuit breaker: open duration.</summary>
    public TimeSpan CircuitBreakerOpenDuration { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed class WebhookResilienceOptions
{
    public int RetryAttempts { get; init; } = 5;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public int CircuitBreakerFailureThreshold { get; init; } = 10;
    public TimeSpan CircuitBreakerOpenDuration { get; init; } = TimeSpan.FromSeconds(60);
}
