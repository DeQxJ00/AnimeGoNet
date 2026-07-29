using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Configuration;

public sealed record DownloaderDeploymentFieldLock(
    string DownloaderId,
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
}

public sealed class DownloaderDeploymentLocks
{
    private static readonly HashSet<string> KnownFields =
    [
        "type",
        "base_url",
        "username",
        "password",
        "download_path",
        "enabled",
    ];

    private static readonly Dictionary<string, string> LegacyBtFields =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANIMEGO_CLIENT"] = "type",
            ["ANIMEGO_CLIENT_URL"] = "base_url",
            ["ANIMEGO_CLIENT_USERNAME"] = "username",
            ["ANIMEGO_CLIENT_PASSWORD"] = "password",
            ["ANIMEGO_CLIENT_DOWNLOAD_PATH"] = "download_path",
        };

    private readonly HashSet<(string DownloaderId, string Field)> _keys;

    private DownloaderDeploymentLocks(
        IReadOnlyList<DownloaderDeploymentFieldLock> items)
    {
        Items = items;
        _keys = items
            .Select(item => (item.DownloaderId, item.Field))
            .ToHashSet();
    }

    public static DownloaderDeploymentLocks Empty { get; } = new([]);

    public IReadOnlyList<DownloaderDeploymentFieldLock> Items { get; }

    public static DownloaderDeploymentLocks FromCurrentProcess(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var names = Environment.GetEnvironmentVariables()
            .Keys.Cast<object>()
            .OfType<string>()
            .ToArray();
        return FromSources(names, args);
    }

    public static DownloaderDeploymentLocks FromSources(
        IEnumerable<string> environmentVariableNames,
        IEnumerable<string> commandLineArguments)
    {
        ArgumentNullException.ThrowIfNull(environmentVariableNames);
        ArgumentNullException.ThrowIfNull(commandLineArguments);

        var values = new Dictionary<
            (string DownloaderId, string Field),
            MutableLock>();
        foreach (var rawName in environmentVariableNames)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                continue;
            }

            var name = rawName.Trim();
            if (LegacyBtFields.TryGetValue(name, out var legacyField))
            {
                Add("bt", legacyField, name, isEnvironment: true);
                continue;
            }

            if (TryParseCanonicalKey(name, out var id, out var field))
            {
                Add(id, field, name, isEnvironment: true);
            }
        }

        foreach (var rawArgument in commandLineArguments)
        {
            if (string.IsNullOrWhiteSpace(rawArgument)
                || !rawArgument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = rawArgument.IndexOf('=');
            var key = separator >= 0
                ? rawArgument[2..separator]
                : rawArgument[2..];
            if (LegacyBtFields.TryGetValue(key, out var legacyField))
            {
                Add("bt", legacyField, $"--{key}", isEnvironment: false);
                continue;
            }

            if (TryParseCanonicalKey(key, out var id, out var field))
            {
                Add(id, field, $"--{key}", isEnvironment: false);
            }
        }

        if (values.Count == 0)
        {
            return Empty;
        }

        return new DownloaderDeploymentLocks(values
            .OrderBy(pair => pair.Key.DownloaderId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Field, StringComparer.Ordinal)
            .Select(pair => new DownloaderDeploymentFieldLock(
                pair.Key.DownloaderId,
                pair.Key.Field,
                pair.Value.EnvironmentVariables
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                pair.Value.CommandLineArguments
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray());

        void Add(
            string downloaderId,
            string field,
            string source,
            bool isEnvironment)
        {
            var key = (downloaderId, field);
            if (!values.TryGetValue(key, out var value))
            {
                value = new MutableLock();
                values.Add(key, value);
            }

            if (isEnvironment)
            {
                value.EnvironmentVariables.Add(source);
            }
            else
            {
                value.CommandLineArguments.Add(source);
            }
        }
    }

    public bool IsLocked(string downloaderId, string field) =>
        _keys.Contains((
            downloaderId.Trim().ToLowerInvariant(),
            field.Trim().ToLowerInvariant()));

    public IReadOnlyList<DownloaderDeploymentFieldLock> ForDownloader(
        string downloaderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloaderId);
        var id = downloaderId.Trim().ToLowerInvariant();
        return Items
            .Where(item => item.DownloaderId == id)
            .ToArray();
    }

    public AnimeGoOptions Reapply(
        AnimeGoOptions deployment,
        AnimeGoOptions candidate)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(candidate);
        if (Items.Count == 0)
        {
            return candidate;
        }

        var downloaders = new Dictionary<string, QbittorrentInstanceOptions>(
            candidate.Downloaders,
            StringComparer.OrdinalIgnoreCase);
        foreach (var (id, deployed) in deployment.Downloaders)
        {
            var effective = downloaders.GetValueOrDefault(id) ?? deployed;
            downloaders[id] = Reapply(id, deployed, effective);
        }

        return candidate with { Downloaders = downloaders };
    }

    public QbittorrentInstanceOptions Reapply(
        string downloaderId,
        QbittorrentInstanceOptions deployment,
        QbittorrentInstanceOptions candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloaderId);
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate with
        {
            Type = IsLocked(downloaderId, "type")
                ? deployment.Type
                : candidate.Type,
            BaseUrl = IsLocked(downloaderId, "base_url")
                ? deployment.BaseUrl
                : candidate.BaseUrl,
            Username = IsLocked(downloaderId, "username")
                ? deployment.Username
                : candidate.Username,
            Password = IsLocked(downloaderId, "password")
                ? deployment.Password
                : candidate.Password,
            DownloadPath = IsLocked(downloaderId, "download_path")
                ? deployment.DownloadPath
                : candidate.DownloadPath,
            Enabled = IsLocked(downloaderId, "enabled")
                ? deployment.Enabled
                : candidate.Enabled,
        };
    }

    private static bool TryParseCanonicalKey(
        string raw,
        out string downloaderId,
        out string field)
    {
        var parts = raw.Trim()
            .Replace("__", ":", StringComparison.Ordinal)
            .ToLowerInvariant()
            .Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3
            && parts[0] == "downloaders"
            && AnimeGoOptionsValidator.IsStableId(parts[1])
            && KnownFields.Contains(parts[2]))
        {
            downloaderId = parts[1];
            field = parts[2];
            return true;
        }

        downloaderId = string.Empty;
        field = string.Empty;
        return false;
    }

    private sealed class MutableLock
    {
        public HashSet<string> EnvironmentVariables { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> CommandLineArguments { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
