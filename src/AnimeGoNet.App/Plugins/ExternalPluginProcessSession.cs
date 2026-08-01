using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Serialization;

namespace AnimeGoNet.App.Plugins;

public sealed class ExternalPluginProcessSession : IAsyncDisposable
{
    private static readonly byte[] Newline = [(byte)'\n'];
    private static readonly FrozenSet<string> InitializeResultFields =
        new[] { "pluginId", "pluginVersion", "apiVersion", "type", "capabilities" }
            .ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> HealthResultFields =
        new[] { "healthy" }.ToFrozenSet(StringComparer.Ordinal);
    private readonly ExternalPluginManifestLoader _manifestLoader;
    private readonly ExternalPluginPackage _configuredPackage;
    private readonly string _pluginDataPath;
    private readonly ExternalPluginSessionOptions _options;
    private readonly IExternalPluginProcessFactory _processFactory;
    private readonly Func<string> _requestIdFactory;
    private readonly SemaphoreSlim _callGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lock _stateLock = new();
    private IExternalPluginProcess? _process;
    private BoundedUtf8LineReader? _stdout;
    private Task? _stderrDrain;
    private ExternalPluginPackage? _activePackage;
    private ExternalPluginSessionState _state = ExternalPluginSessionState.Created;
    private bool _disposed;

    public ExternalPluginProcessSession(
        ExternalPluginManifestLoader manifestLoader,
        ExternalPluginPackage package,
        string pluginDataPath,
        ExternalPluginSessionOptions? options = null)
        : this(
            manifestLoader,
            package,
            pluginDataPath,
            options,
            new SystemExternalPluginProcessFactory(),
            static () => Guid.NewGuid().ToString("N"))
    {
    }

