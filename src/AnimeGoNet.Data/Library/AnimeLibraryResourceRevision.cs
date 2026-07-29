using System.Security.Cryptography;
using System.Text;

namespace AnimeGoNet.Data.Library;

internal static class AnimeLibraryResourceRevision
{
    public static string Create(
        string seriesRowId,
        string seriesUpdatedAtUtc,
        string seasonRowId,
        string seasonUpdatedAtUtc)
    {
        var payload = string.Concat(
            seriesRowId,
            "\n",
            seriesUpdatedAtUtc,
            "\n",
            seasonRowId,
            "\n",
            seasonUpdatedAtUtc);
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
