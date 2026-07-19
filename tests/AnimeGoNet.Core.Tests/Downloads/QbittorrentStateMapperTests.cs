using AnimeGoNet.Core.Downloads;

namespace AnimeGoNet.Core.Tests.Downloads;

public sealed class QbittorrentStateMapperTests
{
    [Theory]
    [InlineData("downloading", 0.5, DownloadTaskState.Downloading)]
    [InlineData("moving", 1, DownloadTaskState.Moving)]
    [InlineData("uploading", 1, DownloadTaskState.Seeding)]
    [InlineData("stoppedDL", 0.4, DownloadTaskState.Paused)]
    [InlineData("pausedUP", 1, DownloadTaskState.Complete)]
    [InlineData("missingFiles", 0.2, DownloadTaskState.Error)]
    [InlineData("queuedDL", 0.2, DownloadTaskState.Waiting)]
    [InlineData("new-state", 0, DownloadTaskState.Unknown)]
    public void MapsUpstreamAndQbittorrentFiveStates(string state, double progress, DownloadTaskState expected)
    {
        Assert.Equal(expected, QbittorrentStateMapper.Map(state, progress));
    }
}
