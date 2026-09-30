namespace PaymentGateway.Infrastructure.Redis;

/// <summary>
/// Redis connection configuration. Redis is a coordination layer (idempotency cache, distributed
/// locks) — never the authoritative financial store. If Redis is unavailable, the system falls
/// back to SQL-only behaviour (slower, still correct).
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>StackExchange.Redis connection string. e.g. "localhost:6379,abortConnect=false".</summary>
    public string ConnectionString { get; init; } = "localhost:6379,abortConnect=false";

    /// <summary>Instance prefix for all keys. Useful for multi-tenant or multi-env shared Redis.</summary>
    public string InstanceName { get; init; } = "payment-gateway";

    /// <summary>Default TTL for idempotency entries.</summary>
    public TimeSpan DefaultTtl { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Lock TTL. Bounded — never permanent. Renewed by the holder while the work is in progress.</summary>
    public TimeSpan LockTtl { get; init; } = TimeSpan.FromSeconds(30);
}
