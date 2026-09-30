using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Auditing;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Exceptions;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Events;
using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Application.Payments;

/// <summary>
/// Recovers a payment stuck in Unknown or stale Processing state. The recovery worker (in
/// Infrastructure) detects stuck payments and calls this handler. The handler:
///
///  1. Queries the acquirer using the deterministic idempotency key (PAY-{paymentId}) — NEVER
///     re-issues an authorization request. This is critical: if we crashed before processing the
///     acquirer's response, the acquirer may have already authorized. Re-authorizing would
///     double-charge the cardholder.
///  2. If the acquirer reports known success → transition to Authorized, emit PaymentAuthorizedEvent.
///  3. If the acquirer reports known decline → transition to Failed, emit PaymentFailedEvent.
///  4. If the acquirer reports Unknown → throw UnknownAcquirerOutcomeException. The worker retries
///     later. After MaxRecoveryAttempts, the worker transitions to Failed with reason "RecoveryTimeout".
///
/// The transition is atomic (serializable transaction with UPDLOCK) and idempotent (if the payment
/// is already Authorized or Failed, the handler is a no-op).
/// </summary>
public sealed class RecoverPaymentHandler
{
    private readonly IPaymentGatewayDbContext _dbContext;
    private readonly IAcquirerClient _acquirer;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly ICorrelationContext _correlation;
    private readonly AuditService _auditService;
    private readonly ILogger<RecoverPaymentHandler> _logger;

    private static readonly JsonSerializerOptions EventJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public RecoverPaymentHandler(
        IPaymentGatewayDbContext dbContext,
        IAcquirerClient acquirer,
        IClock clock,
        IIdGenerator idGenerator,
        ICorrelationContext correlation,
        AuditService auditService,
        ILogger<RecoverPaymentHandler> logger)
    {
        _dbContext = dbContext;
        _acquirer = acquirer;
        _clock = clock;
        _idGenerator = idGenerator;
        _correlation = correlation;
        _auditService = auditService;
        _logger = logger;
    }

