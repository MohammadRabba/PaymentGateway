namespace PaymentGateway.Application.Contracts;

/// <summary>
/// Request to register a new merchant. Admin-only operation.
/// </summary>
public sealed record RegisterMerchantRequest
{
    public required string ExternalReference { get; init; }

    public required string Name { get; init; }

    public required string WebhookUrl { get; init; }

    public required string IdempotencyKey { get; init; }
}

/// <summary>
/// Merchant response. Never includes WebhookSecret or ApiKeyHash — these are sensitive.
/// The ApiKey is shown to the caller ONCE at registration and never retrievable again.
/// </summary>
public sealed record MerchantResponse
{
    public required Guid Id { get; init; }

    public required string ExternalReference { get; init; }

    public required string Name { get; init; }

    public required string WebhookUrl { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Raw API key. Returned only on registration. Never persisted in plaintext.
    /// The caller must store this securely; it cannot be retrieved again.
    /// </summary>
    public string? ApiKey { get; init; }
}
