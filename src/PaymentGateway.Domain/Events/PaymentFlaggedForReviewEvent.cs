using System.Text.Json.Serialization;

namespace PaymentGateway.Domain.Events;

public sealed record PaymentFlaggedForReviewEvent
{
    public required Guid EventId { get; init; }
    public required Guid PaymentId { get; init; }
    public required Guid MerchantId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required decimal RiskScore { get; init; }
    public required string ModelVersion { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string CorrelationId { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance property required for JSON serialization")]
    [JsonPropertyName("eventType")]
    public string EventType => "payment.flagged.review";
}
