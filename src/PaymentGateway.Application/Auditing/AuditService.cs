using System.Text.Json;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Auditing;

/// <summary>
/// Append-only audit service. The audit record is ADDED to the DbContext (not saved); the caller's
/// transaction includes it in the atomic commit. This means the audit record and the financial
/// mutation commit atomically — never one without the other.
///
/// Metadata is JSON-serialised. NEVER include secrets, API keys, webhook secrets, card tokens,
/// or full payment payloads in metadata. Include only non-sensitive identifiers and operational data.
/// </summary>
public sealed class AuditService
{
    private readonly IPaymentGatewayDbContext _dbContext;
    private readonly IIdGenerator _idGenerator;
    private readonly IClock _clock;
    private readonly ICorrelationContext _correlation;

    private static readonly JsonSerializerOptions MetadataOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public AuditService(
        IPaymentGatewayDbContext dbContext,
        IIdGenerator idGenerator,
        IClock clock,
        ICorrelationContext correlation)
    {
        _dbContext = dbContext;
        _idGenerator = idGenerator;
        _clock = clock;
        _correlation = correlation;
    }

    /// <summary>
    /// Record an audit entry. The entry is added to the DbContext; the caller's SaveChanges persists it.
    /// The actor is the merchant ID (for merchant-initiated actions) or "system" (for workers).
    /// </summary>
    public void Record(string actor, string action, string aggregateType, Guid aggregateId, object? metadata = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(actor);
        ArgumentException.ThrowIfNullOrEmpty(action);
        ArgumentException.ThrowIfNullOrEmpty(aggregateType);

        var metadataJson = metadata is null
            ? "{}"
            : JsonSerializer.Serialize(metadata, MetadataOptions);

        var record = new AuditRecord(
            _idGenerator.NewId(),
            actor,
            action,
            aggregateType,
            aggregateId,
            _correlation.CurrentId,
            _clock.UtcNow,
            metadataJson);

        _dbContext.AuditRecords.Add(record);
    }
}
