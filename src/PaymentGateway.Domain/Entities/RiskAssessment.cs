using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.Entities;

/// <summary>
/// Persisted result of a fraud-risk evaluation. Append-only — never updated.
/// Multiple assessments can exist per payment (e.g., pre-auth + post-settle re-scores).
/// </summary>
public sealed class RiskAssessment
{
    public Guid Id { get; private set; }

    public Guid PaymentId { get; private set; }

    public Guid MerchantId { get; private set; }

    /// <summary>Fraud probability from the model, 0.0 to 1.0.</summary>
    public decimal Score { get; private set; }

    /// <summary>Decision applied to the payment based on the score + threshold.</summary>
    public RiskDecision Decision { get; private set; }

    /// <summary>Human-readable reason. Includes model version + top feature contributions if available.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>Model version that produced this score. Used for drift detection.</summary>
    public string ModelVersion { get; private set; } = string.Empty;

    /// <summary>Time taken by the fraud service call, in milliseconds. For latency monitoring.</summary>
    public long LatencyMs { get; private set; }

    public DateTimeOffset AssessedAt { get; private set; }

    private RiskAssessment() { }

    public RiskAssessment(
        Guid id,
        Guid paymentId,
        Guid merchantId,
        decimal score,
        RiskDecision decision,
        string reason,
        string modelVersion,
        long latencyMs,
        DateTimeOffset assessedAt)
    {
        Id = id;
        PaymentId = paymentId;
        MerchantId = merchantId;
        Score = score;
        Decision = decision;
        Reason = reason;
        ModelVersion = modelVersion;
        LatencyMs = latencyMs;
        AssessedAt = assessedAt;
    }
}

public enum RiskDecision
{
    /// <summary>Score below review threshold — proceed normally.</summary>
    Pass = 1,

    /// <summary>Score between review and block thresholds — proceed but flag for post-event review.</summary>
    Review = 2,

    /// <summary>Score above block threshold — fail the payment before calling the acquirer.</summary>
    Block = 3,

    /// <summary>Fraud service unavailable; configured fallback decision (default Pass with alert).</summary>
    Unavailable = 4,
}
