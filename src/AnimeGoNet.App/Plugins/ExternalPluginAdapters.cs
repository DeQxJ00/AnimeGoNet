using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Logging;

namespace AnimeGoNet.App.Plugins;

internal static class ExternalPluginAdapterFactory
{
    public static IReadOnlyList<IAnimeGoPlugin> Create(
        ExternalPluginDiscoveryResult discovery,
        ExternalPluginHostManager host)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(host);
        return discovery.Packages
            .OrderBy(package => package.Manifest.Id, StringComparer.Ordinal)
            .Select<ExternalPluginPackage, IAnimeGoPlugin>(package =>
                package.Manifest.Type switch
                {
                    "source" => new ExternalSourceAdapter(package, host),
                    "feed" => new ExternalFeedAdapter(package, host),
                    "parser" => new ExternalParserAdapter(package, host),
                    "filter" => new ExternalFilterAdapter(package, host),
                    "rename" => new ExternalRenameAdapter(package, host),
                    "schedule" => new ExternalScheduleAdapter(package, host),
                    _ => throw new InvalidOperationException(
                        "Validated external plugin package has an unsupported type."),
                })
            .ToArray();
    }
}

internal abstract class ExternalPluginAdapterBase(
    ExternalPluginPackage package,
    ExternalPluginHostManager host,
    PluginCategory category)
{
    protected ExternalPluginPackage Package { get; } = package;

    protected ExternalPluginHostManager Host { get; } = host;

    public PluginDescriptor Descriptor { get; } = new(
        package.Manifest.Id,
        package.Manifest.Name,
        package.Manifest.Version,
        category,
        Order: 1000,
        IsBuiltIn: false);

    protected static JsonElement Serialize<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.SerializeToElement(value, typeInfo);
    }

    protected async ValueTask<TResult> InvokeAsync<TResult>(
        string operation,
        JsonElement payload,
        Func<JsonElement, TResult> resultFactory,
        Func<PluginOperationError, TResult> failureFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Host.ExecuteConfiguredAsync(
                Package.Manifest.Id,
                operation,
                payload,
                resultFactory,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ExternalPluginRemoteException exception)
        {
            return failureFactory(new PluginOperationError(
                exception.Code,
                SafeRemoteMessage(exception.Message)));
        }
        catch (ExternalPluginUnavailableException exception)
        {
            return failureFactory(new PluginOperationError(
                exception.Code,
                $"External plugin '{Package.Manifest.Id}' is unavailable."));
        }
        catch (ExternalPluginProtocolException exception)
        {
            return failureFactory(new PluginOperationError(
                exception.Code,
                $"External plugin '{Package.Manifest.Id}' returned an invalid response."));
        }
    }

    private static string SafeRemoteMessage(string message)
    {
        try
        {
            var safe = WebSocketLogFormatter.Redact(message);
            return safe.Length == 0 ? "External plugin operation failed." : safe;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return "External plugin operation failed.";
        }
    }
}

internal sealed class ExternalSourceAdapter(
    ExternalPluginPackage package,
    ExternalPluginHostManager host) :
    ExternalPluginAdapterBase(package, host, PluginCategory.Source),
    IInputSourceAdapter
{
    PluginDescriptor IAnimeGoPlugin.Descriptor => Descriptor;

    public async ValueTask<SourceIngestResult> NormalizeAsync(
        SourceIngestContext context,
        CancellationToken cancellationToken) =>
        await InvokeAsync(
            ExternalPluginOperations.SourceNormalize,
            Serialize(context, ExternalPluginAdapterJsonContext.Default.SourceIngestContext),
            ExternalPluginResultParser.ParseSource,
            static error => new SourceIngestResult(null, [error]),
            cancellationToken).ConfigureAwait(false);
}

