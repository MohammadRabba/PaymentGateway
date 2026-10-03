namespace PaymentGateway.Application.Common;

/// <summary>
/// External fraud detection boundary. Implementations either call a Python microservice
/// exposing the HGB model via HTTP, or run an ONNX export of the model in-process.
/// The client must apply its own timeout and retry policy — see FraudResiliencePipeline.
/// </summary>
public interface IFraudDetectionClient
{
    Task<FraudRiskScore> ScoreAsync(FraudScoreRequest request, CancellationToken cancellationToken);
}

public sealed record FraudScoreRequest
{
    public required Guid PaymentId { get; init; }
    public required Guid MerchantId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string CardToken { get; init; }
    public required DateTimeOffset TransactionTime { get; init; }

    /// <summary>Velocity features computed by the gateway (counts in trailing windows).</summary>
    public required VelocityFeatures Velocity { get; init; }
}

public sealed record VelocityFeatures
{
    public int SameCardLastHour { get; init; }
    public int SameCardLastDay { get; init; }
    public int SameMerchantLastHour { get; init; }
    public decimal SameCardAmountSumLastDay { get; init; }
}

public sealed record FraudRiskScore
{
    /// <summary>Fraud probability, 0.0 to 1.0.</summary>
    public required decimal Score { get; init; }

    /// <summary>Model version that produced the score (e.g., "hgb-v1.2-20261001").</summary>
    public required string ModelVersion { get; init; }

    /// <summary>Optional: top contributing features for explainability. Null if unavailable.</summary>
    public IReadOnlyList<FeatureContribution>? TopFeatures { get; init; }
}

public sealed record FeatureContribution(string Feature, decimal Contribution);
