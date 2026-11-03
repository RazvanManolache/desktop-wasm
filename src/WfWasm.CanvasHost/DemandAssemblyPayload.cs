using System.Security.Cryptography;

namespace WfWasm.CanvasHost;

internal static class DemandAssemblyPayload
{
    // The sync browser bridge prefixes the HTTP response's SHA-256 header
    // as 32 binary bytes. An empty response is an ordinary resolution miss.
    internal static byte[]? Decode(byte[] packet)
    {
        if (packet.Length == 0) return null;
        if (packet.Length <= 32) throw new InvalidDataException("Truncated assembly response.");
        ReadOnlySpan<byte> expectedSha = packet.AsSpan(0, 32);
        ReadOnlySpan<byte> payload = packet.AsSpan(32);
        Span<byte> actualSha = stackalloc byte[32];
        SHA256.HashData(payload, actualSha);
        if (!CryptographicOperations.FixedTimeEquals(expectedSha, actualSha))
            throw new InvalidDataException("Runtime assembly integrity check failed.");
        return payload.ToArray();
    }
}
