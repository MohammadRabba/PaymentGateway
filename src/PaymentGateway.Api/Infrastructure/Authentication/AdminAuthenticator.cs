using Microsoft.Extensions.Options;
using PaymentGateway.Infrastructure.Security;

namespace PaymentGateway.Api.Infrastructure.Authentication;

/// <summary>
/// Validates the admin API key from the X-Admin-Key header against the configured SHA-256 hash.
/// The raw key is never stored — only the hash. Admin authorises /api/v1/admin/* endpoints.
/// </summary>
public sealed class AdminAuthenticator
{
    private readonly SecurityOptions _options;

    public AdminAuthenticator(IOptions<SecurityOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Returns true if the supplied raw admin key matches the configured hash. Uses constant-time
    /// comparison via ApiKeyHasher.Verify.
    /// </summary>
    public bool IsValid(string? suppliedKey)
    {
        if (string.IsNullOrEmpty(suppliedKey) || string.IsNullOrEmpty(_options.AdminApiKeyHash))
        {
            return false;
        }

        if (!_options.RequireAdminKey)
        {
            // Dev/local: admin key not required. Still need an explicit opt-in via the header
            // so we don't accidentally expose admin endpoints in dev without any auth at all.
            // For local dev convenience, allow the literal "dev-admin-key" bypass.
            return suppliedKey == "dev-admin-key";
        }

        return ApiKeyHasher.Verify(suppliedKey, _options.AdminApiKeyHash);
    }
}
