namespace PaymentGateway.Application.Common;

/// <summary>
/// Read-only authenticated merchant context. Populated by the API key authentication middleware
/// from the resolved Merchant.ApiKeyHash lookup. Client-supplied MerchantId is ignored if it
/// conflicts with this context — the authenticated identity is always authoritative.
/// </summary>
public interface IMerchantContext
{
    /// <summary>True if the current request is authenticated as a specific merchant.</summary>
    bool IsAuthenticated { get; }

    /// <summary>The authenticated merchant's ID. Throws if IsAuthenticated is false.</summary>
    Guid MerchantId { get; }

    /// <summary>True if the current request uses an admin key (can act on any merchant).</summary>
    bool IsAdmin { get; }

    /// <summary>
    /// The effective merchant ID to use for this request: the authenticated merchant's ID for
    /// merchant-key requests, or a client-specified merchant ID for admin-key requests.
    /// </summary>
    Guid? ResolveMerchantId(Guid? clientSuppliedId);
}
