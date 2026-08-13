using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Library;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Tests.Metadata;

public sealed class OtherFileReadaptationStoreTests
{
    [Fact]
    public async Task StartsOnlyOtherFilesAndClearsMetadataForFreshResolution()
    {
        await using var fixture = await ReadaptationFixture.CreateAsync();

        var preview = Assert.IsType<OtherFileReadaptationPreview>(
            await fixture.Store.PreviewAsync(fixture.TaskId));
        var file = Assert.Single(preview.Files);
        Assert.Equal("episode_unresolved", file.OtherReason);
        Assert.Equal(100, file.TmdbSeriesId);
        Assert.Equal(2, file.TmdbSeasonNumber);
        Assert.Equal(1, file.SharedPathReferenceCount);

        Assert.Equal(
            OtherFileReadaptationStartResult.Started,
            await fixture.Store.StartAsync(fixture.TaskId, DateTimeOffset.UtcNow));

        var state = await fixture.ReadStateAsync();
        Assert.Equal("download_preparing", state.TaskStatus);
        Assert.Equal("pending", state.FileDisposition);
        Assert.Null(state.OtherReason);
        Assert.Null(state.TmdbSeriesId);
        Assert.Null(state.TmdbSeasonNumber);
        Assert.Equal("pending", state.OrganizationState);
        Assert.Equal("not_started", state.OrganizationPhase);
        Assert.Equal(0, state.OperationCount);
        Assert.Equal(1, state.ReadaptationCount);
    }

    [Fact]
    public async Task SharedMediaPathStartsWithPreserveSourceCopySemantics()
    {
        await using var fixture = await ReadaptationFixture.CreateAsync();
        await fixture.AddSharedPathReferenceAsync();

        var preview = Assert.IsType<OtherFileReadaptationPreview>(
            await fixture.Store.PreviewAsync(fixture.TaskId));
        Assert.Equal(2, Assert.Single(preview.Files).SharedPathReferenceCount);
        Assert.Equal(
            OtherFileReadaptationStartResult.Started,
            await fixture.Store.StartAsync(fixture.TaskId, DateTimeOffset.UtcNow));
        Assert.Equal("download_preparing", (await fixture.ReadStateAsync()).TaskStatus);
        Assert.True(await fixture.ReadPreserveSourceAsync());
    }

    [Fact]
    public async Task ReorganizationUsesCurrentOtherPathAndCompletesWithoutDownloaderCleanup()
    {
        await using var fixture = await ReadaptationFixture.CreateAsync();
        Assert.Equal(
            OtherFileReadaptationStartResult.Started,
            await fixture.Store.StartAsync(fixture.TaskId, DateTimeOffset.UtcNow));
        await fixture.ResolveAsEpisodeAsync(12, 10012);

        var organizations = new MediaOrganizationStore(fixture.Database);
        var now = DateTimeOffset.UtcNow;
        var claim = Assert.IsType<MediaOrganizationClaim>(await organizations.TryClaimNextAsync(
            now,
            TimeSpan.FromMinutes(1)));
        Assert.True(claim.IsOtherReadaptation);
        var file = Assert.Single(claim.Files);
        Assert.Equal(fixture.TargetPath, file.SourceOverridePath);

        var finalTarget = "/library/Series/S02/Series - S02E12.mkv";
        var operation = Assert.Single(await organizations.EnsureOperationsAsync(
            claim,
            [new MediaOperationPlan(file.TaskFileId, file.SourceOverridePath!, finalTarget)],
            now));
        await organizations.CompleteFileAsync(claim, operation.OperationId, file.SizeBytes, now);
        await organizations.CompleteMovesAsync(claim, now);

        var state = await fixture.ReadStateAsync();
        Assert.Equal("organized", state.TaskStatus);
        Assert.Equal("completed", state.OrganizationState);
        Assert.Equal("completed", state.OrganizationPhase);
        Assert.Equal(0, state.ReadaptationCount);
        Assert.Null(await organizations.TryClaimNextAsync(now.AddSeconds(1), TimeSpan.FromMinutes(1)));

        var review = Assert.IsType<OtherFileReadaptationReviewPreview>(
            await fixture.Store.GetReviewPreviewAsync(fixture.TaskId));
        Assert.Equal("pending", review.ReviewState);
        Assert.NotEqual(DateTimeOffset.MinValue, review.RequestedAtUtc);
        Assert.NotNull(review.CompletedAtUtc);
        var comparison = Assert.Single(review.Files);
        Assert.Equal("episode.mkv", comparison.SourceName);
        Assert.Equal("other", comparison.BeforeDisposition);
        Assert.Equal("episode_unresolved", comparison.BeforeOtherReason);
        Assert.Equal(100, comparison.BeforeTmdbSeriesId);
        Assert.Equal("Series", comparison.BeforeSeriesName);
        Assert.Equal(2, comparison.BeforeTmdbSeasonNumber);
        Assert.Null(comparison.BeforeTmdbEpisodeNumber);
        Assert.Equal("episode", comparison.AfterDisposition);
        Assert.Null(comparison.AfterOtherReason);
        Assert.Equal(100, comparison.AfterTmdbSeriesId);
        Assert.Equal(2, comparison.AfterTmdbSeasonNumber);
        Assert.Equal(12, comparison.AfterTmdbEpisodeNumber);
        Assert.False(comparison.PreservedSharedSource);
        Assert.Equal(fixture.TargetPath, comparison.BeforeMediaPath);
        Assert.Equal(finalTarget, comparison.AfterMediaPath);
    }

