namespace PaymentGateway.Domain.Enums;

/// <summary>
/// Refund lifecycle. Refunds are first-class operations with their own idempotency keys and state.
/// </summary>
public enum RefundStatus
{
    Pending = 1,
    Completed = 2,
    Failed = 3,
}
