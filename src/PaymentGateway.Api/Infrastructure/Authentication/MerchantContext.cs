using PaymentGateway.Application.Common;

namespace PaymentGateway.Api.Infrastructure.Authentication;

/// <summary>
/// Scoped authenticated merchant context. Populated by ApiKeyAuthenticationMiddleware or
/// AdminAuthenticationMiddleware. The handlers read it via IMerchantContext to enforce isolation:
/// a merchant cannot access another merchant's resources. Client-supplied MerchantId is ignored
/// when it conflicts with the authenticated identity.
/// </summary>
public sealed class MerchantContext : IMerchantContext
{
    /// <summary>True if the current request is authenticated as a specific merchant OR as admin.</summary>
    public bool IsAuthenticated { get; private set; }

    public Guid MerchantId { get; private set; }

    public bool IsAdmin { get; private set; }

    /// <summary>
    /// Set the merchant context from a merchant API key authentication.
    /// </summary>
    public void SetMerchant(Guid merchantId)
    {
        IsAuthenticated = true;
        MerchantId = merchantId;
        IsAdmin = false;
    }

    /// <summary>
    /// Set the merchant context from an admin API key authentication. Admins can act on any merchant
    /// — the client-supplied MerchantId is used (with explicit authorisation checks in handlers).
    /// </summary>
    public void SetAdmin()
    {
        IsAuthenticated = true;
        IsAdmin = true;
    }

    /// <summary>
    /// Resolve the effective merchant ID for the request. For merchant-key requests, always
    /// returns the authenticated MerchantId (client-supplied ID is ignored). For admin-key
    /// requests, returns the client-supplied ID (admin can act on any merchant).
    /// </summary>
    public Guid? ResolveMerchantId(Guid? clientSuppliedId)
    {
        if (!IsAuthenticated)
        {
            return null;
        }

        if (IsAdmin)
        {
            return clientSuppliedId;
        }

        return MerchantId;
    }
}
