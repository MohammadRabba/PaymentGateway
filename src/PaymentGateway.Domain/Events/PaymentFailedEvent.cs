using System.Text.Json.Serialization;

namespace PaymentGateway.Domain.Events;

/// <summary>
/// Stable integration event published when a payment has permanently failed (decline or recovery timeout).
/// No ledger posting occurred; account balances are unchanged.
/// </summary>
public sealed record PaymentFailedEvent
{
    public required Guid EventId { get; init; }

    public required Guid PaymentId { get; init; }

    public required Guid MerchantId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string FailureReason { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string CorrelationId { get; init; }

    [JsonPropertyName("eventType")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Required for JSON serialization")]
    public string EventType => "payment.failed";
}
