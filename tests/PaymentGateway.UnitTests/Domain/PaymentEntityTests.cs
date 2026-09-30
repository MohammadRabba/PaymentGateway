using FluentAssertions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.UnitTests.Domain;

public class PaymentEntityTests
{
    private static readonly Currency Usd = Currency.Parse("USD");

    [Fact]
    public void Constructor_sets_initial_state_to_Pending()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), "key-1", OperationType.Payment, new Money(100m, Usd), DateTimeOffset.UtcNow);
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.TotalRefunded.Should().Be(0m);
    }

    [Fact]
    public void BeginProcessing_transitions_Pending_to_Processing_and_sets_heartbeat()
    {
        var payment = NewPayment();
        var at = DateTimeOffset.UtcNow;

        payment.BeginProcessing(at);

        payment.Status.Should().Be(PaymentStatus.Processing);
        payment.HeartbeatAt.Should().Be(at);
    }

    [Fact]
    public void MarkAuthorized_transitions_Processing_to_Authorized()
    {
        var payment = NewPayment();
        payment.BeginProcessing(DateTimeOffset.UtcNow);

        var auth = new Authorization
        {
            AuthCode = "ABC123",
            AcquirerReference = "ACQ-123",
            Outcome = AcquirerOutcome.Success,
            AuthorizedAt = DateTimeOffset.UtcNow,
        };

        payment.MarkAuthorized(auth, DateTimeOffset.UtcNow);

        payment.Status.Should().Be(PaymentStatus.Authorized);
        payment.AuthCode.Should().Be("ABC123");
        payment.HeartbeatAt.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_transitions_Processing_to_Failed_with_reason()
    {
        var payment = NewPayment();
        payment.BeginProcessing(DateTimeOffset.UtcNow);

        payment.MarkFailed("Insufficient funds", DateTimeOffset.UtcNow);

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Be("Insufficient funds");
    }

    [Fact]
    public void MarkUnknown_transitions_Processing_to_Unknown()
    {
        var payment = NewPayment();
        payment.BeginProcessing(DateTimeOffset.UtcNow);

        payment.MarkUnknown(DateTimeOffset.UtcNow);

        payment.Status.Should().Be(PaymentStatus.Unknown);
        payment.HeartbeatAt.Should().NotBeNull();
    }

    [Fact]
    public void Settle_transitions_Authorized_to_Settled()
    {
        var payment = NewProcessingPayment();
        MarkAuthorized(payment);

        payment.Settle(DateTimeOffset.UtcNow);

        payment.Status.Should().Be(PaymentStatus.Settled);
        payment.SettledAt.Should().NotBeNull();
    }

    [Fact]
    public void ApplyRefund_partial_transitions_Settled_to_PartiallyRefunded()
    {
        var payment = NewSettledPayment();
        var refund = new Money(30m, Usd);

        payment.ApplyRefund(refund, DateTimeOffset.UtcNow);

        payment.Status.Should().Be(PaymentStatus.PartiallyRefunded);
        payment.TotalRefunded.Should().Be(30m);
    }

    [Fact]
    public void ApplyRefund_full_transitions_Settled_to_Refunded()
    {
        var payment = NewSettledPayment();
        var refund = new Money(100m, Usd);

        payment.ApplyRefund(refund, DateTimeOffset.UtcNow);

        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.TotalRefunded.Should().Be(100m);
    }

    [Fact]
    public void ApplyRefund_throws_when_amount_exceeds_remaining()
    {
        var payment = NewSettledPayment();
        payment.ApplyRefund(new Money(60m, Usd), DateTimeOffset.UtcNow);

        var act = () => payment.ApplyRefund(new Money(50m, Usd), DateTimeOffset.UtcNow);
        act.Should().Throw<RefundExceedsSettledAmountException>();
    }

    [Fact]
    public void Constructor_rejects_zero_amount()
    {
        var act = () => new Payment(Guid.NewGuid(), Guid.NewGuid(), "k", OperationType.Payment, Money.Zero(Usd), DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_rejects_empty_idempotency_key()
    {
        var act = () => new Payment(Guid.NewGuid(), Guid.NewGuid(), "", OperationType.Payment, new Money(100m, Usd), DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentException>();
    }

    private static Payment NewPayment() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "key-1", OperationType.Payment, new Money(100m, Usd), DateTimeOffset.UtcNow);

    private static Payment NewProcessingPayment()
    {
        var p = NewPayment();
        p.BeginProcessing(DateTimeOffset.UtcNow);
        return p;
    }

    private static void MarkAuthorized(Payment payment)
    {
        var auth = new Authorization
        {
            AuthCode = "ABC",
            AcquirerReference = "ACQ-1",
            Outcome = AcquirerOutcome.Success,
            AuthorizedAt = DateTimeOffset.UtcNow,
        };
        payment.MarkAuthorized(auth, DateTimeOffset.UtcNow);
    }

    private static Payment NewSettledPayment()
    {
        var p = NewProcessingPayment();
        MarkAuthorized(p);
        p.Settle(DateTimeOffset.UtcNow);
        return p;
    }
}
