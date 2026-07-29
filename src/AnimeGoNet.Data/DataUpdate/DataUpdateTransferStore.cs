using System.Globalization;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.DataUpdate;

public sealed class DataUpdateTransferStore(AnimeGoSqliteDatabase database)
{
    public async Task<string> StartAsync(
        string triggerKind,
        string requestedAction,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ValidateTrigger(triggerKind);
        ValidateAction(requestedAction);
        var runId = Guid.NewGuid().ToString("N");
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO data_update_transfer_runs (
                id, trigger_kind, requested_action, status,
                data_version, manifest_sha256, failure_code,
                downloaded_bytes, total_bytes, started_at_utc, completed_at_utc)
            VALUES (
                $id, $trigger, $action, 'checking',
                NULL, NULL, NULL, 0, 0, $now, NULL);
            """;
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$trigger", triggerKind);
        command.Parameters.AddWithValue("$action", requestedAction);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return runId;
    }

    public async Task SetStageAsync(
        string runId,
        string status,
        string dataVersion,
        string manifestSha256,
        long downloadedBytes,
        long totalBytes,
        CancellationToken cancellationToken = default)
    {
        if (status is not (
            DataUpdateTransferStatuses.Downloading
            or DataUpdateTransferStatuses.Importing))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        ValidateProgress(downloadedBytes, totalBytes);
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE data_update_transfer_runs
            SET status = $status,
                data_version = $version,
                manifest_sha256 = $manifest,
                downloaded_bytes = $downloaded,
                total_bytes = $total
            WHERE id = $id
              AND status IN ('checking', 'downloading', 'downloaded');
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$version", dataVersion);
        command.Parameters.AddWithValue("$manifest", manifestSha256);
        command.Parameters.AddWithValue("$downloaded", downloadedBytes);
        command.Parameters.AddWithValue("$total", totalBytes);
        command.Parameters.AddWithValue("$id", runId);
        await RequireOneAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetProgressAsync(
        string runId,
        long downloadedBytes,
        long totalBytes,
        CancellationToken cancellationToken = default)
    {
        ValidateProgress(downloadedBytes, totalBytes);
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE data_update_transfer_runs
            SET downloaded_bytes = $downloaded,
                total_bytes = $total
            WHERE id = $id AND status = 'downloading';
            """;
        command.Parameters.AddWithValue("$downloaded", downloadedBytes);
        command.Parameters.AddWithValue("$total", totalBytes);
        command.Parameters.AddWithValue("$id", runId);
        await RequireOneAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteAsync(
        string runId,
        string status,
        string? dataVersion,
        string? manifestSha256,
        long downloadedBytes,
        long totalBytes,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        if (status is not (
            DataUpdateTransferStatuses.UpdateAvailable
            or DataUpdateTransferStatuses.UpToDate
            or DataUpdateTransferStatuses.Downloaded
            or DataUpdateTransferStatuses.Completed))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        ValidateProgress(downloadedBytes, totalBytes);
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE data_update_transfer_runs
            SET status = $status,
                data_version = $version,
                manifest_sha256 = $manifest,
                downloaded_bytes = $downloaded,
                total_bytes = $total,
                completed_at_utc = $now
            WHERE id = $id
              AND status IN ('checking', 'downloading', 'importing');
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$version", (object?)dataVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$manifest", (object?)manifestSha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$downloaded", downloadedBytes);
        command.Parameters.AddWithValue("$total", totalBytes);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        command.Parameters.AddWithValue("$id", runId);
        await RequireOneAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task FailAsync(
        string runId,
        string failureCode,
        long downloadedBytes,
        long totalBytes,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ValidateProgress(downloadedBytes, totalBytes);
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE data_update_transfer_runs
            SET status = 'failed',
                failure_code = $failure,
                downloaded_bytes = $downloaded,
                total_bytes = $total,
                completed_at_utc = $now
            WHERE id = $id
              AND status IN ('checking', 'downloading', 'importing');
            """;
        command.Parameters.AddWithValue("$failure", failureCode);
        command.Parameters.AddWithValue("$downloaded", downloadedBytes);
        command.Parameters.AddWithValue("$total", totalBytes);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        command.Parameters.AddWithValue("$id", runId);
        await RequireOneAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveDownloadAsync(
        DownloadedDataPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO data_update_downloads (
                data_version, manifest_sha256, relative_directory, state,
                downloaded_at_utc, imported_at_utc)
            VALUES (
                $version, $manifest, $directory, $state, $downloaded, $imported)
            ON CONFLICT(data_version) DO UPDATE SET
                manifest_sha256 = excluded.manifest_sha256,
                relative_directory = excluded.relative_directory,
                state = excluded.state,
                downloaded_at_utc = excluded.downloaded_at_utc,
                imported_at_utc = excluded.imported_at_utc;
            """;
        command.Parameters.AddWithValue("$version", package.DataVersion);
        command.Parameters.AddWithValue("$manifest", package.ManifestSha256);
        command.Parameters.AddWithValue("$directory", package.RelativeDirectory);
        command.Parameters.AddWithValue("$state", package.State);
        command.Parameters.AddWithValue("$downloaded", Format(package.DownloadedAtUtc));
        command.Parameters.AddWithValue(
            "$imported",
            package.ImportedAtUtc is null
                ? DBNull.Value
                : Format(package.ImportedAtUtc.Value));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkImportedAsync(
        string dataVersion,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE data_update_downloads
            SET state = 'imported', imported_at_utc = $now
            WHERE data_version = $version;
            """;
        command.Parameters.AddWithValue("$now", Format(utcNow));
        command.Parameters.AddWithValue("$version", dataVersion);
        await RequireOneAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DownloadedDataPackage?> GetDownloadAsync(
        string dataVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT data_version, manifest_sha256, relative_directory, state,
                   downloaded_at_utc, imported_at_utc
            FROM data_update_downloads
            WHERE data_version = $version;
            """;
        command.Parameters.AddWithValue("$version", dataVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadDownload(reader)
            : null;
    }

    public async Task<IReadOnlyList<DownloadedDataPackage>> ListDownloadsAsync(
        CancellationToken cancellationToken = default)
    {
        var packages = new List<DownloadedDataPackage>();
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT data_version, manifest_sha256, relative_directory, state,
                   downloaded_at_utc, imported_at_utc
            FROM data_update_downloads
            ORDER BY downloaded_at_utc DESC, data_version DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            packages.Add(ReadDownload(reader));
        }
        return packages;
    }

    public async Task<DataUpdateTransferRun?> GetLastRunAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, trigger_kind, requested_action, status,
                   data_version, manifest_sha256, failure_code,
                   downloaded_bytes, total_bytes, started_at_utc, completed_at_utc
            FROM data_update_transfer_runs
            ORDER BY sequence DESC
            LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new DataUpdateTransferRun(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                Parse(reader.GetString(9)),
                reader.IsDBNull(10) ? null : Parse(reader.GetString(10)))
            : null;
    }

    private static DownloadedDataPackage ReadDownload(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : Parse(reader.GetString(5)));

    private static async Task RequireOneAsync(
        Microsoft.Data.Sqlite.SqliteCommand command,
        CancellationToken cancellationToken)
    {
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Data update transfer run changed concurrently.");
        }
    }

    private static void ValidateTrigger(string value)
    {
        if (value is not (DataUpdateTriggerKinds.Manual or DataUpdateTriggerKinds.Scheduled))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static void ValidateAction(string value)
    {
        if (value is not (
            DataUpdateActions.Check
            or DataUpdateActions.Download
            or DataUpdateActions.DownloadImport))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static void ValidateProgress(long downloadedBytes, long totalBytes)
    {
        if (downloadedBytes < 0 || totalBytes < 0 || downloadedBytes > totalBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(downloadedBytes));
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
