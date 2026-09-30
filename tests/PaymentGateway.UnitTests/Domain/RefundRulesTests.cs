using FluentAssertions;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Rules;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.UnitTests.Domain;

public class RefundRulesTests
{
    private static readonly Currency Usd = Currency.Parse("USD");

    [Fact]
    public void AssertCanRefund_does_not_throw_when_request_is_within_settled_amount()
    {
        var settled = new Money(100m, Usd);
        var already = new Money(0m, Usd);
        var requested = new Money(100m, Usd);

        var act = () => RefundRules.AssertCanRefund(Guid.NewGuid(), PaymentStatus.Settled, settled, already, requested);
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertCanRefund_does_not_throw_for_partial_refund_on_partially_refunded_payment()
    {
        var settled = new Money(100m, Usd);
        var already = new Money(60m, Usd);
        var requested = new Money(40m, Usd);

        var act = () => RefundRules.AssertCanRefund(Guid.NewGuid(), PaymentStatus.PartiallyRefunded, settled, already, requested);
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertCanRefund_throws_when_requested_exceeds_remaining()
    {
        var settled = new Money(100m, Usd);
        var already = new Money(60m, Usd);
        var requested = new Money(50m, Usd);

        var act = () => RefundRules.AssertCanRefund(Guid.NewGuid(), PaymentStatus.Settled, settled, already, requested);
        act.Should().Throw<RefundExceedsSettledAmountException>();
    }

    [Fact]
    public void AssertCanRefund_throws_when_payment_is_not_in_refundable_state()
    {
        var settled = new Money(100m, Usd);
        var already = new Money(0m, Usd);
        var requested = new Money(50m, Usd);

        var act = () => RefundRules.AssertCanRefund(Guid.NewGuid(), PaymentStatus.Authorized, settled, already, requested);
        act.Should().Throw<InvalidPaymentStateException>();
    }

    [Fact]
    public void AssertCanRefund_throws_for_currency_mismatch()
    {
        var eur = Currency.Parse("EUR");
        var settled = new Money(100m, eur);
        var already = new Money(0m, Usd);
        var requested = new Money(50m, eur);

        var act = () => RefundRules.AssertCanRefund(Guid.NewGuid(), PaymentStatus.Settled, settled, already, requested);
        act.Should().Throw<CurrencyMismatchException>();
    }

    [Fact]
    public void WouldFullyRefund_returns_true_when_sum_equals_settled()
    {
        var settled = new Money(100m, Usd);
        var already = new Money(30m, Usd);
        var requested = new Money(70m, Usd);

        RefundRules.WouldFullyRefund(settled, already, requested).Should().BeTrue();
    }

    [Fact]
    public void WouldFullyRefund_returns_false_when_sum_is_less_than_settled()
    {
        var settled = new Money(100m, Usd);
        var already = new Money(30m, Usd);
        var requested = new Money(50m, Usd);

        RefundRules.WouldFullyRefund(settled, already, requested).Should().BeFalse();
    }
}
