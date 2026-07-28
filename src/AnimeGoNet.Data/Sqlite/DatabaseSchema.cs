namespace AnimeGoNet.Data.Sqlite;

public static class DatabaseSchema
{
    public const int CurrentVersion = 21;

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
    ];

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
}
