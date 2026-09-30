using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Infrastructure.Security;

/// <summary>
/// API key hashing. SHA-256 is sufficient for random API keys (no need for bcrypt/PBKDF2) because
/// the keys are 32-byte cryptographically-secure random values, not human passwords. Constant-time
/// comparison on lookup is handled by the database (UNIQUE index on ApiKeyHash).
/// </summary>
public static class ApiKeyHasher
{
    /// <summary>
    /// Hash a raw API key to its hex-encoded SHA-256 representation.
    /// </summary>
    public static string Hash(string rawApiKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawApiKey);
        var bytes = Encoding.UTF8.GetBytes(rawApiKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Constant-time equality check. Use this when comparing a supplied hash to a stored hash
    /// directly in code (not via database lookup).
    /// </summary>
    public static bool Verify(string rawApiKey, string storedHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawApiKey);
        ArgumentException.ThrowIfNullOrEmpty(storedHash);
        var computed = Hash(rawApiKey);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(storedHash.ToLowerInvariant()));
    }
}
