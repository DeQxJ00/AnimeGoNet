using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.App.Configuration;

public sealed record SourceProfileDeploymentFieldLock(
    string SourceProfileId,
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

public sealed class SourceProfileDeploymentLocks
{
    private static readonly HashSet<string> KnownFields =
    [
        "category",
        "dynamic_tag_template",
        "mikan_identity_cookie",
    ];

    private static readonly Dictionary<string, string> LegacyMikanFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ANIMEGO_CATEGORY"] = "category",
            ["ANIMEGO_TAG"] = "dynamic_tag_template",
            ["ANIMEGO_MIKAN_COOKIE"] = "mikan_identity_cookie",
        };

    private readonly HashSet<(string SourceProfileId, string Field)> _keys;

    private SourceProfileDeploymentLocks(
        IReadOnlyList<SourceProfileDeploymentFieldLock> items)
    {
        Items = items;
        _keys = items
            .Select(item => (item.SourceProfileId, item.Field))
            .ToHashSet();
    }

    public static SourceProfileDeploymentLocks Empty { get; } = new([]);

    public IReadOnlyList<SourceProfileDeploymentFieldLock> Items { get; }

    public static SourceProfileDeploymentLocks FromCurrentProcess(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var names = Environment.GetEnvironmentVariables()
            .Keys.Cast<object>()
            .OfType<string>()
            .ToArray();
        return FromSources(names, args);
    }

    public static SourceProfileDeploymentLocks FromSources(
        IEnumerable<string> environmentVariableNames,
        IEnumerable<string> commandLineArguments)
    {
        ArgumentNullException.ThrowIfNull(environmentVariableNames);
        ArgumentNullException.ThrowIfNull(commandLineArguments);
        var values = new Dictionary<(string Id, string Field), MutableLock>();

        foreach (var rawName in environmentVariableNames)
        {
            if (TryResolve(rawName, out var id, out var field))
            {
                Add(id, field, rawName.Trim(), isEnvironment: true);
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
            var rawKey = separator >= 0
                ? rawArgument[2..separator]
                : rawArgument[2..];
            if (TryResolve(rawKey, out var id, out var field))
            {
                Add(id, field, $"--{rawKey}", isEnvironment: false);
            }
        }

        if (values.Count == 0)
        {
            return Empty;
        }

        return new SourceProfileDeploymentLocks(values
            .OrderBy(pair => pair.Key.Id, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Field, StringComparer.Ordinal)
            .Select(pair => new SourceProfileDeploymentFieldLock(
                pair.Key.Id,
                pair.Key.Field,
                pair.Value.EnvironmentVariables
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                pair.Value.CommandLineArguments
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray());

        void Add(
            string id,
            string field,
            string source,
            bool isEnvironment)
        {
            var key = (id, field);
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

    public bool IsLocked(string sourceProfileId, string field) =>
        _keys.Contains((
            sourceProfileId.Trim().ToLowerInvariant(),
            field.Trim().ToLowerInvariant()));

    public IReadOnlyList<SourceProfileDeploymentFieldLock> ForSource(
        string sourceProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        var id = sourceProfileId.Trim().ToLowerInvariant();
        return Items.Where(item => item.SourceProfileId == id).ToArray();
    }

    public SourceProfileDeploymentOverride? CreateOverride(SourceProfileSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        var category = IsLocked(seed.Id, "category");
        var dynamicTag = IsLocked(seed.Id, "dynamic_tag_template");
        var cookie = IsLocked(seed.Id, "mikan_identity_cookie");
        return !category && !dynamicTag && !cookie
            ? null
            : new SourceProfileDeploymentOverride(
                seed.Id,
                seed.Adapter,
                category,
                seed.Category,
                dynamicTag,
                seed.DynamicTagTemplate,
                cookie,
                seed.MikanIdentityCookie);
    }

    private static bool TryResolve(
        string? rawKey,
        out string id,
        out string field)
    {
        id = string.Empty;
        field = string.Empty;
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return false;
        }

        var key = rawKey.Trim();
        if (LegacyMikanFields.TryGetValue(key, out var legacyField))
        {
            id = "mikan";
            field = legacyField;
            return true;
        }

        var parts = key.Replace("__", ":", StringComparison.Ordinal)
            .Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !string.Equals(parts[0], "sources", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        id = parts[1].ToLowerInvariant();
        field = parts[2].ToLowerInvariant();
        return id.Length > 0 && KnownFields.Contains(field);
    }

    private sealed class MutableLock
    {
        public HashSet<string> EnvironmentVariables { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> CommandLineArguments { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
