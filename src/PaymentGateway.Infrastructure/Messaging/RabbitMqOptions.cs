namespace PaymentGateway.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ configuration. RabbitMQ is an event transport — never the financial source of truth.
/// All events originate in the SQL outbox table and are published to RabbitMQ by the OutboxPublisherWorker.
/// Delivery is at-least-once; consumers must deduplicate by EventId.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    /// <summary>AMQP connection URI. e.g. "amqp://guest:guest@rabbitmq:5672/%2f".</summary>
    public string ConnectionUri { get; init; } = "amqp://guest:guest@localhost:5672/%2f";

    /// <summary>Topic exchange name for payment events. Durable.</summary>
    public string ExchangeName { get; init; } = "payments.events";

    /// <summary>Queue for webhook delivery consumer. Durable.</summary>
    public string WebhookQueueName { get; init; } = "webhooks.events";

    /// <summary>Dead-letter exchange. Failed messages are routed here after max retries.</summary>
    public string DeadLetterExchangeName { get; init; } = "payments.events.dlx";

    /// <summary>Dead-letter queue bound to the DLX.</summary>
    public string DeadLetterQueueName { get; init; } = "webhooks.dlq";

    /// <summary>Publisher confirm timeout. Used by the OutboxPublisherWorker.</summary>
    public TimeSpan PublisherConfirmTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Consumer prefetch count. Bounded concurrency per consumer.</summary>
    public ushort PrefetchCount { get; init; } = 5;

    /// <summary>Initial reconnect delay. Doubles on each failure, capped at MaxReconnectDelay.</summary>
    public TimeSpan InitialReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum reconnect delay.</summary>
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);
}
