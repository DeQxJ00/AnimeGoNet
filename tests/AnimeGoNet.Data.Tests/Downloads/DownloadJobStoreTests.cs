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
        var page = await fixture.Jobs.ListPageAsync(
            new DownloadJobListQuery(1, 10, null, null, null, null, null));
        Assert.Equal(1, page.Summary.StaleJobs);
        Assert.Equal(1, page.Summary.OfflineInstanceCount);
        Assert.Equal(0, page.Summary.ConnectedDownloadSpeedBytesPerSecond);
        Assert.Equal("qbittorrent_http_error", page.Summary.LatestFailureCode);
        Assert.Equal(first, page.Summary.LastDownloaderSuccessAtUtc);
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

    [Fact]
    public async Task SeedingLifecycleIsDurableMonotonicAndAudited()
    {
        await using var fixture = await DownloadJobFixture.CreateAsync();
        await fixture.ConfigureSeedingAsync(30, "waiting");
        var seedingAt = DateTimeOffset.UtcNow.AddMinutes(-20);
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        await fixture.Jobs.ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(
                fixture.InfoHash, "Episode", DownloadTaskState.Seeding,
                1, 100, 100, 0, null, 8, 2, 600)],
            seedingAt);
        var seeding = Assert.Single(await fixture.Jobs.ListAsync());

        Assert.Equal("seeding", seeding.SeedingState);
        Assert.Equal(30, seeding.SeedingTargetMinutes);
        Assert.Equal(600, seeding.SeedingElapsedSeconds);
        Assert.Null(seeding.SeedingCompletedAtUtc);

        await fixture.Jobs.ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(
                fixture.InfoHash, "Episode", DownloadTaskState.Seeding,
                1, 100, 100, 0, null, 8, 2, 1_800)],
            completedAt);
        await fixture.Jobs.ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(
                fixture.InfoHash, "Episode", DownloadTaskState.Downloading,
                1, 100, 100, 0, null, 8, 2, 1_200)],
            DateTimeOffset.UtcNow);
        var detail = Assert.IsType<DownloadJobDetailRecord>(
            await fixture.Jobs.GetDetailAsync(seeding.JobId));

        Assert.Equal("completed", detail.Summary.SeedingState);
        Assert.Equal(1_800, detail.Summary.SeedingElapsedSeconds);
        Assert.Equal(completedAt, detail.Summary.SeedingCompletedAtUtc);
        Assert.Contains(
            detail.Events,
            value => value.Kind == "seeding_state"
                && value.FromState == "waiting"
                && value.ToState == "seeding");
        Assert.Contains(
            detail.Events,
            value => value.Kind == "seeding_state"
                && value.FromState == "seeding"
                && value.ToState == "completed");
    }

    [Fact]
    public async Task ListPageFiltersAndDetailExposeFilesAndAuditTimeline()
    {
        await using var fixture = await DownloadJobFixture.CreateAsync();
        await fixture.Jobs.ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(
                fixture.InfoHash,
                "Episode",
                DownloadTaskState.Downloading,
                0.5,
                50,
                100,
                10,
                5)],
            DateTimeOffset.UtcNow);

        var page = await fixture.Jobs.ListPageAsync(
            new DownloadJobListQuery(
                1, 10, "EPISODE", "DOWNLOADING", "DOWNLOADING", "BT", "MIKAN"));
        var item = Assert.Single(page.Items);
        var detail = Assert.IsType<DownloadJobDetailRecord>(
            await fixture.Jobs.GetDetailAsync(item.JobId));

        Assert.Equal(1, page.TotalItems);
        Assert.Equal(1, page.Summary.TotalJobs);
        Assert.Equal(1, page.Summary.ActiveJobs);
        Assert.Equal(10, page.Summary.ConnectedDownloadSpeedBytesPerSecond);
        Assert.Equal("episode.mkv", Assert.Single(detail.Files).RelativePath);
        Assert.Contains(detail.Events, value => value.Kind == "dispatch_confirmed");
        Assert.Contains(
            detail.Events,
            value => value.Kind == "snapshot_sync"
                && value.FromState == "waiting"
                && value.ToState == "downloading");
    }

    [Fact]
    public async Task RemoteControlUsesRevisionAndRecordsSuccessfulTransition()
    {
        await using var fixture = await DownloadJobFixture.CreateAsync();
        await fixture.Jobs.ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(
                fixture.InfoHash,
                "Episode",
                DownloadTaskState.Downloading,
                0.25,
                25,
                100,
                5,
                15)],
            DateTimeOffset.UtcNow);
        var item = Assert.Single(await fixture.Jobs.ListAsync());
        var target = Assert.IsType<DownloadJobControlTarget>(
            await fixture.Jobs.GetControlTargetAsync(item.JobId));

        var updated = await fixture.Jobs.ApplyRemoteControlAsync(
            target,
            "pause",
            "paused",
            DateTimeOffset.UtcNow);
        var staleUpdate = await fixture.Jobs.ApplyRemoteControlAsync(
            target,
            "pause",
            "paused",
            DateTimeOffset.UtcNow);
        var detail = Assert.IsType<DownloadJobDetailRecord>(
            await fixture.Jobs.GetDetailAsync(item.JobId));

        Assert.Equal(DownloadJobControlUpdateResult.Updated, updated);
        Assert.Equal(DownloadJobControlUpdateResult.RevisionConflict, staleUpdate);
        Assert.Equal("paused", detail.Summary.State);
        Assert.Contains(
            detail.Events,
            value => value.Kind == "pause"
                && value.Result == "succeeded"
                && value.FromState == "downloading"
                && value.ToState == "paused");
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

        public async Task ConfigureSeedingAsync(int targetMinutes, string state)
        {
            await using var connection = await _databaseFixture.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE download_jobs
                SET seeding_target_minutes = $target,
                    seeding_state = $state,
                    seeding_elapsed_seconds = 0,
                    seeding_completed_at_utc = NULL;
                """;
            command.Parameters.AddWithValue("$target", targetMinutes);
            command.Parameters.AddWithValue("$state", state);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

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
                "/download/incomplete/bt",
                "/download/anime",
                DateTimeOffset.UtcNow);
            await using (var connection = await databaseFixture.Database.OpenConnectionAsync())
            await using (var ready = connection.CreateCommand())
            {
                ready.CommandText = """
                    UPDATE download_jobs
                    SET preparation_state = 'completed'
                    WHERE task_id = $task_id;
                    UPDATE ingest_tasks
                    SET status = 'download_queued'
                    WHERE id = $task_id;
                    """;
                ready.Parameters.AddWithValue("$task_id", claim.TaskId);
                Assert.Equal(2, await ready.ExecuteNonQueryAsync());
            }
            return new DownloadJobFixture(databaseFixture, new DownloadJobStore(databaseFixture.Database), hash);
        }

        public ValueTask DisposeAsync() => _databaseFixture.DisposeAsync();
    }
}
