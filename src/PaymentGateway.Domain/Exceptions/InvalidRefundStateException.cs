using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.Exceptions;

/// <summary>
/// Thrown when a refund state transition is illegal per RefundStateMachine. Maps to HTTP 409 Conflict.
/// </summary>
public sealed class InvalidRefundStateException : DomainException
{
    public RefundStatus CurrentStatus { get; }

    public RefundStatus AttemptedStatus { get; }

    public InvalidRefundStateException(RefundStatus current, RefundStatus attempted)
        : base($"Cannot transition refund from {current} to {attempted}.")
    {
        CurrentStatus = current;
        AttemptedStatus = attempted;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/invalid-refund-state";
    public override int HttpStatusCode => 409;
}
