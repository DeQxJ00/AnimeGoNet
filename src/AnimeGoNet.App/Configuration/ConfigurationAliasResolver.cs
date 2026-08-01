using Microsoft.Extensions.Configuration;

namespace AnimeGoNet.App.Configuration;

internal static class ConfigurationAliasResolver
{
    public static string? FirstNonEmpty(
        IConfiguration configuration,
        params string[] keys) =>
        Resolve(configuration, includeEmpty: false, keys);

    public static string? FirstPresent(
        IConfiguration configuration,
        params string[] keys) =>
        Resolve(configuration, includeEmpty: true, keys);

    public static IReadOnlyDictionary<string, string?> HighestPriorityValues(
        IConfiguration configuration,
        params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(keys);

        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.Reverse())
            {
                var values = new Dictionary<string, string?>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var key in keys)
                {
                    if (provider.TryGet(key, out var value))
                    {
                        values[key] = value;
                    }
                }

                if (values.Count > 0)
                {
                    return values;
                }
            }

            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        return keys
            .Where(key => configuration[key] is not null)
            .ToDictionary(
                key => key,
                key => configuration[key],
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? Resolve(
        IConfiguration configuration,
        bool includeEmpty,
        IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(keys);

        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.Reverse())
            {
                foreach (var key in keys)
                {
                    if (provider.TryGet(key, out var value)
                        && value is not null
                        && (includeEmpty || !string.IsNullOrWhiteSpace(value)))
                    {
                        return value.Trim();
                    }
                }
            }

            return null;
        }

        foreach (var key in keys)
        {
            if (configuration[key] is { } value
                && (includeEmpty || !string.IsNullOrWhiteSpace(value)))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