internal sealed class ExternalFeedAdapter(
    ExternalPluginPackage package,
    ExternalPluginHostManager host) :
    ExternalPluginAdapterBase(package, host, PluginCategory.Feed),
    IFeedPlugin
{
    PluginDescriptor IAnimeGoPlugin.Descriptor => Descriptor;

    public async ValueTask<FeedResult> FetchAsync(
        FeedContext context,
        CancellationToken cancellationToken) =>
        await InvokeAsync(
            ExternalPluginOperations.FeedFetch,
            Serialize(context, ExternalPluginAdapterJsonContext.Default.FeedContext),
            ExternalPluginResultParser.ParseFeed,
            static error => new FeedResult(
                [],
                [error],
                ExternalPluginResultParser.EmptyStringMap),
            cancellationToken).ConfigureAwait(false);
}

internal sealed class ExternalParserAdapter(
    ExternalPluginPackage package,
    ExternalPluginHostManager host) :
    ExternalPluginAdapterBase(package, host, PluginCategory.Parser),
    ITitleParserPlugin
{
    PluginDescriptor IAnimeGoPlugin.Descriptor => Descriptor;

    public async ValueTask<TitleParseResult> ParseAsync(
        TitleParseContext context,
        CancellationToken cancellationToken) =>
        await InvokeAsync(
            ExternalPluginOperations.ParserParse,
            Serialize(context, ExternalPluginAdapterJsonContext.Default.TitleParseContext),
            ExternalPluginResultParser.ParseTitle,
            static error => new TitleParseResult(
                false,
                null,
                null,
                null,
                "unknown",
                null,
                null,
                null,
                [error]),
            cancellationToken).ConfigureAwait(false);
}

internal sealed class ExternalFilterAdapter(
    ExternalPluginPackage package,
    ExternalPluginHostManager host) :
    ExternalPluginAdapterBase(package, host, PluginCategory.Filter),
    IFeedFilterPlugin
{
    PluginDescriptor IAnimeGoPlugin.Descriptor => Descriptor;

    public async ValueTask<FilterResult> FilterAsync(
        FilterContext context,
        CancellationToken cancellationToken) =>
        await InvokeAsync(
            ExternalPluginOperations.FilterAll,
            Serialize(context, ExternalPluginAdapterJsonContext.Default.FilterContext),
            result => ExternalPluginResultParser.ParseFilter(result, context.Items),
            static error => new FilterResult(
                [],
                [error],
                ExternalPluginResultParser.EmptyStringMap),
            cancellationToken).ConfigureAwait(false);
}

internal sealed class ExternalRenameAdapter(
    ExternalPluginPackage package,
    ExternalPluginHostManager host) :
    ExternalPluginAdapterBase(package, host, PluginCategory.Rename),
    IRenamePlugin
{
    PluginDescriptor IAnimeGoPlugin.Descriptor => Descriptor;

    public async ValueTask<RenameResult> RenameAsync(
        RenameContext context,
        CancellationToken cancellationToken) =>
        await InvokeAsync(
            ExternalPluginOperations.RenamePlan,
            Serialize(context, ExternalPluginAdapterJsonContext.Default.RenameContext),
            ExternalPluginResultParser.ParseRename,
            static error => new RenameResult(false, null, [error]),
            cancellationToken).ConfigureAwait(false);
}

internal sealed class ExternalScheduleAdapter(
    ExternalPluginPackage package,
    ExternalPluginHostManager host) :
    ExternalPluginAdapterBase(package, host, PluginCategory.Schedule),
    IScheduledPlugin
{
    PluginDescriptor IAnimeGoPlugin.Descriptor => Descriptor;

    public async ValueTask<ScheduledResult> ExecuteAsync(
        ScheduledContext context,
        CancellationToken cancellationToken) =>
        await InvokeAsync(
            ExternalPluginOperations.ScheduleExecute,
            Serialize(context, ExternalPluginAdapterJsonContext.Default.ScheduledContext),
            ExternalPluginResultParser.ParseSchedule,
            static error => new ScheduledResult(false, null, [error], null),
            cancellationToken).ConfigureAwait(false);
}

