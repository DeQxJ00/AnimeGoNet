using System.Globalization;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Media;
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
            var displayName = seed.DisplayName?.Trim() ?? seed.Id;
            if (displayName.Length is < 1 or > 128)
            {
                throw new ArgumentException(
                    "Source profile display name must contain 1 to 128 characters.",
                    nameof(seeds));
            }
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
            var rssFeedUrl = SourceRssSchedulePolicy.NormalizeFeedUrl(
                seed.Adapter,
                seed.RssFeedUrl);
            var rssScheduleCron = SourceRssSchedulePolicy.NormalizeCron(
                seed.RssScheduleCron);
            var mediaType = NormalizeMediaType(seed.Adapter, seed.MediaType);
            SourceRssSchedulePolicy.ValidateEnabled(
                seed.Adapter,
                sourceEnabled: true,
                seed.RssScheduleEnabled,
                rssFeedUrl);
            if (rssFeedUrl is not null
                && !SourceRssSchedulePolicy.IsHostAllowed(
                    new Uri(rssFeedUrl, UriKind.Absolute).IdnHost,
                    seed.AllowedTorrentHosts))
            {
                throw new ArgumentException(
                    "Source profile RSS feed host must be included in allowed Torrent hosts.",
                    nameof(seeds));
            }
            await using var command = connection.CreateCommand();
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    allowed_torrent_hosts_json, category, tags_json, seeding_time_minutes,
                    rss_filter_enabled, rss_priority_enabled, duplicate_notification_enabled,
                    revision, enabled,
                    created_at_utc, updated_at_utc, mikan_identity_cookie,
                    dynamic_tag_template, dynamic_tag_template_initialized,
                    rss_feed_url, rss_schedule_enabled, rss_schedule_cron, media_type,
                    prefer_anidb_tmdb_mapping, anidb_tmdb_mapping_url_template)
                VALUES (
                    $id, $display_name, $adapter, $downloader_id, $file_strategy,
                    $allowed_torrent_hosts_json, $category, $tags_json, $seeding_time_minutes,
                    $rss_filter_enabled, $rss_priority_enabled, $duplicate_notification_enabled, 1, 1,
                    $created_at_utc, $updated_at_utc, $mikan_identity_cookie,
                    $dynamic_tag_template, 1,
                    $rss_feed_url, $rss_schedule_enabled, $rss_schedule_cron, $media_type,
                    $prefer_anidb_tmdb_mapping, $anidb_tmdb_mapping_url_template)
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
            command.Parameters.AddWithValue("$display_name", displayName);
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
            command.Parameters.AddWithValue(
                "$duplicate_notification_enabled",
                seed.DuplicateNotificationEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$created_at_utc", now);
            command.Parameters.AddWithValue("$updated_at_utc", now);
            command.Parameters.AddWithValue(
                "$mikan_identity_cookie",
                (object?)mikanIdentityCookie ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$dynamic_tag_template",
                (object?)dynamicTagTemplate ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$rss_feed_url",
                (object?)rssFeedUrl ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$rss_schedule_enabled",
                seed.RssScheduleEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$rss_schedule_cron", rssScheduleCron);
            command.Parameters.AddWithValue("$media_type", mediaType);
            command.Parameters.AddWithValue("$prefer_anidb_tmdb_mapping", seed.PreferAniDbTmdbMapping ? 1 : 0);
            command.Parameters.AddWithValue(
                "$anidb_tmdb_mapping_url_template",
                NormalizeAniDbTmdbMappingUrlTemplate(seed.AniDbTmdbMappingUrlTemplate));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ApplyDeploymentOverrideAsync(
        SourceProfileDeploymentOverride value,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var id = NormalizeId(value.Id);
        var category = value.OverrideCategory
            ? SourceDownloadPolicy.NormalizeCategory(value.Category)
            : null;
        var dynamicTagTemplate = value.OverrideDynamicTagTemplate
            ? DownloadDynamicTagTemplate.Normalize(value.DynamicTagTemplate)
            : null;
        var mikanIdentityCookie = value.OverrideMikanIdentityCookie
            ? NormalizeMikanIdentityCookie(value.Adapter, value.MikanIdentityCookie)
            : null;

        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE source_profiles
            SET category = CASE
                    WHEN $override_category = 1 THEN $category
                    ELSE category
                END,
                dynamic_tag_template = CASE
                    WHEN $override_dynamic_tag_template = 1
                        THEN $dynamic_tag_template
                    ELSE dynamic_tag_template
                END,
                mikan_identity_cookie = CASE
                    WHEN $override_mikan_identity_cookie = 1
                        THEN $mikan_identity_cookie
                    ELSE mikan_identity_cookie
                END,
                revision = revision + 1,
                updated_at_utc = $updated_at_utc
            WHERE id = $id
              AND (
                    ($override_category = 1 AND category IS NOT $category)
                 OR ($override_dynamic_tag_template = 1
                     AND dynamic_tag_template IS NOT $dynamic_tag_template)
                 OR ($override_mikan_identity_cookie = 1
                     AND mikan_identity_cookie IS NOT $mikan_identity_cookie)
              );
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$override_category", value.OverrideCategory ? 1 : 0);
        command.Parameters.AddWithValue("$category", (object?)category ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$override_dynamic_tag_template",
            value.OverrideDynamicTagTemplate ? 1 : 0);
        command.Parameters.AddWithValue(
            "$dynamic_tag_template",
            (object?)dynamicTagTemplate ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$override_mikan_identity_cookie",
            value.OverrideMikanIdentityCookie ? 1 : 0);
        command.Parameters.AddWithValue(
            "$mikan_identity_cookie",
            (object?)mikanIdentityCookie ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$updated_at_utc",
            utcNow.ToString("O", CultureInfo.InvariantCulture));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
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
                   rss_filter_enabled, rss_priority_enabled, duplicate_notification_enabled, revision,
                   mikan_identity_cookie, dynamic_tag_template,
                   rss_feed_url, rss_schedule_enabled, rss_schedule_cron, media_type,
                   prefer_anidb_tmdb_mapping, anidb_tmdb_mapping_url_template
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
            reader.GetInt64(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.GetBoolean(15),
            reader.GetString(16),
            reader.GetInt64(10) != 0,
            reader.GetString(17),
            reader.GetInt64(18) != 0,
            reader.GetString(19));
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
                rss_filter_enabled, rss_priority_enabled, duplicate_notification_enabled,
                revision, enabled, created_at_utc, updated_at_utc,
                mikan_identity_cookie, dynamic_tag_template,
                dynamic_tag_template_initialized, rss_feed_url,
                rss_schedule_enabled, rss_schedule_cron, media_type,
                prefer_anidb_tmdb_mapping, anidb_tmdb_mapping_url_template)
            VALUES ($id, $name, $adapter, $downloader, $strategy, $hosts,
                    $category, $tags, $seeding_time, $filter, $priority, $duplicate_notification,
                    1, $enabled, $now, $now, $mikan_identity_cookie,
                    $dynamic_tag_template, 1, $rss_feed_url,
                    $rss_schedule_enabled, $rss_schedule_cron, $media_type,
                    $prefer_anidb_tmdb_mapping, $anidb_tmdb_mapping_url_template);
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
                rss_priority_enabled = $priority,
                duplicate_notification_enabled = $duplicate_notification,
                enabled = $enabled,
                mikan_identity_cookie = $mikan_identity_cookie,
                dynamic_tag_template = $dynamic_tag_template,
                rss_feed_url = $rss_feed_url,
                rss_schedule_enabled = $rss_schedule_enabled,
                rss_schedule_cron = $rss_schedule_cron,
                media_type = $media_type,
                prefer_anidb_tmdb_mapping = $prefer_anidb_tmdb_mapping,
                anidb_tmdb_mapping_url_template = $anidb_tmdb_mapping_url_template,
                rss_last_run_state = 'never',
                rss_last_started_at_utc = NULL,
                rss_last_completed_at_utc = NULL,
                rss_last_failure_code = NULL,
                rss_last_batch_id = NULL,
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

    public async Task<IReadOnlyList<SourceProfileAdminRecord>> ListScheduledAsync(
        CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken).ConfigureAwait(false))
        .Where(profile => profile.Enabled && profile.RssScheduleEnabled)
        .ToArray();

    public async Task<SourceProfileRecord?> GetScheduledExecutionAsync(
        string id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedRevision, 1);
        var profile = await GetEnabledAsync(NormalizeId(id), cancellationToken).ConfigureAwait(false);
        return profile is not null
            && profile.Revision == expectedRevision
            && profile.RssScheduleEnabled
            && profile.RssFeedUrl is not null
                ? profile
                : null;
    }

    public async Task<bool> TryStartScheduledRunAsync(
        string id,
        long expectedRevision,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE source_profiles
            SET rss_last_run_state = 'running',
                rss_last_started_at_utc = $now,
                rss_last_completed_at_utc = NULL,
                rss_last_failure_code = NULL,
                rss_last_batch_id = NULL
            WHERE id = $id AND revision = $revision
              AND enabled = 1 AND rss_schedule_enabled = 1
              AND rss_feed_url IS NOT NULL
              AND rss_last_run_state <> 'running';
            """;
        command.Parameters.AddWithValue("$id", NormalizeId(id));
        command.Parameters.AddWithValue("$revision", expectedRevision);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> TryStartManualRssRunAsync(
        string id,
        long expectedRevision,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE source_profiles
            SET rss_last_run_state = 'running',
                rss_last_started_at_utc = $now,
                rss_last_completed_at_utc = NULL,
                rss_last_failure_code = NULL,
                rss_last_batch_id = NULL
            WHERE id = $id AND revision = $revision
              AND enabled = 1
              AND adapter = 'mikan'
              AND rss_feed_url IS NOT NULL
              AND rss_last_run_state <> 'running';
            """;
        command.Parameters.AddWithValue("$id", NormalizeId(id));
        command.Parameters.AddWithValue("$revision", expectedRevision);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<int> RecoverInterruptedScheduledRunsAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE source_profiles
            SET rss_last_run_state = 'failed',
                rss_last_completed_at_utc = $now,
                rss_last_failure_code = 'rss_schedule_interrupted',
                rss_last_batch_id = NULL
            WHERE rss_last_run_state = 'running';
            """;
        command.Parameters.AddWithValue("$now", Format(utcNow));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> CompleteScheduledRunAsync(
        string id,
        long expectedRevision,
        string batchId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        FinishScheduledRunAsync(
            id,
            expectedRevision,
            "succeeded",
            batchId,
            null,
            utcNow,
            cancellationToken);

    public Task<bool> FailScheduledRunAsync(
        string id,
        long expectedRevision,
        string failureCode,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (failureCode.Length > 128)
        {
            throw new ArgumentException("RSS schedule failure code must not exceed 128 characters.");
        }
        return FinishScheduledRunAsync(
            id,
            expectedRevision,
            "failed",
            null,
            failureCode,
            utcNow,
            cancellationToken);
    }

    private async Task<bool> FinishScheduledRunAsync(
        string id,
        long expectedRevision,
        string state,
        string? batchId,
        string? failureCode,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE source_profiles
            SET rss_last_run_state = $state,
                rss_last_completed_at_utc = $now,
                rss_last_failure_code = $failure,
                rss_last_batch_id = $batch
            WHERE id = $id AND revision = $revision
              AND rss_last_run_state = 'running';
            """;
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        command.Parameters.AddWithValue("$failure", (object?)failureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$batch", (object?)batchId ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", NormalizeId(id));
        command.Parameters.AddWithValue("$revision", expectedRevision);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private const string AdminSelect = """
        SELECT p.id, p.display_name, p.adapter, p.downloader_id, p.file_strategy,
               p.allowed_torrent_hosts_json, p.category, p.tags_json, p.seeding_time_minutes,
               p.rss_filter_enabled, p.rss_priority_enabled,
               p.duplicate_notification_enabled, p.enabled, p.revision,
               (SELECT COUNT(*) FROM ingest_tasks i WHERE i.source_profile_id = p.id),
               (SELECT COUNT(*) FROM mikan_rss_batches b WHERE b.source_profile_id = p.id),
               p.created_at_utc, p.updated_at_utc, p.mikan_identity_cookie,
               p.dynamic_tag_template, p.rss_feed_url, p.rss_schedule_enabled,
               p.rss_schedule_cron, p.rss_last_run_state,
               p.rss_last_started_at_utc, p.rss_last_completed_at_utc,
               p.rss_last_failure_code, p.rss_last_batch_id, p.media_type,
               p.prefer_anidb_tmdb_mapping, p.anidb_tmdb_mapping_url_template
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
        reader.GetBoolean(12),
        reader.GetInt64(13),
        reader.GetInt64(14),
        reader.GetInt64(15),
        DateTimeOffset.Parse(reader.GetString(16), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(17), CultureInfo.InvariantCulture),
        reader.IsDBNull(18) ? null : reader.GetString(18),
        reader.IsDBNull(19) ? null : reader.GetString(19),
        reader.IsDBNull(20) ? null : reader.GetString(20),
        reader.GetBoolean(21),
        reader.GetString(22),
        reader.GetString(23),
        reader.IsDBNull(24)
            ? null
            : DateTimeOffset.Parse(reader.GetString(24), CultureInfo.InvariantCulture),
        reader.IsDBNull(25)
            ? null
            : DateTimeOffset.Parse(reader.GetString(25), CultureInfo.InvariantCulture),
        reader.IsDBNull(26) ? null : reader.GetString(26),
        reader.IsDBNull(27) ? null : reader.GetString(27),
        reader.GetBoolean(11),
        reader.GetString(28),
        reader.GetInt64(29) != 0,
        reader.GetString(30));

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
        var rssFeedUrl = SourceRssSchedulePolicy.NormalizeFeedUrl(
            definition.Adapter,
            definition.RssFeedUrl);
        var rssScheduleCron = SourceRssSchedulePolicy.NormalizeCron(
            definition.RssScheduleCron);
        var mediaType = NormalizeMediaType(definition.Adapter, definition.MediaType);
        SourceRssSchedulePolicy.ValidateEnabled(
            definition.Adapter,
            definition.Enabled,
            definition.RssScheduleEnabled,
            rssFeedUrl);
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
        command.Parameters.AddWithValue(
            "$duplicate_notification",
            definition.DuplicateNotificationEnabled);
        command.Parameters.AddWithValue("$enabled", definition.Enabled);
        command.Parameters.AddWithValue(
            "$mikan_identity_cookie",
            (object?)mikanIdentityCookie ?? DBNull.Value);
        command.Parameters.AddWithValue("$rss_feed_url", (object?)rssFeedUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$rss_schedule_enabled", definition.RssScheduleEnabled);
        command.Parameters.AddWithValue("$rss_schedule_cron", rssScheduleCron);
        command.Parameters.AddWithValue("$media_type", mediaType);
        command.Parameters.AddWithValue(
            "$prefer_anidb_tmdb_mapping",
            definition.Adapter == "u2" && definition.PreferAniDbTmdbMapping ? 1 : 0);
        command.Parameters.AddWithValue(
            "$anidb_tmdb_mapping_url_template",
            NormalizeAniDbTmdbMappingUrlTemplate(definition.AniDbTmdbMappingUrlTemplate));
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

    private static string NormalizeAniDbTmdbMappingUrlTemplate(string? value)
    {
        var template = string.IsNullOrWhiteSpace(value)
            ? "https://raw.githubusercontent.com/DeQxJ00/Anime-Lists-Json/refs/heads/main/api/anidb/{anidbid}.json"
            : value.Trim();
        if (template.Length > 2048
            || !template.Contains("{anidbid}", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(template.Replace("{anidbid}", "1", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "anidb_tmdb_mapping_url_template must be an absolute HTTP(S) URL containing {anidbid}.");
        }
        return template;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

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

    private static string NormalizeMediaType(string adapter, string? value)
    {
        if (!MediaTypes.TryNormalize(value, out var normalized))
        {
            throw new ArgumentException("Source profile media type must be tv or movie.");
        }
        if (normalized == MediaTypes.Movie
            && !string.Equals(adapter, "mikan", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Movie media type can only be configured for a Mikan source profile.");
        }
        return normalized;
    }
}
