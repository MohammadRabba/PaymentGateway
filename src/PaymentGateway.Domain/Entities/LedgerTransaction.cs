using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.Domain.Entities;

/// <summary>
/// Aggregate root: LedgerTransaction. A balanced set of ledger entries posted atomically.
/// All entries must share the same Currency. The transaction is append-only — once committed,
/// it cannot be modified or deleted. Corrections are new LedgerTransactions of type Correction.
/// The Entries collection is exposed read-only and populated by EF Core via the backing field.
/// </summary>
public sealed class LedgerTransaction
{
    public Guid Id { get; private set; }

    public Guid? PaymentId { get; private set; }

    public Guid? RefundId { get; private set; }

    public LedgerTransactionType Type { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public string CorrelationId { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<LedgerEntry> _entries = [];

    // Returns the same _entries reference each call — EF populates this via the backing field.
    public IReadOnlyCollection<LedgerEntry> Entries => _entries;

    private LedgerTransaction() { }

    public LedgerTransaction(
        Guid id,
        LedgerTransactionType type,
        Currency currency,
        string correlationId,
        DateTimeOffset createdAt,
        Guid? paymentId = null,
        Guid? refundId = null)
    {
        Id = id;
        Type = type;
        Currency = currency.Code;
        CorrelationId = correlationId;
        CreatedAt = createdAt;
        PaymentId = paymentId;
        RefundId = refundId;
    }

    public void AddEntry(Guid entryId, Guid accountId, EntryType entryType, Money amount, DateTimeOffset createdAt)
    {
        if (!string.Equals(amount.Currency.Code, Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Ledger entry currency {amount.Currency.Code} does not match transaction currency {Currency}.");
        }

        _entries.Add(new LedgerEntry(entryId, Id, accountId, entryType, amount.Amount, Currency, createdAt));
    }
}
