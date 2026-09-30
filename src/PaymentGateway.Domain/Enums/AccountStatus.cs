namespace PaymentGateway.Domain.Enums;

/// <summary>
/// Lifecycle status of a financial account. Frozen accounts cannot be posted to.
/// </summary>
public enum AccountStatus
{
    Active = 1,
    Frozen = 2,
}
