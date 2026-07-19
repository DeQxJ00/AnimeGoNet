namespace AnimeGoNet.Data.Sqlite;

public static class DatabaseSchema
{
    public const int CurrentVersion = 3;

    internal static IReadOnlyList<SchemaMigration> Migrations { get; } =
    [
        new SchemaMigration(1, "initial_business_schema", InitialBusinessSchema),
        new SchemaMigration(2, "source_torrent_host_allowlist", SourceTorrentHostAllowlist),
        new SchemaMigration(3, "staged_ingest_lifecycle", StagedIngestLifecycle),
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
}
