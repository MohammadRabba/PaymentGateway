namespace PaymentGateway.Application.Contracts;

/// <summary>
/// Reconciliation result. Reports discrepancies between Account.Balance (materialised) and
/// SUM(LedgerEntries) (authoritative). Discrepancies are reported, NEVER auto-repaired.
/// </summary>
public sealed record ReconciliationResponse
{
    public required DateTimeOffset ReconciledAt { get; init; }

    public required int AccountsChecked { get; init; }

    public required int DiscrepanciesFound { get; init; }

    public required IReadOnlyList<AccountDiscrepancy> Discrepancies { get; init; }
}

public sealed record AccountDiscrepancy
{
    public required Guid AccountId { get; init; }

    public required Guid MerchantId { get; init; }

    public required string AccountType { get; init; }

    public required string Currency { get; init; }

    public required decimal StoredBalance { get; init; }

    public required decimal CalculatedBalance { get; init; }

    public required decimal Difference { get; init; }
}

/// <summary>
/// Response for listing poisoned outbox messages. These are messages that exhausted retry
/// attempts and were moved to the dead-letter table for manual inspection.
/// </summary>
public sealed record PoisonedOutboxResponse
{
    public required Guid Id { get; init; }

    public required string EventType { get; init; }

    public required Guid AggregateId { get; init; }

    public required int Attempts { get; init; }

    public required string LastError { get; init; }

    public required DateTimeOffset OriginalOccurredAt { get; init; }

    public required DateTimeOffset PoisonedAt { get; init; }
}
