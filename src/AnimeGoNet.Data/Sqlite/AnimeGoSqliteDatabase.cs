using Microsoft.Data.Sqlite;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;

namespace AnimeGoNet.Data.Sqlite;

public sealed class AnimeGoSqliteDatabase
{
    private readonly string _connectionString;
    private readonly string _databaseFile;
    private readonly IReadOnlyList<PortablePathRoot>? _pathRoots;

    public bool PortablePathsEnabled => _pathRoots is not null;

    public long UnresolvedPathCount { get; private set; }

    public string? PathBindingRevision { get; }

    public AnimeGoSqliteDatabase(string databaseFile, AnimeGoOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        _databaseFile = databaseFile;
        _pathRoots = options is null ? null : PortablePathStorage.Roots(options);
        PathBindingRevision = _pathRoots is null ? null : Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                string.Join('\n', _pathRoots.OrderBy(root => root.Kind, StringComparer.Ordinal)
                    .ThenBy(root => root.Id, StringComparer.Ordinal)
                    .Select(root => root.Kind + '\0' + root.Id + '\0' + root.Path)))));
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        };
        _connectionString = builder.ToString();
    }

    public async ValueTask<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            PortablePathStorage.Register(connection, _pathRoots);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (var journalCommand = connection.CreateCommand())
        {
            journalCommand.CommandText = "PRAGMA journal_mode = WAL;";
            await journalCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }

        await BackupBeforePathUpgradeAsync(connection, cancellationToken).ConfigureAwait(false);
        await SchemaMigrationRunner.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        if (_pathRoots is not null)
        {
            await PortablePathStorage.UpgradeRecordsAsync(connection, _pathRoots, cancellationToken).ConfigureAwait(false);
            await using var unresolved = connection.CreateCommand();
            unresolved.CommandText = "SELECT COUNT(*) FROM stored_path_locations WHERE root_kind IS NULL;";
            UnresolvedPathCount = Convert.ToInt64(await unresolved.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private async Task BackupBeforePathUpgradeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (_databaseFile == ":memory:") return;
        await using var probe = connection.CreateCommand();
        probe.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'completion_records';";
        if (Convert.ToInt64(await probe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0) return;
        probe.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'stored_path_locations';";
        if (Convert.ToInt64(await probe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 0) return;
        var backupFile = _databaseFile + ".pre-portable-paths-" + Guid.NewGuid().ToString("N") + ".db";
        await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupFile, Pooling = false }.ToString());
        await backup.OpenAsync(cancellationToken).ConfigureAwait(false);
        connection.BackupDatabase(backup);
    }
}
