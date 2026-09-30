using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.Entities;

/// <summary>
/// Authoritative idempotency record in SQL Server. Enforced UNIQUE on (MerchantId, Operation, Key).
/// Redis is the fast coordination cache; this row is the correctness boundary. If Redis is down,
/// this row's unique constraint still prevents double financial execution.
/// </summary>
public sealed class IdempotencyRecord
{
    public Guid Id { get; private set; }

    public Guid MerchantId { get; private set; }

    public OperationType Operation { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public IdempotencyState State { get; private set; }

    public int? StatusCode { get; private set; }

    public string? ResponsePayload { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    private IdempotencyRecord() { }

    public IdempotencyRecord(
        Guid id,
        Guid merchantId,
        OperationType operation,
        string key,
        string requestHash,
        DateTimeOffset createdAt,
        TimeSpan expiresAfter)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key must not be empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(requestHash))
        {
            throw new ArgumentException("RequestHash must not be empty.", nameof(requestHash));
        }

        Id = id;
        MerchantId = merchantId;
        Operation = operation;
        Key = key;
        RequestHash = requestHash;
        State = IdempotencyState.Processing;
        CreatedAt = createdAt;
        ExpiresAt = createdAt.Add(expiresAfter);
    }

    public void Complete(int statusCode, string responsePayload, DateTimeOffset at)
    {
        StatusCode = statusCode;
        ResponsePayload = responsePayload;
        CompletedAt = at;
        State = IdempotencyState.Completed;
    }

    public void Fail(int statusCode, string responsePayload, DateTimeOffset at)
    {
        StatusCode = statusCode;
        ResponsePayload = responsePayload;
        CompletedAt = at;
        State = IdempotencyState.Failed;
    }
}
