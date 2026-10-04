using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.Deletion;
using AnimeGoNet.Data.Sqlite;
using AnimeGoNet.Data.Library;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sources;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Tests.Sqlite;

public sealed class PortablePathStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "animegonet-portable-path-tests", Guid.NewGuid().ToString("N"));

    public PortablePathStorageTests() => Directory.CreateDirectory(_root);

    private string DatabaseFile => Path.Combine(_root, "test.db");

    private AnimeGoOptions Options(string mount) => AnimeGoDefaults.CreateNative(Path.Combine(_root, mount));

    [Fact]
    public async Task NewWritesCaptureRelativePathAndReadInsideSameTransaction()
    {
        var options = Options("old");
        var database = new AnimeGoSqliteDatabase(DatabaseFile, options);
        await database.InitializeAsync();
        var path = Path.Combine(options.Paths.SavePath, "Show", "S01", "E001.mkv");
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        await InsertCompletionAsync(connection, path, transaction);
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = "SELECT root_kind, root_id, relative_path, app_path(original_path) FROM stored_path_locations WHERE original_path = $path;";
        query.Parameters.AddWithValue("$path", path);
        await using (var reader = await query.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("tv_library", reader.GetString(0));
            Assert.Equal("default", reader.GetString(1));
            Assert.Equal("Show/S01/E001.mkv", reader.GetString(2));
            Assert.Equal(path, reader.GetString(3));
        }
        await transaction.RollbackAsync();
        query.Transaction = null;
        query.CommandText = "SELECT COUNT(*) FROM stored_path_locations;";
        Assert.Equal(0L, await query.ExecuteScalarAsync());
    }

    [Fact]
    public async Task RootChangeResolvesOldRecordsWithoutRewritingSnapshotOrFilename()
    {
        var oldOptions = Options("old");
        var oldDatabase = new AnimeGoSqliteDatabase(DatabaseFile, oldOptions);
        await oldDatabase.InitializeAsync();
        var oldPath = Path.Combine(oldOptions.Paths.SavePath, "Original Name", "S01", "E001.mkv");
        await using (var connection = await oldDatabase.OpenConnectionAsync()) await InsertCompletionAsync(connection, oldPath);
        var newOptions = Options("new");
        var newDatabase = new AnimeGoSqliteDatabase(DatabaseFile, newOptions);
        await newDatabase.InitializeAsync();
        await newDatabase.InitializeAsync();
        await using var current = await newDatabase.OpenConnectionAsync();
        await using var query = current.CreateCommand();
        query.CommandText = "SELECT media_path, app_path(media_path) FROM completion_records;";
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(oldPath, reader.GetString(0));
        Assert.Equal(Path.Combine(newOptions.Paths.SavePath, "Original Name", "S01", "E001.mkv"), reader.GetString(1));
    }

    [Fact]
    public async Task Version75IsBackedUpAndForeignPlatformRecordsAreUpgraded()
    {
        var legacy = new AnimeGoSqliteDatabase(DatabaseFile);
        await using (var connection = await legacy.OpenConnectionAsync())
        {
            await SchemaMigrationRunner.ApplyAsync(connection, DatabaseSchema.Migrations.Take(75).ToArray(), CancellationToken.None);
            await InsertCompletionAsync(connection, "/old/tv/Show/S01/E001.mkv");
        }
        // The captured root normally comes from download_jobs. A same-platform first upgrade
        // also establishes root history for external-library imports without a download job.
        var oldOptions = Options("old") with { Paths = Options("old").Paths with { SavePath = "/old/tv" } };
        await new AnimeGoSqliteDatabase(DatabaseFile, oldOptions).InitializeAsync();
        Assert.Single(Directory.GetFiles(_root, "test.db.pre-portable-paths-*.db"));
        var currentOptions = Options("current");
        var current = new AnimeGoSqliteDatabase(DatabaseFile, currentOptions);
        await current.InitializeAsync();
        await using var currentConnection = await current.OpenConnectionAsync();
        await using var query = currentConnection.CreateCommand();
        query.CommandText = "SELECT app_path(media_path) FROM completion_records;";
        Assert.Equal(Path.Combine(currentOptions.Paths.SavePath, "Show", "S01", "E001.mkv"), await query.ExecuteScalarAsync());
        Assert.Single(Directory.GetFiles(_root, "test.db.pre-portable-paths-*.db"));
    }

    [Fact]
    public async Task UnknownLegacyRootIsPreservedButNeverUsedAsFallback()
    {
        var database = new AnimeGoSqliteDatabase(DatabaseFile, Options("current"));
        await database.InitializeAsync();
        await using var connection = await database.OpenConnectionAsync();
        await InsertCompletionAsync(connection, "/unknown/Show/E001.mkv");
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT app_path_optional(media_path) FROM completion_records;";
        Assert.Equal(DBNull.Value, await query.ExecuteScalarAsync());
        query.CommandText = "SELECT app_path(media_path) FROM completion_records;";
        var exception = await Assert.ThrowsAsync<SqliteException>(async () => await query.ExecuteScalarAsync());
        Assert.Contains("path_mapping_required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MovieMainUsesMovieRoot()
    {
        var options = Options("current");
        var database = new AnimeGoSqliteDatabase(DatabaseFile, options);
        await database.InitializeAsync();
        await using var connection = await database.OpenConnectionAsync();
        await using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO movie_completion_records VALUES ('movie', 1, 'u2', NULL, $path, '2026-10-05');";
        insert.Parameters.AddWithValue("$path", Path.Combine(options.Paths.EffectiveMovieSavePath, "Film", "Film.mkv"));
        await insert.ExecuteNonQueryAsync();
        insert.CommandText = "SELECT root_kind || ':' || relative_path FROM stored_path_locations;";
        Assert.Equal("movie_library:Film/Film.mkv", await insert.ExecuteScalarAsync());
    }

    [Fact]
    public async Task FrozenDeleteIsInvalidatedBeforeClaimAfterRootChange()
    {
        var options = Options("old");
        var database = new AnimeGoSqliteDatabase(DatabaseFile, options);
        await database.InitializeAsync();
        await using (var connection = await database.OpenConnectionAsync())
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO delete_executions(id, task_id, delete_business_record, delete_downloader_task,
                    delete_source_files, delete_media_files, plan_json, state, created_at_utc)
                VALUES ('delete', 'task', 0, 0, 0, 1, '{}', 'pending', '2026-10-05');
                INSERT INTO delete_execution_items(id, execution_id, item_kind, target_key, root_path,
                    display_value, ordinal, state)
                VALUES ('item', 'delete', 'media_file', $path, $root, $path, 0, 'pending');
                """;
            insert.Parameters.AddWithValue("$path", Path.Combine(options.Paths.SavePath, "Show", "E001.mkv"));
            insert.Parameters.AddWithValue("$root", options.Paths.SavePath);
            await insert.ExecuteNonQueryAsync();
        }
        var current = new AnimeGoSqliteDatabase(DatabaseFile, Options("new"));
        await current.InitializeAsync();
        var store = new DeleteExecutionStore(current);
        Assert.Null(await store.TryClaimAsync("delete", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
        var status = await store.GetAsync("delete");
        Assert.NotNull(status);
        Assert.Equal("failed", status.State);
        Assert.Equal("delete_path_mapping_changed", status.FailureReason);
    }

    [Fact]
    public async Task MoviePathAndRevisionFollowNewRootSoOldDeleteConfirmationCannotBeReplayed()
    {
        var options = Options("old");
        var database = new AnimeGoSqliteDatabase(DatabaseFile, options);
        await database.InitializeAsync();
        await using (var connection = await database.OpenConnectionAsync())
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO anime_movies VALUES ('movie', 1, 'Film', 'Film', NULL, NULL, '2026-10-05', '2026-10-05');
                INSERT INTO movie_completion_records VALUES ('completion', 1, 'u2', NULL, $path, '2026-10-05');
                """;
            insert.Parameters.AddWithValue("$path", Path.Combine(options.Paths.EffectiveMovieSavePath, "Film", "Film.mkv"));
            await insert.ExecuteNonQueryAsync();
        }
        var before = await new AnimeLibraryAdminStore(database).GetMovieFileContextAsync(1);
        Assert.NotNull(before);
        var currentOptions = Options("new");
        var current = new AnimeGoSqliteDatabase(DatabaseFile, currentOptions);
        await current.InitializeAsync();
        var admin = new AnimeLibraryAdminStore(current);
        var after = await admin.GetMovieFileContextAsync(1);
        Assert.NotNull(after);
        Assert.False(after.PathMappingRequired);
        Assert.Equal(Path.Combine(currentOptions.Paths.EffectiveMovieSavePath, "Film", "Film.mkv"), after.MainMediaPath);
        Assert.NotEqual(before.ResourceRevision, after.ResourceRevision);
        var delete = await admin.ForceDeleteOrphanMovieAsync(1, before.ResourceRevision);
        Assert.Equal(AnimeLibraryMutationStatus.RevisionConflict, delete.Status);
    }

    private static async Task InsertCompletionAsync(SqliteConnection connection, string path, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO completion_records(id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                source_id, media_path, completed_at_utc) VALUES ('episode', 1, 1, 1, 'u2', $path, '2026-10-05');
            """;
        command.Parameters.AddWithValue("$path", path);
        await command.ExecuteNonQueryAsync();
    }

    [Theory]
    [InlineData("/old/tv", "/old/download")]
    [InlineData(@"Z:\OldLibrary", @"Z:\OldDownload")]
    public async Task LegacyJobRootsMigrateOperationsExtrasSubtitlesDeletionPreviewAndNfo(string oldTv, string oldDownload)
    {
        var old = new AnimeGoSqliteDatabase(DatabaseFile);
        await using (var connection = await old.OpenConnectionAsync())
            await SchemaMigrationRunner.ApplyAsync(connection, DatabaseSchema.Migrations.Take(75).ToArray(), CancellationToken.None);
        await SeedJobAsync(old, oldTv, oldDownload);
        var options = Options("current");
        var current = new AnimeGoSqliteDatabase(DatabaseFile, options);
        await current.InitializeAsync();
        var organization = new MediaOrganizationStore(current);
        var claim = await organization.TryClaimNextAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        Assert.NotNull(claim);
        Assert.Equal(options.Paths.SavePath, claim.SaveRootPath);
        Assert.Equal(options.Downloaders["bt"].DownloadPath, claim.DownloadRootPath);
        var operations = await organization.GetPersistedPlansAsync(claim, CancellationToken.None);
        Assert.Equal(3, operations.Count);
        Assert.Contains(operations, operation => operation.TargetPath == Path.Combine(options.Paths.SavePath, "Series", "S01", "E001.mkv"));
        Assert.Contains(operations, operation => operation.TargetPath == Path.Combine(options.Paths.SavePath, "Series", "S01", "Extras", "SP", "Preview.mkv"));
        Assert.Contains(operations, operation => operation.TargetPath == Path.Combine(options.Paths.SavePath, "Series", "S01", "E001.zh.ass"));
        Assert.All(operations, operation => Assert.StartsWith(options.Downloaders["bt"].DownloadPath, operation.SourcePath, StringComparison.Ordinal));
        var preview = await new DeletePlanStore(current, options).GetPreviewAsync("task");
        Assert.NotNull(preview);
        Assert.Equal(3, preview.MediaFiles.Count);
        Assert.All(preview.MediaFiles, file => Assert.Equal(options.Paths.SavePath, file.RootPath));
        Assert.All(preview.SourceFiles, file => Assert.Equal(options.Downloaders["bt"].DownloadPath, file.RootPath));
        var nfo = await new PendingTmdbNfoRewriteStore(current).TryClaimNextAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        Assert.NotNull(nfo);
        Assert.Equal(options.Paths.SavePath, nfo.SaveRootPath);
    }

    [Fact]
    public async Task UnmappedOrganizationIsReportedAndNotClaimed()
    {
        var database = new AnimeGoSqliteDatabase(DatabaseFile, Options("current"));
        await database.InitializeAsync();
        await SeedJobAsync(database, "/unbound/tv", "/unbound/download");
        Assert.Null(await new MediaOrganizationStore(database).TryClaimNextAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
        await using var connection = await database.OpenConnectionAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT failure_reason FROM ingest_tasks WHERE id = 'task';";
        Assert.Equal("path_mapping_required", await query.ExecuteScalarAsync());
    }

    [Fact]
    public async Task WindowsCaseCollisionIsBlockedButLinuxKeepsBothIdentities()
    {
        var database = new AnimeGoSqliteDatabase(DatabaseFile, Options("current"));
        await database.InitializeAsync();
        await using var connection = await database.OpenConnectionAsync();
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO stored_path_locations(original_path, root_kind, root_id, relative_path, relative_key) VALUES
                ('/old/Show/E001.mkv', 'tv_library', 'default', 'Show/E001.mkv', 'SHOW/E001.MKV'),
                ('/old/show/E001.mkv', 'tv_library', 'default', 'show/E001.mkv', 'SHOW/E001.MKV');
            """;
        await insert.ExecuteNonQueryAsync();
        insert.CommandText = "SELECT app_path('/old/Show/E001.mkv');";
        if (OperatingSystem.IsWindows())
        {
            var error = await Assert.ThrowsAsync<SqliteException>(async () => await insert.ExecuteScalarAsync());
            Assert.Contains("path_case_collision", error.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(Path.Combine(Options("current").Paths.SavePath, "Show", "E001.mkv"), await insert.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task ExistingIdentityIsNotReinferredWhenNewRootOverlapsOldRoot()
    {
        var options = Options("old");
        var database = new AnimeGoSqliteDatabase(DatabaseFile, options);
        await database.InitializeAsync();
        await using (var connection = await database.OpenConnectionAsync())
            await InsertCompletionAsync(connection, Path.Combine(options.Paths.SavePath, "nested", "Show", "E001.mkv"));
        var newOptions = options with { Paths = options.Paths with { SavePath = Path.Combine(options.Paths.SavePath, "nested") } };
        var current = new AnimeGoSqliteDatabase(DatabaseFile, newOptions);
        await current.InitializeAsync();
        await using var currentConnection = await current.OpenConnectionAsync();
        await using var query = currentConnection.CreateCommand();
        query.CommandText = "SELECT app_path(media_path) FROM completion_records;";
        Assert.Equal(Path.Combine(newOptions.Paths.SavePath, "nested", "Show", "E001.mkv"), await query.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ContradictoryLegacyLibraryIdentitiesStayUnresolvedAcrossRestarts()
    {
        var legacy = new AnimeGoSqliteDatabase(DatabaseFile);
        await legacy.InitializeAsync();
        await using (var connection = await legacy.OpenConnectionAsync())
        {
            await InsertCompletionAsync(connection, "/shared/Film.mkv");
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO movie_completion_records VALUES ('movie', 1, 'u2', NULL, '/shared/Film.mkv', '2026-10-05');
                INSERT INTO path_root_history VALUES ('tv_library', 'default', '/shared'), ('movie_library', 'default', '/shared');
                """;
            await insert.ExecuteNonQueryAsync();
        }
        var current = new AnimeGoSqliteDatabase(DatabaseFile, Options("current"));
        await current.InitializeAsync();
        await current.InitializeAsync();
        await using var currentConnection = await current.OpenConnectionAsync();
        await using var query = currentConnection.CreateCommand();
        query.CommandText = "SELECT mapping_conflict FROM stored_path_locations WHERE original_path = '/shared/Film.mkv';";
        Assert.Equal(1L, await query.ExecuteScalarAsync());
        query.CommandText = "SELECT app_path_optional(media_path) FROM completion_records;";
        Assert.Equal(DBNull.Value, await query.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedDownloaderRootsKeepIndependentIdentitiesWhenLaterSeparated(bool legacySchema)
    {
        var options = Options("old");
        var sharedRoot = options.Downloaders["bt"].DownloadPath;
        options = options with
        {
            Downloaders = new Dictionary<string, QbittorrentInstanceOptions>(options.Downloaders)
            {
                ["pt"] = options.Downloaders["pt"] with { DownloadPath = sharedRoot },
            },
        };
        var database = new AnimeGoSqliteDatabase(DatabaseFile, legacySchema ? null : options);
        if (legacySchema)
        {
            await using var connection = await database.OpenConnectionAsync();
            await SchemaMigrationRunner.ApplyAsync(connection, DatabaseSchema.Migrations.Take(75).ToArray(), CancellationToken.None);
        }
        else await database.InitializeAsync();
        await SeedJobAsync(database, options.Paths.SavePath, sharedRoot);
        await using (var connection = await database.OpenConnectionAsync())
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO ingest_tasks(id, source_profile_id, source_profile_revision, source_id, title,
                    torrent_url_fingerprint, downloader_id, route_snapshot_json, status, created_at_utc, updated_at_utc)
                VALUES ('pt-task', 'mikan', 1, 'u2', 'Series', 'pt-hash', 'pt', '{"file_strategy":"link"}', 'downloaded', '2026-10-05', '2026-10-05');
                INSERT INTO download_jobs(id, task_id, downloader_id, state, progress, downloaded_bytes,
                    total_bytes, speed_bytes_per_second, download_root_path, save_root_path, created_at_utc, updated_at_utc)
                VALUES ('pt-job', 'pt-task', 'pt', 'complete', 1, 5, 5, 0, $download, $tv, '2026-10-05', '2026-10-05');
                INSERT INTO task_files(id, task_id, relative_path, size_bytes, disposition)
                VALUES ('pt-file', 'pt-task', 'E001.mkv', 5, 'extras');
                INSERT INTO file_operations(id, task_file_id, strategy, source_path, target_path, state, bytes_verified, created_at_utc, updated_at_utc)
                VALUES ('pt-op', 'pt-file', 'link', $download || '/E001.mkv', $tv || '/Series/S01/Extras/PT/E001.mkv', 'completed', 5, '2026-10-05', '2026-10-05');
                """;
            insert.Parameters.AddWithValue("$download", sharedRoot);
            insert.Parameters.AddWithValue("$tv", options.Paths.SavePath);
            await insert.ExecuteNonQueryAsync();
        }
        var separated = Options("new");
        var current = new AnimeGoSqliteDatabase(DatabaseFile, separated);
        await current.InitializeAsync();
        await using var verify = await current.OpenConnectionAsync();
        await using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT task.downloader_id, app_path(operation.source_path, task.downloader_id)
            FROM file_operations operation JOIN task_files file ON file.id = operation.task_file_id
            JOIN ingest_tasks task ON task.id = file.task_id WHERE operation.id IN ('op1', 'pt-op')
            ORDER BY task.downloader_id;
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("bt", reader.GetString(0));
        Assert.Equal(Path.Combine(separated.Downloaders["bt"].DownloadPath, "E001.mkv"), reader.GetString(1));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("pt", reader.GetString(0));
        Assert.Equal(Path.Combine(separated.Downloaders["pt"].DownloadPath, "E001.mkv"), reader.GetString(1));
    }

    private static async Task SeedJobAsync(AnimeGoSqliteDatabase database, string tvRoot, string downloadRoot)
    {
        await new SourceProfileStore(database).EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO anime_series(id, tmdb_series_id, canonical_name, needs_tmdb_completion, created_at_utc, updated_at_utc)
            VALUES ('series', 1, 'Series', 0, $now, $now);
            INSERT INTO ingest_tasks(id, source_profile_id, source_profile_revision, source_id, title,
                torrent_url_fingerprint, downloader_id, route_snapshot_json, status, created_at_utc, updated_at_utc)
            VALUES ('task', 'mikan', 1, 'mikan', 'Series', 'hash', 'bt', '{"file_strategy":"link"}', 'downloaded', $now, $now);
            INSERT INTO download_jobs(id, task_id, downloader_id, info_hash, state, progress, downloaded_bytes,
                total_bytes, speed_bytes_per_second, download_root_path, save_root_path, organization_state, created_at_utc, updated_at_utc)
            VALUES ('job', 'task', 'bt', 'hash', 'complete', 1, 15, 15, 0, $download, $tv, 'pending', $now, $now);
            INSERT INTO task_files(id, task_id, relative_path, size_bytes, disposition, tmdb_series_id,
                tmdb_season_number, tmdb_episode_number, download_wanted)
            VALUES ('episode', 'task', 'E001.mkv', 5, 'episode', 1, 1, 1, 1),
                   ('extra', 'task', 'SP/Preview.mkv', 5, 'extras', 1, 1, NULL, 1),
                   ('sub', 'task', 'E001.zh.ass', 5, 'extras', 1, 1, NULL, 1);
            INSERT INTO file_operations(id, task_file_id, strategy, source_path, target_path, state, bytes_verified, created_at_utc, updated_at_utc)
            VALUES ('op1', 'episode', 'link', $download || '/E001.mkv', $tv || '/Series/S01/E001.mkv', 'completed', 5, $now, $now),
                   ('op2', 'extra', 'link', $download || '/SP/Preview.mkv', $tv || '/Series/S01/Extras/SP/Preview.mkv', 'completed', 5, $now, $now),
                   ('op3', 'sub', 'link', $download || '/E001.zh.ass', $tv || '/Series/S01/E001.zh.ass', 'completed', 5, $now, $now);
            INSERT INTO pending_tmdb_nfo_rewrite_jobs(id, bangumi_subject_id, tmdb_series_id, save_root_path,
                series_directory_name, canonical_series_name, state, created_at_utc, updated_at_utc)
            VALUES ('rewrite', 1, 1, $tv, 'Series', 'Series', 'pending', $now, $now);
            """;
        command.Parameters.AddWithValue("$tv", tvRoot);
        command.Parameters.AddWithValue("$download", downloadRoot);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
