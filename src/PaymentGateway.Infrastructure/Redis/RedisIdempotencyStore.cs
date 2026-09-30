using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Idempotency;
using PaymentGateway.Domain.Enums;
using StackExchange.Redis;

namespace PaymentGateway.Infrastructure.Redis;

/// <summary>
/// Redis implementation of IIdempotencyStore. The fast coordination layer for idempotency.
/// All operations catch Redis exceptions and rethrow as RedisUnavailableException so that
/// IdempotencyService can fall through to the SQL authoritative path.
///
/// Atomic operations used:
///   - GET (read existing entry)
///   - SET NX PX (atomic claim — fails if key exists)
///   - SET PX (unconditional overwrite — update state)
///   - DEL (delete on failure path)
/// </summary>
public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly RedisConnection _connection;
    private readonly ILogger<RedisIdempotencyStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public RedisIdempotencyStore(RedisConnection connection, ILogger<RedisIdempotencyStore> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task<IdempotencyStoreEntry?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var prefixedKey = _connection.PrefixKey(key);
            var db = _connection.GetDatabase();
            var value = await db.StringGetAsync(prefixedKey);
            if (!value.HasValue)
            {
                return null;
            }

            return Deserialize(value.ToString());
        }
        catch (RedisUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GetAsync failed for key {Key}", key);
            throw new RedisUnavailableException($"Redis GetAsync failed for key {key}.", ex);
        }
    }

    public async Task<bool> TrySetAsync(string key, IdempotencyStoreEntry entry, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            var prefixedKey = _connection.PrefixKey(key);
            var db = _connection.GetDatabase();
            var json = Serialize(entry);
            var expiry = ttl;

            // SET NX PX: set only if not exists, with TTL. Returns true if set.
            var wasSet = await db.StringSetAsync(prefixedKey, json, expiry, When.NotExists);
            return wasSet;
        }
        catch (RedisUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis TrySetAsync failed for key {Key}", key);
            throw new RedisUnavailableException($"Redis TrySetAsync failed for key {key}.", ex);
        }
    }

    public async Task SetAsync(string key, IdempotencyStoreEntry entry, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            var prefixedKey = _connection.PrefixKey(key);
            var db = _connection.GetDatabase();
            var json = Serialize(entry);

            // SET PX: unconditional set with TTL. Overwrites existing value.
            await db.StringSetAsync(prefixedKey, json, ttl);
        }
        catch (RedisUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SetAsync failed for key {Key}", key);
            throw new RedisUnavailableException($"Redis SetAsync failed for key {key}.", ex);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var prefixedKey = _connection.PrefixKey(key);
            var db = _connection.GetDatabase();
            await db.KeyDeleteAsync(prefixedKey);
        }
        catch (RedisUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis DeleteAsync failed for key {Key}", key);
            throw new RedisUnavailableException($"Redis DeleteAsync failed for key {key}.", ex);
        }
    }

    private static string Serialize(IdempotencyStoreEntry entry)
    {
        return JsonSerializer.Serialize(new
        {
            state = (int)entry.State,
            requestHash = entry.RequestHash,
            statusCode = entry.StatusCode,
            responsePayload = entry.ResponsePayload,
            createdAt = entry.CreatedAt.ToString("O"),
            completedAt = entry.CompletedAt?.ToString("O"),
        }, JsonOptions);
    }

    private static IdempotencyStoreEntry? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("state", out var stateEl) || !stateEl.TryGetInt32(out var stateInt))
        {
            return null;
        }

        if (!Enum.IsDefined((IdempotencyState)stateInt))
        {
            return null;
        }

        var requestHash = root.TryGetProperty("requestHash", out var hashEl) ? hashEl.GetString() ?? string.Empty : string.Empty;
        int? statusCode = root.TryGetProperty("statusCode", out var scEl) && scEl.ValueKind == JsonValueKind.Number
            ? scEl.GetInt32()
            : null;
        string? responsePayload = root.TryGetProperty("responsePayload", out var rpEl) ? rpEl.GetString() : null;
        DateTimeOffset createdAt = root.TryGetProperty("createdAt", out var caEl) && DateTimeOffset.TryParse(caEl.GetString(), out var ca)
            ? ca
            : DateTimeOffset.UtcNow;
        DateTimeOffset? completedAt = root.TryGetProperty("completedAt", out var cmpEl) && cmpEl.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(cmpEl.GetString(), out var cmp)
                ? cmp
                : null;

        return new IdempotencyStoreEntry
        {
            State = (IdempotencyState)stateInt,
            RequestHash = requestHash,
            StatusCode = statusCode,
            ResponsePayload = responsePayload,
            CreatedAt = createdAt,
            CompletedAt = completedAt,
        };
    }
}
