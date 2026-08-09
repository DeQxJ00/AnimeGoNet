using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Tests.Sqlite;

public sealed class SchemaReliabilityTests
{
    [Fact]
    public async Task ConcurrentFirstStartSerializesAndRecordsEachMigrationOnce()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "concurrent.db");
            var databases = Enumerable.Range(0, 8)
                .Select(_ => new AnimeGoSqliteDatabase(path))
                .ToArray();

            await Task.WhenAll(databases.Select(database => database.InitializeAsync()));

            await using var connection = await databases[0].OpenConnectionAsync();
            await using var migrations = connection.CreateCommand();
            migrations.CommandText = "SELECT COUNT(*), MAX(version) FROM schema_migrations;";
            await using var reader = await migrations.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(DatabaseSchema.CurrentVersion, reader.GetInt32(0));
            Assert.Equal(DatabaseSchema.CurrentVersion, reader.GetInt32(1));
            Assert.False(await reader.ReadAsync());
            await reader.DisposeAsync();

            await using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", await integrity.ExecuteScalarAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FailedMigrationRollsBackItsDdlAndCanResumeAfterRepair()
    {
        var root = CreateRoot();
        try
        {
            var database = new AnimeGoSqliteDatabase(Path.Combine(root, "fault.db"));
            await using var connection = await database.OpenConnectionAsync();
            SchemaMigration[] faulted =
            [
                new(1, "create_first", "CREATE TABLE first_value (id INTEGER PRIMARY KEY) STRICT;"),
                new(
                    2,
                    "create_second",
                    "CREATE TABLE second_value (id INTEGER PRIMARY KEY) STRICT; SELECT * FROM missing_table;"),
            ];

            await Assert.ThrowsAsync<SqliteException>(() =>
                SchemaMigrationRunner.ApplyAsync(connection, faulted, CancellationToken.None));

            Assert.Equal(1L, await ScalarInt64Async(
                connection,
                "SELECT COUNT(*) FROM schema_migrations;"));
            Assert.Equal(0L, await ScalarInt64Async(
                connection,
                "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'second_value';"));

            SchemaMigration[] repaired =
            [
                faulted[0],
                new(2, "create_second", "CREATE TABLE second_value (id INTEGER PRIMARY KEY) STRICT;"),
            ];
            await SchemaMigrationRunner.ApplyAsync(connection, repaired, CancellationToken.None);

            Assert.Equal(2L, await ScalarInt64Async(
                connection,
                "SELECT COUNT(*) FROM schema_migrations;"));
            Assert.Equal(1L, await ScalarInt64Async(
                connection,
                "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'second_value';"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(
        "UPDATE schema_migrations SET name = 'tampered' WHERE version = 39;",
        SchemaMigrationException.HistoryInvalidCode)]
    [InlineData(
        "DELETE FROM schema_migrations WHERE version = 20;",
        SchemaMigrationException.HistoryInvalidCode)]
    [InlineData(
        "INSERT INTO schema_migrations(version, name, applied_at_utc) VALUES (41, 'future', '2026-08-08T00:00:00Z');",
        SchemaMigrationException.DatabaseNewerCode)]
    public async Task InvalidOrNewerMigrationHistoryFailsClosed(
        string mutation,
        string expectedCode)
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using (var connection = await fixture.Database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = mutation;
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<SchemaMigrationException>(
            () => fixture.Database.InitializeAsync());

        Assert.Equal(expectedCode, exception.Code);
        Assert.DoesNotContain("tampered", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("future", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<long> ScalarInt64Async(
        SqliteConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-schema-reliability",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
