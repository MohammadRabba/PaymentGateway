using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.Domain.Rules;

/// <summary>
/// Pure domain rule enforcing refund invariants. Called both before posting (early rejection)
/// and inside the financial transaction (re-check) to defend against concurrent refund attempts.
/// </summary>
public static class RefundRules
{
    /// <summary>
    /// Invariant: TotalRefunded + requestedAmount &lt;= SettledAmount.
    /// Called both before posting (early rejection) and inside the transaction (re-check).
    /// </summary>
    public static void AssertCanRefund(
        Guid paymentId,
        PaymentStatus currentStatus,
        Money settledAmount,
        Money alreadyRefunded,
        Money requestedAmount)
    {
        if (currentStatus is not (PaymentStatus.Settled or PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidPaymentStateException(currentStatus, PaymentStatus.PartiallyRefunded);
        }

        if (!string.Equals(settledAmount.Currency.Code, alreadyRefunded.Currency.Code, StringComparison.Ordinal)
            || !string.Equals(settledAmount.Currency.Code, requestedAmount.Currency.Code, StringComparison.Ordinal))
        {
            throw new CurrencyMismatchException(
                settledAmount.Currency.Code,
                alreadyRefunded.Currency.Code);
        }

        var totalAfterRequested = alreadyRefunded.Add(requestedAmount);
        if (totalAfterRequested.Amount > settledAmount.Amount)
        {
            throw new RefundExceedsSettledAmountException(
                paymentId,
                settledAmount.Amount,
                alreadyRefunded.Amount,
                requestedAmount.Amount);
        }
    }

    /// <summary>
    /// True when adding requestedAmount to alreadyRefunded exactly equals settledAmount.
    /// </summary>
    public static bool WouldFullyRefund(
        Money settledAmount,
        Money alreadyRefunded,
        Money requestedAmount)
    {
        AssertSameCurrencies(settledAmount, alreadyRefunded, requestedAmount);
        return alreadyRefunded.Add(requestedAmount).Amount == settledAmount.Amount;
    }

    private static void AssertSameCurrencies(Money a, Money b, Money c)
    {
        if (!string.Equals(a.Currency.Code, b.Currency.Code, StringComparison.Ordinal)
            || !string.Equals(a.Currency.Code, c.Currency.Code, StringComparison.Ordinal))
        {
            throw new CurrencyMismatchException(a.Currency.Code, b.Currency.Code);
        }
    }
}
