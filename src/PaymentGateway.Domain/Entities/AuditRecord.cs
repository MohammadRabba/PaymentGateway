namespace PaymentGateway.Domain.Entities;

/// <summary>
/// Append-only audit of important financial and operational mutations. Never updated or deleted;
/// corrections or retractions are new AuditRecords that reference the original. The Metadata JSON
/// must never contain secrets, API keys, webhook secrets, or sensitive payment data.
/// </summary>
public sealed class AuditRecord
{
    public Guid Id { get; private set; }

    public string Actor { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string AggregateType { get; private set; } = string.Empty;

    public Guid AggregateId { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public string Metadata { get; private set; } = "{}";

    private AuditRecord() { }

    public AuditRecord(
        Guid id,
        string actor,
        string action,
        string aggregateType,
        Guid aggregateId,
        string correlationId,
        DateTimeOffset occurredAt,
        string metadata)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("Actor must not be empty.", nameof(actor));
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Action must not be empty.", nameof(action));
        }

        if (string.IsNullOrWhiteSpace(aggregateType))
        {
            throw new ArgumentException("AggregateType must not be empty.", nameof(aggregateType));
        }

        Id = id;
        Actor = actor;
        Action = action;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
        Metadata = string.IsNullOrWhiteSpace(metadata) ? "{}" : metadata;
    }
}
