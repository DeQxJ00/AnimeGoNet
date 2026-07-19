using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Sqlite;

internal static class SchemaMigrationRunner
{
    public static async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await EnsureMigrationTableAsync(connection, cancellationToken).ConfigureAwait(false);
        var applied = await ReadAppliedVersionsAsync(connection, cancellationToken).ConfigureAwait(false);

        foreach (var migration in DatabaseSchema.Migrations)
        {
            if (applied.Contains(migration.Version))
            {
                continue;
            }

            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = migration.Sql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var recordCommand = connection.CreateCommand())
            {
                recordCommand.Transaction = transaction;
                recordCommand.CommandText = """
                    INSERT INTO schema_migrations(version, name, applied_at_utc)
                    VALUES ($version, $name, $appliedAtUtc);
                    """;
                recordCommand.Parameters.AddWithValue("$version", migration.Version);
                recordCommand.Parameters.AddWithValue("$name", migration.Name);
                recordCommand.Parameters.AddWithValue(
                    "$appliedAtUtc",
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                await recordCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureMigrationTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER NOT NULL PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                applied_at_utc TEXT NOT NULL
            ) STRICT;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HashSet<int>> ReadAppliedVersionsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var versions = new HashSet<int>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_migrations ORDER BY version;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }
}
