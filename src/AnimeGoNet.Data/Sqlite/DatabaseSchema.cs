namespace AnimeGoNet.Data.Sqlite;

public static class DatabaseSchema
{
    public const int CurrentVersion = 64;

    internal static IReadOnlyList<SchemaMigration> Migrations { get; } =
    [
        new SchemaMigration(1, "initial_business_schema", InitialBusinessSchema),
        new SchemaMigration(2, "source_torrent_host_allowlist", SourceTorrentHostAllowlist),
        new SchemaMigration(3, "staged_ingest_lifecycle", StagedIngestLifecycle),
        new SchemaMigration(4, "staged_dispatch_lease", StagedDispatchLease),
        new SchemaMigration(5, "download_runtime_projection", DownloadRuntimeProjection),
        new SchemaMigration(6, "metadata_resolution_lease", MetadataResolutionLease),
        new SchemaMigration(7, "metadata_resolution_stages", MetadataResolutionStages),
        new SchemaMigration(8, "download_file_preparation", DownloadFilePreparation),
        new SchemaMigration(9, "download_path_snapshot", DownloadPathSnapshot),
        new SchemaMigration(10, "media_organization_lease", MediaOrganizationLease),
        new SchemaMigration(11, "subtitle_episode_association", SubtitleEpisodeAssociation),
        new SchemaMigration(12, "auditable_delete_plans", AuditableDeletePlans),
        new SchemaMigration(13, "mikan_rss_rule_storage", MikanRssRuleStorage),
        new SchemaMigration(14, "mikan_rss_batch_audit", MikanRssBatchAudit),
        new SchemaMigration(15, "legacy_mikan_filter_storage", LegacyMikanFilterStorage),
        new SchemaMigration(16, "mikan_legacy_filter_audit", MikanLegacyFilterAudit),
        new SchemaMigration(17, "source_download_policy", SourceDownloadPolicy),
        new SchemaMigration(18, "enable_all_file_strategies", EnableAllFileStrategies),
        new SchemaMigration(19, "mikan_publication_evidence", MikanPublicationEvidence),
        new SchemaMigration(20, "pending_tmdb_recovery", PendingTmdbRecovery),
        new SchemaMigration(21, "pending_tmdb_nfo_rewrite_jobs", PendingTmdbNfoRewriteJobs),
        new SchemaMigration(22, "sqlite_json_cache", SqliteJsonCache),
        new SchemaMigration(23, "library_tmdb_projection", LibraryTmdbProjection),
        new SchemaMigration(24, "download_job_audit_events", DownloadJobAuditEvents),
        new SchemaMigration(25, "mikan_rss_rule_snapshots", MikanRssRuleSnapshots),
        new SchemaMigration(26, "mikan_bangumi_discovery_audit", MikanBangumiDiscoveryAudit),
        new SchemaMigration(27, "directory_database_index", DirectoryDatabaseIndex),
        new SchemaMigration(28, "animegonet_data_versions", AnimeGoNetDataVersions),
        new SchemaMigration(29, "data_update_transfer_audit", DataUpdateTransferAudit),
        new SchemaMigration(30, "library_metadata_audit_indexes", LibraryMetadataAuditIndexes),
        new SchemaMigration(31, "source_mikan_identity_cookie", SourceMikanIdentityCookie),
        new SchemaMigration(32, "tmdb_resolution_evidence", TmdbResolutionEvidence),
        new SchemaMigration(33, "download_seeding_lifecycle", DownloadSeedingLifecycle),
        new SchemaMigration(34, "dynamic_download_tags", DynamicDownloadTags),
        new SchemaMigration(35, "completion_source_alias_audit", CompletionSourceAliasAudit),
        new SchemaMigration(36, "source_rss_scheduling", SourceRssScheduling),
        new SchemaMigration(37, "media_organization_progress", MediaOrganizationProgress),
        new SchemaMigration(38, "source_duplicate_notifications", SourceDuplicateNotifications),
        new SchemaMigration(39, "legacy_cache_import_audit", LegacyCacheImportAudit),
        new SchemaMigration(40, "ai_metadata_usage_audit", AiMetadataUsageAudit),
        new SchemaMigration(
            41,
            "tmdb_episode_date_evidence",
            TmdbEpisodeBangumiDateEvidence),
        new SchemaMigration(
            42,
            "bangumi_archive_subject_relations",
            BangumiArchiveSubjectRelations),
        new SchemaMigration(
            43,
            "tmdb_episode_nearest_date_evidence",
            TmdbEpisodeNearestDateEvidence),
        new SchemaMigration(
            44,
            "recover_completed_metadata_organization",
            RecoverCompletedMetadataOrganization),
        new SchemaMigration(
            45,
            "bangumi_archive_usage_audit",
            BangumiArchiveUsageAudit),
        new SchemaMigration(
            46,
            "bangumi_archive_usage_events",
            BangumiArchiveUsageEvents),
        new SchemaMigration(
            47,
            "other_file_readaptation",
            OtherFileReadaptation),
        new SchemaMigration(
            48,
            "fresh_readaptation_review_and_task_delete",
            FreshReadaptationReviewAndTaskDelete),
        new SchemaMigration(
            49,
            "readaptation_review_comparison",
            ReadaptationReviewComparison),
        new SchemaMigration(
            50,
            "readaptation_manual_tmdb_override",
            ReadaptationManualTmdbOverride),
        new SchemaMigration(
            51,
            "configurable_trusted_offset_threshold",
            ConfigurableTrustedOffsetThreshold),
        new SchemaMigration(
            52,
            "ai_validated_episode_audit",
            AiValidatedEpisodeAudit),
        new SchemaMigration(
            53,
            "trusted_offset_blacklist",
            TrustedOffsetBlacklist),
        new SchemaMigration(
            54,
            "webhook_notifications",
            WebhookNotifications),
        new SchemaMigration(
            55,
            "ai_invocation_trigger_reason",
            AiInvocationTriggerReason),
        new SchemaMigration(
            56,
            "task_media_type",
            TaskMediaType),
        new SchemaMigration(
            57,
            "movie_metadata_identity",
            MovieMetadataIdentity),
        new SchemaMigration(
            58,
            "source_profile_media_type",
            SourceProfileMediaType),
        new SchemaMigration(
            59,
            "movie_file_disposition",
            MovieFileDisposition,
            RequiresForeignKeysDisabled: true),
        new SchemaMigration(
            60,
            "ai_series_change_review",
            AiSeriesChangeReview),
        new SchemaMigration(
            61,
            "mikan_manual_series_mapping",
            MikanManualSeriesMapping),
        new SchemaMigration(
            62,
            "mikan_plugin_call_audit",
            MikanPluginCallAudit),
        new SchemaMigration(
            63,
            "mikan_publish_group_directory",
            MikanPublishGroupDirectory),
        new SchemaMigration(
            64,
            "mikan_plugin_call_item_title",
            MikanPluginCallItemTitle),
    ];

    private const string MikanPluginCallItemTitle = """
        ALTER TABLE mikan_plugin_call_log_items
        ADD COLUMN title TEXT NULL
            CHECK (title IS NULL OR length(title) BETWEEN 1 AND 1000);
        """;

    private const string MikanPublishGroupDirectory = """
        CREATE TABLE mikan_publish_groups (
            groupid INTEGER NOT NULL PRIMARY KEY CHECK (groupid > 0),
            group_name TEXT NULL,
            name_source TEXT NOT NULL CHECK (name_source IN ('automatic', 'manual')),
            source_profile_id TEXT NULL,
            state TEXT NOT NULL CHECK (state IN ('pending', 'resolved', 'failed')),
            failure_code TEXT NULL,
            fetched_at_utc TEXT NULL,
            next_attempt_at_utc TEXT NULL,
            updated_at_utc TEXT NOT NULL,
            revision INTEGER NOT NULL CHECK (revision > 0)
        ) STRICT;

        CREATE INDEX ix_mikan_publish_groups_retry
        ON mikan_publish_groups(state, next_attempt_at_utc, updated_at_utc);
        """;

    private const string MikanPluginCallAudit = """
        CREATE TABLE mikan_plugin_call_logs (
            id TEXT NOT NULL PRIMARY KEY,
            endpoint TEXT NOT NULL,
            mode TEXT NOT NULL CHECK (mode IN ('single', 'all', 'selected', 'batch')),
            media_type TEXT NOT NULL CHECK (media_type IN ('tv', 'movie', 'mixed')),
            result TEXT NOT NULL CHECK (result IN ('success', 'partial', 'failed')),
            requested_count INTEGER NOT NULL CHECK (requested_count >= 0),
            accepted_count INTEGER NOT NULL CHECK (accepted_count >= 0),
            rejected_count INTEGER NOT NULL CHECK (rejected_count >= 0),
            failure_code TEXT NULL,
            duration_ms INTEGER NOT NULL CHECK (duration_ms >= 0),
            started_at_utc TEXT NOT NULL,
            completed_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE INDEX ix_mikan_plugin_call_logs_completed
        ON mikan_plugin_call_logs(completed_at_utc DESC);

        CREATE TABLE mikan_plugin_call_log_items (
            call_id TEXT NOT NULL,
            item_index INTEGER NOT NULL CHECK (item_index >= 0),
            task_id TEXT NULL,
            mikanid INTEGER NULL CHECK (mikanid > 0),
            groupid INTEGER NULL CHECK (groupid > 0),
            status TEXT NOT NULL,
            failure_code TEXT NULL,
            PRIMARY KEY (call_id, item_index),
            FOREIGN KEY (call_id) REFERENCES mikan_plugin_call_logs(id) ON DELETE CASCADE
        ) STRICT;
        """;

    private const string MikanManualSeriesMapping = """
        ALTER TABLE ai_series_change_reviews ADD COLUMN mikanid INTEGER NULL CHECK (mikanid > 0);
        ALTER TABLE ai_series_change_reviews ADD COLUMN groupid INTEGER NULL CHECK (groupid > 0);

        CREATE TABLE mikan_manual_series_mappings (
            mikanid INTEGER NOT NULL CHECK (mikanid > 0),
            groupid INTEGER NOT NULL CHECK (groupid > 0),
            expected_tmdb_series_id INTEGER NOT NULL CHECK (expected_tmdb_series_id > 0),
            tmdb_series_id INTEGER NOT NULL CHECK (tmdb_series_id > 0),
            tmdb_season_number INTEGER NOT NULL CHECK (tmdb_season_number > 0),
            accepted_from_task_id TEXT NOT NULL,
            accepted_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (mikanid, groupid)
        ) STRICT;

        CREATE INDEX ix_mikan_manual_series_mappings_target
        ON mikan_manual_series_mappings(tmdb_series_id, tmdb_season_number);
        """;

    private const string AiSeriesChangeReview = """
        CREATE TABLE ai_series_change_reviews (
            id TEXT NOT NULL PRIMARY KEY,
            task_id TEXT NOT NULL REFERENCES ingest_tasks(id) ON DELETE CASCADE,
            task_file_id TEXT NOT NULL REFERENCES task_files(id) ON DELETE CASCADE,
            state TEXT NOT NULL CHECK (state IN ('pending', 'accepted', 'rejected')),
            expected_tmdb_series_id INTEGER NOT NULL CHECK (expected_tmdb_series_id > 0),
            expected_tmdb_season_number INTEGER NOT NULL CHECK (expected_tmdb_season_number > 0),
            proposed_tmdb_series_id INTEGER NOT NULL CHECK (proposed_tmdb_series_id > 0),
            proposed_series_name TEXT NOT NULL,
            proposed_original_name TEXT NOT NULL,
            proposed_series_first_air_date TEXT,
            proposed_series_poster_path TEXT,
            proposed_tmdb_season_id INTEGER NOT NULL CHECK (proposed_tmdb_season_id > 0),
            proposed_tmdb_season_number INTEGER NOT NULL CHECK (proposed_tmdb_season_number > 0),
            proposed_season_name TEXT NOT NULL,
            proposed_season_air_date TEXT,
            proposed_season_episode_count INTEGER NOT NULL CHECK (proposed_season_episode_count >= 0),
            proposed_season_poster_path TEXT,
            proposed_tmdb_episode_id INTEGER NOT NULL CHECK (proposed_tmdb_episode_id > 0),
            proposed_tmdb_episode_number INTEGER NOT NULL CHECK (proposed_tmdb_episode_number > 0),
            proposed_episode_name TEXT NOT NULL,
            proposed_episode_air_date TEXT,
            requested_at_utc TEXT NOT NULL,
            reviewed_at_utc TEXT,
            UNIQUE(task_id, task_file_id)
        ) STRICT;

        CREATE INDEX ix_ai_series_change_reviews_task_state
        ON ai_series_change_reviews(task_id, state, requested_at_utc DESC);
        """;

    private const string MovieFileDisposition = """
        ALTER TABLE task_files RENAME TO task_files_v58;

        CREATE TABLE task_files (
            id TEXT NOT NULL PRIMARY KEY,
            task_id TEXT NOT NULL REFERENCES ingest_tasks(id) ON DELETE CASCADE,
            relative_path TEXT NOT NULL,
            size_bytes INTEGER NOT NULL CHECK (size_bytes >= 0),
            source_episode TEXT,
            file_episode_candidate TEXT,
            tmdb_series_id INTEGER CHECK (tmdb_series_id > 0),
            tmdb_season_number INTEGER CHECK (tmdb_season_number > 0),
            tmdb_episode_number INTEGER CHECK (tmdb_episode_number > 0),
            tmdb_episode_id INTEGER CHECK (tmdb_episode_id > 0),
            disposition TEXT NOT NULL CHECK (disposition IN (
                'pending', 'episode', 'movie', 'other', 'ignored', 'duplicate')),
            other_reason TEXT,
            download_file_index INTEGER CHECK (download_file_index >= 0),
            download_priority INTEGER CHECK (download_priority BETWEEN 0 AND 7),
            download_wanted INTEGER CHECK (download_wanted IN (0, 1)),
            associated_task_file_id TEXT REFERENCES task_files(id) ON DELETE SET NULL,
            rename_suffix TEXT,
            episode_resolution_source TEXT,
            episode_resolution_run_id TEXT,
            episode_resolution_attempt_id TEXT,
            tmdb_movie_id INTEGER CHECK (tmdb_movie_id > 0)
                REFERENCES anime_movies(tmdb_movie_id),
            UNIQUE (task_id, relative_path)
        ) STRICT;

        INSERT INTO task_files (
            id, task_id, relative_path, size_bytes, source_episode,
            file_episode_candidate, tmdb_series_id, tmdb_season_number,
            tmdb_episode_number, tmdb_episode_id, disposition, other_reason,
            download_file_index, download_priority, download_wanted,
            associated_task_file_id, rename_suffix, episode_resolution_source,
            episode_resolution_run_id, episode_resolution_attempt_id,
            tmdb_movie_id)
        SELECT
            file.id, file.task_id, file.relative_path, file.size_bytes,
            file.source_episode, file.file_episode_candidate, file.tmdb_series_id,
            file.tmdb_season_number, file.tmdb_episode_number, file.tmdb_episode_id,
            CASE
                WHEN task.media_type = 'movie'
                 AND file.disposition = 'other'
                 AND file.other_reason IN ('movie', 'movie_subtitle')
                    THEN 'movie'
                ELSE file.disposition
            END,
            CASE
                WHEN task.media_type = 'movie'
                 AND file.disposition = 'other'
                 AND file.other_reason IN ('movie', 'movie_subtitle')
                    THEN NULL
                ELSE file.other_reason
            END,
            file.download_file_index, file.download_priority, file.download_wanted,
            file.associated_task_file_id, file.rename_suffix,
            file.episode_resolution_source, file.episode_resolution_run_id,
            file.episode_resolution_attempt_id, file.tmdb_movie_id
        FROM task_files_v58 AS file
        JOIN ingest_tasks AS task ON task.id = file.task_id;

        DROP TABLE task_files_v58;

        CREATE INDEX ix_task_files_associated
        ON task_files(associated_task_file_id);

        CREATE INDEX ix_task_files_tmdb_season_task
        ON task_files(tmdb_series_id, tmdb_season_number, task_id);

        CREATE INDEX ix_task_files_episode_evidence
        ON task_files(task_id, episode_resolution_source, episode_resolution_run_id);

        CREATE INDEX ix_task_files_tmdb_movie
        ON task_files(tmdb_movie_id, task_id);

        CREATE TRIGGER tr_task_files_episode_evidence_insert
        BEFORE INSERT ON task_files
        WHEN NOT (
            (
                NEW.episode_resolution_source IS NULL
                AND NEW.episode_resolution_run_id IS NULL
                AND NEW.episode_resolution_attempt_id IS NULL
            )
            OR (
                NEW.episode_resolution_source IN (
                    'manual_mikan_offset', 'trusted_mikan_offset',
                    'ai_metadata', 'tmdb_episode_number',
                    'tmdb_episode_bangumi_date',
                    'tmdb_episode_bangumi_nearest_date',
                    'subtitle_association')
                AND NEW.episode_resolution_run_id IS NOT NULL
                AND NEW.episode_resolution_attempt_id IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run
                      ON run.id = attempt.run_id
                    WHERE attempt.id = NEW.episode_resolution_attempt_id
                      AND attempt.run_id = NEW.episode_resolution_run_id
                      AND run.task_id = NEW.task_id
                      AND attempt.stage = 'episode'
                      AND attempt.strategy = NEW.episode_resolution_source
                      AND attempt.result = 'matched')
            )
        )
        BEGIN
            SELECT RAISE(ABORT, 'invalid TMDB Episode resolution evidence');
        END;

        CREATE TRIGGER tr_task_files_episode_evidence_update
        BEFORE UPDATE OF
            episode_resolution_source, episode_resolution_run_id,
            episode_resolution_attempt_id
        ON task_files
        WHEN NOT (
            (
                NEW.episode_resolution_source IS NULL
                AND NEW.episode_resolution_run_id IS NULL
                AND NEW.episode_resolution_attempt_id IS NULL
            )
            OR (
                NEW.episode_resolution_source IN (
                    'manual_mikan_offset', 'trusted_mikan_offset',
                    'ai_metadata', 'tmdb_episode_number',
                    'tmdb_episode_bangumi_date',
                    'tmdb_episode_bangumi_nearest_date',
                    'subtitle_association')
                AND NEW.episode_resolution_run_id IS NOT NULL
                AND NEW.episode_resolution_attempt_id IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run
                      ON run.id = attempt.run_id
                    WHERE attempt.id = NEW.episode_resolution_attempt_id
                      AND attempt.run_id = NEW.episode_resolution_run_id
                      AND run.task_id = NEW.task_id
                      AND attempt.stage = 'episode'
                      AND attempt.strategy = NEW.episode_resolution_source
                      AND attempt.result = 'matched')
            )
        )
        BEGIN
            SELECT RAISE(ABORT, 'invalid TMDB Episode resolution evidence');
        END;
        """;

