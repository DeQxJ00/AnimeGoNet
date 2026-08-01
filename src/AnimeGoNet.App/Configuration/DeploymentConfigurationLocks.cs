using System.Collections;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Configuration;

public sealed record DeploymentConfigurationLock(
    string Field,
    IReadOnlyList<string> EnvironmentVariables);

public sealed class DeploymentConfigurationLocks
{
    private static readonly LockDefinition[] Definitions =
    [
        new("tmdb_base_url", ["tmdb_base_url"]),
        new("tmdb_proxy_url", ["tmdb_proxy_url", "ANIMEGO_PROXY_URL"]),
        new("tmdb_language", ["tmdb_language"]),
        new("tmdb_http_timeout_seconds", ["tmdb_timeout_second"]),
        new("tmdb_retry_count", ["tmdb_retry_count"]),
        new("tmdb_retry_delay_seconds", ["tmdb_retry_wait_second"]),
        new("tmdb_api_key", ["tmdb_api_key", "ANIMEGO_THEMOVIEDB_KEY"]),
        new("tmdb_read_access_token", ["tmdb_read_access_token"]),
        new("bangumi_base_url", ["bangumi_base_url"]),
        new("bangumi_proxy_url", ["bangumi_proxy_url", "ANIMEGO_PROXY_URL"]),
        new("bangumi_http_timeout_seconds", ["bangumi_timeout_second"]),
        new("bangumi_retry_count", ["bangumi_retry_count"]),
        new(
            "bangumi_retry_delay_seconds",
            ["bangumi_retry_wait_second"]),
        new(
            "ai_use_metadata_match",
            ["ai_use_metadata_match", "ai_use_season_match", "ai_use_episode_match"]),
        new("ai_http_timeout_seconds", ["ai_timeout_second"]),
        new("data_update_enabled", ["data_update_enabled"]),
        new("data_update_cron", ["data_update_cron"]),
        new("data_update_manifest_url", ["data_update_manifest_url"]),
        new("data_update_auto_download", ["data_update_auto_download"]),
        new("data_update_auto_import", ["data_update_auto_import"]),
        new("data_update_keep_versions", ["data_update_keep_versions"]),
        new("data_update_http_timeout_seconds", ["data_update_timeout_second"]),
    ];

    private readonly HashSet<string> _fields;

    private DeploymentConfigurationLocks(IReadOnlyList<DeploymentConfigurationLock> items)
    {
        Items = items;
        _fields = items.Select(item => item.Field).ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlyList<DeploymentConfigurationLock> Items { get; }

    public static DeploymentConfigurationLocks Empty { get; } = new([]);

    public static DeploymentConfigurationLocks FromCurrentProcess()
    {
        var names = new List<string>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name)
            {
                names.Add(name);
            }
        }

        return FromVariableNames(names);
    }

    public static DeploymentConfigurationLocks FromVariableNames(
        IEnumerable<string> variableNames)
    {
        ArgumentNullException.ThrowIfNull(variableNames);
        var present = variableNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var locks = Definitions
            .Select(definition => new DeploymentConfigurationLock(
                definition.Field,
                definition.EnvironmentVariables
                    .Where(present.ContainsKey)
                    .Select(name => present[name])
                    .ToArray()))
            .Where(item => item.EnvironmentVariables.Count > 0)
            .ToArray();
        return locks.Length == 0 ? Empty : new DeploymentConfigurationLocks(locks);
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
                TmdbBaseUrl = Preserve(
                    "tmdb_base_url",
                    current.TmdbBaseUrl,
                    candidate.TmdbBaseUrl),
                TmdbProxyUrlOverridden = Preserve(
                    "tmdb_proxy_url",
                    current.TmdbProxyUrlOverridden,
                    candidate.TmdbProxyUrlOverridden),
                TmdbProxyUrl = Preserve(
                    "tmdb_proxy_url",
                    current.TmdbProxyUrl,
                    candidate.TmdbProxyUrl),
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
                BangumiProxyUrlOverridden = Preserve(
                    "bangumi_proxy_url",
                    current.BangumiProxyUrlOverridden,
                    candidate.BangumiProxyUrlOverridden),
                BangumiProxyUrl = Preserve(
                    "bangumi_proxy_url",
                    current.BangumiProxyUrl,
                    candidate.BangumiProxyUrl),
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
                AiHttpTimeoutSeconds = Preserve(
                    "ai_http_timeout_seconds",
                    current.AiHttpTimeoutSeconds,
                    candidate.AiHttpTimeoutSeconds),
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

        var tmdb = candidate.Metadata.Tmdb;
        if (IsLocked("tmdb_base_url"))
        {
            tmdb = tmdb with { BaseUrl = deployment.Metadata.Tmdb.BaseUrl };
        }
        if (IsLocked("tmdb_proxy_url"))
        {
            tmdb = tmdb with { ProxyUrl = deployment.Metadata.Tmdb.ProxyUrl };
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
        if (IsLocked("bangumi_proxy_url"))
        {
            bangumi = bangumi with { ProxyUrl = deployment.Metadata.Bangumi.ProxyUrl };
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

        var ai = candidate.Metadata.Ai;
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
            Metadata = candidate.Metadata with
            {
                Tmdb = tmdb,
                Bangumi = bangumi,
                Ai = ai,
            },
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
            "tmdb_base_url",
            deployment.Metadata.Tmdb.BaseUrl,
            candidate.Metadata.Tmdb.BaseUrl);
        AddIfChanged(
            "tmdb_proxy_url",
            deployment.Metadata.Tmdb.ProxyUrl,
            candidate.Metadata.Tmdb.ProxyUrl);
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
            "bangumi_base_url",
            deployment.Metadata.Bangumi.BaseUrl,
            candidate.Metadata.Bangumi.BaseUrl);
        AddIfChanged(
            "bangumi_proxy_url",
            deployment.Metadata.Bangumi.ProxyUrl,
            candidate.Metadata.Bangumi.ProxyUrl);
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
            "ai_use_metadata_match",
            deployment.Metadata.Ai.UseMetadataMatch,
            candidate.Metadata.Ai.UseMetadataMatch);
        AddIfChanged(
            "ai_http_timeout_seconds",
            deployment.Metadata.Ai.HttpTimeout,
            candidate.Metadata.Ai.HttpTimeout);
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
        IReadOnlyList<string> EnvironmentVariables);
}

public sealed class ConfigurationFieldLockedException(
    IReadOnlyList<string> fields) : InvalidOperationException(
        $"Configuration field(s) are controlled by environment variables: {string.Join(", ", fields)}.")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}
