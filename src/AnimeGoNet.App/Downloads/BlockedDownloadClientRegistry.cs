using AnimeGoNet.Core.Downloads;

namespace AnimeGoNet.App.Downloads;

public sealed class BlockedDownloadClientRegistry : IDownloadClientRegistry
{
    public IReadOnlyCollection<string> InstanceIds => [];

    public IDownloadClient GetRequired(string instanceId) =>
        throw new KeyNotFoundException(
            $"Downloader instance '{instanceId}' is blocked by a legacy configuration diagnostic.");
}
