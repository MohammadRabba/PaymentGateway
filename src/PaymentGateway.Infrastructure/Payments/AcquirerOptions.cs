namespace PaymentGateway.Infrastructure.Payments;

/// <summary>
/// Configuration for the simulated acquirer. Supports deterministic failure modes so tests can
/// assert exact outcomes without relying on random behaviour. The seed is used by the Random
/// failure mode to make tests reproducible.
/// </summary>
public sealed class AcquirerOptions
{
    public const string SectionName = "Acquirer";

    /// <summary>
    /// Failure mode for the simulator. Default None = always succeeds. Tests inject a specific mode
    /// for deterministic assertions.
    /// </summary>
    public AcquirerFailureMode FailureMode { get; init; } = AcquirerFailureMode.None;

    /// <summary>
    /// Probability (0.0 to 1.0) of a failure when FailureMode=Random. Used together with Seed
    /// for deterministic reproduction.
    /// </summary>
    public double FailureProbability { get; init; } = 0.1;

    /// <summary>
    /// Simulated processing latency. Acquirer calls sleep this long before responding.
    /// </summary>
    public TimeSpan Latency { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Random seed for FailureMode=Random. Same seed + same paymentId sequence = same outcomes.
    /// </summary>
    public int Seed { get; init; } = 42;

    /// <summary>
    /// Timeout threshold. If Latency is set higher than this, the simulator throws TimeoutException.
    /// This lets tests simulate timeouts without configuring the HTTP client timeout separately.
    /// </summary>
    public TimeSpan TimeoutThreshold { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When true, the simulator tracks every authorization request by idempotency key so that
    /// GetAuthorizationStatusAsync returns the original outcome. Set to true in tests and
    /// production-like dev; can be false for pure unit tests that don't exercise recovery.
    /// </summary>
    public bool TrackAuthorizations { get; init; } = true;
}

public enum AcquirerFailureMode
{
    /// <summary>Always succeeds. Default.</summary>
    None = 0,

    /// <summary>Always declines with a permanent decline reason.</summary>
    Decline = 1,

    /// <summary>Throws HttpRequestException (simulating transient 5xx).</summary>
    Transient5xx = 2,

    /// <summary>Throws TaskCanceledException (simulating timeout).</summary>
    Timeout = 3,

    /// <summary>Returns Unknown outcome (acquirer lost the request).</summary>
    Unknown = 4,

    /// <summary>Random success/decline based on FailureProbability and Seed.</summary>
    Random = 5,
}
