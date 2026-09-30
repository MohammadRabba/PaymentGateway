using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.Entities;

/// <summary>
/// Aggregate root: WebhookDelivery. Persisted by the consumer to track delivery attempts and
/// provide idempotency by EventId (consumer-side deduplication). Duplicate event delivery is
/// therefore safe — a Delivered row is found and the message is ACKed without re-sending.
/// </summary>
public sealed class WebhookDelivery
{
    public Guid Id { get; private set; }

    public Guid EventId { get; private set; }

    public Guid WebhookId { get; private set; }

    public Guid MerchantId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public int AttemptCount { get; private set; }

    public WebhookStatus Status { get; private set; }

    public int? ResponseStatusCode { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public string PayloadHash { get; private set; } = string.Empty;

    private WebhookDelivery() { }

    public WebhookDelivery(
        Guid id,
        Guid eventId,
        Guid webhookId,
        Guid merchantId,
        string eventType,
        string payloadHash,
        DateTimeOffset createdAt)
    {
        Id = id;
        EventId = eventId;
        WebhookId = webhookId;
        MerchantId = merchantId;
        EventType = eventType;
        PayloadHash = payloadHash;
        Status = WebhookStatus.Pending;
        AttemptCount = 0;
    }

    public void RecordSuccess(int statusCode, DateTimeOffset at)
    {
        AttemptCount++;
        ResponseStatusCode = statusCode;
        DeliveredAt = at;
        NextAttemptAt = null;
        LastError = null;
        Status = WebhookStatus.Delivered;
    }

    public void RecordRetry(int statusCode, string error, DateTimeOffset at, TimeSpan backoff)
    {
        AttemptCount++;
        ResponseStatusCode = statusCode;
        LastError = error;
        NextAttemptAt = at.Add(backoff);
        Status = WebhookStatus.Retry;
    }

    public void MarkDead(int statusCode, string error, DateTimeOffset at)
    {
        AttemptCount++;
        ResponseStatusCode = statusCode;
        LastError = error;
        DeliveredAt = null;
        NextAttemptAt = null;
        Status = WebhookStatus.Dead;
    }
}
