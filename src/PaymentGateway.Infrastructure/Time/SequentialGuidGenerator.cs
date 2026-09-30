using System.Security.Cryptography;
using PaymentGateway.Application.Common;

namespace PaymentGateway.Infrastructure.Time;

/// <summary>
/// Sequential GUID generator. Produces GUIDs whose leading bytes are derived from the current
/// timestamp so that consecutive IDs sort together — this gives SQL Server clustered indexes
/// good locality (page splits at the end of the index instead of random insertion points).
///
/// Format: 10 bytes of timestamp + 6 bytes of cryptographic randomness. The timestamp is
/// milliseconds since 2024-01-01 (a custom epoch to maximise the usable 10-byte range).
/// </summary>
public sealed class SequentialGuidGenerator : IIdGenerator
{
    private static readonly DateTimeOffset Epoch = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public Guid NewId()
    {
        var ms = (DateTimeOffset.UtcNow - Epoch).TotalMilliseconds;
        var msInt = (ulong)Math.Max(0, ms);

        Span<byte> bytes = stackalloc byte[16];
        // First 8 bytes: timestamp (big-endian so the leading bytes sort chronologically).
        bytes[0] = (byte)(msInt >> 56);
        bytes[1] = (byte)(msInt >> 48);
        bytes[2] = (byte)(msInt >> 40);
        bytes[3] = (byte)(msInt >> 32);
        bytes[4] = (byte)(msInt >> 24);
        bytes[5] = (byte)(msInt >> 16);
        bytes[6] = (byte)(msInt >> 8);
        bytes[7] = (byte)msInt;
        // Next 8 bytes: cryptographic randomness for collision-resistance.
        RandomNumberGenerator.Fill(bytes.Slice(8, 8));

        return new Guid(bytes);
    }
}
