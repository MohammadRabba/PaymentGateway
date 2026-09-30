using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Application.Idempotency;

/// <summary>
/// Fast coordination cache (typically Redis) for idempotency state. The SQL IdempotencyRecord
/// table is the authoritative correctness boundary; this store is an optimisation to avoid hitting
/// the database for the common replay/concurrent-duplicate path. If this store is unavailable,
/// IdempotencyService falls back to SQL-only mode (slower, still correct).
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Get the current entry for a key, or null if absent.</summary>
    Task<IdempotencyStoreEntry?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically set the entry IF the key is absent (Redis SET NX PX semantics).
    /// Returns true if the entry was set (acquired), false if the key already exists.
    /// </summary>
    Task<bool> TrySetAsync(string key, IdempotencyStoreEntry entry, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>
    /// Unconditionally set/overwrite the entry (Redis SET PX semantics). Used to update state
    /// from Processing to Completed/Failed after the work finishes.
    /// </summary>
    Task SetAsync(string key, IdempotencyStoreEntry entry, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>Delete the key. Used for cleanup on failure paths.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// Typed entry stored in the idempotency cache. Serialised to JSON by the implementation.
/// </summary>
public sealed record IdempotencyStoreEntry
{
    public required IdempotencyState State { get; init; }

    public required string RequestHash { get; init; }

    public int? StatusCode { get; init; }

    public string? ResponsePayload { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}
