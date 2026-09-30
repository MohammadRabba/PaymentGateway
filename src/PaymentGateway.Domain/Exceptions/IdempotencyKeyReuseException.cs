namespace PaymentGateway.Domain.Exceptions;

/// <summary>
/// Thrown when a request reuses an idempotency key with a different payload.
/// Maps to HTTP 422 Unprocessable Entity with stable type idempotency-key-reuse.
/// </summary>
public sealed class IdempotencyKeyReuseException : DomainException
{
    public string IdempotencyKey { get; }

    public IdempotencyKeyReuseException(string idempotencyKey)
        : base($"The supplied idempotency key '{idempotencyKey}' was previously used with a different request.")
    {
        IdempotencyKey = idempotencyKey;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/idempotency-key-reuse";
    public override int HttpStatusCode => 422;
}
