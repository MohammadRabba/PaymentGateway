using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Exceptions;
using PaymentGateway.Application.Options;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Application.Idempotency;

/// <summary>
/// Coordinates Redis (fast path) and SQL (authoritative) to implement first-class idempotency.
///
/// State machine: Processing → Completed | Failed.
///
/// Flow:
///  1. Try Redis GET. If Completed with matching hash → Replay. If Processing → InProgress.
///  2. If Redis miss (or Redis down), try Redis SET NX. If acquired, proceed to SQL.
///  3. Check SQL IdempotencyRecord. If exists with Completed state → Replay (and backfill Redis).
///     If exists with Processing state → InProgress (or reclaim if stale). If not exists → INSERT.
///  4. SQL INSERT is atomic via UNIQUE(MerchantId, Operation, Key) constraint — concurrent inserts
///     race; the loser gets a unique-violation and returns InProgress.
///
/// Redis is the fast coordination layer; the SQL unique constraint is the correctness boundary.
/// If Redis is unavailable, correctness is preserved (slower, no fast NX check).
/// </summary>
public sealed class IdempotencyService
{
    private readonly IPaymentGatewayDbContext _dbContext;
    private readonly IIdempotencyStore _store;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<IdempotencyService> _logger;

    public IdempotencyService(
        IPaymentGatewayDbContext dbContext,
        IIdempotencyStore store,
        IClock clock,
        IIdGenerator idGenerator,
        IOptions<IdempotencyOptions> options,
        ILogger<IdempotencyService> logger)
    {
        _dbContext = dbContext;
        _store = store;
        _clock = clock;
        _idGenerator = idGenerator;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Check and claim an idempotency slot. Returns Proceed (with the tracked IdempotencyRecord)
    /// if the caller should execute the work, or InProgress/Replay/Reuse if the caller should
    /// short-circuit with the appropriate HTTP response.
    /// </summary>
    public async Task<IdempotencyCheckResult> CheckAsync(
        Guid merchantId,
        OperationType operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var scopeKey = BuildScopeKey(merchantId, operation, key);

        // Step 1: Try Redis fast path.
        var redisResult = await TryRedisFastPathAsync(scopeKey, requestHash, cancellationToken);
        if (redisResult is not null)
        {
            return redisResult;
        }

        // Step 2: Redis miss or down — check SQL authoritative.
        return await CheckSqlAsync(merchantId, operation, key, requestHash, scopeKey, cancellationToken);
    }

    /// <summary>
    /// Backfill the Redis cache after a SQL hit. Best-effort; if Redis is down, ignore.
    /// </summary>
    public async Task BackfillRedisAsync(
        string scopeKey,
        IdempotencyStoreEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            await _store.SetAsync(scopeKey, entry, _options.ExpiresAfter, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis backfill failed for key {ScopeKey}; SQL remains authoritative.", scopeKey);
        }
    }

    /// <summary>
    /// Update the Redis cache to Completed state after the work transaction commits. Best-effort.
    /// The SQL IdempotencyRecord has already been updated inside the work transaction by the handler
    /// (via the Record.Complete method on the tracked entity).
    /// </summary>
    public async Task CompleteRedisAsync(
        string scopeKey,
        string requestHash,
        int statusCode,
        string responsePayload,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = new IdempotencyStoreEntry
            {
                State = IdempotencyState.Completed,
                RequestHash = requestHash,
                StatusCode = statusCode,
                ResponsePayload = responsePayload,
                CreatedAt = _clock.UtcNow,
                CompletedAt = _clock.UtcNow,
            };
            await _store.SetAsync(scopeKey, entry, _options.ExpiresAfter, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis completion backfill failed for key {ScopeKey}; SQL remains authoritative.", scopeKey);
        }
    }

    /// <summary>
    /// Mark a SQL IdempotencyRecord as Failed in a separate transaction. Used when the work
    /// transaction itself fails (e.g., domain exception). Best-effort Redis cleanup.
    /// </summary>
    public async Task FailAsync(
        Guid idempotencyRecordId,
        string scopeKey,
        int statusCode,
        string responsePayload,
        CancellationToken cancellationToken)
    {
        try
        {
            var record = await _dbContext.IdempotencyRecords
                .FirstOrDefaultAsync(r => r.Id == idempotencyRecordId, cancellationToken);

            if (record is not null && record.State == IdempotencyState.Processing)
            {
                record.Fail(statusCode, responsePayload, _clock.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark IdempotencyRecord {RecordId} as Failed.", idempotencyRecordId);
            // Don't rethrow — the original work exception should propagate, not this cleanup failure.
        }

        try
        {
            var entry = new IdempotencyStoreEntry
            {
                State = IdempotencyState.Failed,
                RequestHash = string.Empty,
                StatusCode = statusCode,
                ResponsePayload = responsePayload,
                CreatedAt = _clock.UtcNow,
                CompletedAt = _clock.UtcNow,
            };
            await _store.SetAsync(scopeKey, entry, _options.ExpiresAfter, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis fail backfill failed for key {ScopeKey}; SQL remains authoritative.", scopeKey);
        }
    }

    private async Task<IdempotencyCheckResult?> TryRedisFastPathAsync(
        string scopeKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _store.GetAsync(scopeKey, cancellationToken);
            if (existing is null)
            {
                // Redis miss — try to claim the slot atomically.
                var claimEntry = new IdempotencyStoreEntry
                {
                    State = IdempotencyState.Processing,
                    RequestHash = requestHash,
                    CreatedAt = _clock.UtcNow,
                };

                var acquired = await _store.TrySetAsync(scopeKey, claimEntry, _options.ProcessingTtl, cancellationToken);
                if (!acquired)
                {
                    // Another process beat us — re-read to determine its state.
                    var reRead = await _store.GetAsync(scopeKey, cancellationToken);
                    if (reRead is not null)
                    {
                        return ClassifyExistingEntry(reRead, requestHash);
                    }
                    // Extremely rare: key disappeared between TrySet and Get. Fall through to SQL.
                }

                return null; // Proceed to SQL authoritative check.
            }

            return ClassifyExistingEntry(existing, requestHash);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis fast-path failed for key {ScopeKey}; falling back to SQL-only.", scopeKey);
            return null; // Fall through to SQL.
        }
    }

    private IdempotencyCheckResult ClassifyExistingEntry(IdempotencyStoreEntry existing, string requestHash)
    {
        if (existing.State == IdempotencyState.Processing)
        {
            return new IdempotencyCheckResult.InProgress(_options.InProgressRetryAfter);
        }

        // Completed or Failed — check the request hash.
        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return IdempotencyCheckResult.Reuse.Instance;
        }

        return new IdempotencyCheckResult.Replay(
            existing.StatusCode ?? 500,
            existing.ResponsePayload ?? "{}");
    }

    private async Task<IdempotencyCheckResult> CheckSqlAsync(
        Guid merchantId,
        OperationType operation,
        string key,
        string requestHash,
        string scopeKey,
        CancellationToken cancellationToken)
    {
        // Check for an existing record.
        var existing = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r =>
                r.MerchantId == merchantId
                && r.Operation == operation
                && r.Key == key,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.State == IdempotencyState.Processing)
            {
                // Stale Processing — the original request may have crashed. The recovery worker
                // handles reclaiming stale records; for now, treat as InProgress.
                return new IdempotencyCheckResult.InProgress(_options.InProgressRetryAfter);
            }

            // Completed or Failed — check the hash.
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return IdempotencyCheckResult.Reuse.Instance;
            }

            // Hash matches — replay the stored response.
            // Backfill Redis so future requests hit the fast path.
            var backfillEntry = new IdempotencyStoreEntry
            {
                State = existing.State,
                RequestHash = existing.RequestHash,
                StatusCode = existing.StatusCode,
                ResponsePayload = existing.ResponsePayload,
                CreatedAt = existing.CreatedAt,
                CompletedAt = existing.CompletedAt,
            };
            await BackfillRedisAsync(scopeKey, backfillEntry, cancellationToken);

            return new IdempotencyCheckResult.Replay(
                existing.StatusCode ?? 500,
                existing.ResponsePayload ?? "{}");
        }

