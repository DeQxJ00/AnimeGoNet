using System.Text.Json;
using AnimeGoNet.App.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Plugins;

public sealed class ExternalPluginHostManagerTests
{
    [Fact]
    public async Task ApplicationRegistersLazyManagerAndSeparatePluginDataRoot()
    {
        await using var app = await RunningApp.StartAsync();

        var manager = app.App.Services.GetRequiredService<ExternalPluginHostManager>();

        Assert.Empty(manager.PluginIds);
        Assert.Empty(manager.GetSnapshots());
        Assert.True(Directory.Exists(Path.Combine(app.RootPath, "data", "plugins")));
        Assert.True(Directory.Exists(Path.Combine(app.RootPath, "data", "plugin-data")));
    }

    [Fact]
    public async Task SuccessfulCallsStartLazilyReuseOneSessionAndStayHealthy()
    {
        using var fixture = new ManagerFixture("filter-one");
        var session = new FakeSession();
        fixture.Factory.Enqueue("com.example.filter-one", session);
        await using var manager = fixture.CreateManager();

        var first = await manager.ExecuteAsync(
            "com.example.filter-one",
            "filter.all",
            Json("{}"),
            Json("{}"));
        var second = await manager.ExecuteAsync(
            "com.example.filter-one",
            "filter.all",
            Json("{}"),
            Json("{}"));

        Assert.True(first.GetProperty("accepted").GetBoolean());
        Assert.True(second.GetProperty("accepted").GetBoolean());
        Assert.Equal(1, fixture.Factory.CreateCount("com.example.filter-one"));
        Assert.Equal(1, session.StartCount);
        Assert.Equal(2, session.ExecuteCount);
        Assert.Equal("9.8.7", session.HostVersion);
        Assert.Equal(
            Path.Combine(fixture.PluginDataRoot, "com.example.filter-one"),
            fixture.Factory.DataPaths.Single());
        Assert.Equal(
            new ExternalPluginRuntimeSnapshot(
                "com.example.filter-one",
                ExternalPluginRuntimeState.Ready,
                0,
                null,
                null),
            manager.GetSnapshot("com.example.filter-one"));
    }

    [Fact]
    public async Task FailuresBackOffExponentiallyAutoDisableAndRequireExplicitReset()
    {
        var now = new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);
        using var fixture = new ManagerFixture("unstable", "healthy", now);
        fixture.Factory.Enqueue(
            "com.example.unstable",
            FailingExecute("plugin_process_exited"),
            FailingExecute("plugin_call_timeout"),
            FailingExecute("plugin_response_json_invalid"),
            new FakeSession());
        fixture.Factory.Enqueue("com.example.healthy", new FakeSession());
        await using var manager = fixture.CreateManager(new ExternalPluginHostOptions
        {
            InitialBackoff = TimeSpan.FromSeconds(2),
            MaximumBackoff = TimeSpan.FromSeconds(10),
            AutoDisableAfterFailures = 3,
        });

        var first = await Assert.ThrowsAsync<ExternalPluginProtocolException>(() =>
            Execute(manager, "com.example.unstable"));
        Assert.Equal("plugin_process_exited", first.Code);
        AssertSnapshot(
            manager,
            "com.example.unstable",
            ExternalPluginRuntimeState.Backoff,
            1,
            now + TimeSpan.FromSeconds(2));

        var blocked = await Assert.ThrowsAsync<ExternalPluginUnavailableException>(() =>
            Execute(manager, "com.example.unstable"));
        Assert.Equal("plugin_backoff_active", blocked.Code);
        Assert.Equal(1, fixture.Factory.CreateCount("com.example.unstable"));
        Assert.True((await Execute(manager, "com.example.healthy"))
            .GetProperty("accepted").GetBoolean());

        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        var second = await Assert.ThrowsAsync<ExternalPluginProtocolException>(() =>
            Execute(manager, "com.example.unstable"));
        Assert.Equal("plugin_call_timeout", second.Code);
        AssertSnapshot(
            manager,
            "com.example.unstable",
            ExternalPluginRuntimeState.Backoff,
            2,
            fixture.Clock.GetUtcNow() + TimeSpan.FromSeconds(4));

        fixture.Clock.Advance(TimeSpan.FromSeconds(4));
        await Assert.ThrowsAsync<ExternalPluginProtocolException>(() =>
            Execute(manager, "com.example.unstable"));
        var disabled = manager.GetSnapshot("com.example.unstable")!;
        Assert.Equal(ExternalPluginRuntimeState.AutoDisabled, disabled.State);
        Assert.Equal(3, disabled.ConsecutiveFailures);
        Assert.Null(disabled.RetryAtUtc);
        Assert.Equal("plugin_response_json_invalid", disabled.LastFailureCode);

