namespace PaymentGateway.Domain.Entities;

/// <summary>
/// Append-only audit of outbox messages that exhausted retries. Mirrors OutboxMessage at the
/// moment of poisoning. Never deleted; supports operational inspection via admin endpoint.
/// </summary>
public sealed class OutboxDeadLetter
{
    public Guid Id { get; private set; }

    public Guid OutboxMessageId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public Guid AggregateId { get; private set; }

    public string Payload { get; private set; } = string.Empty;

    public string LastError { get; private set; } = string.Empty;

    public int Attempts { get; private set; }

    public DateTimeOffset OriginalOccurredAt { get; private set; }

    public DateTimeOffset PoisonedAt { get; private set; }

    private OutboxDeadLetter() { }

    public OutboxDeadLetter(
        Guid id,
        Guid outboxMessageId,
        string eventType,
        Guid aggregateId,
        string payload,
        string lastError,
        int attempts,
        DateTimeOffset originalOccurredAt,
        DateTimeOffset poisonedAt)
    {
        Id = id;
        OutboxMessageId = outboxMessageId;
        EventType = eventType;
        AggregateId = aggregateId;
        Payload = payload;
        LastError = lastError;
        Attempts = attempts;
        OriginalOccurredAt = originalOccurredAt;
        PoisonedAt = poisonedAt;
    }
}
