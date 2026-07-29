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
        new("tmdb_proxy_url", ["tmdb_proxy_url"]),
        new("tmdb_language", ["tmdb_language"]),
        new("tmdb_http_timeout_seconds", ["tmdb_timeout_second"]),
        new("tmdb_api_key", ["tmdb_api_key"]),
        new("tmdb_read_access_token", ["tmdb_read_access_token"]),
        new("bangumi_base_url", ["bangumi_base_url"]),
        new("bangumi_proxy_url", ["bangumi_proxy_url"]),
        new("bangumi_http_timeout_seconds", ["bangumi_timeout_second"]),
        new(
            "ai_use_metadata_match",
            ["ai_use_metadata_match", "ai_use_season_match", "ai_use_episode_match"]),
        new("ai_http_timeout_seconds", ["ai_timeout_second"]),
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

        return candidate with
        {
            Metadata = candidate.Metadata with
            {
                Tmdb = tmdb,
                Bangumi = bangumi,
                Ai = ai,
            },
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
            "ai_use_metadata_match",
            deployment.Metadata.Ai.UseMetadataMatch,
            candidate.Metadata.Ai.UseMetadataMatch);
        AddIfChanged(
            "ai_http_timeout_seconds",
            deployment.Metadata.Ai.HttpTimeout,
            candidate.Metadata.Ai.HttpTimeout);
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
