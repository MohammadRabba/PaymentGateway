using Microsoft.Extensions.Options;
using PaymentGateway.Api.Infrastructure.Authentication;
using PaymentGateway.Application.Merchants;
using PaymentGateway.Infrastructure.Security;

namespace PaymentGateway.Api.Middleware;

/// <summary>
/// Combined authentication middleware. Handles both merchant API key (Authorization: Bearer ...)
/// and admin API key (X-Admin-Key) authentication in one pass.
///
/// Logic:
///   1. Anonymous paths (health, swagger) skip auth entirely.
///   2. Admin paths (admin endpoints, merchant registration) require a valid X-Admin-Key.
///      On success, sets MerchantContext.SetAdmin().
///   3. All other paths require a valid Authorization: Bearer <api-key>. The raw key is hashed
///      (SHA-256) and looked up against Merchant.ApiKeyHash. On success, sets
///      MerchantContext.SetMerchant(merchantId).
///   4. Missing/invalid auth returns 401 with ProblemDetails. The body never reveals which check
///      failed (no information leak).
///
/// The raw API key is NEVER logged. Only the hash is compared (via the database UNIQUE index on
/// ApiKeyHash, the lookup is O(1)).
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiKeyAuthOptions _authOptions;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;

    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        IOptions<ApiKeyAuthOptions> authOptions,
        ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _authOptions = authOptions.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        MerchantContext merchantContext,
        AdminAuthenticator adminAuthenticator,
        MerchantService merchantService)
    {
        var path = context.Request.Path.Value ?? "/";

        // Anonymous paths: skip auth entirely.
        if (IsAnonymousPath(path))
        {
            await _next(context);
            return;
        }

        // Admin paths: require X-Admin-Key.
        if (IsAdminPath(path))
        {
            var adminKey = context.Request.Headers[_authOptions.AdminHeaderName].ToString();
            if (!adminAuthenticator.IsValid(adminKey))
            {
                _logger.LogWarning("Admin auth failed for path {Path} from {RemoteIp}",
                    path, context.Connection.RemoteIpAddress);
                await WriteUnauthorizedAsync(context, "Admin API key required.");
                return;
            }

            merchantContext.SetAdmin();
            await _next(context);
            return;
        }

        // Merchant paths: require Authorization: Bearer <api-key>.
        var authHeader = context.Request.Headers[_authOptions.HeaderName].ToString();
        if (string.IsNullOrEmpty(authHeader))
        {
            await WriteUnauthorizedAsync(context, "Authorization header is required.");
            return;
        }

        var scheme = _authOptions.Scheme + " ";
        if (!authHeader.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorizedAsync(context, $"Expected '{_authOptions.Scheme}' scheme.");
            return;
        }

        var rawApiKey = authHeader[scheme.Length..].Trim();
        if (string.IsNullOrEmpty(rawApiKey) || rawApiKey.Length < _authOptions.HeaderName.Length)
        {
            await WriteUnauthorizedAsync(context, "API key is missing or malformed.");
            return;
        }

        // Look up the merchant by hashing the API key. This is O(1) via the UNIQUE index on ApiKeyHash.
        var merchant = await merchantService.ResolveByApiKeyAsync(rawApiKey, context.RequestAborted);
        if (merchant is null)
        {
            // Don't reveal whether the key exists or not — same response as missing key.
            await WriteUnauthorizedAsync(context, "Invalid API key.");
            return;
        }

        if (!merchant.CanAcceptPayments())
        {
            await WriteForbiddenAsync(context, "Merchant account is not active.");
            return;
        }

        merchantContext.SetMerchant(merchant.Id);
        await _next(context);
    }

    private bool IsAnonymousPath(string path)
    {
        foreach (var prefix in _authOptions.AnonymousPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private bool IsAdminPath(string path)
    {
        foreach (var prefix in _authOptions.AdminPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";

        var problem = new
        {
            type = "https://payment-gateway.example.com/problems/unauthorized",
            title = "Unauthorized",
            status = 401,
            detail = detail,
            instance = context.Request.Path.Value,
            traceId = context.TraceIdentifier,
            correlationId = context.Response.Headers["X-Correlation-ID"].ToString(),
        };

        await context.Response.WriteAsJsonAsync(problem);
    }

    private static async Task WriteForbiddenAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";

        var problem = new
        {
            type = "https://payment-gateway.example.com/problems/forbidden",
            title = "Forbidden",
            status = 403,
            detail = detail,
            instance = context.Request.Path.Value,
            traceId = context.TraceIdentifier,
            correlationId = context.Response.Headers["X-Correlation-ID"].ToString(),
        };

        await context.Response.WriteAsJsonAsync(problem);
    }
}
