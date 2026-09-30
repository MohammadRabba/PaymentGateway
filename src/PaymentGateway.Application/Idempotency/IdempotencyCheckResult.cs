using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Idempotency;

/// <summary>
/// Discriminated-union result of an idempotency check. Each variant maps to a specific HTTP response:
/// Proceed → execute the request; InProgress → 409 Conflict with Retry-After;
/// Replay → return the cached response; Reuse → 422 idempotency-key-reuse.
/// </summary>
public abstract record IdempotencyCheckResult
{
    private IdempotencyCheckResult() { }

    /// <summary>
    /// First-time request (or fresh request after TTL expiry). The IdempotencyRecord is in
    /// Processing state and tracked by the DbContext. The handler MUST call record.Complete or
    /// record.Fail on it before the request completes.
    /// </summary>
    public sealed record Proceed(
        Guid IdempotencyRecordId,
        string ScopeKey,
        IdempotencyRecord Record) : IdempotencyCheckResult;

    /// <summary>
    /// A concurrent request with the same key is still in Processing state. Return 409 Conflict
    /// with Retry-After header. The same request may safely be retried after the retry window.
    /// </summary>
    public sealed record InProgress(TimeSpan RetryAfter) : IdempotencyCheckResult;

    /// <summary>
    /// A previous request with the same key and the same payload already completed. Replay the
    /// exact stored response (status code + payload) to the client. This is the idempotent retry path.
    /// </summary>
    public sealed record Replay(int StatusCode, string ResponsePayload) : IdempotencyCheckResult;

    /// <summary>
    /// The same idempotency key was previously used with a DIFFERENT request payload. Reject with
    /// 422 Unprocessable Entity (idempotency-key-reuse). The client must use a new key.
    /// </summary>
    public sealed record Reuse : IdempotencyCheckResult
    {
        public static Reuse Instance { get; } = new();
        private Reuse() { }
    }
}
