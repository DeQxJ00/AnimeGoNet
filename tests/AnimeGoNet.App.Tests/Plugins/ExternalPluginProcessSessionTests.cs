using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnimeGoNet.App.Plugins;

namespace AnimeGoNet.App.Tests.Plugins;

public sealed class ExternalPluginProcessSessionTests
{
    [Fact]
    public async Task InitializeExecuteHealthAndShutdownUseOneCorrelatedProcess()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var factory = new ScriptedProcessFactory(DefaultHandler(fixture.Manifest));
        await using var session = fixture.CreateSession(factory);

        await session.StartAsync("1.0.0");
        var result = await session.ExecuteAsync(
            "filter.all",
            Json("{\"items\":[1,2]}"),
            Json("{\"enabled\":true}"));
        var healthy = await session.HealthAsync();
        await session.ShutdownAsync("test_complete");

        Assert.True(result.GetProperty("accepted").GetBoolean());
        Assert.Equal("filter.all", result.GetProperty("operation").GetString());
        Assert.True(healthy);
        Assert.Equal(ExternalPluginSessionState.Stopped, session.State);
        Assert.False(factory.Process!.WasKilled);
        Assert.Equal(
            ["initialize", "execute", "health", "shutdown"],
            factory.Process.Requests.Select(request => request["method"]!.GetValue<string>()));
        Assert.All(factory.Process.Requests, request =>
            Assert.Equal(1, request["apiVersion"]!.GetValue<int>()));
        Assert.Equal(
            factory.Process.Requests.Count,
            factory.Process.Requests.Select(request => request["requestId"]!.GetValue<string>())
                .Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task RemoteBusinessErrorDoesNotPoisonHealthySession()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var fallback = DefaultHandler(fixture.Manifest);
        var factory = new ScriptedProcessFactory(async (request, index, cancellationToken) =>
        {
            if (request["method"]!.GetValue<string>() == "execute")
            {
                return Reply.Error(request, "invalid_config", "resolution is required");
            }
            return await fallback(request, index, cancellationToken);
        });
        await using var session = fixture.CreateSession(factory);
        await session.StartAsync("1.0.0");

        var error = await Assert.ThrowsAsync<ExternalPluginRemoteException>(() =>
            session.ExecuteAsync("filter.all", Json("{}"), Json("{}")));

        Assert.Equal("invalid_config", error.Code);
        Assert.Equal("resolution is required", error.Message);
        Assert.Equal(ExternalPluginSessionState.Ready, session.State);
        Assert.True(await session.HealthAsync());
        await session.ShutdownAsync();
    }

    [Theory]
    [InlineData("request_id", "plugin_response_request_id_mismatch")]
    [InlineData("api_version", "plugin_response_api_version_mismatch")]
    [InlineData("dirty_stdout", "plugin_response_json_invalid")]
    [InlineData("unknown_field", "plugin_response_unknown_field")]
    [InlineData("duplicate_field", "plugin_response_duplicate_field")]
    [InlineData("identity", "plugin_initialize_identity_mismatch")]
    [InlineData("result_unknown", "plugin_initialize_result_invalid")]
    public async Task InvalidInitializeResponseFaultsAndKillsProcess(
        string fault,
        string expectedCode)
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var factory = new ScriptedProcessFactory((request, _, _) =>
            Task.FromResult(InvalidInitializeReply(request, fixture.Manifest, fault)));
        await using var session = fixture.CreateSession(factory);

