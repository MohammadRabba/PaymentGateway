namespace PaymentGateway.Domain.Exceptions;

/// <summary>
/// Thrown when a Money arithmetic operation is attempted across two different currencies.
/// No silent conversion; the caller must explicitly convert first.
/// </summary>
public sealed class CurrencyMismatchException : DomainException
{
    public string LeftCurrency { get; }

    public string RightCurrency { get; }

    public CurrencyMismatchException(string leftCurrency, string rightCurrency)
        : base($"Currency mismatch: {leftCurrency} != {rightCurrency}.")
    {
        LeftCurrency = leftCurrency;
        RightCurrency = rightCurrency;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/currency-mismatch";
    public override int HttpStatusCode => 422;
}
