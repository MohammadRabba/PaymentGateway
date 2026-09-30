using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Application.Exceptions;

/// <summary>
/// Thrown when a referenced entity does not exist. Maps to HTTP 404 Not Found.
/// </summary>
public sealed class PaymentNotFoundException : DomainException
{
    public Guid PaymentId { get; }

    public PaymentNotFoundException(Guid paymentId)
        : base($"Payment '{paymentId}' was not found.")
    {
        PaymentId = paymentId;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/payment-not-found";
    public override int HttpStatusCode => 404;
}
