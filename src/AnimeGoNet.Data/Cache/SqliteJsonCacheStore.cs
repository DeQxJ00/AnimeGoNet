using System.Globalization;
using System.Text.Json;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Cache;

public sealed record CacheEntryWrite(string Key, string ValueJson);

public sealed record CacheJsonValue(
    string DatabaseName,
    string Bucket,
    string Key,
    string ValueJson,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed class SqliteJsonCacheStore(AnimeGoSqliteDatabase database)
{
    private const int MaxDatabaseNameLength = 32;
    private const int MaxBucketLength = 256;
    private const int MaxKeyLength = 4096;
    private const int MaxValueBytes = 8 * 1024 * 1024;

    public async Task AddBucketAsync(
        string databaseName,
        string bucket,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var normalizedDatabase = NormalizeDatabaseName(databaseName);
        var normalizedBucket = NormalizeRequired(bucket, nameof(bucket), MaxBucketLength);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cache_buckets (database_name, name, created_at_utc)
            VALUES ($database, $bucket, $now)
            ON CONFLICT(database_name, name) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$database", normalizedDatabase);
        command.Parameters.AddWithValue("$bucket", normalizedBucket);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task PutJsonAsync(
        string databaseName,
        string bucket,
        string key,
        string valueJson,
        TimeSpan? ttl,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        PutBatchJsonAsync(
            databaseName,
            bucket,
            [new CacheEntryWrite(key, valueJson)],
            ttl,
            utcNow,
            cancellationToken);

    public async Task PutBatchJsonAsync(
        string databaseName,
        string bucket,
        IReadOnlyList<CacheEntryWrite> entries,
        TimeSpan? ttl,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var normalizedDatabase = NormalizeDatabaseName(databaseName);
        var normalizedBucket = NormalizeRequired(bucket, nameof(bucket), MaxBucketLength);
        var validated = entries
            .Select(entry => new CacheEntryWrite(
                NormalizeRequired(entry.Key, "key", MaxKeyLength),
                ValidateJson(entry.ValueJson)))
            .ToArray();
        if (validated.Length == 0)
        {
            await AddBucketAsync(
                normalizedDatabase,
                normalizedBucket,
                utcNow,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var expiresAtUtc = ttl is { } duration && duration > TimeSpan.Zero
            ? utcNow.Add(duration)
            : (DateTimeOffset?)null;
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureBucketAsync(
            connection,
            transaction,
            normalizedDatabase,
            normalizedBucket,
            now,
            cancellationToken).ConfigureAwait(false);
        foreach (var entry in validated)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cache_entries (
                    database_name, bucket_name, key, value_json,
                    expires_at_utc, updated_at_utc)
                VALUES ($database, $bucket, $key, $value, $expires, $now)
                ON CONFLICT(database_name, bucket_name, key) DO UPDATE SET
                    value_json = excluded.value_json,
                    expires_at_utc = excluded.expires_at_utc,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$database", normalizedDatabase);
            command.Parameters.AddWithValue("$bucket", normalizedBucket);
            command.Parameters.AddWithValue("$key", entry.Key);
            command.Parameters.AddWithValue("$value", entry.ValueJson);
            command.Parameters.AddWithValue(
                "$expires",
                expiresAtUtc is null ? DBNull.Value : Format(expiresAtUtc.Value));
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CacheJsonValue?> GetJsonAsync(
        string databaseName,
        string bucket,
        string key,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var normalizedDatabase = NormalizeDatabaseName(databaseName);
        var normalizedBucket = NormalizeRequired(bucket, nameof(bucket), MaxBucketLength);
        var normalizedKey = NormalizeRequired(key, nameof(key), MaxKeyLength);
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await DeleteExpiredAsync(
            connection,
            transaction,
            normalizedDatabase,
            normalizedBucket,
            normalizedKey,
            now,
            cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT value_json, expires_at_utc, updated_at_utc
            FROM cache_entries
            WHERE database_name = $database
              AND bucket_name = $bucket
              AND key = $key;
            """;
        command.Parameters.AddWithValue("$database", normalizedDatabase);
        command.Parameters.AddWithValue("$bucket", normalizedBucket);
        command.Parameters.AddWithValue("$key", normalizedKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        CacheJsonValue? result = null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result = new CacheJsonValue(
                normalizedDatabase,
                normalizedBucket,
                normalizedKey,
                reader.GetString(0),
                reader.IsDBNull(1) ? null : Parse(reader.GetString(1)),
                Parse(reader.GetString(2)));
        }

        await reader.DisposeAsync().ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<IReadOnlyList<string>> ListBucketsAsync(
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        var normalizedDatabase = NormalizeDatabaseName(databaseName);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM cache_buckets
            WHERE database_name = $database
            ORDER BY name COLLATE BINARY;
            """;
        command.Parameters.AddWithValue("$database", normalizedDatabase);
        return await ReadStringsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(
        string databaseName,
        string bucket,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var normalizedDatabase = NormalizeDatabaseName(databaseName);
        var normalizedBucket = NormalizeRequired(bucket, nameof(bucket), MaxBucketLength);
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var purge = connection.CreateCommand())
        {
            purge.Transaction = transaction;
            purge.CommandText = """
                DELETE FROM cache_entries
                WHERE database_name = $database
                  AND bucket_name = $bucket
                  AND expires_at_utc IS NOT NULL
                  AND expires_at_utc <= $now;
                """;
            purge.Parameters.AddWithValue("$database", normalizedDatabase);
            purge.Parameters.AddWithValue("$bucket", normalizedBucket);
            purge.Parameters.AddWithValue("$now", now);
            await purge.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT key
            FROM cache_entries
            WHERE database_name = $database
              AND bucket_name = $bucket
            ORDER BY key COLLATE BINARY;
            """;
        command.Parameters.AddWithValue("$database", normalizedDatabase);
        command.Parameters.AddWithValue("$bucket", normalizedBucket);
        var result = await ReadStringsAsync(command, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> DeleteAsync(
        string databaseName,
        string bucket,
        string key,
        CancellationToken cancellationToken = default)
    {
        var normalizedDatabase = NormalizeDatabaseName(databaseName);
        var normalizedBucket = NormalizeRequired(bucket, nameof(bucket), MaxBucketLength);
        var normalizedKey = NormalizeRequired(key, nameof(key), MaxKeyLength);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM cache_entries
            WHERE database_name = $database
              AND bucket_name = $bucket
              AND key = $key;
            """;
        command.Parameters.AddWithValue("$database", normalizedDatabase);
        command.Parameters.AddWithValue("$bucket", normalizedBucket);
        command.Parameters.AddWithValue("$key", normalizedKey);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<int> PurgeExpiredAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM cache_entries
            WHERE expires_at_utc IS NOT NULL
              AND expires_at_utc <= $now;
            """;
        command.Parameters.AddWithValue("$now", Format(utcNow));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureBucketAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string databaseName,
        string bucket,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cache_buckets (database_name, name, created_at_utc)
            VALUES ($database, $bucket, $now)
            ON CONFLICT(database_name, name) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$database", databaseName);
        command.Parameters.AddWithValue("$bucket", bucket);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteExpiredAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string databaseName,
        string bucket,
        string key,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM cache_entries
            WHERE database_name = $database
              AND bucket_name = $bucket
              AND key = $key
              AND expires_at_utc IS NOT NULL
              AND expires_at_utc <= $now;
            """;
        command.Parameters.AddWithValue("$database", databaseName);
        command.Parameters.AddWithValue("$bucket", bucket);
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(reader.GetString(0));
        }
        return values;
    }

    private static string NormalizeDatabaseName(string value)
    {
        var normalized = NormalizeRequired(value, "databaseName", MaxDatabaseNameLength).ToLowerInvariant();
        return normalized is "bolt" or "bolt_sub"
            ? normalized
            : throw new ArgumentException("databaseName must be bolt or bolt_sub.", nameof(value));
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{name} must not exceed {maxLength} characters.", name);
        }
        return normalized;
    }

    private static string ValidateJson(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (System.Text.Encoding.UTF8.GetByteCount(value) > MaxValueBytes)
        {
            throw new ArgumentException($"valueJson must not exceed {MaxValueBytes} UTF-8 bytes.", nameof(value));
        }
        using var document = JsonDocument.Parse(value);
        return document.RootElement.GetRawText();
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
