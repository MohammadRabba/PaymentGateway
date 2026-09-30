namespace PaymentGateway.Domain.Enums;

/// <summary>
/// State of an idempotency record. State machine: Processing → {Completed | Failed}.
/// Abandoned Processing records are detected by ExpiresAt and reclaimed safely by the recovery path.
/// </summary>
public enum IdempotencyState
{
    Processing = 1,
    Completed = 2,
    Failed = 3,
}
