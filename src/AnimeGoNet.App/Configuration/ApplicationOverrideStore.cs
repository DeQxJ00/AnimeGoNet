using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Configuration;

public sealed record ApplicationOverrideEntry(
    string TmdbBaseUrl,
    string TmdbLanguage,
    double TmdbHttpTimeoutSeconds,
    bool TmdbApiKeyOverridden,
    string? TmdbApiKey,
    bool TmdbReadAccessTokenOverridden,
    string? TmdbReadAccessToken,
    bool SeasonFailureSkip,
    bool SeasonFailureBacktrace,
    bool SeasonFailureUseTitleSeason,
    bool SeasonFailureUseFirstSeason,
    bool AiUseSeasonMatch,
    bool AiUseEpisodeMatch,
    double AiHttpTimeoutSeconds,
    bool TmdbFailureUseBangumi,
    bool MikanTrustedOffsetCacheEnabled,
    double TorrentHttpTimeoutSeconds,
    long TorrentMaxResponseBytes,
    int TorrentMaxRedirects,
    double TorrentStagingTtlSeconds,
    DateTimeOffset UpdatedAtUtc,
    string? BangumiBaseUrl = null,
    double? BangumiHttpTimeoutSeconds = null,
    bool? AiUseMetadataMatch = null,
    IReadOnlyList<string>? InheritedFields = null,
    bool? DataUpdateEnabled = null,
    string? DataUpdateCron = null,
    bool? DataUpdateManifestUrlOverridden = null,
    string? DataUpdateManifestUrl = null,
    bool? DataUpdateAutoDownload = null,
    bool? DataUpdateAutoImport = null,
    int? DataUpdateKeepVersions = null,
    double? DataUpdateHttpTimeoutSeconds = null,
    int? TmdbRetryCount = null,
    double? TmdbRetryDelaySeconds = null,
    int? BangumiRetryCount = null,
    double? BangumiRetryDelaySeconds = null,
    double? TmdbCacheHours = null,
    string? MikanBaseUrl = null,
    string? TmdbImageBaseUrl = null,
    bool? OutboundProxyUrlOverridden = null,
    string? OutboundProxyUrl = null,
    IReadOnlyList<string>? OutboundProxyHosts = null,
    bool? AiBaseUrlOverridden = null,
    string? AiBaseUrl = null,
    bool? AiApiKeyOverridden = null,
    string? AiApiKey = null,
    bool? AiModelOverridden = null,
    string? AiModel = null,
    string? AiTmdbMcpUrl = null,
    string? AiBangumiMcpUrl = null,
    bool? WriteBangumiIdWhenTmdbMatched = null,
    string? AiPromptTemplate = null,
    double? MikanEpisodeIdentityCacheHours = null,
    double? MikanBangumiIdentityCacheHours = null,
    bool? AiDebugMode = null,
    bool? AiReasoningEffortOverridden = null,
    string? AiReasoningEffort = null,
    int? MikanTrustedOffsetRequiredEpisodes = null);

public sealed record ApplicationOverrideSnapshot(
    int FormatVersion,
    long Revision,
    ApplicationOverrideEntry? Settings);

public sealed class ApplicationConfigurationRuntimeState(long appliedRevision)
{
    private long _appliedRevision = appliedRevision;

    public long AppliedRevision => Interlocked.Read(ref _appliedRevision);

    public void MarkApplied(long revision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        Interlocked.Exchange(ref _appliedRevision, revision);
    }
}

public sealed class ApplicationOverrideRevisionException : InvalidOperationException;

