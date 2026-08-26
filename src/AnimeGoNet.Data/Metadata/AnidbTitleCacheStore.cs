using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Metadata;

public sealed record AnidbTitleCacheStatus(
    string SourceUrl,
    string? ETag,
    string? LastModified,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? DownloadedAtUtc,
    DateTimeOffset? ImportedAtUtc,
    DateTimeOffset? NextCheckAtUtc,
    long AnimeCount,
    long TitleCount,
    long SourceSizeBytes,
    int RefreshIntervalHours,
    string LastStatus,
    string? LastFailureCode);

public sealed record AnidbTitleCacheEntry(
    int Aid,
    string Language,
    string TitleType,
    string Title);

public sealed record AnidbTitleCachePage(
    int Page,
    int PageSize,
    long TotalItems,
    string? Query,
    int? Aid,
    IReadOnlyList<AnidbTitleCacheEntry> Items);

public sealed record AnidbTitleImportResult(long AnimeCount, long TitleCount);

public sealed class AnidbTitleCacheStore(AnimeGoSqliteDatabase database)
{
    public async Task<AnidbTitleCacheStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_url, etag, last_modified, last_attempt_at_utc,
                   downloaded_at_utc, imported_at_utc, next_check_at_utc,
                   anime_count, title_count, source_size_bytes,
                   refresh_interval_hours, last_status, last_failure_code
            FROM anidb_title_cache_state
            WHERE singleton = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("AniDB title cache state is missing.");
        }

        return new AnidbTitleCacheStatus(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            ReadDate(reader, 3),
            ReadDate(reader, 4),
            ReadDate(reader, 5),
            ReadDate(reader, 6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetInt64(9),
            reader.GetInt32(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12));
    }

    public async Task<AnidbTitleCacheStatus> SetRefreshIntervalHoursAsync(
        int hours,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        if (hours is < 1 or > 720)
        {
            throw new ArgumentOutOfRangeException(nameof(hours), "AniDB cache interval must be between 1 and 720 hours.");
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE anidb_title_cache_state
            SET refresh_interval_hours = $hours,
                next_check_at_utc = CASE
                    WHEN last_status IN ('completed', 'not_modified')
                        THEN $now
                    ELSE next_check_at_utc
                END
            WHERE singleton = 1;
            """;
        command.Parameters.AddWithValue("$hours", hours);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("AniDB title cache state is missing.");
        }

        return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkCheckingAsync(
        string sourceUrl,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE anidb_title_cache_state
            SET source_url = $source_url,
                last_attempt_at_utc = $now,
                last_status = 'checking',
                last_failure_code = NULL
            WHERE singleton = 1;
            """;
        command.Parameters.AddWithValue("$source_url", sourceUrl);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkNotModifiedAsync(
        DateTimeOffset utcNow,
        DateTimeOffset nextCheckAtUtc,
        CancellationToken cancellationToken = default)
    {
        await UpdateOutcomeAsync(
            "not_modified", null, utcNow, nextCheckAtUtc, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(
        string failureCode,
        DateTimeOffset utcNow,
        DateTimeOffset nextCheckAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        await UpdateOutcomeAsync(
            "failed", failureCode, utcNow, nextCheckAtUtc, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AnidbTitleImportResult> ImportGzipAsync(
        Stream gzipStream,
        string sourceUrl,
        string? etag,
        string? lastModified,
        long sourceSizeBytes,
        DateTimeOffset utcNow,
        DateTimeOffset nextCheckAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gzipStream);
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM anidb_titles;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        long animeCount = 0;
        long titleCount = 0;
        using var gzip = new GZipStream(gzipStream, CompressionMode.Decompress, leaveOpen: true);
        using var xml = XmlReader.Create(gzip, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        });
        while (await xml.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (xml.NodeType != XmlNodeType.Element || xml.Name != "anime")
            {
                continue;
            }
            if (!int.TryParse(xml.GetAttribute("aid"), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var currentAid) || currentAid <= 0)
            {
                throw new InvalidDataException("AniDB title archive contains an invalid aid.");
            }
            animeCount++;
            using var anime = xml.ReadSubtree();
            await anime.ReadAsync().ConfigureAwait(false);
            while (!anime.EOF)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (anime.NodeType != XmlNodeType.Element || anime.Name != "title")
                {
                    await anime.ReadAsync().ConfigureAwait(false);
                    continue;
                }
                var language = anime.GetAttribute("lang", "http://www.w3.org/XML/1998/namespace")
                    ?? anime.GetAttribute("xml:lang") ?? "und";
                var titleType = anime.GetAttribute("type") ?? "unknown";
                var title = (await anime.ReadElementContentAsStringAsync().ConfigureAwait(false)).Trim();
                if (title.Length == 0) continue;
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT OR IGNORE INTO anidb_titles (
                        aid, language, title_type, title, normalized_title)
                    VALUES ($aid, $language, $title_type, $title, $normalized_title);
                    """;
                insert.Parameters.AddWithValue("$aid", currentAid);
                insert.Parameters.AddWithValue("$language", language);
                insert.Parameters.AddWithValue("$title_type", titleType);
                insert.Parameters.AddWithValue("$title", title);
                insert.Parameters.AddWithValue("$normalized_title", Normalize(title));
                titleCount += await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        if (animeCount == 0 || titleCount == 0)
        {
            throw new InvalidDataException("AniDB title archive did not contain any titles.");
        }

        await using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = """
                UPDATE anidb_title_cache_state
                SET source_url = $source_url,
                    etag = $etag,
                    last_modified = $last_modified,
                    downloaded_at_utc = $now,
                    imported_at_utc = $now,
                    next_check_at_utc = $next_check,
                    anime_count = $anime_count,
                    title_count = $title_count,
                    source_size_bytes = $source_size_bytes,
                    last_status = 'completed',
                    last_failure_code = NULL
                WHERE singleton = 1;
                """;
            state.Parameters.AddWithValue("$source_url", sourceUrl);
            state.Parameters.AddWithValue("$etag", (object?)etag ?? DBNull.Value);
            state.Parameters.AddWithValue("$last_modified", (object?)lastModified ?? DBNull.Value);
            state.Parameters.AddWithValue("$now", Format(utcNow));
            state.Parameters.AddWithValue("$next_check", Format(nextCheckAtUtc));
            state.Parameters.AddWithValue("$anime_count", animeCount);
            state.Parameters.AddWithValue("$title_count", titleCount);
            state.Parameters.AddWithValue("$source_size_bytes", sourceSizeBytes);
            await state.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AnidbTitleImportResult(animeCount, titleCount);
    }

    public async Task<AnidbTitleCachePage> ListAsync(
        int page,
        int pageSize,
        string? query,
        int? aid,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (aid is <= 0) throw new ArgumentOutOfRangeException(nameof(aid));
        query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var normalized = query is null ? null : Normalize(query);
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        var where = aid is not null
            ? "WHERE aid = $aid"
            : normalized is not null
                ? "WHERE normalized_title LIKE '%' || $query || '%'"
                : string.Empty;
        long total;
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = $"SELECT COUNT(*) FROM anidb_titles {where};";
            AddFilters(count, aid, normalized);
            total = (long)(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
        }
        var items = new List<AnidbTitleCacheEntry>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT aid, language, title_type, title
                FROM anidb_titles
                {where}
                ORDER BY aid, CASE title_type WHEN 'main' THEN 0 WHEN 'official' THEN 1 ELSE 2 END,
                         language, title
                LIMIT $limit OFFSET $offset;
                """;
            AddFilters(command, aid, normalized);
            command.Parameters.AddWithValue("$limit", pageSize);
            command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new AnidbTitleCacheEntry(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }
        return new AnidbTitleCachePage(page, pageSize, total, query, aid, items);
    }

    public async Task<IReadOnlyList<string>> GetPreferredTitlesAsync(
        int aid,
        int limit = 32,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(aid);
        if (limit is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT title
            FROM anidb_titles
            WHERE aid = $aid
            ORDER BY CASE title_type
                WHEN 'official' THEN 0
                WHEN 'main' THEN 1
                WHEN 'synonym' THEN 2
                ELSE 3
            END,
            CASE language WHEN 'x-jat' THEN 0 WHEN 'ja' THEN 1 ELSE 2 END,
            title;
            """;
        command.Parameters.AddWithValue("$aid", aid);
        var titles = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (titles.Count < limit && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            titles.Add(reader.GetString(0));
        }
        return titles;
    }

    private async Task UpdateOutcomeAsync(
        string status,
        string? failureCode,
        DateTimeOffset utcNow,
        DateTimeOffset nextCheckAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE anidb_title_cache_state
            SET last_status = $status,
                last_failure_code = $failure_code,
                next_check_at_utc = $next_check,
                last_attempt_at_utc = COALESCE(last_attempt_at_utc, $now)
            WHERE singleton = 1;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$failure_code", (object?)failureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$next_check", Format(nextCheckAtUtc));
        command.Parameters.AddWithValue("$now", Format(utcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddFilters(SqliteCommand command, int? aid, string? normalized)
    {
        if (aid is not null) command.Parameters.AddWithValue("$aid", aid.Value);
        else if (normalized is not null) command.Parameters.AddWithValue("$query", normalized);
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static DateTimeOffset? ReadDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
