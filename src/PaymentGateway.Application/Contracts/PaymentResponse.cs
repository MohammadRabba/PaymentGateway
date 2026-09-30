namespace PaymentGateway.Application.Contracts;

/// <summary>
/// Full payment response. Never exposes CardToken or any other sensitive input — the simulator
/// discards card data immediately after the acquirer call.
/// </summary>
public sealed record PaymentResponse
{
    public required Guid Id { get; init; }

    public required Guid MerchantId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? AuthCode { get; init; }

    public string? AcquirerReference { get; init; }

    public DateTimeOffset? AuthorizedAt { get; init; }

    public DateTimeOffset? SettledAt { get; init; }

    public DateTimeOffset? FailedAt { get; init; }

    public string? FailureReason { get; init; }

    public decimal TotalRefunded { get; init; }
}

/// <summary>
/// Request to settle an authorized payment. Settlement posts the double-entry ledger and
/// transitions the payment from Authorized to Settled atomically.
/// </summary>
public sealed record SettlePaymentRequest
{
    public required Guid PaymentId { get; init; }

    public required Guid MerchantId { get; init; }

    public required string IdempotencyKey { get; init; }
}
