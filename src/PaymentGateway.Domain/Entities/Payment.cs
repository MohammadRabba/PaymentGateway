using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Rules;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.Domain.Entities;

/// <summary>
/// Aggregate root: Payment. Authorisation and Settlement are distinct transitions: Authorized means
/// the acquirer approved; Settled means the ledger has been posted. Unknown means the acquirer
/// outcome is uncertain — recovery worker queries GetAuthorizationStatus before deciding.
/// </summary>
public sealed class Payment
{
    public Guid Id { get; private set; }

    public Guid MerchantId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public OperationType Operation { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public PaymentStatus Status { get; private set; }

    public string? AuthCode { get; private set; }

    public string? AcquirerReference { get; private set; }

    public decimal TotalRefunded { get; private set; }

    /// <summary>
    /// Refreshed while in Processing. Recovery worker detects stuck payments via HeartbeatAt &lt; now - timeout.
    /// </summary>
    public DateTimeOffset? HeartbeatAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? AuthorizedAt { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public string? FailureReason { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    private Payment() { }

    public Payment(
        Guid id,
        Guid merchantId,
        string idempotencyKey,
        OperationType operation,
        Money amount,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey must not be empty.", nameof(idempotencyKey));
        }

        if (amount.IsZero)
        {
            throw new ArgumentException("Payment amount must be positive.", nameof(amount));
        }

        Id = id;
        MerchantId = merchantId;
        IdempotencyKey = idempotencyKey;
        Operation = operation;
        Amount = amount.Amount;
        Currency = amount.Currency.Code;
        Status = PaymentStatus.Pending;
        TotalRefunded = 0m;
        CreatedAt = createdAt;
    }

    public void BeginProcessing(DateTimeOffset heartbeatAt)
    {
        PaymentStateMachine.AssertTransition(Status, PaymentStatus.Processing);
        Status = PaymentStatus.Processing;
        HeartbeatAt = heartbeatAt;
    }

    public void MarkAuthorized(Authorization auth, DateTimeOffset at)
    {
        if (auth.Outcome != AcquirerOutcome.Success)
        {
            throw new InvalidOperationException("Cannot MarkAuthorized with non-success outcome.");
        }

        PaymentStateMachine.AssertTransition(Status, PaymentStatus.Authorized);
        AuthCode = auth.AuthCode;
        AcquirerReference = auth.AcquirerReference;
        AuthorizedAt = at;
        HeartbeatAt = null;
        Status = PaymentStatus.Authorized;
    }

    public void MarkFailed(string reason, DateTimeOffset at)
    {
        PaymentStateMachine.AssertTransition(Status, PaymentStatus.Failed);
        FailedAt = at;
        FailureReason = reason;
        HeartbeatAt = null;
        Status = PaymentStatus.Failed;
    }

    public void MarkUnknown(DateTimeOffset heartbeatAt)
    {
        PaymentStateMachine.AssertTransition(Status, PaymentStatus.Unknown);
        HeartbeatAt = heartbeatAt;
        Status = PaymentStatus.Unknown;
    }

    public void Settle(DateTimeOffset at)
    {
        PaymentStateMachine.AssertTransition(Status, PaymentStatus.Settled);
        SettledAt = at;
        Status = PaymentStatus.Settled;
    }

    public void ApplyRefund(Money refundAmount, DateTimeOffset at)
    {
        if (refundAmount.IsZero)
        {
            throw new ArgumentException("Refund amount must be positive.", nameof(refundAmount));
        }

        var currency = ValueObjects.Currency.Parse(Currency);
        var settled = new Money(Amount, currency);
        var already = new Money(TotalRefunded, currency);

        RefundRules.AssertCanRefund(Id, Status, settled, already, refundAmount);

        TotalRefunded += refundAmount.Amount;
        if (TotalRefunded == Amount)
        {
            PaymentStateMachine.AssertTransition(Status, PaymentStatus.Refunded);
            Status = PaymentStatus.Refunded;
        }
        else
        {
            PaymentStateMachine.AssertTransition(Status, PaymentStatus.PartiallyRefunded);
            Status = PaymentStatus.PartiallyRefunded;
        }
    }

    public void RefreshHeartbeat(DateTimeOffset heartbeatAt)
    {
        if (Status != PaymentStatus.Processing)
        {
            throw new InvalidOperationException("Cannot refresh heartbeat when payment is not Processing.");
        }

        HeartbeatAt = heartbeatAt;
    }
}
