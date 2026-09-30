using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.Domain.Rules;

/// <summary>
/// Hard accounting invariants enforced inside the financial transaction before commit.
/// All checks are pure: they throw on violation and have no side effects.
/// </summary>
public static class LedgerRules
{
    /// <summary>
    /// Invariant: SUM(debits) == SUM(credits) for every posted ledger transaction.
    /// Currencies are checked separately; this only sums amounts.
    /// </summary>
    public static void AssertBalanced(IReadOnlyCollection<(EntryType Type, Money Amount)> entries)
    {
        if (entries.Count == 0)
        {
            throw new LedgerNotBalancedException(0m, 0m);
        }

        var debits = 0m;
        var credits = 0m;
        foreach (var (type, amount) in entries)
        {
            if (type == EntryType.Debit) debits += amount.Amount;
            else if (type == EntryType.Credit) credits += amount.Amount;
        }

        if (debits != credits)
        {
            throw new LedgerNotBalancedException(debits, credits);
        }
    }

    /// <summary>
    /// Invariant: every entry within a ledger transaction uses the same currency,
    /// matching the parent transaction's currency. Prevents accidental cross-currency postings.
    /// </summary>
    public static void AssertSingleCurrency(IReadOnlyCollection<Money> entries, Currency transactionCurrency)
    {
        var expected = transactionCurrency.Code;
        foreach (var entry in entries)
        {
            if (!string.Equals(entry.Currency.Code, expected, StringComparison.Ordinal))
            {
                throw new CurrencyMismatchException(expected, entry.Currency.Code);
            }
        }
    }

    /// <summary>
    /// Invariant: every amount in a posted ledger transaction must be positive.
    /// Zero or negative entries break the double-entry model.
    /// </summary>
    public static void AssertAllPositive(IReadOnlyCollection<Money> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Amount <= 0m)
            {
                throw new ArgumentException($"Ledger entry amount must be positive, got {entry.Amount} {entry.Currency.Code}.");
            }
        }
    }
}