    private const string SourceProfileMediaType = """
        ALTER TABLE source_profiles
        ADD COLUMN media_type TEXT NOT NULL DEFAULT 'tv'
            CHECK (media_type IN ('tv', 'movie'));
        """;

    private const string MovieMetadataIdentity = """
        CREATE TABLE anime_movies (
            id TEXT NOT NULL PRIMARY KEY,
            tmdb_movie_id INTEGER NOT NULL UNIQUE CHECK (tmdb_movie_id > 0),
            canonical_title TEXT NOT NULL,
            original_title TEXT NOT NULL,
            release_date TEXT,
            poster_path TEXT,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        ) STRICT;

        ALTER TABLE task_files
        ADD COLUMN tmdb_movie_id INTEGER CHECK (tmdb_movie_id > 0)
            REFERENCES anime_movies(tmdb_movie_id);

        ALTER TABLE metadata_resolution_runs
        ADD COLUMN tmdb_movie_id INTEGER CHECK (tmdb_movie_id > 0);

        CREATE TABLE movie_claims (
            id TEXT NOT NULL PRIMARY KEY,
            tmdb_movie_id INTEGER NOT NULL CHECK (tmdb_movie_id > 0),
            task_file_id TEXT NOT NULL REFERENCES task_files(id) ON DELETE CASCADE,
            state TEXT NOT NULL CHECK (state IN ('active', 'completed', 'released')),
            claimed_at_utc TEXT NOT NULL,
            expires_at_utc TEXT,
            UNIQUE (tmdb_movie_id)
        ) STRICT;

        CREATE TABLE movie_completion_records (
            id TEXT NOT NULL PRIMARY KEY,
            tmdb_movie_id INTEGER NOT NULL CHECK (tmdb_movie_id > 0),
            source_id TEXT NOT NULL,
            source_item_id TEXT,
            media_path TEXT,
            completed_at_utc TEXT NOT NULL,
            UNIQUE (tmdb_movie_id)
        ) STRICT;

        CREATE INDEX ix_task_files_tmdb_movie
        ON task_files(tmdb_movie_id, task_id);

        CREATE INDEX ix_movie_completion_source
        ON movie_completion_records(source_id, source_item_id);
        """;

    private const string TaskMediaType = """
        ALTER TABLE ingest_tasks
        ADD COLUMN media_type TEXT NOT NULL DEFAULT 'tv'
            CHECK (media_type IN ('tv', 'movie'));

        CREATE INDEX ix_ingest_tasks_media_type_status
        ON ingest_tasks(media_type, status, created_at_utc DESC);
        """;

    private const string AiInvocationTriggerReason = """
        ALTER TABLE metadata_resolution_attempts
        ADD COLUMN ai_trigger_reason TEXT CHECK (
            ai_trigger_reason IS NULL
            OR length(ai_trigger_reason) BETWEEN 1 AND 1024);
        """;

    private const string WebhookNotifications = """
        CREATE TABLE notification_channels (
            id TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL CHECK (length(name) BETWEEN 1 AND 100),
            provider TEXT NOT NULL CHECK (provider IN (
                'bark', 'generic', 'discord', 'slack',
                'telegram', 'serverchan', 'pushplus')),
            enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
            endpoint_url TEXT NOT NULL CHECK (length(endpoint_url) BETWEEN 8 AND 2048),
            secret TEXT CHECK (secret IS NULL OR length(secret) BETWEEN 1 AND 4096),
            target TEXT CHECK (target IS NULL OR length(target) BETWEEN 1 AND 512),
            options_json TEXT NOT NULL CHECK (json_valid(options_json)),
            events_json TEXT NOT NULL CHECK (json_valid(events_json)),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE notification_events (
            id TEXT NOT NULL PRIMARY KEY,
            event_type TEXT NOT NULL CHECK (event_type IN (
                'metadata_failed', 'metadata_other', 'download_failed',
                'download_completed', 'organization_completed',
                'review_required', 'test')),
            task_id TEXT,
            title TEXT NOT NULL,
            body TEXT NOT NULL,
            payload_json TEXT NOT NULL CHECK (json_valid(payload_json)),
            state TEXT NOT NULL CHECK (state IN ('pending', 'processing', 'completed')),
            lease_expires_at_utc TEXT,
            created_at_utc TEXT NOT NULL,
            completed_at_utc TEXT
        ) STRICT;

        CREATE INDEX ix_notification_events_pending
        ON notification_events(state, created_at_utc);

        CREATE TABLE notification_deliveries (
            id TEXT NOT NULL PRIMARY KEY,
            event_id TEXT REFERENCES notification_events(id) ON DELETE SET NULL,
            channel_id TEXT REFERENCES notification_channels(id) ON DELETE SET NULL,
            channel_name TEXT NOT NULL,
            provider TEXT NOT NULL,
            event_type TEXT NOT NULL,
            task_id TEXT,
            title TEXT NOT NULL,
            state TEXT NOT NULL CHECK (state IN ('succeeded', 'failed', 'skipped')),
            http_status INTEGER,
            failure_code TEXT,
            response_excerpt TEXT,
            duration_ms INTEGER NOT NULL CHECK (duration_ms >= 0),
            created_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE INDEX ix_notification_deliveries_created
        ON notification_deliveries(created_at_utc DESC);

        CREATE TRIGGER tr_notification_ingest_status
        AFTER UPDATE OF status, readaptation_review_state ON ingest_tasks
        BEGIN
            INSERT INTO notification_events (
                id, event_type, task_id, title, body, payload_json,
                state, created_at_utc)
            SELECT lower(hex(randomblob(16))),
                   CASE NEW.status
                     WHEN 'metadata_failed' THEN 'metadata_failed'
                     WHEN 'download_error' THEN 'download_failed'
                     WHEN 'downloaded' THEN 'download_completed'
                     WHEN 'organized' THEN 'organization_completed'
                   END,
                   NEW.id, NEW.title,
                   CASE NEW.status
                     WHEN 'metadata_failed' THEN '元数据匹配失败：' || COALESCE(NEW.failure_reason, '未提供原因')
                     WHEN 'download_error' THEN '下载失败：' || COALESCE(NEW.failure_reason, '未提供原因')
                     WHEN 'downloaded' THEN '下载已完成，等待或正在整理媒体文件。'
                     WHEN 'organized' THEN '媒体文件已经整理完成。'
                   END,
                   json_object('status', NEW.status,
                               'failure_kind', NEW.failure_kind,
                               'failure_reason', NEW.failure_reason),
                   'pending', NEW.updated_at_utc
            WHERE NEW.status <> OLD.status
              AND NEW.status IN ('metadata_failed', 'download_error', 'downloaded', 'organized');

            INSERT INTO notification_events (
                id, event_type, task_id, title, body, payload_json,
                state, created_at_utc)
            SELECT lower(hex(randomblob(16))), 'metadata_other', NEW.id, NEW.title,
                   '元数据处理完成，但存在需要检查的 Other 文件。',
                   json_object('other_count', (
                       SELECT COUNT(*) FROM task_files
                       WHERE task_id = NEW.id AND disposition = 'other')),
                   'pending', NEW.updated_at_utc
            WHERE NEW.status <> OLD.status
              AND NEW.status IN ('download_preparing', 'downloaded')
              AND EXISTS (
                  SELECT 1 FROM task_files
                  WHERE task_id = NEW.id AND disposition = 'other');

            INSERT INTO notification_events (
                id, event_type, task_id, title, body, payload_json,
                state, created_at_utc)
            SELECT lower(hex(randomblob(16))), 'review_required', NEW.id, NEW.title,
                   'Other 重新适配已经完成，等待人工审核。', '{}',
                   'pending', NEW.updated_at_utc
            WHERE NEW.readaptation_review_state = 'pending'
              AND NEW.readaptation_review_state <> OLD.readaptation_review_state;
        END;
        """;

    private const string TrustedOffsetBlacklist = """
        CREATE TABLE mikan_trusted_offset_blacklist (
            scope TEXT NOT NULL CHECK (scope IN ('mikanid', 'groupid', 'pair')),
            mikanid INTEGER NOT NULL DEFAULT 0 CHECK (mikanid >= 0),
            groupid INTEGER NOT NULL DEFAULT 0 CHECK (groupid >= 0),
            created_at_utc TEXT NOT NULL,
            CHECK (
                (scope = 'mikanid' AND mikanid > 0 AND groupid = 0)
                OR (scope = 'groupid' AND mikanid = 0 AND groupid > 0)
                OR (scope = 'pair' AND mikanid > 0 AND groupid > 0)),
            PRIMARY KEY (scope, mikanid, groupid)
        ) STRICT;

        CREATE INDEX ix_mikan_trusted_offset_blacklist_mikanid
        ON mikan_trusted_offset_blacklist(mikanid)
        WHERE mikanid > 0;

        CREATE INDEX ix_mikan_trusted_offset_blacklist_groupid
        ON mikan_trusted_offset_blacklist(groupid)
        WHERE groupid > 0;
        """;

    private const string AiValidatedEpisodeAudit = """
        CREATE TABLE metadata_ai_validated_episodes (
            attempt_id TEXT NOT NULL
                REFERENCES metadata_resolution_attempts(id) ON DELETE CASCADE,
            tmdb_series_id INTEGER NOT NULL CHECK (tmdb_series_id > 0),
            tmdb_season_number INTEGER NOT NULL CHECK (tmdb_season_number > 0),
            tmdb_episode_number INTEGER NOT NULL CHECK (tmdb_episode_number > 0),
            tmdb_episode_id INTEGER CHECK (tmdb_episode_id > 0),
            episode_name TEXT,
            validated_at_utc TEXT NOT NULL,
            PRIMARY KEY (
                attempt_id, tmdb_series_id,
                tmdb_season_number, tmdb_episode_number)
        ) STRICT;

        CREATE INDEX ix_metadata_ai_validated_episodes_identity
        ON metadata_ai_validated_episodes(
            tmdb_series_id, tmdb_season_number, tmdb_episode_number);

        INSERT INTO metadata_ai_validated_episodes (
            attempt_id, tmdb_series_id, tmdb_season_number,
            tmdb_episode_number, tmdb_episode_id, episode_name,
            validated_at_utc)
        SELECT file.episode_resolution_attempt_id,
               file.tmdb_series_id, file.tmdb_season_number,
               file.tmdb_episode_number, MAX(file.tmdb_episode_id),
               MAX(episode.name),
               COALESCE(run.completed_at_utc, attempt.created_at_utc)
        FROM task_files AS file
        JOIN metadata_resolution_attempts AS attempt
          ON attempt.id = file.episode_resolution_attempt_id
        JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
        LEFT JOIN tmdb_episodes AS episode
          ON episode.tmdb_episode_id = file.tmdb_episode_id
        WHERE file.episode_resolution_source = 'ai_metadata'
          AND file.tmdb_series_id IS NOT NULL
          AND file.tmdb_season_number IS NOT NULL
          AND file.tmdb_episode_number IS NOT NULL
          AND attempt.stage = 'episode'
          AND attempt.strategy = 'ai_metadata'
          AND attempt.result = 'matched'
        GROUP BY file.episode_resolution_attempt_id,
                 file.tmdb_series_id, file.tmdb_season_number,
                 file.tmdb_episode_number;
        """;

    private const string ConfigurableTrustedOffsetThreshold = """
        ALTER TABLE mikan_trusted_offsets RENAME TO mikan_trusted_offsets_legacy;

        CREATE TABLE mikan_trusted_offsets (
            mikanid INTEGER NOT NULL CHECK (mikanid > 0),
            groupid INTEGER NOT NULL CHECK (groupid > 0),
            tmdb_series_id INTEGER NOT NULL CHECK (tmdb_series_id > 0),
            tmdb_season_number INTEGER NOT NULL CHECK (tmdb_season_number > 0),
            episode_offset INTEGER NOT NULL,
            distinct_episode_count INTEGER NOT NULL CHECK (distinct_episode_count >= 1),
            state TEXT NOT NULL CHECK (state IN ('trusted', 'revoked')),
            updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (mikanid, groupid)
        ) STRICT;

        INSERT INTO mikan_trusted_offsets (
            mikanid, groupid, tmdb_series_id, tmdb_season_number,
            episode_offset, distinct_episode_count, state, updated_at_utc)
        SELECT mikanid, groupid, tmdb_series_id, tmdb_season_number,
               episode_offset, distinct_episode_count, state, updated_at_utc
        FROM mikan_trusted_offsets_legacy;

        DROP TABLE mikan_trusted_offsets_legacy;
        """;

    private const string ReadaptationManualTmdbOverride = """
        ALTER TABLE other_file_readaptation_jobs ADD COLUMN resolution_source_override TEXT
            CHECK (resolution_source_override IS NULL
                   OR resolution_source_override = 'manual_review_override');
        """;

    private const string ReadaptationReviewComparison = """
        ALTER TABLE other_file_readaptation_jobs ADD COLUMN original_disposition TEXT;
        ALTER TABLE other_file_readaptation_jobs ADD COLUMN original_tmdb_series_id INTEGER;
        ALTER TABLE other_file_readaptation_jobs ADD COLUMN original_tmdb_season_number INTEGER;
        ALTER TABLE other_file_readaptation_jobs ADD COLUMN original_tmdb_episode_number INTEGER;
        """;

