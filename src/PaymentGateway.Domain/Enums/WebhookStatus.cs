namespace PaymentGateway.Domain.Enums;

/// <summary>
/// Webhook delivery lifecycle. Delivery is at-least-once; merchants must dedup by X-Webhook-Id.
/// </summary>
public enum WebhookStatus
{
    Pending = 1,
    Delivered = 2,
    Retry = 3,
    Dead = 4,
}
