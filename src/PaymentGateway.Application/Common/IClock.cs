namespace PaymentGateway.Application.Common;

/// <summary>
/// Clock abstraction so domain logic and handlers never call DateTime.UtcNow directly.
/// Enables deterministic time in tests and supports freezing the clock for invariant verification.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
