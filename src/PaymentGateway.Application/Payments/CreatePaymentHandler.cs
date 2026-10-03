using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Auditing;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Exceptions;
using PaymentGateway.Application.Idempotency;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Events;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.Application.Payments;

/// <summary>
/// Creates and authorizes a payment. The flow:
///
///  1. Idempotency check (Redis fast path + SQL authoritative).
///  2. Distributed lock (Redis, per merchant+key) to reduce wasted work on concurrent duplicates.
///  3. Create Payment (Pending → Processing) + Audit in a small transaction.
///  4. Call acquirer (OUTSIDE any DB transaction — external calls must not hold DB locks).
///     The acquirer idempotency key is deterministic (PAY-{paymentId}) so recovery can query it.
///  5. Transition payment state + OutboxMessage + Audit + IdempotencyRecord.Complete in ONE transaction.
///
/// If the acquirer times out or the outcome is unknown, the payment transitions to Unknown.
/// The recovery worker queries the acquirer later — NEVER re-authorize blindly.
/// </summary>
public sealed class CreatePaymentHandler
{
    private readonly IPaymentGatewayDbContext _dbContext;
    private readonly IAcquirerClient _acquirer;
    private readonly IDistributedLock _distributedLock;
    private readonly IdempotencyService _idempotency;
    private readonly RequestFingerprinter _fingerprinter;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly ICorrelationContext _correlation;
    private readonly AuditService _auditService;
    private readonly ILogger<CreatePaymentHandler> _logger;
    private readonly PaymentGateway.Application.Risk.RiskGate _riskGate;

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public CreatePaymentHandler(
        IPaymentGatewayDbContext dbContext,
        IAcquirerClient acquirer,
        IDistributedLock distributedLock,
        IdempotencyService idempotency,
        RequestFingerprinter fingerprinter,
        IClock clock,
        IIdGenerator idGenerator,
        ICorrelationContext correlation,
        AuditService auditService,
        ILogger<CreatePaymentHandler> logger,
        PaymentGateway.Application.Risk.RiskGate riskGate)
    {
        _dbContext = dbContext;
        _acquirer = acquirer;
        _distributedLock = distributedLock;
        _idempotency = idempotency;
        _fingerprinter = fingerprinter;
        _clock = clock;
        _idGenerator = idGenerator;
        _correlation = correlation;
        _auditService = auditService;
        _logger = logger;
        _riskGate = riskGate;
    }

    public async Task<CreatePaymentResponse> HandleAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var requestHash = _fingerprinter.ComputeHash(request);

        // Step 1: Idempotency check.
        var idemResult = await _idempotency.CheckAsync(
            request.MerchantId,
            OperationType.Payment,
            request.IdempotencyKey,
            requestHash,
            cancellationToken);

