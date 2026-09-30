using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Webhooks;

namespace PaymentGateway.Infrastructure.Webhooks;

/// <summary>
/// Orchestrates a single webhook delivery: sign the payload, POST via WebhookHttpClient, classify
/// the response, and return a WebhookDeliveryResult. Does NOT persist the WebhookDelivery record
/// — that is the consumer's responsibility. The service is stateless and can be called concurrently.
///
/// Response classification:
///   - 2xx: success, ACK
///   - 408, 429, 5xx: retryable, NACK with backoff
///   - 400, 401, 403, 404, 410, 422: permanent failure, dead-letter
///   - other 4xx: retryable, NACK with backoff
///   - network/timeout: retryable, NACK with backoff
/// </summary>
public sealed class WebhookDeliveryService
{
    private readonly WebhookHttpClient _httpClient;
    private readonly WebhookOptions _options;
    private readonly ILogger<WebhookDeliveryService> _logger;

    public WebhookDeliveryService(
        WebhookHttpClient httpClient,
        IOptions<WebhookOptions> options,
        ILogger<WebhookDeliveryService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Deliver a webhook. The payloadBytes are the EXACT bytes used as the signing input AND
    /// the HTTP body — no transformation between signing and sending.
    /// </summary>
    public async Task<WebhookDeliveryResult> DeliverAsync(
        Guid merchantId,
        string webhookUrl,
        string webhookSecret,
        Guid eventId,
        string eventType,
        byte[] payloadBytes,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var webhookId = Guid.NewGuid();
        var timestamp = now.ToUnixTimeSeconds();
        var payloadHash = ComputePayloadHash(payloadBytes);

        try
        {
            var response = await _httpClient.PostAsync(
                webhookUrl,
                webhookSecret,
                webhookId,
                timestamp,
                eventType,
                payloadBytes,
                cancellationToken);

            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Webhook delivered: merchant {MerchantId} event {EventId} status {StatusCode}",
                    merchantId, eventId, statusCode);
                return new WebhookDeliveryResult
                {
                    Success = true,
                    PermanentFailure = false,
                    StatusCode = statusCode,
                    Error = null,
                    PayloadHash = payloadHash,
                    DeliveredAt = now,
                };
            }

            // Classify the failure.
            var isPermanent = Array.IndexOf(_options.PermanentFailureCodes, statusCode) >= 0;
            var isRetryable = Array.IndexOf(_options.RetryableCodes, statusCode) >= 0;

            // Other 4xx that aren't in the permanent list are treated as retryable.
            if (statusCode >= 400 && statusCode < 500 && !isPermanent && !isRetryable)
            {
                isRetryable = true;
            }

            var error = $"HTTP {statusCode}: {response.ReasonPhrase}";

            _logger.LogWarning(
                "Webhook delivery failed: merchant {MerchantId} event {EventId} status {StatusCode} permanent={Permanent}",
                merchantId, eventId, statusCode, isPermanent);

            return new WebhookDeliveryResult
            {
                Success = false,
                PermanentFailure = isPermanent,
                StatusCode = statusCode,
                Error = error,
                PayloadHash = payloadHash,
                DeliveredAt = now,
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "Webhook HTTP error: merchant {MerchantId} event {EventId} url {Url}",
                merchantId, eventId, webhookUrl);
            return new WebhookDeliveryResult
            {
                Success = false,
                PermanentFailure = false,
                StatusCode = null,
                Error = ex.Message,
                PayloadHash = payloadHash,
                DeliveredAt = now,
            };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Webhook timeout: merchant {MerchantId} event {EventId} url {Url} timeout {Timeout}",
                merchantId, eventId, webhookUrl, _options.HttpTimeout);
            return new WebhookDeliveryResult
            {
                Success = false,
                PermanentFailure = false,
                StatusCode = null,
                Error = $"Timeout after {_options.HttpTimeout.TotalSeconds}s",
                PayloadHash = payloadHash,
                DeliveredAt = now,
            };
        }
    }

    private static string ComputePayloadHash(byte[] payload)
    {
        var hash = SHA256.HashData(payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
