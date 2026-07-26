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
}
