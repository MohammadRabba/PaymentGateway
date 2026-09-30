using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Rules;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.Domain.Entities;

/// <summary>
/// Refund entity under Payment. Has its own IdempotencyKey + state machine so a refund is
/// first-class: full, partial, multiple partial, with cumulative validation.
/// </summary>
public sealed class Refund
{
    public Guid Id { get; private set; }

    public Guid PaymentId { get; private set; }

    public Guid MerchantId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public RefundStatus Status { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureReason { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    private Refund() { }

    public Refund(
        Guid id,
        Guid paymentId,
        Guid merchantId,
        string idempotencyKey,
        Money amount,
        string? reason,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey must not be empty.", nameof(idempotencyKey));
        }

        if (amount.IsZero)
        {
            throw new ArgumentException("Refund amount must be positive.", nameof(amount));
        }

        Id = id;
        PaymentId = paymentId;
        MerchantId = merchantId;
        IdempotencyKey = idempotencyKey;
        Amount = amount.Amount;
        Currency = amount.Currency.Code;
        Reason = reason;
        Status = RefundStatus.Pending;
        CreatedAt = createdAt;
    }

    public void Complete(DateTimeOffset at)
    {
        RefundStateMachine.AssertTransition(Status, RefundStatus.Completed);
        CompletedAt = at;
        Status = RefundStatus.Completed;
    }

    public void Fail(string reason, DateTimeOffset at)
    {
        RefundStateMachine.AssertTransition(Status, RefundStatus.Failed);
        FailureReason = reason;
        CompletedAt = at;
        Status = RefundStatus.Failed;
    }
}