    private const string FreshReadaptationReviewAndTaskDelete = """
        ALTER TABLE ingest_tasks ADD COLUMN source_page_url TEXT;
        ALTER TABLE ingest_tasks ADD COLUMN readaptation_review_state TEXT NOT NULL
            DEFAULT 'not_required'
            CHECK (readaptation_review_state IN ('not_required', 'pending', 'approved'));
        ALTER TABLE ingest_tasks ADD COLUMN readaptation_review_requested_at_utc TEXT;
        ALTER TABLE ingest_tasks ADD COLUMN readaptation_reviewed_at_utc TEXT;

        ALTER TABLE other_file_readaptation_jobs
            ADD COLUMN preserve_source INTEGER NOT NULL DEFAULT 0
            CHECK (preserve_source IN (0, 1));

        UPDATE ingest_tasks
        SET source_page_url = (
            SELECT entry.mikan_url
            FROM mikan_rss_batch_entries AS entry
            WHERE entry.ingest_task_id = ingest_tasks.id
              AND instr(entry.mikan_url, '?') = 0
              AND instr(entry.mikan_url, '#') = 0
            ORDER BY entry.rowid DESC
            LIMIT 1)
        WHERE source_page_url IS NULL
          AND EXISTS (
            SELECT 1 FROM mikan_rss_batch_entries AS entry
            WHERE entry.ingest_task_id = ingest_tasks.id
              AND instr(entry.mikan_url, '?') = 0
              AND instr(entry.mikan_url, '#') = 0);

        DROP INDEX ix_delete_execution_items_pending;
        ALTER TABLE delete_execution_items RENAME TO delete_execution_items_v47;
        CREATE TABLE delete_execution_items (
            id TEXT NOT NULL PRIMARY KEY,
            execution_id TEXT NOT NULL REFERENCES delete_executions(id) ON DELETE CASCADE,
            item_kind TEXT NOT NULL CHECK (item_kind IN (
                'business_record', 'downloader_task', 'source_file', 'media_file', 'task_record')),
            target_key TEXT NOT NULL,
            root_path TEXT,
            downloader_id TEXT,
            display_value TEXT NOT NULL,
            ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
            state TEXT NOT NULL CHECK (state IN ('pending', 'completed', 'skipped', 'failed')),
            failure_code TEXT,
            completed_at_utc TEXT,
            UNIQUE (execution_id, item_kind, target_key)
        ) STRICT;
        INSERT INTO delete_execution_items (
            id, execution_id, item_kind, target_key, root_path, downloader_id,
            display_value, ordinal, state, failure_code, completed_at_utc)
        SELECT id, execution_id, item_kind, target_key, root_path, downloader_id,
               display_value, ordinal, state, failure_code, completed_at_utc
        FROM delete_execution_items_v47;
        DROP TABLE delete_execution_items_v47;
        CREATE INDEX ix_delete_execution_items_pending
        ON delete_execution_items(execution_id, state, ordinal);
        """;

    private const string OtherFileReadaptation = """
        CREATE TABLE other_file_readaptation_jobs (
            id TEXT NOT NULL PRIMARY KEY,
            task_id TEXT NOT NULL REFERENCES ingest_tasks(id) ON DELETE CASCADE,
            task_file_id TEXT NOT NULL REFERENCES task_files(id) ON DELETE CASCADE,
            source_media_path TEXT NOT NULL,
            original_other_reason TEXT NOT NULL,
            state TEXT NOT NULL CHECK (state IN ('pending', 'completed')),
            requested_at_utc TEXT NOT NULL,
            completed_at_utc TEXT
        ) STRICT;

        CREATE UNIQUE INDEX ux_other_file_readaptation_active
        ON other_file_readaptation_jobs(task_file_id)
        WHERE state = 'pending';

        CREATE INDEX ix_other_file_readaptation_task
        ON other_file_readaptation_jobs(task_id, state, requested_at_utc DESC);
        """;

    private const string BangumiArchiveUsageEvents = """
        CREATE TABLE bangumi_archive_usage_events (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            data_version TEXT NOT NULL,
            hit_kind TEXT NOT NULL CHECK (
                hit_kind IN ('subject', 'episodes', 'relations')),
            subject_id INTEGER NOT NULL CHECK (subject_id > 0),
            result_count INTEGER NOT NULL CHECK (result_count >= 0),
            hit_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE INDEX ix_bangumi_archive_usage_events_recent
            ON bangumi_archive_usage_events(hit_at_utc DESC, id DESC);

        CREATE INDEX ix_bangumi_archive_usage_events_kind_recent
            ON bangumi_archive_usage_events(
                hit_kind, hit_at_utc DESC, id DESC);
        """;

    private const string BangumiArchiveUsageAudit = """
        CREATE TABLE bangumi_archive_usage (
            data_version TEXT NOT NULL PRIMARY KEY,
            subject_hit_count INTEGER NOT NULL DEFAULT 0
                CHECK (subject_hit_count >= 0),
            episode_hit_count INTEGER NOT NULL DEFAULT 0
                CHECK (episode_hit_count >= 0),
            relation_hit_count INTEGER NOT NULL DEFAULT 0
                CHECK (relation_hit_count >= 0),
            last_hit_at_utc TEXT NOT NULL
        ) STRICT;
        """;

    private const string RecoverCompletedMetadataOrganization = """
        UPDATE ingest_tasks
        SET status = 'downloaded', failure_kind = NULL, failure_reason = NULL
        WHERE status = 'metadata_season_resolved'
          AND EXISTS (
              SELECT 1
              FROM download_jobs AS job
              WHERE job.task_id = ingest_tasks.id
                AND job.preparation_state = 'completed'
                AND job.organization_state = 'pending'
                AND (job.state IN ('seeding', 'complete') OR job.progress >= 1)
          )
          AND EXISTS (
              SELECT 1 FROM task_files AS file
              WHERE file.task_id = ingest_tasks.id
          )
          AND NOT EXISTS (
              SELECT 1 FROM task_files AS file
              WHERE file.task_id = ingest_tasks.id
                AND file.disposition = 'pending'
          )
          AND NOT EXISTS (
              SELECT 1 FROM metadata_resolution_runs AS run
              WHERE run.task_id = ingest_tasks.id
                AND run.status = 'running'
          );
        """;

    private const string TmdbEpisodeNearestDateEvidence = """
        DROP TRIGGER tr_task_files_episode_evidence_insert;
        DROP TRIGGER tr_task_files_episode_evidence_update;

        CREATE TRIGGER tr_task_files_episode_evidence_insert
        BEFORE INSERT ON task_files
        WHEN NOT (
            (
                NEW.episode_resolution_source IS NULL
                AND NEW.episode_resolution_run_id IS NULL
                AND NEW.episode_resolution_attempt_id IS NULL
            )
            OR (
                NEW.episode_resolution_source IN (
                    'manual_mikan_offset', 'trusted_mikan_offset',
                    'ai_metadata', 'tmdb_episode_number',
                    'tmdb_episode_bangumi_date',
                    'tmdb_episode_bangumi_nearest_date',
                    'subtitle_association')
                AND NEW.episode_resolution_run_id IS NOT NULL
                AND NEW.episode_resolution_attempt_id IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run
                      ON run.id = attempt.run_id
                    WHERE attempt.id = NEW.episode_resolution_attempt_id
                      AND attempt.run_id = NEW.episode_resolution_run_id
                      AND run.task_id = NEW.task_id
                      AND attempt.stage = 'episode'
                      AND attempt.strategy = NEW.episode_resolution_source
                      AND attempt.result = 'matched')
            )
        )
        BEGIN
            SELECT RAISE(ABORT, 'invalid TMDB Episode resolution evidence');
        END;

        CREATE TRIGGER tr_task_files_episode_evidence_update
        BEFORE UPDATE OF
            episode_resolution_source, episode_resolution_run_id,
            episode_resolution_attempt_id
        ON task_files
        WHEN NOT (
            (
                NEW.episode_resolution_source IS NULL
                AND NEW.episode_resolution_run_id IS NULL
                AND NEW.episode_resolution_attempt_id IS NULL
            )
            OR (
                NEW.episode_resolution_source IN (
                    'manual_mikan_offset', 'trusted_mikan_offset',
                    'ai_metadata', 'tmdb_episode_number',
                    'tmdb_episode_bangumi_date',
                    'tmdb_episode_bangumi_nearest_date',
                    'subtitle_association')
                AND NEW.episode_resolution_run_id IS NOT NULL
                AND NEW.episode_resolution_attempt_id IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run
                      ON run.id = attempt.run_id
                    WHERE attempt.id = NEW.episode_resolution_attempt_id
                      AND attempt.run_id = NEW.episode_resolution_run_id
                      AND run.task_id = NEW.task_id
                      AND attempt.stage = 'episode'
                      AND attempt.strategy = NEW.episode_resolution_source
                      AND attempt.result = 'matched')
            )
        )
        BEGIN
            SELECT RAISE(ABORT, 'invalid TMDB Episode resolution evidence');
        END;
        """;

    private const string BangumiArchiveSubjectRelations = """
        CREATE TABLE data_update_staging_relations (
            run_id TEXT NOT NULL REFERENCES data_update_runs(id) ON DELETE CASCADE,
            subject_id INTEGER NOT NULL CHECK (subject_id > 0),
            related_subject_id INTEGER NOT NULL CHECK (related_subject_id > 0),
            relation_type INTEGER NOT NULL CHECK (relation_type > 0),
            relation_order INTEGER NOT NULL CHECK (relation_order >= 0),
            PRIMARY KEY (run_id, subject_id, related_subject_id, relation_type)
        ) STRICT;

        CREATE INDEX ix_data_update_staging_relation_subject
            ON data_update_staging_relations(run_id, subject_id, relation_order);

        CREATE TABLE bangumi_archive_subject_relations (
            data_version TEXT NOT NULL,
            subject_id INTEGER NOT NULL CHECK (subject_id > 0),
            related_subject_id INTEGER NOT NULL CHECK (related_subject_id > 0),
            relation_type INTEGER NOT NULL CHECK (relation_type > 0),
            relation_order INTEGER NOT NULL CHECK (relation_order >= 0),
            PRIMARY KEY (
                data_version, subject_id, related_subject_id, relation_type),
            FOREIGN KEY (data_version, subject_id)
                REFERENCES bangumi_archive_subjects(data_version, subject_id)
                ON DELETE CASCADE,
            FOREIGN KEY (data_version, related_subject_id)
                REFERENCES bangumi_archive_subjects(data_version, subject_id)
                ON DELETE CASCADE
        ) STRICT;

        CREATE INDEX ix_bangumi_archive_relation_subject
            ON bangumi_archive_subject_relations(
                data_version, subject_id, relation_order, related_subject_id);
        """;

    private const string TmdbEpisodeBangumiDateEvidence = """
        DROP TRIGGER tr_task_files_episode_evidence_insert;
        DROP TRIGGER tr_task_files_episode_evidence_update;

        CREATE TRIGGER tr_task_files_episode_evidence_insert
        BEFORE INSERT ON task_files
        WHEN NOT (
            (
                NEW.episode_resolution_source IS NULL
                AND NEW.episode_resolution_run_id IS NULL
                AND NEW.episode_resolution_attempt_id IS NULL
            )
            OR (
                NEW.episode_resolution_source IN (
                    'manual_mikan_offset', 'trusted_mikan_offset',
                    'ai_metadata', 'tmdb_episode_number',
                    'tmdb_episode_bangumi_date',
                    'subtitle_association')
                AND NEW.episode_resolution_run_id IS NOT NULL
                AND NEW.episode_resolution_attempt_id IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run
                      ON run.id = attempt.run_id
                    WHERE attempt.id = NEW.episode_resolution_attempt_id
                      AND attempt.run_id = NEW.episode_resolution_run_id
                      AND run.task_id = NEW.task_id
                      AND attempt.stage = 'episode'
                      AND attempt.strategy = NEW.episode_resolution_source
                      AND attempt.result = 'matched')
            )
        )
        BEGIN
            SELECT RAISE(ABORT, 'invalid TMDB Episode resolution evidence');
        END;

        CREATE TRIGGER tr_task_files_episode_evidence_update
        BEFORE UPDATE OF
            episode_resolution_source, episode_resolution_run_id,
            episode_resolution_attempt_id
        ON task_files
        WHEN NOT (
            (
                NEW.episode_resolution_source IS NULL
                AND NEW.episode_resolution_run_id IS NULL
                AND NEW.episode_resolution_attempt_id IS NULL
            )
            OR (
                NEW.episode_resolution_source IN (
                    'manual_mikan_offset', 'trusted_mikan_offset',
                    'ai_metadata', 'tmdb_episode_number',
                    'tmdb_episode_bangumi_date',
                    'subtitle_association')
                AND NEW.episode_resolution_run_id IS NOT NULL
                AND NEW.episode_resolution_attempt_id IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run
                      ON run.id = attempt.run_id
                    WHERE attempt.id = NEW.episode_resolution_attempt_id
                      AND attempt.run_id = NEW.episode_resolution_run_id
                      AND run.task_id = NEW.task_id
                      AND attempt.stage = 'episode'
                      AND attempt.strategy = NEW.episode_resolution_source
                      AND attempt.result = 'matched')
            )
        )
        BEGIN
            SELECT RAISE(ABORT, 'invalid TMDB Episode resolution evidence');
        END;
        """;

    private const string AiMetadataUsageAudit = """
        ALTER TABLE metadata_resolution_attempts
        ADD COLUMN ai_model TEXT CHECK (
            ai_model IS NULL OR length(ai_model) BETWEEN 1 AND 256);

        ALTER TABLE metadata_resolution_attempts
        ADD COLUMN ai_prompt_tokens INTEGER CHECK (
            ai_prompt_tokens IS NULL OR ai_prompt_tokens >= 0);

        ALTER TABLE metadata_resolution_attempts
        ADD COLUMN ai_completion_tokens INTEGER CHECK (
            ai_completion_tokens IS NULL OR ai_completion_tokens >= 0);

        ALTER TABLE metadata_resolution_attempts
        ADD COLUMN ai_total_tokens INTEGER CHECK (
            ai_total_tokens IS NULL OR ai_total_tokens >= 0);

        ALTER TABLE metadata_resolution_attempts
        ADD COLUMN ai_request_count INTEGER CHECK (
            ai_request_count IS NULL OR ai_request_count > 0);

        ALTER TABLE metadata_resolution_attempts
        ADD COLUMN ai_tool_call_count INTEGER CHECK (
            ai_tool_call_count IS NULL OR ai_tool_call_count >= 0);
        """;

    private const string LegacyCacheImportAudit = """
        CREATE TABLE legacy_cache_imports (
            package_sha256 TEXT PRIMARY KEY CHECK (
                length(package_sha256) = 64
                AND package_sha256 = lower(package_sha256)),
            format_version INTEGER NOT NULL CHECK (format_version = 1),
            source_commit TEXT NOT NULL CHECK (length(source_commit) BETWEEN 1 AND 128),
            bucket_count INTEGER NOT NULL CHECK (bucket_count BETWEEN 0 AND 6),
            entry_count INTEGER NOT NULL CHECK (entry_count BETWEEN 0 AND 50000),
            imported_entry_count INTEGER NOT NULL CHECK (
                imported_entry_count BETWEEN 0 AND entry_count),
            skipped_expired_entry_count INTEGER NOT NULL CHECK (
                skipped_expired_entry_count BETWEEN 0 AND entry_count
                AND imported_entry_count + skipped_expired_entry_count = entry_count),
            imported_at_utc TEXT NOT NULL,
            last_seen_at_utc TEXT NOT NULL,
            repeat_count INTEGER NOT NULL DEFAULT 0 CHECK (repeat_count >= 0)
        ) STRICT;
        """;

    private const string SourceDuplicateNotifications = """
        ALTER TABLE source_profiles
        ADD COLUMN duplicate_notification_enabled INTEGER NOT NULL DEFAULT 1
        CHECK (duplicate_notification_enabled IN (0, 1));
        """;

