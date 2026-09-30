namespace PaymentGateway.Domain.Enums;

/// <summary>
/// Lifecycle status of a merchant. Only Active merchants can initiate payments.
/// </summary>
public enum MerchantStatus
{
    Active = 1,
    Suspended = 2,
    Closed = 3,
}
