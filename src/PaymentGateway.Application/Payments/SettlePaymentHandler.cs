using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Auditing;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Exceptions;
using PaymentGateway.Application.Idempotency;
using PaymentGateway.Application.Ledger;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Events;
using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Application.Payments;

/// <summary>
/// Settles an authorized payment. The settlement transaction is the narrow serializable critical
/// section: load payment + accounts with UPDLOCK/HOLDLOCK, post balanced ledger entries, update
/// balances, transition payment to Settled, insert outbox event, insert audit record, and update
/// the idempotency record — all atomically.
///
/// Concurrency: rowversion on Payment + serializable isolation prevents double settlement.
/// Idempotency: if the payment is already Settled, the service returns without posting a duplicate
/// ledger transaction (defense in depth even if the idempotency record expired).
/// </summary>
public sealed class SettlePaymentHandler
{
    private const int MaxConcurrencyRetries = 3;

    private readonly IPaymentGatewayDbContext _dbContext;
    private readonly SettlementService _settlementService;
    private readonly IdempotencyService _idempotency;
    private readonly RequestFingerprinter _fingerprinter;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly ICorrelationContext _correlation;
    private readonly AuditService _auditService;
    private readonly ILogger<SettlePaymentHandler> _logger;

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public SettlePaymentHandler(
        IPaymentGatewayDbContext dbContext,
        SettlementService settlementService,
        IdempotencyService idempotency,
        RequestFingerprinter fingerprinter,
        IClock clock,
        IIdGenerator idGenerator,
        ICorrelationContext correlation,
        AuditService auditService,
        ILogger<SettlePaymentHandler> logger)
    {
        _dbContext = dbContext;
        _settlementService = settlementService;
        _idempotency = idempotency;
        _fingerprinter = fingerprinter;
        _clock = clock;
        _idGenerator = idGenerator;
        _correlation = correlation;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<PaymentResponse> HandleAsync(SettlePaymentRequest request, CancellationToken cancellationToken)
    {
        var requestHash = _fingerprinter.ComputeHash(request);

        var idemResult = await _idempotency.CheckAsync(
            request.MerchantId,
            OperationType.Settlement,
            request.IdempotencyKey,
            requestHash,
            cancellationToken);

        switch (idemResult)
        {
            case IdempotencyCheckResult.Replay replay:
                return JsonSerializer.Deserialize<PaymentResponse>(replay.ResponsePayload, ResponseJsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize cached settlement response.");
            case IdempotencyCheckResult.InProgress inProgress:
                throw new IdempotencyInProgressException(request.IdempotencyKey, inProgress.RetryAfter);
            case IdempotencyCheckResult.Reuse:
                throw new IdempotencyKeyReuseException(request.IdempotencyKey);
            case IdempotencyCheckResult.Proceed proceed:
                return await ExecuteWithIdempotencyAsync(request, proceed, cancellationToken);
            default:
                throw new InvalidOperationException($"Unexpected idempotency result type: {idemResult.GetType().Name}");
        }
    }

    private async Task<PaymentResponse> ExecuteWithIdempotencyAsync(
        SettlePaymentRequest request,
        IdempotencyCheckResult.Proceed proceed,
        CancellationToken cancellationToken)
    {
        try
        {
            // Retry on optimistic concurrency conflict (rowversion). The state machine + idempotency
            // ensures the retry is safe — a second settlement attempt that sees Settled is a no-op.
            for (int attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
            {
                try
                {
                    return await ExecuteSettlementAsync(request, proceed, cancellationToken);
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
                {
                    _logger.LogInformation("Optimistic concurrency conflict on settlement attempt {Attempt}. Retrying.", attempt);
                    // Clear the change tracker so the next iteration re-reads fresh entities.
                    _dbContext.ChangeTracker.Clear();
                }
            }

            throw new ConcurrencyConflictException("Payment", request.PaymentId, MaxConcurrencyRetries);
        }
        catch (Exception ex) when (ex is not IdempotencyKeyReuseException
                                    and not ConcurrencyConflictException)
        {
            _logger.LogError(ex, "Settlement failed for payment {PaymentId}.", request.PaymentId);

            await _idempotency.FailAsync(
                proceed.IdempotencyRecordId,
                proceed.ScopeKey,
                500,
                JsonSerializer.Serialize(new { error = ex.Message }, ResponseJsonOptions),
                cancellationToken);

            throw;
        }
    }

    private async Task<PaymentResponse> ExecuteSettlementAsync(
        SettlePaymentRequest request,
        IdempotencyCheckResult.Proceed proceed,
        CancellationToken cancellationToken)
    {
        // The narrow serializable critical section. This is intentionally scoped to ONE payment's
        // settlement to avoid locking more than necessary.
        using var txn = await _dbContext.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            // Load the payment with UPDLOCK + HOLDLOCK for the critical section. This prevents
            // other transactions from reading or modifying this row until we commit.
            var payment = await _dbContext.Payments
                .FromSqlInterpolated($"SELECT * FROM Payments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {request.PaymentId}")
                .FirstOrDefaultAsync(cancellationToken);

            if (payment is null)
            {
                throw new PaymentNotFoundException(request.PaymentId);
            }

            if (payment.MerchantId != request.MerchantId)
            {
                throw new MerchantIsolationException(request.MerchantId, payment.MerchantId);
            }

            // Post the settlement ledger. This loads accounts with UPDLOCK, builds balanced entries,
            // applies them to balances, and transitions the payment to Settled. If already settled,
            // returns Guid.Empty (idempotent no-op).
            var ledgerTxnId = await _settlementService.PostSettlementAsync(payment, cancellationToken);

            if (ledgerTxnId != Guid.Empty)
            {
                _auditService.Record(request.MerchantId.ToString(), "PaymentSettled", "Payment", payment.Id,
                    new { LedgerTransactionId = ledgerTxnId });

                // Outbox: PaymentSettledEvent
                var settledEvent = new PaymentSettledEvent
                {
                    EventId = _idGenerator.NewId(),
                    PaymentId = payment.Id,
                    MerchantId = payment.MerchantId,
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    LedgerTransactionId = ledgerTxnId,
                    OccurredAt = _clock.UtcNow,
                    CorrelationId = _correlation.CurrentId,
                };
                AddOutboxMessage(settledEvent, payment.Id);
            }

            // Build the response and update the idempotency record atomically.
            var response = PaymentDtoMapping.MapToResponse(payment);
            var responseJson = JsonSerializer.Serialize(response, ResponseJsonOptions);
            proceed.Record.Complete(200, responseJson, _clock.UtcNow);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await txn.CommitAsync(cancellationToken);

            // Best-effort Redis cache update (after commit).
            await _idempotency.CompleteRedisAsync(
                proceed.ScopeKey,
                _fingerprinter.ComputeHash(request),
                200,
                responseJson,
                cancellationToken);

            return response;
        }
        catch
        {
            await txn.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private void AddOutboxMessage<T>(T @event, Guid aggregateId) where T : class
    {
        var payload = JsonSerializer.Serialize(@event, ResponseJsonOptions);
        var outboxMessage = new OutboxMessage(
            _idGenerator.NewId(),
            @event.GetType().Name,
            aggregateId,
            payload,
            _clock.UtcNow);
        _dbContext.OutboxMessages.Add(outboxMessage);
    }
}
