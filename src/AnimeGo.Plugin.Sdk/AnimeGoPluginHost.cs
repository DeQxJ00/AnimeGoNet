using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AnimeGo.Plugin.Abstractions;

namespace AnimeGo.Plugin.Sdk;

public static class AnimeGoPluginHost
{
    private const int CurrentApiVersion = 1;
    private static readonly byte[] Newline = [(byte)'\n'];

    public static Task<int> RunSourceAsync(
        AnimeGoPluginMetadata metadata,
        ISourcePluginHandler handler,
        JsonTypeInfo<SourceIngestContext> requestTypeInfo,
        JsonTypeInfo<SourceIngestResult> resultTypeInfo,
        AnimeGoPluginHostOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunStandardAsync(
            metadata,
            PluginCategory.Source,
            handler,
            requestTypeInfo,
            resultTypeInfo,
            options,
            cancellationToken);

    public static Task<int> RunFeedAsync(
        AnimeGoPluginMetadata metadata,
        IFeedPluginHandler handler,
        JsonTypeInfo<FeedContext> requestTypeInfo,
        JsonTypeInfo<FeedResult> resultTypeInfo,
        AnimeGoPluginHostOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunStandardAsync(
            metadata,
            PluginCategory.Feed,
            handler,
            requestTypeInfo,
            resultTypeInfo,
            options,
            cancellationToken);

    public static Task<int> RunParserAsync(
        AnimeGoPluginMetadata metadata,
        IParserPluginHandler handler,
        JsonTypeInfo<TitleParseContext> requestTypeInfo,
        JsonTypeInfo<TitleParseResult> resultTypeInfo,
        AnimeGoPluginHostOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunStandardAsync(
            metadata,
            PluginCategory.Parser,
            handler,
            requestTypeInfo,
            resultTypeInfo,
            options,
            cancellationToken);

    public static Task<int> RunFilterAsync(
        AnimeGoPluginMetadata metadata,
        IFilterPluginHandler handler,
        JsonTypeInfo<FilterContext> requestTypeInfo,
        JsonTypeInfo<FilterResult> resultTypeInfo,
        AnimeGoPluginHostOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunStandardAsync(
            metadata,
            PluginCategory.Filter,
            handler,
            requestTypeInfo,
            resultTypeInfo,
            options,
            cancellationToken);

    public static Task<int> RunRenameAsync(
        AnimeGoPluginMetadata metadata,
        IRenamePluginHandler handler,
        JsonTypeInfo<RenameContext> requestTypeInfo,
        JsonTypeInfo<RenameResult> resultTypeInfo,
        AnimeGoPluginHostOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunStandardAsync(
            metadata,
            PluginCategory.Rename,
            handler,
            requestTypeInfo,
            resultTypeInfo,
            options,
            cancellationToken);

    public static Task<int> RunScheduleAsync(
        AnimeGoPluginMetadata metadata,
        ISchedulePluginHandler handler,
        JsonTypeInfo<ScheduledContext> requestTypeInfo,
        JsonTypeInfo<ScheduledResult> resultTypeInfo,
        AnimeGoPluginHostOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunStandardAsync(
            metadata,
            PluginCategory.Schedule,
            handler,
            requestTypeInfo,
            resultTypeInfo,
            options,
            cancellationToken);

    internal static Task<int> RunAsync<TRequest, TResult>(
        AnimeGoPluginMetadata metadata,
        PluginCategory expectedCategory,
        IAnimeGoExternalPluginHandler<TRequest, TResult> handler,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        Stream input,
        Stream output,
        TextWriter error,
        AnimeGoPluginHostEnvironment environment,
        AnimeGoPluginHostOptions? options = null,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResult : class =>
        RunCoreAsync(
            metadata,
            expectedCategory,
            handler,
            requestTypeInfo,
            resultTypeInfo,
            input,
            output,
            error,
            environment,
            options ?? new AnimeGoPluginHostOptions(),
            cancellationToken);

    private static Task<int> RunStandardAsync<TRequest, TResult>(
        AnimeGoPluginMetadata metadata,
        PluginCategory expectedCategory,
        IAnimeGoExternalPluginHandler<TRequest, TResult> handler,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        AnimeGoPluginHostOptions? options,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResult : class =>
        RunAsync(
            metadata,
            expectedCategory,
            handler,
            requestTypeInfo,
            resultTypeInfo,
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            Console.Error,
            AnimeGoPluginHostEnvironment.FromProcess(),
            options,
            cancellationToken);

    private static async Task<int> RunCoreAsync<TRequest, TResult>(
        AnimeGoPluginMetadata metadata,
        PluginCategory expectedCategory,
        IAnimeGoExternalPluginHandler<TRequest, TResult> handler,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        Stream input,
        Stream output,
        TextWriter error,
        AnimeGoPluginHostEnvironment environment,
        AnimeGoPluginHostOptions options,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(environment);
        metadata.Validate(expectedCategory);
        options.Validate();
        var dataPath = environment.Validate(metadata);
        var reader = new PluginLineReader(input);
        var initialized = false;

        while (true)
        {
            byte[]? line;
            try
            {
                line = await reader.ReadLineAsync(
                    options.MaximumRequestBytes,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (PluginInputException)
            {
                return 20;
            }
            if (line is null)
            {
                return initialized ? 20 : 0;
            }

            if (!TryDeserializeRequest(line, out var request))
            {
                return 20;
            }
            if (request.ApiVersion != CurrentApiVersion
                || !ValidRequestId(request.RequestId)
                || string.IsNullOrWhiteSpace(request.Method))
            {
                return 20;
            }
            var requestId = request.RequestId!;

            switch (request.Method)
            {
                case "initialize":
                    {
                        if (initialized
                            || request.HasOperation
                            || request.Config is not null
                            || !TryDeserialize(
                                request.Payload,
                                PluginSdkJsonContext.Default.PluginInitializePayload,
                                out var initialize)
                            || !InitializeMatches(metadata, initialize))
                        {
                            if (!await WriteErrorAsync(
                                    output,
                                    requestId,
                                    "plugin_initialize_invalid",
                                    "Plugin initialize metadata does not match this executable.",
                                    options.MaximumResponseBytes,
                                    cancellationToken).ConfigureAwait(false))
                            {
                                return 31;
                            }
                            return 21;
                        }

                        var result = JsonSerializer.SerializeToElement(
                            new PluginInitializeResult(
                                metadata.Id,
                                metadata.Version,
                                CurrentApiVersion,
                                metadata.Type,
                                metadata.Capabilities),
                            PluginSdkJsonContext.Default.PluginInitializeResult);
                        if (!await WriteSuccessAsync(
                                output,
                                requestId,
                                result,
                                options.MaximumResponseBytes,
                                cancellationToken).ConfigureAwait(false))
                        {
                            return 31;
                        }
                        initialized = true;
                        break;
                    }
                case "execute":
                    {
                        if (!initialized
                            || !string.Equals(
                                request.Operation,
                                metadata.Operation,
                                StringComparison.Ordinal)
                            || request.Payload is not { ValueKind: JsonValueKind.Object } payload
                            || request.Config is not { ValueKind: JsonValueKind.Object } config)
                        {
                            if (!await WriteErrorAsync(
                                    output,
                                    requestId,
                                    "plugin_execute_invalid",
                                    "Plugin execute request is invalid for this plugin type.",
                                    options.MaximumResponseBytes,
                                    cancellationToken).ConfigureAwait(false))
                            {
                                return 31;
                            }
                            break;
                        }
                        if (!TryDeserialize(payload, requestTypeInfo, out TRequest? typedRequest))
                        {
                            if (!await WriteErrorAsync(
                                    output,
                                    requestId,
                                    "plugin_payload_invalid",
                                    "Plugin execute payload does not match its typed contract.",
                                    options.MaximumResponseBytes,
                                    cancellationToken).ConfigureAwait(false))
                            {
                                return 31;
                            }
                            break;
                        }

                        TResult handlerResult;
                        try
                        {
                            handlerResult = await handler.ExecuteAsync(
                                new AnimeGoPluginExecutionContext<TRequest>(
                                    typedRequest!,
                                    payload.Clone(),
                                    config.Clone(),
                                    dataPath),
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (AnimeGoPluginExecutionException exception)
                        {
                            if (!await WriteErrorAsync(
                                    output,
                                    requestId,
                                    exception.Code,
                                    exception.Message,
                                    options.MaximumResponseBytes,
                                    cancellationToken).ConfigureAwait(false))
                            {
                                return 31;
                            }
                            break;
                        }
                        catch (Exception exception)
                        {
                            await WriteUnhandledAsync(error, exception, cancellationToken)
                                .ConfigureAwait(false);
                            return 30;
                        }
                        if (handlerResult is null)
                        {
                            return 30;
                        }
                        JsonElement result;
                        try
                        {
                            result = JsonSerializer.SerializeToElement(handlerResult, resultTypeInfo);
                        }
                        catch (Exception exception)
                        {
                            await WriteUnhandledAsync(error, exception, cancellationToken)
                                .ConfigureAwait(false);
                            return 30;
                        }
                        if (!await WriteSuccessAsync(
                                output,
                                requestId,
                                result,
                                options.MaximumResponseBytes,
                                cancellationToken).ConfigureAwait(false))
                        {
                            return 31;
                        }
                        break;
                    }
                case "health":
                    {
                        if (!initialized || !EmptyRequest(request))
                        {
                            return 20;
                        }
                        var result = JsonSerializer.SerializeToElement(
                            new PluginHealthResult(true),
                            PluginSdkJsonContext.Default.PluginHealthResult);
                        if (!await WriteSuccessAsync(
                                output,
                                requestId,
                                result,
                                options.MaximumResponseBytes,
                                cancellationToken).ConfigureAwait(false))
                        {
                            return 31;
                        }
                        break;
                    }
                case "shutdown":
                    {
                        if (!initialized
                            || request.HasOperation
                            || request.Config is not null
                            || !TryDeserialize(
                                request.Payload,
                                PluginSdkJsonContext.Default.PluginShutdownPayload,
                                out var shutdown)
                            || !ValidShutdownReason(shutdown!.Reason))
                        {
                            return 20;
                        }
                        var result = JsonSerializer.SerializeToElement(
                            new PluginShutdownResult(true),
                            PluginSdkJsonContext.Default.PluginShutdownResult);
                        return await WriteSuccessAsync(
                            output,
                            requestId,
                            result,
                            options.MaximumResponseBytes,
                            cancellationToken).ConfigureAwait(false)
                            ? 0
                            : 31;
                    }
                default:
                    return 20;
            }
        }
    }

    private static bool EmptyRequest(PluginWireRequest request) =>
        !request.HasOperation
        && request.Payload is null
        && request.Config is null;

    private static bool InitializeMatches(
        AnimeGoPluginMetadata metadata,
        PluginInitializePayload? payload) =>
        payload is not null
        && !string.IsNullOrWhiteSpace(payload.HostVersion)
        && string.Equals(payload.PluginId, metadata.Id, StringComparison.Ordinal)
        && string.Equals(payload.PluginVersion, metadata.Version, StringComparison.Ordinal)
        && payload.ApiVersion == CurrentApiVersion
        && string.Equals(payload.Type, metadata.Type, StringComparison.Ordinal)
        && payload.Capabilities is not null
        && payload.Capabilities.SequenceEqual(metadata.Capabilities, StringComparer.Ordinal);

    private static bool TryDeserializeRequest(
        ReadOnlyMemory<byte> bytes,
        out PluginWireRequest request)
    {
        request = null!;
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            var nodes = 0;
            EnsureUniqueProperties(document.RootElement, ref nodes);
            request = JsonSerializer.Deserialize(
                document.RootElement,
                PluginSdkJsonContext.Default.PluginWireRequest)!;
            return request is not null;
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException or PluginInputException)
        {
            return false;
        }
    }

    private static bool TryDeserialize<T>(
        JsonElement? element,
        JsonTypeInfo<T> typeInfo,
        out T? value)
        where T : class
    {
        value = null;
        if (element is not { ValueKind: JsonValueKind.Object } objectElement)
        {
            return false;
        }
        try
        {
            value = JsonSerializer.Deserialize(objectElement, typeInfo);
            return value is not null;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static void EnsureUniqueProperties(JsonElement element, ref int nodes)
    {
        if (++nodes > 100_000)
        {
            throw new PluginInputException();
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureUniqueProperties(item, ref nodes);
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
                throw new PluginInputException();
            }
            EnsureUniqueProperties(property.Value, ref nodes);
        }
    }

    private static async Task<bool> WriteSuccessAsync(
        Stream output,
        string requestId,
        JsonElement result,
        int maximumBytes,
        CancellationToken cancellationToken) =>
        await WriteResponseAsync(
            output,
            new PluginWireResponse(
                CurrentApiVersion,
                requestId,
                true,
                result,
                null),
            maximumBytes,
            cancellationToken).ConfigureAwait(false);

    private static async Task<bool> WriteErrorAsync(
        Stream output,
        string requestId,
        string code,
        string message,
        int maximumBytes,
        CancellationToken cancellationToken) =>
        await WriteResponseAsync(
            output,
            new PluginWireResponse(
                CurrentApiVersion,
                requestId,
                false,
                null,
                new PluginWireError(code, message)),
            maximumBytes,
            cancellationToken).ConfigureAwait(false);

    private static async Task<bool> WriteResponseAsync(
        Stream output,
        PluginWireResponse response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            response,
            PluginSdkJsonContext.Default.PluginWireResponse);
        if (bytes.Length > maximumBytes)
        {
            return false;
        }
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(Newline, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static bool ValidRequestId(string? value) =>
        value is { Length: 32 }
        && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool ValidShutdownReason(string? value) =>
        value is { Length: >= 1 and <= 128 }
        && !string.IsNullOrWhiteSpace(value)
        && value.All(character => !char.IsControl(character));

    private static async Task WriteUnhandledAsync(
        TextWriter error,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await error.WriteLineAsync(
            $"Unhandled plugin handler exception: {exception.GetType().Name}")
            .ConfigureAwait(false);
        await error.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class PluginLineReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[4096];
        private int _offset;
        private int _count;

        public async Task<byte[]?> ReadLineAsync(
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            var line = new ArrayBufferWriter<byte>(Math.Min(maximumBytes, 4096));
            while (true)
            {
                if (_offset < _count)
                {
                    var newline = Array.IndexOf(
                        _buffer,
                        (byte)'\n',
                        _offset,
                        _count - _offset);
                    if (newline >= 0)
                    {
                        Append(line, _buffer.AsSpan(_offset, newline - _offset), maximumBytes);
                        _offset = newline + 1;
                        var length = line.WrittenCount;
                        if (length > 0 && line.WrittenSpan[length - 1] == (byte)'\r')
                        {
                            length--;
                        }
                        return line.WrittenSpan[..length].ToArray();
                    }
                    Append(line, _buffer.AsSpan(_offset, _count - _offset), maximumBytes);
                    _offset = _count;
                }

                _count = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
                _offset = 0;
                if (_count == 0)
                {
                    if (line.WrittenCount == 0)
                    {
                        return null;
                    }
                    throw new PluginInputException();
                }
            }
        }

        private static void Append(
            ArrayBufferWriter<byte> writer,
            ReadOnlySpan<byte> value,
            int maximumBytes)
        {
            if (value.Length > maximumBytes - writer.WrittenCount)
            {
                throw new PluginInputException();
            }
            writer.Write(value);
        }
    }

    private sealed class PluginInputException : Exception;
}

internal sealed record PluginWireRequest(
    [property: JsonPropertyName("apiVersion")] int ApiVersion,
    [property: JsonPropertyName("requestId")] string? RequestId,
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("operation")] JsonElement OperationValue,
    [property: JsonPropertyName("payload")] JsonElement? Payload,
    [property: JsonPropertyName("config")] JsonElement? Config)
{
    [JsonIgnore]
    public bool HasOperation => OperationValue.ValueKind != JsonValueKind.Undefined;

    [JsonIgnore]
    public string? Operation => OperationValue.ValueKind switch
    {
        JsonValueKind.String => OperationValue.GetString(),
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        _ => "\0invalid",
    };
}

internal sealed record PluginWireResponse(
    [property: JsonPropertyName("apiVersion")] int ApiVersion,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonElement? Result,
    [property: JsonPropertyName("error")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PluginWireError? Error);

internal sealed record PluginWireError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

internal sealed record PluginInitializePayload(
    [property: JsonPropertyName("hostVersion")] string? HostVersion,
    [property: JsonPropertyName("pluginId")] string? PluginId,
    [property: JsonPropertyName("pluginVersion")] string? PluginVersion,
    [property: JsonPropertyName("apiVersion")] int ApiVersion,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string>? Capabilities);

internal sealed record PluginInitializeResult(
    [property: JsonPropertyName("pluginId")] string PluginId,
    [property: JsonPropertyName("pluginVersion")] string PluginVersion,
    [property: JsonPropertyName("apiVersion")] int ApiVersion,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities);

internal sealed record PluginHealthResult(
    [property: JsonPropertyName("healthy")] bool Healthy);

internal sealed record PluginShutdownPayload(
    [property: JsonPropertyName("reason")] string? Reason);

internal sealed record PluginShutdownResult(
    [property: JsonPropertyName("accepted")] bool Accepted);

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PluginWireRequest))]
[JsonSerializable(typeof(PluginWireResponse))]
[JsonSerializable(typeof(PluginInitializePayload))]
[JsonSerializable(typeof(PluginInitializeResult))]
[JsonSerializable(typeof(PluginHealthResult))]
[JsonSerializable(typeof(PluginShutdownPayload))]
[JsonSerializable(typeof(PluginShutdownResult))]
internal sealed partial class PluginSdkJsonContext : JsonSerializerContext;
