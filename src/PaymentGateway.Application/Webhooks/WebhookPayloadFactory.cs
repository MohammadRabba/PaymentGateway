using System.Text.Json;
using PaymentGateway.Domain.Events;

namespace PaymentGateway.Application.Webhooks;

/// <summary>
/// Factory for webhook payloads. Serialises domain events to JSON with stable options so the
/// signature is deterministic. The raw bytes produced here are BOTH the HTTP body AND the signing
/// input — the signer uses these exact bytes to compute the HMAC.
/// </summary>
public sealed class WebhookPayloadFactory
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serialise a domain event to UTF-8 JSON bytes. These bytes are the exact HTTP body sent to
    /// the merchant AND the exact bytes used as signing input.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Part of instance API contract")]
    public byte[] CreatePayloadBytes<T>(T @event) where T : class
    {
        ArgumentNullException.ThrowIfNull(@event);
        return JsonSerializer.SerializeToUtf8Bytes(@event, PayloadOptions);
    }

    /// <summary>
    /// Serialise a domain event to a JSON string. Useful for logging (without secrets) and for
    /// computing the payload hash stored in WebhookDelivery for verification.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Part of instance API contract")]
    public string CreatePayloadString<T>(T @event) where T : class
    {
        ArgumentNullException.ThrowIfNull(@event);
        return JsonSerializer.Serialize(@event, PayloadOptions);
    }
}
