using System.Globalization;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Domain.ValueObjects;

/// <summary>
/// Monetary amount + currency. The only monetary representation in the system.
/// No float/double ever crosses a money boundary. All arithmetic produces new Money values;
/// mismatched currencies throw <see cref="CurrencyMismatchException"/>. Rounding is explicit
/// via <see cref="Round"/>, never implicit. SQL storage uses DECIMAL(19,4).
/// </summary>
public readonly record struct Money
{
    public decimal Amount { get; }

    public Currency Currency { get; }

    public Money(decimal amount, Currency currency)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Money amount must be non-negative.", nameof(amount));
        }

        Amount = amount;
        Currency = currency;
    }

    public static Money Zero(Currency currency) => new(0m, currency);

    public bool IsZero => Amount == 0m;

    public Money Add(Money other)
    {
        AssertSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        AssertSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor)
    {
        if (factor < 0)
        {
            throw new ArgumentException("Money multiplication factor must be non-negative.", nameof(factor));
        }

        return new Money(Amount * factor, Currency);
    }

    /// <summary>
    /// Explicit Banker's rounding at the currency's minor-unit precision. Never silent.
    /// Use this only at the boundary where a value must be expressed in minor units.
    /// </summary>
    public Money Round()
    {
        var rounded = Math.Round(Amount, Currency.DecimalPlaces, MidpointRounding.ToEven);
        return new Money(rounded, Currency);
    }

    public Money Min(Money other)
    {
        AssertSameCurrency(other);
        return new Money(Math.Min(Amount, other.Amount), Currency);
    }

    public int CompareTo(Money other)
    {
        AssertSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    public static bool operator >(Money left, Money right)
    {
        left.AssertSameCurrency(right);
        return left.Amount > right.Amount;
    }

    public static bool operator <(Money left, Money right)
    {
        left.AssertSameCurrency(right);
        return left.Amount < right.Amount;
    }

    public static bool operator >=(Money left, Money right)
    {
        left.AssertSameCurrency(right);
        return left.Amount >= right.Amount;
    }

    public static bool operator <=(Money left, Money right)
    {
        left.AssertSameCurrency(right);
        return left.Amount <= right.Amount;
    }

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator *(Money left, decimal factor) => left.Multiply(factor);

    private void AssertSameCurrency(Money other)
    {
        if (!string.Equals(Currency.Code, other.Currency.Code, StringComparison.Ordinal))
        {
            throw new CurrencyMismatchException(Currency.Code, other.Currency.Code);
        }
    }

    public override string ToString() =>
        $"{Amount.ToString("0.####", CultureInfo.InvariantCulture)} {Currency.Code}";
}
