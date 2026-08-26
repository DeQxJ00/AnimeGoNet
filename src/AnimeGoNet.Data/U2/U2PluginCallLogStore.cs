using System.Globalization;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.U2;

public sealed record U2PluginCallLogItem(
    int Index,
    int? U2Id,
    string Title,
    string DetailsUrl,
    int? AniDbId,
    int? CategoryId,
    string? CategoryName,
    string MediaType,
    string? TaskId,
    string Status,
    string? FailureCode);

public sealed record U2PluginCallLog(
    string Id,
    string Endpoint,
    string SourceProfileId,
    string Result,
    int RequestedCount,
    int AcceptedCount,
    int RejectedCount,
    string? FailureCode,
    long DurationMilliseconds,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<U2PluginCallLogItem> Items);

public sealed record U2PluginCallLogPage(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<U2PluginCallLog> Items);

public sealed class U2PluginCallLogStore(AnimeGoSqliteDatabase database)
{
    public async Task RecordAsync(
        U2PluginCallLog entry,
        CancellationToken cancellationToken = default)
    {
        Validate(entry);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO u2_plugin_call_logs (
                    id, endpoint, source_profile_id, result, requested_count,
                    accepted_count, rejected_count, failure_code, duration_ms,
                    started_at_utc, completed_at_utc)
                VALUES (
                    $id, $endpoint, $source_profile_id, $result, $requested,
                    $accepted, $rejected, $failure, $duration, $started, $completed);
                """;
            command.Parameters.AddWithValue("$id", entry.Id);
            command.Parameters.AddWithValue("$endpoint", entry.Endpoint);
            command.Parameters.AddWithValue("$source_profile_id", entry.SourceProfileId);
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
                INSERT INTO u2_plugin_call_log_items (
                    call_id, item_index, u2id, title, details_url, anidbid,
                    category_id, category_name, media_type, task_id, status, failure_code)
                VALUES (
                    $call_id, $index, $u2id, $title, $details_url, $anidbid,
                    $category_id, $category_name, $media_type, $task_id, $status, $failure);
                """;
            command.Parameters.AddWithValue("$call_id", entry.Id);
            command.Parameters.AddWithValue("$index", item.Index);
            command.Parameters.AddWithValue("$u2id", (object?)item.U2Id ?? DBNull.Value);
            command.Parameters.AddWithValue("$title", item.Title.Trim());
            command.Parameters.AddWithValue("$details_url", item.DetailsUrl);
            command.Parameters.AddWithValue("$anidbid", (object?)item.AniDbId ?? DBNull.Value);
            command.Parameters.AddWithValue("$category_id", (object?)item.CategoryId ?? DBNull.Value);
            command.Parameters.AddWithValue("$category_name", (object?)Normalize(item.CategoryName) ?? DBNull.Value);
            command.Parameters.AddWithValue("$media_type", item.MediaType);
            command.Parameters.AddWithValue("$task_id", (object?)item.TaskId ?? DBNull.Value);
            command.Parameters.AddWithValue("$status", item.Status);
            command.Parameters.AddWithValue("$failure", (object?)item.FailureCode ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<U2PluginCallLogPage> ListAsync(
        int page,
        int pageSize,
        string? result = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 200);
        result = Normalize(result)?.ToLowerInvariant();
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        const string where = "WHERE ($result IS NULL OR result = $result)";
        await using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM u2_plugin_call_logs {where};";
        count.Parameters.AddWithValue("$result", (object?)result ?? DBNull.Value);
        var total = Convert.ToInt32(
            await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, endpoint, source_profile_id, result, requested_count,
                   accepted_count, rejected_count, failure_code, duration_ms,
                   started_at_utc, completed_at_utc
            FROM u2_plugin_call_logs
            {where}
            ORDER BY completed_at_utc DESC, id DESC
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$result", (object?)result ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        var entries = new List<U2PluginCallLog>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new U2PluginCallLog(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetInt64(8),
                Parse(reader.GetString(9)), Parse(reader.GetString(10)), []));
        }
        for (var index = 0; index < entries.Count; index++)
        {
            entries[index] = entries[index] with
            {
                Items = await ReadItemsAsync(connection, entries[index].Id, cancellationToken).ConfigureAwait(false),
            };
        }
        return new U2PluginCallLogPage(page, pageSize, total, entries);
    }

    private static async Task<IReadOnlyList<U2PluginCallLogItem>> ReadItemsAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string callId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT item_index, u2id, title, details_url, anidbid, category_id,
                   category_name, media_type, task_id, status, failure_code
            FROM u2_plugin_call_log_items WHERE call_id = $call_id ORDER BY item_index;
            """;
        command.Parameters.AddWithValue("$call_id", callId);
        var values = new List<U2PluginCallLogItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(new U2PluginCallLogItem(
                reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10)));
        }
        return values;
    }

    private static void Validate(U2PluginCallLog entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.SourceProfileId);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.RequestedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.AcceptedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.RejectedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.DurationMilliseconds);
        foreach (var item in entry.Items)
        {
            if (item.U2Id is <= 0) throw new ArgumentOutOfRangeException(nameof(entry), "u2id must be positive.");
            if (item.Title.Length > 1000 || item.DetailsUrl.Length > 2048)
                throw new ArgumentOutOfRangeException(nameof(entry), "U2 audit item is too long.");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
