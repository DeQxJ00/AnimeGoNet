using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Notifications;

public sealed class NotificationStore(AnimeGoSqliteDatabase database)
{
    public async Task<IReadOnlyList<NotificationChannel>> ListChannelsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, provider, enabled, endpoint_url, secret, target,
                   options_json, events_json, created_at_utc, updated_at_utc
            FROM notification_channels
            ORDER BY name COLLATE NOCASE, id;
            """;
        var result = new List<NotificationChannel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadChannel(reader));
        }
        return result;
    }

    public async Task<NotificationChannel?> GetChannelAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, provider, enabled, endpoint_url, secret, target,
                   options_json, events_json, created_at_utc, updated_at_utc
            FROM notification_channels WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadChannel(reader) : null;
    }

    public async Task<NotificationChannel> SaveChannelAsync(
        string? id,
        NotificationChannelWrite write,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        Validate(write);
        id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
        var now = Format(utcNow);
        var eventsJson = new JsonArray(
            write.Events.Distinct(StringComparer.Ordinal)
                .Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()).ToJsonString();
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO notification_channels (
                id, name, provider, enabled, endpoint_url, secret, target,
                options_json, events_json, created_at_utc, updated_at_utc)
            VALUES ($id, $name, $provider, $enabled, $endpoint, $secret, $target,
                    $options, $events, $now, $now)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name, provider = excluded.provider,
                enabled = excluded.enabled, endpoint_url = excluded.endpoint_url,
                secret = excluded.secret, target = excluded.target,
                options_json = excluded.options_json, events_json = excluded.events_json,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", write.Name.Trim());
        command.Parameters.AddWithValue("$provider", write.Provider);
        command.Parameters.AddWithValue("$enabled", write.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$endpoint", write.EndpointUrl.Trim());
        command.Parameters.AddWithValue("$secret", (object?)NullIfWhiteSpace(write.Secret) ?? DBNull.Value);
        command.Parameters.AddWithValue("$target", (object?)NullIfWhiteSpace(write.Target) ?? DBNull.Value);
        command.Parameters.AddWithValue("$options", write.OptionsJson);
        command.Parameters.AddWithValue("$events", eventsJson);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return await GetChannelAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Saved notification channel disappeared.");
    }

    public async Task<bool> DeleteChannelAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM notification_channels WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<NotificationEvent?> ClaimNextEventAsync(
        DateTimeOffset utcNow,
        TimeSpan lease,
        CancellationToken cancellationToken = default)
    {
        var now = Format(utcNow);
        var expires = Format(utcNow.Add(lease));
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE notification_events
            SET state = 'processing', lease_expires_at_utc = $expires
            WHERE id = (
                SELECT id FROM notification_events
                WHERE state = 'pending'
                   OR (state = 'processing' AND lease_expires_at_utc <= $now)
                ORDER BY created_at_utc, id LIMIT 1)
            RETURNING id, event_type, task_id, title, body, payload_json, created_at_utc;
            """;
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$expires", expires);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        NotificationEvent? result = null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result = new NotificationEvent(
                reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                Parse(reader.GetString(6)));
        }
        await reader.DisposeAsync().ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<NotificationEvent> CreateTestEventAsync(
        string title,
        string body,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var value = new NotificationEvent(
            Guid.NewGuid().ToString("N"), "test", null, title, body, "{}", utcNow.ToUniversalTime());
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO notification_events (
                id, event_type, task_id, title, body, payload_json,
                state, lease_expires_at_utc, created_at_utc)
            VALUES ($id, 'test', NULL, $title, $body, '{}',
                    'processing', $expires, $now);
            """;
        command.Parameters.AddWithValue("$id", value.Id);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$expires", Format(utcNow.AddMinutes(1)));
        command.Parameters.AddWithValue("$now", Format(utcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return value;
    }

    public async Task CompleteEventAsync(
        string eventId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE notification_events
            SET state = 'completed', lease_expires_at_utc = NULL, completed_at_utc = $now
            WHERE id = $id AND state = 'processing';
            """;
        command.Parameters.AddWithValue("$id", eventId);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordDeliveryAsync(
        NotificationEvent value,
        NotificationChannel channel,
        NotificationSendResult result,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO notification_deliveries (
                id, event_id, channel_id, channel_name, provider, event_type,
                task_id, title, state, http_status, failure_code,
                response_excerpt, duration_ms, created_at_utc)
            VALUES ($id, $event_id, $channel_id, $channel_name, $provider, $event_type,
                    $task_id, $title, $state, $http_status, $failure_code,
                    $response, $duration, $now);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$event_id", value.Id);
        command.Parameters.AddWithValue("$channel_id", channel.Id);
        command.Parameters.AddWithValue("$channel_name", channel.Name);
        command.Parameters.AddWithValue("$provider", channel.Provider);
        command.Parameters.AddWithValue("$event_type", value.EventType);
        command.Parameters.AddWithValue("$task_id", (object?)value.TaskId ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", value.Title);
        command.Parameters.AddWithValue("$state", result.Succeeded ? "succeeded" : "failed");
        command.Parameters.AddWithValue("$http_status", (object?)result.HttpStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$failure_code", (object?)result.FailureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$response", (object?)result.ResponseExcerpt ?? DBNull.Value);
        command.Parameters.AddWithValue("$duration", result.DurationMilliseconds);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NotificationDelivery>> ListDeliveriesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, event_id, channel_id, channel_name, provider, event_type,
                   task_id, title, state, http_status, failure_code,
                   response_excerpt, duration_ms, created_at_utc
            FROM notification_deliveries
            ORDER BY created_at_utc DESC, id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<NotificationDelivery>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new NotificationDelivery(
                reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetInt64(12),
                Parse(reader.GetString(13))));
        }
        return result;
    }

    private static NotificationChannel ReadChannel(SqliteDataReader reader)
    {
        using var eventsDocument = JsonDocument.Parse(reader.GetString(8));
        var events = eventsDocument.RootElement.EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .Where(value => value.Length > 0)
            .ToArray();
        return new NotificationChannel(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3) != 0,
            reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), events,
            Parse(reader.GetString(9)), Parse(reader.GetString(10)));
    }

    private static void Validate(NotificationChannelWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (string.IsNullOrWhiteSpace(write.Name) || write.Name.Trim().Length > 100)
            throw new ArgumentException("Channel name must contain 1 to 100 characters.");
        if (!NotificationProviders.All.Contains(write.Provider))
            throw new ArgumentException("Notification provider is not supported.");
        if (!Uri.TryCreate(write.EndpointUrl?.Trim(), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
            throw new ArgumentException("Endpoint URL must be an absolute HTTP or HTTPS URL.");
        try { using var _ = JsonDocument.Parse(write.OptionsJson); }
        catch (JsonException) { throw new ArgumentException("options_json must be valid JSON."); }
        if (write.Events.Count == 0 || write.Events.Any(value => !NotificationEventTypes.All.Contains(value)))
            throw new ArgumentException("At least one supported notification event is required.");
        if (write.Provider is "bark" or "telegram" or "serverchan" or "pushplus"
            && string.IsNullOrWhiteSpace(write.Secret))
            throw new ArgumentException("The selected provider requires a secret or device key.");
        if (write.Provider == "telegram" && string.IsNullOrWhiteSpace(write.Target))
            throw new ArgumentException("Telegram requires a chat ID target.");
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
