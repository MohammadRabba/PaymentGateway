using FluentAssertions;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Rules;

namespace PaymentGateway.UnitTests.Domain;

public class PaymentStateMachineTests
{
    [Theory]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Processing)]
    [InlineData(PaymentStatus.Processing, PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Processing, PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Processing, PaymentStatus.Unknown)]
    [InlineData(PaymentStatus.Unknown, PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Unknown, PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Authorized, PaymentStatus.Settled)]
    [InlineData(PaymentStatus.Settled, PaymentStatus.PartiallyRefunded)]
    [InlineData(PaymentStatus.Settled, PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.PartiallyRefunded, PaymentStatus.PartiallyRefunded)]
    [InlineData(PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded)]
    public void CanTransition_returns_true_for_valid_transitions(PaymentStatus from, PaymentStatus to)
    {
        PaymentStateMachine.CanTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Settled)]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Processing, PaymentStatus.Settled)]
    [InlineData(PaymentStatus.Processing, PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Authorized, PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Authorized, PaymentStatus.Unknown)]
    [InlineData(PaymentStatus.Settled, PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Settled, PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Failed, PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Failed, PaymentStatus.Processing)]
    [InlineData(PaymentStatus.Refunded, PaymentStatus.PartiallyRefunded)]
    [InlineData(PaymentStatus.Refunded, PaymentStatus.Settled)]
    public void CanTransition_returns_false_for_invalid_transitions(PaymentStatus from, PaymentStatus to)
    {
        PaymentStateMachine.CanTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void AssertTransition_throws_for_invalid_transition()
    {
        var act = () => PaymentStateMachine.AssertTransition(PaymentStatus.Pending, PaymentStatus.Settled);
        act.Should().Throw<InvalidPaymentStateException>()
            .Which.CurrentStatus.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public void AssertTransition_does_not_throw_for_valid_transition()
    {
        var act = () => PaymentStateMachine.AssertTransition(PaymentStatus.Authorized, PaymentStatus.Settled);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(PaymentStatus.Failed, true)]
    [InlineData(PaymentStatus.Refunded, true)]
    [InlineData(PaymentStatus.Pending, false)]
    [InlineData(PaymentStatus.Processing, false)]
    [InlineData(PaymentStatus.Authorized, false)]
    [InlineData(PaymentStatus.Settled, false)]
    [InlineData(PaymentStatus.PartiallyRefunded, false)]
    [InlineData(PaymentStatus.Unknown, false)]
    public void IsTerminal_returns_correct_value(PaymentStatus status, bool expected)
    {
        PaymentStateMachine.IsTerminal(status).Should().Be(expected);
    }

    [Fact]
    public void AllowedTransitions_returns_empty_for_terminal_states()
    {
        PaymentStateMachine.AllowedTransitions(PaymentStatus.Failed).Should().BeEmpty();
        PaymentStateMachine.AllowedTransitions(PaymentStatus.Refunded).Should().BeEmpty();
    }

    [Fact]
    public void AllowedTransitions_returns_all_valid_targets()
    {
        var allowed = PaymentStateMachine.AllowedTransitions(PaymentStatus.Processing);
        allowed.Should().Contain(new[] { PaymentStatus.Authorized, PaymentStatus.Failed, PaymentStatus.Unknown });
        allowed.Should().HaveCount(3);
    }
}
