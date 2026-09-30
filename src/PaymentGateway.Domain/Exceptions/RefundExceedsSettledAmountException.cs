namespace PaymentGateway.Domain.Exceptions;

/// <summary>
/// Thrown when a refund request would exceed the settled amount available for refund.
/// Enforces the TotalRefunded &lt;= SettledAmount invariant.
/// </summary>
public sealed class RefundExceedsSettledAmountException : DomainException
{
    public Guid PaymentId { get; }

    public decimal SettledAmount { get; }

    public decimal AlreadyRefunded { get; }

    public decimal RequestedAmount { get; }

    public RefundExceedsSettledAmountException(
        Guid paymentId,
        decimal settledAmount,
        decimal alreadyRefunded,
        decimal requestedAmount)
        : base($"Refund of {requestedAmount} on payment {paymentId} exceeds available amount. Settled {settledAmount}, already refunded {alreadyRefunded}.")
    {
        PaymentId = paymentId;
        SettledAmount = settledAmount;
        AlreadyRefunded = alreadyRefunded;
        RequestedAmount = requestedAmount;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/refund-exceeds-settled-amount";
    public override int HttpStatusCode => 422;
}
