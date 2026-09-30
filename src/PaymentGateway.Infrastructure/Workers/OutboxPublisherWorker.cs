using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Messaging;
using PaymentGateway.Infrastructure.Observability;

namespace PaymentGateway.Infrastructure.Workers;

/// <summary>
/// Background worker that drains the outbox table into RabbitMQ. Implements the transactional
/// outbox pattern: the financial transaction inserts the OutboxMessage; this worker publishes it.
///
/// Scoping: the worker is a singleton IHostedService. To avoid capturing a Scoped DbContext as a
/// de-facto singleton (not thread-safe, change-tracker bloat), each iteration creates a fresh DI
/// scope via IServiceScopeFactory and resolves IPaymentGatewayDbContext from it.
///
/// At-least-once semantics:
///   1. Atomically claim messages (UPDATE ... SET ClaimedBy, ClaimedAt, Status=Claimed WHERE Id IN
///      (SELECT TOP N FROM Outbox WHERE Status=Pending AND NextAttemptAt &lt;= now))
///   2. Publish each claimed message to RabbitMQ.
///   3. On success: UPDATE ... SET Status=Published, ProcessedAt=now
///   4. On transient failure: UPDATE ... SET Attempts++, NextAttemptAt=now+backoff, Status=Pending
///   5. On Attempts >= MaxAttempts: UPDATE ... SET Status=Poisoned + INSERT OutboxDeadLetter audit row
///
/// Crash safety: if the worker crashes between publishing and updating ProcessedAt, the message
/// is redelivered on the next iteration (Status=Claimed but ClaimedAt older than StuckClaimThreshold).
/// Consumers must dedup by EventId.
/// </summary>
public sealed class OutboxPublisherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqEventPublisher _publisher;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly WorkerOptions _options;
    private readonly PaymentGatewayMetrics _metrics;
    private readonly ILogger<OutboxPublisherWorker> _logger;

    public OutboxPublisherWorker(
        IServiceScopeFactory scopeFactory,
        RabbitMqEventPublisher publisher,
        IClock clock,
        IIdGenerator idGenerator,
        IOptions<WorkerOptions> options,
        PaymentGatewayMetrics metrics,
        ILogger<OutboxPublisherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _clock = clock;
        _idGenerator = idGenerator;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxPublisherWorker started. WorkerId={WorkerId}", _options.WorkerId);

        await Task.Delay(_options.StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<IPaymentGatewayDbContext>();
                await ProcessBatchAsync(dbContext, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxPublisherWorker iteration failed.");
            }

            try
            {
                await Task.Delay(_options.OutboxPollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("OutboxPublisherWorker stopped.");
    }

    private async Task ProcessBatchAsync(IPaymentGatewayDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var claimedIds = await ClaimBatchAsync(dbContext, now, cancellationToken);
        if (claimedIds.Count == 0)
        {
            await UpdatePendingCountMetricAsync(dbContext, cancellationToken);
            return;
        }

        var messages = await dbContext.OutboxMessages
            .Where(m => claimedIds.Contains(m.Id))
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                var published = await _publisher.PublishAsync(
                    message.EventType,
                    message.AggregateId,
                    message.Payload,
                    cancellationToken);

                if (published)
                {
                    message.MarkPublished(now);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    _metrics.OutboxProcessed.Add(1);
                    _logger.LogInformation("Outbox message {Id} published.", message.Id);
                }
                else
                {
                    RecordTransientFailure(dbContext, message, "Publisher returned false.", now);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                RecordTransientFailure(dbContext, message, ex.Message, now);
                await dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogError(ex, "Failed to publish outbox message {Id}.", message.Id);
            }
        }

        await UpdatePendingCountMetricAsync(dbContext, cancellationToken);
    }

    private async Task<List<Guid>> ClaimBatchAsync(IPaymentGatewayDbContext dbContext, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var batchSize = _options.OutboxBatchSize;
        var workerId = _options.WorkerId;

        using var txn = await dbContext.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var stuckClaimThreshold = now.Add(-TimeSpan.FromMinutes(5));
            var messages = await dbContext.OutboxMessages
                .Where(m => (m.Status == OutboxStatus.Pending && m.NextAttemptAt <= now)
                          || (m.Status == OutboxStatus.Claimed && m.ClaimedAt < stuckClaimThreshold))
                .OrderBy(m => m.OccurredAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            var claimedIds = new List<Guid>();
            foreach (var msg in messages)
            {
                msg.Claim(workerId, now, _options.OutboxInitialBackoff);
                claimedIds.Add(msg.Id);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await txn.CommitAsync(cancellationToken);

            return claimedIds;
        }
        catch
        {
            await txn.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private void RecordTransientFailure(IPaymentGatewayDbContext dbContext, OutboxMessage message, string error, DateTimeOffset now)
    {
        var nextBackoff = CalculateBackoff(message.Attempts + 1);
        message.RecordFailure(error, now, nextBackoff, _options.OutboxMaxAttempts);

        if (message.Status == OutboxStatus.Poisoned)
        {
            var deadLetter = new OutboxDeadLetter(
                _idGenerator.NewId(),
                message.Id,
                message.EventType,
                message.AggregateId,
                message.Payload,
                error,
                message.Attempts,
                message.OccurredAt,
                now);

            dbContext.OutboxDeadLetters.Add(deadLetter);
            _metrics.OutboxPoisoned.Add(1);
            _logger.LogError("Outbox message {Id} poisoned after {Attempts} attempts. Last error: {Error}",
                message.Id, message.Attempts, error);
        }
        else
        {
            _metrics.OutboxRetry.Add(1);
        }
    }

    private TimeSpan CalculateBackoff(int attempt)
    {
        var delay = _options.OutboxInitialBackoff.TotalSeconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(delay, _options.OutboxMaxBackoff.TotalSeconds);
        return TimeSpan.FromSeconds(capped);
    }

    private async Task UpdatePendingCountMetricAsync(IPaymentGatewayDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            var pendingCount = await dbContext.OutboxMessages
                .CountAsync(m => m.Status == OutboxStatus.Pending || m.Status == OutboxStatus.Claimed, cancellationToken);
            _metrics.OutboxPending.Record(pendingCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update pending count metric.");
        }
    }
}
