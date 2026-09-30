using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Application.Exceptions;

/// <summary>
/// Thrown when a referenced merchant does not exist. Maps to HTTP 404 Not Found.
/// </summary>
public sealed class MerchantNotFoundException : DomainException
{
    public Guid MerchantId { get; }

    public MerchantNotFoundException(Guid merchantId)
        : base($"Merchant '{merchantId}' was not found.")
    {
        MerchantId = merchantId;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/merchant-not-found";
    public override int HttpStatusCode => 404;
}
