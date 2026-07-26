using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Configuration;

public static partial class AnimeGoOptionsValidator
{
    public static IReadOnlyList<string> Validate(AnimeGoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        ValidateAbsolutePath(options.Paths.DataPath, "data_path", errors);
        ValidateAbsolutePath(options.Paths.DownloadPath, "download_path", errors);
        ValidateAbsolutePath(options.Paths.SavePath, "save_path", errors);

        if (options.Downloaders.Count == 0)
        {
            errors.Add("At least one qBittorrent instance is required.");
        }

        foreach (var (rawId, downloader) in options.Downloaders)
        {
            var id = rawId.ToLowerInvariant();
            if (!rawId.Equals(id, StringComparison.Ordinal) || !IsStableId(id))
            {
                errors.Add($"Downloader id '{rawId}' must already be lowercase and contain only letters, digits, '.', '_' or '-'.");
            }

            if (!downloader.Type.Equals(DownloaderTypes.Qbittorrent, StringComparison.Ordinal))
            {
                errors.Add($"Downloader '{rawId}' has unsupported type '{downloader.Type}'. Only qBittorrent is supported.");
            }

            if (downloader.BaseUrl.Scheme is not ("http" or "https"))
            {
                errors.Add($"Downloader '{rawId}' base URL must use HTTP or HTTPS.");
            }

            if (!PathBoundary.IsWithin(options.Paths.DownloadPath, downloader.DownloadPath))
            {
                errors.Add($"Downloader '{rawId}' download path must be inside download_path.");
            }
        }

        foreach (var profile in options.InitialSourceProfiles)
        {
            if (!profile.Id.Equals(profile.Id.ToLowerInvariant(), StringComparison.Ordinal) || !IsStableId(profile.Id))
            {
                errors.Add($"Source profile id '{profile.Id}' is not a stable lowercase id.");
            }

            if (!options.Downloaders.ContainsKey(profile.DownloaderId))
            {
                errors.Add($"Source profile '{profile.Id}' references missing downloader '{profile.DownloaderId}'.");
            }

            if (profile.AllowedTorrentHosts.Count == 0)
            {
                errors.Add($"Source profile '{profile.Id}' requires at least one allowed Torrent host.");
            }

            foreach (var host in profile.AllowedTorrentHosts)
            {
                if (!IsValidTorrentHostPattern(host))
                {
                    errors.Add($"Source profile '{profile.Id}' has invalid Torrent host pattern '{host}'.");
                }
            }
        }

        if (options.Metadata.Ai.HttpTimeout <= TimeSpan.Zero)
        {
            errors.Add("AI HTTP timeout must be positive.");
        }

        if (!options.Metadata.Tmdb.BaseUrl.IsAbsoluteUri
            || options.Metadata.Tmdb.BaseUrl.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(options.Metadata.Tmdb.BaseUrl.UserInfo)
            || options.Metadata.Tmdb.BaseUrl.AbsolutePath != "/"
            || !string.IsNullOrEmpty(options.Metadata.Tmdb.BaseUrl.Query)
            || !string.IsNullOrEmpty(options.Metadata.Tmdb.BaseUrl.Fragment))
        {
            errors.Add("TMDB base URL must be an absolute HTTP(S) origin without credentials, query or fragment.");
        }

        if (options.Metadata.Tmdb.HttpTimeout <= TimeSpan.Zero)
        {
            errors.Add("TMDB HTTP timeout must be positive.");
        }

        if (string.IsNullOrWhiteSpace(options.Metadata.Tmdb.Language))
        {
            errors.Add("TMDB language must not be empty.");
        }

        if (options.TorrentFetch.Timeout <= TimeSpan.Zero)
        {
            errors.Add("Torrent fetch timeout must be positive.");
        }

        if (options.TorrentFetch.MaxResponseBytes <= 0)
        {
            errors.Add("Torrent maximum response size must be positive.");
        }

        if (options.TorrentFetch.MaxRedirects is < 0 or > 10)
        {
            errors.Add("Torrent maximum redirects must be between 0 and 10.");
        }

        if (options.TorrentFetch.StagingTtl <= TimeSpan.Zero)
        {
            errors.Add("Torrent staging TTL must be positive.");
        }

        return errors;
    }

    public static bool IsStableId(string value) =>
        !string.IsNullOrWhiteSpace(value) && StableId().IsMatch(value);

    public static bool IsValidTorrentHostPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || !string.Equals(pattern, pattern.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        var host = pattern.StartsWith("*.", StringComparison.Ordinal) ? pattern[2..] : pattern;
        return host.Length > 0
            && !host.Contains('*', StringComparison.Ordinal)
            && Uri.TryCreate($"https://{host}/", UriKind.Absolute, out var uri)
            && string.Equals(uri.IdnHost, host, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateAbsolutePath(string path, string name, List<string> errors)
    {
        if (!PathBoundary.IsAbsolute(path))
        {
            errors.Add($"{name} must be an absolute path.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex StableId();
}
