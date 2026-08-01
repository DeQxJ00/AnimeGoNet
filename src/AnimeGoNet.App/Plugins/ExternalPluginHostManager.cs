using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace AnimeGoNet.App.Plugins;

public enum ExternalPluginRuntimeState
{
    Stopped,
    Starting,
    Ready,
    Backoff,
    AutoDisabled,
}

public sealed record ExternalPluginRuntimeSnapshot(
    string PluginId,
    ExternalPluginRuntimeState State,
    int ConsecutiveFailures,
    DateTimeOffset? RetryAtUtc,
    string? LastFailureCode);

public sealed record ExternalPluginHostOptions
{
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaximumBackoff { get; init; } = TimeSpan.FromMinutes(2);

    public int AutoDisableAfterFailures { get; init; } = 5;

    public ExternalPluginSessionOptions Session { get; init; } = new();

    public void Validate()
    {
        if (InitialBackoff <= TimeSpan.Zero || InitialBackoff > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(InitialBackoff),
                "External plugin initial backoff must be positive and at most ten minutes.");
        }
        if (MaximumBackoff < InitialBackoff || MaximumBackoff > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumBackoff),
                "External plugin maximum backoff must be at least the initial delay and at most one hour.");
        }
        if (AutoDisableAfterFailures is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AutoDisableAfterFailures),
                "External plugins must auto-disable after 1 to 100 consecutive failures.");
        }
        ArgumentNullException.ThrowIfNull(Session);
        Session.Validate();
    }
}

public sealed class ExternalPluginUnavailableException(
    string pluginId,
    string code,
    DateTimeOffset? retryAtUtc = null) : InvalidOperationException(
        retryAtUtc is { } retry
            ? $"External plugin '{pluginId}' is unavailable until {retry:O}."
            : $"External plugin '{pluginId}' is unavailable.")
{
    public string PluginId { get; } = pluginId;

    public string Code { get; } = code;

    public DateTimeOffset? RetryAtUtc { get; } = retryAtUtc;
}

internal interface IExternalPluginSessionFactory
{
    IExternalPluginSession Create(
        ExternalPluginPackage package,
        string pluginDataPath,
        ExternalPluginSessionOptions options);
}

internal sealed class ExternalPluginSessionFactory(
    ExternalPluginManifestLoader loader) : IExternalPluginSessionFactory
{
    public IExternalPluginSession Create(
        ExternalPluginPackage package,
        string pluginDataPath,
        ExternalPluginSessionOptions options) =>
        new ExternalPluginProcessSession(loader, package, pluginDataPath, options);
}

public sealed class ExternalPluginHostManager : IAsyncDisposable
{
    private readonly FrozenDictionary<string, PluginRuntime> _plugins;
    private readonly string _pluginDataRoot;
    private readonly string _hostVersion;
    private readonly ExternalPluginHostOptions _options;
    private readonly IExternalPluginSessionFactory _sessionFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ExternalPluginConfigurationStore? _configurations;
    private bool _disposed;

    public ExternalPluginHostManager(
        ExternalPluginManifestLoader loader,
        ExternalPluginDiscoveryResult discovery,
        string pluginDataRoot,
        ExternalPluginHostOptions? options = null,
        ExternalPluginConfigurationStore? configurations = null)
        : this(
            discovery,
            pluginDataRoot,
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            options,
            new ExternalPluginSessionFactory(loader),
            TimeProvider.System,
            configurations)
    {
    }

