using System.Globalization;
using System.Text.RegularExpressions;

namespace AnimeGoNet.App.AiTesterCompat;

public static partial class PubDatePriority
{
    public static PubDatePriorityGate Evaluate(TesterConfig config, MatchRequestInput input)
    {
        if (!input.EnableBangumiPubDateFirst)
        {
            return new(false, input.TorrentFileCount, input.BgmEpisodeCandidate, null, "Bangumi pubDate 优先开关已关闭");
        }

        if (!input.IsMikanRssSource)
        {
            return new(false, input.TorrentFileCount, input.BgmEpisodeCandidate, null, "不是 Mikan RSS 来源");
        }

        if (input.TorrentFileCount is not 1)
        {
            string countReason = input.TorrentFileCount is null
                ? "未导入可信 torrent，torrent_file_count unavailable"
                : $"torrent_file_count={input.TorrentFileCount}，不是单文件 torrent";
            return new(false, input.TorrentFileCount, input.BgmEpisodeCandidate, null, countReason);
        }

        if (input.Bgmid is null)
        {
            return new(false, input.TorrentFileCount, input.BgmEpisodeCandidate, null, "bgmid 为空");
        }

        if (!config.EnableBgmMcp)
        {
            return new(false, input.TorrentFileCount, input.BgmEpisodeCandidate, null, "BGM MCP 已关闭");
        }

        if (input.BgmEpisodeCandidate is null)
        {
            return new(false, input.TorrentFileCount, null, null, "bgm_episode_candidate 为空");
        }

        if (!TryNormalizePubDate(input.MikanPubDate, out string? normalized, out string reason))
        {
            return new(false, input.TorrentFileCount, input.BgmEpisodeCandidate, null, reason);
        }

        return new(true, input.TorrentFileCount, input.BgmEpisodeCandidate, normalized, "开关已启用，且满足单文件、bgmid、bgm_episode_candidate、pubDate 和 BGM MCP 门禁");
    }

    public static bool TryNormalizePubDate(string? value, out string? normalized, out string reason)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            reason = "Mikan pubDate 为空";
            return false;
        }

        string text = value.Trim();
        bool hasOffset = text.EndsWith('Z') || OffsetSuffixRegex().IsMatch(text);
        if (hasOffset && DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
            out DateTimeOffset offsetValue))
        {
            normalized = offsetValue.ToString("O", CultureInfo.InvariantCulture);
            reason = "valid";
            return true;
        }

        string[] formats = ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"];
        if (!hasOffset && DateTime.TryParseExact(
            text,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime localValue))
        {
            normalized = localValue.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
            reason = "valid";
            return true;
        }

        reason = "Mikan pubDate 格式无效";
        return false;
    }

    [GeneratedRegex(@"[+-]\d{2}:\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetSuffixRegex();
}
