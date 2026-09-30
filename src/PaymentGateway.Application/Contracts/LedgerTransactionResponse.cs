namespace PaymentGateway.Application.Contracts;

/// <summary>
/// Ledger transaction response with its balanced entries. Append-only: the ledger can never
/// be modified or deleted. Corrections are new LedgerTransactionResponse of type Correction.
/// </summary>
public sealed record LedgerTransactionResponse
{
    public required Guid Id { get; init; }

    public Guid? PaymentId { get; init; }

    public Guid? RefundId { get; init; }

    public required string Type { get; init; }

    public required string Currency { get; init; }

    public required string CorrelationId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required IReadOnlyList<LedgerEntryResponse> Entries { get; init; }
}

public sealed record LedgerEntryResponse
{
    public required Guid Id { get; init; }

    public required Guid AccountId { get; init; }

    public required string EntryType { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
