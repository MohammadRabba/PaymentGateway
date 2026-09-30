using PaymentGateway.Application.Common;

namespace PaymentGateway.Infrastructure.Time;

/// <summary>
/// Production clock. Returns the real UTC now. Tests inject a fake IClock for deterministic time.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
