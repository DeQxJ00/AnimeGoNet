using System.Globalization;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Sources;
using AnimeGoNet.Data.Serialization;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Sources;

public sealed class SourceProfileStore(AnimeGoSqliteDatabase database)
{
    public async Task EnsureSeedsAsync(
        IReadOnlyList<SourceProfileSeed> seeds,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var seed in seeds)
        {
            var fileStrategy = ToDatabaseValue(seed.FileStrategy);
            var category = SourceDownloadPolicy.NormalizeCategory(seed.Category);
            var tags = SourceDownloadPolicy.NormalizeTags(seed.Tags);
            var dynamicTagTemplate = DownloadDynamicTagTemplate.Normalize(
                seed.DynamicTagTemplate);
            var seedingTimeMinutes = SourceDownloadPolicy.ValidateSeedingTimeMinutes(
                fileStrategy, seed.SeedingTimeMinutes);
            var mikanIdentityCookie = NormalizeMikanIdentityCookie(
                seed.Adapter,
                seed.MikanIdentityCookie);
            await using var command = connection.CreateCommand();
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    allowed_torrent_hosts_json, category, tags_json, seeding_time_minutes,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc, mikan_identity_cookie,
                    dynamic_tag_template, dynamic_tag_template_initialized)
                VALUES (
                    $id, $display_name, $adapter, $downloader_id, $file_strategy,
                    $allowed_torrent_hosts_json, $category, $tags_json, $seeding_time_minutes,
                    $rss_filter_enabled, $rss_priority_enabled, 1, 1,
                    $created_at_utc, $updated_at_utc, $mikan_identity_cookie,
                    $dynamic_tag_template, 1)
                ON CONFLICT(id) DO UPDATE SET
                    allowed_torrent_hosts_json = CASE
                        WHEN source_profiles.allowed_torrent_hosts_json = '[]'
                            THEN excluded.allowed_torrent_hosts_json
                        ELSE source_profiles.allowed_torrent_hosts_json
                    END,
                    mikan_identity_cookie = COALESCE(
                        excluded.mikan_identity_cookie,
                        source_profiles.mikan_identity_cookie),
                    dynamic_tag_template = CASE
                        WHEN source_profiles.dynamic_tag_template_initialized = 0
                            THEN excluded.dynamic_tag_template
                        ELSE source_profiles.dynamic_tag_template
                    END,
                    dynamic_tag_template_initialized = 1,
                    revision = source_profiles.revision + CASE
                        WHEN source_profiles.allowed_torrent_hosts_json = '[]'
                          OR (
                              excluded.mikan_identity_cookie IS NOT NULL
                              AND excluded.mikan_identity_cookie
                                  <> COALESCE(source_profiles.mikan_identity_cookie, ''))
                          OR (
                              source_profiles.dynamic_tag_template_initialized = 0
                              AND excluded.dynamic_tag_template
                                  IS NOT source_profiles.dynamic_tag_template)
                            THEN 1
                        ELSE 0
                    END,
                    updated_at_utc = CASE
                        WHEN source_profiles.allowed_torrent_hosts_json = '[]'
                          OR (
                              excluded.mikan_identity_cookie IS NOT NULL
                              AND excluded.mikan_identity_cookie
                                  <> COALESCE(source_profiles.mikan_identity_cookie, ''))
                          OR (
                              source_profiles.dynamic_tag_template_initialized = 0
                              AND excluded.dynamic_tag_template
                                  IS NOT source_profiles.dynamic_tag_template)
                            THEN excluded.updated_at_utc
                        ELSE source_profiles.updated_at_utc
                    END
                WHERE source_profiles.allowed_torrent_hosts_json = '[]'
                   OR source_profiles.dynamic_tag_template_initialized = 0
                   OR (
                       excluded.mikan_identity_cookie IS NOT NULL
                       AND excluded.mikan_identity_cookie
                           <> COALESCE(source_profiles.mikan_identity_cookie, ''));
                """;
            command.Parameters.AddWithValue("$id", seed.Id);
            command.Parameters.AddWithValue("$display_name", seed.Id);
            command.Parameters.AddWithValue("$adapter", seed.Adapter);
            command.Parameters.AddWithValue("$downloader_id", seed.DownloaderId);
            command.Parameters.AddWithValue("$file_strategy", fileStrategy);
            command.Parameters.AddWithValue(
                "$allowed_torrent_hosts_json",
                System.Text.Json.JsonSerializer.Serialize(seed.AllowedTorrentHosts.ToArray(), DataJsonContext.Default.StringArray));
            command.Parameters.AddWithValue("$category", category);
            command.Parameters.AddWithValue(
                "$tags_json",
                System.Text.Json.JsonSerializer.Serialize(tags.ToArray(), DataJsonContext.Default.StringArray));
            command.Parameters.AddWithValue("$seeding_time_minutes", seedingTimeMinutes);
            command.Parameters.AddWithValue("$rss_filter_enabled", seed.RssFilterEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$rss_priority_enabled", seed.RssPriorityEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$created_at_utc", now);
            command.Parameters.AddWithValue("$updated_at_utc", now);
            command.Parameters.AddWithValue(
                "$mikan_identity_cookie",
                (object?)mikanIdentityCookie ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$dynamic_tag_template",
                (object?)dynamicTagTemplate ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SourceProfileRecord?> GetEnabledAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, adapter, downloader_id, file_strategy,
                   allowed_torrent_hosts_json, category, tags_json, seeding_time_minutes,
                   rss_filter_enabled, rss_priority_enabled, revision,
                   mikan_identity_cookie, dynamic_tag_template
            FROM source_profiles
            WHERE id = $id AND enabled = 1;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new SourceProfileRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            System.Text.Json.JsonSerializer.Deserialize(reader.GetString(4), DataJsonContext.Default.StringArray) ?? [],
            reader.GetString(5),
            System.Text.Json.JsonSerializer.Deserialize(reader.GetString(6), DataJsonContext.Default.StringArray) ?? [],
            reader.GetInt32(7),
            reader.GetInt64(8) != 0,
            reader.GetInt64(9) != 0,
            reader.GetInt64(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12));
    }

    public async Task<IReadOnlyList<SourceProfileAdminRecord>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = AdminSelect + " ORDER BY p.id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var records = new List<SourceProfileAdminRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) records.Add(ReadAdmin(reader));
        return records;
    }

    public async Task<SourceProfileAdminRecord?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeId(id);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = AdminSelect + " WHERE p.id = $id;";
        command.Parameters.AddWithValue("$id", normalized);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadAdmin(reader) : null;
    }

    public async Task<SourceProfileAdminRecord> CreateAsync(
        string id,
        SourceProfileDefinition definition,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeId(id);
        ArgumentNullException.ThrowIfNull(definition);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO source_profiles (
                id, display_name, adapter, downloader_id, file_strategy,
                allowed_torrent_hosts_json, category, tags_json, seeding_time_minutes,
                rss_filter_enabled, rss_priority_enabled,
                revision, enabled, created_at_utc, updated_at_utc,
                mikan_identity_cookie, dynamic_tag_template,
                dynamic_tag_template_initialized)
            VALUES ($id, $name, $adapter, $downloader, $strategy, $hosts,
                    $category, $tags, $seeding_time, $filter, $priority,
                    1, $enabled, $now, $now, $mikan_identity_cookie,
                    $dynamic_tag_template, 1);
            """;
        BindDefinition(command, normalized, definition, utcNow);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new SourceProfileDuplicateException();
        }
        return (await GetAsync(normalized, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<SourceProfileAdminRecord> UpdateAsync(
        string id,
        SourceProfileDefinition definition,
        long expectedRevision,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeId(id);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedRevision, 1);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE source_profiles
            SET display_name = $name, downloader_id = $downloader, file_strategy = $strategy,
                allowed_torrent_hosts_json = $hosts, category = $category, tags_json = $tags,
                seeding_time_minutes = $seeding_time, rss_filter_enabled = $filter,
                rss_priority_enabled = $priority, enabled = $enabled,
                mikan_identity_cookie = $mikan_identity_cookie,
                dynamic_tag_template = $dynamic_tag_template,
                revision = revision + 1, updated_at_utc = $now
            WHERE id = $id AND adapter = $adapter AND revision = $expected;
            """;
        BindDefinition(command, normalized, definition, utcNow);
        command.Parameters.AddWithValue("$expected", expectedRevision);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            if (await ExistsAsync(connection, normalized, cancellationToken).ConfigureAwait(false))
                throw new SourceProfileRevisionException();
            throw new KeyNotFoundException("Source profile was not found.");
        }
        return (await GetAsync(normalized, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task DeleteAsync(
        string id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeId(id);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedRevision, 1);
        if (normalized == "mikan")
            throw new SourceProfileConflictException("The default Mikan source profile cannot be deleted.");

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        long revision;
        long references;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT p.revision,
                       (SELECT COUNT(*) FROM ingest_tasks i WHERE i.source_profile_id = p.id)
                     + (SELECT COUNT(*) FROM mikan_rss_batches b WHERE b.source_profile_id = p.id)
                FROM source_profiles p WHERE p.id = $id;
                """;
            read.Parameters.AddWithValue("$id", normalized);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new KeyNotFoundException("Source profile was not found.");
            revision = reader.GetInt64(0);
            references = reader.GetInt64(1);
        }
        if (revision != expectedRevision) throw new SourceProfileRevisionException();
        if (references > 0)
            throw new SourceProfileConflictException($"Source profile has {references} immutable task or RSS batch reference(s).");
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM source_profiles WHERE id = $id AND revision = $revision;";
            delete.Parameters.AddWithValue("$id", normalized);
            delete.Parameters.AddWithValue("$revision", expectedRevision);
            if (await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new SourceProfileRevisionException();
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string AdminSelect = """
        SELECT p.id, p.display_name, p.adapter, p.downloader_id, p.file_strategy,
               p.allowed_torrent_hosts_json, p.category, p.tags_json, p.seeding_time_minutes,
               p.rss_filter_enabled, p.rss_priority_enabled, p.enabled, p.revision,
               (SELECT COUNT(*) FROM ingest_tasks i WHERE i.source_profile_id = p.id),
               (SELECT COUNT(*) FROM mikan_rss_batches b WHERE b.source_profile_id = p.id),
               p.created_at_utc, p.updated_at_utc, p.mikan_identity_cookie,
               p.dynamic_tag_template
        FROM source_profiles p
        """;

    private static SourceProfileAdminRecord ReadAdmin(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        System.Text.Json.JsonSerializer.Deserialize(reader.GetString(5), DataJsonContext.Default.StringArray) ?? [],
        reader.GetString(6),
        System.Text.Json.JsonSerializer.Deserialize(reader.GetString(7), DataJsonContext.Default.StringArray) ?? [],
        reader.GetInt32(8),
        reader.GetBoolean(9),
        reader.GetBoolean(10),
        reader.GetBoolean(11),
        reader.GetInt64(12),
        reader.GetInt64(13),
        reader.GetInt64(14),
        DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(16), CultureInfo.InvariantCulture),
        reader.IsDBNull(17) ? null : reader.GetString(17),
        reader.IsDBNull(18) ? null : reader.GetString(18));

    private static void BindDefinition(
        SqliteCommand command,
        string id,
        SourceProfileDefinition definition,
        DateTimeOffset utcNow)
    {
        var category = SourceDownloadPolicy.NormalizeCategory(definition.Category);
        var tags = SourceDownloadPolicy.NormalizeTags(definition.Tags);
        var dynamicTagTemplate = DownloadDynamicTagTemplate.Normalize(
            definition.DynamicTagTemplate);
        var seedingTimeMinutes = SourceDownloadPolicy.ValidateSeedingTimeMinutes(
            definition.FileStrategy, definition.SeedingTimeMinutes);
        var mikanIdentityCookie = NormalizeMikanIdentityCookie(
            definition.Adapter,
            definition.MikanIdentityCookie);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", definition.DisplayName);
        command.Parameters.AddWithValue("$adapter", definition.Adapter);
        command.Parameters.AddWithValue("$downloader", definition.DownloaderId);
        command.Parameters.AddWithValue("$strategy", definition.FileStrategy);
        command.Parameters.AddWithValue(
            "$hosts",
            System.Text.Json.JsonSerializer.Serialize(
                definition.AllowedTorrentHosts.ToArray(), DataJsonContext.Default.StringArray));
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue(
            "$tags",
            System.Text.Json.JsonSerializer.Serialize(
                tags.ToArray(), DataJsonContext.Default.StringArray));
        command.Parameters.AddWithValue(
            "$dynamic_tag_template",
            (object?)dynamicTagTemplate ?? DBNull.Value);
        command.Parameters.AddWithValue("$seeding_time", seedingTimeMinutes);
        command.Parameters.AddWithValue("$filter", definition.RssFilterEnabled);
        command.Parameters.AddWithValue("$priority", definition.RssPriorityEnabled);
        command.Parameters.AddWithValue("$enabled", definition.Enabled);
        command.Parameters.AddWithValue(
            "$mikan_identity_cookie",
            (object?)mikanIdentityCookie ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }

    private static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM source_profiles WHERE id = $id);";
        command.Parameters.AddWithValue("$id", id);
        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! == 1;
    }

    private static string NormalizeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant();
    }

    private static string ToDatabaseValue(FileStrategy strategy) => strategy switch
    {
        FileStrategy.Link => "link",
        FileStrategy.LinkDelete => "link_delete",
        FileStrategy.Move => "move",
        FileStrategy.WaitMove => "wait_move",
        _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
    };

    private static string? NormalizeMikanIdentityCookie(
        string adapter,
        string? value)
    {
        var normalized = MikanIdentityCookie.NormalizeOptional(value);
        if (normalized is not null
            && !string.Equals(adapter, "mikan", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Mikan identity Cookie can only be configured for a Mikan source profile.");
        }

        return normalized;
    }
}
