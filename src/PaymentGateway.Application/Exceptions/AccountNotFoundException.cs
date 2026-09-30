using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Application.Exceptions;

/// <summary>
/// Thrown when a referenced account does not exist. Maps to HTTP 404 Not Found.
/// </summary>
public sealed class AccountNotFoundException : DomainException
{
    public Guid AccountId { get; }

    public AccountNotFoundException(Guid accountId)
        : base($"Account '{accountId}' was not found.")
    {
        AccountId = accountId;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/account-not-found";
    public override int HttpStatusCode => 404;
}
