using System.Globalization;

namespace AnimeGoNet.Core.Feeds;

public static class MikanIdentityParser
{
    public static int? TryParseMikanId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 2 < segments.Length; index++)
        {
            if (segments[index].Equals("home", StringComparison.OrdinalIgnoreCase)
                && segments[index + 1].Equals("bangumi", StringComparison.OrdinalIgnoreCase)
                && TryPositive(segments[index + 2], out var pathId))
            {
                return pathId;
            }
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            if (!Uri.UnescapeDataString(key).Equals("bangumiId", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var encoded = separator < 0 ? string.Empty : pair[(separator + 1)..];
            if (TryPositive(Uri.UnescapeDataString(encoded), out var queryId))
            {
                return queryId;
            }
        }

        return null;
    }

    private static bool TryPositive(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result > 0;
}
