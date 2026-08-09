namespace AnimeGoNet.Core.Configuration;

public static class OutboundProxyPolicy
{
    public static bool ShouldProxy(Uri destination, OutboundProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        return options.Url is not null
            && options.HostPatterns.Any(pattern => Matches(destination.IdnHost, pattern));
    }

    public static bool Matches(string host, string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[1..];
            return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && host.Length > suffix.Length;
        }

        return string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
