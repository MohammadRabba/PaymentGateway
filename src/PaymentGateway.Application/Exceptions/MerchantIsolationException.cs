using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Application.Exceptions;

/// <summary>
/// Thrown when an authenticated merchant tries to access a resource belonging to a different merchant.
/// Maps to HTTP 403 Forbidden. Never trust client-supplied merchant identity over authenticated identity.
/// </summary>
public sealed class MerchantIsolationException : DomainException
{
    public Guid AuthenticatedMerchantId { get; }

    public Guid RequestedMerchantId { get; }

    public MerchantIsolationException(Guid authenticatedMerchantId, Guid requestedMerchantId)
        : base($"Merchant '{authenticatedMerchantId}' cannot access resources belonging to merchant '{requestedMerchantId}'.")
    {
        AuthenticatedMerchantId = authenticatedMerchantId;
        RequestedMerchantId = requestedMerchantId;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/merchant-isolation";
    public override int HttpStatusCode => 403;
}
