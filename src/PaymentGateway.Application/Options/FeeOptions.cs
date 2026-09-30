namespace PaymentGateway.Application.Options;

/// <summary>
/// Fee configuration. When CalculateFeesEnabled is true, settlement posts a three-entry ledger
/// (Dr AcquirerReceivable gross, Cr MerchantPayable net, Cr FeeRevenue fee). When false, it posts
/// a two-entry ledger (Dr AcquirerReceivable gross, Cr MerchantPayable gross).
/// </summary>
public sealed class FeeOptions
{
    public const string SectionName = "Fees";

    /// <summary>When true, fees are charged on settlement. Default false for simplicity.</summary>
    public bool CalculateFeesEnabled { get; init; }

    /// <summary>Fee percentage of gross amount. E.g., 2.5 means 2.5% fee. Must be non-negative.</summary>
    public decimal FeePercent { get; init; } = 0m;

    /// <summary>How fees are handled on refunds. Proportional reverses the fee proportionally.</summary>
    public RefundFeeBehaviour RefundFeeBehaviour { get; init; } = RefundFeeBehaviour.Proportional;
}

public enum RefundFeeBehaviour
{
    /// <summary>Reverse the fee proportionally to the refund amount.</summary>
    Proportional = 1,

    /// <summary>Do not reverse fees on refund.</summary>
    None = 2,
}
