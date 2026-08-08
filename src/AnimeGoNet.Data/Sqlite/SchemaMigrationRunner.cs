using System.Globalization;
using AnimeGoNet.Core.Diagnostics;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Sqlite;

internal static class SchemaMigrationRunner
{
    public static async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken)
        => await ApplyAsync(connection, DatabaseSchema.Migrations, cancellationToken)
            .ConfigureAwait(false);

    internal static async Task ApplyAsync(
        SqliteConnection connection,
        IReadOnlyList<SchemaMigration> migrations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(migrations);
        ValidateDefinitions(migrations);
        await EnsureMigrationTableAsync(connection, cancellationToken).ConfigureAwait(false);
        await ValidateAppliedHistoryAsync(connection, migrations, cancellationToken).ConfigureAwait(false);

        foreach (var migration in migrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = connection.BeginTransaction(deferred: false);
            var appliedName = await ReadAppliedNameAsync(
                connection,
                transaction,
                migration.Version,
                cancellationToken).ConfigureAwait(false);
            if (appliedName is not null)
            {
                if (!string.Equals(appliedName, migration.Name, StringComparison.Ordinal))
                {
                    throw SchemaMigrationException.HistoryInvalid();
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

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

        await ValidateAppliedHistoryAsync(connection, migrations, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateDefinitions(IReadOnlyList<SchemaMigration> migrations)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < migrations.Count; index++)
        {
            var migration = migrations[index];
            if (migration.Version != index + 1
                || string.IsNullOrWhiteSpace(migration.Name)
                || string.IsNullOrWhiteSpace(migration.Sql)
                || !names.Add(migration.Name))
            {
                throw new InvalidOperationException("Schema migration definitions must be contiguous and non-empty.");
            }
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

    private static async Task ValidateAppliedHistoryAsync(
        SqliteConnection connection,
        IReadOnlyList<SchemaMigration> migrations,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version, name FROM schema_migrations ORDER BY version;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var expectedVersion = 1;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var version = reader.GetInt32(0);
            if (version > migrations.Count)
            {
                throw SchemaMigrationException.DatabaseNewerThanApplication();
            }

            if (version != expectedVersion
                || !string.Equals(
                    reader.GetString(1),
                    migrations[version - 1].Name,
                    StringComparison.Ordinal))
            {
                throw SchemaMigrationException.HistoryInvalid();
            }

            expectedVersion++;
        }
    }

    private static async Task<string?> ReadAppliedNameAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name FROM schema_migrations WHERE version = $version;";
        command.Parameters.AddWithValue("$version", version);
        return (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class SchemaMigrationException : Exception, IStableError
{
    public const string HistoryInvalidCode = "schema_migration_history_invalid";
    public const string DatabaseNewerCode = "schema_database_newer_than_application";

    private SchemaMigrationException(string code, string message)
        : base(message)
    {
        Code = StableErrorCode.Require(code, nameof(code));
    }

    public string Code { get; }

    public StableErrorSemantic Semantics => StableErrorSemantic.None;

    internal static SchemaMigrationException HistoryInvalid() =>
        new(HistoryInvalidCode, "SQLite migration history is not a valid prefix of this application schema.");

    internal static SchemaMigrationException DatabaseNewerThanApplication() =>
        new(DatabaseNewerCode, "SQLite schema is newer than this application version.");
}
