using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Application.Exceptions;

/// <summary>
/// Thrown when a refund is requested but not found. Maps to HTTP 404 Not Found.
/// </summary>
public sealed class RefundNotFoundException : DomainException
{
    public Guid RefundId { get; }

    public RefundNotFoundException(Guid refundId)
        : base($"Refund '{refundId}' was not found.")
    {
        RefundId = refundId;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/refund-not-found";
    public override int HttpStatusCode => 404;
}