    private const string MediaOrganizationProgress = """
        ALTER TABLE download_jobs
        ADD COLUMN organization_phase TEXT NOT NULL DEFAULT 'not_started'
        CHECK (organization_phase IN (
            'not_started', 'rename_planning', 'media_transfer', 'subtitle_transfer',
            'nfo_write', 'directory_index', 'cleanup_downloader', 'completed'));

        ALTER TABLE download_jobs
        ADD COLUMN organization_total_units INTEGER NOT NULL DEFAULT 0
        CHECK (organization_total_units >= 0);

        ALTER TABLE download_jobs
        ADD COLUMN organization_completed_units INTEGER NOT NULL DEFAULT 0
        CHECK (organization_completed_units >= 0
            AND organization_completed_units <= organization_total_units);

        UPDATE download_jobs
        SET organization_phase = CASE organization_state
                WHEN 'completed' THEN 'completed'
                WHEN 'cleanup' THEN 'cleanup_downloader'
                ELSE 'not_started'
            END,
            organization_total_units = CASE
                WHEN organization_state IN ('completed', 'cleanup') THEN 1 ELSE 0 END,
            organization_completed_units = CASE
                WHEN organization_state = 'completed' THEN 1 ELSE 0 END;

        CREATE TRIGGER tr_download_jobs_organization_progress_insert
        BEFORE INSERT ON download_jobs
        WHEN NOT (
            NEW.organization_completed_units BETWEEN 0 AND NEW.organization_total_units
            AND (
                (NEW.organization_phase = 'not_started'
                    AND NEW.organization_total_units = 0
                    AND NEW.organization_completed_units = 0)
                OR (NEW.organization_phase = 'completed'
                    AND NEW.organization_total_units = 1
                    AND NEW.organization_completed_units = 1)
                OR (NEW.organization_phase IN (
                        'rename_planning', 'media_transfer', 'subtitle_transfer',
                        'nfo_write', 'directory_index', 'cleanup_downloader')
                    AND NEW.organization_total_units > 0)
            )
            AND (NEW.organization_state != 'not_required'
                OR NEW.organization_phase = 'not_started')
            AND (NEW.organization_state != 'cleanup'
                OR NEW.organization_phase = 'cleanup_downloader')
            AND (NEW.organization_state != 'completed'
                OR NEW.organization_phase = 'completed')
            AND (NEW.organization_phase != 'completed'
                OR NEW.organization_state = 'completed')
        )
        BEGIN
            SELECT RAISE(ABORT, 'invalid media organization progress');
        END;

        CREATE TRIGGER tr_download_jobs_organization_progress_update
        BEFORE UPDATE OF organization_state, organization_phase,
            organization_total_units, organization_completed_units
        ON download_jobs
        WHEN NOT (
            NEW.organization_completed_units BETWEEN 0 AND NEW.organization_total_units
            AND (
                (NEW.organization_phase = 'not_started'
                    AND NEW.organization_total_units = 0
                    AND NEW.organization_completed_units = 0)
                OR (NEW.organization_phase = 'completed'
                    AND NEW.organization_total_units = 1
                    AND NEW.organization_completed_units = 1)
                OR (NEW.organization_phase IN (
                        'rename_planning', 'media_transfer', 'subtitle_transfer',
                        'nfo_write', 'directory_index', 'cleanup_downloader')
                    AND NEW.organization_total_units > 0)
            )
            AND (NEW.organization_state != 'not_required'
                OR NEW.organization_phase = 'not_started')
            AND (NEW.organization_state != 'cleanup'
                OR NEW.organization_phase = 'cleanup_downloader')
            AND (NEW.organization_state != 'completed'
                OR NEW.organization_phase = 'completed')
            AND (NEW.organization_phase != 'completed'
                OR NEW.organization_state = 'completed')
        )
        BEGIN
            SELECT RAISE(ABORT, 'invalid media organization progress');
        END;
        """;

    private const string SourceRssScheduling = """
        ALTER TABLE source_profiles
        ADD COLUMN rss_feed_url TEXT
        CHECK (rss_feed_url IS NULL OR length(rss_feed_url) BETWEEN 1 AND 4096);

        ALTER TABLE source_profiles
        ADD COLUMN rss_schedule_enabled INTEGER NOT NULL DEFAULT 0
        CHECK (rss_schedule_enabled IN (0, 1));

        ALTER TABLE source_profiles
        ADD COLUMN rss_schedule_cron TEXT NOT NULL DEFAULT '0 0/15 * * * ?'
        CHECK (length(rss_schedule_cron) BETWEEN 1 AND 256);

        ALTER TABLE source_profiles
        ADD COLUMN rss_last_run_state TEXT NOT NULL DEFAULT 'never'
        CHECK (rss_last_run_state IN ('never', 'running', 'succeeded', 'failed'));

        ALTER TABLE source_profiles
        ADD COLUMN rss_last_started_at_utc TEXT;

        ALTER TABLE source_profiles
        ADD COLUMN rss_last_completed_at_utc TEXT;

        ALTER TABLE source_profiles
        ADD COLUMN rss_last_failure_code TEXT
        CHECK (rss_last_failure_code IS NULL
            OR length(rss_last_failure_code) BETWEEN 1 AND 128);

        ALTER TABLE source_profiles
        ADD COLUMN rss_last_batch_id TEXT
        REFERENCES mikan_rss_batches(id) ON DELETE SET NULL;

        CREATE INDEX ix_source_profiles_rss_schedule
        ON source_profiles(rss_schedule_enabled, enabled, id);
        """;

    private const string CompletionSourceAliasAudit = """
        ALTER TABLE mikan_rss_batch_entries
        ADD COLUMN early_completion_id TEXT
            REFERENCES completion_records(id) ON DELETE SET NULL;

        ALTER TABLE mikan_rss_batch_entries
        ADD COLUMN early_completion_alias_id TEXT
            REFERENCES completion_aliases(id) ON DELETE SET NULL;

        ALTER TABLE mikan_rss_batch_entries
        ADD COLUMN early_completion_checked_at_utc TEXT;

        CREATE INDEX ix_completion_aliases_source_episode
        ON completion_aliases(source_id, source_work_id, source_episode, created_at_utc)
        WHERE source_work_id IS NOT NULL AND source_episode IS NOT NULL;

        CREATE INDEX ix_mikan_rss_entries_early_completion
        ON mikan_rss_batch_entries(early_completion_id, batch_id, ordinal)
        WHERE early_completion_id IS NOT NULL;

        INSERT OR IGNORE INTO completion_aliases (
            id, completion_id, source_id, source_work_id, source_episode,
            info_hash, created_at_utc)
        SELECT
            'v35-' || completion.id || '-' || file.id,
            completion.id,
            lower(completion.source_id),
            task.source_work_id,
            file.source_episode,
            job.info_hash,
            completion.completed_at_utc
        FROM completion_records AS completion
        JOIN ingest_tasks AS task
          ON lower(task.source_id) = lower(completion.source_id)
         AND task.source_item_id = completion.source_item_id
        JOIN task_files AS file
          ON file.task_id = task.id
         AND file.tmdb_series_id = completion.tmdb_series_id
         AND file.tmdb_season_number = completion.tmdb_season_number
         AND file.tmdb_episode_number = completion.tmdb_episode_number
         AND file.associated_task_file_id IS NULL
        LEFT JOIN download_jobs AS job ON job.task_id = task.id
        WHERE completion.source_item_id IS NOT NULL
          AND task.source_work_id IS NOT NULL
          AND file.source_episode IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM completion_aliases AS alias
              WHERE alias.completion_id = completion.id
                AND alias.source_id = lower(completion.source_id)
                AND alias.source_work_id = task.source_work_id
                AND alias.source_episode = file.source_episode);
        """;

    private const string DynamicDownloadTags = """
        ALTER TABLE source_profiles
        ADD COLUMN dynamic_tag_template TEXT
        CHECK (dynamic_tag_template IS NULL
            OR length(dynamic_tag_template) BETWEEN 1 AND 512);

        ALTER TABLE source_profiles
        ADD COLUMN dynamic_tag_template_initialized INTEGER NOT NULL DEFAULT 0
        CHECK (dynamic_tag_template_initialized IN (0, 1));

        ALTER TABLE download_jobs
        ADD COLUMN dynamic_tags_json TEXT NOT NULL DEFAULT '[]'
        CHECK (json_valid(dynamic_tags_json)
            AND json_type(dynamic_tags_json) = 'array');

        ALTER TABLE download_jobs
        ADD COLUMN dynamic_tag_state TEXT NOT NULL DEFAULT 'not_configured'
        CHECK (dynamic_tag_state IN ('not_configured', 'pending', 'applied', 'skipped'));

        ALTER TABLE download_jobs
        ADD COLUMN dynamic_tag_failure_code TEXT
        CHECK (dynamic_tag_failure_code IS NULL
            OR length(dynamic_tag_failure_code) BETWEEN 1 AND 128);

        UPDATE source_profiles
        SET dynamic_tag_template = '{year}年{quarter}月新番'
        WHERE id = 'mikan' AND dynamic_tag_template IS NULL;

        CREATE INDEX ix_download_jobs_dynamic_tag_state
        ON download_jobs(dynamic_tag_state, updated_at_utc);
        """;

    private const string DownloadSeedingLifecycle = """
        ALTER TABLE download_jobs
        ADD COLUMN seeding_target_minutes INTEGER NOT NULL DEFAULT 0
        CHECK (seeding_target_minutes BETWEEN -1 AND 5256000);

        ALTER TABLE download_jobs
        ADD COLUMN seeding_state TEXT NOT NULL DEFAULT 'not_required'
        CHECK (seeding_state IN ('not_required', 'waiting', 'seeding', 'completed'));

        ALTER TABLE download_jobs
        ADD COLUMN seeding_elapsed_seconds INTEGER NOT NULL DEFAULT 0
        CHECK (seeding_elapsed_seconds >= 0);

        ALTER TABLE download_jobs
        ADD COLUMN seeding_completed_at_utc TEXT;

        UPDATE download_jobs
        SET seeding_target_minutes = CASE
                WHEN task_id IN (
                    SELECT id FROM ingest_tasks
                    WHERE json_extract(route_snapshot_json, '$.file_strategy') = 'move')
                    THEN 0
                WHEN CAST(COALESCE((
                    SELECT json_extract(route_snapshot_json, '$.seeding_time_minutes')
                    FROM ingest_tasks WHERE id = download_jobs.task_id), 0) AS INTEGER)
                    BETWEEN -1 AND 5256000
                    THEN CAST(COALESCE((
                        SELECT json_extract(route_snapshot_json, '$.seeding_time_minutes')
                        FROM ingest_tasks WHERE id = download_jobs.task_id), 0) AS INTEGER)
                ELSE 0
            END;

        UPDATE download_jobs
        SET seeding_state = CASE
                WHEN seeding_target_minutes = 0 THEN 'not_required'
                WHEN state = 'complete' THEN 'completed'
                WHEN state = 'seeding' THEN 'seeding'
                ELSE 'waiting'
            END,
            seeding_completed_at_utc = CASE
                WHEN seeding_target_minutes <> 0 AND state = 'complete'
                    THEN updated_at_utc
                ELSE NULL
            END;

        CREATE INDEX ix_download_jobs_seeding_state
        ON download_jobs(seeding_state, updated_at_utc);
        """;

    private const string TmdbResolutionEvidence = """
        ALTER TABLE metadata_resolution_runs
        ADD COLUMN series_resolution_source TEXT;

        ALTER TABLE metadata_resolution_runs
        ADD COLUMN series_resolution_attempt_id TEXT;

        ALTER TABLE metadata_resolution_runs
        ADD COLUMN season_resolution_source TEXT;

        ALTER TABLE metadata_resolution_runs
        ADD COLUMN season_resolution_attempt_id TEXT;

        ALTER TABLE task_files
        ADD COLUMN episode_resolution_source TEXT;

        ALTER TABLE task_files
        ADD COLUMN episode_resolution_run_id TEXT;

        ALTER TABLE task_files
        ADD COLUMN episode_resolution_attempt_id TEXT;

        UPDATE metadata_resolution_runs AS target
        SET series_resolution_source = (
                SELECT attempt.strategy
                FROM metadata_resolution_attempts AS attempt
                WHERE attempt.run_id = target.id
                  AND attempt.stage = 'series'
                  AND attempt.result = 'matched'
                  AND attempt.strategy IN (
                      'manual_mikan_override', 'tmdb_title', 'backtrace',
                      'ai_metadata', 'trusted_mikan_offset')
                ORDER BY attempt.created_at_utc DESC, attempt.id DESC
                LIMIT 1),
            series_resolution_attempt_id = (
                SELECT attempt.id
                FROM metadata_resolution_attempts AS attempt
                WHERE attempt.run_id = target.id
                  AND attempt.stage = 'series'
                  AND attempt.result = 'matched'
                  AND attempt.strategy IN (
                      'manual_mikan_override', 'tmdb_title', 'backtrace',
                      'ai_metadata', 'trusted_mikan_offset')
                ORDER BY attempt.created_at_utc DESC, attempt.id DESC
                LIMIT 1),
            season_resolution_source = (
                SELECT attempt.strategy
                FROM metadata_resolution_attempts AS attempt
                WHERE attempt.run_id = target.id
                  AND attempt.stage = 'season'
                  AND attempt.result = 'matched'
                  AND attempt.strategy IN (
                      'manual_mikan_override', 'tmdb_air_date', 'backtrace',
                      'ai_metadata', 'title_season', 'first_season',
                      'trusted_mikan_offset')
                ORDER BY attempt.created_at_utc DESC, attempt.id DESC
                LIMIT 1),
            season_resolution_attempt_id = (
                SELECT attempt.id
                FROM metadata_resolution_attempts AS attempt
                WHERE attempt.run_id = target.id
                  AND attempt.stage = 'season'
                  AND attempt.result = 'matched'
                  AND attempt.strategy IN (
                      'manual_mikan_override', 'tmdb_air_date', 'backtrace',
                      'ai_metadata', 'title_season', 'first_season',
                      'trusted_mikan_offset')
                ORDER BY attempt.created_at_utc DESC, attempt.id DESC
                LIMIT 1)
        WHERE target.tmdb_series_id IS NOT NULL;

        CREATE INDEX ix_metadata_runs_series_evidence
        ON metadata_resolution_runs(
            task_id, series_resolution_source, completed_at_utc DESC);

        CREATE INDEX ix_metadata_runs_season_evidence
        ON metadata_resolution_runs(
            task_id, season_resolution_source, completed_at_utc DESC);

        CREATE INDEX ix_task_files_episode_evidence
        ON task_files(
            task_id, episode_resolution_source, episode_resolution_run_id);

        CREATE TRIGGER tr_metadata_runs_resolution_evidence_insert
        BEFORE INSERT ON metadata_resolution_runs
        WHEN NOT (
            (
                NEW.series_resolution_source IS NULL
                AND NEW.series_resolution_attempt_id IS NULL
            )
            OR (
                NEW.series_resolution_source IN (
                    'manual_mikan_override', 'tmdb_title', 'backtrace',
                    'ai_metadata', 'trusted_mikan_offset')
                AND NEW.series_resolution_attempt_id IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM metadata_resolution_attempts AS attempt
                    WHERE attempt.id = NEW.series_resolution_attempt_id
                      AND attempt.run_id = NEW.id
                      AND attempt.stage = 'series'
                      AND attempt.strategy = NEW.series_resolution_source
                      AND attempt.result = 'matched')
            )
        )
        OR NOT (
            (
                NEW.season_resolution_source IS NULL
                AND NEW.season_resolution_attempt_id IS NULL
            )
            OR (
                NEW.season_resolution_source IN (
                    'manual_mikan_override', 'tmdb_air_date', 'backtrace',
                    'ai_metadata', 'title_season', 'first_season',
                    'trusted_mikan_offset')
                AND NEW.season_resolution_attempt_id IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM metadata_resolution_attempts AS attempt
                    WHERE attempt.id = NEW.season_resolution_attempt_id
                      AND attempt.run_id = NEW.id
                      AND attempt.stage = 'season'
                      AND attempt.strategy = NEW.season_resolution_source
                      AND attempt.result = 'matched')
            )
        )
        BEGIN
            SELECT RAISE(ABORT, 'invalid TMDB run resolution evidence');
        END;

        CREATE TRIGGER tr_metadata_runs_resolution_evidence_update
        BEFORE UPDATE OF
            series_resolution_source, series_resolution_attempt_id,
            season_resolution_source, season_resolution_attempt_id
        ON metadata_resolution_runs
        WHEN NOT (
            (
                NEW.series_resolution_source IS NULL
                AND NEW.series_resolution_attempt_id IS NULL
            )
            OR (
                NEW.series_resolution_source IN (
                    'manual_mikan_override', 'tmdb_title', 'backtrace',
                    'ai_metadata', 'trusted_mikan_offset')
                AND NEW.series_resolution_attempt_id IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM metadata_resolution_attempts AS attempt
                    WHERE attempt.id = NEW.series_resolution_attempt_id
                      AND attempt.run_id = NEW.id
                      AND attempt.stage = 'series'
                      AND attempt.strategy = NEW.series_resolution_source
                      AND attempt.result = 'matched')
            )
        )
        OR NOT (
            (
                NEW.season_resolution_source IS NULL
                AND NEW.season_resolution_attempt_id IS NULL
            )
            OR (
                NEW.season_resolution_source IN (
                    'manual_mikan_override', 'tmdb_air_date', 'backtrace',
                    'ai_metadata', 'title_season', 'first_season',
                    'trusted_mikan_offset')
                AND NEW.season_resolution_attempt_id IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM metadata_resolution_attempts AS attempt
                    WHERE attempt.id = NEW.season_resolution_attempt_id
                      AND attempt.run_id = NEW.id
                      AND attempt.stage = 'season'
                      AND attempt.strategy = NEW.season_resolution_source
                      AND attempt.result = 'matched')
            )
        )
        BEGIN
            SELECT RAISE(ABORT, 'invalid TMDB run resolution evidence');
        END;

        CREATE TRIGGER tr_task_files_episode_evidence_insert
        BEFORE INSERT ON task_files
        WHEN NOT (
            (
                NEW.episode_resolution_source IS NULL
                AND NEW.episode_resolution_run_id IS NULL
                AND NEW.episode_resolution_attempt_id IS NULL
            )
            OR (
                NEW.episode_resolution_source IN (
                    'manual_mikan_offset', 'trusted_mikan_offset',
                    'ai_metadata', 'tmdb_episode_number',
                    'subtitle_association')
                AND NEW.episode_resolution_run_id IS NOT NULL
                AND NEW.episode_resolution_attempt_id IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run
                      ON run.id = attempt.run_id
                    WHERE attempt.id = NEW.episode_resolution_attempt_id
                      AND attempt.run_id = NEW.episode_resolution_run_id
                      AND run.task_id = NEW.task_id
                      AND attempt.stage = 'episode'
                      AND attempt.strategy = NEW.episode_resolution_source
                      AND attempt.result = 'matched')
            )
        )
        BEGIN
            SELECT RAISE(ABORT, 'invalid TMDB Episode resolution evidence');
        END;

        CREATE TRIGGER tr_task_files_episode_evidence_update
        BEFORE UPDATE OF
            episode_resolution_source, episode_resolution_run_id,
            episode_resolution_attempt_id
        ON task_files
        WHEN NOT (
            (
                NEW.episode_resolution_source IS NULL
                AND NEW.episode_resolution_run_id IS NULL
                AND NEW.episode_resolution_attempt_id IS NULL
            )
            OR (
                NEW.episode_resolution_source IN (
                    'manual_mikan_offset', 'trusted_mikan_offset',
                    'ai_metadata', 'tmdb_episode_number',
                    'subtitle_association')
                AND NEW.episode_resolution_run_id IS NOT NULL
                AND NEW.episode_resolution_attempt_id IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run
                      ON run.id = attempt.run_id
                    WHERE attempt.id = NEW.episode_resolution_attempt_id
                      AND attempt.run_id = NEW.episode_resolution_run_id
                      AND run.task_id = NEW.task_id
                      AND attempt.stage = 'episode'
                      AND attempt.strategy = NEW.episode_resolution_source
                      AND attempt.result = 'matched')
            )
        )
        BEGIN
            SELECT RAISE(ABORT, 'invalid TMDB Episode resolution evidence');
        END;
        """;

