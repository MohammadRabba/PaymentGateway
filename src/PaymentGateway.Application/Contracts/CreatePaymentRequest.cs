namespace PaymentGateway.Application.Contracts;

/// <summary>
/// Request to create a new payment. The IdempotencyKey is extracted from the X-Idempotency-Key
/// header by the API layer and the MerchantId is set from the authenticated merchant context,
/// never from the client body (to prevent cross-merchant access).
/// </summary>
public sealed record CreatePaymentRequest
{
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>
    /// Simulator-only card token reference. Never a real card number. Never persisted in the database
    /// or logged. Used only to pass to the acquirer for authorization.
    /// </summary>
    public required string CardToken { get; init; }

    public required string IdempotencyKey { get; init; }

    /// <summary>Set by the API layer from the authenticated merchant context.</summary>
    public required Guid MerchantId { get; init; }

    public string? Description { get; init; }
}

/// <summary>
/// Response returned immediately after creating a payment. The payment is in Pending or Processing
/// state; the client should poll GET /payments/{id} for the final status.
/// </summary>
public sealed record CreatePaymentResponse
{
    public required Guid PaymentId { get; init; }

    public required string Status { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
