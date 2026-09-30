using FluentAssertions;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Rules;

namespace PaymentGateway.UnitTests.Domain;

public class RefundStateMachineTests
{
    [Theory]
    [InlineData(RefundStatus.Pending, RefundStatus.Completed)]
    [InlineData(RefundStatus.Pending, RefundStatus.Failed)]
    public void CanTransition_returns_true_for_valid_transitions(RefundStatus from, RefundStatus to)
    {
        RefundStateMachine.CanTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(RefundStatus.Completed, RefundStatus.Pending)]
    [InlineData(RefundStatus.Completed, RefundStatus.Failed)]
    [InlineData(RefundStatus.Failed, RefundStatus.Pending)]
    [InlineData(RefundStatus.Failed, RefundStatus.Completed)]
    [InlineData(RefundStatus.Pending, RefundStatus.Pending)]
    public void CanTransition_returns_false_for_invalid_transitions(RefundStatus from, RefundStatus to)
    {
        RefundStateMachine.CanTransition(from, to).Should().BeFalse();
    }

    [Theory]
    [InlineData(RefundStatus.Completed, true)]
    [InlineData(RefundStatus.Failed, true)]
    [InlineData(RefundStatus.Pending, false)]
    public void IsTerminal_returns_correct_value(RefundStatus status, bool expected)
    {
        RefundStateMachine.IsTerminal(status).Should().Be(expected);
    }

    [Fact]
    public void AssertTransition_does_not_throw_for_valid_transition()
    {
        var act = () => RefundStateMachine.AssertTransition(RefundStatus.Pending, RefundStatus.Completed);
        act.Should().NotThrow();
    }
}
