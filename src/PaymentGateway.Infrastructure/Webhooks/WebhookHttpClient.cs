using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Infrastructure.Webhooks;

/// <summary>
/// Thin wrapper around HttpClient for webhook delivery. Adds the HMAC-SHA256 signature headers
/// to every request. The HttpClient is injected via IHttpClientFactory (configured in API DI)
/// so connections are pooled and socket exhaustion is avoided.
/// </summary>
public sealed class WebhookHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly WebhookOptions _options;
    private readonly ILogger<WebhookHttpClient> _logger;

    public WebhookHttpClient(HttpClient httpClient, IOptions<WebhookOptions> options, ILogger<WebhookHttpClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = _options.HttpTimeout;
    }

    /// <summary>
    /// POST the raw payload bytes to the webhook URL with signing headers. Returns the HTTP response
    /// status code, or throws on network/timeout failure.
    /// </summary>
    public async Task<HttpResponseMessage> PostAsync(
        string webhookUrl,
        string webhookSecret,
        Guid webhookId,
        long timestamp,
        string eventType,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        // Compute the signature: HMAC-SHA256 over "{webhookId}.{timestamp}.{payload}".
        var signingString = $"{webhookId}.{timestamp}.";
        var signingBytes = Encoding.UTF8.GetBytes(signingString);
        var secretBytes = Encoding.UTF8.GetBytes(webhookSecret);
        var messageBytes = new byte[signingBytes.Length + payload.Length];
        signingBytes.CopyTo(messageBytes, 0);
        payload.CopyTo(messageBytes, signingBytes.Length);

        using var hmac = new HMACSHA256(secretBytes);
        var hashBytes = hmac.ComputeHash(messageBytes);
        var signature = Convert.ToHexString(hashBytes).ToLowerInvariant();

        using var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl);
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        request.Headers.Add("X-Webhook-Id", webhookId.ToString("N"));
        request.Headers.Add("X-Webhook-Timestamp", timestamp.ToString());
        request.Headers.Add("X-Webhook-Signature", signature);
        request.Headers.Add("X-Webhook-Event", eventType);

        return await _httpClient.SendAsync(request, cancellationToken);
    }
}
