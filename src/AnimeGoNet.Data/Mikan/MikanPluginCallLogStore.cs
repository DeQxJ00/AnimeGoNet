using System.Globalization;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Mikan;

public sealed record MikanPluginCallLogItem(
    int Index,
    string? TaskId,
    int? MikanId,
    int? GroupId,
    string Status,
    string? FailureCode);

public sealed record MikanPluginCallLog(
    string Id,
    string Endpoint,
    string Mode,
    string MediaType,
    string Result,
    int RequestedCount,
    int AcceptedCount,
    int RejectedCount,
    string? FailureCode,
    long DurationMilliseconds,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<MikanPluginCallLogItem> Items);

public sealed record MikanPluginCallLogPage(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<MikanPluginCallLog> Items);

public sealed class MikanPluginCallLogStore(AnimeGoSqliteDatabase database)
{
    public async Task RecordAsync(
        MikanPluginCallLog entry,
        CancellationToken cancellationToken = default)
    {
        Validate(entry);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO mikan_plugin_call_logs (
                    id, endpoint, mode, media_type, result, requested_count,
                    accepted_count, rejected_count, failure_code, duration_ms,
                    started_at_utc, completed_at_utc)
                VALUES (
                    $id, $endpoint, $mode, $media_type, $result, $requested,
                    $accepted, $rejected, $failure, $duration, $started, $completed);
                """;
            command.Parameters.AddWithValue("$id", entry.Id);
            command.Parameters.AddWithValue("$endpoint", entry.Endpoint);
            command.Parameters.AddWithValue("$mode", entry.Mode);
            command.Parameters.AddWithValue("$media_type", entry.MediaType);
            command.Parameters.AddWithValue("$result", entry.Result);
            command.Parameters.AddWithValue("$requested", entry.RequestedCount);
            command.Parameters.AddWithValue("$accepted", entry.AcceptedCount);
            command.Parameters.AddWithValue("$rejected", entry.RejectedCount);
            command.Parameters.AddWithValue("$failure", (object?)entry.FailureCode ?? DBNull.Value);
            command.Parameters.AddWithValue("$duration", entry.DurationMilliseconds);
            command.Parameters.AddWithValue("$started", Format(entry.StartedAtUtc));
            command.Parameters.AddWithValue("$completed", Format(entry.CompletedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var item in entry.Items)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO mikan_plugin_call_log_items (
                    call_id, item_index, task_id, mikanid, groupid, status, failure_code)
                VALUES ($call_id, $index, $task_id, $mikanid, $groupid, $status, $failure);
                """;
            command.Parameters.AddWithValue("$call_id", entry.Id);
            command.Parameters.AddWithValue("$index", item.Index);
            command.Parameters.AddWithValue("$task_id", (object?)item.TaskId ?? DBNull.Value);
            command.Parameters.AddWithValue("$mikanid", (object?)item.MikanId ?? DBNull.Value);
            command.Parameters.AddWithValue("$groupid", (object?)item.GroupId ?? DBNull.Value);
            command.Parameters.AddWithValue("$status", item.Status);
            command.Parameters.AddWithValue("$failure", (object?)item.FailureCode ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MikanPluginCallLogPage> ListAsync(
        int page,
        int pageSize,
        string? mode = null,
        string? result = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 200);
        mode = string.IsNullOrWhiteSpace(mode) ? null : mode.Trim().ToLowerInvariant();
        result = string.IsNullOrWhiteSpace(result) ? null : result.Trim().ToLowerInvariant();
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var where = "WHERE ($mode IS NULL OR mode = $mode) AND ($result IS NULL OR result = $result)";
        await using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM mikan_plugin_call_logs {where};";
        count.Parameters.AddWithValue("$mode", (object?)mode ?? DBNull.Value);
        count.Parameters.AddWithValue("$result", (object?)result ?? DBNull.Value);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, endpoint, mode, media_type, result, requested_count,
                   accepted_count, rejected_count, failure_code, duration_ms,
                   started_at_utc, completed_at_utc
            FROM mikan_plugin_call_logs
            {where}
            ORDER BY completed_at_utc DESC, id DESC
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$mode", (object?)mode ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", (object?)result ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        var entries = new List<MikanPluginCallLog>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new MikanPluginCallLog(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetInt64(9),
                Parse(reader.GetString(10)), Parse(reader.GetString(11)), []));
        }

        for (var index = 0; index < entries.Count; index++)
        {
            entries[index] = entries[index] with
            {
                Items = await ReadItemsAsync(connection, entries[index].Id, cancellationToken).ConfigureAwait(false),
            };
        }
        return new MikanPluginCallLogPage(page, pageSize, total, entries);
    }

    private static async Task<IReadOnlyList<MikanPluginCallLogItem>> ReadItemsAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string callId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT item_index, task_id, mikanid, groupid, status, failure_code
            FROM mikan_plugin_call_log_items WHERE call_id = $call_id ORDER BY item_index;
            """;
        command.Parameters.AddWithValue("$call_id", callId);
        var values = new List<MikanPluginCallLogItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(new MikanPluginCallLogItem(
                reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return values;
    }

    private static void Validate(MikanPluginCallLog entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.RequestedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.AcceptedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.RejectedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.DurationMilliseconds);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
