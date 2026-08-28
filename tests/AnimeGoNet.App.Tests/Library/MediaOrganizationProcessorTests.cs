using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.App.Library;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Library;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Library;

public sealed class MediaOrganizationProcessorTests
{
    [Fact]
    public async Task MoveWritesNfoAndCompletionBeforeSafeDownloaderCleanup()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var taskId = await PrepareDownloadedTaskAsync(app, paths);
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var sourceConnection = await database.OpenConnectionAsync())
        await using (var source = sourceConnection.CreateCommand())
        {
            source.CommandText = "UPDATE task_files SET source_episode = '1' WHERE task_id = $task_id;";
            source.Parameters.AddWithValue("$task_id", taskId);
            Assert.Equal(1, await source.ExecuteNonQueryAsync());
        }
        var processor = app.App.Services.GetRequiredService<MediaOrganizationProcessor>();

        Assert.Equal(MediaOrganizationResult.FilesCompleted, await processor.RunOnceAsync());

        var target = Path.Combine(paths.SavePath, "Series", "S01", "E001.mkv");
        var nfo = Path.Combine(paths.SavePath, "Series", "tvshow.nfo");
        var animeSidecar = Path.Combine(paths.SavePath, "Series", "anime.a_json");
        var seasonSidecar = Path.Combine(paths.SavePath, "Series", "S01", "anime.s_json");
        var episodeSidecar = Path.Combine(paths.SavePath, "Series", "S01", "E001.e_json");
        Assert.True(File.Exists(target));
        Assert.True(File.Exists(nfo));
        Assert.True(File.Exists(animeSidecar));
        Assert.True(File.Exists(seasonSidecar));
        Assert.True(File.Exists(episodeSidecar));
        var document = XDocument.Load(nfo);
        Assert.Equal("100", document.Root?.Element("tmdbid")?.Value);
        Assert.Null(document.Root?.Element("bangumiid"));
        using (var sidecar = JsonDocument.Parse(await File.ReadAllTextAsync(episodeSidecar)))
        {
            Assert.Equal(1, sidecar.RootElement.GetProperty("season").GetInt32());
            Assert.Equal(1, sidecar.RootElement.GetProperty("ep").GetInt32());
            Assert.True(sidecar.RootElement.GetProperty("state").GetProperty("downloaded").GetBoolean());
        }
        await using (var indexConnection = await database.OpenConnectionAsync())
        await using (var index = indexConnection.CreateCommand())
        {
            index.CommandText = """
                SELECT COUNT(*) FROM directory_database_entries
                WHERE anime_name = 'Series';
                """;
            Assert.Equal(3L, await index.ExecuteScalarAsync());
        }
        await using (var aliasConnection = await database.OpenConnectionAsync())
        await using (var alias = aliasConnection.CreateCommand())
        {
            alias.CommandText = """
                SELECT alias.source_id, alias.source_work_id, alias.source_episode,
                       alias.info_hash, completion.tmdb_series_id,
                       completion.tmdb_season_number, completion.tmdb_episode_number
                FROM completion_aliases AS alias
                JOIN completion_records AS completion ON completion.id = alias.completion_id;
                """;
            await using var reader = await alias.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("mikan", reader.GetString(0));
            Assert.Equal("3951", reader.GetString(1));
            Assert.Equal("1", reader.GetString(2));
            Assert.Equal(40, reader.GetString(3).Length);
            Assert.Equal(100, reader.GetInt32(4));
            Assert.Equal(1, reader.GetInt32(5));
            Assert.Equal(1, reader.GetInt32(6));
            Assert.False(await reader.ReadAsync());
        }
        Assert.Empty(client.Deleted);
        var intermediate = await ReadStateAsync(app, taskId);
        Assert.Equal(("organizing_cleanup", "cleanup", 1), intermediate);
        Assert.Equal(
            (MediaOrganizationPhases.CleanupDownloader, 0, 1),
            await ReadProgressAsync(app, taskId));

        Assert.Equal(MediaOrganizationResult.CleanupCompleted, await processor.RunOnceAsync());

        var deleted = Assert.Single(client.Deleted);
        Assert.False(deleted.DeleteFiles);
        Assert.Equal(("organized", "completed", 1), await ReadStateAsync(app, taskId));
        Assert.Equal(
            (MediaOrganizationPhases.Completed, 1, 1),
            await ReadProgressAsync(app, taskId));
        Assert.False(File.Exists(Path.Combine(paths.DownloadPath, "bt", "episode.mkv")));
        Assert.NotEmpty(client.Paused);
    }

    [Fact]
    public async Task AssociatedSubtitleMovesWithSuffixWithoutCreatingSecondCompletion()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var taskId = await PrepareDownloadedTaskAsync(app, paths);
        await AddAssociatedSubtitleAsync(app, taskId, paths);

        Assert.Equal(
            MediaOrganizationResult.FilesCompleted,
            await app.App.Services.GetRequiredService<MediaOrganizationProcessor>().RunOnceAsync());

        Assert.True(File.Exists(Path.Combine(paths.SavePath, "Series", "S01", "E001.mkv")));
        Assert.Equal(
            new byte[] { 6, 7, 8 },
            await File.ReadAllBytesAsync(Path.Combine(paths.SavePath, "Series", "S01", "E001.zh-Hans.forced.ass")));
        Assert.Equal(("organizing_cleanup", "cleanup", 1), await ReadStateAsync(app, taskId));
    }

    [Fact]
    public async Task ExistingTaskUsesPortableQbittorrentPathWhenOriginalNameContainsWindowsColon()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var taskId = await PrepareDownloadedTaskAsync(app, paths);
        var downloadRoot = Path.Combine(paths.DownloadPath, "bt");
        var actualName = "Cyborg 009_ Nemesis - 03.mkv";
        File.Move(
            Path.Combine(downloadRoot, "episode.mkv"),
            Path.Combine(downloadRoot, actualName));

        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE task_files SET relative_path = 'Cyborg 009: Nemesis - 03.mkv' WHERE task_id = $task_id;";
            update.Parameters.AddWithValue("$task_id", taskId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        Assert.Equal(
            MediaOrganizationResult.FilesCompleted,
            await app.App.Services.GetRequiredService<MediaOrganizationProcessor>().RunOnceAsync());

        Assert.False(File.Exists(Path.Combine(downloadRoot, actualName)));
        Assert.True(File.Exists(Path.Combine(paths.SavePath, "Series", "S01", "E001.mkv")));
    }

    [Fact]
    public async Task DownloaderCleanupCallbackRetriesWithoutDeletingOrganizedMedia()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(
            downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var taskId = await PrepareDownloadedTaskAsync(app, paths);
        var processor = app.App.Services.GetRequiredService<MediaOrganizationProcessor>();

        Assert.Equal(MediaOrganizationResult.FilesCompleted, await processor.RunOnceAsync());
        var target = Path.Combine(paths.SavePath, "Series", "S01", "E001.mkv");
        Assert.True(File.Exists(target));
        client.DeleteFailure = new HttpRequestException("fake qB unavailable");

        Assert.Equal(MediaOrganizationResult.RetryScheduled, await processor.RunOnceAsync());

        Assert.True(File.Exists(target));
        Assert.Equal(("organizing_cleanup", "cleanup", 1), await ReadStateAsync(app, taskId));
        Assert.Single(client.Deleted);
        Assert.False(client.Deleted[0].DeleteFiles);

        client.DeleteFailure = null;
        await app.App.Services.GetRequiredService<DownloadClientOperationCoordinator>()
            .ExecuteProbeAsync(
                "bt",
                async (downloadClient, cancellationToken) =>
                {
                    await downloadClient.ConnectAsync(cancellationToken);
                    return true;
                });
        await MakeOrganizationRetryReadyAsync(app, taskId);

        var retryResult = await processor.RunOnceAsync();
        Assert.True(
            retryResult == MediaOrganizationResult.CleanupCompleted,
            $"Cleanup retry returned {retryResult}: {await ReadOrganizationFailureCodeAsync(app, taskId)}");
        Assert.True(File.Exists(target));
        Assert.Equal(("organized", "completed", 1), await ReadStateAsync(app, taskId));
        Assert.Equal(2, client.Deleted.Count);
        Assert.All(client.Deleted, attempt => Assert.False(attempt.DeleteFiles));
    }

    [Fact]
    public async Task InvalidDirectorySidecarPreventsBusinessCompletionAndSchedulesRetry()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var taskId = await PrepareDownloadedTaskAsync(app, paths);
        var seriesDirectory = Path.Combine(paths.SavePath, "Series");
        Directory.CreateDirectory(seriesDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(seriesDirectory, "anime.a_json"),
            """{"info":{"hash":"broken"}}""");

        Assert.Equal(
            MediaOrganizationResult.RetryScheduled,
            await app.App.Services.GetRequiredService<MediaOrganizationProcessor>().RunOnceAsync());

        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM completion_records),
                (SELECT organization_state FROM download_jobs WHERE task_id = $task_id),
                (SELECT status FROM ingest_tasks WHERE id = $task_id),
                (SELECT organization_phase FROM download_jobs WHERE task_id = $task_id),
                (SELECT organization_completed_units FROM download_jobs WHERE task_id = $task_id),
                (SELECT organization_total_units FROM download_jobs WHERE task_id = $task_id);
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal("pending", reader.GetString(1));
        Assert.Equal("downloaded", reader.GetString(2));
        Assert.Equal(MediaOrganizationPhases.DirectoryIndex, reader.GetString(3));
        Assert.Equal(0, reader.GetInt32(4));
        Assert.Equal(1, reader.GetInt32(5));
    }

    [Fact]
    public async Task MultiFileConflictResumesOnlyPendingOperationInStablePathOrder()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(
            downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var taskId = await PrepareDownloadedTaskAsync(app, paths);
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        var downloadRoot = Path.Combine(paths.DownloadPath, "bt");
        var secondSource = Path.Combine(downloadRoot, "episode2.mkv");
        await File.WriteAllBytesAsync(secondSource, [6, 7, 8, 9]);
        await using (var connection = await database.OpenConnectionAsync())
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, source_episode,
                    file_episode_candidate, tmdb_series_id, tmdb_season_number,
                    tmdb_episode_number, tmdb_episode_id, disposition, download_wanted)
                VALUES (
                    'second-episode', $task_id, 'episode2.mkv', 4, '2', '2',
                    100, 1, 2, 1002, 'episode', 1);
                """;
            insert.Parameters.AddWithValue("$task_id", taskId);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        var firstTarget = Path.Combine(paths.SavePath, "Series", "S01", "E001.mkv");
        var secondTarget = Path.Combine(paths.SavePath, "Series", "S01", "E002.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(secondTarget)!);
        await File.WriteAllBytesAsync(secondTarget, [9, 8, 7, 6]);
        var processor = app.App.Services.GetRequiredService<MediaOrganizationProcessor>();

        Assert.Equal(MediaOrganizationResult.RetryScheduled, await processor.RunOnceAsync());

        Assert.True(File.Exists(firstTarget));
        Assert.False(File.Exists(Path.Combine(downloadRoot, "episode.mkv")));
        Assert.True(File.Exists(secondSource));
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, await File.ReadAllBytesAsync(secondTarget));
        await using (var failedConnection = await database.OpenConnectionAsync())
        await using (var failed = failedConnection.CreateCommand())
        {
            failed.CommandText = """
                SELECT
                    SUM(CASE WHEN operation.state = 'completed' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN operation.state = 'pending' THEN 1 ELSE 0 END),
                    (SELECT COUNT(*) FROM completion_records),
                    job.organization_state,
                    job.organization_failure_code,
                    task.status
                FROM file_operations AS operation
                JOIN task_files AS file ON file.id = operation.task_file_id
                JOIN download_jobs AS job ON job.task_id = file.task_id
                JOIN ingest_tasks AS task ON task.id = file.task_id
                WHERE file.task_id = $task_id
                GROUP BY job.organization_state, job.organization_failure_code, task.status;
                """;
            failed.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await failed.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Equal(0, reader.GetInt32(2));
            Assert.Equal("pending", reader.GetString(3));
            Assert.Equal("target_conflict", reader.GetString(4));
            Assert.Equal("downloaded", reader.GetString(5));
        }

        File.Delete(secondTarget);
        await MakeOrganizationRetryReadyAsync(app, taskId);

        Assert.Equal(MediaOrganizationResult.FilesCompleted, await processor.RunOnceAsync());

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, await File.ReadAllBytesAsync(firstTarget));
        Assert.Equal(new byte[] { 6, 7, 8, 9 }, await File.ReadAllBytesAsync(secondTarget));
        Assert.False(File.Exists(secondSource));
        Assert.Equal(("organizing_cleanup", "cleanup", 2), await ReadStateAsync(app, taskId));

        Assert.Equal(MediaOrganizationResult.CleanupCompleted, await processor.RunOnceAsync());
        Assert.Equal(("organized", "completed", 2), await ReadStateAsync(app, taskId));
    }

    [Fact]
    public async Task BangumiFallbackMovesToOtherAndWritesTmdbZeroNfoWithoutCanonicalCompletion()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var taskId = await PrepareDownloadedTaskAsync(app, paths);
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                DELETE FROM anime_series WHERE id = 'series';
                INSERT INTO anime_series (
                    id, tmdb_series_id, bangumi_subject_id, canonical_name, original_name,
                    needs_tmdb_completion, created_at_utc, updated_at_utc)
                VALUES ('fallback-series', 0, 547888, 'Fallback Series', 'Fallback Series', 1, $now, $now);
                UPDATE task_files
                SET disposition = 'other', other_reason = 'tmdb_fallback_pending_completion',
                    tmdb_series_id = NULL, tmdb_season_number = 2,
                    tmdb_episode_number = NULL, tmdb_episode_id = NULL
                WHERE task_id = $task_id;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(3, await command.ExecuteNonQueryAsync());
        }
        await SeedFallbackClaimAsync(app, taskId);

        Assert.Equal(
            MediaOrganizationResult.FilesCompleted,
            await app.App.Services.GetRequiredService<MediaOrganizationProcessor>().RunOnceAsync());

        var target = Path.Combine(paths.SavePath, "Fallback Series", "S02", "Extras", "episode.mkv");
        var nfo = Path.Combine(paths.SavePath, "Fallback Series", "tvshow.nfo");
        Assert.True(File.Exists(target));
        var document = XDocument.Load(nfo);
        Assert.Equal("0", document.Root?.Element("tmdbid")?.Value);
        Assert.Equal("547888", document.Root?.Element("bangumiid")?.Value);
        Assert.Equal(("organizing_cleanup", "cleanup", 0), await ReadStateAsync(app, taskId));
        await using var verifyConnection = await database.OpenConnectionAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText = """
            SELECT completion.scope_kind, completion.source_id, completion.media_path,
                   claim.state
            FROM fallback_completion_records AS completion
            JOIN fallback_claims AS claim
              ON claim.scope_kind = completion.scope_kind
             AND claim.scope_key = completion.scope_key;
            """;
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("torrent_file", reader.GetString(0));
        Assert.Equal("mikan", reader.GetString(1));
        Assert.Equal(target, reader.GetString(2));
        Assert.Equal("completed", reader.GetString(3));
        Assert.False(await reader.ReadAsync());
    }

    [Theory]
    [InlineData("Show [48.5].mkv", "48.5", "fractional_episode", "other")]
    [InlineData("Show [SP01].mkv", "sp01", "special_episode", "other")]
    [InlineData("Show unmatched.mkv", null, "tmdb_episode_not_found", "other")]
    [InlineData("Show AI other.mkv", null, "ai_episode_unmatched", "other")]
    [InlineData("Show commentary.zh-Hans.ass", null, "subtitle_unmatched", "other")]
    [InlineData("Show [Fonts].7z", null, "episode_not_parsed", "extras")]
    public async Task ConfirmedSeasonOtherMovesOriginalNameWithoutInventingEpisodeProgress(
        string relativePath,
        string? sourceEpisode,
        string otherReason,
        string disposition)
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(
            downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var taskId = await PrepareDownloadedTaskAsync(app, paths);
        var downloadRoot = Path.Combine(paths.DownloadPath, "bt");
        File.Move(
            Path.Combine(downloadRoot, "episode.mkv"),
            Path.Combine(downloadRoot, relativePath));
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = """
                UPDATE task_files
                SET relative_path = $relative_path,
                    source_episode = $source_episode,
                    file_episode_candidate = NULL,
                    tmdb_episode_number = NULL,
                    tmdb_episode_id = NULL,
                    disposition = $disposition,
                    other_reason = $other_reason,
                    episode_resolution_source = NULL,
                    episode_resolution_run_id = NULL,
                    episode_resolution_attempt_id = NULL
                WHERE task_id = $task_id;
                """;
            update.Parameters.AddWithValue("$task_id", taskId);
            update.Parameters.AddWithValue("$relative_path", relativePath);
            update.Parameters.AddWithValue(
                "$source_episode",
                (object?)sourceEpisode ?? DBNull.Value);
            update.Parameters.AddWithValue("$other_reason", otherReason);
            update.Parameters.AddWithValue("$disposition", disposition);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        Assert.Equal(
            MediaOrganizationResult.FilesCompleted,
            await app.App.Services.GetRequiredService<MediaOrganizationProcessor>().RunOnceAsync());

        var target = Path.Combine(paths.SavePath, "Series", "S01", "Extras", relativePath);
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(Path.Combine(downloadRoot, relativePath)));
        Assert.True(File.Exists(Path.Combine(paths.SavePath, "Series", "S01", "anime.s_json")));
        Assert.Empty(Directory.GetFiles(
            Path.Combine(paths.SavePath, "Series", "S01"),
            "*.e_json",
            SearchOption.TopDirectoryOnly));
        await using var verifyConnection = await database.OpenConnectionAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM completion_records),
                (SELECT COUNT(*) FROM completion_aliases),
                (SELECT COUNT(*) FROM episode_claims WHERE state = 'completed'),
                (SELECT disposition FROM task_files WHERE task_id = $task_id),
                (SELECT other_reason FROM task_files WHERE task_id = $task_id);
            """;
        verify.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
        Assert.Equal(disposition, reader.GetString(3));
        Assert.Equal(otherReason, reader.GetString(4));
    }

    [Theory]
    [InlineData("link", true)]
    [InlineData("link_delete", false)]
    public async Task LinkStrategiesPublishBeforeSeedingEndsAndCleanupAfterCompletion(
        string strategy,
        bool preserveSource)
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var taskId = await PrepareDownloadedTaskAsync(app, paths, strategy, "seeding");
        var source = Path.Combine(paths.DownloadPath, "bt", "episode.mkv");
        var target = Path.Combine(paths.SavePath, "Series", "S01", "E001.mkv");
        var processor = app.App.Services.GetRequiredService<MediaOrganizationProcessor>();

        Assert.Equal(MediaOrganizationResult.FilesCompleted, await processor.RunOnceAsync());
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(target));
        Assert.Empty(client.Paused);
        Assert.Empty(client.Deleted);
        Assert.Equal(("organized", "cleanup", 1), await ReadStateAsync(app, taskId));
        Assert.Equal(MediaOrganizationResult.NoWork, await processor.RunOnceAsync());

        await SetDownloadStateAsync(app, taskId, "complete");
        Assert.Equal(MediaOrganizationResult.CleanupCompleted, await processor.RunOnceAsync());

        Assert.Equal(preserveSource, File.Exists(source));
        Assert.True(File.Exists(target));
        Assert.Single(client.Deleted);
        Assert.Equal(("organized", "completed", 1), await ReadStateAsync(app, taskId));
    }

    [Fact]
    public async Task LinkStrategyUsesConfiguredSymbolicLinkAndPreservesSeedingSource()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        _ = await PrepareDownloadedTaskAsync(
            app, paths, "link", "seeding", linkType: "symbolic");
        var source = Path.Combine(paths.DownloadPath, "bt", "episode.mkv");
        var target = Path.Combine(paths.SavePath, "Series", "S01", "E001.mkv");
        if (!CanCreateSymbolicLink(paths.SavePath, source))
        {
            Assert.True(OperatingSystem.IsWindows());
            Assert.True(File.Exists(source));
            return;
        }

        Assert.Equal(
            MediaOrganizationResult.FilesCompleted,
            await app.App.Services.GetRequiredService<MediaOrganizationProcessor>().RunOnceAsync());

        var targetInfo = new FileInfo(target);
        Assert.NotNull(targetInfo.LinkTarget);
        Assert.Equal(Path.GetFullPath(source), targetInfo.ResolveLinkTarget(true)!.FullName);
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(target));
        Assert.Empty(client.Paused);
    }

    [Fact]
    public async Task MixedMediaPostprocessRelinksSymbolicMediaToOriginalDownload()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var taskId = await PrepareDownloadedTaskAsync(
            app, paths, "link", "complete", linkType: "symbolic");
        var source = Path.Combine(paths.DownloadPath, "bt", "episode.mkv");
        if (!CanCreateSymbolicLink(paths.SavePath, source))
        {
            Assert.True(OperatingSystem.IsWindows());
            return;
        }

        var processor = app.App.Services.GetRequiredService<MediaOrganizationProcessor>();
        Assert.Equal(MediaOrganizationResult.FilesCompleted, await processor.RunOnceAsync());
        Assert.Equal(MediaOrganizationResult.CleanupCompleted, await processor.RunOnceAsync());

        var oldTarget = Path.Combine(paths.SavePath, "Series", "S01", "E001.mkv");
        Assert.True(FilePathInspector.HasExpectedFileLength(oldTarget, 5));
        var postprocess = app.App.Services.GetRequiredService<MixedMediaPostprocessStore>();
        var preview = Assert.IsType<MixedMediaPostprocessPreview>(await postprocess.PreviewAsync(taskId));
        var file = Assert.Single(preview.Files);
        Assert.Equal(
            MixedMediaPostprocessResult.Started,
            await postprocess.StartAsync(
                taskId,
                file.TaskFileId,
                new TmdbMovie(200, "Movie", "Movie", new DateOnly(2026, 1, 2), null),
                DateTimeOffset.UtcNow));

        Assert.Equal(MediaOrganizationResult.FilesCompleted, await processor.RunOnceAsync());
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT target_path FROM file_operations WHERE task_file_id = $file_id AND state = 'completed';";
        query.Parameters.AddWithValue("$file_id", file.TaskFileId);
        var movieTarget = Assert.IsType<string>(await query.ExecuteScalarAsync());

        Assert.False(File.Exists(oldTarget));
        Assert.True(File.Exists(source));
        Assert.True(FilePathInspector.HasExpectedFileLength(movieTarget, 5));
        Assert.Equal(
            Path.GetFullPath(source),
            new FileInfo(movieTarget).ResolveLinkTarget(returnFinalTarget: true)!.FullName);
    }

    private static bool CanCreateSymbolicLink(string directory, string source)
    {
        var probe = Path.Combine(directory, $".animegonet-symlink-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            File.CreateSymbolicLink(probe, source);
            return new FileInfo(probe).LinkTarget is not null;
        }
        catch (IOException) when (OperatingSystem.IsWindows())
        {
            return false;
        }
        finally
        {
            if (new FileInfo(probe).LinkTarget is not null || File.Exists(probe))
            {
                File.Delete(probe);
            }
        }
    }

    [Fact]
    public async Task WaitMoveDoesNotTouchFilesUntilSeedingEnds()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var taskId = await PrepareDownloadedTaskAsync(app, paths, "wait_move", "seeding");
        var source = Path.Combine(paths.DownloadPath, "bt", "episode.mkv");
        var target = Path.Combine(paths.SavePath, "Series", "S01", "E001.mkv");
        var processor = app.App.Services.GetRequiredService<MediaOrganizationProcessor>();

        Assert.Equal(MediaOrganizationResult.NoWork, await processor.RunOnceAsync());
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(target));

        await SetRawDownloadStateAsync(app, taskId, "complete");
        Assert.Equal(MediaOrganizationResult.NoWork, await processor.RunOnceAsync());
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(target));

        await SetDownloadStateAsync(app, taskId, "complete");
        Assert.Equal(MediaOrganizationResult.FilesCompleted, await processor.RunOnceAsync());

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(target));
        Assert.NotEmpty(client.Paused);
        Assert.Equal(("organizing_cleanup", "cleanup", 1), await ReadStateAsync(app, taskId));
    }

    private static async Task AddAssociatedSubtitleAsync(RunningApp app, string taskId, PathOptions paths)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO task_files (
                id, task_id, relative_path, size_bytes, source_episode, file_episode_candidate,
                tmdb_series_id, tmdb_season_number, tmdb_episode_number, tmdb_episode_id,
                disposition, associated_task_file_id, rename_suffix, download_wanted)
            SELECT 'subtitle', $task_id, 'episode.zh-Hans.forced.ass', 3, '1', '1',
                   100, 1, 1, 1001, 'episode', id, '.zh-Hans.forced.ass', 1
            FROM task_files WHERE task_id = $task_id AND relative_path = 'episode.mkv';
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        await File.WriteAllBytesAsync(
            Path.Combine(paths.DownloadPath, "bt", "episode.zh-Hans.forced.ass"), [6, 7, 8]);
    }

    private static async Task MakeOrganizationRetryReadyAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE download_jobs
            SET organization_next_attempt_at_utc = $now
            WHERE task_id = $task_id AND organization_state IN ('pending', 'cleanup');
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        command.Parameters.AddWithValue(
            "$now",
            DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task SeedFallbackClaimAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        string fileId;
        FallbackDedupScope scope;
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT file.id, task.source_id, task.mikanid, task.source_work_id,
                       task.source_item_id, job.info_hash, file.relative_path,
                       file.size_bytes, file.source_episode
                FROM task_files AS file
                JOIN ingest_tasks AS task ON task.id = file.task_id
                JOIN download_jobs AS job ON job.task_id = task.id
                WHERE task.id = $task_id;
                """;
            query.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            fileId = reader.GetString(0);
            scope = FallbackDedupScopeResolver.Resolve(
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetString(8));
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO fallback_claims (
                id, scope_kind, scope_key, task_file_id,
                state, claimed_at_utc, expires_at_utc)
            VALUES ('fallback-claim', $scope_kind, $scope_key, $file_id, 'active', $now, NULL);
            """;
        insert.Parameters.AddWithValue("$scope_kind", scope.Kind);
        insert.Parameters.AddWithValue("$scope_key", scope.Key);
        insert.Parameters.AddWithValue("$file_id", fileId);
        insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        Assert.Equal(1, await insert.ExecuteNonQueryAsync());
    }

    private static async Task<string> PrepareDownloadedTaskAsync(
        RunningApp app,
        PathOptions paths,
        string strategy = "move",
        string downloadState = "complete",
        string linkType = "hard")
    {
        if (strategy != "move")
        {
            using var update = await app.Client.PutAsync(
                "/api/v1/sources/mikan",
                new StringContent(
                    $$"""
                      {
                        "display_name": "Mikan",
                        "downloader_id": "bt",
                        "file_strategy": "{{strategy}}",
                        "link_type": "{{linkType}}",
                        "allowed_torrent_hosts": ["mikanani.me"],
                        "category": "animegonet",
                        "tags": [],
                        "seeding_time_minutes": 30,
                        "rss_filter_enabled": true,
                        "rss_priority_enabled": true,
                        "enabled": true,
                        "expected_revision": 1
                      }
                      """,
                    Encoding.UTF8,
                    "application/json"));
            update.EnsureSuccessStatusCode();
        }

        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/passkey/organization-worker.torrent",
                "info": { "title": "Organization", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var response = await app.Client.PostAsync(
            "/api/v1/ingest", new StringContent(payload, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var taskId = json.RootElement.GetProperty("items")[0].GetProperty("ingest_id").GetString()!;
        var hash = json.RootElement.GetProperty("items")[0].GetProperty("info_hash").GetString()!;
        var tasks = app.App.Services.GetRequiredService<IngestTaskStore>();
        var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
        var downloadRoot = Path.Combine(paths.DownloadPath, "bt");
        await tasks.CompleteDispatchAsync(
            dispatch,
            new DownloadTaskSnapshot(hash, "Organization", DownloadTaskState.Complete, 1, 5, 5, 0, null),
            downloadRoot,
            paths.SavePath,
            DateTimeOffset.UtcNow);

        var source = Path.Combine(downloadRoot, "episode.mkv");
        Directory.CreateDirectory(downloadRoot);
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4, 5]);
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var setup = connection.CreateCommand();
        setup.CommandText = """
            INSERT INTO anime_series (
                id, tmdb_series_id, bangumi_subject_id, canonical_name, original_name,
                needs_tmdb_completion, created_at_utc, updated_at_utc)
            VALUES ('series', 100, 547888, 'Series', 'Series', 0, $now, $now);
            UPDATE task_files SET disposition = 'episode', tmdb_series_id = 100,
                tmdb_season_number = 1, tmdb_episode_number = 1,
                tmdb_episode_id = 1001, download_wanted = 1
            WHERE task_id = $task_id;
            UPDATE download_jobs
            SET preparation_state = 'completed', state = $download_state, progress = 1,
                seeding_state = CASE
                    WHEN seeding_target_minutes = 0 THEN 'not_required'
                    WHEN $download_state = 'complete' THEN 'completed'
                    WHEN $download_state = 'seeding' THEN 'seeding'
                    ELSE 'waiting'
                END,
                seeding_completed_at_utc = CASE
                    WHEN seeding_target_minutes <> 0 AND $download_state = 'complete'
                        THEN $now
                    ELSE NULL
                END
            WHERE task_id = $task_id;
            UPDATE ingest_tasks SET status = 'downloaded' WHERE id = $task_id;
            """;
        setup.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        setup.Parameters.AddWithValue("$task_id", taskId);
        setup.Parameters.AddWithValue("$download_state", downloadState);
        Assert.Equal(4, await setup.ExecuteNonQueryAsync());
        return taskId;
    }

    private static async Task SetDownloadStateAsync(RunningApp app, string taskId, string state)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE download_jobs
            SET state = $state,
                seeding_state = CASE
                    WHEN seeding_target_minutes = 0 THEN 'not_required'
                    WHEN $state = 'complete' THEN 'completed'
                    WHEN $state = 'seeding' THEN 'seeding'
                    ELSE 'waiting'
                END,
                seeding_completed_at_utc = CASE
                    WHEN seeding_target_minutes <> 0 AND $state = 'complete'
                        THEN $now
                    ELSE NULL
                END,
                updated_at_utc = $now
            WHERE task_id = $task_id;
            """;
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task SetRawDownloadStateAsync(RunningApp app, string taskId, string state)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE download_jobs SET state = $state, updated_at_utc = $now
            WHERE task_id = $task_id;
            """;
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<(string Task, string Organization, int Completions)> ReadStateAsync(
        RunningApp app,
        string taskId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task.status, job.organization_state, (SELECT COUNT(*) FROM completion_records)
            FROM ingest_tasks AS task JOIN download_jobs AS job ON job.task_id = task.id
            WHERE task.id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetInt32(2));
    }

    private static async Task<string?> ReadOrganizationFailureCodeAsync(
        RunningApp app,
        string taskId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT organization_failure_code
            FROM download_jobs
            WHERE task_id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        return await command.ExecuteScalarAsync() as string;
    }

    private sealed class FakeRegistry(IDownloadClient client) : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds => ["bt"];

        public IDownloadClient GetRequired(string instanceId) =>
            instanceId == "bt" ? client : throw new KeyNotFoundException();
    }

    private sealed class FakeDownloadClient : IDownloadClient
    {
        public List<string> Paused { get; } = [];

        public List<(string[] Hashes, bool DeleteFiles)> Deleted { get; } = [];

        public Exception? DeleteFailure { get; set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadTaskSnapshot>>([]);
        public Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadFileSnapshot>> ListFilesAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadFileSnapshot>>([]);
        public Task SetFilePriorityAsync(string hash, IReadOnlyList<int> fileIndexes, int priority, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task AddTagsAsync(IReadOnlyList<string> hashes, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task PauseAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default)
        {
            Paused.AddRange(hashes);
            return Task.CompletedTask;
        }
        public Task ResumeAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(IReadOnlyList<string> hashes, bool deleteFiles, CancellationToken cancellationToken = default)
        {
            Deleted.Add((hashes.ToArray(), deleteFiles));
            return DeleteFailure is null
                ? Task.CompletedTask
                : Task.FromException(DeleteFailure);
        }
    }

    private static async Task<(string Phase, int Completed, int Total)> ReadProgressAsync(
        RunningApp app,
        string taskId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT organization_phase, organization_completed_units,
                   organization_total_units
            FROM download_jobs
            WHERE task_id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2));
    }
}