    private const string SourceMikanIdentityCookie = """
        ALTER TABLE source_profiles
        ADD COLUMN mikan_identity_cookie TEXT
            CHECK (
                mikan_identity_cookie IS NULL
                OR length(mikan_identity_cookie) BETWEEN 1 AND 8192);
        """;

    private const string LibraryMetadataAuditIndexes = """
        CREATE INDEX ix_task_files_tmdb_season_task
            ON task_files(tmdb_series_id, tmdb_season_number, task_id);

        CREATE INDEX ix_metadata_runs_tmdb_season_task
            ON metadata_resolution_runs(
                tmdb_series_id, tmdb_season_number, task_id, started_at_utc DESC);

        CREATE INDEX ix_metadata_attempts_run_created
            ON metadata_resolution_attempts(run_id, created_at_utc DESC);

        CREATE INDEX ix_mikan_work_rules_tmdb_season
            ON mikan_work_rules(tmdb_series_id, tmdb_season_number, mikanid);
        """;

    private const string DataUpdateTransferAudit = """
        CREATE TABLE data_update_transfer_runs (
            sequence INTEGER PRIMARY KEY AUTOINCREMENT,
            id TEXT NOT NULL UNIQUE,
            trigger_kind TEXT NOT NULL CHECK (trigger_kind IN ('manual', 'scheduled')),
            requested_action TEXT NOT NULL CHECK (
                requested_action IN ('check', 'download', 'download_import')),
            status TEXT NOT NULL CHECK (
                status IN (
                    'checking', 'update_available', 'up_to_date',
                    'downloading', 'downloaded', 'importing',
                    'completed', 'failed')),
            data_version TEXT,
            manifest_sha256 TEXT CHECK (
                manifest_sha256 IS NULL
                OR (
                    length(manifest_sha256) = 64
                    AND manifest_sha256 NOT GLOB '*[^0-9a-f]*')),
            failure_code TEXT,
            downloaded_bytes INTEGER NOT NULL CHECK (downloaded_bytes >= 0),
            total_bytes INTEGER NOT NULL CHECK (total_bytes >= 0),
            started_at_utc TEXT NOT NULL,
            completed_at_utc TEXT,
            CHECK (
                (status IN ('checking', 'downloading', 'importing')
                 AND failure_code IS NULL
                 AND completed_at_utc IS NULL)
                OR
                (status IN (
                    'update_available', 'up_to_date', 'downloaded', 'completed')
                 AND failure_code IS NULL
                 AND completed_at_utc IS NOT NULL)
                OR
                (status = 'failed'
                 AND failure_code IS NOT NULL
                 AND completed_at_utc IS NOT NULL)
            )
        ) STRICT;

        CREATE INDEX ix_data_update_transfer_runs_started
            ON data_update_transfer_runs(started_at_utc DESC, sequence DESC);

        CREATE TABLE data_update_downloads (
            data_version TEXT NOT NULL PRIMARY KEY,
            manifest_sha256 TEXT NOT NULL CHECK (
                length(manifest_sha256) = 64
                AND manifest_sha256 NOT GLOB '*[^0-9a-f]*'),
            relative_directory TEXT NOT NULL,
            state TEXT NOT NULL CHECK (state IN ('verified', 'imported')),
            downloaded_at_utc TEXT NOT NULL,
            imported_at_utc TEXT
        ) STRICT;
        """;

    private const string AnimeGoNetDataVersions = """
        CREATE TABLE data_update_versions (
            data_version TEXT NOT NULL PRIMARY KEY,
            schema_version INTEGER NOT NULL CHECK (schema_version > 0),
            generated_at_utc TEXT NOT NULL,
            minimum_client_version TEXT NOT NULL,
            manifest_sha256 TEXT NOT NULL CHECK (
                length(manifest_sha256) = 64
                AND manifest_sha256 NOT GLOB '*[^0-9a-f]*'),
            upstream_repository TEXT NOT NULL,
            upstream_release TEXT NOT NULL,
            upstream_asset TEXT NOT NULL,
            upstream_sha256 TEXT NOT NULL CHECK (
                length(upstream_sha256) = 64
                AND upstream_sha256 NOT GLOB '*[^0-9a-f]*'),
            subject_count INTEGER NOT NULL CHECK (subject_count > 0),
            episode_count INTEGER NOT NULL CHECK (episode_count > 0),
            state TEXT NOT NULL CHECK (state IN ('active', 'inactive')),
            installed_at_utc TEXT NOT NULL,
            activated_at_utc TEXT
        ) STRICT;

        CREATE UNIQUE INDEX ux_data_update_versions_active
            ON data_update_versions(state) WHERE state = 'active';

        CREATE TABLE data_update_state (
            singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
            active_version TEXT REFERENCES data_update_versions(data_version) ON DELETE SET NULL,
            previous_version TEXT REFERENCES data_update_versions(data_version) ON DELETE SET NULL,
            updated_at_utc TEXT NOT NULL,
            CHECK (active_version IS NULL OR active_version <> previous_version)
        ) STRICT;

        INSERT INTO data_update_state (
            singleton, active_version, previous_version, updated_at_utc)
        VALUES (1, NULL, NULL, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

        CREATE TABLE data_update_runs (
            id TEXT NOT NULL PRIMARY KEY,
            operation TEXT NOT NULL CHECK (operation IN ('import', 'rollback')),
            data_version TEXT,
            status TEXT NOT NULL CHECK (status IN ('running', 'completed', 'failed')),
            failure_code TEXT,
            subject_count INTEGER NOT NULL CHECK (subject_count >= 0),
            episode_count INTEGER NOT NULL CHECK (episode_count >= 0),
            started_at_utc TEXT NOT NULL,
            completed_at_utc TEXT,
            CHECK (
                (status = 'running' AND failure_code IS NULL AND completed_at_utc IS NULL)
                OR (status = 'completed' AND failure_code IS NULL AND completed_at_utc IS NOT NULL)
                OR (status = 'failed' AND failure_code IS NOT NULL AND completed_at_utc IS NOT NULL)
            )
        ) STRICT;

        CREATE TABLE data_update_staging_subjects (
            run_id TEXT NOT NULL REFERENCES data_update_runs(id) ON DELETE CASCADE,
            subject_id INTEGER NOT NULL CHECK (subject_id > 0),
            name TEXT NOT NULL,
            name_cn TEXT,
            air_date TEXT,
            episode_count INTEGER NOT NULL CHECK (episode_count >= 0),
            PRIMARY KEY (run_id, subject_id)
        ) STRICT;

        CREATE TABLE data_update_staging_episodes (
            run_id TEXT NOT NULL REFERENCES data_update_runs(id) ON DELETE CASCADE,
            episode_id INTEGER NOT NULL CHECK (episode_id > 0),
            subject_id INTEGER NOT NULL CHECK (subject_id > 0),
            sort_number INTEGER NOT NULL CHECK (sort_number > 0),
            episode_number TEXT NOT NULL,
            air_date TEXT,
            PRIMARY KEY (run_id, episode_id)
        ) STRICT;

        CREATE INDEX ix_data_update_staging_episode_subject
            ON data_update_staging_episodes(run_id, subject_id);

        CREATE TABLE bangumi_archive_subjects (
            data_version TEXT NOT NULL REFERENCES data_update_versions(data_version) ON DELETE CASCADE,
            subject_id INTEGER NOT NULL CHECK (subject_id > 0),
            name TEXT NOT NULL,
            name_cn TEXT,
            air_date TEXT,
            episode_count INTEGER NOT NULL CHECK (episode_count >= 0),
            PRIMARY KEY (data_version, subject_id)
        ) STRICT;

        CREATE TABLE bangumi_archive_episodes (
            data_version TEXT NOT NULL,
            episode_id INTEGER NOT NULL CHECK (episode_id > 0),
            subject_id INTEGER NOT NULL CHECK (subject_id > 0),
            sort_number INTEGER NOT NULL CHECK (sort_number > 0),
            episode_number TEXT NOT NULL,
            air_date TEXT,
            PRIMARY KEY (data_version, episode_id),
            FOREIGN KEY (data_version, subject_id)
                REFERENCES bangumi_archive_subjects(data_version, subject_id)
                ON DELETE CASCADE
        ) STRICT;

        CREATE INDEX ix_bangumi_archive_episode_subject
            ON bangumi_archive_episodes(data_version, subject_id, sort_number);
        """;

    private const string DirectoryDatabaseIndex = """
        CREATE TABLE directory_database_scan_runs (
            id TEXT NOT NULL PRIMARY KEY,
            status TEXT NOT NULL CHECK (status IN ('running', 'completed', 'failed')),
            scanned_count INTEGER NOT NULL CHECK (scanned_count >= 0),
            indexed_count INTEGER NOT NULL CHECK (indexed_count >= 0),
            rejected_count INTEGER NOT NULL CHECK (rejected_count >= 0),
            failure_code TEXT,
            started_at_utc TEXT NOT NULL,
            completed_at_utc TEXT
        ) STRICT;

        CREATE TABLE directory_database_scan_issues (
            run_id TEXT NOT NULL REFERENCES directory_database_scan_runs(id) ON DELETE CASCADE,
            relative_path TEXT NOT NULL,
            error_code TEXT NOT NULL,
            PRIMARY KEY (run_id, relative_path)
        ) STRICT;

        CREATE TABLE directory_database_entries (
            relative_path TEXT NOT NULL PRIMARY KEY,
            entry_kind TEXT NOT NULL CHECK (entry_kind IN ('anime', 'season', 'episode')),
            info_hash TEXT NOT NULL,
            anime_name TEXT NOT NULL,
            season_number INTEGER,
            episode_type INTEGER,
            episode_number INTEGER,
            seeded INTEGER CHECK (seeded IN (0, 1)),
            downloaded INTEGER CHECK (downloaded IN (0, 1)),
            renamed INTEGER CHECK (renamed IN (0, 1)),
            scraped INTEGER CHECK (scraped IN (0, 1)),
            create_at_unix INTEGER NOT NULL CHECK (create_at_unix >= 0),
            update_at_unix INTEGER NOT NULL CHECK (update_at_unix >= 0),
            indexed_at_utc TEXT NOT NULL,
            CHECK (
                (entry_kind = 'anime'
                 AND season_number IS NULL
                 AND episode_type IS NULL
                 AND episode_number IS NULL
                 AND seeded IS NULL
                 AND downloaded IS NULL
                 AND renamed IS NULL
                 AND scraped IS NULL)
                OR
                (entry_kind = 'season'
                 AND season_number > 0
                 AND episode_type IS NULL
                 AND episode_number IS NULL
                 AND seeded IS NULL
                 AND downloaded IS NULL
                 AND renamed IS NULL
                 AND scraped IS NULL)
                OR
                (entry_kind = 'episode'
                 AND season_number > 0
                 AND episode_type BETWEEN 0 AND 2
                 AND episode_number >= 0
                 AND seeded IS NOT NULL
                 AND downloaded IS NOT NULL
                 AND renamed IS NOT NULL
                 AND scraped IS NOT NULL)
            )
        ) STRICT;

        CREATE INDEX ix_directory_database_entries_identity
            ON directory_database_entries(anime_name, season_number, episode_number);
        """;

