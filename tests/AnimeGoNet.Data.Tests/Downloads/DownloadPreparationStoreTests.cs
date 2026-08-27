using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Downloads;

public sealed class DownloadPreparationStoreTests
{
    [Fact]
    public async Task ConcurrentWorkersClaimPreparingTaskOnlyOnce()
    {
        await using var fixture = await PreparationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        var claims = await Task.WhenAll(
            fixture.Store.TryClaimNextAsync(now, TimeSpan.FromMinutes(1)),
            fixture.Store.TryClaimNextAsync(now, TimeSpan.FromMinutes(1)));

        var claimed = Assert.Single(claims, claim => claim is not null)!;
        Assert.Equal(fixture.TaskId, claimed.TaskId);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.Single(claimed.Files);
    }

    [Fact]
    public async Task ReleasedClaimHonorsRetryTimeAndCanBeClaimedAgain()
    {
        await using var fixture = await PreparationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var first = Assert.IsType<DownloadPreparationClaim>(await fixture.Store.TryClaimNextAsync(
            now,
            TimeSpan.FromMinutes(1)));
        Assert.True(await fixture.Store.ReleaseAsync(
            first,
            "qbittorrent_metadata_pending",
            now.AddSeconds(30),
            now));

        Assert.Null(await fixture.Store.TryClaimNextAsync(now.AddSeconds(29), TimeSpan.FromMinutes(1)));
        var second = Assert.IsType<DownloadPreparationClaim>(await fixture.Store.TryClaimNextAsync(
            now.AddSeconds(30),
            TimeSpan.FromMinutes(1)));
        Assert.Equal(2, second.AttemptCount);
    }

    [Fact]
    public async Task ExpiredLeaseIsRecoveredWithoutResumingDownloader()
    {
        await using var fixture = await PreparationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        _ = Assert.IsType<DownloadPreparationClaim>(await fixture.Store.TryClaimNextAsync(
            now,
            TimeSpan.FromSeconds(10)));

        var recovered = Assert.IsType<DownloadPreparationClaim>(await fixture.Store.TryClaimNextAsync(
            now.AddSeconds(11),
            TimeSpan.FromMinutes(1)));

        Assert.Equal(2, recovered.AttemptCount);
    }

    [Fact]
    public async Task ClaimRepairsAndExcludesLegacyDotPadRows()
    {
        await using var fixture = await PreparationFixture.CreateAsync(
        [
            new TorrentFile("Show/episode.mkv", 5, false),
            new TorrentFile("Show/.pad/3", 3, false),
        ]);

        var claim = Assert.IsType<DownloadPreparationClaim>(await fixture.Store.TryClaimNextAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));

        var file = Assert.Single(claim.Files);
        Assert.Equal("Show/episode.mkv", file.RelativePath);
    }

    private sealed class PreparationFixture : IAsyncDisposable
    {
        private readonly SqliteDatabaseFixture _database;

        private PreparationFixture(
            SqliteDatabaseFixture database,
            DownloadPreparationStore store,
            string taskId)
        {
            _database = database;
            Store = store;
            TaskId = taskId;
        }

        public DownloadPreparationStore Store { get; }

        public string TaskId { get; }

        public static async Task<PreparationFixture> CreateAsync(
            IReadOnlyList<TorrentFile>? torrentFiles = null)
        {
            var database = await SqliteDatabaseFixture.CreateAsync();
            var profiles = new SourceProfileStore(database.Database);
            await profiles.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
            var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
            var normalized = Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
                "mikan",
                new IngestItemCommand(
                    "https://mikanani.me/passkey/preparation.torrent",
                    new IngestItemInfo("Episode", null, "one", "3951", null, null, 3951, 547888, null, null))).Item);
            var hash = new string('e', 40);
            torrentFiles ??= [new TorrentFile("episode.mkv", 5, false)];
            var tasks = new IngestTaskStore(database.Database);
            var staged = await tasks.AddStagedAsync(
                normalized,
                profile,
                new TorrentMetadata("episode.mkv", hash, torrentFiles.Sum(file => file.Size), torrentFiles),
                "preparation.torrent",
                DateTimeOffset.UtcNow.AddMinutes(15));
            var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1)));
            await tasks.CompleteDispatchAsync(
                dispatch,
                new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Paused, 0, 0, 5, 0, null),
                "/download/incomplete/bt",
                "/download/anime",
                DateTimeOffset.UtcNow);
            await using var connection = await database.Database.OpenConnectionAsync();
            await using var ready = connection.CreateCommand();
            ready.CommandText = """
                UPDATE ingest_tasks SET status = 'metadata_resolved' WHERE id = $task_id;
                UPDATE task_files SET disposition = 'episode'
                WHERE task_id = $task_id AND other_reason IS NULL;
                """;
            ready.Parameters.AddWithValue("$task_id", staged.Id);
            Assert.Equal(
                1 + torrentFiles.Count(file => !file.IsPadding),
                await ready.ExecuteNonQueryAsync());
            return new PreparationFixture(database, new DownloadPreparationStore(database.Database), staged.Id);
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }
}