internal static class ExternalPluginResultParser
{
    private const int MaximumCollectionItems = 10_000;
    private const int MaximumErrors = 128;
    private const int MaximumMapItems = 256;
    private const int MaximumTextLength = 8192;

    public static IReadOnlyDictionary<string, string> EmptyStringMap { get; } =
        FrozenDictionary<string, string>.Empty;

    public static SourceIngestResult ParseSource(JsonElement element)
    {
        const string code = "source_result_invalid";
        var result = Deserialize(
            element,
            ExternalPluginAdapterJsonContext.Default.SourceIngestResult,
            code);
        ValidateErrors(result.Errors, code);
        if (result.Item is null)
        {
            if (result.Errors.Count == 0)
            {
                throw Invalid(code);
            }
            return result;
        }
        if (result.Errors.Count != 0)
        {
            throw Invalid(code);
        }

        var item = result.Item;
        ValidateRequiredText(item.Source, 256, code);
        var torrentUri = RequireHttpUrl(item.TorrentUrl, code);
        if (item.TorrentUrlFingerprint is null
            || item.TorrentUrlFingerprint.Length != 64
            || item.TorrentUrlFingerprint.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || !string.Equals(
                item.TorrentUrlFingerprint,
                Convert.ToHexStringLower(SHA256.HashData(
                    Encoding.UTF8.GetBytes(torrentUri.AbsoluteUri))),
                StringComparison.Ordinal))
        {
            throw Invalid(code);
        }
        ValidateRequiredText(item.Title, 4096, code);
        ValidateOptionalText(item.SourceItemId, 512, code);
        ValidateOptionalText(item.SourceWorkId, 512, code);
        ValidatePositive(item.MikanId, code);
        ValidatePositive(item.BangumiId, code);
        ValidatePositive(item.AniDbId, code);
        ValidateOptionalText(item.ImdbId, 64, code);
        ValidateOptionalText(item.PublishedAtRaw, 256, code);
        return result;
    }

    public static FeedResult ParseFeed(JsonElement element)
    {
        const string code = "feed_result_invalid";
        var result = Deserialize(
            element,
            ExternalPluginAdapterJsonContext.Default.FeedResult,
            code);
        ValidateErrors(result.Errors, code);
        ValidateStringMap(result.Metadata, code);
        if (result.Items is null
            || result.Items.Count > MaximumCollectionItems
            || result.Errors.Count > 0 && result.Items.Count > 0)
        {
            throw Invalid(code);
        }
        foreach (var item in result.Items)
        {
            if (item is null || item.Length < 0)
            {
                throw Invalid(code);
            }
            ValidateRequiredText(item.Title, 4096, code);
            ValidateHttpUrl(item.TorrentUrl, code);
            ValidateOptionalHttpUrl(item.SourceUrl, code);
            ValidateOptionalText(item.SourceItemId, 512, code);
            ValidateOptionalText(item.SourceWorkId, 512, code);
            ValidateOptionalText(item.ContentType, 256, code);
            ValidateOptionalText(item.PublishedAtRaw, 256, code);
        }
        return result;
    }

    public static TitleParseResult ParseTitle(JsonElement element)
    {
        const string code = "parser_result_invalid";
        var result = Deserialize(
            element,
            ExternalPluginAdapterJsonContext.Default.TitleParseResult,
            code);
        ValidateErrors(result.Errors, code);
        if (result.Matched && result.Errors.Count > 0
            || result.Season is <= 0
            || result.Episode is <= 0)
        {
            throw Invalid(code);
        }
        ValidateOptionalText(result.AnimeTitle, 4096, code);
        ValidateOptionalToken(result.EpisodeKind, code);
        ValidateOptionalText(result.EpisodeText, 256, code);
        ValidateOptionalText(result.ReleaseGroup, 512, code);
        ValidateOptionalText(result.Resolution, 128, code);
        return result;
    }