        switch (idemResult)
        {
            case IdempotencyCheckResult.Replay replay:
                return DeserializeResponse(replay.ResponsePayload);
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

    private async Task<CreatePaymentResponse> ExecuteWithIdempotencyAsync(
        CreatePaymentRequest request,
        IdempotencyCheckResult.Proceed proceed,
        CancellationToken cancellationToken)
    {
        // Step 2: Distributed lock (coordination only, not correctness). If Redis is down and the
        // lock can't be acquired, proceed without it — the SQL unique constraint still protects us.
        var lockKey = $"lock:payment:{request.MerchantId:N}:{request.IdempotencyKey}";
        await using var lockHandle = await _distributedLock.AcquireAsync(lockKey, TimeSpan.FromSeconds(30), cancellationToken);

        try
        {
            var response = await ExecuteCreateAsync(request, proceed, cancellationToken);

            // Update Redis cache to Completed state (best-effort).
            var responseJson = JsonSerializer.Serialize(response, ResponseJsonOptions);
            await _idempotency.CompleteRedisAsync(
                proceed.ScopeKey,
                _fingerprinter.ComputeHash(request),
                201,
                responseJson,
                cancellationToken);

            return response;
        }
        catch (Exception ex) when (ex is not IdempotencyKeyReuseException)
        {
            _logger.LogError(ex, "Payment creation failed for merchant {MerchantId}, idempotency key {Key}.",
                request.MerchantId, request.IdempotencyKey);

            await _idempotency.FailAsync(
                proceed.IdempotencyRecordId,
                proceed.ScopeKey,
                500,
                JsonSerializer.Serialize(new { error = ex.Message }, ResponseJsonOptions),
                cancellationToken);

            throw;
        }
    }

    private async Task<CreatePaymentResponse> ExecuteCreateAsync(
        CreatePaymentRequest request,
        IdempotencyCheckResult.Proceed proceed,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var currency = Currency.Parse(request.Currency);
        var amount = new Money(request.Amount, currency);

        // Step 3: Create Payment (Pending → Processing) + Audit. The IdempotencyRecord was
        // already inserted by IdempotencyService.CheckAsync and is tracked by proceed.Record.
        var paymentId = _idGenerator.NewId();
        var payment = new Payment(
            paymentId,
            request.MerchantId,
            request.IdempotencyKey,
            OperationType.Payment,
            amount,
            now);

        // Transition to Processing in-memory. EF will insert with Status=Processing.
        payment.BeginProcessing(now);

        _dbContext.Payments.Add(payment);
        _auditService.Record(request.MerchantId.ToString(), "PaymentCreated", "Payment", paymentId,
            new { request.Amount, request.Currency, request.IdempotencyKey });

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Risk Gate: evaluate the payment BEFORE calling the acquirer.
        RiskAssessment assessment = null!;
        try
        {
            assessment = await _riskGate.EvaluateAsync(paymentId, request.MerchantId, request.Amount, request.Currency, request.CardToken, cancellationToken);
        }
        catch (Exception ex)
        {
            // Any unexpected exception should not break payment flow; RiskGate internally handles failures
            // and records an Unavailable assessment. Log and continue.
            _logger.LogWarning(ex, "RiskGate evaluation failed for payment {PaymentId}. Proceeding with acquirer call.", paymentId);
        }

        if (assessment != null && assessment.Decision == PaymentGateway.Domain.Entities.RiskDecision.Block)
        {
            // Persist block in a transaction and complete idempotency without calling the acquirer.
            // reuse existing 'now' from the method scope
            using var txnBlock = await _dbContext.BeginTransactionAsync(cancellationToken);
            try
            {
                var loadedPayment = await _dbContext.Payments.FirstAsync(p => p.Id == paymentId, cancellationToken);
                loadedPayment.MarkFailed($"Blocked by fraud gate (score {assessment.Score:F4})", now);

                _auditService.Record(request.MerchantId.ToString(), "PaymentBlockedAsFraud", "Payment", paymentId,
                    new { Score = assessment.Score, ModelVersion = assessment.ModelVersion });

                // Outbox: PaymentBlockedAsFraudEvent
                var blockEvent = new PaymentGateway.Domain.Events.PaymentBlockedAsFraudEvent
                {
                    EventId = _idGenerator.NewId(),
                    PaymentId = paymentId,
                    MerchantId = request.MerchantId,
                    Amount = request.Amount,
                    Currency = request.Currency,
                    RiskScore = assessment.Score,
                    ModelVersion = assessment.ModelVersion,
                    OccurredAt = _clock.UtcNow,
                    CorrelationId = _correlation.CurrentId,
                };
                AddOutboxMessage(blockEvent, paymentId);

                var response = PaymentDtoMapping.MapToCreateResponse(loadedPayment);
                var responseJson = JsonSerializer.Serialize(response, ResponseJsonOptions);
                proceed.Record.Complete(403, responseJson, now);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await txnBlock.CommitAsync(cancellationToken);

                return response;
            }
            catch
            {
                await txnBlock.RollbackAsync(cancellationToken);
                throw;
            }
        }
        
        // If Review, publish a review event but continue to acquirer call (shadow/review mode):
        if (assessment != null && assessment.Decision == PaymentGateway.Domain.Entities.RiskDecision.Review)
        {
            var reviewEvent = new PaymentGateway.Domain.Events.PaymentFlaggedForReviewEvent
            {
                EventId = _idGenerator.NewId(),
                PaymentId = paymentId,
                MerchantId = request.MerchantId,
                Amount = request.Amount,
                Currency = request.Currency,
                RiskScore = assessment.Score,
                ModelVersion = assessment.ModelVersion,
                OccurredAt = _clock.UtcNow,
                CorrelationId = _correlation.CurrentId,
            };
            AddOutboxMessage(reviewEvent, paymentId);
            // Note: the assessment was already added to the DbContext by RiskGate and will be saved
            // when the post-acquirer transaction commits below. We choose to proceed to the acquirer
            // for Review decisions (flag-and-proceed).
        }

        // Step 4: Call acquirer (OUTSIDE any DB transaction). Use the deterministic idempotency key
        // so recovery can query this authorization later if needed.
        var acquirerKey = AcquirerReferences.ForPayment(paymentId);
        var acquirerRequest = new AcquirerAuthorizationRequest
        {
            PaymentId = paymentId,
            Amount = request.Amount,
            Currency = request.Currency,
            CardToken = request.CardToken,
            IdempotencyKey = acquirerKey,
        };

        AcquirerAuthorization? authResult;
        try
        {
            authResult = await _acquirer.AuthorizeAsync(acquirerRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            // Network/polly exhausted — the acquirer outcome is unknown. Transition to Unknown.
            // Recovery worker will query the acquirer later.
            _logger.LogWarning(ex, "Acquirer call failed for payment {PaymentId}. Transitioning to Unknown.", paymentId);
            return await TransitionToUnknownAsync(payment, proceed, request, cancellationToken);
        }

        // Step 5: Transition payment state + OutboxMessage + Audit + IdempotencyRecord.Complete
        // in a SINGLE transaction.
        using var txn = await _dbContext.BeginTransactionAsync(cancellationToken);

        try
        {
            // Reload the payment to get the current rowversion for optimistic concurrency.
            var loadedPayment = await _dbContext.Payments
                .FirstAsync(p => p.Id == paymentId, cancellationToken);

            if (authResult.Outcome == AcquirerOutcome.Success)
            {
                var auth = new Authorization
                {
                    AuthCode = authResult.AuthCode,
                    AcquirerReference = authResult.AcquirerReference,
                    Outcome = AcquirerOutcome.Success,
                    AuthorizedAt = now,
                };
                loadedPayment.MarkAuthorized(auth, now);

                _auditService.Record(request.MerchantId.ToString(), "PaymentAuthorized", "Payment", paymentId,
                    new { authResult.AuthCode, authResult.AcquirerReference });

                // Outbox: PaymentAuthorizedEvent
                var authEvent = new PaymentAuthorizedEvent
                {
                    EventId = _idGenerator.NewId(),
                    PaymentId = paymentId,
                    MerchantId = request.MerchantId,
                    Amount = request.Amount,
                    Currency = request.Currency,
                    AuthCode = authResult.AuthCode,
                    AcquirerReference = authResult.AcquirerReference,
                    OccurredAt = now,
                    CorrelationId = _correlation.CurrentId,
                };
                AddOutboxMessage(authEvent, paymentId);
            }
            else if (authResult.Outcome == AcquirerOutcome.Decline)
            {
                loadedPayment.MarkFailed(authResult.DeclineReason ?? "Acquirer declined", now);

                _auditService.Record(request.MerchantId.ToString(), "PaymentFailed", "Payment", paymentId,
                    new { Reason = authResult.DeclineReason });

                var failEvent = new PaymentFailedEvent
                {
                    EventId = _idGenerator.NewId(),
                    PaymentId = paymentId,
                    MerchantId = request.MerchantId,
                    Amount = request.Amount,
                    Currency = request.Currency,
                    FailureReason = authResult.DeclineReason ?? "Acquirer declined",
                    OccurredAt = now,
                    CorrelationId = _correlation.CurrentId,
                };
                AddOutboxMessage(failEvent, paymentId);
            }
            else // Timeout or Unknown
            {
                loadedPayment.MarkUnknown(now);

                _auditService.Record(request.MerchantId.ToString(), "PaymentUnknown", "Payment", paymentId,
                    new { Reason = "Acquirer outcome unknown" });

                // No event for Unknown — recovery worker will emit the appropriate event when resolved.
            }

            // Complete the idempotency record (part of the same transaction).
            var response = PaymentDtoMapping.MapToCreateResponse(loadedPayment);
            var responseJson = JsonSerializer.Serialize(response, ResponseJsonOptions);
            proceed.Record.Complete(201, responseJson, now);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await txn.CommitAsync(cancellationToken);

            return response;
        }
        catch
        {
            await txn.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<CreatePaymentResponse> TransitionToUnknownAsync(
        Payment payment,
        IdempotencyCheckResult.Proceed proceed,
        CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        using var txn = await _dbContext.BeginTransactionAsync(cancellationToken);
        try
        {
            var loadedPayment = await _dbContext.Payments
                .FirstAsync(p => p.Id == payment.Id, cancellationToken);

            loadedPayment.MarkUnknown(now);

            _auditService.Record(request.MerchantId.ToString(), "PaymentUnknown", "Payment", payment.Id,
                new { Reason = "Acquirer call failed" });

            var response = PaymentDtoMapping.MapToCreateResponse(loadedPayment);
            var responseJson = JsonSerializer.Serialize(response, ResponseJsonOptions);
            proceed.Record.Complete(201, responseJson, now);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await txn.CommitAsync(cancellationToken);

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
        var eventType = @event switch
        {
            PaymentAuthorizedEvent => nameof(PaymentAuthorizedEvent),
            PaymentSettledEvent => nameof(PaymentSettledEvent),
            PaymentFailedEvent => nameof(PaymentFailedEvent),
            RefundCompletedEvent => nameof(RefundCompletedEvent),
            _ => @event.GetType().Name,
        };

        var outboxMessage = new OutboxMessage(
            _idGenerator.NewId(),
            eventType,
            aggregateId,
            payload,
            _clock.UtcNow);

        _dbContext.OutboxMessages.Add(outboxMessage);
    }

    private static CreatePaymentResponse DeserializeResponse(string json)
    {
        return JsonSerializer.Deserialize<CreatePaymentResponse>(json, ResponseJsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize cached idempotency response.");
    }
}
