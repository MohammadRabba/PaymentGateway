namespace PaymentGateway.Domain.Exceptions;

/// <summary>
/// Thrown when optimistic concurrency retry has been exhausted.
/// Maps to HTTP 409 Conflict.
/// </summary>
public sealed class ConcurrencyConflictException : DomainException
{
    public string AggregateType { get; }

    public Guid AggregateId { get; }

    public ConcurrencyConflictException(string aggregateType, Guid aggregateId, int attempts)
        : base($"Optimistic concurrency conflict on {aggregateType} {aggregateId} after {attempts} attempts.")
    {
        AggregateType = aggregateType;
        AggregateId = aggregateId;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/concurrency-conflict";
    public override int HttpStatusCode => 409;
}
