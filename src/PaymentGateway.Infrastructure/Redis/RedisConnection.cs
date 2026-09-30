using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace PaymentGateway.Infrastructure.Redis;

/// <summary>
/// Wraps the StackExchange.Redis ConnectionMultiplexer. The multiplexer is a singleton — it
/// internally reconnects on failure and is safe to share across requests. Exposes a typed
/// database accessor with the configured instance prefix applied.
/// </summary>
public sealed class RedisConnection : IAsyncDisposable
{
    private readonly ConnectionMultiplexer _multiplexer;
    private readonly RedisOptions _options;
    private readonly ILogger<RedisConnection> _logger;

    public RedisConnection(IOptions<RedisOptions> options, ILogger<RedisConnection> logger)
    {
        _options = options.Value;
        _logger = logger;

        try
        {
            _multiplexer = ConnectionMultiplexer.Connect(_options.ConnectionString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Redis at {ConnectionString}", _options.ConnectionString);
            throw new RedisUnavailableException("Failed to connect to Redis.", ex);
        }
    }

    /// <summary>
    /// Get a typed Redis database. Apply the instance prefix to all keys.
    /// </summary>
    public IDatabase GetDatabase()
    {
        try
        {
            return _multiplexer.GetDatabase();
        }
        catch (Exception ex)
        {
            throw new RedisUnavailableException("Failed to get Redis database.", ex);
        }
    }

    /// <summary>
    /// Apply the instance prefix to a key. Returns "{prefix}:{key}".
    /// </summary>
    public string PrefixKey(string key) => $"{_options.InstanceName}:{key}";

    public ValueTask DisposeAsync()
    {
        return _multiplexer.DisposeAsync();
    }
}
