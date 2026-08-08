using System.Security.Cryptography;
using System.Text;

namespace AnimeGoNet.Core.Compatibility;

public static class StableHash
{
    public static string Sha256LowerHex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static string Sha256LowerHex(ReadOnlySpan<byte> value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));
}
