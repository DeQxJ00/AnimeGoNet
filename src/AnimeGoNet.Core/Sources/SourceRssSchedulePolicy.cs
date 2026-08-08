using AnimeGoNet.Core.Scheduling;

namespace AnimeGoNet.Core.Sources;

public static class SourceRssSchedulePolicy
{
    public const string DefaultCron = "0 0/15 * * * ?";
    public const int MaximumUrlLength = 4096;
    public const int MaximumCronLength = 256;

    public static string NormalizeCron(string? value)
    {
        var cron = string.IsNullOrWhiteSpace(value) ? DefaultCron : value.Trim();
        if (cron.Length > MaximumCronLength)
        {
            throw new ArgumentException("rss_schedule_cron must not exceed 256 characters.");
        }
        try
        {
            _ = SixFieldCronExpression.Parse(cron);
        }
        catch (CronExpressionException exception)
        {
            throw new ArgumentException(exception.Message, nameof(value), exception);
        }
        return cron;
    }

    public static string? NormalizeFeedUrl(string adapter, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Trim();
        if (candidate.Length > MaximumUrlLength
            || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.IdnHost)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "rss_feed_url must be an absolute HTTP(S) URL without userinfo or fragment.");
        }
        if (!string.Equals(adapter, "mikan", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("rss_feed_url can only be configured for a Mikan adapter.");
        }
        return uri.AbsoluteUri;
    }

    public static void ValidateEnabled(
        string adapter,
        bool sourceEnabled,
        bool scheduleEnabled,
        string? feedUrl)
    {
        if (!scheduleEnabled) return;
        if (!sourceEnabled)
        {
            throw new ArgumentException("An RSS schedule requires the source profile to be enabled.");
        }
        if (!string.Equals(adapter, "mikan", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("RSS scheduling currently requires a Mikan adapter.");
        }
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            throw new ArgumentException("rss_feed_url is required when RSS scheduling is enabled.");
        }
    }

    public static bool IsHostAllowed(
        string host,
        IReadOnlyList<string> allowedPatterns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(allowedPatterns);
        var normalizedHost = new Uri($"https://{host}/").IdnHost.ToLowerInvariant();
        foreach (var rawPattern in allowedPatterns)
        {
            var wildcard = rawPattern.StartsWith("*.", StringComparison.Ordinal);
            var pattern = wildcard ? rawPattern[2..] : rawPattern;
            if (!Uri.TryCreate($"https://{pattern}/", UriKind.Absolute, out var patternUri))
            {
                continue;
            }

            var normalizedPattern = patternUri.IdnHost.ToLowerInvariant();
            if ((!wildcard
                    && string.Equals(
                        normalizedHost,
                        normalizedPattern,
                        StringComparison.Ordinal))
                || (wildcard
                    && normalizedHost.Length > normalizedPattern.Length
                    && normalizedHost.EndsWith(
                        '.' + normalizedPattern,
                        StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}
