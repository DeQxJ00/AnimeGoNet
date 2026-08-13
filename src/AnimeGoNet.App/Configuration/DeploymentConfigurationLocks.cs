using System.Collections;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Configuration;

public sealed record DeploymentConfigurationLock(
    string Field,
    IReadOnlyList<string> EnvironmentVariables,
    IReadOnlyList<string> CommandLineArguments)
{
    public string Source =>
        EnvironmentVariables.Count > 0 && CommandLineArguments.Count > 0
            ? "environment_and_command_line"
            : EnvironmentVariables.Count > 0
                ? "environment"
                : "command_line";

    public IReadOnlyList<string> ControllingKeys =>
        EnvironmentVariables
            .Concat(CommandLineArguments)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public sealed class DeploymentConfigurationLocks
{
    private static readonly LockDefinition[] Definitions =
    [
        new("mikan_base_url", ["mikan_base_url", "metadata:mikan:base_url"]),
        new("mikan_episode_identity_cache_hours", ["mikan_episode_identity_cache_hours", "metadata:mikan:episode_identity_cache_hours"]),
        new("mikan_bangumi_identity_cache_hours", ["mikan_bangumi_identity_cache_hours", "metadata:mikan:bangumi_identity_cache_hours"]),
        new("outbound_proxy_url", ["outbound_proxy_url", "ANIMEGO_OUTBOUND_PROXY_URL", "outbound_proxy:url"]),
        new("outbound_proxy_hosts", ["outbound_proxy_hosts", "ANIMEGO_OUTBOUND_PROXY_HOSTS", "outbound_proxy:hosts"]),
        new("tmdb_base_url", ["tmdb_base_url", "metadata:tmdb:base_url"]),
        new("tmdb_image_base_url", ["tmdb_image_base_url", "metadata:tmdb:image_base_url"]),
        new("tmdb_language", ["tmdb_language", "metadata:tmdb:language"]),
        new("tmdb_http_timeout_seconds", ["tmdb_timeout_second", "metadata:tmdb:timeout_seconds"]),
        new("tmdb_retry_count", ["tmdb_retry_count", "metadata:tmdb:retry_count"]),
        new("tmdb_retry_delay_seconds", ["tmdb_retry_wait_second", "metadata:tmdb:retry_wait_seconds"]),
        new("tmdb_cache_hours", ["tmdb_cache_hour", "advanced:cache:themoviedb_cache_hour", "metadata:tmdb:cache_hours"]),
        new("tmdb_api_key", ["tmdb_api_key", "ANIMEGO_THEMOVIEDB_KEY", "metadata:tmdb:api_key"]),
        new("tmdb_read_access_token", ["tmdb_read_access_token", "metadata:tmdb:read_access_token"]),
        new("bangumi_base_url", ["bangumi_base_url", "metadata:bangumi:base_url"]),
        new("bangumi_http_timeout_seconds", ["bangumi_timeout_second", "metadata:bangumi:timeout_seconds"]),
        new("bangumi_retry_count", ["bangumi_retry_count", "metadata:bangumi:retry_count"]),
        new("bangumi_retry_delay_seconds", ["bangumi_retry_wait_second", "metadata:bangumi:retry_wait_seconds"]),
        new("season_failure_skip", ["tmdb_fail_skip", "metadata:season_failure:skip"]),
        new("season_failure_backtrace", ["tmdb_fail_backtrace", "metadata:season_failure:backtrace"]),
        new("season_failure_use_title_season", ["tmdb_fail_use_title_season", "metadata:season_failure:use_title_season"]),
        new("season_failure_use_first_season", ["tmdb_fail_use_first_season", "metadata:season_failure:use_first_season"]),
        new(
            "ai_use_metadata_match",
            ["ai_use_metadata_match", "ai_use_season_match", "ai_use_episode_match", "metadata:ai:use_metadata_match"]),
        new("ai_base_url", ["ai_base_url", "metadata:ai:base_url"]),
        new("ai_api_key", ["ai_api_key", "metadata:ai:api_key"]),
        new("ai_model", ["ai_model", "metadata:ai:model"]),
        new("ai_prompt_template", ["ai_prompt_template", "metadata:ai:prompt_template"]),
        new("ai_tmdb_mcp_url", ["ai_tmdb_mcp_url", "metadata:ai:tmdb_mcp_url"]),
        new("ai_bangumi_mcp_url", ["ai_bangumi_mcp_url", "metadata:ai:bangumi_mcp_url"]),
        new("ai_http_timeout_seconds", ["ai_timeout_second", "metadata:ai:timeout_seconds"]),
        new("tmdb_failure_use_bangumi", ["tmdb_fail_use_bangumi", "metadata:tmdb_failure_use_bangumi"]),
        new(
            "write_bangumi_id_when_tmdb_matched",
            ["write_bangumi_id_when_tmdb_matched", "metadata:write_bangumi_id_when_tmdb_matched"]),
        new("mikan_trusted_offset_cache_enabled", ["mikan_trusted_offset_cache_enabled", "metadata:mikan_trusted_offset_cache_enabled"]),
        new("torrent_http_timeout_seconds", ["torrent_http_timeout_seconds", "torrent_fetch:timeout_seconds"]),
        new("torrent_max_response_bytes", ["torrent_max_response_bytes", "torrent_fetch:max_response_bytes"]),
        new("torrent_max_redirects", ["torrent_max_redirects", "torrent_fetch:max_redirects"]),
        new("torrent_staging_ttl_seconds", ["torrent_staging_ttl_seconds", "torrent_fetch:staging_ttl_seconds"]),
        new("data_update_enabled", ["data_update_enabled", "data_update:enabled"]),
        new("data_update_cron", ["data_update_cron", "data_update:cron"]),
        new("data_update_manifest_url", ["data_update_manifest_url", "data_update:manifest_url"]),
        new("data_update_auto_download", ["data_update_auto_download", "data_update:auto_download"]),
        new("data_update_auto_import", ["data_update_auto_import", "data_update:auto_import"]),
        new("data_update_keep_versions", ["data_update_keep_versions", "data_update:keep_versions"]),
        new("data_update_http_timeout_seconds", ["data_update_timeout_second", "data_update:timeout_seconds"]),
    ];

    private readonly HashSet<string> _fields;

    private DeploymentConfigurationLocks(IReadOnlyList<DeploymentConfigurationLock> items)
    {
        Items = items;
        _fields = items.Select(item => item.Field).ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlyList<DeploymentConfigurationLock> Items { get; }

    public static DeploymentConfigurationLocks Empty { get; } = new([]);

    public static DeploymentConfigurationLocks FromCurrentProcess() =>
        FromCurrentProcess([]);

    public static DeploymentConfigurationLocks FromCurrentProcess(
        IReadOnlyCollection<string> commandLineArguments)
    {
        var names = new List<string>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name)
            {
                names.Add(name);
            }
        }

        return FromSources(names, commandLineArguments);
    }

    public static DeploymentConfigurationLocks FromVariableNames(
        IEnumerable<string> variableNames) =>
        FromSources(variableNames, []);

    public static DeploymentConfigurationLocks FromSources(
        IEnumerable<string> variableNames,
        IEnumerable<string> commandLineArguments)
    {
        ArgumentNullException.ThrowIfNull(variableNames);
        ArgumentNullException.ThrowIfNull(commandLineArguments);
        var values = Definitions.ToDictionary(
            definition => definition.Field,
            _ => new MutableLock(),
            StringComparer.Ordinal);

        foreach (var rawName in variableNames)
        {
            Add(rawName, rawName?.Trim(), isEnvironment: true);
        }

        foreach (var rawArgument in commandLineArguments)
        {
            if (string.IsNullOrWhiteSpace(rawArgument)
                || !rawArgument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = rawArgument.IndexOf('=');
            var rawKey = separator >= 0
                ? rawArgument[2..separator]
                : rawArgument[2..];
            Add(rawKey, $"--{rawKey}", isEnvironment: false);
        }

        var locks = Definitions
            .Select(definition => (Definition: definition, Value: values[definition.Field]))
            .Where(pair => pair.Value.EnvironmentVariables.Count > 0
                || pair.Value.CommandLineArguments.Count > 0)
            .Select(pair => new DeploymentConfigurationLock(
                pair.Definition.Field,
                pair.Value.EnvironmentVariables.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                pair.Value.CommandLineArguments.Order(StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();
        return locks.Length == 0 ? Empty : new DeploymentConfigurationLocks(locks);

        void Add(string? rawKey, string? controllingKey, bool isEnvironment)
        {
            if (string.IsNullOrWhiteSpace(rawKey)
                || string.IsNullOrWhiteSpace(controllingKey))
            {
                return;
            }

            var normalized = rawKey.Trim().Replace("__", ":", StringComparison.Ordinal);
            foreach (var definition in Definitions.Where(definition =>
                definition.ConfigurationKeys.Contains(normalized, StringComparer.OrdinalIgnoreCase)))
            {
                var target = values[definition.Field];
                if (isEnvironment)
                {
                    target.EnvironmentVariables.Add(controllingKey);
                }
                else
                {
                    target.CommandLineArguments.Add(controllingKey);
                }
            }
        }
    }

    public bool IsLocked(string field) => _fields.Contains(field);

    public ApplicationOverrideEntry PreserveLockedOverrides(
        ApplicationOverrideEntry? current,
        ApplicationOverrideEntry candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var inherited = candidate.InheritedFields?
            .ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (var item in Items)
        {
            if (current is null
                || current.InheritedFields?.Contains(
                    item.Field,
                    StringComparer.Ordinal) == true)
            {
                inherited.Add(item.Field);
            }
        }

        if (current is not null)
        {
            candidate = candidate with
            {
                MikanBaseUrl = Preserve(
                    "mikan_base_url",
                    current.MikanBaseUrl,
                    candidate.MikanBaseUrl),
                MikanEpisodeIdentityCacheHours = Preserve(
                    "mikan_episode_identity_cache_hours",
                    current.MikanEpisodeIdentityCacheHours,
                    candidate.MikanEpisodeIdentityCacheHours),
                MikanBangumiIdentityCacheHours = Preserve(
                    "mikan_bangumi_identity_cache_hours",
                    current.MikanBangumiIdentityCacheHours,
                    candidate.MikanBangumiIdentityCacheHours),
                OutboundProxyUrlOverridden = Preserve(
                    "outbound_proxy_url",
                    current.OutboundProxyUrlOverridden,
                    candidate.OutboundProxyUrlOverridden),
                OutboundProxyUrl = Preserve(
                    "outbound_proxy_url",
                    current.OutboundProxyUrl,
                    candidate.OutboundProxyUrl),
                OutboundProxyHosts = Preserve(
                    "outbound_proxy_hosts",
                    current.OutboundProxyHosts,
                    candidate.OutboundProxyHosts),
                TmdbBaseUrl = Preserve(
                    "tmdb_base_url",
                    current.TmdbBaseUrl,
                    candidate.TmdbBaseUrl),
                TmdbImageBaseUrl = Preserve(
                    "tmdb_image_base_url",
                    current.TmdbImageBaseUrl,
                    candidate.TmdbImageBaseUrl),
                TmdbLanguage = Preserve(
                    "tmdb_language",
                    current.TmdbLanguage,
                    candidate.TmdbLanguage),
                TmdbHttpTimeoutSeconds = Preserve(
                    "tmdb_http_timeout_seconds",
                    current.TmdbHttpTimeoutSeconds,
                    candidate.TmdbHttpTimeoutSeconds),
                TmdbRetryCount = Preserve(
                    "tmdb_retry_count",
                    current.TmdbRetryCount,
                    candidate.TmdbRetryCount),
                TmdbRetryDelaySeconds = Preserve(
                    "tmdb_retry_delay_seconds",
                    current.TmdbRetryDelaySeconds,
                    candidate.TmdbRetryDelaySeconds),
                TmdbCacheHours = Preserve(
                    "tmdb_cache_hours",
                    current.TmdbCacheHours,
                    candidate.TmdbCacheHours),
                TmdbApiKeyOverridden = Preserve(
                    "tmdb_api_key",
                    current.TmdbApiKeyOverridden,
                    candidate.TmdbApiKeyOverridden),
                TmdbApiKey = Preserve(
                    "tmdb_api_key",
                    current.TmdbApiKey,
                    candidate.TmdbApiKey),
                TmdbReadAccessTokenOverridden = Preserve(
                    "tmdb_read_access_token",
                    current.TmdbReadAccessTokenOverridden,
                    candidate.TmdbReadAccessTokenOverridden),
                TmdbReadAccessToken = Preserve(
                    "tmdb_read_access_token",
                    current.TmdbReadAccessToken,
                    candidate.TmdbReadAccessToken),
                BangumiBaseUrl = Preserve(
                    "bangumi_base_url",
                    current.BangumiBaseUrl,
                    candidate.BangumiBaseUrl),
                BangumiHttpTimeoutSeconds = Preserve(
                    "bangumi_http_timeout_seconds",
                    current.BangumiHttpTimeoutSeconds,
                    candidate.BangumiHttpTimeoutSeconds),
                BangumiRetryCount = Preserve(
                    "bangumi_retry_count",
                    current.BangumiRetryCount,
                    candidate.BangumiRetryCount),
                BangumiRetryDelaySeconds = Preserve(
                    "bangumi_retry_delay_seconds",
                    current.BangumiRetryDelaySeconds,
                    candidate.BangumiRetryDelaySeconds),
                SeasonFailureSkip = Preserve(
                    "season_failure_skip",
                    current.SeasonFailureSkip,
                    candidate.SeasonFailureSkip),
                SeasonFailureBacktrace = Preserve(
                    "season_failure_backtrace",
                    current.SeasonFailureBacktrace,
                    candidate.SeasonFailureBacktrace),
                SeasonFailureUseTitleSeason = Preserve(
                    "season_failure_use_title_season",
                    current.SeasonFailureUseTitleSeason,
                    candidate.SeasonFailureUseTitleSeason),
                SeasonFailureUseFirstSeason = Preserve(
                    "season_failure_use_first_season",
                    current.SeasonFailureUseFirstSeason,
                    candidate.SeasonFailureUseFirstSeason),
                AiUseSeasonMatch = Preserve(
                    "ai_use_metadata_match",
                    current.AiUseSeasonMatch,
                    candidate.AiUseSeasonMatch),
                AiUseEpisodeMatch = Preserve(
                    "ai_use_metadata_match",
                    current.AiUseEpisodeMatch,
                    candidate.AiUseEpisodeMatch),
                AiUseMetadataMatch = Preserve(
                    "ai_use_metadata_match",
                    current.AiUseMetadataMatch,
                    candidate.AiUseMetadataMatch),
                AiBaseUrlOverridden = Preserve(
                    "ai_base_url",
                    current.AiBaseUrlOverridden,
                    candidate.AiBaseUrlOverridden),
                AiBaseUrl = Preserve("ai_base_url", current.AiBaseUrl, candidate.AiBaseUrl),
                AiApiKeyOverridden = Preserve(
                    "ai_api_key",
                    current.AiApiKeyOverridden,
                    candidate.AiApiKeyOverridden),
                AiApiKey = Preserve("ai_api_key", current.AiApiKey, candidate.AiApiKey),
                AiModelOverridden = Preserve(
                    "ai_model",
                    current.AiModelOverridden,
                    candidate.AiModelOverridden),
                AiModel = Preserve("ai_model", current.AiModel, candidate.AiModel),
                AiPromptTemplate = Preserve(
                    "ai_prompt_template",
                    current.AiPromptTemplate,
                    candidate.AiPromptTemplate),
                AiTmdbMcpUrl = Preserve(
                    "ai_tmdb_mcp_url",
                    current.AiTmdbMcpUrl,
                    candidate.AiTmdbMcpUrl),
                AiBangumiMcpUrl = Preserve(
                    "ai_bangumi_mcp_url",
                    current.AiBangumiMcpUrl,
                    candidate.AiBangumiMcpUrl),
                AiHttpTimeoutSeconds = Preserve(
                    "ai_http_timeout_seconds",
                    current.AiHttpTimeoutSeconds,
                    candidate.AiHttpTimeoutSeconds),
                TmdbFailureUseBangumi = Preserve(
                    "tmdb_failure_use_bangumi",
                    current.TmdbFailureUseBangumi,
                    candidate.TmdbFailureUseBangumi),
                WriteBangumiIdWhenTmdbMatched = Preserve(
                    "write_bangumi_id_when_tmdb_matched",
                    current.WriteBangumiIdWhenTmdbMatched,
                    candidate.WriteBangumiIdWhenTmdbMatched),
                MikanTrustedOffsetCacheEnabled = Preserve(
                    "mikan_trusted_offset_cache_enabled",
                    current.MikanTrustedOffsetCacheEnabled,
                    candidate.MikanTrustedOffsetCacheEnabled),
                TorrentHttpTimeoutSeconds = Preserve(
                    "torrent_http_timeout_seconds",
                    current.TorrentHttpTimeoutSeconds,
                    candidate.TorrentHttpTimeoutSeconds),
                TorrentMaxResponseBytes = Preserve(
                    "torrent_max_response_bytes",
                    current.TorrentMaxResponseBytes,
                    candidate.TorrentMaxResponseBytes),
                TorrentMaxRedirects = Preserve(
                    "torrent_max_redirects",
                    current.TorrentMaxRedirects,
                    candidate.TorrentMaxRedirects),
                TorrentStagingTtlSeconds = Preserve(
                    "torrent_staging_ttl_seconds",
                    current.TorrentStagingTtlSeconds,
                    candidate.TorrentStagingTtlSeconds),
                DataUpdateEnabled = Preserve(
                    "data_update_enabled",
                    current.DataUpdateEnabled,
                    candidate.DataUpdateEnabled),
                DataUpdateCron = Preserve(
                    "data_update_cron",
                    current.DataUpdateCron,
                    candidate.DataUpdateCron),
                DataUpdateManifestUrlOverridden = Preserve(
                    "data_update_manifest_url",
                    current.DataUpdateManifestUrlOverridden,
                    candidate.DataUpdateManifestUrlOverridden),
                DataUpdateManifestUrl = Preserve(
                    "data_update_manifest_url",
                    current.DataUpdateManifestUrl,
                    candidate.DataUpdateManifestUrl),
                DataUpdateAutoDownload = Preserve(
                    "data_update_auto_download",
                    current.DataUpdateAutoDownload,
                    candidate.DataUpdateAutoDownload),
                DataUpdateAutoImport = Preserve(
                    "data_update_auto_import",
                    current.DataUpdateAutoImport,
                    candidate.DataUpdateAutoImport),
                DataUpdateKeepVersions = Preserve(
                    "data_update_keep_versions",
                    current.DataUpdateKeepVersions,
                    candidate.DataUpdateKeepVersions),
                DataUpdateHttpTimeoutSeconds = Preserve(
                    "data_update_http_timeout_seconds",
                    current.DataUpdateHttpTimeoutSeconds,
                    candidate.DataUpdateHttpTimeoutSeconds),
            };
        }

        return candidate with
        {
            InheritedFields = inherited.Count == 0
                ? null
                : inherited.Order(StringComparer.Ordinal).ToArray(),
        };

        T Preserve<T>(string field, T existing, T requested) =>
            IsLocked(field)
            && !inherited.Contains(field)
                ? existing
                : requested;
    }

    public AnimeGoOptions Reapply(
        AnimeGoOptions deployment,
        AnimeGoOptions candidate)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(candidate);

        var outboundProxy = candidate.OutboundProxy;
        if (IsLocked("outbound_proxy_url"))
        {
            outboundProxy = outboundProxy with { Url = deployment.OutboundProxy.Url };
        }
        if (IsLocked("outbound_proxy_hosts"))
        {
            outboundProxy = outboundProxy with
            {
                HostPatterns = deployment.OutboundProxy.HostPatterns,
            };
        }

        var mikan = candidate.Metadata.Mikan;
        if (IsLocked("mikan_base_url"))
        {
            mikan = mikan with { BaseUrl = deployment.Metadata.Mikan.BaseUrl };
        }
        if (IsLocked("mikan_episode_identity_cache_hours"))
        {
            mikan = mikan with
            {
                EpisodeIdentityCacheTtl = deployment.Metadata.Mikan.EpisodeIdentityCacheTtl,
            };
        }
        if (IsLocked("mikan_bangumi_identity_cache_hours"))
        {
            mikan = mikan with
            {
                BangumiIdentityCacheTtl = deployment.Metadata.Mikan.BangumiIdentityCacheTtl,
            };
        }

        var tmdb = candidate.Metadata.Tmdb;
        if (IsLocked("tmdb_base_url"))
        {
            tmdb = tmdb with { BaseUrl = deployment.Metadata.Tmdb.BaseUrl };
        }
        if (IsLocked("tmdb_image_base_url"))
        {
            tmdb = tmdb with { ImageBaseUrl = deployment.Metadata.Tmdb.ImageBaseUrl };
        }
        if (IsLocked("tmdb_language"))
        {
            tmdb = tmdb with { Language = deployment.Metadata.Tmdb.Language };
        }
        if (IsLocked("tmdb_http_timeout_seconds"))
        {
            tmdb = tmdb with { HttpTimeout = deployment.Metadata.Tmdb.HttpTimeout };
        }
        if (IsLocked("tmdb_retry_count"))
        {
            tmdb = tmdb with { RetryCount = deployment.Metadata.Tmdb.RetryCount };
        }
        if (IsLocked("tmdb_retry_delay_seconds"))
        {
            tmdb = tmdb with { RetryDelay = deployment.Metadata.Tmdb.RetryDelay };
        }
        if (IsLocked("tmdb_cache_hours"))
        {
            tmdb = tmdb with { CacheTtl = deployment.Metadata.Tmdb.CacheTtl };
        }
        if (IsLocked("tmdb_api_key"))
        {
            tmdb = tmdb with { ApiKey = deployment.Metadata.Tmdb.ApiKey };
        }
        if (IsLocked("tmdb_read_access_token"))
        {
            tmdb = tmdb with
            {
                ReadAccessToken = deployment.Metadata.Tmdb.ReadAccessToken,
            };
        }

        var bangumi = candidate.Metadata.Bangumi;
        if (IsLocked("bangumi_base_url"))
        {
            bangumi = bangumi with { BaseUrl = deployment.Metadata.Bangumi.BaseUrl };
        }
        if (IsLocked("bangumi_http_timeout_seconds"))
        {
            bangumi = bangumi with
            {
                HttpTimeout = deployment.Metadata.Bangumi.HttpTimeout,
            };
        }
        if (IsLocked("bangumi_retry_count"))
        {
            bangumi = bangumi with
            {
                RetryCount = deployment.Metadata.Bangumi.RetryCount,
            };
        }
        if (IsLocked("bangumi_retry_delay_seconds"))
        {
            bangumi = bangumi with
            {
                RetryDelay = deployment.Metadata.Bangumi.RetryDelay,
            };
        }

        var seasonFailure = candidate.Metadata.SeasonFailure;
        if (IsLocked("season_failure_skip"))
        {
            seasonFailure = seasonFailure with
            {
                Skip = deployment.Metadata.SeasonFailure.Skip,
            };
        }
        if (IsLocked("season_failure_backtrace"))
        {
            seasonFailure = seasonFailure with
            {
                Backtrace = deployment.Metadata.SeasonFailure.Backtrace,
            };
        }
        if (IsLocked("season_failure_use_title_season"))
        {
            seasonFailure = seasonFailure with
            {
                UseTitleSeason = deployment.Metadata.SeasonFailure.UseTitleSeason,
            };
        }
        if (IsLocked("season_failure_use_first_season"))
        {
            seasonFailure = seasonFailure with
            {
                UseFirstSeason = deployment.Metadata.SeasonFailure.UseFirstSeason,
            };
        }

        var ai = candidate.Metadata.Ai;
        if (IsLocked("ai_base_url"))
        {
            ai = ai with { BaseUrl = deployment.Metadata.Ai.BaseUrl };
        }
        if (IsLocked("ai_api_key"))
        {
            ai = ai with { ApiKey = deployment.Metadata.Ai.ApiKey };
        }
        if (IsLocked("ai_model"))
        {
            ai = ai with { Model = deployment.Metadata.Ai.Model };
        }
        if (IsLocked("ai_prompt_template"))
        {
            ai = ai with { PromptTemplate = deployment.Metadata.Ai.PromptTemplate };
        }
        if (IsLocked("ai_tmdb_mcp_url"))
        {
            ai = ai with { TmdbMcpUrl = deployment.Metadata.Ai.TmdbMcpUrl };
        }
        if (IsLocked("ai_bangumi_mcp_url"))
        {
            ai = ai with { BangumiMcpUrl = deployment.Metadata.Ai.BangumiMcpUrl };
        }
        if (IsLocked("ai_use_metadata_match"))
        {
            ai = ai with
            {
                UseMetadataMatch = deployment.Metadata.Ai.UseMetadataMatch,
            };
        }
        if (IsLocked("ai_http_timeout_seconds"))
        {
            ai = ai with { HttpTimeout = deployment.Metadata.Ai.HttpTimeout };
        }

        var tmdbFailureUseBangumi = IsLocked("tmdb_failure_use_bangumi")
            ? deployment.Metadata.TmdbFailureUseBangumi
            : candidate.Metadata.TmdbFailureUseBangumi;
        var writeBangumiIdWhenTmdbMatched = IsLocked(
            "write_bangumi_id_when_tmdb_matched")
            ? deployment.Metadata.WriteBangumiIdWhenTmdbMatched
            : candidate.Metadata.WriteBangumiIdWhenTmdbMatched;
        var mikanTrustedOffsetCacheEnabled = IsLocked(
            "mikan_trusted_offset_cache_enabled")
            ? deployment.Metadata.MikanTrustedOffsetCacheEnabled
            : candidate.Metadata.MikanTrustedOffsetCacheEnabled;

        var torrentFetch = candidate.TorrentFetch;
        if (IsLocked("torrent_http_timeout_seconds"))
        {
            torrentFetch = torrentFetch with
            {
                Timeout = deployment.TorrentFetch.Timeout,
            };
        }
        if (IsLocked("torrent_max_response_bytes"))
        {
            torrentFetch = torrentFetch with
            {
                MaxResponseBytes = deployment.TorrentFetch.MaxResponseBytes,
            };
        }
        if (IsLocked("torrent_max_redirects"))
        {
            torrentFetch = torrentFetch with
            {
                MaxRedirects = deployment.TorrentFetch.MaxRedirects,
            };
        }
        if (IsLocked("torrent_staging_ttl_seconds"))
        {
            torrentFetch = torrentFetch with
            {
                StagingTtl = deployment.TorrentFetch.StagingTtl,
            };
        }

        var dataUpdate = candidate.DataUpdate;
        if (IsLocked("data_update_enabled"))
        {
            dataUpdate = dataUpdate with { Enabled = deployment.DataUpdate.Enabled };
        }
        if (IsLocked("data_update_cron"))
        {
            dataUpdate = dataUpdate with { Cron = deployment.DataUpdate.Cron };
        }
        if (IsLocked("data_update_manifest_url"))
        {
            dataUpdate = dataUpdate with { ManifestUrl = deployment.DataUpdate.ManifestUrl };
        }
        if (IsLocked("data_update_auto_download"))
        {
            dataUpdate = dataUpdate with { AutoDownload = deployment.DataUpdate.AutoDownload };
        }
        if (IsLocked("data_update_auto_import"))
        {
            dataUpdate = dataUpdate with { AutoImport = deployment.DataUpdate.AutoImport };
        }
        if (IsLocked("data_update_keep_versions"))
        {
            dataUpdate = dataUpdate with { KeepVersions = deployment.DataUpdate.KeepVersions };
        }
        if (IsLocked("data_update_http_timeout_seconds"))
        {
            dataUpdate = dataUpdate with
            {
                HttpTimeout = deployment.DataUpdate.HttpTimeout,
            };
        }

        return candidate with
        {
            OutboundProxy = outboundProxy,
            Metadata = candidate.Metadata with
            {
                Mikan = mikan,
                Tmdb = tmdb,
                Bangumi = bangumi,
                SeasonFailure = seasonFailure,
                Ai = ai,
                TmdbFailureUseBangumi = tmdbFailureUseBangumi,
                WriteBangumiIdWhenTmdbMatched = writeBangumiIdWhenTmdbMatched,
                MikanTrustedOffsetCacheEnabled = mikanTrustedOffsetCacheEnabled,
            },
            TorrentFetch = torrentFetch,
            DataUpdate = dataUpdate,
        };
    }

    public IReadOnlyList<string> FindChangedLockedFields(
        AnimeGoOptions deployment,
        AnimeGoOptions candidate)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(candidate);
        var changed = new List<string>();
        AddIfChanged(
            "outbound_proxy_url",
            deployment.OutboundProxy.Url,
            candidate.OutboundProxy.Url);
        AddIfChanged(
            "outbound_proxy_hosts",
            string.Join("\n", deployment.OutboundProxy.HostPatterns),
            string.Join("\n", candidate.OutboundProxy.HostPatterns));
        AddIfChanged(
            "mikan_base_url",
            deployment.Metadata.Mikan.BaseUrl,
            candidate.Metadata.Mikan.BaseUrl);
        AddIfChanged(
            "mikan_episode_identity_cache_hours",
            deployment.Metadata.Mikan.EpisodeIdentityCacheTtl,
            candidate.Metadata.Mikan.EpisodeIdentityCacheTtl);
        AddIfChanged(
            "mikan_bangumi_identity_cache_hours",
            deployment.Metadata.Mikan.BangumiIdentityCacheTtl,
            candidate.Metadata.Mikan.BangumiIdentityCacheTtl);
        AddIfChanged(
            "tmdb_base_url",
            deployment.Metadata.Tmdb.BaseUrl,
            candidate.Metadata.Tmdb.BaseUrl);
        AddIfChanged(
            "tmdb_image_base_url",
            deployment.Metadata.Tmdb.ImageBaseUrl,
            candidate.Metadata.Tmdb.ImageBaseUrl);
        AddIfChanged(
            "tmdb_language",
            deployment.Metadata.Tmdb.Language,
            candidate.Metadata.Tmdb.Language);
        AddIfChanged(
            "tmdb_http_timeout_seconds",
            deployment.Metadata.Tmdb.HttpTimeout,
            candidate.Metadata.Tmdb.HttpTimeout);
        AddIfChanged(
            "tmdb_retry_count",
            deployment.Metadata.Tmdb.RetryCount,
            candidate.Metadata.Tmdb.RetryCount);
        AddIfChanged(
            "tmdb_retry_delay_seconds",
            deployment.Metadata.Tmdb.RetryDelay,
            candidate.Metadata.Tmdb.RetryDelay);
        AddIfChanged(
            "tmdb_cache_hours",
            deployment.Metadata.Tmdb.CacheTtl,
            candidate.Metadata.Tmdb.CacheTtl);
        AddIfChanged(
            "bangumi_base_url",
            deployment.Metadata.Bangumi.BaseUrl,
            candidate.Metadata.Bangumi.BaseUrl);
        AddIfChanged(
            "bangumi_http_timeout_seconds",
            deployment.Metadata.Bangumi.HttpTimeout,
            candidate.Metadata.Bangumi.HttpTimeout);
        AddIfChanged(
            "bangumi_retry_count",
            deployment.Metadata.Bangumi.RetryCount,
            candidate.Metadata.Bangumi.RetryCount);
        AddIfChanged(
            "bangumi_retry_delay_seconds",
            deployment.Metadata.Bangumi.RetryDelay,
            candidate.Metadata.Bangumi.RetryDelay);
        AddIfChanged(
            "season_failure_skip",
            deployment.Metadata.SeasonFailure.Skip,
            candidate.Metadata.SeasonFailure.Skip);
        AddIfChanged(
            "season_failure_backtrace",
            deployment.Metadata.SeasonFailure.Backtrace,
            candidate.Metadata.SeasonFailure.Backtrace);
        AddIfChanged(
            "season_failure_use_title_season",
            deployment.Metadata.SeasonFailure.UseTitleSeason,
            candidate.Metadata.SeasonFailure.UseTitleSeason);
        AddIfChanged(
            "season_failure_use_first_season",
            deployment.Metadata.SeasonFailure.UseFirstSeason,
            candidate.Metadata.SeasonFailure.UseFirstSeason);
        AddIfChanged(
            "ai_base_url",
            deployment.Metadata.Ai.BaseUrl,
            candidate.Metadata.Ai.BaseUrl);
        AddIfChanged(
            "ai_api_key",
            deployment.Metadata.Ai.ApiKey,
            candidate.Metadata.Ai.ApiKey);
        AddIfChanged(
            "ai_model",
            deployment.Metadata.Ai.Model,
            candidate.Metadata.Ai.Model);
        AddIfChanged(
            "ai_prompt_template",
            deployment.Metadata.Ai.PromptTemplate,
            candidate.Metadata.Ai.PromptTemplate);
        AddIfChanged(
            "ai_tmdb_mcp_url",
            deployment.Metadata.Ai.TmdbMcpUrl,
            candidate.Metadata.Ai.TmdbMcpUrl);
        AddIfChanged(
            "ai_bangumi_mcp_url",
            deployment.Metadata.Ai.BangumiMcpUrl,
            candidate.Metadata.Ai.BangumiMcpUrl);
        AddIfChanged(
            "ai_use_metadata_match",
            deployment.Metadata.Ai.UseMetadataMatch,
            candidate.Metadata.Ai.UseMetadataMatch);
        AddIfChanged(
            "ai_http_timeout_seconds",
            deployment.Metadata.Ai.HttpTimeout,
            candidate.Metadata.Ai.HttpTimeout);
        AddIfChanged(
            "tmdb_failure_use_bangumi",
            deployment.Metadata.TmdbFailureUseBangumi,
            candidate.Metadata.TmdbFailureUseBangumi);
        AddIfChanged(
            "write_bangumi_id_when_tmdb_matched",
            deployment.Metadata.WriteBangumiIdWhenTmdbMatched,
            candidate.Metadata.WriteBangumiIdWhenTmdbMatched);
        AddIfChanged(
            "mikan_trusted_offset_cache_enabled",
            deployment.Metadata.MikanTrustedOffsetCacheEnabled,
            candidate.Metadata.MikanTrustedOffsetCacheEnabled);
        AddIfChanged(
            "torrent_http_timeout_seconds",
            deployment.TorrentFetch.Timeout,
            candidate.TorrentFetch.Timeout);
        AddIfChanged(
            "torrent_max_response_bytes",
            deployment.TorrentFetch.MaxResponseBytes,
            candidate.TorrentFetch.MaxResponseBytes);
        AddIfChanged(
            "torrent_max_redirects",
            deployment.TorrentFetch.MaxRedirects,
            candidate.TorrentFetch.MaxRedirects);
        AddIfChanged(
            "torrent_staging_ttl_seconds",
            deployment.TorrentFetch.StagingTtl,
            candidate.TorrentFetch.StagingTtl);
        AddIfChanged(
            "data_update_enabled",
            deployment.DataUpdate.Enabled,
            candidate.DataUpdate.Enabled);
        AddIfChanged(
            "data_update_cron",
            deployment.DataUpdate.Cron,
            candidate.DataUpdate.Cron);
        AddIfChanged(
            "data_update_manifest_url",
            deployment.DataUpdate.ManifestUrl,
            candidate.DataUpdate.ManifestUrl);
        AddIfChanged(
            "data_update_auto_download",
            deployment.DataUpdate.AutoDownload,
            candidate.DataUpdate.AutoDownload);
        AddIfChanged(
            "data_update_auto_import",
            deployment.DataUpdate.AutoImport,
            candidate.DataUpdate.AutoImport);
        AddIfChanged(
            "data_update_keep_versions",
            deployment.DataUpdate.KeepVersions,
            candidate.DataUpdate.KeepVersions);
        AddIfChanged(
            "data_update_http_timeout_seconds",
            deployment.DataUpdate.HttpTimeout,
            candidate.DataUpdate.HttpTimeout);
        return changed;

        void AddIfChanged<T>(string field, T deployed, T requested)
        {
            if (IsLocked(field)
                && !EqualityComparer<T>.Default.Equals(deployed, requested))
            {
                changed.Add(field);
            }
        }
    }

    private sealed record LockDefinition(
        string Field,
        IReadOnlyList<string> ConfigurationKeys);

    private sealed class MutableLock
    {
        public HashSet<string> EnvironmentVariables { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> CommandLineArguments { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class ConfigurationFieldLockedException(
    IReadOnlyList<string> fields) : InvalidOperationException(
        $"Configuration field(s) are controlled by environment variables: {string.Join(", ", fields)}.")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}
