namespace PaymentGateway.Domain.Enums;

/// <summary>
/// Type of a ledger transaction. All entries within one transaction must use the same currency.
/// Corrections are append-only — never update or delete historical entries.
/// </summary>
public enum LedgerTransactionType
{
    PaymentSettlement = 1,
    Refund = 2,
    Fee = 3,
    Correction = 4,
}
