namespace PaymentGateway.Domain.Enums;

/// <summary>
/// Outcome of an acquirer authorization request. The Unknown state is critical for safety:
/// when the request was sent but no response was received (timeout), the acquirer may have
/// already authorised. Blind re-authorization is forbidden; the recovery worker must query status.
/// </summary>
public enum AcquirerOutcome
{
    Success = 1,
    Decline = 2,
    Timeout = 3,
    Unknown = 4,
}
