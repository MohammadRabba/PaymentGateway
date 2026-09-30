using System.Security.Cryptography;

namespace PaymentGateway.Infrastructure.Security;

/// <summary>
/// Cryptographically-secure secret generator. Used for merchant API keys and webhook secrets.
/// Returns hex-encoded strings of the requested byte length.
/// </summary>
public static class SecretGenerator
{
    /// <summary>
    /// Generate a hex-encoded random string of the given byte length (output string length = 2 * byteLength).
    /// </summary>
    public static string Generate(int byteLength = 32)
    {
        if (byteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), "Byte length must be positive.");
        }

        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Generate a prefixed API key suitable for sending to merchants. The "pgk_" prefix is a
    /// human-identifiable marker; the rest is 32 random bytes hex-encoded.
    /// </summary>
    public static string GenerateApiKey() => "pgk_" + Generate(32);
}
