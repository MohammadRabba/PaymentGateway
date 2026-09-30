namespace PaymentGateway.Domain.Enums;

/// <summary>
/// Operation type. Used to scope idempotency keys so a payment key and a refund key never collide
/// even if the merchant reuses the same client-generated key value.
/// </summary>
public enum OperationType
{
    Payment = 1,
    Refund = 2,
    Settlement = 3,
}
