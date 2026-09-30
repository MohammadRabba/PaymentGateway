using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Application.Exceptions;

/// <summary>
/// Thrown when a merchant does not have the required account type for a currency. This is a
/// configuration error (merchant onboarding did not create the account). Maps to HTTP 500
/// because it indicates an internal misconfiguration, not a client-correctable input.
/// </summary>
public sealed class AccountNotConfiguredException : DomainException
{
    public Guid MerchantId { get; }

    public AccountType AccountType { get; }

    public string Currency { get; }

    public AccountNotConfiguredException(Guid merchantId, AccountType accountType, string currency)
        : base($"Merchant '{merchantId}' does not have a {accountType} account in {currency}. Onboarding is misconfigured.")
    {
        MerchantId = merchantId;
        AccountType = accountType;
        Currency = currency;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/account-not-configured";
    public override int HttpStatusCode => 500;
}