    public static FilterResult ParseFilter(
        JsonElement element,
        IReadOnlyList<FilterItem> input)
    {
        const string code = "filter_result_invalid";
        ArgumentNullException.ThrowIfNull(input);
        var result = Deserialize(
            element,
            ExternalPluginAdapterJsonContext.Default.FilterResult,
            code);
        ValidateErrors(result.Errors, code);
        ValidateStringMap(result.Metadata, code);
        if (result.Decisions is null
            || result.Decisions.Count > MaximumCollectionItems
            || result.Errors.Count > 0 && result.Decisions.Count > 0)
        {
            throw Invalid(code);
        }
        if (result.Errors.Count > 0)
        {
            return result;
        }
        if (result.Decisions.Count != input.Count)
        {
            throw Invalid(code);
        }

        var expected = input.Select(item => item.Index).ToHashSet();
        var actual = new HashSet<int>();
        foreach (var decision in result.Decisions)
        {
            if (decision is null
                || !expected.Contains(decision.Index)
                || !actual.Add(decision.Index)
                || decision.Priority is < -1_000_000 or > 1_000_000)
            {
                throw Invalid(code);
            }
            ValidateRequiredText(decision.Outcome, 128, code);
            ValidateRequiredText(decision.Reason, 1024, code);
            ValidateNullableStringMap(decision.Metadata, code);
        }
        if (!actual.SetEquals(expected))
        {
            throw Invalid(code);
        }
        return result;
    }

    public static RenameResult ParseRename(JsonElement element)
    {
        const string code = "rename_result_invalid";
        var result = Deserialize(
            element,
            ExternalPluginAdapterJsonContext.Default.RenameResult,
            code);
        ValidateErrors(result.Errors, code);
        if (result.Matched)
        {
            if (result.Errors.Count > 0
                || !ValidRelativePath(result.RelativeTargetPath))
            {
                throw Invalid(code);
            }
        }
        else if (!string.IsNullOrWhiteSpace(result.RelativeTargetPath))
        {
            throw Invalid(code);
        }
        return result;
    }

    public static ScheduledResult ParseSchedule(JsonElement element)
    {
        const string code = "schedule_result_invalid";
        var result = Deserialize(
            element,
            ExternalPluginAdapterJsonContext.Default.ScheduledResult,
            code);
        ValidateErrors(result.Errors, code);
        ValidateOptionalText(result.Message, 1024, code);
        if (result.Succeeded != (result.Errors.Count == 0)
            || result.NextDelay is { } nextDelay
                && (nextDelay < TimeSpan.Zero || nextDelay > TimeSpan.FromDays(7)))
        {
            throw Invalid(code);
        }
        return result;
    }