    private const string InitialBusinessSchema = """
        CREATE TABLE source_profiles (
            id TEXT NOT NULL PRIMARY KEY,
            display_name TEXT NOT NULL,
            adapter TEXT NOT NULL,
            downloader_id TEXT NOT NULL,
            file_strategy TEXT NOT NULL CHECK (file_strategy IN ('link', 'link_delete', 'move', 'wait_move')),
            rss_filter_enabled INTEGER NOT NULL CHECK (rss_filter_enabled IN (0, 1)),
            rss_priority_enabled INTEGER NOT NULL CHECK (rss_priority_enabled IN (0, 1)),
            revision INTEGER NOT NULL,
            enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE mikan_work_rules (
            mikanid INTEGER NOT NULL PRIMARY KEY CHECK (mikanid > 0),
            bangumi_subject_id INTEGER CHECK (bangumi_subject_id > 0),
            tmdb_series_id INTEGER CHECK (tmdb_series_id > 0),
            tmdb_season_number INTEGER CHECK (tmdb_season_number > 0),
            episode_offset INTEGER,
            enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
            revision INTEGER NOT NULL,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE mikan_offset_evidence (
            id TEXT NOT NULL PRIMARY KEY,
            mikanid INTEGER NOT NULL CHECK (mikanid > 0),
            groupid INTEGER NOT NULL CHECK (groupid > 0),
            source_episode TEXT NOT NULL,
            tmdb_series_id INTEGER NOT NULL CHECK (tmdb_series_id > 0),
            tmdb_season_number INTEGER NOT NULL CHECK (tmdb_season_number > 0),
            episode_offset INTEGER NOT NULL,
            observed_at_utc TEXT NOT NULL,
            UNIQUE (mikanid, groupid, source_episode)
        ) STRICT;

        CREATE TABLE mikan_trusted_offsets (
            mikanid INTEGER NOT NULL CHECK (mikanid > 0),
            groupid INTEGER NOT NULL CHECK (groupid > 0),
            tmdb_series_id INTEGER NOT NULL CHECK (tmdb_series_id > 0),
            tmdb_season_number INTEGER NOT NULL CHECK (tmdb_season_number > 0),
            episode_offset INTEGER NOT NULL,
            distinct_episode_count INTEGER NOT NULL CHECK (distinct_episode_count >= 3),
            state TEXT NOT NULL CHECK (state IN ('trusted', 'revoked')),
            updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (mikanid, groupid)
        ) STRICT;

        CREATE TABLE anime_series (
            id TEXT NOT NULL PRIMARY KEY,
            tmdb_series_id INTEGER NOT NULL CHECK (tmdb_series_id >= 0),
            bangumi_subject_id INTEGER CHECK (bangumi_subject_id > 0),
            canonical_name TEXT,
            original_name TEXT,
            poster_path TEXT,
            needs_tmdb_completion INTEGER NOT NULL CHECK (needs_tmdb_completion IN (0, 1)),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            CHECK ((tmdb_series_id > 0 AND needs_tmdb_completion = 0) OR
                   (tmdb_series_id = 0 AND bangumi_subject_id IS NOT NULL AND needs_tmdb_completion = 1))
        ) STRICT;

        CREATE UNIQUE INDEX ux_anime_series_tmdb
            ON anime_series(tmdb_series_id) WHERE tmdb_series_id > 0;
        CREATE UNIQUE INDEX ux_anime_series_bangumi_fallback
            ON anime_series(bangumi_subject_id) WHERE tmdb_series_id = 0;

        CREATE TABLE anime_seasons (
            id TEXT NOT NULL PRIMARY KEY,
            series_id TEXT NOT NULL REFERENCES anime_series(id) ON DELETE CASCADE,
            season_number INTEGER NOT NULL CHECK (season_number > 0),
            canonical_name TEXT,
            poster_path TEXT,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            UNIQUE (series_id, season_number)
        ) STRICT;

        CREATE TABLE tmdb_episodes (
            tmdb_episode_id INTEGER NOT NULL PRIMARY KEY CHECK (tmdb_episode_id > 0),
            series_id TEXT NOT NULL REFERENCES anime_series(id) ON DELETE CASCADE,
            season_number INTEGER NOT NULL CHECK (season_number > 0),
            episode_number INTEGER NOT NULL CHECK (episode_number > 0),
            name TEXT,
            air_date TEXT,
            runtime_minutes INTEGER,
            fetched_at_utc TEXT NOT NULL,
            UNIQUE (series_id, season_number, episode_number)
        ) STRICT;

        CREATE TABLE ingest_tasks (
            id TEXT NOT NULL PRIMARY KEY,
            source_profile_id TEXT NOT NULL REFERENCES source_profiles(id),
            source_profile_revision INTEGER NOT NULL,
            source_id TEXT NOT NULL,
            source_item_id TEXT,
            source_work_id TEXT,
            mikanid INTEGER CHECK (mikanid > 0),
            groupid INTEGER CHECK (groupid > 0),
            bangumi_subject_id INTEGER CHECK (bangumi_subject_id > 0),
            anidb_id INTEGER CHECK (anidb_id > 0),
            imdb_id TEXT,
            title TEXT NOT NULL,
            torrent_url_fingerprint TEXT NOT NULL,
            downloader_id TEXT NOT NULL,
            route_snapshot_json TEXT NOT NULL,
            status TEXT NOT NULL,
            failure_kind TEXT,
            failure_reason TEXT,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE task_files (
            id TEXT NOT NULL PRIMARY KEY,
            task_id TEXT NOT NULL REFERENCES ingest_tasks(id) ON DELETE CASCADE,
            relative_path TEXT NOT NULL,
            size_bytes INTEGER NOT NULL CHECK (size_bytes >= 0),
            source_episode TEXT,
            file_episode_candidate TEXT,
            tmdb_series_id INTEGER CHECK (tmdb_series_id > 0),
            tmdb_season_number INTEGER CHECK (tmdb_season_number > 0),
            tmdb_episode_number INTEGER CHECK (tmdb_episode_number > 0),
            tmdb_episode_id INTEGER CHECK (tmdb_episode_id > 0),
            disposition TEXT NOT NULL CHECK (disposition IN ('pending', 'episode', 'other', 'ignored', 'duplicate')),
            other_reason TEXT,
            UNIQUE (task_id, relative_path)
        ) STRICT;

        CREATE TABLE metadata_resolution_runs (
            id TEXT NOT NULL PRIMARY KEY,
            task_id TEXT NOT NULL REFERENCES ingest_tasks(id) ON DELETE CASCADE,
            status TEXT NOT NULL,
            tmdb_access_confirmed INTEGER NOT NULL CHECK (tmdb_access_confirmed IN (0, 1)),
            failure_kind TEXT,
            fallback_eligible INTEGER NOT NULL CHECK (fallback_eligible IN (0, 1)),
            fallback_denial_reason TEXT,
            started_at_utc TEXT NOT NULL,
            completed_at_utc TEXT
        ) STRICT;

        CREATE TABLE metadata_resolution_attempts (
            id TEXT NOT NULL PRIMARY KEY,
            run_id TEXT NOT NULL REFERENCES metadata_resolution_runs(id) ON DELETE CASCADE,
            stage TEXT NOT NULL CHECK (stage IN ('series', 'season', 'episode')),
            strategy TEXT NOT NULL,
            priority INTEGER,
            result TEXT NOT NULL,
            error_code TEXT,
            reason TEXT,
            retryable INTEGER NOT NULL CHECK (retryable IN (0, 1)),
            attempt_number INTEGER NOT NULL CHECK (attempt_number > 0),
            duration_ms INTEGER NOT NULL CHECK (duration_ms >= 0),
            created_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE episode_claims (
            id TEXT NOT NULL PRIMARY KEY,
            tmdb_series_id INTEGER NOT NULL CHECK (tmdb_series_id > 0),
            tmdb_season_number INTEGER NOT NULL CHECK (tmdb_season_number > 0),
            tmdb_episode_number INTEGER NOT NULL CHECK (tmdb_episode_number > 0),
            task_file_id TEXT NOT NULL REFERENCES task_files(id) ON DELETE CASCADE,
            state TEXT NOT NULL CHECK (state IN ('active', 'completed', 'released')),
            claimed_at_utc TEXT NOT NULL,
            expires_at_utc TEXT,
            UNIQUE (tmdb_series_id, tmdb_season_number, tmdb_episode_number)
        ) STRICT;

        CREATE TABLE fallback_claims (
            id TEXT NOT NULL PRIMARY KEY,
            scope_kind TEXT NOT NULL CHECK (scope_kind IN ('bangumi_episode', 'mikan_episode', 'source_work_episode', 'torrent_file')),
            scope_key TEXT NOT NULL,
            task_file_id TEXT NOT NULL REFERENCES task_files(id) ON DELETE CASCADE,
            state TEXT NOT NULL CHECK (state IN ('active', 'completed', 'released')),
            claimed_at_utc TEXT NOT NULL,
            expires_at_utc TEXT,
            UNIQUE (scope_kind, scope_key)
        ) STRICT;

        CREATE TABLE completion_records (
            id TEXT NOT NULL PRIMARY KEY,
            tmdb_series_id INTEGER NOT NULL CHECK (tmdb_series_id > 0),
            tmdb_season_number INTEGER NOT NULL CHECK (tmdb_season_number > 0),
            tmdb_episode_number INTEGER NOT NULL CHECK (tmdb_episode_number > 0),
            source_id TEXT NOT NULL,
            source_item_id TEXT,
            media_path TEXT,
            completed_at_utc TEXT NOT NULL,
            UNIQUE (tmdb_series_id, tmdb_season_number, tmdb_episode_number)
        ) STRICT;

        CREATE TABLE completion_aliases (
            id TEXT NOT NULL PRIMARY KEY,
            completion_id TEXT NOT NULL REFERENCES completion_records(id) ON DELETE CASCADE,
            source_id TEXT NOT NULL,
            source_work_id TEXT,
            source_episode TEXT,
            info_hash TEXT,
            created_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE fallback_completion_records (
            id TEXT NOT NULL PRIMARY KEY,
            anime_series_id TEXT NOT NULL REFERENCES anime_series(id) ON DELETE CASCADE,
            bangumi_subject_id INTEGER NOT NULL CHECK (bangumi_subject_id > 0),
            scope_kind TEXT NOT NULL CHECK (scope_kind IN ('bangumi_episode', 'mikan_episode', 'source_work_episode', 'torrent_file')),
            scope_key TEXT NOT NULL,
            source_id TEXT NOT NULL,
            source_episode TEXT,
            media_path TEXT,
            completed_at_utc TEXT NOT NULL,
            UNIQUE (scope_kind, scope_key)
        ) STRICT;

        CREATE TABLE download_jobs (
            id TEXT NOT NULL PRIMARY KEY,
            task_id TEXT NOT NULL REFERENCES ingest_tasks(id) ON DELETE CASCADE,
            downloader_id TEXT NOT NULL,
            info_hash TEXT,
            state TEXT NOT NULL,
            progress REAL NOT NULL CHECK (progress >= 0 AND progress <= 1),
            downloaded_bytes INTEGER NOT NULL CHECK (downloaded_bytes >= 0),
            total_bytes INTEGER NOT NULL CHECK (total_bytes >= 0),
            speed_bytes_per_second INTEGER NOT NULL CHECK (speed_bytes_per_second >= 0),
            eta_seconds INTEGER,
            failure_reason TEXT,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE file_operations (
            id TEXT NOT NULL PRIMARY KEY,
            task_file_id TEXT NOT NULL REFERENCES task_files(id) ON DELETE CASCADE,
            strategy TEXT NOT NULL CHECK (strategy IN ('link', 'link_delete', 'move', 'wait_move')),
            source_path TEXT NOT NULL,
            target_path TEXT NOT NULL,
            state TEXT NOT NULL,
            bytes_verified INTEGER NOT NULL CHECK (bytes_verified >= 0),
            failure_reason TEXT,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE delete_executions (
            id TEXT NOT NULL PRIMARY KEY,
            task_id TEXT,
            delete_business_record INTEGER NOT NULL CHECK (delete_business_record IN (0, 1)),
            delete_downloader_task INTEGER NOT NULL CHECK (delete_downloader_task IN (0, 1)),
            delete_source_files INTEGER NOT NULL CHECK (delete_source_files IN (0, 1)),
            delete_media_files INTEGER NOT NULL CHECK (delete_media_files IN (0, 1)),
            plan_json TEXT NOT NULL,
            state TEXT NOT NULL,
            failure_reason TEXT,
            created_at_utc TEXT NOT NULL,
            completed_at_utc TEXT
        ) STRICT;
        """;

    private const string SourceTorrentHostAllowlist = """
        ALTER TABLE source_profiles
        ADD COLUMN allowed_torrent_hosts_json TEXT NOT NULL DEFAULT '[]'
        CHECK (json_valid(allowed_torrent_hosts_json) AND json_type(allowed_torrent_hosts_json) = 'array');
        """;

    private const string StagedIngestLifecycle = """
        CREATE TABLE staged_torrents (
            task_id TEXT NOT NULL PRIMARY KEY REFERENCES ingest_tasks(id) ON DELETE CASCADE,
            staging_file_name TEXT NOT NULL UNIQUE,
            info_hash TEXT NOT NULL CHECK (length(info_hash) = 40 AND info_hash = lower(info_hash)),
            total_size_bytes INTEGER NOT NULL CHECK (total_size_bytes >= 0),
            expires_at_utc TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            CHECK (staging_file_name NOT LIKE '%/%' AND staging_file_name NOT LIKE '%\%')
        ) STRICT;

        CREATE INDEX ix_staged_torrents_expiry ON staged_torrents(expires_at_utc);
        """;

    private const string StagedDispatchLease = """
        ALTER TABLE staged_torrents
        ADD COLUMN dispatch_state TEXT NOT NULL DEFAULT 'ready'
        CHECK (dispatch_state IN ('ready', 'dispatching'));

        ALTER TABLE staged_torrents
        ADD COLUMN lease_token TEXT;

        ALTER TABLE staged_torrents
        ADD COLUMN lease_expires_at_utc TEXT;

        ALTER TABLE staged_torrents
        ADD COLUMN attempt_count INTEGER NOT NULL DEFAULT 0
        CHECK (attempt_count >= 0);

        ALTER TABLE staged_torrents
        ADD COLUMN next_attempt_at_utc TEXT;

        ALTER TABLE staged_torrents
        ADD COLUMN last_failure_code TEXT;

        CREATE INDEX ix_staged_torrents_dispatch
        ON staged_torrents(dispatch_state, next_attempt_at_utc, created_at_utc);

        CREATE UNIQUE INDEX ux_download_jobs_task ON download_jobs(task_id);
        """;

    private const string DownloadRuntimeProjection = """
        ALTER TABLE download_jobs
        ADD COLUMN seeds INTEGER NOT NULL DEFAULT 0 CHECK (seeds >= 0);

        ALTER TABLE download_jobs
        ADD COLUMN peers INTEGER NOT NULL DEFAULT 0 CHECK (peers >= 0);

        ALTER TABLE download_jobs
        ADD COLUMN snapshot_at_utc TEXT;

        ALTER TABLE download_jobs
        ADD COLUMN is_stale INTEGER NOT NULL DEFAULT 0 CHECK (is_stale IN (0, 1));

        ALTER TABLE download_jobs
        ADD COLUMN revision INTEGER NOT NULL DEFAULT 1 CHECK (revision > 0);

        CREATE TABLE downloader_runtime_state (
            downloader_id TEXT NOT NULL PRIMARY KEY,
            connected INTEGER NOT NULL CHECK (connected IN (0, 1)),
            failure_code TEXT,
            last_success_at_utc TEXT,
            updated_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE INDEX ix_download_jobs_active_instance
        ON download_jobs(downloader_id, state, updated_at_utc);
        """;

    private const string MetadataResolutionLease = """
        ALTER TABLE metadata_resolution_runs
        ADD COLUMN lease_token TEXT;

        ALTER TABLE metadata_resolution_runs
        ADD COLUMN lease_expires_at_utc TEXT;

        ALTER TABLE metadata_resolution_runs
        ADD COLUMN attempt_number INTEGER NOT NULL DEFAULT 1 CHECK (attempt_number > 0);

        ALTER TABLE metadata_resolution_runs
        ADD COLUMN tmdb_series_id INTEGER CHECK (tmdb_series_id > 0);

        ALTER TABLE metadata_resolution_runs
        ADD COLUMN tmdb_season_number INTEGER CHECK (tmdb_season_number > 0);

        CREATE UNIQUE INDEX ux_metadata_resolution_runs_active_task
        ON metadata_resolution_runs(task_id) WHERE status = 'running';

        CREATE INDEX ix_metadata_resolution_runs_lease
        ON metadata_resolution_runs(status, lease_expires_at_utc);
        """;

    private const string MetadataResolutionStages = """
        ALTER TABLE metadata_resolution_attempts RENAME TO metadata_resolution_attempts_v6;

        CREATE TABLE metadata_resolution_attempts (
            id TEXT NOT NULL PRIMARY KEY,
            run_id TEXT NOT NULL REFERENCES metadata_resolution_runs(id) ON DELETE CASCADE,
            stage TEXT NOT NULL CHECK (stage IN (
                'input', 'bangumi', 'series', 'season', 'episode',
                'ai', 'validation', 'download_gate')),
            strategy TEXT NOT NULL,
            priority INTEGER,
            result TEXT NOT NULL,
            error_code TEXT,
            reason TEXT,
            retryable INTEGER NOT NULL CHECK (retryable IN (0, 1)),
            attempt_number INTEGER NOT NULL CHECK (attempt_number > 0),
            duration_ms INTEGER NOT NULL CHECK (duration_ms >= 0),
            created_at_utc TEXT NOT NULL
        ) STRICT;

        INSERT INTO metadata_resolution_attempts (
            id, run_id, stage, strategy, priority, result, error_code,
            reason, retryable, attempt_number, duration_ms, created_at_utc)
        SELECT id, run_id, stage, strategy, priority, result, error_code,
               reason, retryable, attempt_number, duration_ms, created_at_utc
        FROM metadata_resolution_attempts_v6;

        DROP TABLE metadata_resolution_attempts_v6;
        """;

    private const string DownloadFilePreparation = """
        ALTER TABLE download_jobs
        ADD COLUMN preparation_state TEXT NOT NULL DEFAULT 'not_required'
        CHECK (preparation_state IN ('not_required', 'pending', 'preparing', 'completed'));

        ALTER TABLE download_jobs
        ADD COLUMN preparation_lease_token TEXT;

        ALTER TABLE download_jobs
        ADD COLUMN preparation_lease_expires_at_utc TEXT;

        ALTER TABLE download_jobs
        ADD COLUMN preparation_attempt_count INTEGER NOT NULL DEFAULT 0
        CHECK (preparation_attempt_count >= 0);

        ALTER TABLE download_jobs
        ADD COLUMN preparation_next_attempt_at_utc TEXT;

        ALTER TABLE download_jobs
        ADD COLUMN preparation_failure_code TEXT;

        ALTER TABLE task_files
        ADD COLUMN download_file_index INTEGER CHECK (download_file_index >= 0);

        ALTER TABLE task_files
        ADD COLUMN download_priority INTEGER CHECK (download_priority BETWEEN 0 AND 7);

        ALTER TABLE task_files
        ADD COLUMN download_wanted INTEGER CHECK (download_wanted IN (0, 1));

        CREATE INDEX ix_download_jobs_preparation
        ON download_jobs(preparation_state, preparation_next_attempt_at_utc, updated_at_utc);
        """;

