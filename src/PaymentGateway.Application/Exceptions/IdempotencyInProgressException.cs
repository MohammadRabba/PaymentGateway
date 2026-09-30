using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Application.Exceptions;

/// <summary>
/// Thrown when an idempotency key is already being processed by a concurrent request.
/// Maps to HTTP 409 Conflict with a Retry-After header derived from IdempotencyOptions.
/// </summary>
public sealed class IdempotencyInProgressException : DomainException
{
    public TimeSpan RetryAfter { get; }

    public string IdempotencyKey { get; }

    public IdempotencyInProgressException(string idempotencyKey, TimeSpan retryAfter)
        : base($"A request with idempotency key '{idempotencyKey}' is already in progress.")
    {
        IdempotencyKey = idempotencyKey;
        RetryAfter = retryAfter;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/idempotency-in-progress";
    public override int HttpStatusCode => 409;
}
