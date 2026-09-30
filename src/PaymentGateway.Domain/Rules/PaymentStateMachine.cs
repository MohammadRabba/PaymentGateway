using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Domain.Rules;

/// <summary>
/// Pure domain rule that defines valid Payment state transitions. No side effects.
/// Implemented as a static class so it can be unit-tested exhaustively without instantiation.
/// </summary>
public static class PaymentStateMachine
{
    private static readonly Dictionary<PaymentStatus, IReadOnlySet<PaymentStatus>> Transitions =
        new Dictionary<PaymentStatus, IReadOnlySet<PaymentStatus>>
        {
            [PaymentStatus.Pending] = new HashSet<PaymentStatus>
            {
                PaymentStatus.Processing,
            },
            [PaymentStatus.Processing] = new HashSet<PaymentStatus>
            {
                PaymentStatus.Authorized,
                PaymentStatus.Failed,
                PaymentStatus.Unknown,
            },
            [PaymentStatus.Unknown] = new HashSet<PaymentStatus>
            {
                PaymentStatus.Authorized,
                PaymentStatus.Failed,
            },
            [PaymentStatus.Authorized] = new HashSet<PaymentStatus>
            {
                PaymentStatus.Settled,
            },
            [PaymentStatus.Settled] = new HashSet<PaymentStatus>
            {
                PaymentStatus.PartiallyRefunded,
                PaymentStatus.Refunded,
            },
            [PaymentStatus.PartiallyRefunded] = new HashSet<PaymentStatus>
            {
                PaymentStatus.PartiallyRefunded,
                PaymentStatus.Refunded,
            },
        };

    public static bool CanTransition(PaymentStatus from, PaymentStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>
    /// Throws InvalidPaymentStateException if the transition is not allowed.
    /// </summary>
    public static void AssertTransition(PaymentStatus from, PaymentStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidPaymentStateException(from, to);
        }
    }

    public static IReadOnlySet<PaymentStatus> AllowedTransitions(PaymentStatus from) =>
        Transitions.TryGetValue(from, out var allowed)
            ? allowed
            : new HashSet<PaymentStatus>();

    public static bool IsTerminal(PaymentStatus status) =>
        status is PaymentStatus.Failed or PaymentStatus.Refunded;
}
