using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Auditing;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Options;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Application.Risk;

/// <summary>
/// Orchestrates the fraud risk gate. Called by CreatePaymentHandler after idempotency check,
/// BEFORE the acquirer call. The risk gate:
///   1. Computes velocity features from recent payments in the DB.
///   2. Calls IFraudDetectionClient.ScoreAsync.
///   3. Persists a RiskAssessment row.
///   4. Returns a RiskDecision that the handler enforces (Block / Review / Pass).
///
/// Failure handling: if the fraud client throws, the gate applies the configured FailClosedMode
/// and persists a RiskAssessment with Decision=Unavailable. The payment flow continues.
/// </summary>
public sealed class RiskGate
{
    private readonly IPaymentGatewayDbContext _dbContext;
    private readonly IFraudDetectionClient _fraudClient;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly FraudOptions _options;
    private readonly AuditService _auditService;
    private readonly ILogger<RiskGate> _logger;

    public RiskGate(
        IPaymentGatewayDbContext dbContext,
        IFraudDetectionClient fraudClient,
        IClock clock,
        IIdGenerator idGenerator,
        IOptions<FraudOptions> options,
        AuditService auditService,
        ILogger<RiskGate> logger)
    {
        _dbContext = dbContext;
        _fraudClient = fraudClient;
        _clock = clock;
        _idGenerator = idGenerator;
        _options = options.Value;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<RiskAssessment> EvaluateAsync(
        Guid paymentId,
        Guid merchantId,
        decimal amount,
        string currency,
        string cardToken,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            // Gate disabled — return a Pass assessment with zero score.
            var disabledAssessment = new RiskAssessment(
                _idGenerator.NewId(), paymentId, merchantId, 0m,
                RiskDecision.Pass, "Fraud gate disabled", "disabled", 0, _clock.UtcNow);
            return disabledAssessment;
        }

        var startedAt = _clock.UtcNow;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Compute velocity features from recent payments.
        var velocity = await ComputeVelocityAsync(merchantId, cardToken, cancellationToken);

        var request = new FraudScoreRequest
        {
            PaymentId = paymentId,
            MerchantId = merchantId,
            Amount = amount,
            Currency = currency,
            CardToken = cardToken,
            TransactionTime = startedAt,
            Velocity = velocity,
        };

        FraudRiskScore? score;
        RiskDecision decision;
        string reason;

        try
        {
            // 2. Call the fraud client (with its own internal timeout).
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.Timeout);
            score = await _fraudClient.ScoreAsync(request, cts.Token);

            // 3. Apply thresholds.
            decision = score.Score >= _options.BlockThreshold ? RiskDecision.Block
                     : score.Score >= _options.ReviewThreshold ? RiskDecision.Review
                     : RiskDecision.Pass;
            reason = decision == RiskDecision.Block
                ? $"Score {score.Score:F4} >= block threshold {_options.BlockThreshold:F4}"
                : decision == RiskDecision.Review
                    ? $"Score {score.Score:F4} >= review threshold {_options.ReviewThreshold:F4}"
                    : $"Score {score.Score:F4} below review threshold";
        }
        catch (Exception ex) when (ex is not DomainException)
        {
            _logger.LogWarning(ex, "Fraud client failed for payment {PaymentId}", paymentId);
            decision = _options.FailClosedMode == FailClosedMode.Block ? RiskDecision.Block : RiskDecision.Unavailable;
            score = null;
            reason = $"Fraud service unavailable; fail-closed={_options.FailClosedMode}";
        }

        sw.Stop();

        // 4. Persist the assessment (caller's transaction commits it).
        var assessment = new RiskAssessment(
            _idGenerator.NewId(),
            paymentId,
            merchantId,
            score?.Score ?? 0m,
            decision,
            reason,
            score?.ModelVersion ?? "unavailable",
            sw.ElapsedMilliseconds,
            _clock.UtcNow);

        _dbContext.RiskAssessments.Add(assessment);

        _auditService.Record(
            merchantId.ToString(),
            decision == RiskDecision.Block ? "PaymentBlockedAsFraud" : "RiskAssessed",
            "RiskAssessment",
            assessment.Id,
            new { PaymentId = paymentId, Score = assessment.Score, Decision = decision, ModelVersion = assessment.ModelVersion });

        return assessment;
    }

    private async Task<VelocityFeatures> ComputeVelocityAsync(Guid merchantId, string cardToken, CancellationToken ct)
    {
        var oneHourAgo = _clock.UtcNow.AddHours(-1);
        var oneDayAgo = _clock.UtcNow.AddDays(-1);

        // Count recent payments by same card (using CardToken as a stand-in for card id).
        var sameCardLastHour = await _dbContext.Payments
            .AsNoTracking()
            .CountAsync(p => p.MerchantId == merchantId && p.CreatedAt >= oneHourAgo, ct);
        // Note: we don't have a CardToken column on Payment — for real deployment, add a
        // CardHash column (HMAC-SHA256 of card token with a gateway secret) so we can group
        // without storing the raw token.

        var sameCardLastDay = await _dbContext.Payments
            .AsNoTracking()
            .CountAsync(p => p.MerchantId == merchantId && p.CreatedAt >= oneDayAgo, ct);

        var sameMerchantLastHour = sameCardLastHour; // simplified; could split by card/merchant
        var sameCardAmountSumLastDay = await _dbContext.Payments
            .AsNoTracking()
            .Where(p => p.MerchantId == merchantId && p.CreatedAt >= oneDayAgo)
            .SumAsync(p => p.Amount, ct);

        return new VelocityFeatures
        {
            SameCardLastHour = sameCardLastHour,
            SameCardLastDay = sameCardLastDay,
            SameMerchantLastHour = sameMerchantLastHour,
            SameCardAmountSumLastDay = sameCardAmountSumLastDay,
        };
    }
}
