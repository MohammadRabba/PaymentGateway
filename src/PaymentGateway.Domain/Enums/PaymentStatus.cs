namespace PaymentGateway.Domain.Enums;

/// <summary>
/// Payment lifecycle. Transitions are validated by PaymentStateMachine.
/// Authorisation (acquirer) and Settlement (internal ledger) are distinct steps:
/// Authorized means the acquirer approved; Settled means the ledger has been posted.
/// </summary>
public enum PaymentStatus
{
    Pending = 1,
    Processing = 2,
    Authorized = 3,
    Unknown = 4,
    Settled = 5,
    PartiallyRefunded = 6,
    Refunded = 7,
    Failed = 8,
}
