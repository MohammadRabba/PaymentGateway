namespace PaymentGateway.Domain.Enums;

/// <summary>
/// Outbox message lifecycle. Poisoned messages are mirrored into OutboxDeadLetter for inspection
/// and never silently deleted.
/// </summary>
public enum OutboxStatus
{
    Pending = 1,
    Claimed = 2,
    Published = 3,
    Poisoned = 4,
}
