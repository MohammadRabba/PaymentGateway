using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Application.Exceptions;

/// <summary>
/// Thrown when a ledger transaction is requested but not found for the authenticated merchant.
/// Maps to HTTP 404 Not Found.
/// </summary>
public sealed class LedgerTransactionNotFoundException : DomainException
{
    public Guid LedgerTransactionId { get; }

    public LedgerTransactionNotFoundException(Guid ledgerTransactionId)
        : base($"Ledger transaction '{ledgerTransactionId}' was not found.")
    {
        LedgerTransactionId = ledgerTransactionId;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/ledger-transaction-not-found";
    public override int HttpStatusCode => 404;
}
