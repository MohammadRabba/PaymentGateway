namespace PaymentGateway.Infrastructure.Workers;

/// <summary>
/// Background worker configuration. All workers are IHostedService implementations that poll
/// SQL on an interval. They back off gracefully on shutdown via CancellationToken.
/// </summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Workers";

    /// <summary>Interval at which the OutboxPublisherWorker polls for unprocessed messages.</summary>
    public TimeSpan OutboxPollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Number of outbox messages claimed per poll iteration. Bounded for memory safety.</summary>
    public int OutboxBatchSize { get; init; } = 50;

    /// <summary>Max attempts before an outbox message is poisoned.</summary>
    public int OutboxMaxAttempts { get; init; } = 10;

    /// <summary>Initial retry backoff for outbox messages. Doubles per attempt, capped.</summary>
    public TimeSpan OutboxInitialBackoff { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Max backoff for outbox message retries.</summary>
    public TimeSpan OutboxMaxBackoff { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Worker identifier. Used for claiming messages (allows multiple workers).</summary>
    public string WorkerId { get; init; } = Environment.MachineName;

    /// <summary>Interval at which the PendingPaymentRecoveryWorker scans for stuck payments.</summary>
    public TimeSpan RecoveryPollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Heartbeat age after which a Processing payment is considered stuck.</summary>
    public TimeSpan StuckPaymentThreshold { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Max recovery attempts before a stuck payment is force-failed.</summary>
    public int MaxRecoveryAttempts { get; init; } = 5;

    /// <summary>Interval at which the ReconciliationWorker runs (default: hourly).</summary>
    public TimeSpan ReconciliationInterval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Initial delay before workers start. Lets the system stabilise on startup.</summary>
    public TimeSpan StartupDelay { get; init; } = TimeSpan.FromSeconds(15);
}