    private static T Deserialize<T>(
        JsonElement element,
        JsonTypeInfo<T> typeInfo,
        string code)
        where T : class
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(code);
        }
        var nodeCount = 0;
        ValidateUniqueProperties(element, ref nodeCount, code);
        try
        {
            return JsonSerializer.Deserialize(element, typeInfo)
                ?? throw Invalid(code);
        }
        catch (ExternalPluginResultException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            throw Invalid(code, exception);
        }
    }

    private static void ValidateUniqueProperties(
        JsonElement element,
        ref int nodeCount,
        string code)
    {
        if (++nodeCount > 100_000)
        {
            throw Invalid(code);
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateUniqueProperties(item, ref nodeCount, code);
            }
            return;
        }
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw Invalid(code);
            }
            ValidateUniqueProperties(property.Value, ref nodeCount, code);
        }
    }

    private static void ValidateErrors(
        IReadOnlyList<PluginOperationError>? errors,
        string code)
    {
        if (errors is null || errors.Count > MaximumErrors)
        {
            throw Invalid(code);
        }
        foreach (var error in errors)
        {
            if (error is null || !ValidErrorCode(error.Code))
            {
                throw Invalid(code);
            }
            ValidateRequiredText(error.Message, 1024, code);
        }
    }

    private static void ValidateStringMap(
        IReadOnlyDictionary<string, string>? values,
        string code)
    {
        if (values is null || values.Count > MaximumMapItems)
        {
            throw Invalid(code);
        }
        foreach (var item in values)
        {
            ValidateMapKey(item.Key, code);
            ValidateRequiredOrEmptyText(item.Value, MaximumTextLength, code);
        }
    }

    private static void ValidateNullableStringMap(
        IReadOnlyDictionary<string, string?>? values,
        string code)
    {
        if (values is null || values.Count > MaximumMapItems)
        {
            throw Invalid(code);
        }
        foreach (var item in values)
        {
            ValidateMapKey(item.Key, code);
            ValidateOptionalText(item.Value, MaximumTextLength, code);
        }
    }

    private static void ValidateMapKey(string value, string code) =>
        ValidateRequiredText(value, 256, code);

    private static void ValidateRequiredText(string? value, int maximum, string code)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid(code);
        }
        ValidateRequiredOrEmptyText(value, maximum, code);
    }

    private static void ValidateRequiredOrEmptyText(
        string? value,
        int maximum,
        string code)
    {
        if (value is null
            || value.Length > maximum
            || value.Any(character => char.IsControl(character) && character != '\t'))
        {
            throw Invalid(code);
        }
    }

    private static void ValidateOptionalText(string? value, int maximum, string code)
    {
        if (value is not null)
        {
            ValidateRequiredOrEmptyText(value, maximum, code);
        }
    }

    private static void ValidateOptionalToken(string? value, string code)
    {
        if (value is null)
        {
            return;
        }
        if (value.Length is < 1 or > 128
            || value.Any(character =>
                character is not (>= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '_'
                    or '-'
                    or '.')))
        {
            throw Invalid(code);
        }
    }

    private static Uri RequireHttpUrl(string? value, string code)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumTextLength
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw Invalid(code);
        }
        return uri;
    }

    private static void ValidateHttpUrl(string? value, string code)
    {
        _ = RequireHttpUrl(value, code);
    }

    private static void ValidateOptionalHttpUrl(string? value, string code)
    {
        if (value is not null)
        {
            ValidateHttpUrl(value, code);
        }
    }

    private static void ValidatePositive(int? value, string code)
    {
        if (value is <= 0)
        {
            throw Invalid(code);
        }
    }

    private static bool ValidRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 4096
            || Path.IsPathRooted(value)
            || value.Any(char.IsControl))
        {
            return false;
        }
        var segments = value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0
            && segments.All(segment => segment is not ("." or ".."));
    }

    private static bool ValidErrorCode(string? value) =>
        value is { Length: >= 1 and <= 128 }
        && value[0] is >= 'a' and <= 'z'
        && value.All(character =>
            character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_');

    private static ExternalPluginResultException Invalid(
        string code,
        Exception? innerException = null) =>
        new(code, "The external plugin returned a result outside its typed contract.", innerException);
}

public static class ExternalPluginResultValidator
{
    public static void Validate(
        string pluginType,
        JsonElement result,
        JsonElement? requestPayload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginType);
        _ = pluginType switch
        {
            "source" => (object)ExternalPluginResultParser.ParseSource(result),
            "feed" => ExternalPluginResultParser.ParseFeed(result),
            "parser" => ExternalPluginResultParser.ParseTitle(result),
            "filter" => ExternalPluginResultParser.ParseFilter(
                result,
                ReadFilterItems(requestPayload)),
            "rename" => ExternalPluginResultParser.ParseRename(result),
            "schedule" => ExternalPluginResultParser.ParseSchedule(result),
            _ => throw new ArgumentOutOfRangeException(
                nameof(pluginType),
                "External plugin result validation requires a supported plugin type."),
        };
    }

    private static FilterItem[] ReadFilterItems(JsonElement? requestPayload)
    {
        if (requestPayload is not { ValueKind: JsonValueKind.Object } payload
            || !payload.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw new ExternalPluginProtocolException(
                "filter_fixture_payload_invalid",
                "Filter result validation requires the original fixture items.");
        }
        try
        {
            var parsed = JsonSerializer.Deserialize(
                items,
                ExternalPluginAdapterJsonContext.Default.FilterItemArray) ?? [];
            if (parsed.Any(item => item is null))
            {
                throw new ExternalPluginProtocolException(
                    "filter_fixture_payload_invalid",
                    "Filter fixture items do not match the typed contract.");
            }
            return parsed;
        }
        catch (JsonException exception)
        {
            throw new ExternalPluginProtocolException(
                "filter_fixture_payload_invalid",
                "Filter fixture items do not match the typed contract.",
                exception);
        }
    }
}

