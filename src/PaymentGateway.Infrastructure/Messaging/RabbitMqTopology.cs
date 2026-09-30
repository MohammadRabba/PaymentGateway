using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace PaymentGateway.Infrastructure.Messaging;

/// <summary>
/// Declares the durable RabbitMQ topology on startup. Idempotent — safe to call repeatedly.
///
/// Topology:
///   - Topic exchange "payments.events" (durable)
///   - Queue "webhooks.events" (durable) bound with routing keys payment.settled, payment.failed, refund.completed
///   - DLX "payments.events.dlx" (durable)
///   - DLQ "webhooks.dlq" (durable) bound to the DLX
///
/// The queue "webhooks.events" is configured with x-dead-letter-exchange pointing to the DLX so
/// rejected messages are routed to the DLQ for inspection. Prefetch is bounded to limit
/// memory pressure per consumer.
/// </summary>
public sealed class RabbitMqTopology
{
    public static readonly string[] PaymentRoutingKeys =
    {
        "payment.authorized", "payment.settled", "payment.failed", "refund.completed",
    };

    private readonly RabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTopology> _logger;

    public RabbitMqTopology(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqTopology> logger)
    {
        _connection = connection;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Declare exchanges, queues, and bindings. Safe to call repeatedly.
    /// </summary>
    public async Task DeclareTopologyAsync(CancellationToken cancellationToken)
    {
        await using var channel = await _connection.CreateChannelAsync(cancellationToken);

        // Dead-letter exchange (declared first — referenced by the main queue).
        await channel.ExchangeDeclareAsync(
            exchange: _options.DeadLetterExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        // Dead-letter queue.
        await channel.QueueDeclareAsync(
            queue: _options.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: _options.DeadLetterQueueName,
            exchange: _options.DeadLetterExchangeName,
            routingKey: _options.WebhookQueueName,
            cancellationToken: cancellationToken);

        // Main topic exchange.
        await channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        // Main queue with x-dead-letter-exchange pointing to the DLX.
        var queueArgs = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName,
        };

        await channel.QueueDeclareAsync(
            queue: _options.WebhookQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs,
            cancellationToken: cancellationToken);

        // Bind each routing key.
        foreach (var routingKey in PaymentRoutingKeys)
        {
            await channel.QueueBindAsync(
                queue: _options.WebhookQueueName,
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                cancellationToken: cancellationToken);
        }

        _logger.LogInformation(
            "RabbitMQ topology declared: exchange {Exchange}, queue {Queue}, DLX {Dlx}, DLQ {Dlq}",
            _options.ExchangeName, _options.WebhookQueueName, _options.DeadLetterExchangeName, _options.DeadLetterQueueName);
    }
}
