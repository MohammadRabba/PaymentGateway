using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.Exceptions;

/// <summary>
/// Thrown when a payment state transition is illegal per PaymentStateMachine.
/// Maps to HTTP 409 Conflict.
/// </summary>
public sealed class InvalidPaymentStateException : DomainException
{
    public PaymentStatus CurrentStatus { get; }

    public PaymentStatus AttemptedStatus { get; }

    public InvalidPaymentStateException(PaymentStatus current, PaymentStatus attempted)
        : base($"Cannot transition payment from {current} to {attempted}.")
    {
        CurrentStatus = current;
        AttemptedStatus = attempted;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/invalid-payment-state";
    public override int HttpStatusCode => 409;
}