public static class ExternalPluginRequestValidator
{
    public static void Validate(string pluginType, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw Invalid();
        }
        object? request;
        try
        {
            request = pluginType switch
            {
                "source" => JsonSerializer.Deserialize(
                    payload,
                    ExternalPluginAdapterJsonContext.Default.SourceIngestContext),
                "feed" => JsonSerializer.Deserialize(
                    payload,
                    ExternalPluginAdapterJsonContext.Default.FeedContext),
                "parser" => JsonSerializer.Deserialize(
                    payload,
                    ExternalPluginAdapterJsonContext.Default.TitleParseContext),
                "filter" => JsonSerializer.Deserialize(
                    payload,
                    ExternalPluginAdapterJsonContext.Default.FilterContext),
                "rename" => JsonSerializer.Deserialize(
                    payload,
                    ExternalPluginAdapterJsonContext.Default.RenameContext),
                "schedule" => JsonSerializer.Deserialize(
                    payload,
                    ExternalPluginAdapterJsonContext.Default.ScheduledContext),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(pluginType),
                    "External plugin request validation requires a supported plugin type."),
            };
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw Invalid(exception);
        }
        if (request is null || !HasRequiredMembers(request))
        {
            throw Invalid();
        }
    }

    private static bool HasRequiredMembers(object request) => request switch
    {
        SourceIngestContext source => source.Source is not null,
        FeedContext feed =>
            feed.SourceProfileId is not null
            && feed.FeedUrl is not null
            && ValidMap(feed.Arguments),
        TitleParseContext parser => parser.Title is not null && ValidMap(parser.Arguments),
        FilterContext filter =>
            filter.SourceProfileId is not null
            && ValidMap(filter.Arguments)
            && filter.Items is not null
            && filter.Items.All(item =>
                item is not null && item.Title is not null && item.TorrentUrl is not null),
        RenameContext rename =>
            rename.SourcePath is not null
            && rename.SeriesName is not null
            && rename.Disposition is not null
            && ValidMap(rename.Arguments),
        ScheduledContext schedule => schedule.TaskId is not null && ValidMap(schedule.Arguments),
        _ => false,
    };

    private static bool ValidMap(IReadOnlyDictionary<string, string>? values) =>
        values is not null && values.All(item => item.Key is not null && item.Value is not null);

    private static ExternalPluginProtocolException Invalid(Exception? innerException = null) =>
        new(
            "plugin_fixture_payload_invalid",
            "The fixture payload does not match the typed plugin request contract.",
            innerException);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(SourceIngestContext))]
[JsonSerializable(typeof(SourceIngestResult))]
[JsonSerializable(typeof(FeedContext))]
[JsonSerializable(typeof(FeedResult))]
[JsonSerializable(typeof(TitleParseContext))]
[JsonSerializable(typeof(TitleParseResult))]
[JsonSerializable(typeof(FilterContext))]
[JsonSerializable(typeof(FilterItem[]))]
[JsonSerializable(typeof(FilterResult))]
[JsonSerializable(typeof(RenameContext))]
[JsonSerializable(typeof(RenameResult))]
[JsonSerializable(typeof(ScheduledContext))]
[JsonSerializable(typeof(ScheduledResult))]
internal sealed partial class ExternalPluginAdapterJsonContext : JsonSerializerContext;
