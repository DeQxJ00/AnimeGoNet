using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AnimeGoNet.Data.Library;

public sealed record FallbackDedupScope(string Kind, string Key);

public static class FallbackDedupScopeResolver
{
    public static FallbackDedupScope Resolve(
        string sourceId,
        int? mikanId,
        string? sourceWorkId,
        string? sourceItemId,
        string infoHash,
        string relativePath,
        long sizeBytes,
        string? sourceEpisode,
        int? bangumiEpisodeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(infoHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        var normalizedSource = sourceId.Trim().ToLowerInvariant();
        var normalizedEpisode = NormalizeEpisode(sourceEpisode);
        if (bangumiEpisodeId is > 0)
        {
            return new FallbackDedupScope(
                "bangumi_episode",
                bangumiEpisodeId.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (normalizedSource == "mikan"
            && mikanId is > 0
            && normalizedEpisode is not null)
        {
            return new FallbackDedupScope(
                "mikan_episode",
                $"{mikanId.Value.ToString(CultureInfo.InvariantCulture)}:source:{normalizedEpisode}");
        }

        var normalizedWork = NormalizeOptional(sourceWorkId);
        if (normalizedWork is not null && normalizedEpisode is not null)
        {
            return new FallbackDedupScope(
                "source_work_episode",
                $"{normalizedSource}:{LengthPrefix(normalizedWork)}:source:{normalizedEpisode}");
        }

        var fingerprintInput = string.Concat(
            LengthPrefix(normalizedSource),
            LengthPrefix(NormalizeOptional(sourceItemId) ?? string.Empty),
            LengthPrefix(infoHash.Trim().ToLowerInvariant()),
            LengthPrefix(relativePath.Replace('\\', '/').Trim()),
            sizeBytes.ToString(CultureInfo.InvariantCulture));
        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput)));
        return new FallbackDedupScope("torrent_file", fingerprint);
    }

    private static string? NormalizeEpisode(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        if (decimal.TryParse(
                normalized,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var number))
        {
            return number > 0
                ? number.ToString("0.############################", CultureInfo.InvariantCulture)
                : null;
        }

        return normalized.ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string LengthPrefix(string value) =>
        $"{value.Length.ToString(CultureInfo.InvariantCulture)}:{value}";
}
