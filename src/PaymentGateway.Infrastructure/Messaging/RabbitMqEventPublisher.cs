using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace PaymentGateway.Infrastructure.Messaging;

/// <summary>
/// Publishes outbox events to RabbitMQ. Used by the OutboxPublisherWorker. Messages are persistent
/// and routed via the durable topic exchange. The publisher uses the routing key derived from the
/// event type so consumers can subscribe selectively.
///
/// The publisher does NOT update the outbox row state — that is the worker's responsibility. The
/// worker calls this publisher, then on success/failure updates the OutboxMessage in SQL.
/// </summary>
public sealed class RabbitMqEventPublisher
{
    private readonly RabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventPublisher> _logger;

    public RabbitMqEventPublisher(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _connection = connection;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Publish a single outbox event payload to RabbitMQ. Returns true on success, false on failure.
    /// The caller (worker) handles the SQL state update based on this return value.
    /// </summary>
    public async Task<bool> PublishAsync(string eventType, Guid aggregateId, string payload, CancellationToken cancellationToken)
    {
        try
        {
            await using var channel = await _connection.CreateChannelAsync(cancellationToken);

            var body = System.Text.Encoding.UTF8.GetBytes(payload);

            // Construct the basic properties. In RabbitMQ.Client 7.x, use new BasicProperties() instead of channel.CreateBasicProperties().
            var basicProperties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                Type = eventType,
                MessageId = Guid.NewGuid().ToString("N"),
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    ["x-correlation-id"] = System.Text.Encoding.UTF8.GetBytes(aggregateId.ToString("N")),
                },
            };

            var routingKey = MapEventTypeToRoutingKey(eventType);

            await channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: basicProperties,
                body: body,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Published event {EventType} aggregate {AggregateId} to exchange {Exchange} with routing key {RoutingKey}",
                eventType, aggregateId, _options.ExchangeName, routingKey);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish event {EventType} aggregate {AggregateId} to RabbitMQ",
                eventType, aggregateId);
            return false;
        }
    }

    /// <summary>
    /// Map an event type string to its RabbitMQ routing key. The routing key uses dot-notation
    /// to match the topic exchange.
    /// </summary>
    private static string MapEventTypeToRoutingKey(string eventType) => eventType switch
    {
        "PaymentAuthorizedEvent" => "payment.authorized",
        "PaymentSettledEvent" => "payment.settled",
        "PaymentFailedEvent" => "payment.failed",
        "RefundCompletedEvent" => "refund.completed",
        _ => eventType.Replace("Event", string.Empty).ToLowerInvariant(),
    };
}

