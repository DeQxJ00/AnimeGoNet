using AnimeGoNet.Core.Downloads;

namespace AnimeGoNet.App.Downloads;

public sealed class DownloadClientOperationCoordinator
{
    private readonly IDownloadClientRegistry _registry;
    private readonly Dictionary<string, SemaphoreSlim> _gates;

    public DownloadClientOperationCoordinator(IDownloadClientRegistry registry)
    {
        _registry = registry;
        _gates = registry.InstanceIds.ToDictionary(
            id => id,
            _ => new SemaphoreSlim(1, 1),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> InstanceIds => _registry.InstanceIds;

    public async Task<T> ExecuteAsync<T>(
        string instanceId,
        Func<IDownloadClient, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(operation);
        if (!_gates.TryGetValue(instanceId, out var gate))
        {
            throw new KeyNotFoundException($"Downloader instance '{instanceId}' is not enabled or configured.");
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(_registry.GetRequired(instanceId), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
