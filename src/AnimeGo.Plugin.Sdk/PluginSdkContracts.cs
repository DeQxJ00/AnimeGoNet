using System.Globalization;
using System.Text.Json;
using AnimeGo.Plugin.Abstractions;

namespace AnimeGo.Plugin.Sdk;

public sealed record AnimeGoPluginMetadata(
    string Id,
    string Version,
    PluginCategory Category,
    IReadOnlyList<string> Capabilities)
{
    internal void Validate(PluginCategory expectedCategory)
    {
        if (Category != expectedCategory
            || !ValidPluginId(Id)
            || !ValidSemVer(Version)
            || Capabilities is null
            || Capabilities.Count > 128)
        {
            throw new ArgumentException("Plugin metadata is invalid.");
        }
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in Capabilities)
        {
            if (!ValidStableToken(capability) || !capabilities.Add(capability))
            {
                throw new ArgumentException("Plugin capabilities must be unique stable tokens.");
            }
        }
    }

    internal string Type => Category switch
    {
        PluginCategory.Source => "source",
        PluginCategory.Feed => "feed",
        PluginCategory.Parser => "parser",
        PluginCategory.Filter => "filter",
        PluginCategory.Rename => "rename",
        PluginCategory.Schedule => "schedule",
        _ => throw new ArgumentOutOfRangeException(nameof(Category)),
    };

    internal string Operation => $"{Type}.{Category switch
    {
        PluginCategory.Source => "normalize",
        PluginCategory.Feed => "fetch",
        PluginCategory.Parser => "parse",
        PluginCategory.Filter => "all",
        PluginCategory.Rename => "plan",
        PluginCategory.Schedule => "execute",
        _ => throw new ArgumentOutOfRangeException(nameof(Category)),
    }}";

    private static bool ValidPluginId(string? value)
    {
        if (value is not { Length: >= 3 and <= 128 }
            || !string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return false;
        }
        var segments = value.Split('.');
        return segments.Length >= 2 && segments.All(IsDomainSegment);
    }

    private static bool IsDomainSegment(string segment) =>
        segment.Length > 0
        && segment[0] is >= 'a' and <= 'z'
        && segment[^1] != '-'
        && segment.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static bool ValidStableToken(string? value)
    {
        if (value is not { Length: >= 1 and <= 128 }
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || !string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal)
            || value[0] is not (>= 'a' and <= 'z' or >= '0' and <= '9'))
        {
            return false;
        }
        var previousSeparator = false;
        foreach (var character in value)
        {
            var separator = character is '.' or '-' or '_';
            if (!separator
                && character is not (>= 'a' and <= 'z' or >= '0' and <= '9')
                || separator && previousSeparator)
            {
                return false;
            }
            previousSeparator = separator;
        }
        return !previousSeparator;
    }

    private static bool ValidSemVer(string? value)
    {
        if (value is not { Length: >= 5 and <= 64 } || value.Any(char.IsWhiteSpace))
        {
            return false;
        }
        var buildIndex = value.IndexOf('+', StringComparison.Ordinal);
        if (buildIndex >= 0
            && (value[(buildIndex + 1)..].Contains('+')
                || !ValidIdentifiers(value[(buildIndex + 1)..], true)))
        {
            return false;
        }
        var coreAndPre = buildIndex >= 0 ? value[..buildIndex] : value;
        var prereleaseIndex = coreAndPre.IndexOf('-', StringComparison.Ordinal);
        if (prereleaseIndex >= 0
            && !ValidIdentifiers(coreAndPre[(prereleaseIndex + 1)..], false))
        {
            return false;
        }
        var coreText = prereleaseIndex >= 0 ? coreAndPre[..prereleaseIndex] : coreAndPre;
        var core = coreText.Split('.');
        return core.Length == 3 && core.All(ValidCoreNumber);
    }

    private static bool ValidCoreNumber(string value) =>
        value.Length > 0
        && (value.Length == 1 || value[0] != '0')
        && value.All(character => character is >= '0' and <= '9')
        && uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool ValidIdentifiers(string value, bool numericLeadingZeroAllowed) =>
        value.Split('.').All(identifier =>
            identifier.Length > 0
            && identifier.All(character =>
                character is >= 'A' and <= 'Z'
                    or >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '-')
            && (numericLeadingZeroAllowed
                || !identifier.All(character => character is >= '0' and <= '9')
                || identifier.Length == 1
                || identifier[0] != '0'));

    internal static bool ValidErrorCode(string? value) =>
        value is { Length: >= 1 and <= 128 }
        && value[0] is >= 'a' and <= 'z'
        && value.All(character =>
            character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_');
}

