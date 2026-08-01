using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sdk;
using Xunit;

namespace AnimeGo.Plugin.Sdk.Tests;

public sealed class AnimeGoPluginHostTests
{
    private const string PluginId = "com.example.filter";
    private const string Request1 = "00000000000000000000000000000001";
    private const string Request2 = "00000000000000000000000000000002";
    private const string Request3 = "00000000000000000000000000000003";
    private const string Request4 = "00000000000000000000000000000004";

    [Fact]
    public async Task RunsTypedLifecycleAndRetainsRawArgumentsAndConfiguration()
    {
        var handler = new RecordingHandler();
        var result = await RunAsync(
            handler,
            Initialize()
            + Execute("""
                {"sourceProfileId":"mikan-main","items":[],"arguments":{},"sourceProfileSnapshot":null,"customArg":"configured"}
                """, "{\"token\":\"local-fixture\"}")
            + Health()
            + Shutdown());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.Equal(4, result.Responses.Length);
        Assert.All(result.Responses, response => Assert.True(response.RootElement.GetProperty("ok").GetBoolean()));
        Assert.Equal(PluginId, result.Responses[0].RootElement.GetProperty("result").GetProperty("pluginId").GetString());
        Assert.True(result.Responses[2].RootElement.GetProperty("result").GetProperty("healthy").GetBoolean());
        Assert.True(result.Responses[3].RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
        Assert.NotNull(handler.Context);
        Assert.Equal("mikan-main", handler.Context.Request.SourceProfileId);
        Assert.Equal("configured", handler.Context.RawPayload.GetProperty("customArg").GetString());
        Assert.Equal("local-fixture", handler.Context.Config.GetProperty("token").GetString());
        Assert.Equal(Path.GetFullPath("plugin-data"), handler.Context.PluginDataPath);
    }

    [Fact]
    public async Task BusinessErrorKeepsSessionHealthy()
    {
        var result = await RunAsync(
            new BusinessErrorHandler(),
            Initialize() + Execute(ValidPayload()) + Health() + Shutdown());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(4, result.Responses.Length);
        var failure = result.Responses[1].RootElement;
        Assert.False(failure.GetProperty("ok").GetBoolean());
        Assert.Equal("fixture_rejected", failure.GetProperty("error").GetProperty("code").GetString());
        Assert.True(result.Responses[2].RootElement.GetProperty("result").GetProperty("healthy").GetBoolean());
    }

    [Fact]
    public async Task InitializeIdentityMismatchReturnsStableErrorAndExitCode()
    {
        var result = await RunAsync(
            new RecordingHandler(),
            Initialize(pluginId: "com.example.other"));

        Assert.Equal(21, result.ExitCode);
        var response = Assert.Single(result.Responses).RootElement;
        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Equal("plugin_initialize_invalid", response.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExplicitNullOperationIsNotTreatedAsAnAbsentField()
    {
        var validInitialize = Initialize();
        var initialize = validInitialize[..^2] + ",\"operation\":null}\n";
        var result = await RunAsync(new RecordingHandler(), initialize);

        Assert.Equal(21, result.ExitCode);
        Assert.Single(result.Responses);
        Assert.False(result.Responses[0].RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task EndOfInputAfterInitializeRequiresShutdown()
    {
        var result = await RunAsync(new RecordingHandler(), Initialize());

        Assert.Equal(20, result.ExitCode);
        Assert.Single(result.Responses);
        Assert.True(result.Responses[0].RootElement.GetProperty("ok").GetBoolean());
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task InvalidWireInputExitsWithoutWritingProtocolResponse(string input)
    {
        var result = await RunAsync(new RecordingHandler(), input);

        Assert.Equal(20, result.ExitCode);
        Assert.Empty(result.Responses);
    }

    public static TheoryData<string> InvalidInputs => new()
    {
        "{not-json}\n",
        "{\"apiVersion\":1,\"apiVersion\":1,\"requestId\":\"00000000000000000000000000000001\",\"method\":\"health\"}\n",
        new string('x', 1025) + "\n",
        "{\"apiVersion\":1",
    };

    [Fact]
    public async Task InvalidShutdownReasonIsProtocolFailure()
    {
        var result = await RunAsync(
            new RecordingHandler(),
            Initialize() + Line(Request2, "shutdown", null, "{\"reason\":\"\"}", null));

        Assert.Equal(20, result.ExitCode);
        Assert.Single(result.Responses);
    }

    [Fact]
    public async Task UnexpectedHandlerExceptionDoesNotLeakItsMessage()
    {
        const string secret = "do-not-leak-this-secret";
        var result = await RunAsync(
            new ThrowingHandler(secret),
            Initialize() + Execute(ValidPayload()));

        Assert.Equal(30, result.ExitCode);
        Assert.Single(result.Responses);
        Assert.Contains(nameof(InvalidOperationException), result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResultSerializationExceptionDoesNotLeakItsMessage()
    {
        const string secret = "do-not-leak-result-secret";
        var result = await RunAsync(
            new ThrowingResultHandler(secret),
            Initialize() + Execute(ValidPayload()));

        Assert.Equal(30, result.ExitCode);
        Assert.Single(result.Responses);
        Assert.Contains(nameof(InvalidOperationException), result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedResultReturnsDedicatedExitCode()
    {
        var result = await RunAsync(
            new LargeResultHandler(),
            Initialize() + Execute(ValidPayload()),
            new AnimeGoPluginHostOptions
            {
                MaximumRequestBytes = 1024 * 1024,
                MaximumResponseBytes = 1024,
            });

        Assert.Equal(31, result.ExitCode);
        Assert.Single(result.Responses);
    }

    [Fact]
    public async Task CancellationInterruptsPendingInput()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await AnimeGoPluginHost.RunAsync(
                Metadata(),
                PluginCategory.Filter,
                new RecordingHandler(),
                PluginTestJsonContext.Default.FilterContext,
                PluginTestJsonContext.Default.FilterResult,
                new CancellationStream(),
                new MemoryStream(),
                TextWriter.Null,
                Environment(),
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void RejectsInvalidMetadataAndHostLimits()
    {
        var metadata = Metadata() with { Id = "not-a-reverse-domain" };
        Assert.Throws<ArgumentException>(() => metadata.Validate(PluginCategory.Filter));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnimeGoPluginHostOptions { MaximumRequestBytes = 100 }.Validate());
    }

    [Fact]
    public void MetadataValidationMatchesManifestIdentityRules()
    {
        var valid = new AnimeGoPluginMetadata(
            "com.example.valid-plugin",
            "1.2.3-beta.1+build.01",
            PluginCategory.Filter,
            ["1.feature", "metadata_read"]);
        valid.Validate(PluginCategory.Filter);

        Assert.Throws<ArgumentException>(() =>
            (valid with { Id = "com.example.invalid_plugin" }).Validate(PluginCategory.Filter));
        Assert.Throws<ArgumentException>(() =>
            (valid with { Version = "01.2.3" }).Validate(PluginCategory.Filter));
        Assert.Throws<ArgumentException>(() =>
            (valid with { Version = "1.2" }).Validate(PluginCategory.Filter));
        Assert.Throws<ArgumentException>(() =>
            (valid with { Capabilities = ["metadata..read"] }).Validate(PluginCategory.Filter));
    }

    private static async Task<RunResult> RunAsync(
        IFilterPluginHandler handler,
        string input,
        AnimeGoPluginHostOptions? options = null)
    {
        await using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        await using var outputStream = new MemoryStream();
        using var error = new StringWriter();
        var exitCode = await AnimeGoPluginHost.RunAsync(
            Metadata(),
            PluginCategory.Filter,
            handler,
            PluginTestJsonContext.Default.FilterContext,
            PluginTestJsonContext.Default.FilterResult,
            inputStream,
            outputStream,
            error,
            Environment(),
            options);
        var output = Encoding.UTF8.GetString(outputStream.ToArray());
        return new RunResult(exitCode, output, error.ToString(), ParseLines(output));
    }

    private static JsonDocument[] ParseLines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line))
            .ToArray();

    private static AnimeGoPluginMetadata Metadata() =>
        new(PluginId, "1.0.0", PluginCategory.Filter, ["filter.all"]);

    private static AnimeGoPluginHostEnvironment Environment() =>
        new(PluginId, "1", "plugin-data");

    private static string Initialize(string pluginId = PluginId) => Line(
        Request1,
        "initialize",
        null,
        $$"""
        {"hostVersion":"1.0.0","pluginId":"{{pluginId}}","pluginVersion":"1.0.0","apiVersion":1,"type":"filter","capabilities":["filter.all"]}
        """,
        null);

    private static string Execute(string payload, string config = "{}") =>
        Line(Request2, "execute", "filter.all", payload, config);

    private static string Health() => Line(Request3, "health", null, null, null);

    private static string Shutdown() =>
        Line(Request4, "shutdown", null, "{\"reason\":\"host_shutdown\"}", null);

    private static string ValidPayload() =>
        "{\"sourceProfileId\":\"mikan-main\",\"items\":[],\"arguments\":{},\"sourceProfileSnapshot\":null}";

    private static string Line(
        string requestId,
        string method,
        string? operation,
        string? payload,
        string? config)
    {
        var operationPart = operation is null ? string.Empty : $",\"operation\":\"{operation}\"";
        var payloadPart = payload is null ? string.Empty : $",\"payload\":{payload}";
        var configPart = config is null ? string.Empty : $",\"config\":{config}";
        return $"{{\"apiVersion\":1,\"requestId\":\"{requestId}\",\"method\":\"{method}\"{operationPart}{payloadPart}{configPart}}}\n";
    }

    private sealed class RecordingHandler : IFilterPluginHandler
    {
        public AnimeGoPluginExecutionContext<FilterContext>? Context { get; private set; }

        public ValueTask<FilterResult> ExecuteAsync(
            AnimeGoPluginExecutionContext<FilterContext> context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Context = context;
            return ValueTask.FromResult(new FilterResult([], [], new Dictionary<string, string>()));
        }
    }

    private sealed class BusinessErrorHandler : IFilterPluginHandler
    {
        public ValueTask<FilterResult> ExecuteAsync(
            AnimeGoPluginExecutionContext<FilterContext> context,
            CancellationToken cancellationToken) =>
            throw new AnimeGoPluginExecutionException("fixture_rejected", "Fixture was rejected.");
    }

    private sealed class ThrowingHandler(string message) : IFilterPluginHandler
    {
        public ValueTask<FilterResult> ExecuteAsync(
            AnimeGoPluginExecutionContext<FilterContext> context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);
    }

    private sealed class LargeResultHandler : IFilterPluginHandler
    {
        public ValueTask<FilterResult> ExecuteAsync(
            AnimeGoPluginExecutionContext<FilterContext> context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new FilterResult(
                [],
                [],
                new Dictionary<string, string> { ["large"] = new string('x', 2048) }));
    }

    private sealed class ThrowingResultHandler(string message) : IFilterPluginHandler
    {
        public ValueTask<FilterResult> ExecuteAsync(
            AnimeGoPluginExecutionContext<FilterContext> context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new FilterResult([], [], new ThrowingDictionary(message)));
    }

    private sealed class ThrowingDictionary(string message) : IReadOnlyDictionary<string, string>
    {
        public int Count => 1;
        public IEnumerable<string> Keys => throw new InvalidOperationException(message);
        public IEnumerable<string> Values => throw new InvalidOperationException(message);
        public string this[string key] => throw new InvalidOperationException(message);
        public bool ContainsKey(string key) => throw new InvalidOperationException(message);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            throw new InvalidOperationException(message);
        public bool TryGetValue(string key, out string value) =>
            throw new InvalidOperationException(message);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private sealed class CancellationStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromCanceled<int>(cancellationToken);
    }

    private sealed record RunResult(
        int ExitCode,
        string Output,
        string Error,
        JsonDocument[] Responses);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(FilterContext))]
[JsonSerializable(typeof(FilterResult))]
internal sealed partial class PluginTestJsonContext : JsonSerializerContext;
