using FluentAssertions;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.UnitTests.Domain;

public class CurrencyTests
{
    [Theory]
    [InlineData("USD", 2)]
    [InlineData("EUR", 2)]
    [InlineData("GBP", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KWD", 3)]
    public void Parse_accepts_supported_codes(string code, int expectedDecimalPlaces)
    {
        var currency = Currency.Parse(code);
        currency.Code.Should().Be(code);
        currency.DecimalPlaces.Should().Be(expectedDecimalPlaces);
    }

    [Theory]
    [InlineData("usd")]   // lowercase normalises to uppercase
    [InlineData("Usd")]
    public void Parse_normalises_case(string input)
    {
        var currency = Currency.Parse(input);
        currency.Code.Should().Be("USD");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_rejects_empty_input(string? input)
    {
        var act = () => Currency.Parse(input!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("US")]     // too short
    [InlineData("USDD")]   // too long
    [InlineData("US1")]    // non-letter
    public void Parse_rejects_malformed_codes(string input)
    {
        var act = () => Currency.Parse(input);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("ABC")]
    public void Parse_rejects_unsupported_codes(string input)
    {
        var act = () => Currency.Parse(input);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryParse_returns_true_for_supported()
    {
        Currency.TryParse("USD", out var currency).Should().BeTrue();
        currency.Code.Should().Be("USD");
    }

    [Fact]
    public void TryParse_returns_false_for_unsupported()
    {
        Currency.TryParse("XYZ", out _).Should().BeFalse();
    }

    [Fact]
    public void IsSupported_returns_correct_value()
    {
        Currency.IsSupported("USD").Should().BeTrue();
        Currency.IsSupported("xyz").Should().BeFalse();
        Currency.IsSupported("").Should().BeFalse();
    }

    [Fact]
    public void Equality_with_string_compares_code()
    {
        var usd = Currency.Parse("USD");
        (usd == "USD").Should().BeTrue();
        (usd == "EUR").Should().BeFalse();
    }

    [Fact]
    public void ToString_returns_code()
    {
        Currency.Parse("USD").ToString().Should().Be("USD");
    }
}
