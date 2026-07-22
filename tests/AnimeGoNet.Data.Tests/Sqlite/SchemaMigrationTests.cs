using AnimeGoNet.Data.Sqlite;

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
            "completion_aliases",
            "completion_records",
            "delete_executions",
            "delete_execution_items",
            "download_jobs",
            "downloader_runtime_state",
            "episode_claims",
            "fallback_claims",
            "fallback_completion_records",
            "file_operations",
            "ingest_tasks",
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
            "mikan_trusted_offsets",
            "mikan_work_rules",
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
}
