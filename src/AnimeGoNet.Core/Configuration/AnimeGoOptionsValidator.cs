using System.Text.RegularExpressions;
using AnimeGoNet.Core.Scheduling;

namespace AnimeGoNet.Core.Configuration;

using AnimeGoNet.Core.Sources;
using AnimeGoNet.Core.Downloads;

public static partial class AnimeGoOptionsValidator
{
    public static IReadOnlyList<string> Validate(AnimeGoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        ValidateAbsolutePath(options.Paths.DataPath, "data_path", errors);
        ValidateAbsolutePath(options.Paths.DownloadPath, "download_path", errors);
        ValidateAbsolutePath(options.Paths.SavePath, "save_path", errors);

        if (string.IsNullOrWhiteSpace(options.Web.Host)
            || !string.Equals(options.Web.Host, options.Web.Host.Trim(), StringComparison.Ordinal)
            || options.Web.Host.Length > 253
            || Uri.CheckHostName(options.Web.Host) == UriHostNameType.Unknown)
        {
            errors.Add("Web host must be a valid trimmed DNS name or IP address.");
        }

        if (options.Web.Port is < 0 or > 65535)
        {
            errors.Add("Web port must be between 0 and 65535.");
        }

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

            if (!IsDownloaderBaseUrl(downloader.BaseUrl))
            {
                errors.Add(
                    $"Downloader '{rawId}' base URL must be an absolute HTTP(S) URL without credentials, query or fragment.");
            }

            if (!PathBoundary.IsWithin(options.Paths.DownloadPath, downloader.DownloadPath))
            {
                errors.Add($"Downloader '{rawId}' download path must be inside download_path.");
            }
        }