public sealed record AnimeGoPluginExecutionContext<TRequest>(
    TRequest Request,
    JsonElement RawPayload,
    JsonElement Config,
    string PluginDataPath);

public interface IAnimeGoExternalPluginHandler<TRequest, TResult>
{
    ValueTask<TResult> ExecuteAsync(
        AnimeGoPluginExecutionContext<TRequest> context,
        CancellationToken cancellationToken);
}

public interface ISourcePluginHandler :
    IAnimeGoExternalPluginHandler<SourceIngestContext, SourceIngestResult>;

public interface IFeedPluginHandler :
    IAnimeGoExternalPluginHandler<FeedContext, FeedResult>;

public interface IParserPluginHandler :
    IAnimeGoExternalPluginHandler<TitleParseContext, TitleParseResult>;

public interface IFilterPluginHandler :
    IAnimeGoExternalPluginHandler<FilterContext, FilterResult>;

public interface IRenamePluginHandler :
    IAnimeGoExternalPluginHandler<RenameContext, RenameResult>;

public interface ISchedulePluginHandler :
    IAnimeGoExternalPluginHandler<ScheduledContext, ScheduledResult>;

public sealed class AnimeGoPluginExecutionException : Exception
{
    public AnimeGoPluginExecutionException(string code, string message)
        : base(ValidateMessage(message))
    {
        if (!AnimeGoPluginMetadata.ValidErrorCode(code))
        {
            throw new ArgumentException(
                "Plugin execution error codes must use lowercase letters, digits and underscores.",
                nameof(code));
        }
        Code = code;
    }

    public string Code { get; }

    private static string ValidateMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)
            || message.Length > 1024
            || message.Any(character => char.IsControl(character) && character != '\t'))
        {
            throw new ArgumentException("Plugin execution error messages are invalid.", nameof(message));
        }
        return message;
    }
}

public sealed record AnimeGoPluginHostOptions
{
    public int MaximumRequestBytes { get; init; } = 1024 * 1024;

    public int MaximumResponseBytes { get; init; } = 1024 * 1024;

    internal void Validate()
    {
        if (MaximumRequestBytes is < 1024 or > 16 * 1024 * 1024
            || MaximumResponseBytes is < 1024 or > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumRequestBytes),
                "Plugin protocol limits must be between 1 KiB and 16 MiB.");
        }
    }
}

public sealed record AnimeGoPluginHostEnvironment(
    string PluginId,
    string ApiVersion,
    string PluginDataPath)
{
    public static AnimeGoPluginHostEnvironment FromProcess() => new(
        Environment.GetEnvironmentVariable("ANIMEGO_PLUGIN_ID") ?? string.Empty,
        Environment.GetEnvironmentVariable("ANIMEGO_PLUGIN_API_VERSION") ?? string.Empty,
        Environment.GetEnvironmentVariable("ANIMEGO_PLUGIN_DATA_PATH") ?? string.Empty);

    internal string Validate(AnimeGoPluginMetadata metadata)
    {
        if (!string.Equals(PluginId, metadata.Id, StringComparison.Ordinal)
            || ApiVersion != "1"
            || string.IsNullOrWhiteSpace(PluginDataPath))
        {
            throw new InvalidOperationException(
                "AnimeGoNet plugin environment does not match plugin metadata.");
        }
        return Path.GetFullPath(PluginDataPath);
    }
}
