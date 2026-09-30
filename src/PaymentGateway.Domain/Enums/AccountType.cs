namespace PaymentGateway.Domain.Enums;

/// <summary>
/// Financial account type. Distinct account per type ensures the double-entry postings
/// route to the correct ledger bucket. A merchant has at most one Account per (AccountType, Currency).
/// </summary>
public enum AccountType
{
    /// <summary>What the gateway owes the merchant — credited on settlement, debited on refund.</summary>
    MerchantPayable = 1,

    /// <summary>What the acquirer will deposit to the gateway — debited on settlement, credited on refund.</summary>
    AcquirerReceivable = 2,

    /// <summary>Gateway fee revenue — credited when fees are charged on settlement.</summary>
    FeeRevenue = 3,
}
