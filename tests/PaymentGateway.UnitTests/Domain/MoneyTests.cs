using FluentAssertions;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.UnitTests.Domain;

public class MoneyTests
{
    private static readonly Currency Usd = Currency.Parse("USD");
    private static readonly Currency Eur = Currency.Parse("EUR");

    [Fact]
    public void Constructor_accepts_zero_amount()
    {
        var money = new Money(0m, Usd);
        money.IsZero.Should().BeTrue();
    }

    [Fact]
    public void Constructor_rejects_negative_amount()
    {
        var act = () => new Money(-1m, Usd);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Add_returns_sum_with_same_currency()
    {
        var a = new Money(10m, Usd);
        var b = new Money(20m, Usd);
        var sum = a.Add(b);

        sum.Amount.Should().Be(30m);
        sum.Currency.Should().Be(Usd);
    }

    [Fact]
    public void Subtract_returns_difference_with_same_currency()
    {
        var a = new Money(30m, Usd);
        var b = new Money(10m, Usd);
        var diff = a.Subtract(b);

        diff.Amount.Should().Be(20m);
    }

    [Fact]
    public void Add_throws_for_mismatched_currencies()
    {
        var a = new Money(10m, Usd);
        var b = new Money(10m, Eur);

        var act = () => a.Add(b);
        act.Should().Throw<CurrencyMismatchException>();
    }

    [Fact]
    public void Multiply_scales_amount_by_factor()
    {
        var money = new Money(100m, Usd);
        var result = money.Multiply(0.025m);

        result.Amount.Should().Be(2.5m);
        result.Currency.Should().Be(Usd);
    }

    [Fact]
    public void Multiply_rejects_negative_factor()
    {
        var money = new Money(100m, Usd);
        var act = () => money.Multiply(-1m);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Round_uses_bankers_rounding_at_currency_precision()
    {
        // USD has 2 decimal places. 2.5 rounds to 2 (Banker's rounding, ToEven).
        var money = new Money(2.5m, Usd);
        var rounded = money.Round();
        rounded.Amount.Should().Be(2m);

        // 3.5 rounds to 4 (Banker's rounding, ToEven).
        var money2 = new Money(3.5m, Usd);
        money2.Round().Amount.Should().Be(4m);
    }

    [Fact]
    public void Round_respects_currency_decimal_places()
    {
        // JPY has 0 decimal places.
        var jpy = Currency.Parse("JPY");
        var money = new Money(2.5m, jpy);
        var rounded = money.Round();
        rounded.Amount.Should().Be(2m); // Banker's rounding at 0 decimal places.
    }

    [Fact]
    public void Zero_returns_money_with_zero_amount()
    {
        var zero = Money.Zero(Usd);
        zero.IsZero.Should().BeTrue();
        zero.Currency.Should().Be(Usd);
    }

    [Fact]
    public void Operators_compare_amounts_with_same_currency()
    {
        var a = new Money(10m, Usd);
        var b = new Money(20m, Usd);

        (a < b).Should().BeTrue();
        (a <= b).Should().BeTrue();
        (b > a).Should().BeTrue();
        (b >= a).Should().BeTrue();
    }

    [Fact]
    public void Operators_throw_for_mismatched_currencies()
    {
        var a = new Money(10m, Usd);
        var b = new Money(20m, Eur);

        var act = () => a > b;
        act.Should().Throw<CurrencyMismatchException>();
    }

    [Fact]
    public void Min_returns_smaller_amount()
    {
        var a = new Money(10m, Usd);
        var b = new Money(20m, Usd);

        a.Min(b).Amount.Should().Be(10m);
    }
}
