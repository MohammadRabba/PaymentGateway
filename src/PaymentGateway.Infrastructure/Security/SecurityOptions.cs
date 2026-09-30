namespace PaymentGateway.Infrastructure.Security;

/// <summary>
/// Security configuration. All secrets are externalised via environment variables or user-secrets.
/// Never commit real credentials. The admin key hash is validated by AdminAuthenticator in API.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// Hex-encoded SHA-256 hash of the admin API key. The admin key authorises /api/v1/admin/* calls.
    /// Generate with: echo -n 'admin-key-value' | sha256sum
    /// </summary>
    public string AdminApiKeyHash { get; init; } = string.Empty;

    /// <summary>
    /// If true, admin endpoints require a valid X-Admin-Key header. Set false only in local dev
    /// for convenience; production must always set this to true.
    /// </summary>
    public bool RequireAdminKey { get; init; } = true;

    /// <summary>
    /// Minimum length of a client-supplied API key. Shorter keys are rejected at the auth boundary.
    /// </summary>
    public int MinApiKeyLength { get; init; } = 32;

    /// <summary>
    /// How long to retain audit records (kept for compliance). Set via retention policy.
    /// </summary>
    public TimeSpan AuditRetention { get; init; } = TimeSpan.FromDays(365 * 7);

    /// <summary>
    /// Webhook replay tolerance in seconds. Webhook timestamps older than now - tolerance are rejected.
    /// </summary>
    public long WebhookReplayToleranceSeconds { get; init; } = 300;
}