    internal ExternalPluginHostManager(
        ExternalPluginDiscoveryResult discovery,
        string pluginDataRoot,
        string hostVersion,
        ExternalPluginHostOptions? options,
        IExternalPluginSessionFactory sessionFactory,
        TimeProvider timeProvider,
        ExternalPluginConfigurationStore? configurations = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostVersion);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _pluginDataRoot = Path.GetFullPath(pluginDataRoot);
        _hostVersion = hostVersion.Trim();
        _options = options ?? new ExternalPluginHostOptions();
        _options.Validate();
        _sessionFactory = sessionFactory;
        _timeProvider = timeProvider;
        _configurations = configurations;
        _plugins = discovery.Packages
            .ToFrozenDictionary(
                package => package.Manifest.Id,
                package => new PluginRuntime(package),
                StringComparer.Ordinal);
    }

    public IReadOnlyList<string> PluginIds =>
        _plugins.Keys.Order(StringComparer.Ordinal).ToArray();

    public IReadOnlyList<ExternalPluginRuntimeSnapshot> GetSnapshots() =>
        _plugins.Values
            .OrderBy(runtime => runtime.Package.Manifest.Id, StringComparer.Ordinal)
            .Select(runtime => runtime.Snapshot())
            .ToArray();

    public ExternalPluginRuntimeSnapshot? GetSnapshot(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return _plugins.TryGetValue(pluginId, out var runtime)
            ? runtime.Snapshot()
            : null;
    }

    public async Task<JsonElement> ExecuteConfiguredAsync(
        string pluginId,
        string operation,
        JsonElement payload,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        _ = GetRequired(pluginId);
        var configuration = _configurations?.GetOrDefault(pluginId);
        if (configuration is { Enabled: false })
        {
            throw new ExternalPluginUnavailableException(pluginId, "plugin_disabled");
        }
        var configuredPayload = configuration is null
            ? payload.Clone()
            : MergeArguments(configuration.Args, payload);
        var configuredVars = configuration?.Vars ?? EmptyJsonObject();
        return await ExecuteAsync(
            pluginId,
            operation,
            configuredPayload,
            configuredVars,
            timeout,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> HealthConfiguredAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        _ = GetRequired(pluginId);
        if (_configurations?.GetOrDefault(pluginId) is { Enabled: false })
        {
            throw new ExternalPluginUnavailableException(pluginId, "plugin_disabled");
        }
        return HealthAsync(pluginId, cancellationToken);
    }

    internal async Task<JsonElement> ExecuteAsync(
        string pluginId,
        string operation,
        JsonElement payload,
        JsonElement config,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var runtime = GetRequired(pluginId);
        await runtime.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ThrowIfUnavailable(runtime);
            var session = await EnsureStartedAsync(runtime, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                var result = await session.ExecuteAsync(
                    operation,
                    payload,
                    config,
                    timeout,
                    cancellationToken).ConfigureAwait(false);
                RecordSuccess(runtime);
                return result;
            }
            catch (ExternalPluginRemoteException)
            {
                RecordSuccess(runtime);
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await AbandonWithoutPenaltyAsync(runtime).ConfigureAwait(false);
                throw;
            }
            catch (ExternalPluginProtocolException exception)
            {
                if (session.State == ExternalPluginSessionState.Ready)
                {
                    throw;
                }
                await RecordFailureAndDisposeAsync(runtime, exception.Code)
                    .ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    internal async Task<bool> HealthAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        var runtime = GetRequired(pluginId);
        await runtime.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ThrowIfUnavailable(runtime);
            var session = await EnsureStartedAsync(runtime, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                var healthy = await session.HealthAsync(cancellationToken).ConfigureAwait(false);
                if (healthy)
                {
                    RecordSuccess(runtime);
                    return true;
                }
                await RecordFailureAndDisposeAsync(runtime, "plugin_health_unhealthy")
                    .ConfigureAwait(false);
                return false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await AbandonWithoutPenaltyAsync(runtime).ConfigureAwait(false);
                throw;
            }
            catch (ExternalPluginProtocolException exception)
            {
                await RecordFailureAndDisposeAsync(runtime, exception.Code)
                    .ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    public async Task ResetAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        var runtime = GetRequired(pluginId);
        await runtime.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await DisposeSessionAsync(runtime).ConfigureAwait(false);
            lock (runtime.Sync)
            {
                runtime.State = ExternalPluginRuntimeState.Stopped;
                runtime.ConsecutiveFailures = 0;
                runtime.RetryAtUtc = null;
                runtime.LastFailureCode = null;
            }
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (var runtime in _plugins.Values)
        {
            await runtime.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await DisposeSessionAsync(runtime).ConfigureAwait(false);
                lock (runtime.Sync)
                {
                    if (runtime.State != ExternalPluginRuntimeState.AutoDisabled)
                    {
                        runtime.State = ExternalPluginRuntimeState.Stopped;
                    }
                }
            }
            finally
            {
                runtime.Gate.Release();
                runtime.Gate.Dispose();
            }
        }
    }

    private PluginRuntime GetRequired(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ThrowIfDisposed();
        if (!_plugins.TryGetValue(pluginId, out var runtime))
        {
            throw new ExternalPluginUnavailableException(
                pluginId,
                "plugin_not_found");
        }
        return runtime;
    }

    private void ThrowIfUnavailable(PluginRuntime runtime)
    {
        lock (runtime.Sync)
        {
            if (runtime.State == ExternalPluginRuntimeState.AutoDisabled)
            {
                throw new ExternalPluginUnavailableException(
                    runtime.Package.Manifest.Id,
                    "plugin_auto_disabled");
            }
            if (runtime.RetryAtUtc is { } retryAt
                && retryAt > _timeProvider.GetUtcNow())
            {
                throw new ExternalPluginUnavailableException(
                    runtime.Package.Manifest.Id,
                    "plugin_backoff_active",
                    retryAt);
            }
        }
    }

    private async Task<IExternalPluginSession> EnsureStartedAsync(
        PluginRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (runtime.Session is { State: ExternalPluginSessionState.Ready } ready)
        {
            return ready;
        }
        await DisposeSessionAsync(runtime).ConfigureAwait(false);
        lock (runtime.Sync)
        {
            runtime.State = ExternalPluginRuntimeState.Starting;
        }
        var dataPath = Path.Combine(_pluginDataRoot, runtime.Package.Manifest.Id);
        var session = _sessionFactory.Create(runtime.Package, dataPath, _options.Session);
        runtime.Session = session;
        try
        {
            await session.StartAsync(_hostVersion, cancellationToken).ConfigureAwait(false);
            lock (runtime.Sync)
            {
                runtime.State = ExternalPluginRuntimeState.Ready;
            }
            return session;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await AbandonWithoutPenaltyAsync(runtime).ConfigureAwait(false);
            throw;
        }
        catch (ExternalPluginProtocolException exception)
        {
            await RecordFailureAndDisposeAsync(runtime, exception.Code).ConfigureAwait(false);
            throw;
        }
    }

    private static void RecordSuccess(PluginRuntime runtime)
    {
        lock (runtime.Sync)
        {
            runtime.State = ExternalPluginRuntimeState.Ready;
            runtime.ConsecutiveFailures = 0;
            runtime.RetryAtUtc = null;
            runtime.LastFailureCode = null;
        }
    }

    private async Task RecordFailureAndDisposeAsync(
        PluginRuntime runtime,
        string failureCode)
    {
        await DisposeSessionAsync(runtime).ConfigureAwait(false);
        lock (runtime.Sync)
        {
            runtime.ConsecutiveFailures++;
            runtime.LastFailureCode = failureCode;
            if (runtime.ConsecutiveFailures >= _options.AutoDisableAfterFailures)
            {
                runtime.State = ExternalPluginRuntimeState.AutoDisabled;
                runtime.RetryAtUtc = null;
                return;
            }

            var exponent = Math.Min(runtime.ConsecutiveFailures - 1, 30);
            var multiplier = 1L << exponent;
            var ticks = _options.InitialBackoff.Ticks > long.MaxValue / multiplier
                ? long.MaxValue
                : _options.InitialBackoff.Ticks * multiplier;
            var delay = TimeSpan.FromTicks(Math.Min(ticks, _options.MaximumBackoff.Ticks));
            runtime.State = ExternalPluginRuntimeState.Backoff;
            runtime.RetryAtUtc = _timeProvider.GetUtcNow() + delay;
        }
    }

    private static async Task AbandonWithoutPenaltyAsync(PluginRuntime runtime)
    {
        await DisposeSessionAsync(runtime).ConfigureAwait(false);
        lock (runtime.Sync)
        {
            runtime.State = ExternalPluginRuntimeState.Stopped;
            runtime.RetryAtUtc = null;
        }
    }

    private static async Task DisposeSessionAsync(PluginRuntime runtime)
    {
        var session = runtime.Session;
        runtime.Session = null;
        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static JsonElement MergeArguments(
        JsonElement configuredArgs,
        JsonElement invocationPayload)
    {
        ExternalPluginConfigurationStore.ValidateObject(configuredArgs, "args");
        if (invocationPayload.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "Configured external plugin payload must be a JSON object.",
                nameof(invocationPayload));
        }
        var invocationNames = invocationPayload
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in configuredArgs.EnumerateObject())
            {
                if (!invocationNames.Contains(property.Name))
                {
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }
            }
            foreach (var property in invocationPayload.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    private static JsonElement EmptyJsonObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private sealed class PluginRuntime(ExternalPluginPackage package)
    {
        public object Sync { get; } = new();

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public ExternalPluginPackage Package { get; } = package;

        public IExternalPluginSession? Session { get; set; }

        public ExternalPluginRuntimeState State { get; set; }

        public int ConsecutiveFailures { get; set; }

        public DateTimeOffset? RetryAtUtc { get; set; }

        public string? LastFailureCode { get; set; }

        public ExternalPluginRuntimeSnapshot Snapshot()
        {
            lock (Sync)
            {
                return new ExternalPluginRuntimeSnapshot(
                    Package.Manifest.Id,
                    State,
                    ConsecutiveFailures,
                    RetryAtUtc,
                    LastFailureCode);
            }
        }
    }
}
