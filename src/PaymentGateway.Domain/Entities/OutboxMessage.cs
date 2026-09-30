using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.Entities;

/// <summary>
/// Aggregate root: OutboxMessage. Persisted in the same SQL transaction as the financial mutation,
/// so the system never commits financial state without committing the event to be published.
/// Publisher claims atomically, publishes to RabbitMQ, then marks ProcessedAt.
/// At-least-once semantics: consumers must dedup by EventId.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public Guid AggregateId { get; private set; }

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? ClaimedBy { get; private set; }

    public DateTimeOffset? ClaimedAt { get; private set; }

    public string? LastError { get; private set; }

    public OutboxStatus Status { get; private set; }

    private OutboxMessage() { }

    public OutboxMessage(
        Guid id,
        string eventType,
        Guid aggregateId,
        string payload,
        DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("EventType must not be empty.", nameof(eventType));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("Payload must not be empty.", nameof(payload));
        }

        Id = id;
        EventType = eventType;
        AggregateId = aggregateId;
        Payload = payload;
        OccurredAt = occurredAt;
        NextAttemptAt = occurredAt;
        Attempts = 0;
        Status = OutboxStatus.Pending;
    }

    public void Claim(string workerId, DateTimeOffset claimedAt, TimeSpan retryBackoff)
    {
        Status = OutboxStatus.Claimed;
        ClaimedBy = workerId;
        ClaimedAt = claimedAt;
        NextAttemptAt = claimedAt.Add(retryBackoff);
    }

    public void MarkPublished(DateTimeOffset at)
    {
        ProcessedAt = at;
        Status = OutboxStatus.Published;
    }

    public void RecordFailure(string error, DateTimeOffset at, TimeSpan nextBackoff, int maxAttempts)
    {
        Attempts++;
        LastError = error;
        if (Attempts >= maxAttempts)
        {
            Status = OutboxStatus.Poisoned;
        }
        else
        {
            Status = OutboxStatus.Pending;
            NextAttemptAt = at.Add(nextBackoff);
        }
    }
}
