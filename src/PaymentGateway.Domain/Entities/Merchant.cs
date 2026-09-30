using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.Entities;

/// <summary>
/// Aggregate root: Merchant. The webhook secret and API key hash are NEVER exposed through the API.
/// API keys are stored as SHA-256 hashes only — the raw key is shown to the merchant once at registration.
/// Mutations go through explicit domain methods so invariants cannot be bypassed.
/// </summary>
public sealed class Merchant
{
    public Guid Id { get; private set; }

    public string ExternalReference { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string WebhookUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Hex-encoded HMAC-SHA256 webhook secret. Never logged, never returned by API.
    /// </summary>
    public string WebhookSecret { get; private set; } = string.Empty;

    /// <summary>
    /// Hex-encoded SHA-256 hash of the merchant API key. Raw key never persisted.
    /// </summary>
    public string ApiKeyHash { get; private set; } = string.Empty;

    public MerchantStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    private Merchant() { }

    public Merchant(
        Guid id,
        string externalReference,
        string name,
        string webhookUrl,
        string webhookSecret,
        string apiKeyHash,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(externalReference))
        {
            throw new ArgumentException("ExternalReference must not be empty.", nameof(externalReference));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name must not be empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            throw new ArgumentException("WebhookUrl must not be empty.", nameof(webhookUrl));
        }

        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            throw new ArgumentException("WebhookSecret must not be empty.", nameof(webhookSecret));
        }

        if (string.IsNullOrWhiteSpace(apiKeyHash))
        {
            throw new ArgumentException("ApiKeyHash must not be empty.", nameof(apiKeyHash));
        }

        Id = id;
        ExternalReference = externalReference;
        Name = name;
        WebhookUrl = webhookUrl;
        WebhookSecret = webhookSecret;
        ApiKeyHash = apiKeyHash;
        Status = MerchantStatus.Active;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public void UpdateWebhookUrl(string webhookUrl, DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            throw new ArgumentException("WebhookUrl must not be empty.", nameof(webhookUrl));
        }

        WebhookUrl = webhookUrl;
        UpdatedAt = updatedAt;
    }

    public void Suspend(DateTimeOffset updatedAt)
    {
        if (Status == MerchantStatus.Closed)
        {
            throw new InvalidOperationException("Cannot suspend a closed merchant.");
        }

        Status = MerchantStatus.Suspended;
        UpdatedAt = updatedAt;
    }

    public void Reactivate(DateTimeOffset updatedAt)
    {
        if (Status == MerchantStatus.Closed)
        {
            throw new InvalidOperationException("Cannot reactivate a closed merchant.");
        }

        Status = MerchantStatus.Active;
        UpdatedAt = updatedAt;
    }

    public bool CanAcceptPayments() => Status == MerchantStatus.Active;
}
