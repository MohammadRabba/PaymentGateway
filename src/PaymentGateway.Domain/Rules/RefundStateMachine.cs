using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Domain.Rules;

/// <summary>
/// Pure domain rule for Refund state transitions. Refunds are first-class operations
/// with their own state machine; failures are deterministic and never ambiguous.
/// </summary>
public static class RefundStateMachine
{
    private static readonly Dictionary<RefundStatus, IReadOnlySet<RefundStatus>> Transitions =
        new Dictionary<RefundStatus, IReadOnlySet<RefundStatus>>
        {
            [RefundStatus.Pending] = new HashSet<RefundStatus>
            {
                RefundStatus.Completed,
                RefundStatus.Failed,
            },
        };

    public static bool CanTransition(RefundStatus from, RefundStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static void AssertTransition(RefundStatus from, RefundStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidRefundStateException(from, to);
        }
    }

    public static bool IsTerminal(RefundStatus status) =>
        status is RefundStatus.Completed or RefundStatus.Failed;
}
