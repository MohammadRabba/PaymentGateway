namespace PaymentGateway.Application.Options;

/// <summary>
/// Idempotency configuration. Redis is the fast coordination layer; SQL UNIQUE constraint is the
/// authoritative correctness boundary. If Redis is down, the SQL unique constraint still prevents
/// duplicate financial execution — performance degrades but correctness does not.
/// </summary>
public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>
    /// TTL for the Redis Processing state. If the processing request does not complete within this
    /// window, the Redis key expires and the SQL record becomes reclaimable.
    /// </summary>
    public TimeSpan ProcessingTtl { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long the SQL IdempotencyRecord is retained after completion. Used for replay of
    /// completed responses even after the Redis TTL has expired.
    /// </summary>
    public TimeSpan ExpiresAfter { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Maximum length of a client-supplied idempotency key. Longer keys are rejected.</summary>
    public int KeyMaxLength { get; init; } = 128;

    /// <summary>
    /// Time to wait before retrying a request that received 409 InProgress. Returned as Retry-After.
    /// </summary>
    public TimeSpan InProgressRetryAfter { get; init; } = TimeSpan.FromSeconds(2);
}