    /// <summary>
    /// Attempt to recover a payment. Returns the updated PaymentResponse.
    /// Throws UnknownAcquirerOutcomeException if the acquirer still reports Unknown.
    /// </summary>
    public async Task<PaymentResponse> HandleAsync(Guid paymentId, Guid merchantId, CancellationToken cancellationToken)
    {
        // Query the acquirer using the deterministic idempotency key — NEVER re-authorize.
        var acquirerKey = AcquirerReferences.ForPayment(paymentId);
        var authResult = await _acquirer.GetAuthorizationStatusAsync(acquirerKey, cancellationToken);

        using var txn = await _dbContext.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

        try
        {
            // Load the payment with UPDLOCK for the critical section.
            var payment = await _dbContext.Payments
                .FromSqlInterpolated($"SELECT * FROM Payments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {paymentId}")
                .FirstOrDefaultAsync(cancellationToken);

            if (payment is null)
            {
                throw new PaymentNotFoundException(paymentId);
            }

            if (payment.MerchantId != merchantId)
            {
                throw new MerchantIsolationException(merchantId, payment.MerchantId);
            }

            // Idempotent: if the payment has already transitioned past Processing/Unknown, no-op.
            if (payment.Status is not (PaymentStatus.Processing or PaymentStatus.Unknown))
            {
                await txn.RollbackAsync(cancellationToken);
                return PaymentDtoMapping.MapToResponse(payment);
            }

            var now = _clock.UtcNow;

            if (authResult.Outcome == AcquirerOutcome.Success)
            {
                var auth = new Domain.ValueObjects.Authorization
                {
                    AuthCode = authResult.AuthCode,
                    AcquirerReference = authResult.AcquirerReference,
                    Outcome = AcquirerOutcome.Success,
                    AuthorizedAt = now,
                };
                payment.MarkAuthorized(auth, now);

                _auditService.Record(merchantId.ToString(), "PaymentRecoveredAuthorized", "Payment", paymentId,
                    new { authResult.AuthCode, authResult.AcquirerReference });

                var authEvent = new PaymentAuthorizedEvent
                {
                    EventId = _idGenerator.NewId(),
                    PaymentId = paymentId,
                    MerchantId = merchantId,
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    AuthCode = authResult.AuthCode,
                    AcquirerReference = authResult.AcquirerReference,
                    OccurredAt = now,
                    CorrelationId = _correlation.CurrentId,
                };
                AddOutboxMessage(authEvent, paymentId);
            }
            else if (authResult.Outcome == AcquirerOutcome.Decline)
            {
                payment.MarkFailed(authResult.DeclineReason ?? "Declined during recovery", now);

                _auditService.Record(merchantId.ToString(), "PaymentRecoveredFailed", "Payment", paymentId,
                    new { Reason = authResult.DeclineReason });

                var failEvent = new PaymentFailedEvent
                {
                    EventId = _idGenerator.NewId(),
                    PaymentId = paymentId,
                    MerchantId = merchantId,
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    FailureReason = authResult.DeclineReason ?? "Declined during recovery",
                    OccurredAt = now,
                    CorrelationId = _correlation.CurrentId,
                };
                AddOutboxMessage(failEvent, paymentId);
            }
            else
            {
                // Still unknown — the worker should retry later.
                await txn.RollbackAsync(cancellationToken);
                throw new UnknownAcquirerOutcomeException(paymentId);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await txn.CommitAsync(cancellationToken);

            return PaymentDtoMapping.MapToResponse(payment);
        }
        catch (UnknownAcquirerOutcomeException)
        {
            // Already rolled back above.
            throw;
        }
        catch
        {
            await txn.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Force-fail a payment that has exceeded the maximum recovery attempts. This is called by the
    /// recovery worker when the acquirer has consistently returned Unknown for too long.
    /// </summary>
    public async Task<PaymentResponse> ForceFailAsync(Guid paymentId, Guid merchantId, string reason, CancellationToken cancellationToken)
    {
        using var txn = await _dbContext.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

        try
        {
            var payment = await _dbContext.Payments
                .FromSqlInterpolated($"SELECT * FROM Payments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {paymentId}")
                .FirstOrDefaultAsync(cancellationToken);

            if (payment is null)
            {
                throw new PaymentNotFoundException(paymentId);
            }

            if (payment.MerchantId != merchantId)
            {
                throw new MerchantIsolationException(merchantId, payment.MerchantId);
            }

            if (payment.Status is not (PaymentStatus.Processing or PaymentStatus.Unknown))
            {
                await txn.RollbackAsync(cancellationToken);
                return PaymentDtoMapping.MapToResponse(payment);
            }

            var now = _clock.UtcNow;
            payment.MarkFailed(reason, now);

            _auditService.Record(merchantId.ToString(), "PaymentRecoveryTimeout", "Payment", paymentId, new { Reason = reason });

            var failEvent = new PaymentFailedEvent
            {
                EventId = _idGenerator.NewId(),
                PaymentId = paymentId,
                MerchantId = merchantId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                FailureReason = reason,
                OccurredAt = now,
                CorrelationId = _correlation.CurrentId,
            };
            AddOutboxMessage(failEvent, paymentId);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await txn.CommitAsync(cancellationToken);

            return PaymentDtoMapping.MapToResponse(payment);
        }
        catch
        {
            await txn.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private void AddOutboxMessage<T>(T @event, Guid aggregateId) where T : class
    {
        var payload = JsonSerializer.Serialize(@event, EventJsonOptions);
        var outboxMessage = new OutboxMessage(
            _idGenerator.NewId(),
            @event.GetType().Name,
            aggregateId,
            payload,
            _clock.UtcNow);
        _dbContext.OutboxMessages.Add(outboxMessage);
    }
}