        // No existing record — insert a new Processing record. The UNIQUE constraint is the
        // correctness boundary: if two concurrent inserts race, the loser gets a unique violation
        // and we return InProgress.
        var record = new IdempotencyRecord(
            _idGenerator.NewId(),
            merchantId,
            operation,
            key,
            requestHash,
            _clock.UtcNow,
            _options.ExpiresAfter);

        _dbContext.IdempotencyRecords.Add(record);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new IdempotencyCheckResult.Proceed(record.Id, scopeKey, record);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogInformation(
                "Concurrent idempotency insert detected for merchant {MerchantId} operation {Operation} key {Key}. Returning InProgress.",
                merchantId, operation, key);
            return new IdempotencyCheckResult.InProgress(_options.InProgressRetryAfter);
        }
    }

    private static string BuildScopeKey(Guid merchantId, OperationType operation, string key)
        => $"idem:{merchantId:N}:{(int)operation}:{key}";

    /// <summary>
    /// The only constraint that can fail on an IdempotencyRecord INSERT is the UNIQUE(MerchantId,
    /// Operation, Key) constraint. So any DbUpdateException during insert is treated as a concurrent
    /// request winning the race. This avoids coupling Application to SQL Server-specific exception types.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // A unique violation is typically accompanied by a message containing "unique" or "duplicate".
        // For SQL Server specifically the inner exception carries error number 2601 or 2627, but
        // checking the message keeps this layer database-agnostic.
        var message = (ex.InnerException?.Message ?? ex.Message) ?? string.Empty;
        return message.Contains("unique", StringComparison.OrdinalIgnoreCase)
            || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
    }
}