    private const string DownloadPathSnapshot = """
        ALTER TABLE download_jobs
        ADD COLUMN download_root_path TEXT;

        ALTER TABLE download_jobs
        ADD COLUMN save_root_path TEXT;
        """;

    private const string MediaOrganizationLease = """
        ALTER TABLE download_jobs
        ADD COLUMN organization_state TEXT NOT NULL DEFAULT 'not_required'
        CHECK (organization_state IN ('not_required', 'pending', 'organizing', 'cleanup', 'completed'));

        ALTER TABLE download_jobs ADD COLUMN organization_lease_token TEXT;
        ALTER TABLE download_jobs ADD COLUMN organization_lease_expires_at_utc TEXT;
        ALTER TABLE download_jobs ADD COLUMN organization_attempt_count INTEGER NOT NULL DEFAULT 0
        CHECK (organization_attempt_count >= 0);
        ALTER TABLE download_jobs ADD COLUMN organization_next_attempt_at_utc TEXT;
        ALTER TABLE download_jobs ADD COLUMN organization_failure_code TEXT;

        CREATE INDEX ix_download_jobs_organization
        ON download_jobs(organization_state, organization_next_attempt_at_utc, updated_at_utc);

        CREATE UNIQUE INDEX ux_file_operations_task_file
        ON file_operations(task_file_id);
        """;

    private const string SubtitleEpisodeAssociation = """
        ALTER TABLE task_files
        ADD COLUMN associated_task_file_id TEXT REFERENCES task_files(id) ON DELETE SET NULL;

        ALTER TABLE task_files
        ADD COLUMN rename_suffix TEXT;

        CREATE INDEX ix_task_files_associated ON task_files(associated_task_file_id);
        """;

    private const string AuditableDeletePlans = """
        ALTER TABLE delete_executions ADD COLUMN plan_fingerprint TEXT;
        ALTER TABLE delete_executions ADD COLUMN lease_token TEXT;
        ALTER TABLE delete_executions ADD COLUMN lease_expires_at_utc TEXT;
        ALTER TABLE delete_executions ADD COLUMN attempt_count INTEGER NOT NULL DEFAULT 0
        CHECK (attempt_count >= 0);
        ALTER TABLE delete_executions ADD COLUMN next_attempt_at_utc TEXT;

        CREATE TABLE delete_execution_items (
            id TEXT NOT NULL PRIMARY KEY,
            execution_id TEXT NOT NULL REFERENCES delete_executions(id) ON DELETE CASCADE,
            item_kind TEXT NOT NULL CHECK (item_kind IN (
                'business_record', 'downloader_task', 'source_file', 'media_file')),
            target_key TEXT NOT NULL,
            root_path TEXT,
            downloader_id TEXT,
            display_value TEXT NOT NULL,
            ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
            state TEXT NOT NULL CHECK (state IN ('pending', 'completed', 'skipped', 'failed')),
            failure_code TEXT,
            completed_at_utc TEXT,
            UNIQUE (execution_id, item_kind, target_key)
        ) STRICT;

        CREATE INDEX ix_delete_execution_items_pending
        ON delete_execution_items(execution_id, state, ordinal);

        CREATE UNIQUE INDEX ux_delete_executions_active_task
        ON delete_executions(task_id) WHERE state IN ('pending', 'executing');
        """;

    private const string MikanRssRuleStorage = """
        CREATE TABLE mikan_rss_rule_sets (
            source_profile_id TEXT NOT NULL PRIMARY KEY REFERENCES source_profiles(id) ON DELETE CASCADE,
            revision INTEGER NOT NULL CHECK (revision > 0),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE mikan_rss_priority_groups (
            source_profile_id TEXT NOT NULL REFERENCES mikan_rss_rule_sets(source_profile_id) ON DELETE CASCADE,
            id TEXT NOT NULL,
            name TEXT NOT NULL,
            position INTEGER NOT NULL CHECK (position >= 0),
            PRIMARY KEY (source_profile_id, id),
            UNIQUE (source_profile_id, position)
        ) STRICT;

        CREATE TABLE mikan_rss_match_arrays (
            source_profile_id TEXT NOT NULL REFERENCES mikan_rss_rule_sets(source_profile_id) ON DELETE CASCADE,
            id TEXT NOT NULL,
            scope TEXT NOT NULL CHECK (scope IN ('whitelist', 'blacklist', 'priority')),
            group_id TEXT,
            name TEXT NOT NULL,
            enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
            position INTEGER NOT NULL CHECK (position >= 0),
            PRIMARY KEY (source_profile_id, id),
            FOREIGN KEY (source_profile_id, group_id)
                REFERENCES mikan_rss_priority_groups(source_profile_id, id) ON DELETE CASCADE,
            CHECK ((scope = 'priority' AND group_id IS NOT NULL)
                OR (scope IN ('whitelist', 'blacklist') AND group_id IS NULL))
        ) STRICT;

        CREATE UNIQUE INDEX ux_mikan_rss_match_arrays_list_position
        ON mikan_rss_match_arrays(source_profile_id, scope, position)
        WHERE group_id IS NULL;

        CREATE UNIQUE INDEX ux_mikan_rss_match_arrays_group_position
        ON mikan_rss_match_arrays(source_profile_id, group_id, position)
        WHERE group_id IS NOT NULL;

        CREATE TABLE mikan_rss_match_values (
            source_profile_id TEXT NOT NULL,
            array_id TEXT NOT NULL,
            position INTEGER NOT NULL CHECK (position >= 0),
            value_lower TEXT NOT NULL CHECK (length(value_lower) > 0 AND value_lower = lower(value_lower)),
            PRIMARY KEY (source_profile_id, array_id, position),
            UNIQUE (source_profile_id, array_id, value_lower),
            FOREIGN KEY (source_profile_id, array_id)
                REFERENCES mikan_rss_match_arrays(source_profile_id, id) ON DELETE CASCADE
        ) STRICT;
        """;

    private const string MikanRssBatchAudit = """
        CREATE TABLE mikan_rss_batches (
            id TEXT NOT NULL PRIMARY KEY,
            source_profile_id TEXT NOT NULL REFERENCES source_profiles(id),
            rule_revision INTEGER NOT NULL CHECK (rule_revision > 0),
            fingerprint TEXT NOT NULL UNIQUE CHECK (
                length(fingerprint) = 64 AND fingerprint = lower(fingerprint)),
            mikanid INTEGER CHECK (mikanid > 0),
            priority_enabled INTEGER NOT NULL CHECK (priority_enabled IN (0, 1)),
            entry_count INTEGER NOT NULL CHECK (entry_count >= 0),
            created_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE mikan_rss_batch_entries (
            batch_id TEXT NOT NULL REFERENCES mikan_rss_batches(id) ON DELETE CASCADE,
            candidate_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
            title TEXT NOT NULL,
            mikan_url TEXT NOT NULL,
            torrent_url_fingerprint TEXT NOT NULL CHECK (
                length(torrent_url_fingerprint) = 64 AND torrent_url_fingerprint = lower(torrent_url_fingerprint)),
            content_type TEXT NOT NULL,
            length_bytes INTEGER NOT NULL CHECK (length_bytes >= 0),
            published_date TEXT,
            source_episode_kind TEXT CHECK (source_episode_kind IN ('normal', 'fractional', 'special')),
            source_episode TEXT,
            decision_kind TEXT NOT NULL CHECK (decision_kind IN (
                'Winner', 'RejectedByBlacklist', 'RejectedByWhitelist', 'SuppressedByHigherPriority')),
            decision_reason TEXT NOT NULL,
            winner_candidate_id TEXT,
            effect_state TEXT NOT NULL CHECK (effect_state IN ('blocked', 'ready', 'claimed', 'ingested')),
            claim_token TEXT,
            claim_expires_at_utc TEXT,
            ingest_task_id TEXT REFERENCES ingest_tasks(id),
            PRIMARY KEY (batch_id, candidate_id),
            UNIQUE (batch_id, ordinal),
            CHECK ((source_episode_kind IS NULL AND source_episode IS NULL)
                OR (source_episode_kind IS NOT NULL AND source_episode IS NOT NULL)),
            CHECK ((decision_kind = 'Winner' AND effect_state IN ('ready', 'claimed', 'ingested'))
                OR (decision_kind <> 'Winner' AND effect_state = 'blocked')),
            CHECK ((effect_state = 'claimed' AND claim_token IS NOT NULL AND claim_expires_at_utc IS NOT NULL)
                OR (effect_state <> 'claimed' AND claim_token IS NULL AND claim_expires_at_utc IS NULL)),
            CHECK ((effect_state = 'ingested' AND ingest_task_id IS NOT NULL)
                OR (effect_state <> 'ingested' AND ingest_task_id IS NULL))
        ) STRICT;

        CREATE TABLE mikan_rss_decision_groups (
            batch_id TEXT NOT NULL,
            candidate_id TEXT NOT NULL,
            position INTEGER NOT NULL CHECK (position >= 0),
            group_id TEXT NOT NULL,
            PRIMARY KEY (batch_id, candidate_id, position),
            FOREIGN KEY (batch_id, candidate_id)
                REFERENCES mikan_rss_batch_entries(batch_id, candidate_id) ON DELETE CASCADE
        ) STRICT;

        CREATE INDEX ix_mikan_rss_entries_effect
        ON mikan_rss_batch_entries(effect_state, claim_expires_at_utc, batch_id, ordinal);
        """;

    private const string LegacyMikanFilterStorage = """
        CREATE TABLE legacy_mikan_filter_sets (
            source_profile_id TEXT NOT NULL PRIMARY KEY REFERENCES source_profiles(id) ON DELETE CASCADE,
            revision INTEGER NOT NULL CHECK (revision > 0),
            updated_source TEXT NOT NULL CHECK (updated_source IN ('migration', 'legacy_api', 'web', 'rollback')),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE legacy_mikan_filter_rules (
            source_profile_id TEXT NOT NULL REFERENCES legacy_mikan_filter_sets(source_profile_id) ON DELETE CASCADE,
            tier INTEGER NOT NULL CHECK (tier BETWEEN 0 AND 4),
            legacy_key TEXT NOT NULL,
            position INTEGER NOT NULL CHECK (position >= 0),
            whitelist_enabled INTEGER NOT NULL CHECK (whitelist_enabled IN (0, 1)),
            blacklist_enabled INTEGER NOT NULL CHECK (blacklist_enabled IN (0, 1)),
            PRIMARY KEY (source_profile_id, tier, legacy_key),
            UNIQUE (source_profile_id, tier, position)
        ) STRICT;

        CREATE TABLE legacy_mikan_filter_values (
            source_profile_id TEXT NOT NULL,
            tier INTEGER NOT NULL,
            legacy_key TEXT NOT NULL,
            list_kind TEXT NOT NULL CHECK (list_kind IN ('whitelist', 'blacklist')),
            position INTEGER NOT NULL CHECK (position >= 0),
            value TEXT NOT NULL,
            PRIMARY KEY (source_profile_id, tier, legacy_key, list_kind, position),
            FOREIGN KEY (source_profile_id, tier, legacy_key)
                REFERENCES legacy_mikan_filter_rules(source_profile_id, tier, legacy_key) ON DELETE CASCADE
        ) STRICT;

        CREATE TABLE legacy_mikan_filter_snapshots (
            source_profile_id TEXT NOT NULL REFERENCES legacy_mikan_filter_sets(source_profile_id) ON DELETE CASCADE,
            revision INTEGER NOT NULL CHECK (revision > 0),
            config_json TEXT NOT NULL CHECK (json_valid(config_json) AND json_type(config_json) = 'object'),
            updated_source TEXT NOT NULL CHECK (updated_source IN ('migration', 'legacy_api', 'web', 'rollback')),
            created_at_utc TEXT NOT NULL,
            PRIMARY KEY (source_profile_id, revision)
        ) STRICT;
        """;

    private const string MikanLegacyFilterAudit = """
        ALTER TABLE mikan_rss_batches
        ADD COLUMN legacy_filter_revision INTEGER NOT NULL DEFAULT 1 CHECK (legacy_filter_revision > 0);

        ALTER TABLE mikan_rss_batches
        ADD COLUMN legacy_filter_enabled INTEGER NOT NULL DEFAULT 0 CHECK (legacy_filter_enabled IN (0, 1));

        DROP INDEX ix_mikan_rss_entries_effect;

        CREATE TABLE mikan_rss_batch_entries_v16 (
            batch_id TEXT NOT NULL REFERENCES mikan_rss_batches(id) ON DELETE CASCADE,
            candidate_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
            title TEXT NOT NULL,
            mikan_url TEXT NOT NULL,
            torrent_url_fingerprint TEXT NOT NULL CHECK (
                length(torrent_url_fingerprint) = 64 AND torrent_url_fingerprint = lower(torrent_url_fingerprint)),
            content_type TEXT NOT NULL,
            length_bytes INTEGER NOT NULL CHECK (length_bytes >= 0),
            published_date TEXT,
            source_episode_kind TEXT CHECK (source_episode_kind IN ('normal', 'fractional', 'special')),
            source_episode TEXT,
            decision_kind TEXT NOT NULL CHECK (decision_kind IN (
                'Winner', 'RejectedByBlacklist', 'RejectedByWhitelist', 'SuppressedByHigherPriority',
                'RejectedByLegacyFilter', 'FilterEvaluationFailed')),
            decision_reason TEXT NOT NULL,
            winner_candidate_id TEXT,
            legacy_filter_state TEXT NOT NULL CHECK (legacy_filter_state IN (
                'NotEvaluated', 'Accepted', 'Rejected', 'SkippedByConfiguration', 'FilterEvaluationFailed')),
            legacy_filter_reason TEXT NOT NULL,
            legacy_filter_scope TEXT,
            legacy_filter_key TEXT,
            identity_mikanid INTEGER CHECK (identity_mikanid > 0),
            identity_groupid INTEGER CHECK (identity_groupid > 0),
            effect_state TEXT NOT NULL CHECK (effect_state IN ('blocked', 'ready', 'claimed', 'ingested')),
            claim_token TEXT,
            claim_expires_at_utc TEXT,
            ingest_task_id TEXT REFERENCES ingest_tasks(id),
            PRIMARY KEY (batch_id, candidate_id),
            UNIQUE (batch_id, ordinal),
            CHECK ((source_episode_kind IS NULL AND source_episode IS NULL)
                OR (source_episode_kind IS NOT NULL AND source_episode IS NOT NULL)),
            CHECK ((decision_kind = 'Winner' AND effect_state IN ('ready', 'claimed', 'ingested'))
                OR (decision_kind <> 'Winner' AND effect_state = 'blocked')),
            CHECK ((effect_state = 'claimed' AND claim_token IS NOT NULL AND claim_expires_at_utc IS NOT NULL)
                OR (effect_state <> 'claimed' AND claim_token IS NULL AND claim_expires_at_utc IS NULL)),
            CHECK ((effect_state = 'ingested' AND ingest_task_id IS NOT NULL)
                OR (effect_state <> 'ingested' AND ingest_task_id IS NULL))
        ) STRICT;

        INSERT INTO mikan_rss_batch_entries_v16 (
            batch_id, candidate_id, ordinal, title, mikan_url, torrent_url_fingerprint,
            content_type, length_bytes, published_date, source_episode_kind, source_episode,
            decision_kind, decision_reason, winner_candidate_id,
            legacy_filter_state, legacy_filter_reason,
            effect_state, claim_token, claim_expires_at_utc, ingest_task_id)
        SELECT batch_id, candidate_id, ordinal, title, mikan_url, torrent_url_fingerprint,
            content_type, length_bytes, published_date, source_episode_kind, source_episode,
            decision_kind, decision_reason, winner_candidate_id,
            'NotEvaluated', 'LegacyFilterNotRecorded',
            effect_state, claim_token, claim_expires_at_utc, ingest_task_id
        FROM mikan_rss_batch_entries;

        CREATE TABLE mikan_rss_decision_groups_v16 (
            batch_id TEXT NOT NULL,
            candidate_id TEXT NOT NULL,
            position INTEGER NOT NULL CHECK (position >= 0),
            group_id TEXT NOT NULL,
            PRIMARY KEY (batch_id, candidate_id, position),
            FOREIGN KEY (batch_id, candidate_id)
                REFERENCES mikan_rss_batch_entries_v16(batch_id, candidate_id) ON DELETE CASCADE
        ) STRICT;

        INSERT INTO mikan_rss_decision_groups_v16 (batch_id, candidate_id, position, group_id)
        SELECT batch_id, candidate_id, position, group_id FROM mikan_rss_decision_groups;

        DROP TABLE mikan_rss_decision_groups;
        DROP TABLE mikan_rss_batch_entries;
        ALTER TABLE mikan_rss_batch_entries_v16 RENAME TO mikan_rss_batch_entries;
        ALTER TABLE mikan_rss_decision_groups_v16 RENAME TO mikan_rss_decision_groups;

        CREATE INDEX ix_mikan_rss_entries_effect
        ON mikan_rss_batch_entries(effect_state, claim_expires_at_utc, batch_id, ordinal);
        """;

