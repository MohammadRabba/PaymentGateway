using PaymentGateway.Application.Common;

namespace PaymentGateway.Application.Payments;

/// <summary>
/// Helper for generating deterministic acquirer idempotency keys. The acquirer uses these keys
/// to deduplicate authorization requests. The recovery worker uses the SAME key to query the
/// acquirer about the outcome of a previous request — without re-issuing the authorization.
///
/// This is critical for the Unknown-state recovery flow: if we crashed before processing the
/// acquirer's response, we can safely query "did you authorize PAY-{paymentId}?" and the acquirer
/// will return the original result (or "I don't know this key" if we never sent the request).
/// </summary>
public static class AcquirerReferences
{
    /// <summary>
    /// Deterministic idempotency key for an authorization request, derived from the payment ID.
    /// The acquirer MUST treat this as idempotent: a second request with the same key returns the
    /// original result without re-authorizing.
    /// </summary>
    public static string ForPayment(Guid paymentId) => $"PAY-{paymentId:N}";

    /// <summary>
    /// Deterministic idempotency key for a refund request, derived from the refund ID.
    /// </summary>
    public static string ForRefund(Guid refundId) => $"REF-{refundId:N}";
}
