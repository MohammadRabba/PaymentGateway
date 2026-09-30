using System.Text.Json.Serialization;

namespace PaymentGateway.Domain.Events;

/// <summary>
/// Stable integration event published when a payment has been authorized by the acquirer.
/// Persisted as JSON in the OutboxMessage.Payload column. Never serialise the EF entity directly;
/// this contract is what consumers lock to.
/// </summary>
public sealed record PaymentAuthorizedEvent
{
    public required Guid EventId { get; init; }

    public required Guid PaymentId { get; init; }

    public required Guid MerchantId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string AuthCode { get; init; }

    public required string AcquirerReference { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string CorrelationId { get; init; }

    [JsonPropertyName("eventType")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Required for JSON serialization")]
    public string EventType => "payment.authorized";
}
