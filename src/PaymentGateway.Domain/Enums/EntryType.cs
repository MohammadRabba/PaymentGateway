namespace PaymentGateway.Domain.Enums;

/// <summary>
/// Direction of a ledger entry. Debit increases asset/expense accounts, Credit increases liability/revenue/equity accounts.
/// Invariant: SUM(Debits) == SUM(Credits) for every ledger transaction.
/// </summary>
public enum EntryType
{
    Debit = 1,
    Credit = 2,
}
