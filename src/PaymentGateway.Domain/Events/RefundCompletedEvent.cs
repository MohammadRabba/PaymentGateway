using System.Text.Json.Serialization;

namespace PaymentGateway.Domain.Events;

/// <summary>
/// Stable integration event published when a refund has been completed in the internal ledger.
/// </summary>
public sealed record RefundCompletedEvent
{
    public required Guid EventId { get; init; }

    public required Guid RefundId { get; init; }

    public required Guid PaymentId { get; init; }

    public required Guid MerchantId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required Guid LedgerTransactionId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string CorrelationId { get; init; }

    [JsonPropertyName("eventType")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Required for JSON serialization")]
    public string EventType => "refund.completed";
}
