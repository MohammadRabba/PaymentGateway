namespace PaymentGateway.Domain.Exceptions;

/// <summary>
/// Thrown when the recovery worker queried the acquirer about an Unknown-state payment
/// but the acquirer itself returned Unknown. Maps to HTTP 409 — operation must remain in Unknown
/// and be retried later.
/// </summary>
public sealed class UnknownAcquirerOutcomeException : DomainException
{
    public Guid PaymentId { get; }

    public UnknownAcquirerOutcomeException(Guid paymentId)
        : base($"Acquirer outcome for payment {paymentId} is still unknown after recovery attempt.")
    {
        PaymentId = paymentId;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/unknown-acquirer-outcome";
    public override int HttpStatusCode => 409;
}
