using AnimeGoNet.Core.Downloads;

namespace AnimeGoNet.App.Downloads;

public enum DownloadClientCircuitStatus
{
    Closed,
    Open,
    HalfOpen,
}

public sealed record DownloadClientCircuitSnapshot(
    string InstanceId,
    DownloadClientCircuitStatus Status,
    int ConsecutiveFailures,
    DateTimeOffset? RetryAtUtc);

public sealed class DownloadClientCircuitOpenException(
    string instanceId,
    DateTimeOffset retryAtUtc) : InvalidOperationException(
        $"Downloader instance '{instanceId}' circuit is open until {retryAtUtc:O}.")
{
    public string InstanceId { get; } = instanceId;

    public DateTimeOffset RetryAtUtc { get; } = retryAtUtc;
}

public sealed class DownloadClientOperationCoordinator(
    IDownloadClientRegistry registry,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(2);
    private readonly IDownloadClientRegistry _registry = registry;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _circuitLock = new();
    private readonly Dictionary<string, InstanceState> _instances = registry.InstanceIds.ToDictionary(
        id => id,
        _ => new InstanceState(),
        StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> InstanceIds => _registry.InstanceIds;

    public async Task<T> ExecuteAsync<T>(
        string instanceId,
        Func<IDownloadClient, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default) =>
        await ExecuteCoreAsync(
            instanceId, operation, bypassOpenCircuit: false, cancellationToken).ConfigureAwait(false);

    public async Task<T> ExecuteProbeAsync<T>(
        string instanceId,
        Func<IDownloadClient, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default) =>
        await ExecuteCoreAsync(
            instanceId, operation, bypassOpenCircuit: true, cancellationToken).ConfigureAwait(false);

    public DownloadClientCircuitSnapshot? GetCircuitSnapshot(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (!_instances.TryGetValue(instanceId, out var state))
        {
            return null;
        }

        lock (_circuitLock)
        {
            var status = state.ConsecutiveFailures == 0
                ? DownloadClientCircuitStatus.Closed
                : state.RetryAtUtc > _timeProvider.GetUtcNow()
                    ? DownloadClientCircuitStatus.Open
                    : DownloadClientCircuitStatus.HalfOpen;
            return new DownloadClientCircuitSnapshot(
                instanceId,
                status,
                state.ConsecutiveFailures,
                state.RetryAtUtc);
        }
    }

    private async Task<T> ExecuteCoreAsync<T>(
        string instanceId,
        Func<IDownloadClient, CancellationToken, Task<T>> operation,
        bool bypassOpenCircuit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(operation);
        if (!_instances.TryGetValue(instanceId, out var state))
        {
            throw new KeyNotFoundException($"Downloader instance '{instanceId}' is not enabled or configured.");
        }

        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!bypassOpenCircuit)
            {
                ThrowIfOpen(instanceId, state);
            }

            try
            {
                var result = await operation(
                    _registry.GetRequired(instanceId), cancellationToken).ConfigureAwait(false);
                Reset(state);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsCircuitFailure(exception))
            {
                RecordFailure(state);
                throw;
            }
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private void ThrowIfOpen(string instanceId, InstanceState state)
    {
        lock (_circuitLock)
        {
            if (state.RetryAtUtc is { } retryAtUtc && retryAtUtc > _timeProvider.GetUtcNow())
            {
                throw new DownloadClientCircuitOpenException(instanceId, retryAtUtc);
            }
        }
    }

    private void Reset(InstanceState state)
    {
        lock (_circuitLock)
        {
            state.ConsecutiveFailures = 0;
            state.RetryAtUtc = null;
        }
    }

    private void RecordFailure(InstanceState state)
    {
        lock (_circuitLock)
        {
            state.ConsecutiveFailures++;
            var exponent = Math.Min(state.ConsecutiveFailures - 1, 6);
            var delay = TimeSpan.FromTicks(InitialBackoff.Ticks * (1L << exponent));
            if (delay > MaximumBackoff)
            {
                delay = MaximumBackoff;
            }

            state.RetryAtUtc = _timeProvider.GetUtcNow() + delay;
        }
    }

    private static bool IsCircuitFailure(Exception exception) =>
        exception is HttpRequestException
            or TaskCanceledException
            or IOException
            or InvalidOperationException;

    private sealed class InstanceState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public int ConsecutiveFailures { get; set; }

        public DateTimeOffset? RetryAtUtc { get; set; }
    }
}
