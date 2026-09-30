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
using PaymentGateway.Application.Payments;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Events;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Rules;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.Application.Refunds;

/// <summary>
/// Creates and settles a refund. Refunds are first-class operations with their own idempotency keys,
/// state machine, and ledger postings. The refund invariant (TotalRefunded + amount &lt;= SettledAmount)
/// is enforced both before posting (early rejection) and inside the transaction (re-check).
///
/// Flow:
///  1. Idempotency check (Operation=Refund, key from header).
///  2. Load payment (with lock), verify state is Settled or PartiallyRefunded.
///  3. Pre-check RefundRules.AssertCanRefund (early 422 rejection for invalid amounts).
///  4. Serializable transaction: reload payment + accounts with UPDLOCK, create Refund entity,
///     post balanced refund ledger, apply refund to payment, mark refund Completed, insert
///     RefundCompletedEvent outbox, audit, update IdempotencyRecord — all atomic.
///  5. Concurrency: rowversion + serializable + idempotency unique constraint prevent duplicate
///     refunds even under concurrent retry.
/// </summary>
public sealed class RefundPaymentHandler
{
    private const int MaxConcurrencyRetries = 3;

    private readonly IPaymentGatewayDbContext _dbContext;
    private readonly IAcquirerClient _acquirer;
    private readonly RefundSettlementService _refundSettlementService;
    private readonly IdempotencyService _idempotency;
    private readonly RequestFingerprinter _fingerprinter;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly ICorrelationContext _correlation;
    private readonly AuditService _auditService;
    private readonly ILogger<RefundPaymentHandler> _logger;

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public RefundPaymentHandler(
        IPaymentGatewayDbContext dbContext,
        IAcquirerClient acquirer,
        RefundSettlementService refundSettlementService,
        IdempotencyService idempotency,
        RequestFingerprinter fingerprinter,
        IClock clock,
        IIdGenerator idGenerator,
        ICorrelationContext correlation,
        AuditService auditService,
        ILogger<RefundPaymentHandler> logger)
    {
        _dbContext = dbContext;
        _acquirer = acquirer;
        _refundSettlementService = refundSettlementService;
        _idempotency = idempotency;
        _fingerprinter = fingerprinter;
        _clock = clock;
        _idGenerator = idGenerator;
        _correlation = correlation;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<RefundResponse> HandleAsync(RefundRequest request, CancellationToken cancellationToken)
    {
        var requestHash = _fingerprinter.ComputeHash(request);

        var idemResult = await _idempotency.CheckAsync(
            request.MerchantId,
            OperationType.Refund,
            request.IdempotencyKey,
            requestHash,
            cancellationToken);

        switch (idemResult)
        {
            case IdempotencyCheckResult.Replay replay:
                return JsonSerializer.Deserialize<RefundResponse>(replay.ResponsePayload, ResponseJsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize cached refund response.");
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

    private async Task<RefundResponse> ExecuteWithIdempotencyAsync(
        RefundRequest request,
        IdempotencyCheckResult.Proceed proceed,
        CancellationToken cancellationToken)
    {
        try
        {
            for (int attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
            {
                try
                {
                    return await ExecuteRefundAsync(request, proceed, cancellationToken);
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
                {
                    _logger.LogInformation("Optimistic concurrency conflict on refund attempt {Attempt}. Retrying.", attempt);
                    _dbContext.ChangeTracker.Clear();
                }
            }

            throw new ConcurrencyConflictException("Payment", request.PaymentId, MaxConcurrencyRetries);
        }
        catch (Exception ex) when (ex is not IdempotencyKeyReuseException
                                    and not ConcurrencyConflictException)
        {
            _logger.LogError(ex, "Refund failed for payment {PaymentId}.", request.PaymentId);

            await _idempotency.FailAsync(
                proceed.IdempotencyRecordId,
                proceed.ScopeKey,
                500,
                JsonSerializer.Serialize(new { error = ex.Message }, ResponseJsonOptions),
                cancellationToken);

            throw;
        }
    }

    private async Task<RefundResponse> ExecuteRefundAsync(
        RefundRequest request,
        IdempotencyCheckResult.Proceed proceed,
        CancellationToken cancellationToken)
    {
        var currency = Currency.Parse(request.Currency);
        var refundAmount = new Money(request.Amount, currency);
        var now = _clock.UtcNow;

        // Pre-check: load the payment (without lock) to validate state and refund rules early.
        // This avoids entering a serializable transaction for obviously invalid requests (e.g.,
        // refunding more than the settled amount).
        var preCheckPayment = await _dbContext.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken);

        if (preCheckPayment is null)
        {
            throw new PaymentNotFoundException(request.PaymentId);
        }
        if (preCheckPayment.MerchantId != request.MerchantId)
        {
            throw new MerchantIsolationException(request.MerchantId, preCheckPayment.MerchantId);
        }

        var settled = new Money(preCheckPayment.Amount, currency);
        var already = new Money(preCheckPayment.TotalRefunded, currency);
        RefundRules.AssertCanRefund(preCheckPayment.Id, preCheckPayment.Status, settled, already, refundAmount);

        // Call the acquirer to process the refund (outside the DB transaction).
        // Pre-generate the refund ID so we can derive a deterministic acquirer idempotency key.
        var refundId = _idGenerator.NewId();
        var acquirerKey = AcquirerReferences.ForRefund(refundId);

        var acquirerRequest = new AcquirerRefundRequest
        {
            PaymentId = request.PaymentId,
            RefundId = refundId,
            OriginalAcquirerReference = preCheckPayment.AcquirerReference ?? string.Empty,
            Amount = request.Amount,
            Currency = request.Currency,
            IdempotencyKey = acquirerKey,
        };

        AcquirerRefundResult acquirerResult;
        try
        {
            acquirerResult = await _acquirer.RefundAsync(acquirerRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Acquirer refund call failed for payment {PaymentId}.", request.PaymentId);
            throw new InvalidOperationException("Acquirer refund call failed; the refund has not been posted.", ex);
        }

        if (acquirerResult.Outcome == AcquirerOutcome.Decline)
        {
            throw new InvalidOperationException($"Acquirer declined the refund: {acquirerResult.DeclineReason}");
        }

        // If outcome is Unknown or Timeout, we still proceed with the ledger posting because the
        // refund may have succeeded on the acquirer side. The recovery worker can reconcile later.
        // For this simulation, we proceed with the ledger posting regardless of Unknown/Timeout
        // (the acquirer simulator is deterministic, so Unknown is rare).

        // Now enter the serializable critical section.
        using var txn = await _dbContext.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            // Reload the payment with UPDLOCK for the critical section.
            var payment = await _dbContext.Payments
                .FromSqlInterpolated($"SELECT * FROM Payments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {request.PaymentId}")
                .FirstOrDefaultAsync(cancellationToken);

            if (payment is null)
            {
                throw new PaymentNotFoundException(request.PaymentId);
            }

            // Create the refund entity (Pending state).
            var refund = new Refund(
                refundId,
                payment.Id,
                request.MerchantId,
                request.IdempotencyKey,
                refundAmount,
                request.Reason,
                now);

            _dbContext.Refunds.Add(refund);

            _auditService.Record(request.MerchantId.ToString(), "RefundCreated", "Refund", refundId,
                new { PaymentId = payment.Id, request.Amount, request.Currency });

            // Post the refund ledger (loads accounts with UPDLOCK, builds balanced entries,
            // applies to balances, updates payment.TotalRefunded and state, marks refund Completed).
            var ledgerTxnId = await _refundSettlementService.PostRefundAsync(payment, refund, cancellationToken);

            _auditService.Record(request.MerchantId.ToString(), "RefundCompleted", "Refund", refundId,
                new { LedgerTransactionId = ledgerTxnId });

            // Outbox: RefundCompletedEvent
            var refundEvent = new RefundCompletedEvent
            {
                EventId = _idGenerator.NewId(),
                RefundId = refundId,
                PaymentId = payment.Id,
                MerchantId = request.MerchantId,
                Amount = request.Amount,
                Currency = request.Currency,
                LedgerTransactionId = ledgerTxnId,
                OccurredAt = now,
                CorrelationId = _correlation.CurrentId,
            };
            AddOutboxMessage(refundEvent, refundId);

            // Build response and update idempotency record atomically.
            var response = RefundDtoMapping.MapToResponse(refund);
            var responseJson = JsonSerializer.Serialize(response, ResponseJsonOptions);
            proceed.Record.Complete(201, responseJson, now);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await txn.CommitAsync(cancellationToken);

            // Best-effort Redis cache update.
            await _idempotency.CompleteRedisAsync(
                proceed.ScopeKey,
                _fingerprinter.ComputeHash(request),
                201,
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