        var error = await Assert.ThrowsAsync<ExternalPluginProtocolException>(
            () => session.StartAsync("1.0.0"));

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(ExternalPluginSessionState.Faulted, session.State);
        Assert.True(factory.Process!.WasKilled);
    }

    [Fact]
    public async Task OversizedResponseFaultsBeforeJsonAllocationCanGrowUnbounded()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var factory = new ScriptedProcessFactory((request, _, _) => Task.FromResult(
            new Reply(new string('x', 1025), ExitAfterResponse: false)));
        await using var session = fixture.CreateSession(
            factory,
            new ExternalPluginSessionOptions { MaximumResponseBytes = 1024 });

        var error = await Assert.ThrowsAsync<ExternalPluginProtocolException>(
            () => session.StartAsync("1.0.0"));

        Assert.Equal("plugin_response_too_large", error.Code);
        Assert.True(factory.Process!.WasKilled);
    }

    [Fact]
    public async Task ExecuteTimeoutCancelsAndKillsHungProcess()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var fallback = DefaultHandler(fixture.Manifest);
        var factory = new ScriptedProcessFactory(async (request, index, cancellationToken) =>
        {
            if (request["method"]!.GetValue<string>() == "execute")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return await fallback(request, index, cancellationToken);
        });
        await using var session = fixture.CreateSession(
            factory,
            new ExternalPluginSessionOptions { ExecuteTimeout = TimeSpan.FromMilliseconds(50) });
        await session.StartAsync("1.0.0");

        var error = await Assert.ThrowsAsync<ExternalPluginProtocolException>(() =>
            session.ExecuteAsync("filter.all", Json("{}"), Json("{}")));

        Assert.Equal("plugin_call_timeout", error.Code);
        Assert.Equal(ExternalPluginSessionState.Faulted, session.State);
        Assert.True(factory.Process!.WasKilled);
    }

    [Fact]
    public async Task CallerCancellationPropagatesAndKillsProcess()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var fallback = DefaultHandler(fixture.Manifest);
        var factory = new ScriptedProcessFactory(async (request, index, cancellationToken) =>
        {
            if (request["method"]!.GetValue<string>() == "execute")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return await fallback(request, index, cancellationToken);
        });
        await using var session = fixture.CreateSession(factory);
        await session.StartAsync("1.0.0");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.ExecuteAsync(
                "filter.all",
                Json("{}"),
                Json("{}"),
                cancellationToken: cancellation.Token));

        Assert.Equal(ExternalPluginSessionState.Faulted, session.State);
        Assert.True(factory.Process!.WasKilled);
    }

    [Fact]
    public async Task ProcessExitBeforeResponseFaultsOnlyItsSession()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var fallback = DefaultHandler(fixture.Manifest);
        var factory = new ScriptedProcessFactory(async (request, index, cancellationToken) =>
        {
            if (request["method"]!.GetValue<string>() == "execute")
            {
                return new Reply(Line: null, ExitAfterResponse: true);
            }
            return await fallback(request, index, cancellationToken);
        });
        await using var session = fixture.CreateSession(factory);
        await session.StartAsync("1.0.0");

        var error = await Assert.ThrowsAsync<ExternalPluginProtocolException>(() =>
            session.ExecuteAsync("filter.all", Json("{}"), Json("{}")));

        Assert.Equal("plugin_process_exited", error.Code);
        Assert.Equal(ExternalPluginSessionState.Faulted, session.State);
        Assert.True(factory.Process!.WasKilled);
    }

    [Fact]
    public async Task OversizedHostRequestIsRejectedWithoutSendingOrKillingHealthyPlugin()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var factory = new ScriptedProcessFactory(DefaultHandler(fixture.Manifest));
        await using var session = fixture.CreateSession(
            factory,
            new ExternalPluginSessionOptions { MaximumRequestBytes = 1024 });
        await session.StartAsync("1.0.0");
        var before = factory.Process!.Requests.Count;

        var error = await Assert.ThrowsAsync<ExternalPluginProtocolException>(() =>
            session.ExecuteAsync(
                "filter.all",
                Json("{\"value\":\"" + new string('a', 1500) + "\"}"),
                Json("{}")));

        Assert.Equal("plugin_request_too_large", error.Code);
        Assert.Equal(ExternalPluginSessionState.Ready, session.State);
        Assert.False(factory.Process.WasKilled);
        Assert.Equal(before, factory.Process.Requests.Count);
        Assert.True(await session.HealthAsync());
        await session.ShutdownAsync();
    }

    [Fact]
    public async Task StderrIsDrainedWithoutBeingAcceptedAsProtocolOutput()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var fallback = DefaultHandler(fixture.Manifest);
        var factory = new ScriptedProcessFactory(async (request, index, cancellationToken) =>
        {
            var response = await fallback(request, index, cancellationToken);
            return request["method"]!.GetValue<string>() == "execute"
                ? response with { StderrBytes = 256 * 1024 }
                : response;
        });
        await using var session = fixture.CreateSession(factory);
        await session.StartAsync("1.0.0");

        var result = await session.ExecuteAsync("filter.all", Json("{}"), Json("{}"));

        Assert.True(result.GetProperty("accepted").GetBoolean());
        Assert.Equal(ExternalPluginSessionState.Ready, session.State);
        await session.ShutdownAsync();
    }

    [Fact]
    public async Task UnhealthyResponseFaultsAndKillsProcess()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var fallback = DefaultHandler(fixture.Manifest);
        var factory = new ScriptedProcessFactory(async (request, index, cancellationToken) =>
        {
            if (request["method"]!.GetValue<string>() == "health")
            {
                return Reply.Success(request, new JsonObject { ["healthy"] = false });
            }
            return await fallback(request, index, cancellationToken);
        });
        await using var session = fixture.CreateSession(factory);
        await session.StartAsync("1.0.0");

        Assert.False(await session.HealthAsync());
        Assert.Equal(ExternalPluginSessionState.Faulted, session.State);
        Assert.True(factory.Process!.WasKilled);
    }

    [Fact]
    public async Task HealthErrorResponseFaultsAndKillsProcess()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var fallback = DefaultHandler(fixture.Manifest);
        var factory = new ScriptedProcessFactory(async (request, index, cancellationToken) =>
        {
            if (request["method"]!.GetValue<string>() == "health")
            {
                return Reply.Error(request, "health_failed", "fixture is unavailable");
            }
            return await fallback(request, index, cancellationToken);
        });
        await using var session = fixture.CreateSession(factory);
        await session.StartAsync("1.0.0");

        var error = await Assert.ThrowsAsync<ExternalPluginRemoteException>(
            () => session.HealthAsync());

        Assert.Equal("health_failed", error.Code);
        Assert.Equal(ExternalPluginSessionState.Faulted, session.State);
        Assert.True(factory.Process!.WasKilled);
    }

    [Fact]
    public async Task ConcurrentExecuteCallsAreSerializedOnSingleJsonLineChannel()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var fallback = DefaultHandler(fixture.Manifest);
        var active = 0;
        var maximumActive = 0;
        var factory = new ScriptedProcessFactory(async (request, index, cancellationToken) =>
        {
            if (request["method"]!.GetValue<string>() == "execute")
            {
                var current = Interlocked.Increment(ref active);
                maximumActive = Math.Max(maximumActive, current);
                await Task.Delay(20, cancellationToken);
                Interlocked.Decrement(ref active);
            }
            return await fallback(request, index, cancellationToken);
        });
        await using var session = fixture.CreateSession(factory);
        await session.StartAsync("1.0.0");

        var first = session.ExecuteAsync("filter.all", Json("{\"n\":1}"), Json("{}"));
        var second = session.ExecuteAsync("filter.all", Json("{\"n\":2}"), Json("{}"));
        await Task.WhenAll(first, second);

        Assert.Equal(1, maximumActive);
        Assert.Equal(2, factory.Process!.Requests.Count(request =>
            request["method"]!.GetValue<string>() == "execute"));
        await session.ShutdownAsync();
    }

    [Fact]
    public async Task ShutdownAcknowledgementWithoutExitIsKilledAfterDeadline()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var fallback = DefaultHandler(fixture.Manifest);
        var factory = new ScriptedProcessFactory(async (request, index, cancellationToken) =>
        {
            var reply = await fallback(request, index, cancellationToken);
            return request["method"]!.GetValue<string>() == "shutdown"
                ? reply with { ExitAfterResponse = false, HangAfterResponse = true }
                : reply;
        });
        await using var session = fixture.CreateSession(
            factory,
            new ExternalPluginSessionOptions { ShutdownTimeout = TimeSpan.FromMilliseconds(50) });
        await session.StartAsync("1.0.0");

        var error = await Assert.ThrowsAsync<ExternalPluginProtocolException>(
            () => session.ShutdownAsync());

        Assert.Equal("plugin_shutdown_timeout", error.Code);
        Assert.Equal(ExternalPluginSessionState.Faulted, session.State);
        Assert.True(factory.Process!.WasKilled);
    }

    [Fact]
    public async Task ManifestMutationBetweenDiscoveryAndStartPreventsProcessCreation()
    {
        using var fixture = await ProtocolFixture.CreateAsync();
        var factory = new ScriptedProcessFactory(DefaultHandler(fixture.Manifest));
        await File.WriteAllTextAsync(
            fixture.Package.ManifestPath,
            fixture.ManifestJson(version: "2.0.0"));
        await using var session = fixture.CreateSession(factory);

        var error = await Assert.ThrowsAsync<ExternalPluginProtocolException>(
            () => session.StartAsync("1.0.0"));

        Assert.Equal("plugin_manifest_changed", error.Code);
        Assert.Null(factory.Process);
        Assert.Equal(ExternalPluginSessionState.Faulted, session.State);
    }

    private static Func<JsonObject, int, CancellationToken, Task<Reply>> DefaultHandler(
        ExternalPluginManifest manifest) =>
        (request, _, _) =>
        {
            var method = request["method"]!.GetValue<string>();
            return Task.FromResult(method switch
            {
                "initialize" => Reply.Success(request, new JsonObject
                {
                    ["pluginId"] = manifest.Id,
                    ["pluginVersion"] = manifest.Version,
                    ["apiVersion"] = manifest.ApiVersion,
                    ["type"] = manifest.Type,
                    ["capabilities"] = new JsonArray(
                        manifest.Capabilities.Select(value =>
                            (JsonNode?)JsonValue.Create(value)).ToArray()),
                }),
                "execute" => Reply.Success(request, new JsonObject
                {
                    ["accepted"] = true,
                    ["operation"] = request["operation"]!.GetValue<string>(),
                }),
                "health" => Reply.Success(request, new JsonObject { ["healthy"] = true }),
                "shutdown" => Reply.Success(
                    request,
                    new JsonObject(),
                    exitAfterResponse: true),
                _ => Reply.Error(request, "method_unknown", "unknown method"),
            });
        };

    private static Reply InvalidInitializeReply(
        JsonObject request,
        ExternalPluginManifest manifest,
        string fault)
    {
        var requestId = request["requestId"]!.GetValue<string>();
        var result = new JsonObject
        {
            ["pluginId"] = fault == "identity" ? "com.example.forged" : manifest.Id,
            ["pluginVersion"] = manifest.Version,
            ["apiVersion"] = manifest.ApiVersion,
            ["type"] = manifest.Type,
            ["capabilities"] = new JsonArray(),
        };
        if (fault == "result_unknown") result["unknown"] = true;
        return fault switch
        {
            "request_id" => Reply.SuccessRaw(
                "ffffffffffffffffffffffffffffffff",
                result),
            "api_version" => new Reply(
                new JsonObject
                {
                    ["apiVersion"] = 2,
                    ["requestId"] = requestId,
                    ["ok"] = true,
                    ["result"] = result,
                }.ToJsonString(),
                false),
            "dirty_stdout" => new Reply("plugin log on stdout", false),
            "unknown_field" => new Reply(
                $"{{\"apiVersion\":1,\"requestId\":\"{requestId}\",\"ok\":true,\"result\":{result.ToJsonString()},\"extra\":true}}",
                false),
            "duplicate_field" => new Reply(
                $"{{\"apiVersion\":1,\"requestId\":\"{requestId}\",\"requestId\":\"{requestId}\",\"ok\":true,\"result\":{result.ToJsonString()}}}",
                false),
            _ => Reply.Success(request, result),
        };
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private sealed record Reply(
        string? Line,
        bool ExitAfterResponse,
        int StderrBytes = 0,
        bool HangAfterResponse = false)
    {
        public static Reply Success(
            JsonObject request,
            JsonNode result,
            bool exitAfterResponse = false) =>
            SuccessRaw(
                request["requestId"]!.GetValue<string>(),
                result,
                exitAfterResponse);

        public static Reply SuccessRaw(
            string requestId,
            JsonNode result,
            bool exitAfterResponse = false) =>
            new(
                new JsonObject
                {
                    ["apiVersion"] = 1,
                    ["requestId"] = requestId,
                    ["ok"] = true,
                    ["result"] = result,
                }.ToJsonString(),
                exitAfterResponse);

        public static Reply Error(JsonObject request, string code, string message) =>
            new(
                new JsonObject
                {
                    ["apiVersion"] = 1,
                    ["requestId"] = request["requestId"]!.GetValue<string>(),
                    ["ok"] = false,
                    ["error"] = new JsonObject
                    {
                        ["code"] = code,
                        ["message"] = message,
                    },
                }.ToJsonString(),
                false);
    }

    private sealed class ScriptedProcessFactory(
        Func<JsonObject, int, CancellationToken, Task<Reply>> handler)
        : IExternalPluginProcessFactory
    {
        public ScriptedProcess? Process { get; private set; }

        public IExternalPluginProcess Start(
            ExternalPluginPackage package,
            string pluginDataPath)
        {
            Assert.Null(Process);
            Process = new ScriptedProcess(handler);
            return Process;
        }
    }

    private sealed class ScriptedProcess : IExternalPluginProcess
    {
        private readonly Pipe _requests = new();
        private readonly Pipe _responses = new();
        private readonly Pipe _errors = new();
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Task _run;
        private readonly Stream _standardInput;
        private readonly Stream _standardOutput;
        private readonly Stream _standardError;
        private int _exitCode;

        public ScriptedProcess(Func<JsonObject, int, CancellationToken, Task<Reply>> handler)
        {
            _standardInput = _requests.Writer.AsStream();
            _standardOutput = _responses.Reader.AsStream();
            _standardError = _errors.Reader.AsStream();
            _run = RunAsync(handler, _lifetime.Token);
        }

        public List<JsonObject> Requests { get; } = [];

        public bool WasKilled { get; private set; }

        public Stream StandardInput => _standardInput;

        public Stream StandardOutput => _standardOutput;

        public Stream StandardError => _standardError;

        public bool HasExited => _run.IsCompleted;

        public int? ExitCode => HasExited ? _exitCode : null;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _run.WaitAsync(cancellationToken);

        public void Kill()
        {
            WasKilled = true;
            _exitCode = -1;
            _lifetime.Cancel();
        }

        public async ValueTask DisposeAsync()
        {
            Kill();
            try
            {
                await _run.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            _standardInput.Dispose();
            _standardOutput.Dispose();
            _standardError.Dispose();
            _lifetime.Dispose();
        }

        private async Task RunAsync(
            Func<JsonObject, int, CancellationToken, Task<Reply>> handler,
            CancellationToken cancellationToken)
        {
            await using var requestStream = _requests.Reader.AsStream();
            await using var responseStream = _responses.Writer.AsStream();
            await using var errorStream = _errors.Writer.AsStream();
            using var reader = new StreamReader(
                requestStream,
                new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);
            using var writer = new StreamWriter(
                responseStream,
                new UTF8Encoding(false),
                bufferSize: 1024,
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            try
            {
                var index = 0;
                while (true)
                {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null) break;
                    var request = JsonNode.Parse(line)!.AsObject();
                    Requests.Add(request);
                    var reply = await handler(request, index++, cancellationToken)
                        .ConfigureAwait(false);
                    if (reply.StderrBytes > 0)
                    {
                        var bytes = new byte[4096];
                        var remaining = reply.StderrBytes;
                        while (remaining > 0)
                        {
                            var count = Math.Min(remaining, bytes.Length);
                            await errorStream.WriteAsync(
                                bytes.AsMemory(0, count),
                                cancellationToken).ConfigureAwait(false);
                            remaining -= count;
                        }
                        await errorStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    if (reply.Line is not null)
                    {
                        await writer.WriteLineAsync(reply.Line.AsMemory(), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    if (reply.HangAfterResponse)
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    if (reply.ExitAfterResponse) break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _exitCode = -1;
            }
            finally
            {
                await _responses.Writer.CompleteAsync().ConfigureAwait(false);
                await _errors.Writer.CompleteAsync().ConfigureAwait(false);
                await _requests.Reader.CompleteAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class ProtocolFixture : IDisposable
    {
        private long _requestNumber;

        private ProtocolFixture(
            string rootPath,
            ExternalPluginManifestLoader loader,
            ExternalPluginPackage package)
        {
            RootPath = rootPath;
            Loader = loader;
            Package = package;
            Manifest = package.Manifest;
        }

        public string RootPath { get; }

        public ExternalPluginManifestLoader Loader { get; }

        public ExternalPluginPackage Package { get; }

        public ExternalPluginManifest Manifest { get; }

        public static async Task<ProtocolFixture> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "animegonet-plugin-protocol-tests",
                Guid.NewGuid().ToString("N"));
            var plugins = Path.Combine(root, "plugins");
            var package = Path.Combine(plugins, "fixture");
            Directory.CreateDirectory(package);
            var rid = CurrentRid();
            var entryName = OperatingSystem.IsWindows() ? "Plugin.exe" : "Plugin";
            var entry = Path.Combine(package, entryName);
            await File.WriteAllBytesAsync(entry, [0x00]);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    entry,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            await File.WriteAllTextAsync(
                Path.Combine(package, "config.schema.json"),
                "{\"type\":\"object\"}");
            var manifest = ManifestJson(rid, entryName, "1.0.0");
            await File.WriteAllTextAsync(Path.Combine(package, "plugin.json"), manifest);
            var loader = new ExternalPluginManifestLoader(plugins, rid);
            var loaded = await loader.LoadPackageAsync(package);
            return new ProtocolFixture(root, loader, loaded);
        }

        public ExternalPluginProcessSession CreateSession(
            IExternalPluginProcessFactory factory,
            ExternalPluginSessionOptions? options = null) =>
            new(
                Loader,
                Package,
                Path.Combine(RootPath, "plugin-data"),
                options,
                factory,
                () => Interlocked.Increment(ref _requestNumber)
                    .ToString("x32", CultureInfo.InvariantCulture));

        public string ManifestJson(string version) =>
            ManifestJson(Manifest.Rid, Manifest.EntryPoint, version);

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static string ManifestJson(string rid, string entryName, string version) =>
            new JsonObject
            {
                ["id"] = "com.example.protocol-fixture",
                ["name"] = "Protocol fixture",
                ["version"] = version,
                ["apiVersion"] = 1,
                ["type"] = "filter",
                ["rid"] = rid,
                ["entryPoint"] = entryName,
                ["configSchema"] = "config.schema.json",
                ["capabilities"] = new JsonArray(),
            }.ToJsonString();

        private static string CurrentRid()
        {
            var architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException(),
            };
            if (OperatingSystem.IsWindows()) return $"win-{architecture}";
            if (OperatingSystem.IsLinux()) return $"linux-{architecture}";
            if (OperatingSystem.IsMacOS() && architecture == "arm64") return "osx-arm64";
            throw new PlatformNotSupportedException();
        }
    }
}