        var sourceProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in options.InitialSourceProfiles)
        {
            if (!profile.Id.Equals(profile.Id.ToLowerInvariant(), StringComparison.Ordinal) || !IsStableId(profile.Id))
            {
                errors.Add($"Source profile id '{profile.Id}' is not a stable lowercase id.");
            }

            if (!sourceProfileIds.Add(profile.Id))
            {
                errors.Add($"Source profile id '{profile.Id}' is duplicated.");
            }

            if (!profile.Adapter.Equals(profile.Adapter.ToLowerInvariant(), StringComparison.Ordinal)
                || !IsStableId(profile.Adapter))
            {
                errors.Add($"Source profile '{profile.Id}' adapter is not a stable lowercase id.");
            }

            if (!profile.DownloaderId.Equals(profile.DownloaderId.ToLowerInvariant(), StringComparison.Ordinal)
                || !IsStableId(profile.DownloaderId))
            {
                errors.Add($"Source profile '{profile.Id}' downloader reference is not a stable lowercase id.");
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

            try
            {
                var cookie = MikanIdentityCookie.NormalizeOptional(
                    profile.MikanIdentityCookie);
                if (cookie is not null
                    && !string.Equals(
                        profile.Adapter,
                        "mikan",
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Source profile '{profile.Id}' can only configure a Mikan identity Cookie when adapter is mikan.");
                }
            }
            catch (ArgumentException exception)
            {
                errors.Add(
                    $"Source profile '{profile.Id}' has an invalid Mikan identity Cookie: {exception.Message}");
            }

            try
            {
                _ = SourceDownloadPolicy.NormalizeCategory(profile.Category);
                _ = SourceDownloadPolicy.NormalizeTags(profile.Tags);
                _ = DownloadDynamicTagTemplate.Normalize(profile.DynamicTagTemplate);
                _ = SourceDownloadPolicy.ValidateSeedingTimeMinutes(
                    profile.FileStrategy switch
                    {
                        FileStrategy.Link => "link",
                        FileStrategy.LinkDelete => "link_delete",
                        FileStrategy.Move => "move",
                        FileStrategy.WaitMove => "wait_move",
                        _ => string.Empty,
                    },
                    profile.SeedingTimeMinutes);
            }
            catch (ArgumentException exception)
            {
                errors.Add($"Source profile '{profile.Id}' download policy is invalid: {exception.Message}");
            }
        }

        if (options.Metadata.Ai.HttpTimeout <= TimeSpan.Zero)
        {
            errors.Add("AI HTTP timeout must be positive.");
        }

        var ai = options.Metadata.Ai;
        if (!string.Equals(ai.Provider, "openai_compatible", StringComparison.Ordinal))
        {
            errors.Add("AI provider must be 'openai_compatible'.");
        }

        if (ai.BaseUrl is not null && !IsHttpEndpoint(ai.BaseUrl))
        {
            errors.Add("AI base URL must be an absolute HTTP(S) URL without credentials.");
        }

        if (ai.Model is not null
            && (string.IsNullOrWhiteSpace(ai.Model)
                || !string.Equals(ai.Model, ai.Model.Trim(), StringComparison.Ordinal)
                || ai.Model.Length > 256))
        {
            errors.Add("AI model must contain 1 to 256 trimmed characters when configured.");
        }

        if (ai.RetryCount is < 0 or > 10)
        {
            errors.Add("AI retry count must be between 0 and 10.");
        }

        if (!IsHttpEndpoint(ai.TmdbMcpUrl))
        {
            errors.Add("TMDB MCP URL must be an absolute HTTP(S) URL without credentials.");
        }

        if (!IsHttpEndpoint(ai.BangumiMcpUrl))
        {
            errors.Add("Bangumi MCP URL must be an absolute HTTP(S) URL without credentials.");
        }

        if (!string.Equals(
            ai.AniDbMappingUrlTemplate,
            AiMatchingOptions.FixedAniDbMappingUrlTemplate,
            StringComparison.Ordinal))
        {
            errors.Add("AniDB mapping URL template is fixed and cannot be overridden.");
        }

        if (!IsMetadataApiBaseUrl(options.Metadata.Tmdb.BaseUrl))
        {
            errors.Add("TMDB base URL must be an absolute HTTP(S) URL ending in '/' without credentials, query or fragment.");
        }

        if (options.Metadata.Tmdb.ProxyUrl is not null
            && !IsMetadataProxyUrl(options.Metadata.Tmdb.ProxyUrl))
        {
            errors.Add("TMDB proxy URL must be an absolute HTTP(S) or SOCKS5 origin without credentials, query or fragment.");
        }

        if (options.Metadata.Tmdb.HttpTimeout <= TimeSpan.Zero)
        {
            errors.Add("TMDB HTTP timeout must be positive.");
        }

        ValidateMetadataRetry(
            "TMDB",
            options.Metadata.Tmdb.RetryCount,
            options.Metadata.Tmdb.RetryDelay,
            errors);

        if (options.Metadata.Tmdb.CacheTtl <= TimeSpan.Zero
            || options.Metadata.Tmdb.CacheTtl > TimeSpan.FromDays(365))
        {
            errors.Add("TMDB cache TTL must be greater than zero and at most 365 days.");
        }

        if (string.IsNullOrWhiteSpace(options.Metadata.Tmdb.Language))
        {
            errors.Add("TMDB language must not be empty.");
        }

        if (!IsMetadataApiBaseUrl(options.Metadata.Bangumi.BaseUrl))
        {
            errors.Add("Bangumi base URL must be an absolute HTTP(S) URL ending in '/' without credentials, query or fragment.");
        }

        if (options.Metadata.Bangumi.ProxyUrl is not null
            && !IsMetadataProxyUrl(options.Metadata.Bangumi.ProxyUrl))
        {
            errors.Add("Bangumi proxy URL must be an absolute HTTP(S) or SOCKS5 origin without credentials, query or fragment.");
        }

        if (options.Metadata.Bangumi.HttpTimeout <= TimeSpan.Zero)
        {
            errors.Add("Bangumi HTTP timeout must be positive.");
        }

        ValidateMetadataRetry(
            "Bangumi",
            options.Metadata.Bangumi.RetryCount,
            options.Metadata.Bangumi.RetryDelay,
            errors);

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

        try
        {
            _ = SixFieldCronExpression.Parse(options.Schedule.RefreshDatabaseCron);
        }
        catch (CronExpressionException exception)
        {
            errors.Add($"Directory database refresh cron is invalid: {exception.Code}.");
        }

        try
        {
            _ = SixFieldCronExpression.Parse(options.DataUpdate.Cron);
        }
        catch (CronExpressionException exception)
        {
            errors.Add($"Data update cron is invalid: {exception.Code}.");
        }

        if (options.DataUpdate.Enabled && options.DataUpdate.ManifestUrl is null)
        {
            errors.Add("Data update manifest URL is required when scheduled updates are enabled.");
        }
        if (options.DataUpdate.ManifestUrl is { } manifestUrl
            && (!IsHttpEndpoint(manifestUrl) || !string.IsNullOrEmpty(manifestUrl.UserInfo)))
        {
            errors.Add("Data update manifest URL must be an absolute HTTP(S) URL without credentials.");
        }
        if (options.DataUpdate.KeepVersions is < 2 or > 10)
        {
            errors.Add("Data update keep versions must be between 2 and 10.");
        }
        if (options.DataUpdate.HttpTimeout <= TimeSpan.Zero
            || options.DataUpdate.HttpTimeout > TimeSpan.FromHours(1))
        {
            errors.Add("Data update HTTP timeout must be between 0 and 3600 seconds.");
        }

        return errors;
    }

    private static void ValidateMetadataRetry(
        string name,
        int retryCount,
        TimeSpan retryDelay,
        List<string> errors)
    {
        if (retryCount is < 0 or > 10)
        {
            errors.Add($"{name} retry count must be between 0 and 10.");
        }

        if (retryDelay < TimeSpan.Zero || retryDelay > TimeSpan.FromMinutes(5))
        {
            errors.Add($"{name} retry delay must be between 0 and 300 seconds.");
        }
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

    private static bool IsHttpEndpoint(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme is "http" or "https"
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment);

    private static bool IsDownloaderBaseUrl(Uri uri) =>
        IsHttpEndpoint(uri)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query);

    private static bool IsMetadataApiBaseUrl(Uri uri) =>
        IsHttpEndpoint(uri)
        && uri.AbsolutePath.EndsWith('/')
        && string.IsNullOrEmpty(uri.Query);

    private static bool IsMetadataProxyUrl(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme is "http" or "https" or "socks5"
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath == "/"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex StableId();
}
