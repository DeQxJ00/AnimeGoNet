using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeGoNet.Data.Serialization;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Cache;

public sealed record LegacyCacheImportReport(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("package_sha256")] string PackageSha256,
    [property: JsonPropertyName("source_commit")] string SourceCommit,
    [property: JsonPropertyName("bucket_count")] int BucketCount,
    [property: JsonPropertyName("entry_count")] int EntryCount,
    [property: JsonPropertyName("imported_entry_count")] int ImportedEntryCount,
    [property: JsonPropertyName("skipped_expired_entry_count")] int SkippedExpiredEntryCount,
    [property: JsonPropertyName("imported_at_utc")] DateTimeOffset ImportedAtUtc,
    [property: JsonPropertyName("last_seen_at_utc")] DateTimeOffset LastSeenAtUtc,
    [property: JsonPropertyName("repeat_count")] int RepeatCount);

public sealed class LegacyCacheImportException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LegacyCacheExportPackage
{
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("source_commit")]
    public string? SourceCommit { get; init; }

    [JsonPropertyName("exported_at_utc")]
    public string? ExportedAtUtc { get; init; }

    [JsonPropertyName("databases")]
    public LegacyCacheExportDatabase[]? Databases { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LegacyCacheExportDatabase
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("buckets")]
    public LegacyCacheExportBucket[]? Buckets { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LegacyCacheExportBucket
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("entries")]
    public LegacyCacheExportEntry[]? Entries { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LegacyCacheExportEntry
{
    [JsonPropertyName("key_json")]
    public JsonElement KeyJson { get; init; }

    [JsonPropertyName("value_json")]
    public JsonElement ValueJson { get; init; }

    [JsonPropertyName("expires_at_unix_seconds")]
    public long ExpiresAtUnixSeconds { get; init; }
}

public sealed class LegacyCacheImporter(AnimeGoSqliteDatabase database)
{
    public const string FormatName = "animego-legacy-cache";
    public const int FormatVersion = 1;
    public const string PinnedSourceCommit =
        "develop@c7475dfc55a374cd0dd08821bf17125dab1e3145";
    public const int MaxPackageBytes = 64 * 1024 * 1024;
    public const int MaxEntries = 50_000;

    private const int MaxKeyBytes = 4096;
    private const int MaxValueBytes = 8 * 1024 * 1024;

    private static readonly Dictionary<string, IReadOnlySet<string>> KnownBuckets =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["bolt"] = new HashSet<string>(
                ["bangumi", "hash2entity", "mikan", "name2hash", "themoviedb"],
                StringComparer.Ordinal),
            ["bolt_sub"] = new HashSet<string>(["bangumi_sub"], StringComparer.Ordinal),
        };

    public async Task<LegacyCacheImportReport> ImportAsync(
        Stream input,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var bytes = await ReadBoundedAsync(input, cancellationToken).ConfigureAwait(false);
        LegacyCacheExportPackage package;
        try
        {
            package = JsonSerializer.Deserialize(
                    bytes,
                    DataJsonContext.Default.LegacyCacheExportPackage)
                ?? throw Fail("legacy_cache_package_null", "Legacy cache package must be a JSON object.");
        }
        catch (LegacyCacheImportException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Fail(
                "legacy_cache_package_invalid_json",
                "Legacy cache package is not valid schema-v1 JSON.",
                exception);
        }

        var normalized = ValidateAndNormalize(package);
        var fingerprint = ComputeFingerprint(normalized);
        var now = utcNow.ToUniversalTime();
        var nowText = Format(now);

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var existing = await FindExistingAsync(
            connection,
            transaction,
            fingerprint,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await using var repeat = connection.CreateCommand();
            repeat.Transaction = transaction;
            repeat.CommandText = """
                UPDATE legacy_cache_imports
                SET last_seen_at_utc = $now,
                    repeat_count = repeat_count + 1
                WHERE package_sha256 = $fingerprint;
                """;
            repeat.Parameters.AddWithValue("$now", nowText);
            repeat.Parameters.AddWithValue("$fingerprint", fingerprint);
            await repeat.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return existing with
            {
                Status = "already_imported",
                LastSeenAtUtc = now,
                RepeatCount = checked(existing.RepeatCount + 1),
            };
        }

        var importedEntries = 0;
        var skippedExpiredEntries = 0;
        foreach (var sourceDatabase in normalized.Databases)
        {
            foreach (var bucket in sourceDatabase.Buckets)
            {
                await EnsureBucketAsync(
                    connection,
                    transaction,
                    sourceDatabase.Name,
                    bucket.Name,
                    nowText,
                    cancellationToken).ConfigureAwait(false);
                foreach (var entry in bucket.Entries)
                {
                    if (entry.ExpiresAtUnixSeconds > 0
                        && entry.ExpiresAtUnixSeconds <= now.ToUnixTimeSeconds())
                    {
                        skippedExpiredEntries++;
                        continue;
                    }

                    await UpsertEntryAsync(
                        connection,
                        transaction,
                        sourceDatabase.Name,
                        bucket.Name,
                        entry,
                        nowText,
                        cancellationToken).ConfigureAwait(false);
                    importedEntries++;
                }
            }
        }

        await using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
                INSERT INTO legacy_cache_imports (
                    package_sha256, format_version, source_commit,
                    bucket_count, entry_count, imported_entry_count,
                    skipped_expired_entry_count, imported_at_utc,
                    last_seen_at_utc, repeat_count)
                VALUES (
                    $fingerprint, $version, $commit,
                    $buckets, $entries, $imported,
                    $expired, $now, $now, 0);
                """;
            audit.Parameters.AddWithValue("$fingerprint", fingerprint);
            audit.Parameters.AddWithValue("$version", FormatVersion);
            audit.Parameters.AddWithValue("$commit", normalized.SourceCommit);
            audit.Parameters.AddWithValue("$buckets", normalized.BucketCount);
            audit.Parameters.AddWithValue("$entries", normalized.EntryCount);
            audit.Parameters.AddWithValue("$imported", importedEntries);
            audit.Parameters.AddWithValue("$expired", skippedExpiredEntries);
            audit.Parameters.AddWithValue("$now", nowText);
            await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new LegacyCacheImportReport(
            "imported",
            fingerprint,
            normalized.SourceCommit,
            normalized.BucketCount,
            normalized.EntryCount,
            importedEntries,
            skippedExpiredEntries,
            now,
            now,
            0);
    }

    private static NormalizedPackage ValidateAndNormalize(LegacyCacheExportPackage package)
    {
        if (!string.Equals(package.Format, FormatName, StringComparison.Ordinal)
            || package.Version != FormatVersion)
        {
            throw Fail(
                "legacy_cache_format_unsupported",
                $"Legacy cache package must use {FormatName} schema version {FormatVersion}.");
        }

        var sourceCommit = NormalizeRequired(
            package.SourceCommit,
            128,
            "legacy_cache_source_commit_invalid",
            "source_commit must contain 1 to 128 characters.");
        if (!string.Equals(sourceCommit, PinnedSourceCommit, StringComparison.Ordinal))
        {
            throw Fail(
                "legacy_cache_source_commit_unsupported",
                "Legacy cache package source_commit does not match the pinned upstream baseline.");
        }
        if (!DateTimeOffset.TryParseExact(
                package.ExportedAtUtc,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _))
        {
            throw Fail(
                "legacy_cache_export_time_invalid",
                "exported_at_utc must be an ISO-8601 round-trip timestamp.");
        }

        var databases = package.Databases
            ?? throw Fail("legacy_cache_databases_missing", "databases is required.");
        if (databases.Length is < 1 or > 2)
        {
            throw Fail("legacy_cache_database_count_invalid", "databases must contain one or two items.");
        }

        var databaseNames = new HashSet<string>(StringComparer.Ordinal);
        var normalizedDatabases = new List<NormalizedDatabase>(databases.Length);
        var totalBuckets = 0;
        var totalEntries = 0;
        foreach (var sourceDatabase in databases)
        {
            var databaseName = NormalizeRequired(
                sourceDatabase.Name,
                32,
                "legacy_cache_database_invalid",
                "Every database must have a supported name.");
            if (!KnownBuckets.TryGetValue(databaseName, out var knownBuckets)
                || !databaseNames.Add(databaseName))
            {
                throw Fail(
                    "legacy_cache_database_invalid",
                    "Every database must be unique and named bolt or bolt_sub.");
            }

            var buckets = sourceDatabase.Buckets
                ?? throw Fail("legacy_cache_buckets_missing", "Every database requires buckets.");
            if (buckets.Length > knownBuckets.Count)
            {
                throw Fail("legacy_cache_bucket_count_invalid", "A database contains too many buckets.");
            }

            var bucketNames = new HashSet<string>(StringComparer.Ordinal);
            var normalizedBuckets = new List<NormalizedBucket>(buckets.Length);
            foreach (var sourceBucket in buckets)
            {
                var bucketName = NormalizeRequired(
                    sourceBucket.Name,
                    256,
                    "legacy_cache_bucket_invalid",
                    "Every bucket must have a supported name.");
                if (!knownBuckets.Contains(bucketName) || !bucketNames.Add(bucketName))
                {
                    throw Fail(
                        "legacy_cache_bucket_invalid",
                        "Every bucket must be a unique known upstream bucket for its database.");
                }

                var entries = sourceBucket.Entries
                    ?? throw Fail("legacy_cache_entries_missing", "Every bucket requires entries.");
                totalBuckets = checked(totalBuckets + 1);
                totalEntries = checked(totalEntries + entries.Length);
                if (totalEntries > MaxEntries)
                {
                    throw Fail(
                        "legacy_cache_entry_limit_exceeded",
                        $"Legacy cache package must not contain more than {MaxEntries} entries.");
                }

                var keys = new HashSet<string>(StringComparer.Ordinal);
                var normalizedEntries = new List<NormalizedEntry>(entries.Length);
                foreach (var sourceEntry in entries)
                {
                    var keyJson = NormalizeJson(
                        sourceEntry.KeyJson,
                        MaxKeyBytes,
                        "legacy_cache_key_invalid",
                        "A legacy cache key is missing, duplicated, or too large.");
                    if (!keys.Add(keyJson))
                    {
                        throw Fail(
                            "legacy_cache_key_invalid",
                            "A legacy cache key is missing, duplicated, or too large.");
                    }
                    var valueJson = NormalizeJson(
                        sourceEntry.ValueJson,
                        MaxValueBytes,
                        "legacy_cache_value_invalid",
                        "A legacy cache value is missing or too large.");
                    if (sourceEntry.ExpiresAtUnixSeconds < 0)
                    {
                        throw Fail(
                            "legacy_cache_expiration_invalid",
                            "expires_at_unix_seconds must be zero or a valid positive Unix timestamp.");
                    }
                    if (sourceEntry.ExpiresAtUnixSeconds > 0)
                    {
                        try
                        {
                            _ = DateTimeOffset.FromUnixTimeSeconds(sourceEntry.ExpiresAtUnixSeconds);
                        }
                        catch (ArgumentOutOfRangeException exception)
                        {
                            throw Fail(
                                "legacy_cache_expiration_invalid",
                                "expires_at_unix_seconds must be zero or a valid positive Unix timestamp.",
                                exception);
                        }
                    }
                    normalizedEntries.Add(new NormalizedEntry(
                        keyJson,
                        valueJson,
                        sourceEntry.ExpiresAtUnixSeconds));
                }

                normalizedEntries.Sort(static (left, right) =>
                    StringComparer.Ordinal.Compare(left.KeyJson, right.KeyJson));
                normalizedBuckets.Add(new NormalizedBucket(bucketName, normalizedEntries));
            }

            normalizedBuckets.Sort(static (left, right) =>
                StringComparer.Ordinal.Compare(left.Name, right.Name));
            normalizedDatabases.Add(new NormalizedDatabase(databaseName, normalizedBuckets));
        }

        normalizedDatabases.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.Name, right.Name));
        return new NormalizedPackage(
            sourceCommit,
            normalizedDatabases,
            totalBuckets,
            totalEntries);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var block = new byte[64 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(block, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }
            if (buffer.Length + read > MaxPackageBytes)
            {
                throw Fail(
                    "legacy_cache_package_too_large",
                    $"Legacy cache package must not exceed {MaxPackageBytes} bytes.");
            }
            await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ComputeFingerprint(NormalizedPackage package)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, FormatName);
        Append(hash, FormatVersion.ToString(CultureInfo.InvariantCulture));
        Append(hash, package.SourceCommit);
        foreach (var database in package.Databases)
        {
            Append(hash, database.Name);
            foreach (var bucket in database.Buckets)
            {
                Append(hash, bucket.Name);
                foreach (var entry in bucket.Entries)
                {
                    Append(hash, entry.KeyJson);
                    Append(hash, entry.ValueJson);
                    Append(hash, entry.ExpiresAtUnixSeconds.ToString(CultureInfo.InvariantCulture));
                }
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(length, bytes.LongLength);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static async Task<LegacyCacheImportReport?> FindExistingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT source_commit, bucket_count, entry_count,
                   imported_entry_count, skipped_expired_entry_count,
                   imported_at_utc, last_seen_at_utc, repeat_count
            FROM legacy_cache_imports
            WHERE package_sha256 = $fingerprint;
            """;
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }
        return new LegacyCacheImportReport(
            "imported",
            fingerprint,
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            Parse(reader.GetString(5)),
            Parse(reader.GetString(6)),
            reader.GetInt32(7));
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

    private static async Task UpsertEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string databaseName,
        string bucket,
        NormalizedEntry entry,
        string now,
        CancellationToken cancellationToken)
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
        command.Parameters.AddWithValue("$database", databaseName);
        command.Parameters.AddWithValue("$bucket", bucket);
        command.Parameters.AddWithValue("$key", entry.KeyJson);
        command.Parameters.AddWithValue("$value", entry.ValueJson);
        command.Parameters.AddWithValue(
            "$expires",
            entry.ExpiresAtUnixSeconds == 0
                ? DBNull.Value
                : Format(DateTimeOffset.FromUnixTimeSeconds(entry.ExpiresAtUnixSeconds)));
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeJson(
        JsonElement value,
        int maxBytes,
        string code,
        string message)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            throw Fail(code, message);
        }
        var json = value.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > maxBytes)
        {
            throw Fail(code, message);
        }
        return json;
    }

    private static string NormalizeRequired(
        string? value,
        int maxLength,
        string code,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Fail(code, message);
        }
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw Fail(code, message);
        }
        return normalized;
    }

    private static LegacyCacheImportException Fail(
        string code,
        string message,
        Exception? innerException = null) =>
        new(code, message, innerException);

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record NormalizedPackage(
        string SourceCommit,
        IReadOnlyList<NormalizedDatabase> Databases,
        int BucketCount,
        int EntryCount);

    private sealed record NormalizedDatabase(
        string Name,
        IReadOnlyList<NormalizedBucket> Buckets);

    private sealed record NormalizedBucket(
        string Name,
        IReadOnlyList<NormalizedEntry> Entries);

    private sealed record NormalizedEntry(
        string KeyJson,
        string ValueJson,
        long ExpiresAtUnixSeconds);
}
