using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Deletion;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Deletion;

public sealed class DeletePlanStoreTests
{
    [Fact]
    public async Task PreviewSeparatesDeleteTargetKindsAndCreateFreezesSelectedTargets()
    {
        await using var fixture = await DeleteFixture.CreateAsync();
        var preview = Assert.IsType<DeletePlanPreview>(await fixture.Store.GetPreviewAsync(fixture.TaskId));

        Assert.Equal(64, preview.Fingerprint.Length);
        Assert.Single(preview.BusinessRecords);
        Assert.Single(preview.DownloaderTasks);
        Assert.Single(preview.SourceFiles);
        Assert.Single(preview.MediaFiles);
        Assert.Single(preview.TaskRecords);
        Assert.True(preview.TaskRecordDeletionAllowed);
        Assert.Equal("/download/incomplete/bt/episode.mkv", preview.SourceFiles[0].TargetKey);
        Assert.Equal("/download/incomplete/bt", preview.SourceFiles[0].RootPath);
        Assert.Equal("/download/anime/Series/S01/E001.mkv", preview.MediaFiles[0].TargetKey);
        Assert.Equal("/download/anime", preview.MediaFiles[0].RootPath);

        var selection = new DeleteSelection(true, false, false, true);
        var plan = await fixture.Store.CreateAsync(
            fixture.TaskId, preview.Fingerprint, selection, DateTimeOffset.UtcNow);

        Assert.Equal("pending", plan.State);
        Assert.Equal(2, plan.Targets.Count);
        Assert.Contains(plan.Targets, item => item.ItemKind == DeleteItemKinds.BusinessRecord);
        Assert.Contains(plan.Targets, item => item.ItemKind == DeleteItemKinds.MediaFile);
        var persisted = await fixture.ReadPersistedAsync(plan.ExecutionId);
        Assert.Equal(2, persisted.ItemCount);
        Assert.Equal(1, persisted.DeleteBusinessRecord);
        Assert.Equal(0, persisted.DeleteDownloaderTask);
        Assert.Equal(0, persisted.DeleteSourceFiles);
        Assert.Equal(1, persisted.DeleteMediaFiles);
    }

    [Fact]
    public async Task PreviewDoesNotDuplicateOneMediaPathAsDownloadSourceAndMediaTarget()
    {
        await using var fixture = await DeleteFixture.CreateAsync();
        await fixture.MakeSourcePathEqualMediaPathAsync();

        var preview = Assert.IsType<DeletePlanPreview>(
            await fixture.Store.GetPreviewAsync(fixture.TaskId));

        Assert.Empty(preview.SourceFiles);
        var media = Assert.Single(preview.MediaFiles);
        Assert.Equal("/download/anime/Series/S01/E001.mkv", media.TargetKey);
        Assert.Equal("/download/anime", media.RootPath);
    }

