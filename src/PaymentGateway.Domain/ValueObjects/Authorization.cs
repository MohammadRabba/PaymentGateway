using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.ValueObjects;

/// <summary>
/// Snapshot of an acquirer authorization response. Embedded on the Payment aggregate so the
/// recovery worker can query the acquirer using <see cref="AcquirerReference"/> without
/// re-issuing an authorization request.
/// </summary>
public sealed record Authorization
{
    public required string AuthCode { get; init; }

    public required string AcquirerReference { get; init; }

    public required AcquirerOutcome Outcome { get; init; }

    public DateTimeOffset AuthorizedAt { get; init; }

    /// <summary>
    /// Decline reason when Outcome == Decline. Null otherwise.
    /// </summary>
    public string? DeclineReason { get; init; }
}
