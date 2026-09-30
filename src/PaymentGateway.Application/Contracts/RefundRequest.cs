namespace PaymentGateway.Application.Contracts;

/// <summary>
/// Request to create a refund. Supports full and partial refunds. The same IdempotencyKey cannot
/// be reused for a different refund of the same payment (enforced by SQL UNIQUE constraint).
/// </summary>
public sealed record RefundRequest
{
    public required Guid PaymentId { get; init; }

    public required Guid MerchantId { get; init; }

    public required string IdempotencyKey { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public string? Reason { get; init; }
}

/// <summary>
/// Response after creating a refund. The refund may be Pending, Completed, or Failed.
/// </summary>
public sealed record RefundResponse
{
    public required Guid RefundId { get; init; }

    public required Guid PaymentId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? FailureReason { get; init; }
}
