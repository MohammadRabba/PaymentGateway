namespace PaymentGateway.Infrastructure.Webhooks;

/// <summary>
/// Result of a single webhook delivery attempt. Consumed by the WebhookEventConsumer to decide
/// whether to ACK, NACK+requeue, or dead-letter the message.
/// </summary>
public sealed class WebhookDeliveryResult
{
    public required bool Success { get; init; }

    /// <summary>True if the failure is permanent (4xx) and should not be retried. False on retryable failures.</summary>
    public required bool PermanentFailure { get; init; }

    public int? StatusCode { get; init; }

    public string? Error { get; init; }

    /// <summary>SHA-256 hex hash of the payload bytes. Used for verification and deduplication tracking.</summary>
    public required string PayloadHash { get; init; }

    public required DateTimeOffset DeliveredAt { get; init; }
}
