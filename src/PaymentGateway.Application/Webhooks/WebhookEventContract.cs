namespace PaymentGateway.Application.Webhooks;

/// <summary>
/// Webhook event type constants. These are the routing keys used in RabbitMQ and the
/// "eventType" field in the JSON payload sent to merchants.
/// </summary>
public static class WebhookEventTypes
{
    public const string PaymentAuthorized = "payment.authorized";
    public const string PaymentSettled = "payment.settled";
    public const string PaymentFailed = "payment.failed";
    public const string RefundCompleted = "refund.completed";
}

/// <summary>
/// HTTP headers sent with every webhook delivery. The merchant uses X-Webhook-Id as their
/// idempotency key (because HTTP delivery is at-least-once; duplicates are possible).
/// </summary>
public static class WebhookHeaders
{
    public const string WebhookId = "X-Webhook-Id";
    public const string WebhookTimestamp = "X-Webhook-Timestamp";
    public const string WebhookSignature = "X-Webhook-Signature";
    public const string WebhookEventType = "X-Webhook-Event";
}
