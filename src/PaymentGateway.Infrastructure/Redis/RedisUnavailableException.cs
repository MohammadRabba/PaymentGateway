namespace PaymentGateway.Infrastructure.Redis;

/// <summary>
/// Thrown when Redis is unavailable or returns an error. Caught by IdempotencyService to fall back
/// to SQL-only mode. Redis being down must NEVER corrupt financial state — the SQL UNIQUE constraint
/// on (MerchantId, Operation, Key) is the authoritative correctness boundary.
/// </summary>
public sealed class RedisUnavailableException : Exception
{
    public RedisUnavailableException(string message)
        : base(message)
    {
    }

    public RedisUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
