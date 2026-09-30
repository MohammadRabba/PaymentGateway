using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Common;
using StackExchange.Redis;

namespace PaymentGateway.Infrastructure.Redis;

/// <summary>
/// Redis distributed lock implementing the Fencing Token / Redlock-spirit pattern.
///
/// Acquisition: SET key token NX PX ttl
/// Release: Lua CAS — only delete if stored value equals our token (ownership verification).
///
/// The lock is a coordination mechanism only — it reduces wasted work on concurrent duplicate
/// requests. It is NEVER the correctness boundary: even if the lock is lost or skipped, the SQL
/// UNIQUE constraint on (MerchantId, Operation, Key) and the rowversion on Payment enforce
/// correctness. If Redis is down, callers proceed without the lock.
/// </summary>
public sealed class RedisDistributedLock : IDistributedLock
{
    private readonly RedisConnection _connection;
    private readonly RedisOptions _options;
    private readonly ILogger<RedisDistributedLock> _logger;

    /// <summary>
    /// Lua script for ownership-checked release. KEYS[1] = lock key; ARGV[1] = owner token.
    /// Returns 1 if deleted (token matched), 0 otherwise. Atomic — no race between GET and DEL.
    /// </summary>
    private const string ReleaseScript = @"
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        else
            return 0
        end
    ";

    public RedisDistributedLock(
        RedisConnection connection,
        IOptions<RedisOptions> options,
        ILogger<RedisDistributedLock> logger)
    {
        _connection = connection;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IDistributedLockHandle?> AcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            var prefixedKey = _connection.PrefixKey(key);
            var token = Guid.NewGuid().ToString("N");
            var db = _connection.GetDatabase();

            // SET NX PX: atomic acquire. Returns true if acquired.
            var acquired = await db.StringSetAsync(prefixedKey, token, ttl, When.NotExists);
            if (!acquired)
            {
                return null;
            }

            return new RedisLockHandle(db, prefixedKey, token, _logger);
        }
        catch (RedisUnavailableException)
        {
            // Redis down — caller proceeds without the lock. SQL constraint still enforces correctness.
            _logger.LogWarning("Redis unavailable; distributed lock skipped for key {Key}.", key);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis lock acquire failed for key {Key}; proceeding without lock.", key);
            return null;
        }
    }

    private sealed class RedisLockHandle : IDistributedLockHandle
    {
        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _token;
        private readonly ILogger _logger;
        private int _released; // 0 = not released, 1 = released (Interlocked)

        public RedisLockHandle(IDatabase db, string key, string token, ILogger logger)
        {
            _db = db;
            _key = key;
            _token = token;
            _logger = logger;
        }

        public string Key => _key;
        public string Token => _token;

        public async Task<bool> ReleaseAsync()
        {
            // Prevent double-release.
            if (Interlocked.Exchange(ref _released, 1) == 1)
            {
                return true;
            }

            try
            {
                var result = (long)(await _db.ScriptEvaluateAsync(ReleaseScript, new RedisKey[] { _key }, new RedisValue[] { _token }));
                if (result == 1)
                {
                    return true;
                }

                _logger.LogWarning(
                    "Distributed lock {Key} could not be released — TTL expired or token mismatch. " +
                    "Another process may have taken it. This is safe: the SQL constraint remains authoritative.",
                    _key);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release distributed lock {Key}. It will expire by TTL.", _key);
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync();
        }
    }
}
