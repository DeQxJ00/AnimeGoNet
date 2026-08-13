using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

public sealed record CacheBrowserBucket(
    string BucketId,
    string BucketName,
    int EntryCount);

public sealed record CacheBrowserEntry(
    string EntryId,
    string Key,
    string DeleteToken,
    int ValueBytes,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CacheBrowserEntryPage(
    string BucketId,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<CacheBrowserEntry> Items);

public sealed record CacheBrowserEntryDetail(
    string BucketId,
    string BucketName,
    string EntryId,
    string Key,
    string ValueJson,
    int ValueBytes,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset UpdatedAtUtc);

public enum CacheBrowserDeleteResult
{
    Deleted,
    NotFound,
    Changed,
    ReadOnly,
}

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

    public async Task<IReadOnlyList<CacheBrowserBucket>> ListBrowserBucketsAsync(
        string databaseName,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var normalizedDatabase = NormalizeDatabaseName(databaseName);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await PurgeExpiredAsync(
            connection,
            transaction,
            normalizedDatabase,
            Format(utcNow),
            cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT b.name, COUNT(e.key)
            FROM cache_buckets AS b
            LEFT JOIN cache_entries AS e
              ON e.database_name = b.database_name
             AND e.bucket_name = b.name
            WHERE b.database_name = $database
            GROUP BY b.name
            ORDER BY b.name COLLATE BINARY;
            """;
        command.Parameters.AddWithValue("$database", normalizedDatabase);
        var result = new List<CacheBrowserBucket>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(new CacheBrowserBucket(
                    BucketId(normalizedDatabase, reader.GetString(0)),
                    reader.GetString(0),
                    reader.GetInt32(1)));
            }
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<CacheBrowserEntryPage?> ListBrowserEntriesAsync(
        string databaseName,
        string bucketId,
        int page,
        int pageSize,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var normalizedDatabase = NormalizeDatabaseName(databaseName);
        var normalizedBucketId = NormalizeDigest(bucketId, nameof(bucketId));
        if (page is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "page must be between 1 and 1000000.");
        }
        if (pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize must be between 1 and 100.");
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var bucket = await ResolveBucketAsync(
            connection,
            transaction,
            normalizedDatabase,
            normalizedBucketId,
            cancellationToken).ConfigureAwait(false);
        if (bucket is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        await PurgeExpiredAsync(
            connection,
            transaction,
            normalizedDatabase,
            bucket,
            Format(utcNow),
            cancellationToken).ConfigureAwait(false);
        var totalCount = await CountEntriesAsync(
            connection,
            transaction,
            normalizedDatabase,
            bucket,
            cancellationToken).ConfigureAwait(false);
        var offset = checked((page - 1) * pageSize);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT key, value_json, expires_at_utc, updated_at_utc
            FROM cache_entries
            WHERE database_name = $database
              AND bucket_name = $bucket
            ORDER BY key COLLATE BINARY
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$database", normalizedDatabase);
        command.Parameters.AddWithValue("$bucket", bucket);
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", offset);
        var items = new List<CacheBrowserEntry>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = reader.GetString(0);
                var valueJson = reader.GetString(1);
                var expires = reader.IsDBNull(2) ? null : reader.GetString(2);
                var updated = reader.GetString(3);
                items.Add(new CacheBrowserEntry(
                    EntryId(normalizedDatabase, bucket, key),
                    key,
                    DeleteToken(normalizedDatabase, bucket, key, valueJson, expires, updated),
                    Encoding.UTF8.GetByteCount(valueJson),
                    expires is null ? null : Parse(expires),
                    Parse(updated)));
            }
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new CacheBrowserEntryPage(
            normalizedBucketId,
            page,
            pageSize,
            totalCount,
            items);
    }

    public async Task<CacheBrowserEntryDetail?> GetBrowserEntryAsync(
        string databaseName,
        string bucketId,
        string entryId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var normalizedDatabase = NormalizeDatabaseName(databaseName);
        var normalizedBucketId = NormalizeDigest(bucketId, nameof(bucketId));
        var normalizedEntryId = NormalizeDigest(entryId, nameof(entryId));
        await using var connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var bucket = await ResolveBucketAsync(
            connection,
            transaction,
            normalizedDatabase,
            normalizedBucketId,
            cancellationToken).ConfigureAwait(false);
        if (bucket is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        await PurgeExpiredAsync(
            connection,
            transaction,
            normalizedDatabase,
            bucket,
            Format(utcNow),
            cancellationToken).ConfigureAwait(false);
        CacheBrowserEntryDetail? result = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT key, value_json, expires_at_utc, updated_at_utc
                FROM cache_entries
                WHERE database_name = $database
                  AND bucket_name = $bucket;
                """;
            command.Parameters.AddWithValue("$database", normalizedDatabase);
            command.Parameters.AddWithValue("$bucket", bucket);
            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = reader.GetString(0);
                if (!FixedTimeEquals(
                    normalizedEntryId,
                    EntryId(normalizedDatabase, bucket, key)))
                {
                    continue;
                }

                var valueJson = reader.GetString(1);
                result = new CacheBrowserEntryDetail(
                    normalizedBucketId,
                    bucket,
                    normalizedEntryId,
                    key,
                    valueJson,
                    Encoding.UTF8.GetByteCount(valueJson),
                    reader.IsDBNull(2) ? null : Parse(reader.GetString(2)),
                    Parse(reader.GetString(3)));
                break;
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<CacheBrowserDeleteResult> DeleteBrowserEntryAsync(
        string databaseName,
        string bucketId,
        string entryId,
        string deleteToken,
        CancellationToken cancellationToken = default)
    {
        var normalizedDatabase = NormalizeDatabaseName(databaseName);
        var normalizedBucketId = NormalizeDigest(bucketId, nameof(bucketId));
        var normalizedEntryId = NormalizeDigest(entryId, nameof(entryId));
        var normalizedDeleteToken = NormalizeDigest(deleteToken, nameof(deleteToken));
        if (!string.Equals(normalizedDatabase, "bolt", StringComparison.Ordinal))
        {
            return CacheBrowserDeleteResult.ReadOnly;
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var bucket = await ResolveBucketAsync(
            connection,
            transaction,
            normalizedDatabase,
            normalizedBucketId,
            cancellationToken).ConfigureAwait(false);
        if (bucket is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CacheBrowserDeleteResult.NotFound;
        }

        string? key = null;
        string? valueJson = null;
        string? expires = null;
        string? updated = null;
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = """
                SELECT key, value_json, expires_at_utc, updated_at_utc
                FROM cache_entries
                WHERE database_name = $database
                  AND bucket_name = $bucket;
                """;
            find.Parameters.AddWithValue("$database", normalizedDatabase);
            find.Parameters.AddWithValue("$bucket", bucket);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var candidateKey = reader.GetString(0);
                if (!FixedTimeEquals(
                    normalizedEntryId,
                    EntryId(normalizedDatabase, bucket, candidateKey)))
                {
                    continue;
                }
                key = candidateKey;
                valueJson = reader.GetString(1);
                expires = reader.IsDBNull(2) ? null : reader.GetString(2);
                updated = reader.GetString(3);
                break;
            }
        }

        if (key is null || valueJson is null || updated is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CacheBrowserDeleteResult.NotFound;
        }
        if (!FixedTimeEquals(
            normalizedDeleteToken,
            DeleteToken(normalizedDatabase, bucket, key, valueJson, expires, updated)))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CacheBrowserDeleteResult.Changed;
        }

        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = """
            DELETE FROM cache_entries
            WHERE database_name = $database
              AND bucket_name = $bucket
              AND key = $key
              AND value_json = $value
              AND expires_at_utc IS $expires
              AND updated_at_utc = $updated;
            """;
        delete.Parameters.AddWithValue("$database", normalizedDatabase);
        delete.Parameters.AddWithValue("$bucket", bucket);
        delete.Parameters.AddWithValue("$key", key);
        delete.Parameters.AddWithValue("$value", valueJson);
        delete.Parameters.AddWithValue("$expires", expires is null ? DBNull.Value : expires);
        delete.Parameters.AddWithValue("$updated", updated);
        var deleted = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted == 1
            ? CacheBrowserDeleteResult.Deleted
            : CacheBrowserDeleteResult.Changed;
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

    private static async Task PurgeExpiredAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string databaseName,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM cache_entries
            WHERE database_name = $database
              AND expires_at_utc IS NOT NULL
              AND expires_at_utc <= $now;
            """;
        command.Parameters.AddWithValue("$database", databaseName);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task PurgeExpiredAsync(
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
            DELETE FROM cache_entries
            WHERE database_name = $database
              AND bucket_name = $bucket
              AND expires_at_utc IS NOT NULL
              AND expires_at_utc <= $now;
            """;
        command.Parameters.AddWithValue("$database", databaseName);
        command.Parameters.AddWithValue("$bucket", bucket);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ResolveBucketAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string databaseName,
        string bucketId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT name
            FROM cache_buckets
            WHERE database_name = $database;
            """;
        command.Parameters.AddWithValue("$database", databaseName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var candidate = reader.GetString(0);
            if (FixedTimeEquals(bucketId, BucketId(databaseName, candidate)))
            {
                return candidate;
            }
        }
        return null;
    }

    private static async Task<int> CountEntriesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string databaseName,
        string bucket,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM cache_entries
            WHERE database_name = $database
              AND bucket_name = $bucket;
            """;
        command.Parameters.AddWithValue("$database", databaseName);
        command.Parameters.AddWithValue("$bucket", bucket);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
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

    private static string NormalizeDigest(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException($"{name} must be a SHA-256 digest.", name);
        }
        return normalized;
    }

    private static string BucketId(string databaseName, string bucket) =>
        Digest("bucket", databaseName, bucket);

    private static string EntryId(string databaseName, string bucket, string key) =>
        Digest("entry", databaseName, bucket, key);

    private static string DeleteToken(
        string databaseName,
        string bucket,
        string key,
        string valueJson,
        string? expires,
        string updated) =>
        Digest("delete", databaseName, bucket, key, valueJson, expires ?? string.Empty, updated);

    private static string Digest(params string[] parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(part.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(part);
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(left),
            Convert.FromHexString(right));

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
