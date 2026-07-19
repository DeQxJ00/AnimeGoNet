using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Downloads;

public sealed class DownloadJobStoreTests
{
    [Fact]
    public async Task SnapshotUpdatesCanonicalProgressAndDownloaderHealth()
    {
        await using var fixture = await DownloadJobFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        var result = await fixture.Jobs.ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(fixture.InfoHash, "Episode", DownloadTaskState.Downloading, 0.5, 50, 100, 10, 5, 3, 7)],
            now);

        Assert.Equal(1, result.ActiveJobs);
        Assert.Equal(1, result.MatchedJobs);
        var item = Assert.Single(await fixture.Jobs.ListAsync());
        Assert.Equal("downloading", item.State);
        Assert.Equal("downloading", item.BusinessStatus);
        Assert.Equal(0.5, item.Progress);
        Assert.Equal(3, item.Seeds);
        Assert.Equal(7, item.Peers);
        Assert.False(item.IsStale);
        Assert.True(item.DownloaderConnected);
        Assert.Null(item.DownloaderFailureCode);
        Assert.Equal(now, item.DownloaderLastSuccessAtUtc);
    }

    [Fact]
    public async Task OfflineInstanceKeepsLastSnapshotAndMarksOnlyItsJobsStale()
    {
        await using var fixture = await DownloadJobFixture.CreateAsync();
        var first = DateTimeOffset.UtcNow.AddSeconds(-1);
        await fixture.Jobs.ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(fixture.InfoHash, "Episode", DownloadTaskState.Downloading, 0.25, 25, 100, 5, 15)],
            first);

        await fixture.Jobs.MarkInstanceUnavailableAsync("bt", "qbittorrent_http_error", DateTimeOffset.UtcNow);

        var item = Assert.Single(await fixture.Jobs.ListAsync());
        Assert.Equal(0.25, item.Progress);
        Assert.Equal(25, item.DownloadedBytes);
        Assert.True(item.IsStale);
        Assert.False(item.DownloaderConnected);
        Assert.Equal("qbittorrent_http_error", item.DownloaderFailureCode);
        Assert.Equal(first, item.DownloaderLastSuccessAtUtc);
    }

    [Fact]
    public async Task DownloadErrorRemainsPollableAndCanRecover()
    {
        await using var fixture = await DownloadJobFixture.CreateAsync();
        await fixture.Jobs.ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(fixture.InfoHash, "Episode", DownloadTaskState.Error, 0.25, 25, 100, 0, null)],
            DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.Equal(1, await fixture.Jobs.CountActiveAsync());

        await fixture.Jobs.ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(fixture.InfoHash, "Episode", DownloadTaskState.Downloading, 0.5, 50, 100, 10, 5)],
            DateTimeOffset.UtcNow);

        var item = Assert.Single(await fixture.Jobs.ListAsync());
        Assert.Equal("downloading", item.BusinessStatus);
        Assert.False(item.IsStale);
    }

    [Fact]
    public async Task SeedingIsDownloadedButRemainsPollableUntilOrganizingStarts()
    {
        await using var fixture = await DownloadJobFixture.CreateAsync();
        await fixture.Jobs.ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(fixture.InfoHash, "Episode", DownloadTaskState.Seeding, 1, 100, 100, 0, null, 8, 2)],
            DateTimeOffset.UtcNow);

        var item = Assert.Single(await fixture.Jobs.ListAsync());
        Assert.Equal("seeding", item.State);
        Assert.Equal("downloaded", item.BusinessStatus);
        Assert.Equal(1, await fixture.Jobs.CountActiveAsync());
    }

    private sealed class DownloadJobFixture : IAsyncDisposable
    {
        private readonly SqliteDatabaseFixture _databaseFixture;

        private DownloadJobFixture(SqliteDatabaseFixture databaseFixture, DownloadJobStore jobs, string infoHash)
        {
            _databaseFixture = databaseFixture;
            Jobs = jobs;
            InfoHash = infoHash;
        }

        public DownloadJobStore Jobs { get; }

        public string InfoHash { get; }

        public static async Task<DownloadJobFixture> CreateAsync()
        {
            var databaseFixture = await SqliteDatabaseFixture.CreateAsync();
            var profiles = new SourceProfileStore(databaseFixture.Database);
            await profiles.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
            var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
            var normalized = Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
                "mikan",
                new IngestItemCommand(
                    "https://mikanani.me/passkey/file.torrent",
                    new IngestItemInfo("Episode", null, "one", "3951", null, null, 3951, 547888, null, null))).Item);
            var hash = new string('d', 40);
            var tasks = new IngestTaskStore(databaseFixture.Database);
            await tasks.AddStagedAsync(
                normalized,
                profile,
                new TorrentMetadata("episode.mkv", hash, 100, [new TorrentFile("episode.mkv", 100, false)]),
                "download-job.torrent",
                DateTimeOffset.UtcNow.AddMinutes(15));
            var claim = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1)));
            await tasks.CompleteDispatchAsync(
                claim,
                new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Waiting, 0, 0, 100, 0, null),
                DateTimeOffset.UtcNow);
            return new DownloadJobFixture(databaseFixture, new DownloadJobStore(databaseFixture.Database), hash);
        }

        public ValueTask DisposeAsync() => _databaseFixture.DisposeAsync();
    }
}
