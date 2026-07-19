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
            "download_jobs",
            "downloader_runtime_state",
            "episode_claims",
            "fallback_claims",
            "fallback_completion_records",
            "file_operations",
            "ingest_tasks",
            "metadata_resolution_attempts",
            "metadata_resolution_runs",
            "mikan_offset_evidence",
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
        Assert.Equal(5, reader.GetInt32(0));
        Assert.Equal(DatabaseSchema.CurrentVersion, reader.GetInt32(1));
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
