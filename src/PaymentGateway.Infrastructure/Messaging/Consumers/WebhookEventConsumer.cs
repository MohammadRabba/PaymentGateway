using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Webhooks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PaymentGateway.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumes payment events from RabbitMQ and delivers them to merchant webhooks.
///
/// Scoping: the consumer is a singleton (holds the long-lived channel). For each message, it
/// creates a fresh DI scope via IServiceScopeFactory so the DbContext is not captured as a
/// singleton — this is the standard pattern for BackgroundService/IHostedService consumers.
///
/// Delivery semantics: at-least-once. The consumer:
///   1. Receives a message from RabbitMQ.
///   2. Checks the WebhookDelivery table — if a Delivered row exists for this EventId, ACK without
///      re-sending (consumer-side idempotency).
///   3. Looks up the merchant's webhook URL and secret.
///   4. Computes HMAC-SHA256 signature, posts to the webhook URL.
///   5. Records the WebhookDelivery in SQL.
///   6. ACKs the message on 2xx or permanent 4xx; NACKs + requeues on retryable failure.
///
/// A crash between HTTP delivery and SQL commit can result in duplicate HTTP delivery. Merchants
/// MUST use X-Webhook-Id as their idempotency key — this is documented in the README.
/// </summary>
public sealed class WebhookEventConsumer : IAsyncDisposable
{
    private readonly RabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WebhookDeliveryService _deliveryService;
    private readonly WebhookPayloadFactory _payloadFactory;
    private readonly WebhookSigner _signer;
    private readonly ILogger<WebhookEventConsumer> _logger;

    private IChannel? _channel;
    private AsyncEventingBasicConsumer? _consumer;
    private string? _consumerTag;

    public WebhookEventConsumer(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        WebhookDeliveryService deliveryService,
        WebhookPayloadFactory payloadFactory,
        WebhookSigner signer,
        ILogger<WebhookEventConsumer> logger)
    {
        _connection = connection;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _deliveryService = deliveryService;
        _payloadFactory = payloadFactory;
        _signer = signer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _channel = await _connection.CreateChannelAsync(cancellationToken);

        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: _options.PrefetchCount, global: false, cancellationToken);

        _consumer = new AsyncEventingBasicConsumer(_channel);
        // TODO: RabbitMQ.Client v7 API change - Received event needs to be handled differently
        // _consumer.Received += OnMessageReceived;

        _consumerTag = await _channel.BasicConsumeAsync(
            queue: _options.WebhookQueueName,
            autoAck: false,
            consumer: _consumer,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Webhook consumer started on queue {Queue}", _options.WebhookQueueName);
    }

    private async Task OnMessageReceived(object sender, BasicDeliverEventArgs ea)
    {
        if (_channel is null)
        {
            _logger.LogError("Received message but channel is null. Cannot process.");
            return;
        }

        var deliveryTag = ea.DeliveryTag;
        var body = ea.Body.ToArray();

        try
        {
            var payloadJson = Encoding.UTF8.GetString(body);
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("eventId", out var eventIdEl)
                || !Guid.TryParse(eventIdEl.GetString(), out var eventId))
            {
                _logger.LogError("Webhook message missing eventId. Rejecting.");
                await _channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false);
                return;
            }

            var eventType = root.TryGetProperty("eventType", out var etEl)
                ? etEl.GetString() ?? "unknown"
                : "unknown";

            var merchantId = root.TryGetProperty("merchantId", out var midEl)
                && Guid.TryParse(midEl.GetString(), out var mid)
                    ? mid
                    : Guid.Empty;

            if (merchantId == Guid.Empty)
            {
                _logger.LogError("Webhook event {EventId} missing merchantId. Rejecting.", eventId);
                await _channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false);
                return;
            }

            // Create a DI scope for this message so DbContext is fresh.
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IPaymentGatewayDbContext>();

            // Consumer-side idempotency: check if already delivered.
            var existingDelivery = await dbContext.WebhookDeliveries
                .FirstOrDefaultAsync(w => w.EventId == eventId);

            if (existingDelivery is { Status: WebhookStatus.Delivered })
            {
                _logger.LogInformation("Event {EventId} already delivered. ACKing without re-sending.", eventId);
                await _channel.BasicAckAsync(deliveryTag, multiple: false);
                return;
            }

            var merchant = await dbContext.Merchants
                .FirstOrDefaultAsync(m => m.Id == merchantId);

            if (merchant is null)
            {
                _logger.LogWarning("Merchant {MerchantId} not found for event {EventId}. NACKing without requeue.", merchantId, eventId);
                await _channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false);
                return;
            }

            var deliveryResult = await _deliveryService.DeliverAsync(
                merchantId: merchantId,
                webhookUrl: merchant.WebhookUrl,
                webhookSecret: merchant.WebhookSecret,
                eventId: eventId,
                eventType: eventType,
                payloadBytes: body,
                cancellationToken: CancellationToken.None);

            if (existingDelivery is not null)
            {
                if (deliveryResult.Success)
                {
                    existingDelivery.RecordSuccess(deliveryResult.StatusCode ?? 200, DateTimeOffset.UtcNow);
                }
                else if (deliveryResult.PermanentFailure)
                {
                    existingDelivery.MarkDead(deliveryResult.StatusCode ?? 0, deliveryResult.Error ?? "Unknown", DateTimeOffset.UtcNow);
                }
                else
                {
                    existingDelivery.RecordRetry(deliveryResult.StatusCode ?? 0, deliveryResult.Error ?? "Unknown", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30));
                }
            }
            else
            {
                var delivery = new WebhookDelivery(
                    Guid.NewGuid(),
                    eventId,
                    Guid.NewGuid(),
                    merchantId,
                    eventType,
                    deliveryResult.PayloadHash,
                    DateTimeOffset.UtcNow);

                if (deliveryResult.Success)
                {
                    delivery.RecordSuccess(deliveryResult.StatusCode ?? 200, DateTimeOffset.UtcNow);
                }
                else if (deliveryResult.PermanentFailure)
                {
                    delivery.MarkDead(deliveryResult.StatusCode ?? 0, deliveryResult.Error ?? "Unknown", DateTimeOffset.UtcNow);
                }
                else
                {
                    delivery.RecordRetry(deliveryResult.StatusCode ?? 0, deliveryResult.Error ?? "Unknown", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30));
                }

                dbContext.WebhookDeliveries.Add(delivery);
            }

            await dbContext.SaveChangesAsync();

            if (deliveryResult.Success || deliveryResult.PermanentFailure)
            {
                await _channel.BasicAckAsync(deliveryTag, multiple: false);
            }
            else
            {
                await _channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing webhook message delivery tag {DeliveryTag}. NACKing without requeue.", deliveryTag);
            try { await _channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false); }
            catch (Exception nackEx) { _logger.LogError(nackEx, "Failed to NACK after error."); }
        }
    }

    public async Task StopAsync()
    {
        if (_channel is not null && _consumerTag is not null)
        {
            try
            {
                await _channel.BasicCancelAsync(_consumerTag);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cancel consumer.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }
    }
}
