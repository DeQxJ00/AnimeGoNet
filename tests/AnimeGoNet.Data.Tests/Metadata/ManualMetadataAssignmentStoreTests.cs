using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Metadata;

public sealed class ManualMetadataAssignmentStoreTests
{
    [Fact]
    public async Task TvAssignmentWritesEpisodeExtrasClaimsAndResolvedStateAtomically()
    {
        await using var fixture = await Fixture.CreateAsync();
        var season = new TmdbSeason(
            9001, 123, 2, "Season 2", new DateOnly(2026, 1, 1), 2, null,
            [
                new TmdbEpisode(9101, 123, 2, 1, "One", new DateOnly(2026, 1, 1)),
                new TmdbEpisode(9102, 123, 2, 2, "Two", new DateOnly(2026, 1, 8)),
            ]);

        await fixture.Store.ApplyTvAsync(
            fixture.TaskId,
            new TmdbSeries(123, "Manual TV", "Manual TV", new DateOnly(2026, 1, 1)),
            season,
            [
                new ManualTvFileAssignment(fixture.VideoFileId, 2),
                new ManualTvFileAssignment(fixture.AttachmentFileId, null),
            ],
            DateTimeOffset.UtcNow);

        await using var connection = await fixture.Database.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task.status, task.media_type,
                   video.disposition, video.tmdb_series_id, video.tmdb_season_number, video.tmdb_episode_number,
                   attachment.disposition, attachment.other_reason,
                   (SELECT COUNT(*) FROM episode_claims WHERE task_file_id = video.id AND state = 'active'),
                   (SELECT COUNT(*) FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
                    WHERE run.task_id = task.id AND attempt.strategy = 'manual_assignment')
            FROM ingest_tasks AS task
            JOIN task_files AS video ON video.id = $video_id
            JOIN task_files AS attachment ON attachment.id = $attachment_id
            WHERE task.id = $task_id;
            """;
        command.Parameters.AddWithValue("$video_id", fixture.VideoFileId);
        command.Parameters.AddWithValue("$attachment_id", fixture.AttachmentFileId);
        command.Parameters.AddWithValue("$task_id", fixture.TaskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("metadata_resolved", reader.GetString(0));
        Assert.Equal("tv", reader.GetString(1));
        Assert.Equal("episode", reader.GetString(2));
        Assert.Equal(123, reader.GetInt32(3));
        Assert.Equal(2, reader.GetInt32(4));
        Assert.Equal(2, reader.GetInt32(5));
        Assert.Equal("extras", reader.GetString(6));
        Assert.Equal("manual_tv_extra", reader.GetString(7));
        Assert.Equal(1, reader.GetInt32(8));
        Assert.Equal(3, reader.GetInt32(9));
    }

    [Fact]
    public async Task MovieAssignmentWritesExactlyOneMainAndAllRemainingFilesAsExtras()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Store.ApplyMovieAsync(
            fixture.TaskId,
            new TmdbMovie(456, "Manual Movie", "Manual Movie", new DateOnly(2026, 2, 1)),
            fixture.VideoFileId,
            DateTimeOffset.UtcNow);

        await using var connection = await fixture.Database.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task.status, task.media_type,
                   video.disposition, video.tmdb_movie_id,
                   attachment.disposition, attachment.tmdb_movie_id,
                   attachment.associated_task_file_id,
                   (SELECT COUNT(*) FROM movie_claims WHERE tmdb_movie_id = 456 AND task_file_id = video.id AND state = 'active')
            FROM ingest_tasks AS task
            JOIN task_files AS video ON video.id = $video_id
            JOIN task_files AS attachment ON attachment.id = $attachment_id
            WHERE task.id = $task_id;
            """;
        command.Parameters.AddWithValue("$video_id", fixture.VideoFileId);
        command.Parameters.AddWithValue("$attachment_id", fixture.AttachmentFileId);
        command.Parameters.AddWithValue("$task_id", fixture.TaskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("metadata_resolved", reader.GetString(0));
        Assert.Equal("movie", reader.GetString(1));
        Assert.Equal("movie", reader.GetString(2));
        Assert.Equal(456, reader.GetInt32(3));
        Assert.Equal("extras", reader.GetString(4));
        Assert.Equal(456, reader.GetInt32(5));
        Assert.Equal(fixture.VideoFileId, reader.GetString(6));
        Assert.Equal(1, reader.GetInt32(7));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteDatabaseFixture _database;

        private Fixture(SqliteDatabaseFixture database, ManualMetadataAssignmentStore store, string taskId, string videoFileId, string attachmentFileId)
        {
            _database = database;
            Store = store;
            TaskId = taskId;
            VideoFileId = videoFileId;
            AttachmentFileId = attachmentFileId;
        }

        public SqliteDatabaseFixture Database => _database;
        public ManualMetadataAssignmentStore Store { get; }
        public string TaskId { get; }
        public string VideoFileId { get; }
        public string AttachmentFileId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var database = await SqliteDatabaseFixture.CreateAsync();
            var profiles = new SourceProfileStore(database.Database);
            await profiles.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
            var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
            var normalized = Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
                "mikan",
                new IngestItemCommand(
                    "https://mikanani.me/passkey/manual-assignment.torrent",
                    new IngestItemInfo("Manual assignment", null, "one", "3951", null, null, 3951, 547888, null, null))).Item);
            var tasks = new IngestTaskStore(database.Database);
            var task = await tasks.AddStagedAsync(
                normalized,
                profile,
                new TorrentMetadata(
                    "Manual assignment",
                    new string('b', 40),
                    105,
                    [new TorrentFile("episode-02.mkv", 100, false), new TorrentFile("notes.txt", 5, false)]),
                "manual-assignment.torrent",
                DateTimeOffset.UtcNow.AddMinutes(15));

            string videoFileId;
            string attachmentFileId;
            await using (var connection = await database.Database.OpenConnectionAsync())
            {
                await using (var update = connection.CreateCommand())
                {
                    update.CommandText = "UPDATE ingest_tasks SET status = 'metadata_failed', failure_kind = 'InvalidInput', failure_reason = 'test' WHERE id = $task_id;";
                    update.Parameters.AddWithValue("$task_id", task.Id);
                    Assert.Equal(1, await update.ExecuteNonQueryAsync());
                }
                await using var files = connection.CreateCommand();
                files.CommandText = "SELECT id, relative_path FROM task_files WHERE task_id = $task_id ORDER BY relative_path;";
                files.Parameters.AddWithValue("$task_id", task.Id);
                await using var reader = await files.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                videoFileId = reader.GetString(1).EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)
                    ? reader.GetString(0)
                    : string.Empty;
                attachmentFileId = reader.GetString(1).EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                    ? reader.GetString(0)
                    : string.Empty;
                Assert.True(await reader.ReadAsync());
                if (reader.GetString(1).EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)) videoFileId = reader.GetString(0);
                if (reader.GetString(1).EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) attachmentFileId = reader.GetString(0);
            }
            Assert.NotEmpty(videoFileId);
            Assert.NotEmpty(attachmentFileId);
            return new Fixture(database, new ManualMetadataAssignmentStore(database.Database), task.Id, videoFileId, attachmentFileId);
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }
}