public sealed class ApplicationOverrideStore : IDisposable
{
    private const int CurrentFormatVersion = 1;
    private readonly string _path;
    private readonly string _backupsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ApplicationOverrideStore(string configurationPath, string backupsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupsPath);
        _path = Path.Combine(configurationPath, "application.private.json");
        _backupsPath = backupsPath;
    }

    public void Dispose() => _gate.Dispose();

    public async Task<ApplicationOverrideSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ApplicationOverrideSnapshot> SaveAsync(
        ApplicationOverrideEntry settings,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (current.Revision != expectedRevision)
            {
                throw new ApplicationOverrideRevisionException();
            }

            var saved = new ApplicationOverrideSnapshot(
                CurrentFormatVersion,
                current.Revision + 1,
                settings);
            if (File.Exists(_path))
            {
                await BackupCoreAsync(current.Revision, cancellationToken)
                    .ConfigureAwait(false);
            }
            await SaveCoreAsync(saved, cancellationToken).ConfigureAwait(false);
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ApplicationOverrideSnapshot> DeleteAsync(
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (current.Revision != expectedRevision)
            {
                throw new ApplicationOverrideRevisionException();
            }

            if (current.Settings is null)
            {
                return current;
            }

            var saved = new ApplicationOverrideSnapshot(
                CurrentFormatVersion,
                current.Revision + 1,
                null);
            await BackupCoreAsync(current.Revision, cancellationToken)
                .ConfigureAwait(false);
            await SaveCoreAsync(saved, cancellationToken).ConfigureAwait(false);
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static AnimeGoOptions Apply(
        AnimeGoOptions options,
        ApplicationOverrideSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(snapshot);
        var settings = snapshot.Settings;
        if (settings is null)
        {
            return options;
        }

        var inheritedFields = settings.InheritedFields?
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var mikanBaseUrl = inheritedFields.Contains("mikan_base_url")
            || settings.MikanBaseUrl is null
            ? options.Metadata.Mikan.BaseUrl
            : ParseRequiredUri(settings.MikanBaseUrl, "Mikan base URL");
        var tmdbBaseUrl = options.Metadata.Tmdb.BaseUrl;
        if (!inheritedFields.Contains("tmdb_base_url")
            && !Uri.TryCreate(settings.TmdbBaseUrl, UriKind.Absolute, out tmdbBaseUrl))
        {
            throw new InvalidOperationException("Application private configuration has an invalid TMDB base URL.");
        }
        var tmdbImageBaseUrl = inheritedFields.Contains("tmdb_image_base_url")
            || settings.TmdbImageBaseUrl is null
            ? options.Metadata.Tmdb.ImageBaseUrl
            : ParseRequiredUri(settings.TmdbImageBaseUrl, "TMDB image base URL");
        var bangumiBaseUrl = inheritedFields.Contains("bangumi_base_url")
            || settings.BangumiBaseUrl is null
            ? options.Metadata.Bangumi.BaseUrl
            : ParseRequiredUri(settings.BangumiBaseUrl, "Bangumi base URL");
        var outboundProxy = options.OutboundProxy;
        if (!inheritedFields.Contains("outbound_proxy_url")
            && settings.OutboundProxyUrlOverridden == true)
        {
            outboundProxy = outboundProxy with
            {
                Url = ParseOptionalUri(settings.OutboundProxyUrl, "outbound proxy URL"),
            };
        }
        if (!inheritedFields.Contains("outbound_proxy_hosts")
            && settings.OutboundProxyHosts is not null)
        {
            outboundProxy = outboundProxy with
            {
                HostPatterns = settings.OutboundProxyHosts,
            };
        }
        if (outboundProxy.Url == options.OutboundProxy.Url
            && outboundProxy.HostPatterns.SequenceEqual(
                options.OutboundProxy.HostPatterns,
                StringComparer.Ordinal))
        {
            outboundProxy = options.OutboundProxy;
        }
        var dataUpdate = options.DataUpdate with
        {
            Enabled = !inheritedFields.Contains("data_update_enabled")
                ? settings.DataUpdateEnabled ?? options.DataUpdate.Enabled
                : options.DataUpdate.Enabled,
            Cron = !inheritedFields.Contains("data_update_cron")
                ? settings.DataUpdateCron ?? options.DataUpdate.Cron
                : options.DataUpdate.Cron,
            ManifestUrl = !inheritedFields.Contains("data_update_manifest_url")
                && settings.DataUpdateManifestUrlOverridden == true
                ? ParseOptionalUri(settings.DataUpdateManifestUrl, "data update manifest URL")
                : options.DataUpdate.ManifestUrl,
            AutoDownload = !inheritedFields.Contains("data_update_auto_download")
                ? settings.DataUpdateAutoDownload ?? options.DataUpdate.AutoDownload
                : options.DataUpdate.AutoDownload,
            AutoImport = !inheritedFields.Contains("data_update_auto_import")
                ? settings.DataUpdateAutoImport ?? options.DataUpdate.AutoImport
                : options.DataUpdate.AutoImport,
            KeepVersions = !inheritedFields.Contains("data_update_keep_versions")
                ? settings.DataUpdateKeepVersions ?? options.DataUpdate.KeepVersions
                : options.DataUpdate.KeepVersions,
            HttpTimeout = !inheritedFields.Contains("data_update_http_timeout_seconds")
                && settings.DataUpdateHttpTimeoutSeconds is > 0
                ? TimeSpan.FromSeconds(settings.DataUpdateHttpTimeoutSeconds.Value)
                : options.DataUpdate.HttpTimeout,
        };

        return options with
        {
            OutboundProxy = outboundProxy,
            Metadata = options.Metadata with
            {
                Mikan = options.Metadata.Mikan with
                {
                    BaseUrl = mikanBaseUrl,
                    EpisodeIdentityCacheTtl = inheritedFields.Contains(
                        "mikan_episode_identity_cache_hours")
                        ? options.Metadata.Mikan.EpisodeIdentityCacheTtl
                        : settings.MikanEpisodeIdentityCacheHours is { } episodeCacheHours
                        ? TimeSpan.FromHours(episodeCacheHours)
                        : options.Metadata.Mikan.EpisodeIdentityCacheTtl,
                    BangumiIdentityCacheTtl = inheritedFields.Contains(
                        "mikan_bangumi_identity_cache_hours")
                        ? options.Metadata.Mikan.BangumiIdentityCacheTtl
                        : settings.MikanBangumiIdentityCacheHours is { } bangumiCacheHours
                        ? TimeSpan.FromHours(bangumiCacheHours)
                        : options.Metadata.Mikan.BangumiIdentityCacheTtl,
                },
                Tmdb = options.Metadata.Tmdb with
                {
                    BaseUrl = tmdbBaseUrl,
                    ImageBaseUrl = tmdbImageBaseUrl,
                    Language = inheritedFields.Contains("tmdb_language")
                        ? options.Metadata.Tmdb.Language
                        : settings.TmdbLanguage,
                    HttpTimeout = inheritedFields.Contains("tmdb_http_timeout_seconds")
                        ? options.Metadata.Tmdb.HttpTimeout
                        : TimeSpan.FromSeconds(settings.TmdbHttpTimeoutSeconds),
                    RetryCount = inheritedFields.Contains("tmdb_retry_count")
                        ? options.Metadata.Tmdb.RetryCount
                        : settings.TmdbRetryCount
                        ?? options.Metadata.Tmdb.RetryCount,
                    RetryDelay = inheritedFields.Contains("tmdb_retry_delay_seconds")
                        ? options.Metadata.Tmdb.RetryDelay
                        : settings.TmdbRetryDelaySeconds is { } tmdbRetryDelay
                        ? TimeSpan.FromSeconds(tmdbRetryDelay)
                        : options.Metadata.Tmdb.RetryDelay,
                    CacheTtl = inheritedFields.Contains("tmdb_cache_hours")
                        ? options.Metadata.Tmdb.CacheTtl
                        : settings.TmdbCacheHours is { } tmdbCacheHours
                        ? TimeSpan.FromHours(tmdbCacheHours)
                        : options.Metadata.Tmdb.CacheTtl,
                    ApiKey = !inheritedFields.Contains("tmdb_api_key")
                        && settings.TmdbApiKeyOverridden
                        ? settings.TmdbApiKey
                        : options.Metadata.Tmdb.ApiKey,
                    ReadAccessToken = !inheritedFields.Contains("tmdb_read_access_token")
                        && settings.TmdbReadAccessTokenOverridden
                        ? settings.TmdbReadAccessToken
                        : options.Metadata.Tmdb.ReadAccessToken,
                },
                Bangumi = options.Metadata.Bangumi with
                {
                    BaseUrl = bangumiBaseUrl,
                    HttpTimeout = !inheritedFields.Contains("bangumi_http_timeout_seconds")
                        && settings.BangumiHttpTimeoutSeconds is > 0
                        ? TimeSpan.FromSeconds(settings.BangumiHttpTimeoutSeconds.Value)
                        : options.Metadata.Bangumi.HttpTimeout,
                    RetryCount = inheritedFields.Contains("bangumi_retry_count")
                        ? options.Metadata.Bangumi.RetryCount
                        : settings.BangumiRetryCount
                        ?? options.Metadata.Bangumi.RetryCount,
                    RetryDelay = inheritedFields.Contains("bangumi_retry_delay_seconds")
                        ? options.Metadata.Bangumi.RetryDelay
                        : settings.BangumiRetryDelaySeconds is { } bangumiRetryDelay
                        ? TimeSpan.FromSeconds(bangumiRetryDelay)
                        : options.Metadata.Bangumi.RetryDelay,
                },
                SeasonFailure = new SeasonFailureOptions
                {
                    Skip = inheritedFields.Contains("season_failure_skip")
                        ? options.Metadata.SeasonFailure.Skip
                        : settings.SeasonFailureSkip,
                    Backtrace = inheritedFields.Contains("season_failure_backtrace")
                        ? options.Metadata.SeasonFailure.Backtrace
                        : settings.SeasonFailureBacktrace,
                    UseTitleSeason = inheritedFields.Contains(
                        "season_failure_use_title_season")
                        ? options.Metadata.SeasonFailure.UseTitleSeason
                        : settings.SeasonFailureUseTitleSeason,
                    UseFirstSeason = inheritedFields.Contains(
                        "season_failure_use_first_season")
                        ? options.Metadata.SeasonFailure.UseFirstSeason
                        : settings.SeasonFailureUseFirstSeason,
                },
                Ai = options.Metadata.Ai with
                {
                    BaseUrl = !inheritedFields.Contains("ai_base_url")
                        && settings.AiBaseUrlOverridden == true
                        ? ParseOptionalUri(settings.AiBaseUrl, "AI base URL")
                        : options.Metadata.Ai.BaseUrl,
                    ApiKey = !inheritedFields.Contains("ai_api_key")
                        && settings.AiApiKeyOverridden == true
                        ? settings.AiApiKey
                        : options.Metadata.Ai.ApiKey,
                    Model = !inheritedFields.Contains("ai_model")
                        && settings.AiModelOverridden == true
                        ? settings.AiModel
                        : options.Metadata.Ai.Model,
                    ReasoningEffort = !inheritedFields.Contains("ai_reasoning_effort")
                        && settings.AiReasoningEffortOverridden == true
                        ? settings.AiReasoningEffort
                        : options.Metadata.Ai.ReasoningEffort,
                    PromptTemplate = !inheritedFields.Contains("ai_prompt_template")
                        && settings.AiPromptTemplate is not null
                        ? settings.AiPromptTemplate
                        : options.Metadata.Ai.PromptTemplate,
                    TmdbMcpUrl = !inheritedFields.Contains("ai_tmdb_mcp_url")
                        && settings.AiTmdbMcpUrl is not null
                        ? ParseRequiredUri(settings.AiTmdbMcpUrl, "AI TMDB MCP URL")
                        : options.Metadata.Ai.TmdbMcpUrl,
                    BangumiMcpUrl = !inheritedFields.Contains("ai_bangumi_mcp_url")
                        && settings.AiBangumiMcpUrl is not null
                        ? ParseRequiredUri(settings.AiBangumiMcpUrl, "AI Bangumi MCP URL")
                        : options.Metadata.Ai.BangumiMcpUrl,
                    UseMetadataMatch = inheritedFields.Contains("ai_use_metadata_match")
                        ? options.Metadata.Ai.UseMetadataMatch
                        : settings.AiUseMetadataMatch
                        ?? (settings.AiUseSeasonMatch || settings.AiUseEpisodeMatch),
                    DebugMode = inheritedFields.Contains("ai_debug_mode")
                        ? options.Metadata.Ai.DebugMode
                        : settings.AiDebugMode ?? options.Metadata.Ai.DebugMode,
                    HttpTimeout = inheritedFields.Contains("ai_http_timeout_seconds")
                        ? options.Metadata.Ai.HttpTimeout
                        : TimeSpan.FromSeconds(settings.AiHttpTimeoutSeconds),
                },
                TmdbFailureUseBangumi = inheritedFields.Contains(
                    "tmdb_failure_use_bangumi")
                    ? options.Metadata.TmdbFailureUseBangumi
                    : settings.TmdbFailureUseBangumi,
                WriteBangumiIdWhenTmdbMatched = inheritedFields.Contains(
                    "write_bangumi_id_when_tmdb_matched")
                    ? options.Metadata.WriteBangumiIdWhenTmdbMatched
                    : settings.WriteBangumiIdWhenTmdbMatched
                    ?? options.Metadata.WriteBangumiIdWhenTmdbMatched,
                MikanTrustedOffsetCacheEnabled = inheritedFields.Contains(
                    "mikan_trusted_offset_cache_enabled")
                    ? options.Metadata.MikanTrustedOffsetCacheEnabled
                    : settings.MikanTrustedOffsetCacheEnabled,
                MikanTrustedOffsetRequiredEpisodes = inheritedFields.Contains(
                    "mikan_trusted_offset_required_episodes")
                    ? options.Metadata.MikanTrustedOffsetRequiredEpisodes
                    : TrustedOffsetRequiredEpisodes(
                        settings.MikanTrustedOffsetRequiredEpisodes,
                        options.Metadata.MikanTrustedOffsetRequiredEpisodes),
            },
            TorrentFetch = options.TorrentFetch with
            {
                Timeout = inheritedFields.Contains("torrent_http_timeout_seconds")
                    ? options.TorrentFetch.Timeout
                    : TimeSpan.FromSeconds(settings.TorrentHttpTimeoutSeconds),
                MaxResponseBytes = inheritedFields.Contains("torrent_max_response_bytes")
                    ? options.TorrentFetch.MaxResponseBytes
                    : settings.TorrentMaxResponseBytes,
                MaxRedirects = inheritedFields.Contains("torrent_max_redirects")
                    ? options.TorrentFetch.MaxRedirects
                    : settings.TorrentMaxRedirects,
                StagingTtl = inheritedFields.Contains("torrent_staging_ttl_seconds")
                    ? options.TorrentFetch.StagingTtl
                    : TimeSpan.FromSeconds(settings.TorrentStagingTtlSeconds),
            },
            DataUpdate = dataUpdate,
        };
    }

    private static Uri ParseRequiredUri(string value, string name) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException(
                $"Application private configuration has an invalid {name}.");

    private static Uri? ParseOptionalUri(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseRequiredUri(value, name);

    private static int TrustedOffsetRequiredEpisodes(int? value, int fallback)
    {
        var resolved = value ?? fallback;
        return resolved is >= 1 and <= 100
            ? resolved
            : throw new InvalidOperationException(
                "Application private configuration trusted offset threshold must be between 1 and 100.");
    }

    private async Task<ApplicationOverrideSnapshot> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new ApplicationOverrideSnapshot(CurrentFormatVersion, 0, null);
        }

        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var snapshot = await JsonSerializer.DeserializeAsync(
            stream,
            ApplicationOverrideJsonContext.Default.ApplicationOverrideSnapshot,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Application private configuration is empty.");
        if (snapshot.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported application private configuration format {snapshot.FormatVersion}.");
        }

        if (snapshot.Revision < 0)
        {
            throw new InvalidOperationException("Application private configuration revision is invalid.");
        }

        return snapshot;
    }

    private async Task SaveCoreAsync(
        ApplicationOverrideSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".application.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    ApplicationOverrideJsonContext.Default.ApplicationOverrideSnapshot,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private async Task BackupCoreAsync(
        long revision,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            throw new InvalidOperationException(
                "Application private configuration disappeared before backup.");
        }

        Directory.CreateDirectory(_backupsPath);
        var backup = Path.Combine(
            _backupsPath,
            $"application.private.revision-{revision:D20}.json");
        if (File.Exists(backup))
        {
            var sourceBytes = await File.ReadAllBytesAsync(_path, cancellationToken)
                .ConfigureAwait(false);
            var backupBytes = await File.ReadAllBytesAsync(backup, cancellationToken)
                .ConfigureAwait(false);
            if (!sourceBytes.AsSpan().SequenceEqual(backupBytes))
            {
                throw new InvalidOperationException(
                    $"Application private configuration backup revision {revision} conflicts with existing content.");
            }
            return;
        }

        var temporary = Path.Combine(
            _backupsPath,
            $".application.private.backup.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var source = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            File.Move(temporary, backup);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(ApplicationOverrideSnapshot))]
internal sealed partial class ApplicationOverrideJsonContext : JsonSerializerContext;
