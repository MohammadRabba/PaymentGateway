namespace PaymentGateway.Application.Common;

/// <summary>
/// Redis distributed lock abstraction. Used for coordination only — never the correctness boundary.
/// The database (rowversion + serializable transaction) is the correctness boundary. If Redis is
/// unavailable, callers must still operate correctly without the lock (using optimistic retry).
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Try to acquire a lock. Returns null if the lock is held by another process.
    /// The handle releases the lock on dispose (with token ownership verification).
    /// </summary>
    Task<IDistributedLockHandle?> AcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken);
}

/// <summary>
/// Handle to a held distributed lock. Disposing releases the lock IF AND ONLY IF the
/// underlying token still matches this handle's token. This prevents a process from
/// releasing a lock it no longer owns (e.g., its TTL expired and another process took it).
/// </summary>
public interface IDistributedLockHandle : IAsyncDisposable
{
    string Key { get; }

    string Token { get; }

    /// <summary>
    /// Explicit release. Also called by DisposeAsync. Returns true if the lock was
    /// successfully released (token still matched), false if it had already expired
    /// or been taken by another process.
    /// </summary>
    Task<bool> ReleaseAsync();
}
