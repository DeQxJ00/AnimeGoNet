using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Tests.Sqlite;

public sealed class SchemaMigrationTests
{
    [Fact]
    public async Task InitialMigrationCreatesEveryFirstPhaseTable()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table' ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        var expected = new[]
        {
            "anime_seasons",
            "anime_series",
            "cache_buckets",
            "cache_entries",
            "bangumi_archive_episodes",
            "bangumi_archive_subjects",
            "bangumi_archive_usage",
            "completion_aliases",
            "completion_records",
            "data_update_runs",
            "data_update_downloads",
            "data_update_staging_episodes",
            "data_update_staging_subjects",
            "data_update_state",
            "data_update_transfer_runs",
            "data_update_versions",
            "delete_executions",
            "delete_execution_items",
            "directory_database_entries",
            "directory_database_scan_issues",
            "directory_database_scan_runs",
            "download_jobs",
            "download_job_events",
            "downloader_runtime_state",
            "episode_claims",
            "fallback_claims",
            "fallback_completion_records",
            "file_operations",
            "ingest_tasks",
            "legacy_cache_imports",
            "legacy_mikan_filter_rules",
            "legacy_mikan_filter_sets",
            "legacy_mikan_filter_snapshots",
            "legacy_mikan_filter_values",
            "metadata_resolution_attempts",
            "metadata_resolution_runs",
            "mikan_offset_evidence",
            "mikan_rss_batches",
            "mikan_rss_batch_entries",
            "mikan_rss_decision_groups",
            "mikan_rss_match_arrays",
            "mikan_rss_match_values",
            "mikan_rss_priority_groups",
            "mikan_rss_rule_sets",
            "mikan_rss_rule_snapshots",
            "mikan_rss_snapshot_match_arrays",
            "mikan_rss_snapshot_match_values",
            "mikan_rss_snapshot_priority_groups",
            "mikan_trusted_offsets",
            "mikan_work_rules",
            "pending_tmdb_nfo_rewrite_jobs",
            "schema_migrations",
            "source_profiles",
            "staged_torrents",
            "task_files",
            "tmdb_episodes",
        };
        Assert.All(expected, table => Assert.Contains(table, tables));
    }

    [Fact]
    public async Task InitializeIsIdempotentAndRecordsEveryMigration()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();

        await fixture.Database.InitializeAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MAX(version) FROM schema_migrations;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(DatabaseSchema.CurrentVersion, reader.GetInt32(0));
        Assert.Equal(DatabaseSchema.CurrentVersion, reader.GetInt32(1));
    }

    [Fact]
    public async Task LibraryMetadataAuditMigrationCreatesTargetedIndexes()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_schema
            WHERE type = 'index'
              AND name IN (
                  'ix_task_files_tmdb_season_task',
                  'ix_metadata_runs_tmdb_season_task',
                  'ix_metadata_attempts_run_created',
                  'ix_mikan_work_rules_tmdb_season')
            ORDER BY name;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var indexes = new List<string>();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }

        Assert.Equal(
            [
                "ix_metadata_attempts_run_created",
                "ix_metadata_runs_tmdb_season_task",
                "ix_mikan_work_rules_tmdb_season",
                "ix_task_files_tmdb_season_task",
            ],
            indexes);
    }

    [Fact]
    public async Task NativeSmokeDefaultTracksCurrentSchemaVersion()
    {
        var scriptPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "eng",
            "smoke-native.ps1"));
        Assert.True(File.Exists(scriptPath), $"Native smoke script was not found: {scriptPath}");

        var script = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains(
            $"[int]$ExpectedSchemaVersion = {DatabaseSchema.CurrentVersion}",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletedMetadataOrganizationRecoveryOnlyRepairsResolvedDownloadedTasks()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES ('repair', 'Repair', 'mikan', 'bt', 'move', 0, 0, 1, 1, $now, $now);

                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    title, torrent_url_fingerprint, downloader_id, route_snapshot_json,
                    status, created_at_utc, updated_at_utc)
                VALUES
                    ('repairable', 'repair', 1, 'mikan', 'Repairable', $fingerprint_a,
                     'bt', '{"file_strategy":"move"}', 'metadata_season_resolved', $now, $now),
                    ('pending-file', 'repair', 1, 'mikan', 'Pending', $fingerprint_b,
                     'bt', '{"file_strategy":"move"}', 'metadata_season_resolved', $now, $now);

                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, disposition, other_reason)
                VALUES
                    ('repairable-file', 'repairable', 'done.nfo', 1, 'ignored', 'not_media'),
                    ('pending-file-row', 'pending-file', 'pending.mkv', 1, 'pending', NULL);

                INSERT INTO download_jobs (
                    id, task_id, downloader_id, info_hash, state, progress,
                    downloaded_bytes, total_bytes, speed_bytes_per_second,
                    created_at_utc, updated_at_utc, preparation_state,
                    organization_state)
                VALUES
                    ('repairable-job', 'repairable', 'bt', $hash_a, 'complete', 1,
                     1, 1, 0, $now, $now, 'completed', 'pending'),
                    ('pending-job', 'pending-file', 'bt', $hash_b, 'complete', 1,
                     1, 1, 0, $now, $now, 'completed', 'pending');
                """;
            seed.Parameters.AddWithValue("$now", "2026-08-13T00:00:00.0000000+00:00");
            seed.Parameters.AddWithValue("$fingerprint_a", new string('a', 64));
            seed.Parameters.AddWithValue("$fingerprint_b", new string('b', 64));
            seed.Parameters.AddWithValue("$hash_a", new string('a', 40));
            seed.Parameters.AddWithValue("$hash_b", new string('b', 40));
            Assert.Equal(7, await seed.ExecuteNonQueryAsync());
        }

        var migration = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 44);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration.Sql;
            Assert.Equal(1, await migrate.ExecuteNonQueryAsync());
        }

        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT id, status FROM ingest_tasks ORDER BY id;";
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("pending-file", reader.GetString(0));
        Assert.Equal("metadata_season_resolved", reader.GetString(1));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("repairable", reader.GetString(0));
        Assert.Equal("downloaded", reader.GetString(1));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task MediaOrganizationProgressMigrationBackfillsDurableStages()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
            await foreignKeys.ExecuteNonQueryAsync();
        }

        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 36))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES ('mikan', 'Mikan', 'mikan', 'bt', 'move', 1, 1, 1, 1, $now, $now);

                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    title, torrent_url_fingerprint, downloader_id, route_snapshot_json,
                    status, created_at_utc, updated_at_utc)
                VALUES
                    ('completed-task', 'mikan', 1, 'mikan', 'Completed', $fingerprint_a,
                     'bt', '{}', 'organized', $now, $now),
                    ('cleanup-task', 'mikan', 1, 'mikan', 'Cleanup', $fingerprint_b,
                     'bt', '{}', 'organizing_cleanup', $now, $now);

                INSERT INTO download_jobs (
                    id, task_id, downloader_id, info_hash, state, progress,
                    downloaded_bytes, total_bytes, speed_bytes_per_second,
                    created_at_utc, updated_at_utc, organization_state)
                VALUES
                    ('completed-job', 'completed-task', 'bt', $hash_a, 'complete', 1,
                     5, 5, 0, $now, $now, 'completed'),
                    ('cleanup-job', 'cleanup-task', 'bt', $hash_b, 'complete', 1,
                     5, 5, 0, $now, $now, 'cleanup');
                """;
            seed.Parameters.AddWithValue("$now", "2026-08-08T00:00:00.0000000+00:00");
            seed.Parameters.AddWithValue("$fingerprint_a", new string('a', 64));
            seed.Parameters.AddWithValue("$fingerprint_b", new string('b', 64));
            seed.Parameters.AddWithValue("$hash_a", new string('a', 40));
            seed.Parameters.AddWithValue("$hash_b", new string('b', 40));
            Assert.Equal(5, await seed.ExecuteNonQueryAsync());
        }

        var progressMigration = Assert.Single(
            DatabaseSchema.Migrations,
            item => item.Version == 37);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = progressMigration.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT id, organization_phase, organization_completed_units,
                   organization_total_units
            FROM download_jobs
            ORDER BY id;
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("cleanup-job", reader.GetString(0));
        Assert.Equal("cleanup_downloader", reader.GetString(1));
        Assert.Equal(0, reader.GetInt32(2));
        Assert.Equal(1, reader.GetInt32(3));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("completed-job", reader.GetString(0));
        Assert.Equal("completed", reader.GetString(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal(1, reader.GetInt32(3));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task SourceDuplicateNotificationMigrationDefaultsExistingProfilesToEnabled()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 37))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'mikan', 'Mikan', 'mikan', 'bt', 'move',
                    1, 1, 1, 1, $now, $now);
                """;
            seed.Parameters.AddWithValue("$now", "2026-08-08T00:00:00.0000000+00:00");
            Assert.Equal(1, await seed.ExecuteNonQueryAsync());
        }

        var notificationMigration = Assert.Single(
            DatabaseSchema.Migrations,
            item => item.Version == 38);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = notificationMigration.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT duplicate_notification_enabled
                FROM source_profiles
                WHERE id = 'mikan';
                """;
            Assert.Equal(1L, await query.ExecuteScalarAsync());
        }

        await using (var disable = connection.CreateCommand())
        {
            disable.CommandText = """
                UPDATE source_profiles
                SET duplicate_notification_enabled = 0
                WHERE id = 'mikan';
                """;
            Assert.Equal(1, await disable.ExecuteNonQueryAsync());
        }

        await using var invalid = connection.CreateCommand();
        invalid.CommandText = """
            UPDATE source_profiles
            SET duplicate_notification_enabled = 2
            WHERE id = 'mikan';
            """;
        await Assert.ThrowsAsync<SqliteException>(invalid.ExecuteNonQueryAsync);
    }

    [Fact]
    public async Task LegacyCacheImportAuditMigrationUpgradesVersion38WithStrictConstraints()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 38))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal(
            0L,
            await ScalarInt64Async(
                connection,
                "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'legacy_cache_imports';"));
        var migration39 = Assert.Single(
            DatabaseSchema.Migrations,
            item => item.Version == 39);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration39.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using (var valid = connection.CreateCommand())
        {
            valid.CommandText = """
                INSERT INTO legacy_cache_imports (
                    package_sha256, format_version, source_commit,
                    bucket_count, entry_count, imported_entry_count,
                    skipped_expired_entry_count, imported_at_utc,
                    last_seen_at_utc, repeat_count)
                VALUES ($digest, 1, 'develop@test', 1, 2, 1, 1, $now, $now, 0);
                """;
            valid.Parameters.AddWithValue("$digest", new string('a', 64));
            valid.Parameters.AddWithValue("$now", "2026-08-08T00:00:00.0000000+00:00");
            Assert.Equal(1, await valid.ExecuteNonQueryAsync());
        }

        await using var invalid = connection.CreateCommand();
        invalid.CommandText = """
            INSERT INTO legacy_cache_imports (
                package_sha256, format_version, source_commit,
                bucket_count, entry_count, imported_entry_count,
                skipped_expired_entry_count, imported_at_utc,
                last_seen_at_utc, repeat_count)
            VALUES ('short', 1, 'develop@test', 0, 0, 0, 0, $now, $now, 0);
            """;
        invalid.Parameters.AddWithValue("$now", "2026-08-08T00:00:00.0000000+00:00");
        await Assert.ThrowsAsync<SqliteException>(invalid.ExecuteNonQueryAsync);
    }

    [Fact]
    public async Task DownloadSeedingLifecycleMigrationBackfillsImmutableTargetsAndStates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
            await foreignKeys.ExecuteNonQueryAsync();
        }

        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 32))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        const string created = "2026-07-31T09:00:00.0000000+00:00";
        const string completed = "2026-07-31T10:00:00.0000000+00:00";
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'mikan', 'Mikan', 'mikan', 'bt', 'move',
                    1, 1, 1, 1, $created, $created);

                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    title, torrent_url_fingerprint, downloader_id,
                    route_snapshot_json, status, created_at_utc, updated_at_utc)
                VALUES
                    ('move-task', 'mikan', 1, 'mikan', 'Move', 'move-fingerprint', 'bt',
                     '{"file_strategy":"move","seeding_time_minutes":30}',
                     'downloaded', $created, $created),
                    ('link-task', 'mikan', 1, 'mikan', 'Link', 'link-fingerprint', 'bt',
                     '{"file_strategy":"link","seeding_time_minutes":30}',
                     'downloaded', $created, $created),
                    ('infinite-task', 'mikan', 1, 'mikan', 'Infinite', 'infinite-fingerprint', 'bt',
                     '{"file_strategy":"wait_move","seeding_time_minutes":-1}',
                     'downloaded', $created, $completed);

                INSERT INTO download_jobs (
                    id, task_id, downloader_id, info_hash, state, progress,
                    downloaded_bytes, total_bytes, speed_bytes_per_second,
                    eta_seconds, failure_reason, created_at_utc, updated_at_utc)
                VALUES
                    ('move-job', 'move-task', 'bt', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                     'seeding', 1, 1, 1, 0, NULL, NULL, $created, $created),
                    ('link-job', 'link-task', 'bt', 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
                     'seeding', 1, 1, 1, 0, NULL, NULL, $created, $created),
                    ('infinite-job', 'infinite-task', 'bt', 'cccccccccccccccccccccccccccccccccccccccc',
                     'complete', 1, 1, 1, 0, NULL, NULL, $created, $completed);
                """;
            seed.Parameters.AddWithValue("$created", created);
            seed.Parameters.AddWithValue("$completed", completed);
            Assert.Equal(7, await seed.ExecuteNonQueryAsync());
        }

        var migration33 = Assert.Single(
            DatabaseSchema.Migrations,
            item => item.Version == 33);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration33.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT id, seeding_target_minutes, seeding_state,
                       seeding_elapsed_seconds, seeding_completed_at_utc
                FROM download_jobs ORDER BY id;
                """;
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("infinite-job", reader.GetString(0));
            Assert.Equal(-1, reader.GetInt32(1));
            Assert.Equal("completed", reader.GetString(2));
            Assert.Equal(0, reader.GetInt64(3));
            Assert.Equal(completed, reader.GetString(4));
            Assert.True(await reader.ReadAsync());
            Assert.Equal("link-job", reader.GetString(0));
            Assert.Equal(30, reader.GetInt32(1));
            Assert.Equal("seeding", reader.GetString(2));
            Assert.True(reader.IsDBNull(4));
            Assert.True(await reader.ReadAsync());
            Assert.Equal("move-job", reader.GetString(0));
            Assert.Equal(0, reader.GetInt32(1));
            Assert.Equal("not_required", reader.GetString(2));
            Assert.True(reader.IsDBNull(4));
            Assert.False(await reader.ReadAsync());
        }

        await using (var index = connection.CreateCommand())
        {
            index.CommandText = """
                SELECT COUNT(*) FROM sqlite_schema
                WHERE type = 'index' AND name = 'ix_download_jobs_seeding_state';
                """;
            Assert.Equal(1L, await index.ExecuteScalarAsync());
        }

        await using var invalid = connection.CreateCommand();
        invalid.CommandText = """
            UPDATE download_jobs SET seeding_target_minutes = -2 WHERE id = 'link-job';
            """;
        await Assert.ThrowsAsync<SqliteException>(() => invalid.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task DynamicDownloadTagsMigrationBackfillsOnlyMikanAndKeepsJobsAuditable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 33))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        const string now = "2026-08-01T10:00:00.0000000+00:00";
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES
                    ('mikan', 'Mikan', 'mikan', 'bt', 'move', 1, 1, 7, 1, $now, $now),
                    ('u2', 'U2', 'u2', 'pt', 'link', 1, 1, 3, 1, $now, $now);

                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    title, torrent_url_fingerprint, downloader_id,
                    route_snapshot_json, status, created_at_utc, updated_at_utc)
                VALUES (
                    'task', 'mikan', 7, 'mikan', 'Episode', 'fingerprint', 'bt',
                    '{}', 'metadata_resolved', $now, $now);

                INSERT INTO download_jobs (
                    id, task_id, downloader_id, info_hash, state, progress,
                    downloaded_bytes, total_bytes, speed_bytes_per_second,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'job', 'task', 'bt', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                    'paused', 0, 0, 1, 0, $now, $now);
                """;
            seed.Parameters.AddWithValue("$now", now);
            Assert.Equal(4, await seed.ExecuteNonQueryAsync());
        }

        var migration34 = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 34);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration34.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using (var profiles = connection.CreateCommand())
        {
            profiles.CommandText = """
                SELECT id, dynamic_tag_template,
                       dynamic_tag_template_initialized, revision
                FROM source_profiles ORDER BY id;
                """;
            await using var reader = await profiles.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("mikan", reader.GetString(0));
            Assert.Equal("{year}年{quarter}月新番", reader.GetString(1));
            Assert.Equal(0, reader.GetInt32(2));
            Assert.Equal(7, reader.GetInt64(3));
            Assert.True(await reader.ReadAsync());
            Assert.Equal("u2", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.Equal(0, reader.GetInt32(2));
            Assert.Equal(3, reader.GetInt64(3));
            Assert.False(await reader.ReadAsync());
        }

        await using (var job = connection.CreateCommand())
        {
            job.CommandText = """
                SELECT dynamic_tags_json, dynamic_tag_state, dynamic_tag_failure_code
                FROM download_jobs WHERE id = 'job';
                """;
            await using var reader = await job.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("[]", reader.GetString(0));
            Assert.Equal("not_configured", reader.GetString(1));
            Assert.True(reader.IsDBNull(2));
        }

        await using (var index = connection.CreateCommand())
        {
            index.CommandText = """
                SELECT COUNT(*) FROM sqlite_schema
                WHERE type = 'index' AND name = 'ix_download_jobs_dynamic_tag_state';
                """;
            Assert.Equal(1L, await index.ExecuteScalarAsync());
        }

        await using var invalid = connection.CreateCommand();
        invalid.CommandText = "UPDATE download_jobs SET dynamic_tag_state = 'unknown' WHERE id = 'job';";
        await Assert.ThrowsAsync<SqliteException>(() => invalid.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task CompletionSourceAliasAuditMigrationPreservesHistoricalAliasesAndAddsLookupEvidence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 34))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        const string now = "2026-08-01T10:00:00.0000000+00:00";
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'mikan', 'Mikan', 'mikan', 'bt', 'move',
                    1, 1, 1, 1, $now, $now);
                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    source_item_id, source_work_id, title, torrent_url_fingerprint,
                    downloader_id, route_snapshot_json, status, created_at_utc, updated_at_utc)
                VALUES (
                    'historical-task', 'mikan', 1, 'mikan', 'episode-item', '3951',
                    'Historical Episode', 'fingerprint', 'bt', '{}', 'organized', $now, $now);
                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, source_episode,
                    tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    tmdb_episode_id, disposition)
                VALUES (
                    'historical-file', 'historical-task', 'Show - 04.mkv', 5, '4',
                    42, 1, 4, 4204, 'episode');
                INSERT INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, source_item_id, completed_at_utc)
                VALUES
                    ('completion', 42, 1, 3, 'mikan', 'duplicate-item', $now),
                    ('historical-completion', 42, 1, 4, 'mikan', 'episode-item', $now);
                INSERT INTO completion_aliases (
                    id, completion_id, source_id, source_work_id, source_episode, created_at_utc)
                VALUES
                    ('alias-a', 'completion', 'mikan', '3951', '3', $now),
                    ('alias-b', 'completion', 'mikan', '3951', '3', $now);
                """;
            seed.Parameters.AddWithValue("$now", now);
            Assert.Equal(7, await seed.ExecuteNonQueryAsync());
        }

        await using (var historicalCandidate = connection.CreateCommand())
        {
            historicalCandidate.CommandText = """
                SELECT COUNT(*)
                FROM completion_records AS completion
                JOIN ingest_tasks AS task
                  ON task.source_id = completion.source_id
                 AND task.source_item_id = completion.source_item_id
                JOIN task_files AS file
                  ON file.task_id = task.id
                 AND file.tmdb_series_id = completion.tmdb_series_id
                 AND file.tmdb_season_number = completion.tmdb_season_number
                 AND file.tmdb_episode_number = completion.tmdb_episode_number
                WHERE completion.id = 'historical-completion'
                  AND task.source_work_id = '3951'
                  AND file.source_episode = '4'
                  AND file.associated_task_file_id IS NULL;
                """;
            Assert.Equal(1L, await historicalCandidate.ExecuteScalarAsync());
        }

        var migration35 = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 35);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration35.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using (var columns = connection.CreateCommand())
        {
            columns.CommandText = """
                SELECT name FROM pragma_table_info('mikan_rss_batch_entries')
                WHERE name LIKE 'early_completion%'
                ORDER BY name;
                """;
            await using var reader = await columns.ExecuteReaderAsync();
            var names = new List<string>();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            Assert.Equal(
                [
                    "early_completion_alias_id",
                    "early_completion_checked_at_utc",
                    "early_completion_id",
                ],
                names);
        }

        await using (var indexes = connection.CreateCommand())
        {
            indexes.CommandText = """
                SELECT name FROM sqlite_schema
                WHERE type = 'index' AND name IN (
                    'ix_completion_aliases_source_episode',
                    'ix_mikan_rss_entries_early_completion')
                ORDER BY name;
                """;
            await using var reader = await indexes.ExecuteReaderAsync();
            var names = new List<string>();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            Assert.Equal(
                [
                    "ix_completion_aliases_source_episode",
                    "ix_mikan_rss_entries_early_completion",
                ],
                names);
        }

        await using var count = connection.CreateCommand();
        count.CommandText = """
            SELECT COUNT(*),
                   COUNT(*) FILTER (
                       WHERE id = 'v35-historical-completion-historical-file'
                         AND source_id = 'mikan'
                         AND source_work_id = '3951'
                         AND source_episode = '4')
            FROM completion_aliases;
            """;
        await using var countReader = await count.ExecuteReaderAsync();
        Assert.True(await countReader.ReadAsync());
        Assert.Equal(3, countReader.GetInt32(0));
        Assert.Equal(1, countReader.GetInt32(1));
    }

    [Fact]
    public async Task SourceRssSchedulingMigrationPreservesProfilesAndAddsSafeDefaults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 35))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        const string now = "2026-08-01T12:00:00.0000000+00:00";
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'mikan', 'Mikan', 'mikan', 'bt', 'move',
                    1, 1, 7, 1, $now, $now);
                """;
            seed.Parameters.AddWithValue("$now", now);
            Assert.Equal(1, await seed.ExecuteNonQueryAsync());
        }

        var migration36 = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 36);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration36.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using (var profile = connection.CreateCommand())
        {
            profile.CommandText = """
                SELECT revision, rss_feed_url, rss_schedule_enabled, rss_schedule_cron,
                       rss_last_run_state, rss_last_started_at_utc,
                       rss_last_completed_at_utc, rss_last_failure_code, rss_last_batch_id
                FROM source_profiles WHERE id = 'mikan';
                """;
            await using var reader = await profile.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(7, reader.GetInt64(0));
            Assert.True(reader.IsDBNull(1));
            Assert.False(reader.GetBoolean(2));
            Assert.Equal("0 0/15 * * * ?", reader.GetString(3));
            Assert.Equal("never", reader.GetString(4));
            Assert.True(reader.IsDBNull(5));
            Assert.True(reader.IsDBNull(6));
            Assert.True(reader.IsDBNull(7));
            Assert.True(reader.IsDBNull(8));
        }

        await using (var index = connection.CreateCommand())
        {
            index.CommandText = """
                SELECT COUNT(*) FROM sqlite_schema
                WHERE type = 'index' AND name = 'ix_source_profiles_rss_schedule';
                """;
            Assert.Equal(1L, await index.ExecuteScalarAsync());
        }

        await using var invalid = connection.CreateCommand();
        invalid.CommandText =
            "UPDATE source_profiles SET rss_last_run_state = 'unknown' WHERE id = 'mikan';";
        await Assert.ThrowsAsync<SqliteException>(() => invalid.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task TmdbResolutionEvidenceMigrationBackfillsRunsAndGuardsReferences()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
            await foreignKeys.ExecuteNonQueryAsync();
        }

        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 31))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        const string now = "2026-07-30T10:00:00.0000000+00:00";
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'mikan', 'Mikan', 'mikan', 'bt', 'move',
                    1, 1, 1, 1, $now, $now);

                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    title, torrent_url_fingerprint, downloader_id,
                    route_snapshot_json, status, created_at_utc, updated_at_utc)
                VALUES (
                    'task', 'mikan', 1, 'mikan', 'Episode',
                    'fingerprint', 'bt', '{}', 'metadata_resolved', $now, $now);

                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes,
                    tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    disposition)
                VALUES ('file', 'task', 'episode.mkv', 1, 100, 2, 3, 'episode');

                INSERT INTO metadata_resolution_runs (
                    id, task_id, status, tmdb_access_confirmed,
                    fallback_eligible, started_at_utc, completed_at_utc,
                    attempt_number, tmdb_series_id, tmdb_season_number)
                VALUES (
                    'run', 'task', 'resolved', 1, 0, $now, $now, 1, 100, 2);

                INSERT INTO metadata_resolution_attempts (
                    id, run_id, stage, strategy, priority, result,
                    retryable, attempt_number, duration_ms, created_at_utc)
                VALUES
                    ('series-attempt', 'run', 'series', 'tmdb_title',
                     NULL, 'matched', 0, 1, 10, $now),
                    ('season-attempt', 'run', 'season', 'tmdb_air_date',
                     4, 'matched', 0, 1, 20, $now);
                """;
            seed.Parameters.AddWithValue("$now", now);
            Assert.Equal(6, await seed.ExecuteNonQueryAsync());
        }

        var migration32 = Assert.Single(
            DatabaseSchema.Migrations,
            item => item.Version == 32);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration32.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT series_resolution_source, series_resolution_attempt_id,
                       season_resolution_source, season_resolution_attempt_id
                FROM metadata_resolution_runs WHERE id = 'run';
                """;
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("tmdb_title", reader.GetString(0));
            Assert.Equal("series-attempt", reader.GetString(1));
            Assert.Equal("tmdb_air_date", reader.GetString(2));
            Assert.Equal("season-attempt", reader.GetString(3));
        }

        await using (var invalidRun = connection.CreateCommand())
        {
            invalidRun.CommandText = """
                UPDATE metadata_resolution_runs
                SET series_resolution_source = 'tmdb_title',
                    series_resolution_attempt_id = 'season-attempt'
                WHERE id = 'run';
                """;
            await Assert.ThrowsAsync<SqliteException>(
                () => invalidRun.ExecuteNonQueryAsync());
        }

        await using (var invalidFile = connection.CreateCommand())
        {
            invalidFile.CommandText = """
                UPDATE task_files
                SET episode_resolution_source = 'tmdb_episode_number',
                    episode_resolution_run_id = 'run',
                    episode_resolution_attempt_id = 'season-attempt'
                WHERE id = 'file';
                """;
            await Assert.ThrowsAsync<SqliteException>(
                () => invalidFile.ExecuteNonQueryAsync());
        }

        await using (var invalidRunInsert = connection.CreateCommand())
        {
            invalidRunInsert.CommandText = """
                INSERT INTO metadata_resolution_runs (
                    id, task_id, status, tmdb_access_confirmed,
                    fallback_eligible, started_at_utc, completed_at_utc,
                    attempt_number, tmdb_series_id, tmdb_season_number,
                    series_resolution_source, series_resolution_attempt_id)
                VALUES (
                    'invalid-run', 'task', 'resolved', 1, 0, $now, $now,
                    2, 100, 2, 'tmdb_title', 'series-attempt');
                """;
            invalidRunInsert.Parameters.AddWithValue("$now", now);
            await Assert.ThrowsAsync<SqliteException>(
                () => invalidRunInsert.ExecuteNonQueryAsync());
        }

        await using (var invalidFileInsert = connection.CreateCommand())
        {
            invalidFileInsert.CommandText = """
                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes,
                    tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    disposition, episode_resolution_source,
                    episode_resolution_run_id, episode_resolution_attempt_id)
                VALUES (
                    'invalid-file', 'task', 'invalid.mkv', 1,
                    100, 2, 4, 'episode', 'tmdb_episode_number',
                    'run', 'season-attempt');
                """;
            await Assert.ThrowsAsync<SqliteException>(
                () => invalidFileInsert.ExecuteNonQueryAsync());
        }

        await using (var validFile = connection.CreateCommand())
        {
            validFile.CommandText = """
                INSERT INTO metadata_resolution_attempts (
                    id, run_id, stage, strategy, priority, result,
                    retryable, attempt_number, duration_ms, created_at_utc)
                VALUES (
                    'episode-attempt', 'run', 'episode', 'tmdb_episode_number',
                    NULL, 'matched', 0, 1, 30, $now);

                UPDATE task_files
                SET episode_resolution_source = 'tmdb_episode_number',
                    episode_resolution_run_id = 'run',
                    episode_resolution_attempt_id = 'episode-attempt'
                WHERE id = 'file';
                """;
            validFile.Parameters.AddWithValue("$now", now);
            Assert.Equal(2, await validFile.ExecuteNonQueryAsync());
        }

        await using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT episode_resolution_source, episode_resolution_run_id,
                   episode_resolution_attempt_id
            FROM task_files WHERE id = 'file';
            """;
        await using var verifyReader = await verify.ExecuteReaderAsync();
        Assert.True(await verifyReader.ReadAsync());
        Assert.Equal("tmdb_episode_number", verifyReader.GetString(0));
        Assert.Equal("run", verifyReader.GetString(1));
        Assert.Equal("episode-attempt", verifyReader.GetString(2));
    }

    [Fact]
    public async Task DataUpdateTransferMigrationPreservesVersion28ActiveData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
            await foreignKeys.ExecuteNonQueryAsync();
        }
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 28))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO data_update_versions (
                    data_version, schema_version, generated_at_utc,
                    minimum_client_version, manifest_sha256,
                    upstream_repository, upstream_release, upstream_asset, upstream_sha256,
                    subject_count, episode_count, state,
                    installed_at_utc, activated_at_utc)
                VALUES (
                    '2026.07.29.1', 1, $now, '0.1.0', $hash,
                    'https://github.com/bangumi/Archive', 'archive', 'asset.zip', $hash,
                    1, 1, 'active', $now, $now);

                UPDATE data_update_state
                SET active_version = '2026.07.29.1', updated_at_utc = $now
                WHERE singleton = 1;
                """;
            seed.Parameters.AddWithValue("$now", "2026-07-29T12:00:00.0000000+00:00");
            seed.Parameters.AddWithValue("$hash", new string('a', 64));
            Assert.Equal(2, await seed.ExecuteNonQueryAsync());
        }

        var migration29 = Assert.Single(
            DatabaseSchema.Migrations,
            item => item.Version == 29);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration29.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT
                (SELECT active_version FROM data_update_state WHERE singleton = 1),
                EXISTS (
                    SELECT 1 FROM sqlite_schema
                    WHERE type = 'table' AND name = 'data_update_transfer_runs'),
                EXISTS (
                    SELECT 1 FROM sqlite_schema
                    WHERE type = 'table' AND name = 'data_update_downloads');
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("2026.07.29.1", reader.GetString(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
    }

    [Fact]
    public async Task DownloadAuditMigrationPreservesExistingJobAndCreatesInitialEvent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 23))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        const string now = "2026-07-29T10:00:00.0000000+00:00";
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'mikan', 'Mikan', 'mikan', 'bt', 'move',
                    1, 1, 1, 1, $now, $now);

                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    title, torrent_url_fingerprint, downloader_id,
                    route_snapshot_json, status, created_at_utc, updated_at_utc)
                VALUES (
                    'task-1', 'mikan', 1, 'mikan', 'Episode',
                    'fingerprint', 'bt', '{}', 'downloading', $now, $now);

                INSERT INTO download_jobs (
                    id, task_id, downloader_id, info_hash, state, progress,
                    downloaded_bytes, total_bytes, speed_bytes_per_second,
                    eta_seconds, created_at_utc, updated_at_utc)
                VALUES (
                    'job-1', 'task-1', 'bt',
                    'dddddddddddddddddddddddddddddddddddddddd',
                    'downloading', 0.5, 50, 100, 10, 5, $now, $now);
                """;
            seed.Parameters.AddWithValue("$now", now);
            Assert.Equal(3, await seed.ExecuteNonQueryAsync());
        }

        var migration24 = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 24);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration24.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT event.kind, event.result, event.from_state,
                   event.to_state, event.created_at_utc, job.state
            FROM download_job_events AS event
            JOIN download_jobs AS job ON job.id = event.job_id
            WHERE event.job_id = 'job-1';
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("projection_initialized", reader.GetString(0));
        Assert.Equal("observed", reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
        Assert.Equal("downloading", reader.GetString(3));
        Assert.Equal(now, reader.GetString(4));
        Assert.Equal("downloading", reader.GetString(5));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task RssRuleSnapshotMigrationPreservesCurrentRevisionAndOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 24))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        const string now = "2026-07-29T11:00:00.0000000+00:00";
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'mikan', 'Mikan', 'mikan', 'bt', 'move',
                    1, 1, 1, 1, $now, $now);
                INSERT INTO mikan_rss_rule_sets (
                    source_profile_id, revision, created_at_utc, updated_at_utc)
                VALUES ('mikan', 7, $now, $now);
                INSERT INTO mikan_rss_priority_groups (
                    source_profile_id, id, name, position)
                VALUES ('mikan', 'language', 'Language', 0);
                INSERT INTO mikan_rss_match_arrays (
                    source_profile_id, id, scope, group_id, name, enabled, position)
                VALUES ('mikan', 'chs', 'priority', 'language', 'CHS', 1, 0);
                INSERT INTO mikan_rss_match_values (
                    source_profile_id, array_id, position, value_lower)
                VALUES ('mikan', 'chs', 0, 'chs');
                """;
            seed.Parameters.AddWithValue("$now", now);
            Assert.Equal(5, await seed.ExecuteNonQueryAsync());
        }

        var migration25 = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 25);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration25.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT snapshots.revision, groups.id, arrays.id, rule_values.value_lower
            FROM mikan_rss_rule_snapshots AS snapshots
            JOIN mikan_rss_snapshot_priority_groups AS groups
              ON groups.source_profile_id = snapshots.source_profile_id
             AND groups.revision = snapshots.revision
            JOIN mikan_rss_snapshot_match_arrays AS arrays
              ON arrays.source_profile_id = snapshots.source_profile_id
             AND arrays.revision = snapshots.revision
             AND arrays.group_id = groups.id
            JOIN mikan_rss_snapshot_match_values AS rule_values
              ON rule_values.source_profile_id = arrays.source_profile_id
             AND rule_values.revision = arrays.revision
             AND rule_values.array_id = arrays.id;
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(7, reader.GetInt64(0));
        Assert.Equal("language", reader.GetString(1));
        Assert.Equal("chs", reader.GetString(2));
        Assert.Equal("chs", reader.GetString(3));
    }

    [Fact]
    public async Task BangumiDiscoveryMigrationPreservesExistingBatchAsNotAttempted()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 25))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        const string now = "2026-07-29T11:30:00.0000000+00:00";
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'mikan', 'Mikan', 'mikan', 'bt', 'move',
                    1, 1, 1, 1, $now, $now);
                INSERT INTO mikan_rss_batches (
                    id, source_profile_id, rule_revision, fingerprint, mikanid,
                    priority_enabled, entry_count, created_at_utc,
                    legacy_filter_revision, legacy_filter_enabled)
                VALUES (
                    'batch', 'mikan', 1,
                    'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                    3951, 1, 0, $now, 1, 0);
                """;
            seed.Parameters.AddWithValue("$now", now);
            Assert.Equal(2, await seed.ExecuteNonQueryAsync());
        }

        var migration26 = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 26);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration26.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT mikanid, bangumi_subject_id, bangumi_discovery_state,
                   bangumi_discovery_failure_code
            FROM mikan_rss_batches WHERE id = 'batch';
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(3951, reader.GetInt32(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal(MikanBangumiDiscoveryStates.NotAttempted, reader.GetString(2));
        Assert.True(reader.IsDBNull(3));
    }

    [Fact]
    public async Task DownloadJobsIncludeImmutablePathSnapshots()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(download_jobs);";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("download_root_path", columns);
        Assert.Contains("save_root_path", columns);
    }

    [Fact]
    public async Task IngestTasksIncludeMikanPublicationEvidence()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        var columns = await ColumnsAsync(connection, "ingest_tasks");

        Assert.Contains("source_published_at_raw", columns);
        Assert.Contains("source_published_at", columns);
    }

    [Fact]
    public async Task PublicationEvidenceMigrationPreservesSchema18IngestTasks()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 18))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc, allowed_torrent_hosts_json)
                VALUES (
                    'mikan', 'Mikan', 'mikan', 'bt', 'move',
                    1, 1, 1, 1, $now, $now, '["mikanani.me"]');

                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    source_item_id, source_work_id, mikanid, groupid,
                    bangumi_subject_id, anidb_id, imdb_id, title,
                    torrent_url_fingerprint, downloader_id, route_snapshot_json,
                    status, failure_kind, failure_reason, created_at_utc, updated_at_utc)
                VALUES (
                    'legacy-task', 'mikan', 1, 'mikan',
                    'legacy-item', '3951', 3951, NULL,
                    547888, NULL, NULL, 'Legacy episode',
                    'legacy-fingerprint', 'bt', '{}',
                    'received', NULL, NULL, $now, $now);
                """;
            seed.Parameters.AddWithValue("$now", "2026-07-26T10:00:00.0000000+00:00");
            await seed.ExecuteNonQueryAsync();
        }

        var migration19 = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 19);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration19.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT title, source_published_at_raw, source_published_at
            FROM ingest_tasks WHERE id = 'legacy-task';
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Legacy episode", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
    }

    [Fact]
    public async Task PendingTmdbRecoveryMigrationPreservesPendingFallbackRecords()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 19))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, bangumi_subject_id, canonical_name,
                    needs_tmdb_completion, created_at_utc, updated_at_utc)
                VALUES ('fallback-series', 0, 547888, 'Fallback', 1, $now, $now);

                INSERT INTO fallback_completion_records (
                    id, anime_series_id, bangumi_subject_id, scope_kind, scope_key,
                    source_id, source_episode, media_path, completed_at_utc)
                VALUES (
                    'fallback-1', 'fallback-series', 547888, 'mikan_episode', 'scope-1',
                    'mikan', '7', '/media/episode.mkv', $now);
                """;
            seed.Parameters.AddWithValue("$now", "2026-07-28T10:00:00.0000000+00:00");
            await seed.ExecuteNonQueryAsync();
        }

        var migration20 = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 20);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration20.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT resolution_state, resolved_completion_id, resolved_at_utc, resolution_source
            FROM fallback_completion_records WHERE id = 'fallback-1';
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("pending", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
        Assert.True(reader.IsDBNull(3));
    }

    [Fact]
    public async Task LibraryProjectionMigrationPreservesRowsAndAddsTmdbSeasonMetadata()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 22))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }

        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, canonical_name, needs_tmdb_completion,
                    created_at_utc, updated_at_utc)
                VALUES ('series-1', 72517, '来自深渊', 0, $now, $now);

                INSERT INTO anime_seasons (
                    id, series_id, season_number, canonical_name,
                    created_at_utc, updated_at_utc)
                VALUES ('season-1', 'series-1', 2, '烈日的黄金乡', $now, $now);
                """;
            seed.Parameters.AddWithValue("$now", "2026-07-29T10:00:00.0000000+00:00");
            await seed.ExecuteNonQueryAsync();
        }

        var migration23 = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 23);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration23.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT series.canonical_name, series.first_air_date,
                   season.canonical_name, season.air_date, season.episode_count
            FROM anime_series AS series
            JOIN anime_seasons AS season ON season.series_id = series.id
            WHERE series.id = 'series-1' AND season.id = 'season-1';
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("来自深渊", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal("烈日的黄金乡", reader.GetString(2));
        Assert.True(reader.IsDBNull(3));
        Assert.Equal(0, reader.GetInt32(4));
    }

    [Fact]
    public async Task RssBatchAuditIncludesVersionedLegacyFilterColumns()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        var batchColumns = await ColumnsAsync(connection, "mikan_rss_batches");
        var entryColumns = await ColumnsAsync(connection, "mikan_rss_batch_entries");

        Assert.Contains("legacy_filter_revision", batchColumns);
        Assert.Contains("legacy_filter_enabled", batchColumns);
        Assert.Contains("legacy_filter_state", entryColumns);
        Assert.Contains("legacy_filter_reason", entryColumns);
        Assert.Contains("legacy_filter_scope", entryColumns);
        Assert.Contains("legacy_filter_key", entryColumns);
        Assert.Contains("identity_mikanid", entryColumns);
        Assert.Contains("identity_groupid", entryColumns);
    }

    [Fact]
    public async Task SourceDownloadPolicyMigrationPreservesProfilesWithSafeDefaults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 16))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc, allowed_torrent_hosts_json)
                VALUES ('mikan', 'Mikan', 'mikan', 'bt', 'move', 1, 1, 3, 1, $now, $now, '["mikanani.me"]');
                """;
            seed.Parameters.AddWithValue("$now", "2026-07-26T10:00:00.0000000+00:00");
            Assert.Equal(1, await seed.ExecuteNonQueryAsync());
        }

        var migration17 = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 17);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration17.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT category, tags_json, seeding_time_minutes, revision
            FROM source_profiles WHERE id = 'mikan';
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("animegonet", reader.GetString(0));
        Assert.Equal("[]", reader.GetString(1));
        Assert.Equal(0, reader.GetInt32(2));
        Assert.Equal(3, reader.GetInt64(3));
    }

    [Fact]
    public async Task MikanIdentityCookieMigrationPreservesExistingProfiles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 30))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc, allowed_torrent_hosts_json,
                    category, tags_json, seeding_time_minutes)
                VALUES (
                    'mikan', 'Mikan', 'mikan', 'bt', 'move',
                    1, 1, 9, 1, $now, $now, '["mikanani.me"]',
                    'animegonet', '[]', 0);
                """;
            seed.Parameters.AddWithValue(
                "$now",
                "2026-07-30T10:00:00.0000000+00:00");
            Assert.Equal(1, await seed.ExecuteNonQueryAsync());
        }

        var migration31 = Assert.Single(
            DatabaseSchema.Migrations,
            item => item.Version == 31);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration31.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT revision, mikan_identity_cookie
            FROM source_profiles
            WHERE id = 'mikan';
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(9, reader.GetInt64(0));
        Assert.True(reader.IsDBNull(1));
    }

    [Fact]
    public async Task FileStrategyMigrationBackfillsExistingNonMoveDownloadJobs()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var profiles = new SourceProfileStore(fixture.Database);
        await profiles.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        _ = await profiles.UpdateAsync(
            "mikan",
            new SourceProfileDefinition(
                "Mikan", "mikan", "bt", "link",
                ["mikanani.me"], "animegonet", [], 30, true, true, true),
            1,
            DateTimeOffset.UtcNow);
        var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
        var normalized = Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
            "mikan",
            new IngestItemCommand(
                "https://mikanani.me/passkey/legacy-link.torrent",
                new IngestItemInfo(
                    "Legacy link", null, "legacy", "3951", null, null, 3951, 547888, null, null))).Item);
        var tasks = new IngestTaskStore(fixture.Database);
        var task = await tasks.AddStagedAsync(
            normalized,
            profile,
            new TorrentMetadata(
                "episode.mkv", new string('a', 40), 5,
                [new TorrentFile("episode.mkv", 5, false)]),
            "legacy-link.torrent",
            DateTimeOffset.UtcNow.AddMinutes(15));
        var claim = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        await tasks.CompleteDispatchAsync(
            claim,
            new DownloadTaskSnapshot(
                new string('a', 40), "Legacy link", DownloadTaskState.Seeding,
                1, 5, 5, 0, null),
            "/download/incomplete/bt",
            "/download/anime",
            DateTimeOffset.UtcNow);

        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using (var legacy = connection.CreateCommand())
        {
            legacy.CommandText = """
                UPDATE download_jobs SET organization_state = 'not_required'
                WHERE task_id = $task_id;
                """;
            legacy.Parameters.AddWithValue("$task_id", task.Id);
            Assert.Equal(1, await legacy.ExecuteNonQueryAsync());
        }

        var migration = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 18);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = migration.Sql;
            await migrate.ExecuteNonQueryAsync();
        }

        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT organization_state FROM download_jobs WHERE task_id = $task_id;";
        query.Parameters.AddWithValue("$task_id", task.Id);
        Assert.Equal("pending", await query.ExecuteScalarAsync());
    }

    [Fact]
    public async Task LegacyFilterAuditMigrationPreservesSchema15BatchDataAndForeignKeys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
            await foreignKeys.ExecuteNonQueryAsync();
        }
        foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 15))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc, allowed_torrent_hosts_json)
                VALUES ('mikan', 'Mikan', 'mikan', 'bt', 'move', 1, 1, 1, 1, $now, $now, '["mikanani.me"]');
                INSERT INTO mikan_rss_batches (
                    id, source_profile_id, rule_revision, fingerprint, mikanid,
                    priority_enabled, entry_count, created_at_utc)
                VALUES ('batch', 'mikan', 1, $fingerprint, 3951, 1, 1, $now);
                INSERT INTO mikan_rss_batch_entries (
                    batch_id, candidate_id, ordinal, title, mikan_url, torrent_url_fingerprint,
                    content_type, length_bytes, source_episode_kind, source_episode,
                    decision_kind, decision_reason, winner_candidate_id, effect_state)
                VALUES ('batch', 'candidate', 0, 'Show [03]', 'https://mikanani.me/Home/Episode/a',
                    $torrent, 'application/x-bittorrent', 42, 'normal', '3',
                    'Winner', 'SingleCandidateBypass', 'candidate', 'ready');
                INSERT INTO mikan_rss_decision_groups (batch_id, candidate_id, position, group_id)
                VALUES ('batch', 'candidate', 0, 'language');
                """;
            seed.Parameters.AddWithValue("$now", "2026-07-22T10:00:00.0000000+00:00");
            seed.Parameters.AddWithValue("$fingerprint", new string('a', 64));
            seed.Parameters.AddWithValue("$torrent", new string('b', 64));
            Assert.Equal(4, await seed.ExecuteNonQueryAsync());
        }

        var migration16 = Assert.Single(DatabaseSchema.Migrations, item => item.Version == 16);
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using var migrate = connection.CreateCommand();
            migrate.Transaction = (SqliteTransaction)transaction;
            migrate.CommandText = migration16.Sql;
            await migrate.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT b.legacy_filter_revision, b.legacy_filter_enabled,
                       e.decision_kind, e.legacy_filter_state, e.legacy_filter_reason,
                       g.group_id
                FROM mikan_rss_batches b
                JOIN mikan_rss_batch_entries e ON e.batch_id = b.id
                JOIN mikan_rss_decision_groups g
                  ON g.batch_id = e.batch_id AND g.candidate_id = e.candidate_id;
                """;
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt64(0));
            Assert.False(reader.GetBoolean(1));
            Assert.Equal("Winner", reader.GetString(2));
            Assert.Equal("NotEvaluated", reader.GetString(3));
            Assert.Equal("LegacyFilterNotRecorded", reader.GetString(4));
            Assert.Equal("language", reader.GetString(5));
        }
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA foreign_key_check;";
            Assert.Null(await check.ExecuteScalarAsync());
            check.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", await check.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task MetadataAttemptStageMigrationAcceptsBangumiAndPreservesConstraints()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO source_profiles (
                id, display_name, adapter, downloader_id, file_strategy,
                rss_filter_enabled, rss_priority_enabled, revision, enabled,
                created_at_utc, updated_at_utc, allowed_torrent_hosts_json)
            VALUES ('test', 'Test', 'test', 'bt', 'move', 0, 0, 1, 1, $now, $now, '[]');
            INSERT INTO ingest_tasks (
                id, source_profile_id, source_profile_revision, source_id, title,
                torrent_url_fingerprint, downloader_id, route_snapshot_json,
                status, created_at_utc, updated_at_utc)
            VALUES ('task', 'test', 1, 'test', 'Test', 'fingerprint', 'bt', '{}',
                    'metadata_resolving', $now, $now);
            INSERT INTO metadata_resolution_runs (
                id, task_id, status, tmdb_access_confirmed, fallback_eligible,
                started_at_utc, attempt_number)
            VALUES ('run', 'task', 'running', 0, 0, $now, 1);
            INSERT INTO metadata_resolution_attempts (
                id, run_id, stage, strategy, result, retryable,
                attempt_number, duration_ms, created_at_utc)
            VALUES ('attempt', 'run', 'bangumi', 'bangumi_subject', 'matched', 0, 1, 0, $now);
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));

        Assert.Equal(4, await command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task EveryOpenedConnectionEnforcesForeignKeys()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";

        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task NewDatabasePassesIntegrityCheck()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";

        Assert.Equal("ok", await command.ExecuteScalarAsync());
    }

    private static async Task<HashSet<string>> ColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        return columns;
    }

    private static async Task<long> ScalarInt64Async(
        SqliteConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return (long)(await command.ExecuteScalarAsync() ?? -1L);
    }
}