    internal ExternalPluginProcessSession(
        ExternalPluginManifestLoader manifestLoader,
        ExternalPluginPackage package,
        string pluginDataPath,
        ExternalPluginSessionOptions? options,
        IExternalPluginProcessFactory processFactory,
        Func<string> requestIdFactory)
    {
        ArgumentNullException.ThrowIfNull(manifestLoader);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDataPath);
        ArgumentNullException.ThrowIfNull(processFactory);
        ArgumentNullException.ThrowIfNull(requestIdFactory);
        _manifestLoader = manifestLoader;
        _configuredPackage = package;
        _pluginDataPath = Path.GetFullPath(pluginDataPath);
        _options = options ?? new ExternalPluginSessionOptions();
        _options.Validate();
        _processFactory = processFactory;
        _requestIdFactory = requestIdFactory;
    }

    public ExternalPluginSessionState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public ExternalPluginManifest Manifest =>
        _activePackage?.Manifest ?? _configuredPackage.Manifest;

    public async Task StartAsync(
        string hostVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostVersion);
        ThrowIfDisposed();
        lock (_stateLock)
        {
            if (_state != ExternalPluginSessionState.Created)
            {
                throw new InvalidOperationException(
                    "External plugin sessions can only be started once.");
            }
        }

        ExternalPluginPackage package;
        try
        {
            package = await _manifestLoader.LoadPackageAsync(
                _configuredPackage.DirectoryPath,
                cancellationToken).ConfigureAwait(false);
            if (!EquivalentIdentity(_configuredPackage.Manifest, package.Manifest))
            {
                throw new ExternalPluginProtocolException(
                    "plugin_manifest_changed",
                    "The external plugin manifest changed after discovery.");
            }
            _process = _processFactory.Start(package, _pluginDataPath);
            _activePackage = package;
            _stdout = new BoundedUtf8LineReader(_process.StandardOutput);
            _stderrDrain = DrainStderrAsync(
                _process.StandardError,
                _options.StderrBufferBytes,
                _lifetime.Token);

            var payload = JsonSerializer.SerializeToElement(
                new ExternalPluginInitializePayload(
                    hostVersion.Trim(),
                    package.Manifest.Id,
                    package.Manifest.Version,
                    ExternalPluginProtocol.CurrentApiVersion,
                    package.Manifest.Type,
                    package.Manifest.Capabilities),
                ExternalPluginJsonContext.Default.ExternalPluginInitializePayload);
            var response = await CallRawAsync(
                ExternalPluginMethods.Initialize,
                operation: null,
                payload,
                config: null,
                _options.InitializeTimeout,
                allowCreated: true,
                cancellationToken).ConfigureAwait(false);
            ThrowRemote(response);
            var initialized = DeserializeRequiredResult(
                response,
                ExternalPluginJsonContext.Default.ExternalPluginInitializeResult,
                "plugin_initialize_result_invalid",
                InitializeResultFields);
            if (!string.Equals(
                    initialized.PluginId,
                    package.Manifest.Id,
                    StringComparison.Ordinal)
                || !string.Equals(
                    initialized.PluginVersion,
                    package.Manifest.Version,
                    StringComparison.Ordinal)
                || initialized.ApiVersion != ExternalPluginProtocol.CurrentApiVersion
                || !string.Equals(
                    initialized.Type,
                    package.Manifest.Type,
                    StringComparison.Ordinal)
                || initialized.Capabilities is null
                || !initialized.Capabilities.SequenceEqual(
                    package.Manifest.Capabilities,
                    StringComparer.Ordinal))
            {
                throw Fault(
                    "plugin_initialize_identity_mismatch",
                    "The external plugin initialize response does not match its manifest.");
            }

            lock (_stateLock)
            {
                if (_state == ExternalPluginSessionState.Created)
                {
                    _state = ExternalPluginSessionState.Ready;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            FaultAndKill();
            throw;
        }
        catch
        {
            FaultAndKill();
            throw;
        }
    }

    public async Task<JsonElement> ExecuteAsync(
        string operation,
        JsonElement payload,
        JsonElement config,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateOperation(operation, Manifest.Type);
        if (payload.ValueKind == JsonValueKind.Undefined
            || config.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException(
                "External plugin execute payload and config must contain JSON values.");
        }
        var response = await CallRawAsync(
            ExternalPluginMethods.Execute,
            operation,
            payload.Clone(),
            config.Clone(),
            timeout ?? _options.ExecuteTimeout,
            allowCreated: false,
            cancellationToken).ConfigureAwait(false);
        ThrowRemote(response);
        if (response.Result is not { } result
            || result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw Fault(
                "plugin_execute_result_missing",
                "The external plugin execute response did not contain a result.");
        }
        return result.Clone();
    }

    public async Task<bool> HealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var response = await CallRawAsync(
            ExternalPluginMethods.Health,
            operation: null,
            payload: null,
            config: null,
            _options.HealthTimeout,
            allowCreated: false,
            cancellationToken).ConfigureAwait(false);
        ExternalPluginHealthResult health;
        try
        {
            ThrowRemote(response);
            health = DeserializeRequiredResult(
                response,
                ExternalPluginJsonContext.Default.ExternalPluginHealthResult,
                "plugin_health_result_invalid",
                HealthResultFields);
        }
        catch (ExternalPluginProtocolException)
        {
            FaultAndKill();
            throw;
        }
        if (!health.Healthy)
        {
            FaultAndKill();
        }
        return health.Healthy;
    }

    public async Task ShutdownAsync(
        string reason = "host_shutdown",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ThrowIfDisposed();
        var state = State;
        if (state == ExternalPluginSessionState.Stopped)
        {
            return;
        }
        if (state == ExternalPluginSessionState.Faulted
            || _process is null)
        {
            await StopProcessAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            var payload = JsonSerializer.SerializeToElement(
                new ExternalPluginShutdownPayload(reason.Trim()),
                ExternalPluginJsonContext.Default.ExternalPluginShutdownPayload);
            var response = await CallRawAsync(
                ExternalPluginMethods.Shutdown,
                operation: null,
                payload,
                config: null,
                _options.ShutdownTimeout,
                allowCreated: false,
                cancellationToken).ConfigureAwait(false);
            ThrowRemote(response);
            _process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
            timeout.CancelAfter(_options.ShutdownTimeout);
            try
            {
                await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Fault(
                    "plugin_shutdown_timeout",
                    "The external plugin did not exit after acknowledging shutdown.");
            }
            lock (_stateLock)
            {
                _state = ExternalPluginSessionState.Stopped;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            FaultAndKill();
            throw;
        }
        finally
        {
            if (State != ExternalPluginSessionState.Stopped)
            {
                FaultAndKill();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            if (State == ExternalPluginSessionState.Ready)
            {
                try
                {
                    await ShutdownAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is ExternalPluginProtocolException
                        or IOException
                        or OperationCanceledException)
                {
                    FaultAndKill();
                }
            }
        }
        finally
        {
            _disposed = true;
            await StopProcessAsync().ConfigureAwait(false);
            _lifetime.Dispose();
            _callGate.Dispose();
        }
    }

    private async Task<ExternalPluginWireResponse> CallRawAsync(
        string method,
        string? operation,
        JsonElement? payload,
        JsonElement? config,
        TimeSpan timeout,
        bool allowCreated,
        CancellationToken cancellationToken)
    {
        await _callGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var expectedState = allowCreated
                ? ExternalPluginSessionState.Created
                : ExternalPluginSessionState.Ready;
            if (State != expectedState
                || _process is null
                || _stdout is null)
            {
                throw new InvalidOperationException(
                    $"External plugin session is not in the required {expectedState} state.");
            }
            if (_process.HasExited)
            {
                throw Fault(
                    "plugin_process_exited",
                    "The external plugin process exited before the request.");
            }

            var requestId = _requestIdFactory();
            if (!ValidRequestId(requestId))
            {
                throw new InvalidOperationException(
                    "The external plugin request ID factory returned an invalid identifier.");
            }
            var request = new ExternalPluginWireRequest(
                ExternalPluginProtocol.CurrentApiVersion,
                requestId,
                method,
                operation,
                payload,
                config);
            var requestBytes = SerializeRequest(request, _options.MaximumRequestBytes);
            using var callCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
            callCancellation.CancelAfter(timeout);
            try
            {
                await _process.StandardInput.WriteAsync(
                    requestBytes,
                    callCancellation.Token).ConfigureAwait(false);
                await _process.StandardInput.WriteAsync(
                    Newline,
                    callCancellation.Token).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(callCancellation.Token)
                    .ConfigureAwait(false);
                var line = await _stdout.ReadLineAsync(
                    _options.MaximumResponseBytes,
                    callCancellation.Token).ConfigureAwait(false);
                if (line is null)
                {
                    throw Fault(
                        "plugin_process_exited",
                        "The external plugin closed stdout before responding.");
                }
                var response = DeserializeResponse(line);
                if (response.ApiVersion != ExternalPluginProtocol.CurrentApiVersion)
                {
                    throw Fault(
                        "plugin_response_api_version_mismatch",
                        "The external plugin response API version does not match the host.");
                }
                if (!string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
                {
                    throw Fault(
                        "plugin_response_request_id_mismatch",
                        "The external plugin response request ID does not match the request.");
                }
                return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                FaultAndKill();
                throw;
            }
            catch (OperationCanceledException)
            {
                throw Fault(
                    "plugin_call_timeout",
                    "The external plugin call exceeded its timeout.");
            }
            catch (ExternalPluginProtocolException)
            {
                FaultAndKill();
                throw;
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                throw Fault(
                    "plugin_process_io_failed",
                    "External plugin stdio failed.",
                    exception);
            }
        }
        finally
        {
            _callGate.Release();
        }
    }

    private static byte[] SerializeRequest(
        ExternalPluginWireRequest request,
        int maximumBytes)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            request,
            ExternalPluginJsonContext.Default.ExternalPluginWireRequest);
        if (bytes.Length > maximumBytes)
        {
            throw new ExternalPluginProtocolException(
                "plugin_request_too_large",
                "The external plugin request exceeds the configured limit.");
        }
        return bytes;
    }

    private static ExternalPluginWireResponse DeserializeResponse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            ValidateResponseShape(document.RootElement);
            return JsonSerializer.Deserialize(
                bytes.Span,
                ExternalPluginJsonContext.Default.ExternalPluginWireResponse)
                ?? throw new ExternalPluginProtocolException(
                    "plugin_response_json_invalid",
                    "The external plugin response was empty.");
        }
        catch (ExternalPluginProtocolException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ExternalPluginProtocolException(
                "plugin_response_json_invalid",
                "The external plugin response must contain strict JSON.",
                exception);
        }
    }

    private static void ValidateResponseShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ExternalPluginProtocolException(
                "plugin_response_json_invalid",
                "The external plugin response must contain one object.");
        }
        var allowed = new HashSet<string>(
            ["apiVersion", "requestId", "ok", "result", "error"],
            StringComparer.Ordinal);
        var properties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new ExternalPluginProtocolException(
                    "plugin_response_unknown_field",
                    "The external plugin response contains an unknown field.");
            }
            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw new ExternalPluginProtocolException(
                    "plugin_response_duplicate_field",
                    "The external plugin response contains a duplicate field.");
            }
        }
        if (!properties.TryGetValue("apiVersion", out var apiVersion)
            || apiVersion.ValueKind != JsonValueKind.Number
            || !apiVersion.TryGetInt32(out _)
            || !properties.TryGetValue("requestId", out var requestId)
            || requestId.ValueKind != JsonValueKind.String
            || !properties.TryGetValue("ok", out var ok)
            || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ExternalPluginProtocolException(
                "plugin_response_shape_invalid",
                "The external plugin response is missing required protocol fields.");
        }

        var succeeded = ok.GetBoolean();
        properties.TryGetValue("result", out var result);
        properties.TryGetValue("error", out var error);
        if (succeeded)
        {
            if (error.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            {
                throw new ExternalPluginProtocolException(
                    "plugin_response_shape_invalid",
                    "Successful external plugin responses cannot contain an error.");
            }
        }
        else
        {
            if (result.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
                || error.ValueKind != JsonValueKind.Object)
            {
                throw new ExternalPluginProtocolException(
                    "plugin_response_shape_invalid",
                    "Failed external plugin responses require only an error object.");
            }
            ValidateErrorShape(error);
        }
    }

    private static void ValidateErrorShape(JsonElement error)
    {
        string? code = null;
        string? message = null;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in error.EnumerateObject())
        {
            if (!names.Add(property.Name)
                || property.Name is not ("code" or "message")
                || property.Value.ValueKind != JsonValueKind.String)
            {
                throw new ExternalPluginProtocolException(
                    "plugin_response_error_invalid",
                    "The external plugin error object is invalid.");
            }
            if (property.Name == "code") code = property.Value.GetString();
            if (property.Name == "message") message = property.Value.GetString();
        }
        if (names.Count != 2
            || !ValidErrorCode(code)
            || string.IsNullOrWhiteSpace(message)
            || message.Length > 1024
            || message.Any(character => char.IsControl(character) && character != '\t'))
        {
            throw new ExternalPluginProtocolException(
                "plugin_response_error_invalid",
                "The external plugin error code or message is invalid.");
        }
    }

    private static T DeserializeRequiredResult<T>(
        ExternalPluginWireResponse response,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        string errorCode,
        FrozenSet<string> allowedFields)
    {
        if (response.Result is not { } result
            || result.ValueKind != JsonValueKind.Object)
        {
            throw new ExternalPluginProtocolException(
                errorCode,
                "The external plugin response result has an invalid shape.");
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in result.EnumerateObject())
        {
            if (!allowedFields.Contains(property.Name)
                || !names.Add(property.Name))
            {
                throw new ExternalPluginProtocolException(
                    errorCode,
                    "The external plugin response result contains unknown or duplicate fields.");
            }
        }
        if (names.Count != allowedFields.Count)
        {
            throw new ExternalPluginProtocolException(
                errorCode,
                "The external plugin response result is missing required fields.");
        }
        try
        {
            return JsonSerializer.Deserialize(result.GetRawText(), typeInfo)
                ?? throw new ExternalPluginProtocolException(
                    errorCode,
                    "The external plugin response result is empty.");
        }
        catch (JsonException exception)
        {
            throw new ExternalPluginProtocolException(
                errorCode,
                "The external plugin response result is invalid.",
                exception);
        }
    }

    private static void ThrowRemote(ExternalPluginWireResponse response)
    {
        if (response.Ok)
        {
            return;
        }
        throw new ExternalPluginRemoteException(
            response.Error!.Code!,
            response.Error.Message!);
    }

    private ExternalPluginProtocolException Fault(
        string code,
        string message,
        Exception? innerException = null)
    {
        FaultAndKill();
        return new ExternalPluginProtocolException(code, message, innerException);
    }

    private void FaultAndKill()
    {
        lock (_stateLock)
        {
            if (_state != ExternalPluginSessionState.Stopped)
            {
                _state = ExternalPluginSessionState.Faulted;
            }
        }
        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Dispose already owns the remaining process cleanup.
        }
        _process?.Kill();
    }

    private async Task StopProcessAsync()
    {
        _lifetime.Cancel();
        _process?.Kill();
        if (_stderrDrain is not null)
        {
            try
            {
                await _stderrDrain.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during process teardown.
            }
            catch (IOException)
            {
                // The redirected stream closed with the process.
            }
        }
        if (_process is not null)
        {
            await _process.DisposeAsync().ConfigureAwait(false);
            _process = null;
        }
    }

    private static async Task DrainStderrAsync(
        Stream stderr,
        int bufferSize,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[bufferSize];
        while (await stderr.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
        {
            // stderr is deliberately drained without treating it as protocol output.
        }
    }

    private static void ValidateOperation(string operation, string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (operation.Length > 128
            || !operation.StartsWith(type + '.', StringComparison.Ordinal)
            || operation.Any(character =>
                character is not (>= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '.'
                    or '-'
                    or '_')))
        {
            throw new ArgumentException(
                $"External plugin operations must be stable lowercase '{type}.*' tokens.",
                nameof(operation));
        }
    }

    private static bool ValidRequestId(string value) =>
        value.Length == 32
        && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool ValidErrorCode(string? value) =>
        value is { Length: >= 1 and <= 128 }
        && value[0] is >= 'a' and <= 'z'
        && value.All(character =>
            character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_');

    private static bool EquivalentIdentity(
        ExternalPluginManifest left,
        ExternalPluginManifest right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal)
        && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
        && string.Equals(left.Version, right.Version, StringComparison.Ordinal)
        && left.ApiVersion == right.ApiVersion
        && string.Equals(left.Type, right.Type, StringComparison.Ordinal)
        && string.Equals(left.Rid, right.Rid, StringComparison.Ordinal)
        && string.Equals(left.EntryPoint, right.EntryPoint, StringComparison.Ordinal)
        && string.Equals(left.ConfigSchema, right.ConfigSchema, StringComparison.Ordinal)
        && left.Capabilities.SequenceEqual(right.Capabilities, StringComparer.Ordinal);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
