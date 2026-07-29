namespace AnimeGo.Plugin.Abstractions;

public sealed class PluginCatalog
{
    private readonly IReadOnlyList<IAnimeGoPlugin> plugins;
    private readonly Dictionary<string, IAnimeGoPlugin> pluginsById;

    public PluginCatalog(IEnumerable<IAnimeGoPlugin> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var ordered = registrations
            .Select(Validate)
            .OrderBy(plugin => plugin.Descriptor.Order)
            .ThenBy(plugin => plugin.Descriptor.Id, StringComparer.Ordinal)
            .ToArray();
        var byId = new Dictionary<string, IAnimeGoPlugin>(StringComparer.Ordinal);
        foreach (var plugin in ordered)
        {
            if (!byId.TryAdd(plugin.Descriptor.Id, plugin))
            {
                throw new ArgumentException(
                    $"Duplicate plugin id '{plugin.Descriptor.Id}'.",
                    nameof(registrations));
            }
        }

        plugins = ordered;
        pluginsById = byId;
    }

    public IReadOnlyList<IAnimeGoPlugin> All => plugins;

    public IReadOnlyList<TPlugin> GetAll<TPlugin>()
        where TPlugin : class, IAnimeGoPlugin =>
        plugins.OfType<TPlugin>().ToArray();

    public TPlugin? Find<TPlugin>(string id)
        where TPlugin : class, IAnimeGoPlugin
    {
        var normalized = NormalizeLookupId(id);
        return pluginsById.TryGetValue(normalized, out var plugin)
            ? plugin as TPlugin
            : null;
    }

    public TPlugin Require<TPlugin>(string id)
        where TPlugin : class, IAnimeGoPlugin =>
        Find<TPlugin>(id)
        ?? throw new KeyNotFoundException(
            $"Plugin '{NormalizeLookupId(id)}' is not registered as {CategoryName<TPlugin>()}.");

    private static IAnimeGoPlugin Validate(IAnimeGoPlugin? plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(plugin.Descriptor);

        var descriptor = plugin.Descriptor;
        if (!IsStableId(descriptor.Id))
        {
            throw new ArgumentException(
                $"Plugin id '{descriptor.Id}' must be lowercase ASCII segments separated by '.', '-' or '_'.",
                nameof(plugin));
        }

        if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
        {
            throw new ArgumentException("Plugin display name is required.", nameof(plugin));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Version))
        {
            throw new ArgumentException("Plugin version is required.", nameof(plugin));
        }

        if (descriptor.Order < 0)
        {
            throw new ArgumentException("Plugin order cannot be negative.", nameof(plugin));
        }

        var implementedCategory = GetImplementedCategory(plugin);
        if (implementedCategory != descriptor.Category)
        {
            throw new ArgumentException(
                $"Plugin '{descriptor.Id}' declares {descriptor.Category} but implements {implementedCategory}.",
                nameof(plugin));
        }

        return plugin;
    }

    private static PluginCategory GetImplementedCategory(IAnimeGoPlugin plugin)
    {
        var categories = new List<PluginCategory>(6);
        if (plugin is IInputSourceAdapter)
        {
            categories.Add(PluginCategory.Source);
        }
        if (plugin is IFeedPlugin)
        {
            categories.Add(PluginCategory.Feed);
        }
        if (plugin is ITitleParserPlugin)
        {
            categories.Add(PluginCategory.Parser);
        }
        if (plugin is IFeedFilterPlugin)
        {
            categories.Add(PluginCategory.Filter);
        }
        if (plugin is IRenamePlugin)
        {
            categories.Add(PluginCategory.Rename);
        }
        if (plugin is IScheduledPlugin)
        {
            categories.Add(PluginCategory.Schedule);
        }

        return categories.Count == 1
            ? categories[0]
            : throw new ArgumentException(
                $"Plugin '{plugin.Descriptor.Id}' must implement exactly one category contract.",
                nameof(plugin));
    }

    private static string NormalizeLookupId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id.Trim().ToLowerInvariant();
    }

    private static bool IsStableId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)
            || !string.Equals(id, id.Trim(), StringComparison.Ordinal)
            || !string.Equals(id, id.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return false;
        }

        var previousSeparator = true;
        foreach (var character in id)
        {
            var separator = character is '.' or '-' or '_';
            if (!separator && !IsLowerAsciiLetterOrDigit(character))
            {
                return false;
            }

            if (separator && previousSeparator)
            {
                return false;
            }

            previousSeparator = separator;
        }

        return !previousSeparator;
    }

    private static bool IsLowerAsciiLetterOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static string CategoryName<TPlugin>()
        where TPlugin : class, IAnimeGoPlugin =>
        typeof(TPlugin) == typeof(IInputSourceAdapter) ? "source"
        : typeof(TPlugin) == typeof(IFeedPlugin) ? "feed"
        : typeof(TPlugin) == typeof(ITitleParserPlugin) ? "parser"
        : typeof(TPlugin) == typeof(IFeedFilterPlugin) ? "filter"
        : typeof(TPlugin) == typeof(IRenamePlugin) ? "rename"
        : typeof(TPlugin) == typeof(IScheduledPlugin) ? "schedule"
        : typeof(TPlugin).Name;
}