    private sealed class ReadaptationFixture : IAsyncDisposable
    {
        private readonly SqliteDatabaseFixture _database;

        private ReadaptationFixture(
            SqliteDatabaseFixture database,
            OtherFileReadaptationStore store,
            string taskId,
            string fileId,
            string targetPath)
        {
            _database = database;
            Store = store;
            TaskId = taskId;
            FileId = fileId;
            TargetPath = targetPath;
        }

        public OtherFileReadaptationStore Store { get; }

        public string TaskId { get; }

        private string FileId { get; }

        public string TargetPath { get; }

        public AnimeGoSqliteDatabase Database => _database.Database;

        public static async Task<ReadaptationFixture> CreateAsync()
        {
            var database = await SqliteDatabaseFixture.CreateAsync();
            var profiles = new SourceProfileStore(database.Database);
            await profiles.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
            var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
            var normalized = Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
                "mikan",
                new IngestItemCommand(
                    "https://mikanani.me/passkey/readaptation.torrent",
                    new IngestItemInfo("Readaptation", null, "one", "3951", null, null, 3951, 547888, null, null))).Item);
            var hash = new string('a', 40);
            var tasks = new IngestTaskStore(database.Database);
            var task = await tasks.AddStagedAsync(
                normalized,
                profile,
                new TorrentMetadata("episode.mkv", hash, 5, [new TorrentFile("episode.mkv", 5, false)]),
                "readaptation.torrent",
                DateTimeOffset.UtcNow.AddMinutes(15));
            var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1)));
            await tasks.CompleteDispatchAsync(
                dispatch,
                new DownloadTaskSnapshot(hash, "Readaptation", DownloadTaskState.Complete, 1, 5, 5, 0, null),
                "/download/incomplete",
                "/library",
                DateTimeOffset.UtcNow);

            var target = "/library/Series/S02/Other/episode.mkv";
            string fileId;
            await using (var connection = await database.Database.OpenConnectionAsync())
            {
                await using var find = connection.CreateCommand();
                find.CommandText = "SELECT id FROM task_files WHERE task_id = $task_id;";
                find.Parameters.AddWithValue("$task_id", task.Id);
                fileId = Assert.IsType<string>(await find.ExecuteScalarAsync());

                await using var setup = connection.CreateCommand();
                setup.CommandText = """
                    INSERT INTO anime_series (
                        id, tmdb_series_id, canonical_name, original_name,
                        needs_tmdb_completion, created_at_utc, updated_at_utc)
                    VALUES ('series-readapt', 100, 'Series', 'Series', 0, $now, $now);
                    UPDATE task_files
                    SET disposition = 'other', other_reason = 'episode_unresolved',
                        tmdb_series_id = 100, tmdb_season_number = 2,
                        download_wanted = 1
                    WHERE id = $file_id;
                    UPDATE download_jobs
                    SET preparation_state = 'completed', organization_state = 'completed',
                        organization_phase = 'completed', organization_total_units = 1,
                        organization_completed_units = 1, state = 'complete', progress = 1
                    WHERE task_id = $task_id;
                    UPDATE ingest_tasks SET status = 'organized' WHERE id = $task_id;
                    INSERT INTO file_operations (
                        id, task_file_id, strategy, source_path, target_path, state,
                        bytes_verified, failure_reason, created_at_utc, updated_at_utc)
                    VALUES (
                        'operation-readapt', $file_id, 'move', '/download/incomplete/episode.mkv',
                        $target, 'completed', 5, NULL, $now, $now);
                    """;
                setup.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                setup.Parameters.AddWithValue("$task_id", task.Id);
                setup.Parameters.AddWithValue("$file_id", fileId);
                setup.Parameters.AddWithValue("$target", target);
                Assert.Equal(5, await setup.ExecuteNonQueryAsync());
            }

            return new ReadaptationFixture(
                database,
                new OtherFileReadaptationStore(database.Database),
                task.Id,
                fileId,
                target);
        }

        public async Task AddSharedPathReferenceAsync()
        {
            await using var connection = await _database.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, disposition, download_wanted)
                VALUES ('shared-file', $task_id, 'shared.mkv', 5, 'ignored', 0);
                INSERT INTO file_operations (
                    id, task_file_id, strategy, source_path, target_path, state,
                    bytes_verified, failure_reason, created_at_utc, updated_at_utc)
                VALUES (
                    'shared-operation', 'shared-file', 'move', '/download/incomplete/shared.mkv',
                    $target, 'completed', 5, NULL, $now, $now);
                """;
            command.Parameters.AddWithValue("$task_id", TaskId);
            command.Parameters.AddWithValue("$target", TargetPath);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(2, await command.ExecuteNonQueryAsync());
        }

        public async Task ResolveAsEpisodeAsync(int episodeNumber, int episodeId)
        {
            await using var connection = await _database.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE task_files
                SET disposition = 'episode', other_reason = NULL,
                    tmdb_series_id = 100, tmdb_season_number = 2,
                    tmdb_episode_number = $episode_number,
                    tmdb_episode_id = $episode_id
                WHERE id = $file_id;
                UPDATE ingest_tasks SET status = 'downloaded' WHERE id = $task_id;
                """;
            command.Parameters.AddWithValue("$episode_number", episodeNumber);
            command.Parameters.AddWithValue("$episode_id", episodeId);
            command.Parameters.AddWithValue("$file_id", FileId);
            command.Parameters.AddWithValue("$task_id", TaskId);
            Assert.Equal(2, await command.ExecuteNonQueryAsync());
        }

        public async Task<bool> ReadPreserveSourceAsync()
        {
            await using var connection = await _database.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT preserve_source FROM other_file_readaptation_jobs
                WHERE task_file_id = $file_id AND state = 'pending';
                """;
            command.Parameters.AddWithValue("$file_id", FileId);
            return Convert.ToInt64(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture) == 1;
        }

        public async Task<State> ReadStateAsync()
        {
            await using var connection = await _database.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT task.status, file.disposition, file.other_reason,
                       file.tmdb_series_id, file.tmdb_season_number,
                       job.organization_state, job.organization_phase,
                       (SELECT COUNT(*) FROM file_operations WHERE task_file_id = $file_id),
                       (SELECT COUNT(*) FROM other_file_readaptation_jobs
                        WHERE task_file_id = $file_id AND state = 'pending')
                FROM ingest_tasks AS task
                JOIN task_files AS file ON file.task_id = task.id AND file.id = $file_id
                JOIN download_jobs AS job ON job.task_id = task.id
                WHERE task.id = $task_id;
                """;
            command.Parameters.AddWithValue("$task_id", TaskId);
            command.Parameters.AddWithValue("$file_id", FileId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return new State(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7),
                reader.GetInt32(8));
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }

    private sealed record State(
        string TaskStatus,
        string FileDisposition,
        string? OtherReason,
        int? TmdbSeriesId,
        int? TmdbSeasonNumber,
        string OrganizationState,
        string OrganizationPhase,
        int OperationCount,
        int ReadaptationCount);
}
