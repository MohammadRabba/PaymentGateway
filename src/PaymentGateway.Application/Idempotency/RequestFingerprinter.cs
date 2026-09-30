using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.Application.Idempotency;

/// <summary>
/// Computes a deterministic SHA-256 fingerprint of a request DTO. The fingerprint is used to detect
/// reuse of the same idempotency key with a different payload (returns 422 in that case).
/// Uses canonical JSON serialisation (camelCase, no indentation, nulls ignored) so that semantically
/// equivalent requests produce the same hash regardless of property declaration order in the DTO.
/// </summary>
public sealed class RequestFingerprinter
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
    };

#pragma warning disable CA1822 // Mark members as static
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Part of instance API contract")]
    public string ComputeHash<T>(T request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var json = JsonSerializer.Serialize(request, CanonicalOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
#pragma warning restore CA1822 // Mark members as static
}
