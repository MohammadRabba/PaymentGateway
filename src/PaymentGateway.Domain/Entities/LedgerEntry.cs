using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.Entities;

/// <summary>
/// A single double-entry ledger line. Immutable by design — no mutation methods exist.
/// The only way to create a LedgerEntry is via LedgerTransaction.AddEntry, and the only way
/// to correct a posted entry is to post a compensating LedgerTransaction of type Correction.
/// </summary>
public sealed class LedgerEntry
{
    public Guid Id { get; private set; }

    public Guid LedgerTransactionId { get; private set; }

    public Guid AccountId { get; private set; }

    public EntryType EntryType { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    private LedgerEntry() { }

    internal LedgerEntry(
        Guid id,
        Guid ledgerTransactionId,
        Guid accountId,
        EntryType entryType,
        decimal amount,
        string currency,
        DateTimeOffset createdAt)
    {
        Id = id;
        LedgerTransactionId = ledgerTransactionId;
        AccountId = accountId;
        EntryType = entryType;
        Amount = amount;
        Currency = currency;
        CreatedAt = createdAt;
    }
}
