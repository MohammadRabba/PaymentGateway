namespace PaymentGateway.Application.Contracts;

/// <summary>
/// Account response. Balance is a materialised view of the ledger; the ledger is authoritative.
/// Use the reconciliation endpoint to verify Balance == SUM(ledger entries).
/// </summary>
public sealed record AccountResponse
{
    public required Guid Id { get; init; }

    public required Guid MerchantId { get; init; }

    public required string AccountType { get; init; }

    public required string Currency { get; init; }

    public required decimal Balance { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