    private const string SourceDownloadPolicy = """
        ALTER TABLE source_profiles
        ADD COLUMN category TEXT NOT NULL DEFAULT 'animegonet'
            CHECK (length(category) BETWEEN 1 AND 64);

        ALTER TABLE source_profiles
        ADD COLUMN tags_json TEXT NOT NULL DEFAULT '[]'
            CHECK (json_valid(tags_json) AND json_type(tags_json) = 'array');

        ALTER TABLE source_profiles
        ADD COLUMN seeding_time_minutes INTEGER NOT NULL DEFAULT 0
            CHECK (seeding_time_minutes BETWEEN -1 AND 5256000
                AND (file_strategy <> 'move' OR seeding_time_minutes = 0));
        """;

    private const string EnableAllFileStrategies = """
        UPDATE download_jobs
        SET organization_state = 'pending'
        WHERE organization_state = 'not_required'
          AND task_id IN (
              SELECT id
              FROM ingest_tasks
              WHERE json_extract(route_snapshot_json, '$.file_strategy')
                    IN ('link', 'link_delete', 'wait_move'));
        """;

    private const string MikanPublicationEvidence = """
        ALTER TABLE ingest_tasks
        ADD COLUMN source_published_at_raw TEXT;

        ALTER TABLE ingest_tasks
        ADD COLUMN source_published_at TEXT;
        """;

    private const string PendingTmdbRecovery = """
        ALTER TABLE completion_aliases
        ADD COLUMN fallback_scope_kind TEXT
            CHECK (fallback_scope_kind IS NULL OR fallback_scope_kind IN (
                'bangumi_episode', 'mikan_episode', 'source_work_episode', 'torrent_file'));

        ALTER TABLE completion_aliases
        ADD COLUMN fallback_scope_key TEXT;

        CREATE UNIQUE INDEX ux_completion_aliases_fallback_scope
        ON completion_aliases(fallback_scope_kind, fallback_scope_key)
        WHERE fallback_scope_kind IS NOT NULL AND fallback_scope_key IS NOT NULL;

        ALTER TABLE fallback_completion_records
        ADD COLUMN resolution_state TEXT NOT NULL DEFAULT 'pending'
            CHECK (resolution_state IN ('pending', 'resolved', 'duplicate_after_resolution'));

        ALTER TABLE fallback_completion_records
        ADD COLUMN resolved_completion_id TEXT
            REFERENCES completion_records(id) ON DELETE CASCADE;

        ALTER TABLE fallback_completion_records
        ADD COLUMN resolved_at_utc TEXT;

        ALTER TABLE fallback_completion_records
        ADD COLUMN resolution_source TEXT
            CHECK (resolution_source IS NULL OR resolution_source IN ('manual', 'automatic'));

        CREATE TRIGGER completion_alias_fallback_pair_insert_guard
        BEFORE INSERT ON completion_aliases
        WHEN (NEW.fallback_scope_kind IS NULL) <> (NEW.fallback_scope_key IS NULL)
        BEGIN
            SELECT RAISE(ABORT, 'fallback alias identity must be complete');
        END;

        CREATE TRIGGER completion_alias_fallback_pair_update_guard
        BEFORE UPDATE OF fallback_scope_kind, fallback_scope_key ON completion_aliases
        WHEN (NEW.fallback_scope_kind IS NULL) <> (NEW.fallback_scope_key IS NULL)
        BEGIN
            SELECT RAISE(ABORT, 'fallback alias identity must be complete');
        END;

        CREATE TRIGGER fallback_completion_resolution_insert_guard
        BEFORE INSERT ON fallback_completion_records
        WHEN (NEW.resolution_state = 'pending'
                  AND (NEW.resolved_completion_id IS NOT NULL
                       OR NEW.resolved_at_utc IS NOT NULL
                       OR NEW.resolution_source IS NOT NULL))
          OR (NEW.resolution_state <> 'pending'
                  AND (NEW.resolved_completion_id IS NULL
                       OR NEW.resolved_at_utc IS NULL
                       OR NEW.resolution_source IS NULL))
        BEGIN
            SELECT RAISE(ABORT, 'fallback resolution projection is inconsistent');
        END;

        CREATE TRIGGER fallback_completion_resolution_update_guard
        BEFORE UPDATE OF resolution_state, resolved_completion_id, resolved_at_utc, resolution_source
        ON fallback_completion_records
        WHEN (NEW.resolution_state = 'pending'
                  AND (NEW.resolved_completion_id IS NOT NULL
                       OR NEW.resolved_at_utc IS NOT NULL
                       OR NEW.resolution_source IS NOT NULL))
          OR (NEW.resolution_state <> 'pending'
                  AND (NEW.resolved_completion_id IS NULL
                       OR NEW.resolved_at_utc IS NULL
                       OR NEW.resolution_source IS NULL))
        BEGIN
            SELECT RAISE(ABORT, 'fallback resolution projection is inconsistent');
        END;
        """;

    private const string PendingTmdbNfoRewriteJobs = """
        CREATE TABLE pending_tmdb_nfo_rewrite_jobs (
            id TEXT NOT NULL PRIMARY KEY,
            bangumi_subject_id INTEGER NOT NULL CHECK (bangumi_subject_id > 0),
            tmdb_series_id INTEGER NOT NULL CHECK (tmdb_series_id > 0),
            save_root_path TEXT NOT NULL,
            series_directory_name TEXT NOT NULL,
            canonical_series_name TEXT NOT NULL,
            state TEXT NOT NULL CHECK (state IN ('pending', 'writing', 'completed', 'failed')),
            lease_token TEXT,
            lease_expires_at_utc TEXT,
            attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
            next_attempt_at_utc TEXT,
            failure_code TEXT,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            completed_at_utc TEXT,
            UNIQUE (
                bangumi_subject_id, tmdb_series_id, save_root_path, series_directory_name),
            CHECK (
                (state = 'writing' AND lease_token IS NOT NULL AND lease_expires_at_utc IS NOT NULL)
                OR (state <> 'writing' AND lease_token IS NULL AND lease_expires_at_utc IS NULL))
        ) STRICT;

        CREATE INDEX ix_pending_tmdb_nfo_rewrite_jobs_ready
        ON pending_tmdb_nfo_rewrite_jobs(state, next_attempt_at_utc, updated_at_utc);
        """;

    private const string SqliteJsonCache = """
        CREATE TABLE cache_buckets (
            database_name TEXT NOT NULL
                CHECK (database_name IN ('bolt', 'bolt_sub')),
            name TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            PRIMARY KEY (database_name, name)
        ) STRICT;

        CREATE TABLE cache_entries (
            database_name TEXT NOT NULL,
            bucket_name TEXT NOT NULL,
            key TEXT NOT NULL,
            value_json TEXT NOT NULL CHECK (json_valid(value_json)),
            expires_at_utc TEXT,
            updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (database_name, bucket_name, key),
            FOREIGN KEY (database_name, bucket_name)
                REFERENCES cache_buckets(database_name, name)
                ON DELETE CASCADE
        ) STRICT;

        CREATE INDEX ix_cache_entries_expiry
        ON cache_entries(expires_at_utc)
        WHERE expires_at_utc IS NOT NULL;
        """;

    private const string LibraryTmdbProjection = """
        ALTER TABLE anime_series
            ADD COLUMN first_air_date TEXT;

        ALTER TABLE anime_seasons
            ADD COLUMN air_date TEXT;

        ALTER TABLE anime_seasons
            ADD COLUMN episode_count INTEGER NOT NULL DEFAULT 0
                CHECK (episode_count >= 0);
        """;

    private const string DownloadJobAuditEvents = """
        CREATE TABLE download_job_events (
            id TEXT NOT NULL PRIMARY KEY,
            job_id TEXT NOT NULL REFERENCES download_jobs(id) ON DELETE CASCADE,
            kind TEXT NOT NULL,
            result TEXT NOT NULL,
            from_state TEXT,
            to_state TEXT,
            failure_code TEXT,
            created_at_utc TEXT NOT NULL,
            CHECK (length(kind) BETWEEN 1 AND 64),
            CHECK (length(result) BETWEEN 1 AND 64),
            CHECK (failure_code IS NULL OR length(failure_code) BETWEEN 1 AND 128)
        ) STRICT;

        CREATE INDEX ix_download_job_events_job_time
            ON download_job_events(job_id, created_at_utc DESC, id DESC);

        INSERT INTO download_job_events (
            id, job_id, kind, result, from_state, to_state, failure_code, created_at_utc)
        SELECT 'migration-' || id, id, 'projection_initialized', 'observed',
               NULL, state, NULL, updated_at_utc
        FROM download_jobs;
        """;

    private const string MikanRssRuleSnapshots = """
        CREATE TABLE mikan_rss_rule_snapshots (
            source_profile_id TEXT NOT NULL REFERENCES source_profiles(id) ON DELETE CASCADE,
            revision INTEGER NOT NULL CHECK (revision > 0),
            created_at_utc TEXT NOT NULL,
            PRIMARY KEY (source_profile_id, revision)
        ) STRICT;

        CREATE TABLE mikan_rss_snapshot_priority_groups (
            source_profile_id TEXT NOT NULL,
            revision INTEGER NOT NULL,
            id TEXT NOT NULL,
            name TEXT NOT NULL,
            position INTEGER NOT NULL CHECK (position >= 0),
            PRIMARY KEY (source_profile_id, revision, id),
            UNIQUE (source_profile_id, revision, position),
            FOREIGN KEY (source_profile_id, revision)
                REFERENCES mikan_rss_rule_snapshots(source_profile_id, revision)
                ON DELETE CASCADE
        ) STRICT;

        CREATE TABLE mikan_rss_snapshot_match_arrays (
            source_profile_id TEXT NOT NULL,
            revision INTEGER NOT NULL,
            id TEXT NOT NULL,
            scope TEXT NOT NULL CHECK (scope IN ('whitelist', 'blacklist', 'priority')),
            group_id TEXT,
            name TEXT NOT NULL,
            enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
            position INTEGER NOT NULL CHECK (position >= 0),
            PRIMARY KEY (source_profile_id, revision, id),
            FOREIGN KEY (source_profile_id, revision)
                REFERENCES mikan_rss_rule_snapshots(source_profile_id, revision)
                ON DELETE CASCADE,
            FOREIGN KEY (source_profile_id, revision, group_id)
                REFERENCES mikan_rss_snapshot_priority_groups(source_profile_id, revision, id)
                ON DELETE CASCADE,
            CHECK ((scope = 'priority' AND group_id IS NOT NULL)
                OR (scope IN ('whitelist', 'blacklist') AND group_id IS NULL))
        ) STRICT;

        CREATE UNIQUE INDEX ux_mikan_rss_snapshot_array_list_position
        ON mikan_rss_snapshot_match_arrays(source_profile_id, revision, scope, position)
        WHERE group_id IS NULL;

        CREATE UNIQUE INDEX ux_mikan_rss_snapshot_array_group_position
        ON mikan_rss_snapshot_match_arrays(source_profile_id, revision, group_id, position)
        WHERE group_id IS NOT NULL;

        CREATE TABLE mikan_rss_snapshot_match_values (
            source_profile_id TEXT NOT NULL,
            revision INTEGER NOT NULL,
            array_id TEXT NOT NULL,
            position INTEGER NOT NULL CHECK (position >= 0),
            value_lower TEXT NOT NULL CHECK (
                length(value_lower) > 0 AND value_lower = lower(value_lower)),
            PRIMARY KEY (source_profile_id, revision, array_id, position),
            UNIQUE (source_profile_id, revision, array_id, value_lower),
            FOREIGN KEY (source_profile_id, revision, array_id)
                REFERENCES mikan_rss_snapshot_match_arrays(source_profile_id, revision, id)
                ON DELETE CASCADE
        ) STRICT;

        INSERT INTO mikan_rss_rule_snapshots (
            source_profile_id, revision, created_at_utc)
        SELECT source_profile_id, revision, updated_at_utc
        FROM mikan_rss_rule_sets;

        INSERT INTO mikan_rss_snapshot_priority_groups (
            source_profile_id, revision, id, name, position)
        SELECT groups.source_profile_id, sets.revision, groups.id, groups.name, groups.position
        FROM mikan_rss_priority_groups AS groups
        JOIN mikan_rss_rule_sets AS sets
          ON sets.source_profile_id = groups.source_profile_id;

        INSERT INTO mikan_rss_snapshot_match_arrays (
            source_profile_id, revision, id, scope, group_id, name, enabled, position)
        SELECT arrays.source_profile_id, sets.revision, arrays.id, arrays.scope,
               arrays.group_id, arrays.name, arrays.enabled, arrays.position
        FROM mikan_rss_match_arrays AS arrays
        JOIN mikan_rss_rule_sets AS sets
          ON sets.source_profile_id = arrays.source_profile_id;

        INSERT INTO mikan_rss_snapshot_match_values (
            source_profile_id, revision, array_id, position, value_lower)
        SELECT rule_values.source_profile_id, sets.revision, rule_values.array_id,
               rule_values.position, rule_values.value_lower
        FROM mikan_rss_match_values AS rule_values
        JOIN mikan_rss_rule_sets AS sets
          ON sets.source_profile_id = rule_values.source_profile_id;
        """;

    private const string MikanBangumiDiscoveryAudit = """
        ALTER TABLE mikan_rss_batches
        ADD COLUMN bangumi_subject_id INTEGER CHECK (bangumi_subject_id > 0);

        ALTER TABLE mikan_rss_batches
        ADD COLUMN bangumi_discovery_state TEXT NOT NULL DEFAULT 'not_attempted'
            CHECK (bangumi_discovery_state IN (
                'not_attempted', 'resolved', 'not_found', 'failed', 'not_applicable'));

        ALTER TABLE mikan_rss_batches
        ADD COLUMN bangumi_discovery_failure_code TEXT
            CHECK (bangumi_discovery_failure_code IS NULL
                OR length(bangumi_discovery_failure_code) BETWEEN 1 AND 128);

        CREATE TRIGGER tr_mikan_rss_batches_discovery_insert
        BEFORE INSERT ON mikan_rss_batches
        WHEN NOT (
            (NEW.bangumi_discovery_state = 'resolved'
                AND NEW.bangumi_subject_id IS NOT NULL
                AND NEW.bangumi_discovery_failure_code IS NULL)
            OR (NEW.bangumi_discovery_state = 'not_attempted'
                AND NEW.bangumi_subject_id IS NULL
                AND NEW.bangumi_discovery_failure_code IS NULL)
            OR (NEW.bangumi_discovery_state IN ('not_found', 'failed', 'not_applicable')
                AND NEW.bangumi_subject_id IS NULL
                AND NEW.bangumi_discovery_failure_code IS NOT NULL))
        BEGIN
            SELECT RAISE(ABORT, 'invalid mikan bangumi discovery state');
        END;

        CREATE TRIGGER tr_mikan_rss_batches_discovery_update
        BEFORE UPDATE OF bangumi_subject_id, bangumi_discovery_state, bangumi_discovery_failure_code
        ON mikan_rss_batches
        WHEN NOT (
            (NEW.bangumi_discovery_state = 'resolved'
                AND NEW.bangumi_subject_id IS NOT NULL
                AND NEW.bangumi_discovery_failure_code IS NULL)
            OR (NEW.bangumi_discovery_state = 'not_attempted'
                AND NEW.bangumi_subject_id IS NULL
                AND NEW.bangumi_discovery_failure_code IS NULL)
            OR (NEW.bangumi_discovery_state IN ('not_found', 'failed', 'not_applicable')
                AND NEW.bangumi_subject_id IS NULL
                AND NEW.bangumi_discovery_failure_code IS NOT NULL))
        BEGIN
            SELECT RAISE(ABORT, 'invalid mikan bangumi discovery state');
        END;
        """;
}
