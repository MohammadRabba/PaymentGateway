namespace PaymentGateway.Api.Infrastructure.Authentication;

/// <summary>
/// API key authentication configuration. Loaded from "Authentication:ApiKey" section.
/// </summary>
public sealed class ApiKeyAuthOptions
{
    public const string SectionName = "Authentication:ApiKey";

    /// <summary>Header name carrying the merchant API key. Default: Authorization Bearer.</summary>
    public string HeaderName { get; init; } = "Authorization";

    /// <summary>Prefix to strip from the header value (e.g. "Bearer ").</summary>
    public string Scheme { get; init; } = "Bearer";

    /// <summary>Header name for the admin API key. Default: X-Admin-Key.</summary>
    public string AdminHeaderName { get; init; } = "X-Admin-Key";

    /// <summary>Paths that require admin authorisation. Matched by prefix.</summary>
    public string[] AdminPathPrefixes { get; init; } = { "/api/v1/admin", "/api/v1/merchants" };

    /// <summary>Paths that skip authentication entirely (health, swagger).</summary>
    public string[] AnonymousPathPrefixes { get; init; } =
    {
        "/health", "/swagger", "/openapi", "/scalar", "/favicon", "/",
    };
}
