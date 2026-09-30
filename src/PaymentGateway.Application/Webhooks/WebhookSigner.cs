using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Application.Webhooks;

/// <summary>
/// HMAC-SHA256 signer for webhook payloads. The signature is computed over the signing string
/// "{WebhookId}.{Timestamp}.{RawPayload}" and sent as a lowercase-hex X-Webhook-Signature header.
///
/// Merchants verify by recomputing the signature with their stored webhook secret and comparing.
/// Replay protection: reject if |now - timestamp| > Tolerance (default 5 minutes, configurable).
///
/// The secret is NEVER logged. Only the signature (a derived value) is safe to log.
/// </summary>
public sealed class WebhookSigner
{
    /// <summary>
    /// Compute the HMAC-SHA256 signature for a webhook payload.
    /// </summary>
    /// <param name="secret">The merchant's per-merchant webhook secret (hex-encoded or raw bytes).</param>
    /// <param name="webhookId">The unique ID sent as X-Webhook-Id (used for replay protection).</param>
    /// <param name="timestamp">Unix timestamp (seconds) sent as X-Webhook-Timestamp.</param>
    /// <param name="payload">The raw UTF-8 JSON payload bytes (exact bytes sent in the HTTP body).</param>
    /// <returns>Lowercase-hex HMAC-SHA256 signature string.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Part of instance API contract")]
    public string ComputeSignature(string secret, string webhookId, long timestamp, byte[] payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentException.ThrowIfNullOrEmpty(webhookId);
        ArgumentNullException.ThrowIfNull(payload);

        var signingString = $"{webhookId}.{timestamp}.";
        var signingBytes = Encoding.UTF8.GetBytes(signingString);
        var secretBytes = Encoding.UTF8.GetBytes(secret);

        // Concatenate signingString bytes + payload bytes.
        var messageBytes = new byte[signingBytes.Length + payload.Length];
        signingBytes.CopyTo(messageBytes, 0);
        payload.CopyTo(messageBytes, signingBytes.Length);

        using var hmac = new HMACSHA256(secretBytes);
        var hashBytes = hmac.ComputeHash(messageBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verify a received signature. Constant-time comparison to prevent timing attacks.
    /// </summary>
    public bool VerifySignature(string secret, string webhookId, long timestamp, byte[] payload, string receivedSignature)
    {
        var computed = ComputeSignature(secret, webhookId, timestamp, payload);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(receivedSignature.ToLowerInvariant()));
    }

    /// <summary>
    /// Check whether a timestamp is within the acceptable tolerance window. Prevents replay attacks
    /// where an attacker captures an old webhook and resends it later.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Part of instance API contract")]
    public bool IsTimestampValid(long timestamp, long nowUnixSeconds, long toleranceSeconds)
    {
        var diff = Math.Abs(nowUnixSeconds - timestamp);
        return diff <= toleranceSeconds;
    }
}
