using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Library;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Library;

public sealed class MediaOrganizationStoreTests
{
    [Fact]
    public async Task MovesAndCleanupAreSeparateCrashRecoverableStages()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var claim = Assert.IsType<MediaOrganizationClaim>(await fixture.Store.TryClaimNextAsync(
            now, TimeSpan.FromMinutes(1)));
        Assert.Equal(MediaOrganizationStage.MoveFiles, claim.Stage);
        var file = Assert.Single(claim.Files);

        var operations = await fixture.Store.EnsureOperationsAsync(
            claim,
            [new MediaOperationPlan(file.TaskFileId, "/download/incomplete/bt/episode.mkv", "/download/anime/Series/S01/E001.mkv")],
            now);
        var operation = Assert.Single(operations);
        await fixture.Store.CompleteFileAsync(claim, operation.OperationId, file.SizeBytes, now);
        await fixture.Store.CompleteMovesAsync(claim, now);

        var afterMoves = await fixture.ReadStateAsync();
        Assert.Equal("organizing_cleanup", afterMoves.TaskStatus);
        Assert.Equal("cleanup", afterMoves.OrganizationState);
        Assert.Equal(1, afterMoves.CompletionCount);
        Assert.Equal("/download/anime/Series/S01/E001.mkv", afterMoves.MediaPath);

        var cleanup = Assert.IsType<MediaOrganizationClaim>(await fixture.Store.TryClaimNextAsync(
            now.AddSeconds(1), TimeSpan.FromMinutes(1)));
        Assert.Equal(MediaOrganizationStage.CleanupDownloader, cleanup.Stage);
        Assert.Empty(cleanup.Files);
        await fixture.Store.CompleteCleanupAsync(cleanup, now.AddSeconds(1));

        var completed = await fixture.ReadStateAsync();
        Assert.Equal("organized", completed.TaskStatus);
        Assert.Equal("completed", completed.OrganizationState);
    }

    [Fact]
    public async Task ConcurrentWorkersClaimMoveStageOnceAndRetryHonorsTime()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var claims = await Task.WhenAll(
            fixture.Store.TryClaimNextAsync(now, TimeSpan.FromMinutes(1)),
            fixture.Store.TryClaimNextAsync(now, TimeSpan.FromMinutes(1)));
        var claim = Assert.Single(claims, item => item is not null)!;

        await fixture.Store.ReleaseAsync(claim, "target_conflict", now.AddSeconds(30), now);
        Assert.Null(await fixture.Store.TryClaimNextAsync(now.AddSeconds(29), TimeSpan.FromMinutes(1)));
        var retried = Assert.IsType<MediaOrganizationClaim>(await fixture.Store.TryClaimNextAsync(
            now.AddSeconds(30), TimeSpan.FromMinutes(1)));
        Assert.Equal(2, retried.AttemptCount);
    }

    [Fact]
    public async Task CannotWriteCompletionBeforeEveryFileOperationCompletes()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var claim = Assert.IsType<MediaOrganizationClaim>(await fixture.Store.TryClaimNextAsync(
            now, TimeSpan.FromMinutes(1)));
        var file = Assert.Single(claim.Files);
        _ = await fixture.Store.EnsureOperationsAsync(
            claim,
            [new MediaOperationPlan(file.TaskFileId, "/download/incomplete/bt/episode.mkv", "/download/anime/Series/S01/E001.mkv")],
            now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CompleteMovesAsync(claim, now));
        Assert.Equal(0, (await fixture.ReadStateAsync()).CompletionCount);
    }

    private sealed class OrganizationFixture : IAsyncDisposable
    {
        private readonly SqliteDatabaseFixture _database;

        private OrganizationFixture(SqliteDatabaseFixture database, MediaOrganizationStore store, string taskId)
        {
            _database = database;
            Store = store;
            TaskId = taskId;
        }

        public MediaOrganizationStore Store { get; }

        public string TaskId { get; }

        public static async Task<OrganizationFixture> CreateAsync()
        {
            var database = await SqliteDatabaseFixture.CreateAsync();
            var profiles = new SourceProfileStore(database.Database);
            await profiles.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
            var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
            var normalized = Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
                "mikan",
                new IngestItemCommand(
                    "https://mikanani.me/passkey/organization.torrent",
                    new IngestItemInfo("Episode", null, "one", "3951", null, null, 3951, 547888, null, null))).Item);
            var hash = new string('f', 40);
            var tasks = new IngestTaskStore(database.Database);
            var task = await tasks.AddStagedAsync(
                normalized,
                profile,
                new TorrentMetadata("episode.mkv", hash, 5, [new TorrentFile("episode.mkv", 5, false)]),
                "organization.torrent",
                DateTimeOffset.UtcNow.AddMinutes(15));
            var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
                DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
            await tasks.CompleteDispatchAsync(
                dispatch,
                new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Paused, 0, 0, 5, 0, null),
                "/download/incomplete/bt",
                "/download/anime",
                DateTimeOffset.UtcNow);

            await using var connection = await database.Database.OpenConnectionAsync();
            await using var setup = connection.CreateCommand();
            setup.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, canonical_name, original_name,
                    needs_tmdb_completion, created_at_utc, updated_at_utc)
                VALUES ('series', 100, 'Series', 'Series', 0, $now, $now);
                UPDATE task_files SET disposition = 'episode', tmdb_series_id = 100,
                    tmdb_season_number = 1, tmdb_episode_number = 1,
                    tmdb_episode_id = 1001, download_wanted = 1
                WHERE task_id = $task_id;
                UPDATE download_jobs SET preparation_state = 'completed', state = 'complete', progress = 1
                WHERE task_id = $task_id;
                UPDATE ingest_tasks SET status = 'downloaded' WHERE id = $task_id;
                """;
            setup.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            setup.Parameters.AddWithValue("$task_id", task.Id);
            Assert.Equal(4, await setup.ExecuteNonQueryAsync());
            return new OrganizationFixture(database, new MediaOrganizationStore(database.Database), task.Id);
        }

        public async Task<State> ReadStateAsync()
        {
            await using var connection = await _database.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT task.status, job.organization_state,
                       (SELECT COUNT(*) FROM completion_records),
                       (SELECT media_path FROM completion_records LIMIT 1)
                FROM ingest_tasks AS task
                JOIN download_jobs AS job ON job.task_id = task.id
                WHERE task.id = $task_id;
                """;
            command.Parameters.AddWithValue("$task_id", TaskId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return new State(
                reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }

    private sealed record State(string TaskStatus, string OrganizationState, int CompletionCount, string? MediaPath);
}