        var autoDisabled = await Assert.ThrowsAsync<ExternalPluginUnavailableException>(() =>
            Execute(manager, "com.example.unstable"));
        Assert.Equal("plugin_auto_disabled", autoDisabled.Code);
        Assert.Equal(3, fixture.Factory.CreateCount("com.example.unstable"));

        await manager.ResetAsync("com.example.unstable");
        AssertSnapshot(
            manager,
            "com.example.unstable",
            ExternalPluginRuntimeState.Stopped,
            0,
            null);
        Assert.True((await Execute(manager, "com.example.unstable"))
            .GetProperty("accepted").GetBoolean());
        Assert.Equal(4, fixture.Factory.CreateCount("com.example.unstable"));
    }

    [Fact]
    public async Task BusinessErrorProvesProcessAliveAndDoesNotTripBackoff()
    {
        using var fixture = new ManagerFixture("business");
        var session = new FakeSession
        {
            ExecuteHandler = (_, _) => Task.FromException<JsonElement>(
                new ExternalPluginRemoteException("invalid_config", "missing option")),
        };
        fixture.Factory.Enqueue("com.example.business", session);
        await using var manager = fixture.CreateManager();

        var error = await Assert.ThrowsAsync<ExternalPluginRemoteException>(() =>
            Execute(manager, "com.example.business"));

        Assert.Equal("invalid_config", error.Code);
        Assert.Equal(ExternalPluginRuntimeState.Ready,
            manager.GetSnapshot("com.example.business")!.State);
        Assert.Equal(0, manager.GetSnapshot("com.example.business")!.ConsecutiveFailures);
        Assert.Equal(1, fixture.Factory.CreateCount("com.example.business"));
        Assert.False(session.Disposed);
    }

    [Fact]
    public async Task SuccessfulRetryClearsPriorFailureSequence()
    {
        var now = new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);
        using var fixture = new ManagerFixture("recover");
        fixture.Clock.Set(now);
        fixture.Factory.Enqueue(
            "com.example.recover",
            FailingExecute("plugin_process_exited"),
            new FakeSession());
        await using var manager = fixture.CreateManager(new ExternalPluginHostOptions
        {
            InitialBackoff = TimeSpan.FromSeconds(2),
            MaximumBackoff = TimeSpan.FromSeconds(8),
            AutoDisableAfterFailures = 3,
        });

        await Assert.ThrowsAsync<ExternalPluginProtocolException>(() =>
            Execute(manager, "com.example.recover"));
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True((await Execute(manager, "com.example.recover"))
            .GetProperty("accepted").GetBoolean());

        AssertSnapshot(
            manager,
            "com.example.recover",
            ExternalPluginRuntimeState.Ready,
            0,
            null);
        Assert.Null(manager.GetSnapshot("com.example.recover")!.LastFailureCode);
    }

    [Fact]
    public async Task InitializeFailureUsesSameBackoffAndDisposesBrokenSession()
    {
        using var fixture = new ManagerFixture("start-fail");
        var failed = new FakeSession
        {
            StartError = new ExternalPluginProtocolException(
                "plugin_initialize_identity_mismatch",
                "forged identity"),
        };
        fixture.Factory.Enqueue("com.example.start-fail", failed);
        await using var manager = fixture.CreateManager();

        var error = await Assert.ThrowsAsync<ExternalPluginProtocolException>(() =>
            Execute(manager, "com.example.start-fail"));

        Assert.Equal("plugin_initialize_identity_mismatch", error.Code);
        Assert.True(failed.Disposed);
        var snapshot = manager.GetSnapshot("com.example.start-fail")!;
        Assert.Equal(ExternalPluginRuntimeState.Backoff, snapshot.State);
        Assert.Equal(1, snapshot.ConsecutiveFailures);
        Assert.Equal("plugin_initialize_identity_mismatch", snapshot.LastFailureCode);
    }

    [Fact]
    public async Task HostValidationFailureLeavesReadySessionReusable()
    {
        using var fixture = new ManagerFixture("validation");
        var session = new FakeSession
        {
            ExecuteHandler = (_, _) => Task.FromException<JsonElement>(
                new ExternalPluginProtocolException(
                    "plugin_request_too_large",
                    "request rejected before write")),
        };
        fixture.Factory.Enqueue("com.example.validation", session);
        await using var manager = fixture.CreateManager();

        var error = await Assert.ThrowsAsync<ExternalPluginProtocolException>(() =>
            Execute(manager, "com.example.validation"));

        Assert.Equal("plugin_request_too_large", error.Code);
        Assert.Equal(ExternalPluginRuntimeState.Ready,
            manager.GetSnapshot("com.example.validation")!.State);
        Assert.Equal(0, manager.GetSnapshot("com.example.validation")!.ConsecutiveFailures);
        Assert.False(session.Disposed);
    }

    [Fact]
    public async Task CallerCancellationReplacesFaultedSessionWithoutPenaltyOrDelay()
    {
        using var fixture = new ManagerFixture("cancel");
        var canceledSession = new FakeSession
        {
            ExecuteHandler = async (owner, cancellationToken) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return Json("{}");
                }
                catch (OperationCanceledException)
                {
                    owner.State = ExternalPluginSessionState.Faulted;
                    throw;
                }
            },
        };
        fixture.Factory.Enqueue("com.example.cancel", canceledSession, new FakeSession());
        await using var manager = fixture.CreateManager();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Execute(manager, "com.example.cancel", cancellation.Token));

        var snapshot = manager.GetSnapshot("com.example.cancel")!;
        Assert.Equal(ExternalPluginRuntimeState.Stopped, snapshot.State);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.Null(snapshot.RetryAtUtc);
        Assert.True(canceledSession.Disposed);
        Assert.True((await Execute(manager, "com.example.cancel"))
            .GetProperty("accepted").GetBoolean());
        Assert.Equal(2, fixture.Factory.CreateCount("com.example.cancel"));
    }

    [Fact]
    public async Task UnhealthyResultCountsAsFailureAndDisposesSession()
    {
        using var fixture = new ManagerFixture("health");
        var session = new FakeSession { HealthResult = false };
        fixture.Factory.Enqueue("com.example.health", session);
        await using var manager = fixture.CreateManager();

        Assert.False(await manager.HealthAsync("com.example.health"));

        var snapshot = manager.GetSnapshot("com.example.health")!;
        Assert.Equal(ExternalPluginRuntimeState.Backoff, snapshot.State);
        Assert.Equal(1, snapshot.ConsecutiveFailures);
        Assert.Equal("plugin_health_unhealthy", snapshot.LastFailureCode);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task ConcurrentCallsAreSerializedBeforeSessionDispatch()
    {
        using var fixture = new ManagerFixture("serial");
        var session = new FakeSession
        {
            ExecuteHandler = async (owner, cancellationToken) =>
            {
                var active = Interlocked.Increment(ref owner.ActiveExecutions);
                owner.MaximumActiveExecutions = Math.Max(owner.MaximumActiveExecutions, active);
                await Task.Delay(20, cancellationToken);
                Interlocked.Decrement(ref owner.ActiveExecutions);
                return Json("{\"accepted\":true}");
            },
        };
        fixture.Factory.Enqueue("com.example.serial", session);
        await using var manager = fixture.CreateManager();

        await Task.WhenAll(
            Execute(manager, "com.example.serial"),
            Execute(manager, "com.example.serial"));

        Assert.Equal(1, session.MaximumActiveExecutions);
        Assert.Equal(2, session.ExecuteCount);
        Assert.Equal(1, fixture.Factory.CreateCount("com.example.serial"));
    }

    [Fact]
    public async Task MissingPluginUsesStableUnavailableCode()
    {
        using var fixture = new ManagerFixture("known");
        await using var manager = fixture.CreateManager();

        var error = await Assert.ThrowsAsync<ExternalPluginUnavailableException>(() =>
            Execute(manager, "com.example.missing"));

        Assert.Equal("plugin_not_found", error.Code);
        Assert.Equal("com.example.missing", error.PluginId);
    }

    private static FakeSession FailingExecute(string code) => new()
    {
        ExecuteHandler = (owner, _) =>
        {
            owner.State = ExternalPluginSessionState.Faulted;
            return Task.FromException<JsonElement>(
                new ExternalPluginProtocolException(code, "fixture failure"));
        },
    };

    private static Task<JsonElement> Execute(
        ExternalPluginHostManager manager,
        string pluginId,
        CancellationToken cancellationToken = default) =>
        manager.ExecuteAsync(
            pluginId,
            "filter.all",
            Json("{}"),
            Json("{}"),
            cancellationToken: cancellationToken);

    private static void AssertSnapshot(
        ExternalPluginHostManager manager,
        string pluginId,
        ExternalPluginRuntimeState state,
        int failures,
        DateTimeOffset? retryAt)
    {
        var snapshot = manager.GetSnapshot(pluginId)!;
        Assert.Equal(state, snapshot.State);
        Assert.Equal(failures, snapshot.ConsecutiveFailures);
        Assert.Equal(retryAt, snapshot.RetryAtUtc);
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private sealed class ManagerFixture : IDisposable
    {
        public ManagerFixture(params string[] names)
            : this(names, new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero))
        {
        }

        public ManagerFixture(string first, string second, DateTimeOffset now)
            : this([first, second], now)
        {
        }

        private ManagerFixture(string[] names, DateTimeOffset now)
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "animegonet-plugin-manager-tests",
                Guid.NewGuid().ToString("N"));
            PluginDataRoot = Path.Combine(RootPath, "plugin-data");
            Clock = new MutableTimeProvider(now);
            Discovery = new ExternalPluginDiscoveryResult(
                names.Select(CreatePackage).ToArray(),
                []);
        }

        public string RootPath { get; }

        public string PluginDataRoot { get; }

        public MutableTimeProvider Clock { get; }

        public FakeSessionFactory Factory { get; } = new();

        public ExternalPluginDiscoveryResult Discovery { get; }

        public ExternalPluginHostManager CreateManager(ExternalPluginHostOptions? options = null) =>
            new(
                Discovery,
                PluginDataRoot,
                "9.8.7",
                options,
                Factory,
                Clock);

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private ExternalPluginPackage CreatePackage(string name)
        {
            var id = $"com.example.{name}";
            var directory = Path.Combine(RootPath, "plugins", name);
            return new ExternalPluginPackage(
                new ExternalPluginManifest(
                    id,
                    name,
                    "1.0.0",
                    1,
                    "filter",
                    "win-x64",
                    "plugin.exe",
                    "config.schema.json",
                    []),
                directory,
                Path.Combine(directory, "plugin.json"),
                Path.Combine(directory, "plugin.exe"),
                Path.Combine(directory, "config.schema.json"));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now += value;

        public void Set(DateTimeOffset value) => _now = value;
    }

    private sealed class FakeSessionFactory : IExternalPluginSessionFactory
    {
        private readonly Dictionary<string, Queue<FakeSession>> _sessions =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _createCounts =
            new(StringComparer.Ordinal);

        public List<string> DataPaths { get; } = [];

        public void Enqueue(string pluginId, params FakeSession[] sessions) =>
            _sessions[pluginId] = new Queue<FakeSession>(sessions);

        public int CreateCount(string pluginId) =>
            _createCounts.GetValueOrDefault(pluginId);

        public IExternalPluginSession Create(
            ExternalPluginPackage package,
            string pluginDataPath,
            ExternalPluginSessionOptions options)
        {
            _createCounts[package.Manifest.Id] = CreateCount(package.Manifest.Id) + 1;
            DataPaths.Add(pluginDataPath);
            Assert.NotNull(options);
            return _sessions[package.Manifest.Id].Dequeue();
        }
    }

    private sealed class FakeSession : IExternalPluginSession
    {
        public Func<FakeSession, CancellationToken, Task<JsonElement>>? ExecuteHandler { get; init; }

        public ExternalPluginProtocolException? StartError { get; init; }

        public bool HealthResult { get; init; } = true;

        public ExternalPluginSessionState State { get; set; } =
            ExternalPluginSessionState.Created;

        public int StartCount { get; private set; }

        public int ExecuteCount { get; private set; }

        public string? HostVersion { get; private set; }

        public bool Disposed { get; private set; }

        public int ActiveExecutions;

        public int MaximumActiveExecutions;

        public Task StartAsync(string hostVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            HostVersion = hostVersion;
            if (StartError is not null)
            {
                State = ExternalPluginSessionState.Faulted;
                return Task.FromException(StartError);
            }
            State = ExternalPluginSessionState.Ready;
            return Task.CompletedTask;
        }

        public Task<JsonElement> ExecuteAsync(
            string operation,
            JsonElement payload,
            JsonElement config,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("filter.all", operation);
            ExecuteCount++;
            return ExecuteHandler?.Invoke(this, cancellationToken)
                ?? Task.FromResult(Json("{\"accepted\":true}"));
        }

        public Task<bool> HealthAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HealthResult)
            {
                State = ExternalPluginSessionState.Faulted;
            }
            return Task.FromResult(HealthResult);
        }

        public Task ShutdownAsync(
            string reason = "host_shutdown",
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = ExternalPluginSessionState.Stopped;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            State = ExternalPluginSessionState.Stopped;
            return ValueTask.CompletedTask;
        }
    }
}