    [Fact]
    public async Task PendingReadaptationReviewBlocksTaskRecordDeletion()
    {
        await using var fixture = await DeleteFixture.CreateAsync();
        await fixture.SetReviewStateAsync("pending");
        var preview = Assert.IsType<DeletePlanPreview>(await fixture.Store.GetPreviewAsync(fixture.TaskId));

        Assert.False(preview.TaskRecordDeletionAllowed);
        Assert.Contains("人工审核", preview.TaskRecordDeletionDenialReason, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CreateAsync(
            fixture.TaskId, preview.Fingerprint,
            new DeleteSelection(false, true, false, false, true), DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task CreateRejectsStalePreviewAndEmptySelection()
    {
        await using var fixture = await DeleteFixture.CreateAsync();
        var preview = Assert.IsType<DeletePlanPreview>(await fixture.Store.GetPreviewAsync(fixture.TaskId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CreateAsync(
            fixture.TaskId, new string('0', 64), new DeleteSelection(true, false, false, false),
            DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.CreateAsync(
            fixture.TaskId, preview.Fingerprint, new DeleteSelection(false, false, false, false),
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task OnlyOneActivePlanCanExistForATask()
    {
        await using var fixture = await DeleteFixture.CreateAsync();
        var preview = Assert.IsType<DeletePlanPreview>(await fixture.Store.GetPreviewAsync(fixture.TaskId));
        var selection = new DeleteSelection(false, true, false, false);
        _ = await fixture.Store.CreateAsync(fixture.TaskId, preview.Fingerprint, selection, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => fixture.Store.CreateAsync(
            fixture.TaskId, preview.Fingerprint, selection, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task MoviePreviewAndExecutionUseMovieRootAndIndependentCompletionRecord()
    {
        await using var fixture = await DeleteFixture.CreateAsync();
        await fixture.ConvertToMovieAsync();
        var preview = Assert.IsType<DeletePlanPreview>(
            await fixture.Store.GetPreviewAsync(fixture.TaskId));

        var business = Assert.Single(preview.BusinessRecords);
        Assert.Equal("movie:movie-completion", business.TargetKey);
        Assert.Contains("TMDB Movie 10681", business.DisplayValue, StringComparison.Ordinal);
        var media = Assert.Single(preview.MediaFiles);
        Assert.Equal("/download/movies/萤火之森 (2011)/萤火之森 (2011).mkv", media.TargetKey);
        Assert.Equal("/download/movies", media.RootPath);

        var plan = await fixture.Store.CreateAsync(
            fixture.TaskId,
            preview.Fingerprint,
            new DeleteSelection(true, false, false, false),
            DateTimeOffset.UtcNow);
        var executions = new DeleteExecutionStore(fixture.Database);
        var claim = Assert.IsType<DeleteExecutionClaim>(await executions.TryClaimAsync(
            plan.ExecutionId,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        await executions.CompleteBusinessRecordAsync(
            claim,
            Assert.Single(claim.Items),
            DateTimeOffset.UtcNow);
        await executions.CompleteAsync(claim, DateTimeOffset.UtcNow);

        Assert.Equal((0, 0), await fixture.ReadMovieBusinessCountsAsync());
    }

    [Fact]
    public async Task MixedTvTaskMovieFileUsesConfiguredMovieRoot()
    {
        await using var fixture = await DeleteFixture.CreateAsync();
        await fixture.ConvertToMixedMovieAsync();

        var preview = Assert.IsType<DeletePlanPreview>(
            await fixture.Store.GetPreviewAsync(fixture.TaskId));
        var media = Assert.Single(preview.MediaFiles);
        Assert.Equal("/download/movies/萤火之森 (2011)/萤火之森 (2011).mkv", media.TargetKey);
        Assert.Equal("/download/movies", media.RootPath);
    }

    private sealed class DeleteFixture : IAsyncDisposable
    {
        private readonly SqliteDatabaseFixture _database;

        private DeleteFixture(SqliteDatabaseFixture database, DeletePlanStore store, string taskId)
        {
            _database = database;
            Store = store;
            TaskId = taskId;
        }

        public DeletePlanStore Store { get; }
        public string TaskId { get; }
        public AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase Database => _database.Database;

        public static async Task<DeleteFixture> CreateAsync()
        {
            var database = await SqliteDatabaseFixture.CreateAsync();
            var profiles = new SourceProfileStore(database.Database);
            await profiles.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
            var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
            var normalized = Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
                "mikan",
                new IngestItemCommand(
                    "https://mikanani.me/passkey/delete.torrent",
                    new IngestItemInfo("Episode", null, "delete-one", "3951", null, null, 3951, 547888, null, null))).Item);
            var hash = new string('d', 40);
            var tasks = new IngestTaskStore(database.Database);
            var task = await tasks.AddStagedAsync(
                normalized, profile,
                new TorrentMetadata("episode.mkv", hash, 5, [new TorrentFile("episode.mkv", 5, false)]),
                "delete.torrent", DateTimeOffset.UtcNow.AddMinutes(15));
            var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
                DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
            await tasks.CompleteDispatchAsync(
                dispatch,
                new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Complete, 1, 5, 5, 0, 0),
                "/download/incomplete/bt", "/download/anime", DateTimeOffset.UtcNow);

            await using var connection = await database.Database.OpenConnectionAsync();
            await using var setup = connection.CreateCommand();
            setup.CommandText = """
                UPDATE task_files SET disposition = 'episode', tmdb_series_id = 100,
                    tmdb_season_number = 1, tmdb_episode_number = 1, tmdb_episode_id = 1001
                WHERE task_id = $task_id;
                INSERT INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, source_item_id, media_path, completed_at_utc)
                VALUES ('completion', 100, 1, 1, 'mikan', 'delete-one',
                        '/download/anime/Series/S01/E001.mkv', $now);
                INSERT INTO file_operations (
                    id, task_file_id, strategy, source_path, target_path, state,
                    bytes_verified, created_at_utc, updated_at_utc)
                SELECT 'operation', id, 'move', '/download/incomplete/bt/episode.mkv',
                       '/download/anime/Series/S01/E001.mkv', 'completed', 5, $now, $now
                FROM task_files WHERE task_id = $task_id;
                """;
            setup.Parameters.AddWithValue("$task_id", task.Id);
            setup.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(3, await setup.ExecuteNonQueryAsync());
            var options = AnimeGoDefaults.CreateDocker();
            options = options with
            {
                Paths = options.Paths with { MovieSavePath = "/download/movies" },
            };
            return new DeleteFixture(database, new DeletePlanStore(database.Database, options), task.Id);
        }

        public async Task<PersistedState> ReadPersistedAsync(string executionId)
        {
            await using var connection = await _database.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT execution.delete_business_record, execution.delete_downloader_task,
                       execution.delete_source_files, execution.delete_media_files,
                       COUNT(item.id)
                FROM delete_executions AS execution
                LEFT JOIN delete_execution_items AS item ON item.execution_id = execution.id
                WHERE execution.id = $id
                GROUP BY execution.id;
                """;
            command.Parameters.AddWithValue("$id", executionId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return new PersistedState(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4));
        }

        public async Task SetReviewStateAsync(string state)
        {
            await using var connection = await _database.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ingest_tasks SET readaptation_review_state = $state WHERE id = $task_id;
                """;
            command.Parameters.AddWithValue("$state", state);
            command.Parameters.AddWithValue("$task_id", TaskId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        public async Task MakeSourcePathEqualMediaPathAsync()
        {
            await using var connection = await _database.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE file_operations
                SET source_path = target_path
                WHERE task_file_id IN (SELECT id FROM task_files WHERE task_id = $task_id);
                """;
            command.Parameters.AddWithValue("$task_id", TaskId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        public async Task ConvertToMovieAsync()
        {
            await using var connection = await _database.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO anime_movies (
                    id, tmdb_movie_id, canonical_title, original_title,
                    release_date, poster_path, created_at_utc, updated_at_utc)
                VALUES (
                    'movie', 10681, '萤火之森', '蛍火の杜へ',
                    '2011-09-17', NULL, $now, $now);
                UPDATE ingest_tasks SET media_type = 'movie' WHERE id = $task_id;
                UPDATE task_files
                SET disposition = 'movie', other_reason = NULL,
                    tmdb_series_id = NULL, tmdb_season_number = NULL,
                    tmdb_episode_number = NULL, tmdb_episode_id = NULL,
                    tmdb_movie_id = 10681
                WHERE task_id = $task_id;
                DELETE FROM completion_records WHERE id = 'completion';
                INSERT INTO movie_completion_records (
                    id, tmdb_movie_id, source_id, source_item_id,
                    media_path, completed_at_utc)
                VALUES (
                    'movie-completion', 10681, 'mikan', 'delete-one',
                    '/download/movies/萤火之森 (2011)/萤火之森 (2011).mkv', $now);
                INSERT INTO movie_claims (
                    id, tmdb_movie_id, task_file_id, state, claimed_at_utc)
                SELECT 'movie-claim', 10681, id, 'completed', $now
                FROM task_files WHERE task_id = $task_id;
                UPDATE download_jobs SET save_root_path = '/download/movies' WHERE task_id = $task_id;
                UPDATE file_operations
                SET target_path = '/download/movies/萤火之森 (2011)/萤火之森 (2011).mkv'
                WHERE task_file_id IN (SELECT id FROM task_files WHERE task_id = $task_id);
                """;
            command.Parameters.AddWithValue("$task_id", TaskId);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(8, await command.ExecuteNonQueryAsync());
        }

        public async Task ConvertToMixedMovieAsync()
        {
            await using var connection = await _database.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO anime_movies (
                    id, tmdb_movie_id, canonical_title, original_title,
                    release_date, poster_path, created_at_utc, updated_at_utc)
                VALUES (
                    'mixed-movie', 10681, '萤火之森', '蛍火の杜へ',
                    '2011-09-17', NULL, $now, $now);
                UPDATE task_files
                SET disposition = 'movie', other_reason = NULL,
                    tmdb_series_id = NULL, tmdb_season_number = NULL,
                    tmdb_episode_number = NULL, tmdb_episode_id = NULL,
                    tmdb_movie_id = 10681
                WHERE task_id = $task_id;
                UPDATE file_operations
                SET target_path = '/download/movies/萤火之森 (2011)/萤火之森 (2011).mkv'
                WHERE task_file_id IN (SELECT id FROM task_files WHERE task_id = $task_id);
                """;
            command.Parameters.AddWithValue("$task_id", TaskId);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(3, await command.ExecuteNonQueryAsync());
        }

        public async Task<(int Completions, int Claims)> ReadMovieBusinessCountsAsync()
        {
            await using var connection = await _database.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT (SELECT COUNT(*) FROM movie_completion_records),
                       (SELECT COUNT(*) FROM movie_claims);
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetInt32(0), reader.GetInt32(1));
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }

    private sealed record PersistedState(
        int DeleteBusinessRecord,
        int DeleteDownloaderTask,
        int DeleteSourceFiles,
        int DeleteMediaFiles,
        int ItemCount);
}
