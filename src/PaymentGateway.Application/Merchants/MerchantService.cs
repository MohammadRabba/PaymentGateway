using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PaymentGateway.Application.Auditing;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Exceptions;
using PaymentGateway.Application.Idempotency;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.Application.Merchants;

/// <summary>
/// Merchant onboarding and query service. Creates a merchant with three default accounts
/// (MerchantPayable, AcquirerReceivable, FeeRevenue) per supported currency, generates a per-merchant
/// webhook secret, and returns the raw API key ONCE (never retrievable again — only the hash is stored).
/// </summary>
public sealed class MerchantService
{
    private readonly IPaymentGatewayDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly ICorrelationContext _correlation;
    private readonly AuditService _auditService;

    // Default currencies for new merchants. Extend as needed.
    private static readonly string[] DefaultCurrencies = { "USD", "EUR", "GBP" };

    public MerchantService(
        IPaymentGatewayDbContext dbContext,
        IClock clock,
        IIdGenerator idGenerator,
        ICorrelationContext correlation,
        AuditService auditService)
    {
        _dbContext = dbContext;
        _clock = clock;
        _idGenerator = idGenerator;
        _correlation = correlation;
        _auditService = auditService;
    }

    /// <summary>
    /// Register a new merchant. The ExternalReference must be unique. Returns the raw API key
    /// (shown only here; never retrievable again) and the merchant response DTO.
    /// </summary>
    public async Task<MerchantResponse> RegisterAsync(RegisterMerchantRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(request.ExternalReference);
        ArgumentException.ThrowIfNullOrEmpty(request.Name);
        ArgumentException.ThrowIfNullOrEmpty(request.WebhookUrl);

        // Check for existing ExternalReference (the idempotency boundary for merchant registration).
        var existing = await _dbContext.Merchants
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ExternalReference == request.ExternalReference, cancellationToken);
        if (existing is not null)
        {
            // Idempotent: return the existing merchant (without the API key, which can't be retrieved).
            return MapToResponse(existing, apiKey: null);
        }

        var now = _clock.UtcNow;
        var merchantId = _idGenerator.NewId();

        // Generate the raw API key and webhook secret (cryptographically secure random).
        var rawApiKey = GenerateApiKey();
        var webhookSecret = GenerateSecret();
        var apiKeyHash = HashApiKey(rawApiKey);

        var merchant = new Merchant(
            merchantId,
            request.ExternalReference,
            request.Name,
            request.WebhookUrl,
            webhookSecret,
            apiKeyHash,
            now);

        _dbContext.Merchants.Add(merchant);

        // Create default accounts for each supported currency.
        foreach (var currencyCode in DefaultCurrencies)
        {
            var currency = Currency.Parse(currencyCode);
            foreach (var accountType in Enum.GetValues<AccountType>())
            {
                var account = new Account(
                    _idGenerator.NewId(),
                    merchantId,
                    accountType,
                    currency,
                    now);
                _dbContext.Accounts.Add(account);
            }
        }

        _auditService.Record(
            "admin",
            "MerchantCreated",
            "Merchant",
            merchantId,
            new { request.ExternalReference, request.Name });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToResponse(merchant, rawApiKey);
    }

    public async Task<MerchantResponse?> GetAsync(Guid merchantId, CancellationToken cancellationToken)
    {
        var merchant = await _dbContext.Merchants
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == merchantId, cancellationToken);
        if (merchant is null)
        {
            return null;
        }
        return MapToResponse(merchant, apiKey: null);
    }

    /// <summary>
    /// Resolve a merchant by its API key hash. Used by the authentication middleware to populate
    /// the IMerchantContext. Returns null if the key doesn't match any merchant.
    /// </summary>
    public async Task<Merchant?> ResolveByApiKeyAsync(string rawApiKey, CancellationToken cancellationToken)
    {
        var hash = HashApiKey(rawApiKey);
        return await _dbContext.Merchants
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ApiKeyHash == hash, cancellationToken);
    }

    private static MerchantResponse MapToResponse(Merchant merchant, string? apiKey)
    {
        return new MerchantResponse
        {
            Id = merchant.Id,
            ExternalReference = merchant.ExternalReference,
            Name = merchant.Name,
            WebhookUrl = merchant.WebhookUrl,
            Status = merchant.Status.ToString(),
            CreatedAt = merchant.CreatedAt,
            ApiKey = apiKey,
        };
    }

    /// <summary>
    /// Generate a cryptographically secure API key. Format: "pgk_" prefix + 32 random bytes hex-encoded.
    /// Total length: 67 characters. Strong enough for API auth.
    /// </summary>
    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "pgk_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Generate a cryptographically secure webhook secret. 32 bytes hex-encoded = 64 chars.
    /// Used as the HMAC-SHA256 key for signing webhook payloads.
    /// </summary>
    private static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// SHA-256 hash of the raw API key. Stored in Merchant.ApiKeyHash. The raw key is never persisted.
    /// </summary>
    private static string HashApiKey(string rawApiKey)
    {
        var bytes = Encoding.UTF8.GetBytes(rawApiKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
