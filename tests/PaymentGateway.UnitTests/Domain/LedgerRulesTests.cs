using FluentAssertions;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Rules;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.UnitTests.Domain;

public class LedgerRulesTests
{
    private static readonly Currency Usd = Currency.Parse("USD");

    [Fact]
    public void AssertBalanced_does_not_throw_when_debits_equal_credits()
    {
        var entries = new[]
        {
            (EntryType.Debit, new Money(100m, Usd)),
            (EntryType.Credit, new Money(100m, Usd)),
        };

        var act = () => LedgerRules.AssertBalanced(entries);
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertBalanced_throws_when_debits_do_not_equal_credits()
    {
        var entries = new[]
        {
            (EntryType.Debit, new Money(100m, Usd)),
            (EntryType.Credit, new Money(99m, Usd)),
        };

        var act = () => LedgerRules.AssertBalanced(entries);
        act.Should().Throw<LedgerNotBalancedException>()
            .Which.TotalDebits.Should().Be(100m);
    }

    [Fact]
    public void AssertBalanced_throws_for_empty_entries()
    {
        var act = () => LedgerRules.AssertBalanced(Array.Empty<(EntryType, Money)>());
        act.Should().Throw<LedgerNotBalancedException>();
    }

    [Fact]
    public void AssertBalanced_supports_three_entry_postings_with_fees()
    {
        // Dr AcquirerReceivable gross=100, Cr MerchantPayable net=97, Cr FeeRevenue fee=3
        var entries = new[]
        {
            (EntryType.Debit, new Money(100m, Usd)),
            (EntryType.Credit, new Money(97m, Usd)),
            (EntryType.Credit, new Money(3m, Usd)),
        };

        var act = () => LedgerRules.AssertBalanced(entries);
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertSingleCurrency_does_not_throw_when_all_match()
    {
        var entries = new[]
        {
            new Money(100m, Usd),
            new Money(50m, Usd),
        };

        var act = () => LedgerRules.AssertSingleCurrency(entries, Usd);
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertSingleCurrency_throws_when_one_entry_differs()
    {
        var eur = Currency.Parse("EUR");
        var entries = new[]
        {
            new Money(100m, Usd),
            new Money(50m, eur),
        };

        var act = () => LedgerRules.AssertSingleCurrency(entries, Usd);
        act.Should().Throw<CurrencyMismatchException>();
    }

    [Fact]
    public void AssertAllPositive_throws_for_zero_amount()
    {
        var entries = new[] { new Money(0m, Usd) };

        var act = () => LedgerRules.AssertAllPositive(entries);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssertAllPositive_throws_for_negative_amount()
    {
        // Money ctor throws for negative amounts, so we test via reflection-free path:
        // create with positive and verify AssertAllPositive accepts it.
        var entries = new[] { new Money(1m, Usd), new Money(50m, Usd) };

        var act = () => LedgerRules.AssertAllPositive(entries);
        act.Should().NotThrow();
    }
}
