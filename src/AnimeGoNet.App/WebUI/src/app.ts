interface RuntimeStatus {
  database_schema_version: number;
  native_aot: boolean;
  runtime_identifier: string;
  paths: { data_path: string };
  capabilities: Record<string, boolean>;
}

interface DirectoryDatabaseStatus {
  refresh_cron: string;
  entry_count: number;
  last_run_id: string | null;
  last_run_status: "running" | "completed" | "failed" | null;
  last_scanned_count: number;
  last_indexed_count: number;
  last_rejected_count: number;
  last_failure_code: string | null;
  last_started_at_utc: string | null;
  last_completed_at_utc: string | null;
}

interface DataUpdateVersion {
  data_version: string;
  state: "active" | "inactive";
  subject_count: number;
  episode_count: number;
  installed_at_utc: string;
  activated_at_utc: string | null;
}

interface DataUpdateDownload {
  data_version: string;
  state: "verified" | "imported";
  downloaded_at_utc: string;
  imported_at_utc: string | null;
}

interface DataUpdateRun {
  run_id: string;
  operation: "import" | "rollback";
  data_version: string | null;
  status: "running" | "completed" | "failed";
  failure_code: string | null;
  subject_count: number;
  episode_count: number;
  started_at_utc: string;
  completed_at_utc: string | null;
}

interface DataUpdateTransferRun {
  run_id: string;
  trigger_kind: "manual" | "scheduled";
  requested_action: "check" | "download" | "download_import";
  status: string;
  data_version: string | null;
  failure_code: string | null;
  downloaded_bytes: number;
  total_bytes: number;
  started_at_utc: string;
  completed_at_utc: string | null;
}

interface DataUpdateStatus {
  scheduled_enabled: boolean;
  cron: string;
  manifest_configured: boolean;
  auto_download: boolean;
  auto_import: boolean;
  keep_versions: number;
  active_version: string | null;
  previous_version: string | null;
  state_updated_at_utc: string;
  versions: DataUpdateVersion[];
  downloads: DataUpdateDownload[];
  last_package_run: DataUpdateRun | null;
  last_transfer_run: DataUpdateTransferRun | null;
}

interface DataUpdateActionResult {
  run_id: string;
  status: string;
  data_version: string | null;
  active_version: string | null;
  downloaded: boolean;
  imported: boolean;
}

type LiveLogLevel =
  | "trace"
  | "debug"
  | "information"
  | "warning"
  | "error"
  | "critical"
  | "unknown";

interface LiveLogEntry {
  level: LiveLogLevel;
  text: string;
}

interface LiveLogFrameHeader {
  type: "log" | "control";
  count?: number;
  action?: string;
  status?: string;
  code?: string;
}

interface RuntimeConfiguration {
  configuration_revision: number;
  applied_configuration_revision: number;
  restart_required: boolean;
  downloads_blocked: boolean;
  migration_diagnostics: ConfigurationMigrationDiagnostic[];
  paths: {
    data_path: string;
    download_path: string;
    save_path: string;
  };
  deployment: {
    running_in_container: boolean;
    background_workers_enabled: boolean;
    access_key_configured: boolean;
    paths_restart_required: boolean;
  };
  metadata: {
    tmdb: {
      base_url: string;
      proxy_url: string | null;
      language: string;
      http_timeout_seconds: number;
      retry_count: number;
      retry_delay_seconds: number;
      api_key_configured: boolean;
      read_access_token_configured: boolean;
    };
    bangumi: {
      base_url: string;
      proxy_url: string | null;
      http_timeout_seconds: number;
      retry_count: number;
      retry_delay_seconds: number;
    };
    season_failure: {
      skip: boolean;
      backtrace: boolean;
      use_title_season: boolean;
      use_first_season: boolean;
    };
    ai: {
      use_metadata_match: boolean;
      use_season_match: boolean;
      use_episode_match: boolean;
      http_timeout_seconds: number;
    };
    tmdb_failure_use_bangumi: boolean;
    mikan_trusted_offset_cache_enabled: boolean;
  };
  torrent_fetch: {
    http_timeout_seconds: number;
    max_response_bytes: number;
    max_redirects: number;
    staging_ttl_seconds: number;
  };
  data_update: {
    enabled: boolean;
    cron: string;
    manifest_url: string | null;
    auto_download: boolean;
    auto_import: boolean;
    keep_versions: number;
    http_timeout_seconds: number;
    hot_reload_supported: boolean;
  };
  editable: {
    tmdb_base_url: string;
    tmdb_proxy_url: string | null;
    tmdb_language: string;
    tmdb_http_timeout_seconds: number;
    tmdb_retry_count: number;
    tmdb_retry_delay_seconds: number;
    tmdb_api_key_state: "inherit" | "configured" | "cleared";
    tmdb_read_access_token_state: "inherit" | "configured" | "cleared";
    bangumi_base_url: string;
    bangumi_proxy_url: string | null;
    bangumi_http_timeout_seconds: number;
    bangumi_retry_count: number;
    bangumi_retry_delay_seconds: number;
    season_failure_skip: boolean;
    season_failure_backtrace: boolean;
    season_failure_use_title_season: boolean;
    season_failure_use_first_season: boolean;
    ai_use_metadata_match: boolean;
    ai_use_season_match: boolean;
    ai_use_episode_match: boolean;
    ai_http_timeout_seconds: number;
    tmdb_failure_use_bangumi: boolean;
    mikan_trusted_offset_cache_enabled: boolean;
    torrent_http_timeout_seconds: number;
    torrent_max_response_bytes: number;
    torrent_max_redirects: number;
    torrent_staging_ttl_seconds: number;
    data_update_enabled: boolean;
    data_update_cron: string;
    data_update_manifest_url: string | null;
    data_update_auto_download: boolean;
    data_update_auto_import: boolean;
    data_update_keep_versions: number;
    data_update_http_timeout_seconds: number;
    locked_fields: Array<{
      field: string;
      source: "environment";
      environment_variables: string[];
    }>;
  };
}

interface ConfigurationMigrationDiagnostic {
  code: string;
  source: string;
  legacy_downloader_type: string;
  message: string;
  blocks_downloads: boolean;
}

interface ConfigurationUpdatePayload {
  tmdb_base_url: string;
  tmdb_proxy_url: string | null;
  tmdb_language: string;
  tmdb_http_timeout_seconds: number;
  tmdb_retry_count: number;
  tmdb_retry_delay_seconds: number;
  tmdb_api_key: string | null;
  clear_tmdb_api_key: boolean;
  tmdb_read_access_token: string | null;
  clear_tmdb_read_access_token: boolean;
  bangumi_base_url: string;
  bangumi_proxy_url: string | null;
  bangumi_http_timeout_seconds: number;
  bangumi_retry_count: number;
  bangumi_retry_delay_seconds: number;
  season_failure_skip: boolean;
  season_failure_backtrace: boolean;
  season_failure_use_title_season: boolean;
  season_failure_use_first_season: boolean;
  ai_use_metadata_match: boolean;
  ai_http_timeout_seconds: number;
  tmdb_failure_use_bangumi: boolean;
  mikan_trusted_offset_cache_enabled: boolean;
  torrent_http_timeout_seconds: number;
  torrent_max_response_bytes: number;
  torrent_max_redirects: number;
  torrent_staging_ttl_seconds: number;
  data_update_enabled: boolean;
  data_update_cron: string;
  data_update_manifest_url: string | null;
  data_update_auto_download: boolean;
  data_update_auto_import: boolean;
  data_update_keep_versions: number;
  data_update_http_timeout_seconds: number;
  expected_configuration_revision: number;
}

interface ConfigurationChangePreview {
  field: string;
  before: string | null;
  after: string | null;
  effect: "restart" | "hot_reload";
  sensitive: boolean;
}

interface ConfigurationPreview {
  expected_configuration_revision: number;
  current_configuration_revision: number;
  restart_required: boolean;
  data_update_hot_reload: boolean;
  changes: ConfigurationChangePreview[];
}

interface ConfigurationWriteResult {
  configuration_revision: number;
  restart_required: boolean;
  reverted_to_deployment_default: boolean;
  backup_revision: number | null;
}

interface DownloadItem {
  job_id: string;
  task_id: string;
  title: string;
  source: string;
  downloader_id: string;
  info_hash: string;
  state: string;
  business_status: string;
  progress: number;
  downloaded_bytes: number;
  total_bytes: number;
  speed_bytes_per_second: number;
  seeds: number;
  peers: number;
  is_stale: boolean;
  revision: number;
  downloader_failure_code: string | null;
}

interface DownloadListPage {
  page: number;
  page_size: number;
  total_items: number;
  search: string | null;
  state: string | null;
  business_status: string | null;
  downloader_id: string | null;
  source: string | null;
  summary: {
    total_jobs: number;
    active_jobs: number;
    paused_jobs: number;
    failed_jobs: number;
    stale_jobs: number;
    waiting_organization_jobs: number;
    completed_jobs: number;
    preparation_failed_jobs: number;
    organization_failed_jobs: number;
    connected_download_speed_bytes_per_second: number;
    offline_instance_count: number;
    latest_failure_code: string | null;
    last_downloader_success_at_utc: string | null;
  };
  items: DownloadItem[];
}

interface DownloadFileDetail {
  relative_path: string;
  size_bytes: number;
  file_index: number | null;
  wanted: boolean | null;
  priority: number | null;
  progress: number | null;
  downloaded_bytes: number | null;
  disposition: string;
  other_reason: string | null;
}

interface DownloadTimelineItem {
  event_id: string;
  kind: string;
  result: string;
  from_state: string | null;
  to_state: string | null;
  failure_code: string | null;
  created_at_utc: string;
}

interface DownloadDetail {
  summary: DownloadItem;
  task_failure_kind: string | null;
  task_failure_reason: string | null;
  preparation: {
    state: string;
    attempt_count: number;
    next_attempt_at_utc: string | null;
    failure_code: string | null;
  };
  organization: {
    state: string;
    attempt_count: number;
    next_attempt_at_utc: string | null;
    failure_code: string | null;
  };
  file_snapshot_state: "live" | "unavailable";
  file_snapshot_failure_code: string | null;
  can_pause: boolean;
  can_resume: boolean;
  can_retry: boolean;
  files: DownloadFileDetail[];
  timeline: DownloadTimelineItem[];
}

interface DownloadUiState {
  page: number;
  page_size: 10 | 25 | 50;
  search: string;
  state: string;
  business_status: string;
  downloader_id: string;
  source: string;
}

interface MetadataItem {
  task_id: string;
  title: string;
  source: string;
  status: string;
  mikanid: number | null;
  bgmid: number | null;
  tmdb_series_id: number | null;
  tmdb_season_number: number | null;
  series_strategy: string | null;
  season_strategy: string | null;
  episode_strategy: string | null;
  failure_kind: string | null;
  failure_reason: string | null;
  failure_stage: string | null;
  failure_code: string | null;
  failure_retryable: boolean | null;
  handling_category: string;
  episode_file_count: number;
  other_file_count: number;
  duplicate_file_count: number;
  pending_file_count: number;
}

interface MetadataTaskListPage {
  page: number;
  page_size: number;
  total_items: number;
  sort: "updated" | "title" | "status" | "failure";
  direction: "asc" | "desc";
  items: MetadataItem[];
}

interface MetadataUiState {
  page: number;
  page_size: 10 | 25 | 50;
  search: string;
  status: string;
  handling: string;
  failure_stage: string;
  error_code: string;
  retryability: string;
  sort: "updated" | "title" | "status" | "failure";
  direction: "asc" | "desc";
}

interface MetadataTaskDetail {
  summary: MetadataItem;
  ai: {
    status: string;
    stage: string | null;
    error_code: string | null;
    reason: string | null;
    confidence_basis: "tmdb_verified" | "not_established";
    duration_ms: number | null;
    attempted_at_utc: string | null;
  };
  files: Array<{
    source_name: string;
    size_bytes: number;
    source_episode: string | null;
    file_episode_candidate: string | null;
    disposition: string;
    other_reason: string | null;
    tmdb_series_id: number | null;
    tmdb_series_name: string | null;
    tmdb_season_number: number | null;
    tmdb_season_name: string | null;
    tmdb_episode_number: number | null;
    tmdb_episode_name: string | null;
  }>;
}

interface MetadataAttemptItem {
  attempt_id: string;
  run_id: string;
  run_attempt_number: number;
  run_status: string;
  stage: string;
  strategy: string;
  priority: number | null;
  result: string;
  error_code: string | null;
  reason: string | null;
  retryable: boolean;
  attempt_number: number;
  duration_ms: number;
  created_at_utc: string;
  run_started_at_utc: string;
  run_completed_at_utc: string | null;
}

type AnimeLibrarySort = "last_updated" | "name" | "air_date" | "added_at";
type AnimeLibraryDirection = "asc" | "desc";
type AnimeEpisodeFilter = "all" | "downloaded" | "not_downloaded";

interface AnimeSeasonListItem {
  id: string;
  tmdb_series_id: number;
  tmdb_season_number: number;
  display_name: string;
  sort_name: string;
  season_name: string;
  poster_path: string | null;
  poster_source: "season" | "series" | "placeholder";
  poster_url: string;
  air_date: string | null;
  added_at_utc: string;
  last_updated_at_utc: string;
  resource_revision: string;
  episode_total: number;
  episode_snapshot_count: number;
  episode_downloaded: number;
  series_resolution_source: string | null;
  season_resolution_source: string | null;
  validation_status: string;
  last_resolution_run_id: string | null;
  warnings: string[];
}

interface AnimeSeasonListPage {
  page: number;
  page_size: number;
  total_items: number;
  sort: AnimeLibrarySort;
  direction: AnimeLibraryDirection;
  items: AnimeSeasonListItem[];
}

interface AnimeEpisodeItem {
  id: string;
  tmdb_episode_id: number;
  episode_number: number;
  name: string | null;
  air_date: string | null;
  runtime_minutes: number | null;
  fetched_at_utc: string;
  status: "downloaded" | "not_downloaded";
  source_id: string | null;
  downloaded_at_utc: string | null;
  media_path_known: boolean;
}

interface AnimeSeasonManualOffset {
  mikanid: number;
  bgmid: number | null;
  tmdb_series_id: number | null;
  tmdb_season_number: number | null;
  episode_offset: number;
  enabled: boolean;
  revision: number;
  updated_at_utc: string;
}

interface AnimeSeasonRelatedTask {
  task_id: string;
  title: string;
  source_id: string;
  status: string;
  mikanid: number | null;
  bgmid: number | null;
  latest_run_attempt_number: number | null;
  latest_run_status: string | null;
  updated_at_utc: string;
}

interface AnimeSeasonResolutionAttempt {
  task_id: string;
  task_title: string;
  run_attempt_number: number;
  run_status: string;
  stage: string;
  strategy: string;
  priority: number | null;
  result: string;
  error_code: string | null;
  reason: string | null;
  retryable: boolean;
  attempt_number: number;
  duration_ms: number;
  created_at_utc: string;
}

interface AnimeSeasonDetail {
  id: string;
  tmdb_series_id: number;
  tmdb_season_number: number;
  display_name: string;
  season_name: string;
  poster_path: string | null;
  poster_source: "season" | "series" | "placeholder";
  poster_url: string;
  air_date: string | null;
  added_at_utc: string;
  last_updated_at_utc: string;
  resource_revision: string;
  episode_total: number;
  episode_snapshot_count: number;
  episode_downloaded: number;
  series_resolution_source: string | null;
  season_resolution_source: string | null;
  validation_status: string;
  last_resolution_run_id: string | null;
  warnings: string[];
  episodes: AnimeEpisodeItem[];
  manual_offsets: AnimeSeasonManualOffset[];
  related_task_total: number;
  related_tasks_truncated: boolean;
  related_tasks: AnimeSeasonRelatedTask[];
  resolution_attempt_total: number;
  resolution_attempts_truncated: boolean;
  resolution_attempts: AnimeSeasonResolutionAttempt[];
}

interface AnimeLibraryUiState {
  sort: AnimeLibrarySort;
  direction: AnimeLibraryDirection;
  page: number;
  page_size: 12 | 24 | 48;
  episode_filter: AnimeEpisodeFilter;
  active_series_id: number | null;
  active_season_number: number | null;
}

interface PendingTmdbSummary {
  bgmid: number;
  fallback_name: string;
  season_numbers: number[];
  task_count: number;
  processed_file_count: number;
  fallback_record_count: number;
  active_claim_count: number;
  completed_claim_count: number;
  duplicate_file_count: number;
  latest_failure_kind: string | null;
  latest_failure_reason: string | null;
  updated_at_utc: string;
}

interface PendingTmdbTask {
  task_id: string;
  title: string;
  source: string;
  status: string;
  season_number: number | null;
  other_file_count: number;
  duplicate_file_count: number;
  failure_kind: string | null;
  failure_reason: string | null;
  updated_at_utc: string;
}

interface PendingTmdbScope {
  kind: string;
  state: string;
  source: string;
  source_episode: string | null;
  dedup_boundary: string;
  cross_source_duplicate_risk: boolean;
  completed_at_utc: string | null;
}

interface PendingTmdbRecoveryCandidate {
  fallback_record_id: string;
  source: string;
  source_episode: string | null;
  dedup_boundary: string;
  completed_at_utc: string;
}

interface PendingTmdbDetail {
  summary: PendingTmdbSummary;
  tasks: PendingTmdbTask[];
  scopes: PendingTmdbScope[];
  recovery_candidates: PendingTmdbRecoveryCandidate[];
}

interface PendingTmdbRecoveryResult {
  bgmid: number;
  tmdb_series_id: number;
  has_pending_fallback_records: boolean;
  items: Array<{
    fallback_record_id: string;
    tmdb_season_number: number;
    tmdb_episode_number: number;
    state: "Resolved" | "DuplicateAfterResolution";
  }>;
}

interface MikanTrustedOffsetItem {
  mikanid: number;
  groupid: number;
  tmdb_series_id: number;
  tmdb_season_number: number;
  episode_offset: number;
  distinct_episode_count: number;
  required_episode_count: number;
  state: "learning" | "trusted" | "conflict_reset";
  updated_at_utc: string;
}

interface DeleteTarget {
  display_value: string;
}

type DeleteFlag =
  | "delete_business_record"
  | "delete_downloader_task"
  | "delete_source_files"
  | "delete_media_files";
type DeleteCollection = "business_records" | "downloader_tasks" | "source_files" | "media_files";

interface DeletePreview {
  task_id: string;
  title: string;
  task_status: string;
  fingerprint: string;
  business_records: DeleteTarget[];
  downloader_tasks: DeleteTarget[];
  source_files: DeleteTarget[];
  media_files: DeleteTarget[];
}

interface DeleteCreateResponse {
  execution_id: string;
  selected_target_count: number;
}

interface ApiError {
  message?: string;
}

interface RssNamedArray {
  id: string;
  name: string;
  enabled: boolean;
  values: string[];
}

interface RssPriorityGroup {
  id: string;
  name: string;
  arrays: RssNamedArray[];
}

interface RssRuleSnapshot {
  source_profile_id: string;
  rss_filter_enabled: boolean;
  rss_priority_enabled: boolean;
  revision: number;
  whitelist: RssNamedArray[];
  blacklist: RssNamedArray[];
  priority_groups: RssPriorityGroup[];
  snapshots: { revision: number; created_at_utc: string }[];
}

interface RssRuleDecision {
  candidate_id: string;
  decision: string;
  reason: string;
  winner_id: string | null;
  evaluated_priority_groups: string[];
}

interface LegacyMikanFilterRule {
  tier: number;
  position: number;
  key: string;
  whitelist_enabled: boolean;
  blacklist_enabled: boolean;
  whitelist: string[];
  blacklist: string[];
}

interface LegacyMikanFilterSnapshot {
  revision: number;
  updated_source: string;
  created_at_utc: string;
}

interface LegacyMikanFilterResponse {
  source_profile_id: string;
  revision: number;
  updated_source: string;
  created_at_utc: string;
  updated_at_utc: string;
  legacy_json: string;
  rules: LegacyMikanFilterRule[];
  snapshots: LegacyMikanFilterSnapshot[];
}

interface LegacyMikanFilterTrace {
  tier: string;
  key: string | null;
  applicable: boolean;
  accepted: boolean | null;
  whitelist_matches: string[];
  blacklist_matches: string[];
  reason: string;
}

interface LegacyMikanFilterPreview {
  accepted: boolean;
  reason: string;
  matched_scope: string | null;
  matched_key: string | null;
  derived_group_name: string;
  steps: LegacyMikanFilterTrace[];
}

interface ManualIngestItem {
  index: number;
  status: string;
  ingest_id: string | null;
  source_profile_id: string | null;
  source_profile_revision: number | null;
  downloader_id: string | null;
  torrent_url_fingerprint: string | null;
  info_hash: string | null;
  file_count: number | null;
  errors: string[];
}

interface ManualIngestResponse {
  source: string;
  accepted_count: number;
  rejected_count: number;
  items: ManualIngestItem[];
}

interface ManualRssItem {
  decision_kind: string;
  decision_reason: string;
  status: string;
  ingest_task_id: string | null;
  errors: string[];
}

interface ManualRssResponse {
  batch_id: string;
  mikanid: number | null;
  bgmid: number | null;
  bgmid_discovery_state: string;
  bgmid_discovery_failure_code: string | null;
  rule_revision: number;
  legacy_filter_revision: number;
  legacy_filter_enabled: boolean;
  items: ManualRssItem[];
}

interface MikanWorkRule {
  mikanid: number;
  bgmid: number | null;
  tmdb_series_id: number | null;
  tmdb_season_number: number | null;
  episode_offset: number | null;
  enabled: boolean;
  revision: number;
  created_at_utc: string;
  updated_at_utc: string;
}

type MikanWorkImpactCategory =
  | "future"
  | "retryable_failed"
  | "active"
  | "resolved_protected"
  | "completed_protected"
  | "other";

interface MikanWorkImpactTask {
  task_id: string;
  title: string;
  source: string;
  status: string;
  bgmid: number | null;
  tmdb_series_id: number | null;
  tmdb_season_number: number | null;
  organization_state: string | null;
  category: MikanWorkImpactCategory;
  updated_at_utc: string;
}

interface MikanWorkImpact {
  mikanid: number;
  total_task_count: number;
  future_task_count: number;
  retryable_failed_task_count: number;
  active_task_count: number;
  resolved_protected_task_count: number;
  completed_protected_task_count: number;
  other_task_count: number;
  is_truncated: boolean;
  items: MikanWorkImpactTask[];
}

interface MikanWorkRematchResponse {
  mikanid: number;
  rule_revision: number;
  retried_task_count: number;
}

interface SourceProfile {
  id: string;
  display_name: string;
  adapter: "mikan" | "u2" | "ttg";
  downloader_id: string;
  file_strategy: "link" | "link_delete" | "move" | "wait_move";
  allowed_torrent_hosts: string[];
  category: string;
  tags: string[];
  seeding_time_minutes: number;
  rss_filter_enabled: boolean;
  rss_priority_enabled: boolean;
  enabled: boolean;
  mikan_identity_cookie_configured: boolean;
  revision: number;
  ingest_task_count: number;
  rss_batch_count: number;
  is_default: boolean;
  file_strategy_warning: string | null;
}

interface SourceProfileList {
  items: SourceProfile[];
}

interface SourceRoutePreview {
  valid: boolean;
  errors: string[];
  source_profile_id: string;
  source_profile_revision: number;
  adapter: string;
  downloader_id: string;
  downloader_enabled: boolean;
  download_path: string | null;
  save_path: string;
  file_strategy: string;
  category: string;
  tags: string[];
  seeding_time_minutes: number;
  rss_filter_enabled: boolean;
  rss_priority_enabled: boolean;
  rss_rule_revision: number | null;
}

interface DownloaderInstance {
  id: string;
  type: string;
  base_url: string;
  download_path: string;
  enabled: boolean;
  credentials_configured: boolean;
  configuration_source: string;
  override_revision: number | null;
  restart_required: boolean;
  source_profile_count: number;
  ingest_task_count: number;
  download_job_count: number;
  connected: boolean | null;
  failure_code: string | null;
  last_success_at_utc: string | null;
  circuit_state: string | null;
  circuit_failure_count: number;
  circuit_retry_at_utc: string | null;
}

interface DownloaderInstanceList {
  configuration_revision: number;
  applied_configuration_revision: number;
  restart_required: boolean;
  downloads_blocked: boolean;
  migration_diagnostics: ConfigurationMigrationDiagnostic[];
  items: DownloaderInstance[];
}

interface DownloaderConnectionTest {
  id: string;
  connected: boolean;
  task_count: number | null;
  latency_ms: number;
  failure_code: string | null;
  message: string;
  client_version: string | null;
  client_default_save_path: string | null;
}

interface DownloaderPathProbe {
  id: string;
  success: boolean;
  hard_link_supported: boolean;
  download_path: string;
  save_path: string;
  failure_code: string | null;
  message: string;
}

interface DeleteGroup {
  flag: DeleteFlag;
  label: string;
  collection: DeleteCollection;
  help: string;
}

function element<T extends Element>(selector: string): T {
  const found = document.querySelector<T>(selector);
  if (!found) throw new Error(`Required WebUI element is missing: ${selector}`);
  return found;
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

async function responseError(response: Response): Promise<string> {
  const body = await response.json().catch(() => null) as ApiError | null;
  return body?.message ?? `HTTP ${response.status}`;
}

const accessKey = new URLSearchParams(window.location.search).get("access_key");
const headers = new Headers();
if (accessKey) headers.set("Access-Key", accessKey);
const deleteDialog = element<HTMLDialogElement>("#delete-dialog");
const deleteConfirm = element<HTMLButtonElement>("#delete-confirm");
const downloaderConfigDialog = element<HTMLDialogElement>("#downloader-config-dialog");
const configurationDialog = element<HTMLDialogElement>("#configuration-dialog");
let activeDeletePreview: DeletePreview | null = null;
let currentConfiguration: RuntimeConfiguration | null = null;
let activeRssRules: RssRuleSnapshot | null = null;
let activeLegacyMikanFilter: LegacyMikanFilterResponse | null = null;
let sourceProfiles: SourceProfile[] = [];
let activeSourceId: string | null = null;
let downloaderInstances: DownloaderInstance[] = [];
let downloaderConfigurationRevision = 0;
let activeDownloaderId: string | null = null;
let ruleIdSequence = 0;
const libraryStorageKey = "animegonet.library.v1";
let libraryState = readLibraryState();
const downloadStorageKey = "animegonet.downloads.v1";
let downloadState = readDownloadState();
const metadataStorageKey = "animegonet.metadata-tasks.v1";
let metadataState = readMetadataState();
const expandedDownloadJobIds = new Set<string>();
let activeLibraryDetail: AnimeSeasonDetail | null = null;
let libraryListRequestSequence = 0;
let libraryDetailRequestSequence = 0;
let activeMikanWorkRule: MikanWorkRule | null = null;
let loadedMikanWorkId: number | null = null;
let activeMikanWorkImpact: MikanWorkImpact | null = null;
let activeConfigurationLockedFields = new Set<string>();
let pendingConfigurationRequest: ConfigurationUpdatePayload | null = null;
const maximumRenderedLogs = 500;
const liveLogLevelOrder: Record<LiveLogLevel, number> = {
  trace: 0,
  debug: 1,
  information: 2,
  warning: 3,
  error: 4,
  critical: 5,
  unknown: 2,
};
let liveLogSocket: WebSocket | null = null;
let liveLogReconnectTimer: number | null = null;
let liveLogReconnectAttempt = 0;
let liveLogShouldReconnect = true;
let liveLogPaused = false;
let liveLogControlPending = false;
let liveLogEntries: LiveLogEntry[] = [];

const statusLabels: Record<string, string> = {
  received: "已接收",
  staged: "种子已暂存",
  dispatching: "正在提交下载器",
  download_preparing: "等待下载前匹配",
  download_queued: "已允许下载",
  download_skipped_duplicate: "重复集已跳过",
  organizing_cleanup: "整理完成，正在清理下载器任务",
  organized: "已整理入库",
  downloading: "下载中",
  downloaded: "等待元数据匹配",
  metadata_resolving: "正在匹配 Series / Season",
  metadata_season_resolved: "季度已确认",
  metadata_episode_resolving: "正在验证 Episode",
  metadata_resolved: "元数据已确认",
  metadata_failed: "元数据失败",
};

const deleteGroups: DeleteGroup[] = [
  { flag: "delete_business_record", label: "业务完成记录", collection: "business_records", help: "删除后该 TMDB 单集可重新导入" },
  { flag: "delete_downloader_task", label: "qBittorrent 任务", collection: "downloader_tasks", help: "只删除任务，永不让 qB 删除文件" },
  { flag: "delete_source_files", label: "下载源文件", collection: "source_files", help: "精确删除捕获下载根目录内的文件" },
  { flag: "delete_media_files", label: "媒体库文件", collection: "media_files", help: "精确删除捕获媒体库根目录内的文件" },
];

async function loadStatus(): Promise<void> {
  const health = element<HTMLElement>("#health");
  try {
    const response = await fetch("/api/v1/status", { headers });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const status = await response.json() as RuntimeStatus;
    element<HTMLElement>("#schema").textContent = `v${status.database_schema_version}`;
    element<HTMLElement>("#runtime").textContent = status.native_aot
      ? `NativeAOT · ${status.runtime_identifier}`
      : `JIT · ${status.runtime_identifier}`;
    element<HTMLElement>("#data-path").textContent = status.paths.data_path;
    const modules = Object.entries(status.capabilities).map(([name, enabled]) => {
      const item = document.createElement("article");
      item.className = `module ${enabled ? "enabled" : ""}`;
      const title = document.createElement("strong");
      title.textContent = name.replaceAll("_", " ");
      const state = document.createElement("span");
      state.textContent = enabled ? "已启用" : "待实现";
      item.append(title, state);
      return item;
    });
    element<HTMLElement>("#modules").replaceChildren(...modules);
    health.textContent = "运行中";
    health.className = "badge ready";
  } catch (error) {
    health.textContent = errorMessage(error, "连接失败");
    health.className = "badge error";
  }
}

async function loadDirectoryDatabase(refresh = false): Promise<void> {
  const target = element<HTMLElement>("#directory-database-status");
  const button = element<HTMLButtonElement>("#directory-database-refresh");
  button.disabled = true;
  if (refresh) target.textContent = "正在刷新…";
  try {
    const response = await fetch(
      refresh
        ? "/api/v1/library/directory-database/refresh"
        : "/api/v1/library/directory-database",
      { method: refresh ? "POST" : "GET", headers },
    );
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const status = await response.json() as DirectoryDatabaseStatus;
    const rejected = status.last_rejected_count > 0
      ? `，拒绝 ${status.last_rejected_count}`
      : "";
    const failure = status.last_failure_code
      ? `，失败 ${status.last_failure_code}`
      : "";
    target.textContent =
      `${status.entry_count} 条索引；最近扫描 ${status.last_scanned_count}，`
      + `写入 ${status.last_indexed_count}${rejected}${failure}；Cron ${status.refresh_cron}`;
  } catch (error) {
    target.textContent = errorMessage(error, "目录数据库状态读取失败");
  } finally {
    button.disabled = false;
  }
}

let dataUpdateActionRunning = false;

const dataUpdateStatusLabels: Record<string, string> = {
  checking: "正在检查 manifest",
  update_available: "发现新版本",
  up_to_date: "已是最新版本",
  downloading: "正在下载并校验",
  downloaded: "已下载，等待确认导入",
  importing: "正在校验并导入",
  completed: "更新完成",
  failed: "更新失败",
  rolled_back: "已回滚",
};

function dataUpdateTime(value: string | null): string {
  if (!value) return "—";
  const parsed = new Date(value);
  return Number.isNaN(parsed.valueOf()) ? value : parsed.toLocaleString();
}

function setDataUpdateBusy(busy: boolean): void {
  dataUpdateActionRunning = busy;
  for (const button of document.querySelectorAll<HTMLButtonElement>(
    "#data-update button",
  )) {
    if (busy) {
      button.disabled = true;
    } else if (button.id === "data-update-reload") {
      button.disabled = false;
    } else if (button.id === "data-update-offline-import") {
      const input = element<HTMLInputElement>("#data-update-offline-package");
      button.disabled = input.files?.length !== 1;
    }
  }
  element<HTMLInputElement>("#data-update-offline-package").disabled = busy;
}

function renderDataUpdateTransfer(status: DataUpdateStatus): void {
  const target = element<HTMLElement>("#data-update-transfer");
  const run = status.last_transfer_run;
  if (!run) {
    target.replaceChildren(Object.assign(document.createElement("p"), {
      className: "muted empty",
      textContent: "暂无检查或下载记录。",
    }));
    return;
  }
  const heading = document.createElement("div");
  heading.className = "data-update-transfer-heading";
  const title = document.createElement("strong");
  title.textContent = dataUpdateStatusLabels[run.status] ?? run.status;
  const identity = document.createElement("span");
  identity.className = `badge ${run.status === "failed" ? "error" : "ready"}`;
  identity.textContent =
    `${run.trigger_kind === "scheduled" ? "定时" : "手动"} · ${run.requested_action}`;
  heading.append(title, identity);

  const details = document.createElement("p");
  details.className = "muted";
  details.textContent =
    `版本 ${run.data_version ?? "—"} · 开始 ${dataUpdateTime(run.started_at_utc)}`
    + `${run.completed_at_utc ? ` · 完成 ${dataUpdateTime(run.completed_at_utc)}` : ""}`
    + `${run.failure_code ? ` · 失败码 ${run.failure_code}` : ""}`;
  const progress = document.createElement("progress");
  progress.max = Math.max(run.total_bytes, 1);
  progress.value = Math.min(run.downloaded_bytes, progress.max);
  progress.setAttribute(
    "aria-label",
    `数据包下载 ${formatBytes(run.downloaded_bytes)} / ${formatBytes(run.total_bytes)}`,
  );
  const progressText = document.createElement("small");
  progressText.textContent = run.total_bytes > 0
    ? `${formatBytes(run.downloaded_bytes)} / ${formatBytes(run.total_bytes)}`
    : "当前阶段没有下载字节";
  target.replaceChildren(heading, details, progress, progressText);
}

function renderDataUpdateVersions(status: DataUpdateStatus): void {
  const target = element<HTMLElement>("#data-update-versions");
  if (status.versions.length === 0) {
    target.replaceChildren(Object.assign(document.createElement("p"), {
      className: "muted empty",
      textContent: "暂无已安装版本。",
    }));
    return;
  }
  target.replaceChildren(...status.versions.map(version => {
    const item = document.createElement("div");
    item.className = `data-update-item ${version.state}`;
    const heading = document.createElement("strong");
    heading.textContent = version.data_version;
    const state = document.createElement("span");
    state.className = `badge ${version.state === "active" ? "ready" : "pending"}`;
    state.textContent = version.state === "active" ? "当前 active" : "可回滚版本";
    const counts = document.createElement("small");
    counts.textContent =
      `${version.subject_count.toLocaleString()} Subject · `
      + `${version.episode_count.toLocaleString()} Episode · `
      + `安装 ${dataUpdateTime(version.installed_at_utc)}`;
    item.append(heading, state, counts);
    return item;
  }));
}

function renderDataUpdateDownloads(status: DataUpdateStatus): void {
  const target = element<HTMLElement>("#data-update-downloads");
  if (status.downloads.length === 0) {
    target.replaceChildren(Object.assign(document.createElement("p"), {
      className: "muted empty",
      textContent: "暂无已下载数据包。",
    }));
    return;
  }
  target.replaceChildren(...status.downloads.map(download => {
    const item = document.createElement("div");
    item.className = "data-update-item";
    const heading = document.createElement("strong");
    heading.textContent = download.data_version;
    const state = document.createElement("span");
    state.className = `badge ${download.state === "imported" ? "ready" : "pending"}`;
    state.textContent = download.state === "imported" ? "已导入" : "已验证，待导入";
    const timestamp = document.createElement("small");
    timestamp.textContent = `下载 ${dataUpdateTime(download.downloaded_at_utc)}`;
    item.append(heading, state, timestamp);
    if (download.state === "verified") {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "primary-button";
      button.textContent = "导入此版本";
      button.disabled = dataUpdateActionRunning;
      button.addEventListener("click", () => void runDataUpdateAction(
        `/api/v1/data-update/downloads/${encodeURIComponent(download.data_version)}/import`,
        `正在导入 ${download.data_version}…`,
      ));
      item.append(button);
    }
    return item;
  }));
}

async function loadDataUpdate(silent = false): Promise<void> {
  const message = element<HTMLElement>("#data-update-status");
  if (!silent) message.textContent = "正在读取数据版本…";
  try {
    const response = await fetch("/api/v1/data-update", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const status = await response.json() as DataUpdateStatus;
    const policy = !status.scheduled_enabled
      ? "定时更新关闭（手动可用）"
      : `定时 ${status.cron} · ${
        !status.auto_download
          ? "仅检查"
          : status.auto_import ? "自动下载并导入" : "自动下载后等待确认"
      }`;
    message.textContent =
      `${policy} · manifest ${status.manifest_configured ? "已配置" : "未配置"} · `
      + `保留 ${status.keep_versions} 版`;
    element<HTMLElement>("#data-update-summary").replaceChildren(
      configurationCard("版本状态", [
        ["当前 active", status.active_version ?? "尚未导入"],
        ["上一可用版", status.previous_version ?? "无"],
        ["状态更新时间", dataUpdateTime(status.state_updated_at_utc)],
        [
          "最近本地导入",
          status.last_package_run
            ? `${status.last_package_run.operation} · ${status.last_package_run.status}`
              + `${status.last_package_run.failure_code
                ? ` · ${status.last_package_run.failure_code}` : ""}`
            : "无",
        ],
      ]),
    );
    renderDataUpdateTransfer(status);
    renderDataUpdateVersions(status);
    renderDataUpdateDownloads(status);
    if (!dataUpdateActionRunning) {
      const requiresManifest = !status.manifest_configured;
      element<HTMLButtonElement>("#data-update-check").disabled = requiresManifest;
      element<HTMLButtonElement>("#data-update-download").disabled = requiresManifest;
      element<HTMLButtonElement>("#data-update-apply").disabled = requiresManifest;
      element<HTMLButtonElement>("#data-update-rollback").disabled =
        status.previous_version === null;
    }
  } catch (error) {
    message.textContent = errorMessage(error, "数据更新状态读取失败");
  }
}

async function runDataUpdateAction(
  endpoint: string,
  pendingMessage: string,
  confirmation?: string,
): Promise<void> {
  if (confirmation && !window.confirm(confirmation)) return;
  const message = element<HTMLElement>("#data-update-status");
  setDataUpdateBusy(true);
  message.textContent = pendingMessage;
  try {
    const response = await fetch(endpoint, { method: "POST", headers });
    if (!response.ok) throw new Error(await responseError(response));
    const result = await response.json() as DataUpdateActionResult;
    message.textContent =
      `${dataUpdateStatusLabels[result.status] ?? result.status} · `
      + `版本 ${result.data_version ?? result.active_version ?? "—"}`;
  } catch (error) {
    message.textContent = errorMessage(error, "数据更新操作失败");
  } finally {
    setDataUpdateBusy(false);
    await loadDataUpdate(true);
  }
}

async function importOfflineDataPackage(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  if (dataUpdateActionRunning) return;
  const input = element<HTMLInputElement>("#data-update-offline-package");
  const file = input.files?.item(0);
  const message = element<HTMLElement>("#data-update-status");
  if (!file) {
    message.textContent = "请选择一个离线数据包 ZIP。";
    return;
  }

  const displayName = file.name;
  input.value = "";
  setDataUpdateBusy(true);
  message.textContent = `正在上传、校验并导入 ${displayName}…`;
  try {
    const uploadHeaders = new Headers(headers);
    uploadHeaders.set("Content-Type", "application/zip");
    const response = await fetch("/api/v1/data-update/offline/import", {
      method: "POST",
      headers: uploadHeaders,
      body: file,
    });
    if (!response.ok) throw new Error(await responseError(response));
    const result = await response.json() as DataUpdateActionResult;
    message.textContent =
      `离线数据包已导入 · 版本 ${result.data_version ?? result.active_version ?? "—"}`;
  } catch (error) {
    message.textContent = errorMessage(error, "离线数据包导入失败");
  } finally {
    setDataUpdateBusy(false);
    await loadDataUpdate(true);
  }
}

function liveLogWebSocketUrl(): string {
  const url = new URL("/websocket/log", window.location.href);
  url.protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
  url.search = "";
  if (accessKey) url.searchParams.set("access_key", accessKey);
  return url.toString();
}

function setLiveLogStatus(message: string, state: string): void {
  const target = element<HTMLElement>("#live-log-status");
  target.textContent = message;
  target.dataset.state = state;
}

function parseLiveLogLevel(line: string): LiveLogLevel {
  const marker = line.match(/\[(TRC|DBG|INF|WRN|ERR|CRT)\]/)?.[1];
  switch (marker) {
    case "TRC": return "trace";
    case "DBG": return "debug";
    case "INF": return "information";
    case "WRN": return "warning";
    case "ERR": return "error";
    case "CRT": return "critical";
    default: return "unknown";
  }
}

function renderLiveLogs(): void {
  const minimum = element<HTMLSelectElement>("#live-log-level").value;
  const minimumOrder = minimum === "all"
    ? -1
    : liveLogLevelOrder[minimum as LiveLogLevel] ?? 2;
  const visible = liveLogEntries.filter(
    entry => liveLogLevelOrder[entry.level] >= minimumOrder,
  );
  const stream = element<HTMLElement>("#live-log-stream");
  if (visible.length === 0) {
    stream.replaceChildren(Object.assign(document.createElement("p"), {
      className: "muted empty",
      textContent: liveLogEntries.length === 0
        ? "等待日志…"
        : "当前级别过滤下没有日志。",
    }));
  } else {
    const nodes = visible.map(entry => {
      const line = document.createElement("div");
      line.className = `live-log-entry ${entry.level}`;
      line.textContent = entry.text;
      return line;
    });
    stream.replaceChildren(...nodes);
    stream.scrollTop = stream.scrollHeight;
  }
  element<HTMLElement>("#live-log-count").textContent =
    `本页 ${liveLogEntries.length} / ${maximumRenderedLogs} 条`
    + (visible.length === liveLogEntries.length ? "" : ` · 显示 ${visible.length}`);
}

function appendLiveLogs(lines: string[]): void {
  const entries = lines
    .filter(line => line.length > 0)
    .map(line => ({ level: parseLiveLogLevel(line), text: line }));
  if (entries.length === 0) return;
  liveLogEntries.push(...entries);
  if (liveLogEntries.length > maximumRenderedLogs) {
    liveLogEntries.splice(0, liveLogEntries.length - maximumRenderedLogs);
  }
  renderLiveLogs();
}

function updateLiveLogPauseButton(): void {
  const button = element<HTMLButtonElement>("#live-log-pause");
  button.textContent = liveLogPaused ? "恢复" : "暂停";
  button.disabled =
    liveLogControlPending || liveLogSocket?.readyState !== WebSocket.OPEN;
}

function handleLiveLogControl(header: LiveLogFrameHeader): void {
  liveLogControlPending = false;
  if (header.status !== "ok") {
    setLiveLogStatus(
      `日志流控制失败：${header.code ?? "unknown_error"}`,
      "error",
    );
    updateLiveLogPauseButton();
    return;
  }
  if (header.action === "pause") {
    liveLogPaused = true;
    setLiveLogStatus("已暂停；服务器正在缓存最新 1000 条", "paused");
  } else if (header.action === "resume") {
    liveLogPaused = false;
    setLiveLogStatus("日志流已连接", "connected");
  }
  updateLiveLogPauseButton();
}

function handleLiveLogMessage(payload: string): void {
  const parts = payload.split("\n\n");
  let header: LiveLogFrameHeader;
  try {
    header = JSON.parse(parts[0] ?? "") as LiveLogFrameHeader;
  } catch {
    setLiveLogStatus("收到无法解析的日志帧，已忽略", "error");
    return;
  }
  if (header.type === "control") {
    handleLiveLogControl(header);
    return;
  }
  if (
    header.type !== "log"
    || !Number.isInteger(header.count)
    || (header.count ?? 0) < 1
    || (header.count ?? 0) > 1000
    || parts.length - 1 < (header.count ?? 0)
  ) {
    setLiveLogStatus("收到无效日志帧，已忽略", "error");
    return;
  }
  appendLiveLogs(parts.slice(1, 1 + header.count!));
}

function scheduleLiveLogReconnect(): void {
  if (!liveLogShouldReconnect || liveLogReconnectTimer !== null) return;
  const delay = Math.min(30000, 1000 * (2 ** liveLogReconnectAttempt));
  liveLogReconnectAttempt++;
  setLiveLogStatus(`连接已断开，${Math.ceil(delay / 1000)} 秒后重试`, "disconnected");
  liveLogReconnectTimer = window.setTimeout(() => {
    liveLogReconnectTimer = null;
    connectLiveLogs();
  }, delay);
}

function disconnectCurrentLiveLogSocket(): void {
  const socket = liveLogSocket;
  liveLogSocket = null;
  if (!socket) return;
  socket.onopen = null;
  socket.onmessage = null;
  socket.onerror = null;
  socket.onclose = null;
  try {
    socket.close(1000, "reconnect");
  } catch {
    // A connecting browser socket can reject close; detached callbacks keep it harmless.
  }
}

function connectLiveLogs(manual = false): void {
  if (manual) liveLogReconnectAttempt = 0;
  if (liveLogReconnectTimer !== null) {
    window.clearTimeout(liveLogReconnectTimer);
    liveLogReconnectTimer = null;
  }
  disconnectCurrentLiveLogSocket();
  setLiveLogStatus("正在连接日志流…", "connecting");
  updateLiveLogPauseButton();

  let socket: WebSocket;
  try {
    socket = new WebSocket(liveLogWebSocketUrl());
  } catch {
    scheduleLiveLogReconnect();
    return;
  }
  liveLogSocket = socket;
  socket.onopen = () => {
    if (liveLogSocket !== socket) return;
    liveLogReconnectAttempt = 0;
    if (liveLogPaused) {
      liveLogControlPending = true;
      socket.send(JSON.stringify({ action: "pause" }));
      setLiveLogStatus("已重连，正在恢复暂停状态…", "connecting");
    } else {
      setLiveLogStatus("日志流已连接", "connected");
    }
    updateLiveLogPauseButton();
  };
  socket.onmessage = event => {
    if (liveLogSocket === socket && typeof event.data === "string") {
      handleLiveLogMessage(event.data);
    }
  };
  socket.onerror = () => {
    if (liveLogSocket === socket) {
      setLiveLogStatus("日志流连接发生错误", "error");
    }
  };
  socket.onclose = () => {
    if (liveLogSocket !== socket) return;
    liveLogSocket = null;
    liveLogControlPending = false;
    updateLiveLogPauseButton();
    scheduleLiveLogReconnect();
  };
}

function toggleLiveLogPause(): void {
  if (!liveLogSocket || liveLogSocket.readyState !== WebSocket.OPEN) {
    setLiveLogStatus("日志流尚未连接，请重新连接", "disconnected");
    return;
  }
  liveLogControlPending = true;
  updateLiveLogPauseButton();
  liveLogSocket.send(JSON.stringify({
    action: liveLogPaused ? "resume" : "pause",
  }));
}

function readLibraryState(): AnimeLibraryUiState {
  const defaults: AnimeLibraryUiState = {
    sort: "last_updated",
    direction: "desc",
    page: 1,
    page_size: 24,
    episode_filter: "all",
    active_series_id: null,
    active_season_number: null,
  };
  try {
    const raw = window.localStorage.getItem(libraryStorageKey);
    if (!raw) return defaults;
    const stored = JSON.parse(raw) as Partial<AnimeLibraryUiState>;
    const sorts: AnimeLibrarySort[] = ["last_updated", "name", "air_date", "added_at"];
    const directions: AnimeLibraryDirection[] = ["asc", "desc"];
    const filters: AnimeEpisodeFilter[] = ["all", "downloaded", "not_downloaded"];
    const pageSizes = [12, 24, 48] as const;
    return {
      sort: sorts.includes(stored.sort as AnimeLibrarySort)
        ? stored.sort as AnimeLibrarySort : defaults.sort,
      direction: directions.includes(stored.direction as AnimeLibraryDirection)
        ? stored.direction as AnimeLibraryDirection : defaults.direction,
      page: Number.isInteger(stored.page) && (stored.page ?? 0) > 0
        ? stored.page! : defaults.page,
      page_size: pageSizes.includes(stored.page_size as 12 | 24 | 48)
        ? stored.page_size as 12 | 24 | 48 : defaults.page_size,
      episode_filter: filters.includes(stored.episode_filter as AnimeEpisodeFilter)
        ? stored.episode_filter as AnimeEpisodeFilter : defaults.episode_filter,
      active_series_id: Number.isInteger(stored.active_series_id)
        && (stored.active_series_id ?? 0) > 0
        ? stored.active_series_id! : null,
      active_season_number: Number.isInteger(stored.active_season_number)
        && (stored.active_season_number ?? 0) > 0
        ? stored.active_season_number! : null,
    };
  } catch {
    return defaults;
  }
}

function saveLibraryState(): void {
  try {
    window.localStorage.setItem(libraryStorageKey, JSON.stringify(libraryState));
  } catch {
    // Browser storage is an optional UI preference; business state remains server-side.
  }
}

function readDownloadState(): DownloadUiState {
  const defaults: DownloadUiState = {
    page: 1,
    page_size: 25,
    search: "",
    state: "",
    business_status: "",
    downloader_id: "",
    source: "",
  };
  try {
    const raw = window.localStorage.getItem(downloadStorageKey);
    if (!raw) return defaults;
    const stored = JSON.parse(raw) as Partial<DownloadUiState>;
    const pageSizes = [10, 25, 50] as const;
    return {
      page: Number.isInteger(stored.page) && (stored.page ?? 0) > 0
        ? stored.page! : defaults.page,
      page_size: pageSizes.includes(stored.page_size as 10 | 25 | 50)
        ? stored.page_size as 10 | 25 | 50 : defaults.page_size,
      search: typeof stored.search === "string"
        ? stored.search.slice(0, 200) : "",
      state: typeof stored.state === "string"
        ? stored.state.slice(0, 64) : "",
      business_status: typeof stored.business_status === "string"
        ? stored.business_status.slice(0, 64) : "",
      downloader_id: typeof stored.downloader_id === "string"
        ? stored.downloader_id.slice(0, 64) : "",
      source: typeof stored.source === "string"
        ? stored.source.slice(0, 64) : "",
    };
  } catch {
    return defaults;
  }
}

function saveDownloadState(): void {
  try {
    window.localStorage.setItem(downloadStorageKey, JSON.stringify(downloadState));
  } catch {
    // Browser storage is an optional UI preference; business state remains server-side.
  }
}

function readMetadataState(): MetadataUiState {
  const defaults: MetadataUiState = {
    page: 1,
    page_size: 25,
    search: "",
    status: "",
    handling: "all",
    failure_stage: "",
    error_code: "",
    retryability: "all",
    sort: "updated",
    direction: "desc",
  };
  try {
    const raw = window.localStorage.getItem(metadataStorageKey);
    if (!raw) return defaults;
    const stored = JSON.parse(raw) as Partial<MetadataUiState>;
    const pageSizes = [10, 25, 50] as const;
    const sorts = ["updated", "title", "status", "failure"] as const;
    return {
      page: Number.isInteger(stored.page) && (stored.page ?? 0) > 0
        ? stored.page! : 1,
      page_size: pageSizes.includes(stored.page_size as 10 | 25 | 50)
        ? stored.page_size as 10 | 25 | 50 : 25,
      search: typeof stored.search === "string" ? stored.search.slice(0, 200) : "",
      status: typeof stored.status === "string" ? stored.status.slice(0, 64) : "",
      handling: typeof stored.handling === "string" ? stored.handling : "all",
      failure_stage: typeof stored.failure_stage === "string"
        ? stored.failure_stage.slice(0, 64) : "",
      error_code: typeof stored.error_code === "string"
        ? stored.error_code.slice(0, 128) : "",
      retryability: typeof stored.retryability === "string"
        ? stored.retryability : "all",
      sort: sorts.includes(stored.sort as MetadataUiState["sort"])
        ? stored.sort as MetadataUiState["sort"] : "updated",
      direction: stored.direction === "asc" ? "asc" : "desc",
    };
  } catch {
    return defaults;
  }
}

function saveMetadataState(): void {
  try {
    window.localStorage.setItem(metadataStorageKey, JSON.stringify(metadataState));
  } catch {
    // Optional UI preference only.
  }
}

function authorizedAssetUrl(path: string): string {
  if (!accessKey) return path;
  const url = new URL(path, window.location.origin);
  url.searchParams.set("access_key", accessKey);
  return `${url.pathname}${url.search}`;
}

function libraryDate(value: string | null, includeTime = false): string {
  if (!value) return "未提供";
  if (!includeTime) return value;
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? value : date.toLocaleString();
}

function libraryStrategy(value: string | null): string {
  const labels: Record<string, string> = {
    manual_override: "人工覆盖",
    tmdb_title: "TMDB 标题搜索",
    tmdb_air_date: "TMDB 开播日期验证",
    bangumi_backtrace: "P3 Bangumi 回溯验证",
    ai_metadata: "AI 统一匹配 + TMDB 验证",
    title_season: "P2 本地任务 title 季度（未验证）",
    first_season: "P1 本地 S01（未验证）",
    pending_tmdb_manual: "待补全 TMDB 人工恢复",
    pending_tmdb_automatic: "待补全 TMDB 自动恢复",
    manual_mikan_offset: "人工 Mikan EP offset + TMDB 验证",
    trusted_mikan_offset: "可信 Mikan EP offset + TMDB 验证",
    tmdb_episode_number: "文件名 EP + TMDB Episode 验证",
  };
  return value ? labels[value] ?? value : "未记录";
}

function libraryWarning(value: string): string {
  const labels: Record<string, string> = {
    episode_snapshot_incomplete: "TMDB EP snapshot 不完整",
    completion_without_snapshot: "存在 snapshot 外完成记录",
    completion_media_path_unknown: "完成记录缺少媒体路径",
    season_not_tmdb_verified: "本地季度尚未通过 TMDB Season 验证",
  };
  return labels[value] ?? value;
}

function libraryValidation(value: string): string {
  const labels: Record<string, string> = {
    verified: "TMDB 已验证",
    local_unverified: "本地季度 · 未验证",
    projection_only: "仅有 TMDB 投影",
  };
  return labels[value] ?? value;
}

function librarySortLabel(value: AnimeLibrarySort): string {
  const labels: Record<AnimeLibrarySort, string> = {
    last_updated: "最后更新时间",
    name: "TMDB 名称",
    air_date: "季度开播日期",
    added_at: "本地加入日期",
  };
  return labels[value];
}

function libraryPoster(
  url: string,
  title: string,
  className: string,
): HTMLImageElement {
  const image = document.createElement("img");
  image.className = className;
  image.src = authorizedAssetUrl(url);
  image.alt = `${title} 封面`;
  image.loading = "lazy";
  image.width = 500;
  image.height = 750;
  image.addEventListener("error", () => {
    image.classList.add("failed");
    image.alt = `${title} 封面加载失败`;
  }, { once: true });
  return image;
}

function libraryProgress(
  downloaded: number,
  total: number,
): { progress: HTMLProgressElement; label: HTMLSpanElement } {
  const progress = document.createElement("progress");
  progress.max = Math.max(total, 1);
  progress.value = Math.min(downloaded, progress.max);
  progress.setAttribute("aria-label", `TMDB EP 完成进度 ${downloaded} / ${total}`);
  const label = document.createElement("span");
  label.textContent = total > 0 ? `${downloaded} / ${total} EP` : "尚无完整 TMDB EP snapshot";
  return { progress, label };
}

function renderLibraryWarnings(values: string[]): HTMLElement {
  const warnings = document.createElement("div");
  warnings.className = "library-warnings";
  warnings.replaceChildren(...values.map((value) => {
    const warning = document.createElement("span");
    warning.textContent = libraryWarning(value);
    return warning;
  }));
  return warnings;
}

function renderLibraryPage(page: AnimeSeasonListPage): void {
  const list = element<HTMLElement>("#library-list");
  list.setAttribute("aria-busy", "false");
  const pageCount = Math.max(1, Math.ceil(page.total_items / page.page_size));
  element<HTMLElement>("#library-status").textContent =
    `${page.total_items} 个季度 · ${librarySortLabel(page.sort)} · `
    + (page.direction === "asc" ? "升序" : "降序");
  element<HTMLElement>("#library-page-label").textContent =
    `第 ${page.page} / ${pageCount} 页`;
  element<HTMLButtonElement>("#library-previous").disabled = page.page <= 1;
  element<HTMLButtonElement>("#library-next").disabled = page.page >= pageCount;
  if (page.items.length === 0) {
    const empty = document.createElement("p");
    empty.className = "muted empty";
    empty.textContent = "作品库暂时为空。只有已确认 TMDB Series 与普通 Season 的作品会显示在这里；tmdbid=0 条目请到“待补全 TMDB”处理。";
    list.replaceChildren(empty);
    return;
  }

  list.replaceChildren(...page.items.map((item) => {
    const card = document.createElement("button");
    card.type = "button";
    card.className = "library-card";
    if (libraryState.active_series_id === item.tmdb_series_id
        && libraryState.active_season_number === item.tmdb_season_number) {
      card.classList.add("active");
    }
    card.setAttribute(
      "aria-label",
      `查看 ${item.display_name} ${item.season_name} 的 TMDB EP 详情`,
    );
    const image = libraryPoster(
      item.poster_url,
      `${item.display_name} ${item.season_name}`,
      "library-poster",
    );
    const content = document.createElement("span");
    content.className = "library-card-content";
    const heading = document.createElement("span");
    heading.className = "library-card-heading";
    const title = document.createElement("strong");
    title.textContent = item.display_name;
    const season = document.createElement("span");
    season.textContent = `${item.season_name} · S${String(item.tmdb_season_number).padStart(2, "0")}`;
    heading.append(title, season);
    const identity = document.createElement("span");
    identity.className = "library-card-identity";
    identity.textContent =
      `TMDB ${item.tmdb_series_id} · 开播 ${libraryDate(item.air_date)} · ${libraryValidation(item.validation_status)}`;
    const progressRow = document.createElement("span");
    progressRow.className = "library-progress";
    const progress = libraryProgress(item.episode_downloaded, item.episode_total);
    progressRow.append(progress.progress, progress.label);
    content.append(heading, identity, progressRow);
    if (item.warnings.length > 0) content.append(renderLibraryWarnings(item.warnings));
    card.append(image, content);
    card.addEventListener("click", () => {
      libraryState.active_series_id = item.tmdb_series_id;
      libraryState.active_season_number = item.tmdb_season_number;
      saveLibraryState();
      renderLibraryPage(page);
      void loadLibraryDetail(item.tmdb_series_id, item.tmdb_season_number, true);
    });
    return card;
  }));
}

function renderLibraryEpisodes(detail: AnimeSeasonDetail): void {
  const container = element<HTMLElement>("#library-episodes");
  const filtered = detail.episodes.filter((episode) =>
    libraryState.episode_filter === "all"
      || episode.status === libraryState.episode_filter);
  element<HTMLElement>("#library-episode-status").textContent =
    `显示 ${filtered.length} / ${detail.episodes.length} 个 TMDB Episode`;
  if (filtered.length === 0) {
    const empty = document.createElement("p");
    empty.className = "muted empty";
    empty.textContent = libraryState.episode_filter === "all"
      ? "当前季度没有可展示的 TMDB Episode snapshot。"
      : "当前筛选条件下没有 Episode。";
    container.replaceChildren(empty);
    return;
  }

  container.replaceChildren(...filtered.map((episode) => {
    const card = document.createElement("details");
    card.className = `library-episode ${episode.status}`;
    const summary = document.createElement("summary");
    summary.setAttribute("role", "button");
    summary.setAttribute("aria-expanded", "false");
    const number = document.createElement("strong");
    number.textContent = `EP ${String(episode.episode_number).padStart(2, "0")}`;
    const status = document.createElement("span");
    status.textContent = episode.status === "downloaded" ? "✓ 已下载" : "○ 未下载";
    summary.append(number, status);
    const name = document.createElement("p");
    name.className = "library-episode-name";
    name.textContent = episode.name || "TMDB 未提供 Episode 名称";
    const metadata = document.createElement("p");
    metadata.className = "library-episode-meta";
    metadata.textContent =
      `TMDB Episode ${episode.tmdb_episode_id} · 开播 ${libraryDate(episode.air_date)} · `
      + `${episode.runtime_minutes === null ? "时长未提供" : `${episode.runtime_minutes} 分钟`} · `
      + `snapshot ${libraryDate(episode.fetched_at_utc, true)}`;
    const completion = document.createElement("p");
    completion.className = "library-episode-completion";
    completion.textContent = episode.status === "downloaded"
      ? `完成于 ${libraryDate(episode.downloaded_at_utc, true)} · 来源 ${episode.source_id ?? "未记录"}`
        + (episode.media_path_known ? "" : " · 媒体路径未记录")
      : "没有规范完成记录；等待、下载中、整理失败或删除完成记录后都保持未下载。";
    card.addEventListener("toggle", () => {
      summary.setAttribute("aria-expanded", String(card.open));
    });
    summary.addEventListener("keydown", (event) => {
      if (event.key !== "Enter" && event.key !== " ") return;
      event.preventDefault();
      card.open = !card.open;
    });
    card.append(summary, name, metadata, completion);
    return card;
  }));
}

function libraryAuditGroup(
  title: string,
  total: number,
  truncated: boolean,
  items: HTMLElement[],
  open = false,
): HTMLDetailsElement {
  const group = document.createElement("details");
  group.className = "library-audit-group";
  group.open = open;
  const summary = document.createElement("summary");
  summary.textContent = `${title} · ${total}${truncated ? "（仅显示最新一部分）" : ""}`;
  const content = document.createElement("div");
  content.className = "library-audit-list";
  if (items.length === 0) {
    const empty = document.createElement("p");
    empty.className = "muted empty";
    empty.textContent = "暂无记录。";
    content.append(empty);
  } else {
    content.append(...items);
  }
  group.append(summary, content);
  return group;
}

function renderLibraryAudit(detail: AnimeSeasonDetail): void {
  const container = element<HTMLElement>("#library-audit");
  const heading = document.createElement("div");
  heading.className = "library-audit-heading";
  const title = document.createElement("h4");
  title.textContent = "元数据审计";
  const status = document.createElement("span");
  status.className = "muted";
  status.textContent =
    `${detail.related_task_total} 个关联任务 · ${detail.resolution_attempt_total} 次策略验证`;
  heading.append(title, status);

  const offsets = detail.manual_offsets.map((offset) => {
    const row = document.createElement("article");
    row.className = `library-audit-item ${offset.enabled ? "ready" : "disabled"}`;
    const name = document.createElement("strong");
    const signedOffset = offset.episode_offset >= 0
      ? `+${offset.episode_offset}` : String(offset.episode_offset);
    name.textContent = `mikanid ${offset.mikanid} · EP ${signedOffset}`;
    const scope = document.createElement("p");
    scope.textContent =
      `当前规则 ${offset.enabled ? "已启用（人工优先级最高）" : "已禁用"}`
      + ` · TMDB ${textOrDash(offset.tmdb_series_id)}`
      + ` / ${offset.tmdb_season_number === null
        ? "全部季度" : `S${String(offset.tmdb_season_number).padStart(2, "0")}`}`;
    const metadata = document.createElement("p");
    metadata.textContent =
      `bgmid ${textOrDash(offset.bgmid)} · revision ${offset.revision}`
      + ` · 更新 ${libraryDate(offset.updated_at_utc, true)}`;
    row.append(name, scope, metadata);
    return row;
  });

  const tasks = detail.related_tasks.map((task) => {
    const row = document.createElement("article");
    row.className = "library-audit-item";
    const name = document.createElement("strong");
    name.textContent = task.title;
    const identity = document.createElement("p");
    identity.textContent =
      `${task.task_id} · ${task.source_id} · ${task.status}`
      + ` · mikanid ${textOrDash(task.mikanid)} · bgmid ${textOrDash(task.bgmid)}`;
    const run = document.createElement("p");
    run.textContent = task.latest_run_attempt_number === null
      ? `尚无解析 Run · 更新 ${libraryDate(task.updated_at_utc, true)}`
      : `最近 Run #${task.latest_run_attempt_number}`
        + `（${textOrDash(task.latest_run_status)}）`
        + ` · 更新 ${libraryDate(task.updated_at_utc, true)}`;
    row.append(name, identity, run);
    return row;
  });

  const attempts = detail.resolution_attempts.map((attempt) => {
    const row = document.createElement("article");
    row.className =
      `metadata-attempt ${attempt.result === "failed" ? "failed" : ""}`;
    const attemptHeading = document.createElement("div");
    attemptHeading.className = "metadata-attempt-heading";
    const strategy = document.createElement("strong");
    strategy.textContent =
      `${attempt.stage} · ${libraryStrategy(attempt.strategy)}`;
    const result = document.createElement("span");
    result.className = `badge ${attempt.result === "failed" ? "error" : "ready"}`;
    result.textContent = attempt.result;
    attemptHeading.append(strategy, result);
    const task = document.createElement("p");
    task.textContent = `${attempt.task_title} · ${attempt.task_id}`;
    const execution = document.createElement("p");
    execution.textContent =
      `P${textOrDash(attempt.priority)} · Run #${attempt.run_attempt_number}`
      + `（${attempt.run_status}）· 尝试 #${attempt.attempt_number}`
      + ` · ${attempt.duration_ms} ms · ${libraryDate(attempt.created_at_utc, true)}`;
    row.append(attemptHeading, task, execution);
    if (attempt.error_code || attempt.reason) {
      const reason = document.createElement("p");
      reason.className = "metadata-attempt-reason";
      reason.textContent =
        `${textOrDash(attempt.error_code)} · ${textOrDash(attempt.reason)}`
        + ` · ${attempt.retryable ? "可重试" : "不可重试"}`;
      row.append(reason);
    }
    return row;
  });

  container.replaceChildren(
    heading,
    libraryAuditGroup(
      "当前人工 EP offset",
      detail.manual_offsets.length,
      false,
      offsets,
      detail.manual_offsets.length > 0,
    ),
    libraryAuditGroup(
      "关联任务",
      detail.related_task_total,
      detail.related_tasks_truncated,
      tasks,
    ),
    libraryAuditGroup(
      "季度级逐次验证时间线",
      detail.resolution_attempt_total,
      detail.resolution_attempts_truncated,
      attempts,
    ),
  );
}

function renderLibraryDetail(detail: AnimeSeasonDetail, focus: boolean): void {
  activeLibraryDetail = detail;
  const panel = element<HTMLElement>("#library-detail");
  panel.hidden = false;
  element<HTMLElement>("#library-detail-title").textContent =
    `${detail.display_name} · ${detail.season_name}`;
  const summary = element<HTMLElement>("#library-detail-summary");
  const layout = document.createElement("div");
  layout.className = "library-detail-layout";
  const image = libraryPoster(
    detail.poster_url,
    `${detail.display_name} ${detail.season_name}`,
    "library-detail-poster",
  );
  const content = document.createElement("div");
  const progressRow = document.createElement("div");
  progressRow.className = "library-detail-progress";
  const progress = libraryProgress(detail.episode_downloaded, detail.episode_total);
  progressRow.append(progress.progress, progress.label);
  const facts = document.createElement("dl");
  facts.className = "library-detail-facts";
  const values: Array<[string, string]> = [
    ["TMDB 身份", `Series ${detail.tmdb_series_id} · Season ${detail.tmdb_season_number}`],
    ["季度开播", libraryDate(detail.air_date)],
    ["本地加入", libraryDate(detail.added_at_utc, true)],
    ["最后更新", libraryDate(detail.last_updated_at_utc, true)],
    ["Series 取得", libraryStrategy(detail.series_resolution_source)],
    ["Season 取得", libraryStrategy(detail.season_resolution_source)],
    ["验证状态", libraryValidation(detail.validation_status)],
    ["最近解析 Run", detail.last_resolution_run_id ?? "未记录"],
    ["EP snapshot", `${detail.episode_snapshot_count} / TMDB 声明 ${detail.episode_total}`],
  ];
  facts.replaceChildren(...values.map(([label, value]) => {
    const row = document.createElement("div");
    const term = document.createElement("dt");
    term.textContent = label;
    const description = document.createElement("dd");
    description.textContent = value;
    row.append(term, description);
    return row;
  }));
  content.append(progressRow, facts);
  if (detail.warnings.length > 0) content.append(renderLibraryWarnings(detail.warnings));
  layout.append(image, content);
  summary.replaceChildren(layout);
  element<HTMLButtonElement>("#library-detail-refresh").disabled = false;
  element<HTMLButtonElement>("#library-detail-delete").disabled = false;
  element<HTMLElement>("#library-detail-action-status").textContent =
    "刷新只更新 TMDB 权威投影；删除不处理业务记录、下载器任务或文件。";
  renderLibraryAudit(detail);
  renderLibraryEpisodes(detail);
  if (focus) {
    panel.scrollIntoView({ behavior: "smooth", block: "start" });
    element<HTMLButtonElement>("#library-detail-close").focus({ preventScroll: true });
  }
}

async function loadLibraryDetail(
  tmdbSeriesId: number,
  seasonNumber: number,
  focus = false,
): Promise<void> {
  const sequence = ++libraryDetailRequestSequence;
  const panel = element<HTMLElement>("#library-detail");
  panel.hidden = false;
  element<HTMLElement>("#library-detail-title").textContent = "正在读取季度详情…";
  element<HTMLElement>("#library-detail-summary").replaceChildren();
  element<HTMLElement>("#library-audit").replaceChildren();
  element<HTMLElement>("#library-episodes").replaceChildren();
  element<HTMLElement>("#library-episode-status").textContent = "";
  element<HTMLButtonElement>("#library-detail-refresh").disabled = true;
  element<HTMLButtonElement>("#library-detail-delete").disabled = true;
  element<HTMLElement>("#library-detail-action-status").textContent = "";
  try {
    const response = await fetch(
      `/api/v1/library/seasons/${tmdbSeriesId}/${seasonNumber}`,
      { headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const detail = await response.json() as AnimeSeasonDetail;
    if (sequence !== libraryDetailRequestSequence) return;
    renderLibraryDetail(detail, focus);
  } catch (error) {
    if (sequence !== libraryDetailRequestSequence) return;
    activeLibraryDetail = null;
    const message = document.createElement("p");
    message.className = "muted empty";
    message.textContent = `季度详情读取失败：${errorMessage(error, "未知错误")}`;
    element<HTMLElement>("#library-detail-summary").replaceChildren(message);
  }
}

async function loadLibrary(): Promise<void> {
  const sequence = ++libraryListRequestSequence;
  const list = element<HTMLElement>("#library-list");
  list.setAttribute("aria-busy", "true");
  element<HTMLElement>("#library-status").textContent = "正在读取作品库…";
  const query = new URLSearchParams({
    page: String(libraryState.page),
    page_size: String(libraryState.page_size),
    sort: libraryState.sort,
    direction: libraryState.direction,
  });
  try {
    const response = await fetch(`/api/v1/library/seasons?${query}`, { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const page = await response.json() as AnimeSeasonListPage;
    if (sequence !== libraryListRequestSequence) return;
    if (page.items.length === 0 && page.total_items > 0 && libraryState.page > 1) {
      libraryState.page = Math.max(1, Math.ceil(page.total_items / page.page_size));
      saveLibraryState();
      await loadLibrary();
      return;
    }
    libraryState.page = page.page;
    libraryState.page_size = page.page_size as 12 | 24 | 48;
    libraryState.sort = page.sort;
    libraryState.direction = page.direction;
    saveLibraryState();
    renderLibraryPage(page);
    if (libraryState.active_series_id !== null
        && libraryState.active_season_number !== null
        && page.items.some((item) =>
          item.tmdb_series_id === libraryState.active_series_id
          && item.tmdb_season_number === libraryState.active_season_number)) {
      void loadLibraryDetail(
        libraryState.active_series_id,
        libraryState.active_season_number,
      );
    } else if (libraryState.active_series_id !== null) {
      closeLibraryDetail();
    }
  } catch (error) {
    if (sequence !== libraryListRequestSequence) return;
    list.setAttribute("aria-busy", "false");
    const failure = document.createElement("p");
    failure.className = "muted empty";
    failure.textContent = `作品库读取失败：${errorMessage(error, "未知错误")}`;
    list.replaceChildren(failure);
    element<HTMLElement>("#library-status").textContent = "读取失败";
  }
}

async function createLibrarySeason(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  const buttonElement = element<HTMLButtonElement>("#library-create");
  const status = element<HTMLElement>("#library-admin-status");
  const tmdbSeriesId = element<HTMLInputElement>("#library-create-series").valueAsNumber;
  const seasonNumber = element<HTMLInputElement>("#library-create-season").valueAsNumber;
  if (!Number.isInteger(tmdbSeriesId) || tmdbSeriesId <= 0
      || !Number.isInteger(seasonNumber) || seasonNumber <= 0) {
    status.textContent = "TMDB Series ID 与 Season 必须是正整数。";
    return;
  }

  buttonElement.disabled = true;
  status.textContent = `正在通过 TMDB 验证 Series ${tmdbSeriesId} / Season ${seasonNumber}…`;
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/library/seasons", {
      method: "POST",
      headers: requestHeaders,
      body: JSON.stringify({
        tmdb_series_id: tmdbSeriesId,
        tmdb_season_number: seasonNumber,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    libraryState.active_series_id = tmdbSeriesId;
    libraryState.active_season_number = seasonNumber;
    libraryState.page = 1;
    saveLibraryState();
    status.textContent =
      `已添加 TMDB ${tmdbSeriesId} / S${String(seasonNumber).padStart(2, "0")}，正在刷新作品库。`;
    await loadLibrary();
    await loadLibraryDetail(tmdbSeriesId, seasonNumber, true);
  } catch (error) {
    status.textContent = `添加失败：${errorMessage(error, "未知错误")}`;
  } finally {
    buttonElement.disabled = false;
  }
}

async function refreshLibrarySeason(): Promise<void> {
  if (!activeLibraryDetail) return;
  const detail = activeLibraryDetail;
  if (!window.confirm(
    `从 TMDB 重新获取 Series ${detail.tmdb_series_id} / Season ${detail.tmdb_season_number}？`
    + " 名称、封面、季度和 EP snapshot 将以 TMDB 当前返回值为准；完成记录不会删除。",
  )) return;

  const refresh = element<HTMLButtonElement>("#library-detail-refresh");
  const remove = element<HTMLButtonElement>("#library-detail-delete");
  const status = element<HTMLElement>("#library-detail-action-status");
  refresh.disabled = true;
  remove.disabled = true;
  status.textContent = "正在验证并刷新 TMDB 权威投影…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(
      `/api/v1/library/seasons/${detail.tmdb_series_id}/${detail.tmdb_season_number}`,
      {
        method: "PUT",
        headers: requestHeaders,
        body: JSON.stringify({ expected_revision: detail.resource_revision }),
      },
    );
    if (!response.ok) throw new Error(await responseError(response));
    status.textContent = "TMDB 权威投影已刷新。";
    await loadLibrary();
    await loadLibraryDetail(detail.tmdb_series_id, detail.tmdb_season_number);
  } catch (error) {
    status.textContent =
      `刷新失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新载入。`;
    refresh.disabled = false;
    remove.disabled = false;
  }
}

async function deleteLibrarySeason(): Promise<void> {
  if (!activeLibraryDetail) return;
  const detail = activeLibraryDetail;
  if (!window.confirm(
    `仅删除 ${detail.display_name} / ${detail.season_name} 的本地 TMDB 投影？`
    + " 服务端会拒绝仍有任务、完成记录、claim、人工规则或待写 NFO 引用的季度。"
    + " 此操作不会删除下载器任务、下载源文件或媒体文件。",
  )) return;

  const refresh = element<HTMLButtonElement>("#library-detail-refresh");
  const remove = element<HTMLButtonElement>("#library-detail-delete");
  const status = element<HTMLElement>("#library-detail-action-status");
  refresh.disabled = true;
  remove.disabled = true;
  status.textContent = "正在检查引用并删除投影…";
  try {
    const query = new URLSearchParams({
      expected_revision: detail.resource_revision,
    });
    const response = await fetch(
      `/api/v1/library/seasons/${detail.tmdb_series_id}/${detail.tmdb_season_number}?${query}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    closeLibraryDetail();
    element<HTMLElement>("#library-admin-status").textContent =
      `已删除 TMDB ${detail.tmdb_series_id} / S${String(detail.tmdb_season_number).padStart(2, "0")} 的无引用投影。`;
    await loadLibrary();
  } catch (error) {
    status.textContent =
      `删除失败：${errorMessage(error, "未知错误")}；有业务引用时请使用四类删除流程。`;
    refresh.disabled = false;
    remove.disabled = false;
  }
}

function closeLibraryDetail(): void {
  libraryDetailRequestSequence++;
  activeLibraryDetail = null;
  libraryState.active_series_id = null;
  libraryState.active_season_number = null;
  saveLibraryState();
  element<HTMLElement>("#library-detail").hidden = true;
  document.querySelectorAll<HTMLElement>(".library-card.active")
    .forEach((card) => card.classList.remove("active"));
}

function changeLibraryOrdering(): void {
  libraryState.sort = element<HTMLSelectElement>("#library-sort")
    .value as AnimeLibrarySort;
  libraryState.direction = element<HTMLSelectElement>("#library-direction")
    .value as AnimeLibraryDirection;
  libraryState.page_size = Number(
    element<HTMLSelectElement>("#library-page-size").value,
  ) as 12 | 24 | 48;
  libraryState.page = 1;
  closeLibraryDetail();
  saveLibraryState();
  void loadLibrary();
}

function configurationCard(title: string, fields: Array<[string, string]>): HTMLElement {
  const card = document.createElement("article");
  card.className = "configuration-card";
  const heading = document.createElement("h3");
  heading.textContent = title;
  const list = document.createElement("dl");
  list.replaceChildren(...fields.map(([label, value]) => {
    const row = document.createElement("div");
    const term = document.createElement("dt");
    term.textContent = label;
    const detail = document.createElement("dd");
    detail.textContent = value;
    row.append(term, detail);
    return row;
  }));
  card.append(heading, list);
  return card;
}

function enabledLabel(value: boolean): string {
  return value ? "已启用" : "已关闭";
}

function seasonFailurePriority(metadata: RuntimeConfiguration["metadata"]): HTMLElement {
  const panel = document.createElement("section");
  panel.className = "failure-priority";
  panel.setAttribute("aria-label", "TMDB 季度失败优先级");

  const caption = document.createElement("p");
  caption.className = "failure-priority-caption";
  caption.textContent = "由高到低执行；任一策略成功立即停止。Skip 命中会终止后续 fallback。";

  const sequence = document.createElement("ol");
  sequence.className = "failure-priority-list";
  const steps: Array<{
    priority: string;
    title: string;
    description: string;
    enabled: boolean;
    independent?: boolean;
  }> = [
    {
      priority: "4",
      title: "TMDBFailSkip",
      description: "显式终止，不再执行低优先级策略",
      enabled: metadata.season_failure.skip,
    },
    {
      priority: "3",
      title: "TMDBFailBacktrace",
      description: "需要 bgmid；当前 tmdbid + Season 联合匹配失败后，逐层回溯 Bangumi 前传，"
        + "用每个前作的日文名、中文名和开播日期重新搜索并验证完整 tmdbid + Season",
      enabled: metadata.season_failure.backtrace,
    },
    {
      priority: "independent",
      title: "AI 元数据匹配",
      description: "一个任务、一个提示词，统一返回并验证 TMDB Series、Season 和全部文件的 Episode",
      enabled: metadata.ai.use_metadata_match,
      independent: true,
    },
    {
      priority: "2",
      title: "TMDBFailUseTitleSeason",
      description: "前面策略全部失败后，只用本地标题解析器读取任务 title；"
        + "解析成功即使用该本地季度，不验证 TMDB Season；解析不到继续 P1",
      enabled: metadata.season_failure.use_title_season,
    },
    {
      priority: "1",
      title: "TMDBFailUseFirstSeason",
      description: "前序策略全部失败后，勾选即使用本地 S01，不验证 TMDB Season",
      enabled: metadata.season_failure.use_first_season,
    },
  ];

  sequence.replaceChildren(...steps.map((step) => {
    const item = document.createElement("li");
    item.className = `failure-priority-step ${step.enabled ? "enabled" : "disabled"}`
      + (step.independent ? " independent" : "");
    item.dataset.priority = step.priority;

    const badge = document.createElement("span");
    badge.className = "failure-priority-badge";
    badge.textContent = step.independent ? "独立 AI" : `P${step.priority}`;

    const content = document.createElement("span");
    content.className = "failure-priority-content";
    const title = document.createElement("strong");
    title.textContent = step.title;
    const description = document.createElement("small");
    description.textContent = step.description;
    content.append(title, description);

    const state = document.createElement("span");
    state.className = "failure-priority-state";
    state.textContent = enabledLabel(step.enabled);
    item.append(badge, content, state);
    return item;
  }));

  panel.append(caption, sequence);
  return panel;
}

function metadataConfigurationCard(config: RuntimeConfiguration): HTMLElement {
  const tmdbCredential = config.metadata.tmdb.api_key_configured
    || config.metadata.tmdb.read_access_token_configured;
  const card = configurationCard("TMDB 与季度失败链", [
    ["TMDB", tmdbCredential ? "凭据已配置（值已隐藏）" : "未配置凭据"],
    ["API / 语言", `${config.metadata.tmdb.base_url} · ${config.metadata.tmdb.language}`],
    ["TMDB 代理", config.metadata.tmdb.proxy_url ?? "直连（未配置）"],
    ["超时", `${config.metadata.tmdb.http_timeout_seconds} 秒`],
    [
      "TMDB 重试",
      `${config.metadata.tmdb.retry_count} 次 · 间隔 `
      + `${config.metadata.tmdb.retry_delay_seconds} 秒`,
    ],
    ["Bangumi API", config.metadata.bangumi.base_url],
    ["Bangumi 代理", config.metadata.bangumi.proxy_url ?? "直连（未配置）"],
    ["Bangumi 超时", `${config.metadata.bangumi.http_timeout_seconds} 秒`],
    [
      "Bangumi 重试",
      `${config.metadata.bangumi.retry_count} 次 · 间隔 `
      + `${config.metadata.bangumi.retry_delay_seconds} 秒`,
    ],
    [
      "Bangumi 完全兜底（一般不启用这个）",
      `${enabledLabel(config.metadata.tmdb_failure_use_bangumi)} · `
      + "TMDB 完全失败时用 Bangumi 最终兜底；季度固定 S01；需要 bgmid；"
      + "不输出有效 tmdbid（内部仍按现有逻辑写 0）",
    ],
  ]);
  card.append(seasonFailurePriority(config.metadata));
  return card;
}

async function loadConfiguration(): Promise<void> {
  const status = element<HTMLElement>("#configuration-status");
  const container = element<HTMLElement>("#configuration");
  status.textContent = "正在读取脱敏后的生效配置…";
  try {
    const response = await fetch("/api/v1/config", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const config = await response.json() as RuntimeConfiguration;
    currentConfiguration = config;
    element<HTMLButtonElement>("#configuration-reset").disabled =
      config.configuration_revision === 0;
    const cards = [
      configurationCard("目录", [
        ["data_path", config.paths.data_path],
        ["download_path", config.paths.download_path],
        ["save_path", config.paths.save_path],
        ["修改生效", config.deployment.paths_restart_required ? "需要重启" : "即时生效"],
      ]),
      configurationCard("部署与安全", [
        ["容器模式", enabledLabel(config.deployment.running_in_container)],
        ["后台 workers", enabledLabel(config.deployment.background_workers_enabled)],
        ["Access-Key", config.deployment.access_key_configured ? "已配置（值已隐藏）" : "未配置"],
      ]),
      metadataConfigurationCard(config),
      configurationCard("AI、偏移与 Torrent", [
        [
          "AI 匹配",
          `任务级 ${enabledLabel(
            config.metadata.ai.use_metadata_match,
          )} · 单提示词 · `
          + `${config.metadata.ai.http_timeout_seconds} 秒`,
        ],
        ["可信 offset 缓存", enabledLabel(config.metadata.mikan_trusted_offset_cache_enabled)],
        [
          "Torrent HTTP",
          `${config.torrent_fetch.http_timeout_seconds} 秒 · `
          + `${config.torrent_fetch.max_redirects} 次跳转 · `
          + `${config.torrent_fetch.max_response_bytes} bytes`,
        ],
        ["Torrent 暂存 TTL", `${config.torrent_fetch.staging_ttl_seconds} 秒`],
      ]),
      configurationCard("AnimeGoNetData 更新", [
        ["定时更新", enabledLabel(config.data_update.enabled)],
        ["Cron", config.data_update.cron],
        ["Manifest", config.data_update.manifest_url ?? "未配置（仍可离线导入）"],
        [
          "策略",
          !config.data_update.auto_download
            ? "仅检查"
            : config.data_update.auto_import
              ? "自动下载并导入"
              : "自动下载后等待确认",
        ],
        ["保留版本", `${config.data_update.keep_versions} 版`],
        ["HTTP 超时", `${config.data_update.http_timeout_seconds} 秒`],
        ["修改生效", config.data_update.hot_reload_supported ? "即时热重排" : "需要重启"],
      ]),
    ];
    if (config.migration_diagnostics.length > 0) {
      cards.unshift(configurationCard(
        "旧配置迁移阻断",
        config.migration_diagnostics.map((item) => [
          item.code,
          `${item.legacy_downloader_type} · ${item.source} · ${item.message}`,
        ]),
      ));
    }
    container.replaceChildren(...cards);
    status.textContent = config.downloads_blocked
      ? "检测到不支持或无法安全读取的旧下载器配置；下载与后台 workers 已强制停用，请先按迁移提示修复并重启。"
      : config.restart_required
      ? `存在待重启配置 · 已保存 revision ${config.configuration_revision} · `
        + `当前应用 revision ${config.applied_configuration_revision}`
      : `当前进程的生效值 · revision ${config.configuration_revision}；凭据永不回传。`;
  } catch (error) {
    currentConfiguration = null;
    container.replaceChildren();
    status.textContent = `配置读取失败：${errorMessage(error, "未知错误")}`;
  }
}

function configurationSecretLabel(state: "inherit" | "configured" | "cleared"): string {
  switch (state) {
    case "configured": return "当前私密覆盖：已配置（值已隐藏）";
    case "cleared": return "当前私密覆盖：已明确清除";
    default: return "当前私密覆盖：继承部署配置";
  }
}

function setConfigurationValue(id: string, value: string | number): void {
  element<HTMLInputElement>(id).value = String(value);
}

function setConfigurationChecked(id: string, value: boolean): void {
  element<HTMLInputElement>(id).checked = value;
}

function syncConfigurationSecretInputs(): void {
  const clearKey = element<HTMLInputElement>("#configuration-tmdb-key-clear").checked;
  const clearToken = element<HTMLInputElement>("#configuration-tmdb-token-clear").checked;
  const key = element<HTMLInputElement>("#configuration-tmdb-key");
  const token = element<HTMLInputElement>("#configuration-tmdb-token");
  const keyLocked = activeConfigurationLockedFields.has("tmdb_api_key");
  const tokenLocked = activeConfigurationLockedFields.has("tmdb_read_access_token");
  key.disabled = keyLocked || clearKey;
  token.disabled = tokenLocked || clearToken;
  element<HTMLInputElement>("#configuration-tmdb-key-clear").disabled = keyLocked;
  element<HTMLInputElement>("#configuration-tmdb-token-clear").disabled = tokenLocked;
  if (clearKey) key.value = "";
  if (clearToken) token.value = "";
}

const configurationLockSelectors: Record<string, string[]> = {
  tmdb_base_url: ["#configuration-tmdb-url"],
  tmdb_proxy_url: ["#configuration-tmdb-proxy"],
  tmdb_language: ["#configuration-tmdb-language"],
  tmdb_http_timeout_seconds: ["#configuration-tmdb-timeout"],
  tmdb_retry_count: ["#configuration-tmdb-retry-count"],
  tmdb_retry_delay_seconds: ["#configuration-tmdb-retry-delay"],
  tmdb_api_key: ["#configuration-tmdb-key", "#configuration-tmdb-key-clear"],
  tmdb_read_access_token: ["#configuration-tmdb-token", "#configuration-tmdb-token-clear"],
  bangumi_base_url: ["#configuration-bangumi-url"],
  bangumi_proxy_url: ["#configuration-bangumi-proxy"],
  bangumi_http_timeout_seconds: ["#configuration-bangumi-timeout"],
  bangumi_retry_count: ["#configuration-bangumi-retry-count"],
  bangumi_retry_delay_seconds: ["#configuration-bangumi-retry-delay"],
  ai_use_metadata_match: ["#configuration-ai-metadata"],
  ai_http_timeout_seconds: ["#configuration-ai-timeout"],
  data_update_enabled: ["#configuration-data-update-enabled"],
  data_update_cron: ["#configuration-data-update-cron"],
  data_update_manifest_url: ["#configuration-data-update-manifest"],
  data_update_auto_download: ["#configuration-data-update-auto-download"],
  data_update_auto_import: ["#configuration-data-update-auto-import"],
  data_update_keep_versions: ["#configuration-data-update-keep"],
  data_update_http_timeout_seconds: ["#configuration-data-update-timeout"],
};

function applyConfigurationLocks(
  locks: RuntimeConfiguration["editable"]["locked_fields"],
): void {
  activeConfigurationLockedFields = new Set(locks.map((lock) => lock.field));
  const lockByField = new Map(locks.map((lock) => [lock.field, lock]));
  for (const [field, selectors] of Object.entries(configurationLockSelectors)) {
    const lock = lockByField.get(field);
    for (const selector of selectors) {
      const input = element<HTMLInputElement>(selector);
      input.disabled = lock !== undefined;
      const label = input.closest("label");
      label?.classList.toggle("configuration-field-locked", lock !== undefined);
      if (lock) {
        input.title = `由环境变量 ${lock.environment_variables.join(", ")} 控制`;
      } else {
        input.removeAttribute("title");
      }
    }
  }

  const summary = element<HTMLElement>("#configuration-lock-summary");
  if (locks.length === 0) {
    summary.textContent = "当前没有环境变量锁定的可编辑字段。";
    summary.className = "configuration-lock-summary muted";
    return;
  }

  summary.textContent = `以下字段由部署环境控制，Web 只读：${locks
    .map((lock) => `${lock.field} (${lock.environment_variables.join(", ")})`)
    .join("；")}`;
  summary.className = "configuration-lock-summary active";
}

function openConfigurationEditor(): void {
  if (!currentConfiguration) return;
  clearConfigurationPreview();
  const editable = currentConfiguration.editable;
  setConfigurationValue("#configuration-tmdb-url", editable.tmdb_base_url);
  setConfigurationValue("#configuration-tmdb-proxy", editable.tmdb_proxy_url ?? "");
  setConfigurationValue("#configuration-tmdb-language", editable.tmdb_language);
  setConfigurationValue("#configuration-tmdb-timeout", editable.tmdb_http_timeout_seconds);
  setConfigurationValue("#configuration-tmdb-retry-count", editable.tmdb_retry_count);
  setConfigurationValue(
    "#configuration-tmdb-retry-delay",
    editable.tmdb_retry_delay_seconds,
  );
  setConfigurationValue("#configuration-tmdb-key", "");
  setConfigurationChecked("#configuration-tmdb-key-clear", false);
  element<HTMLElement>("#configuration-tmdb-key-state").textContent =
    configurationSecretLabel(editable.tmdb_api_key_state);
  setConfigurationValue("#configuration-tmdb-token", "");
  setConfigurationChecked("#configuration-tmdb-token-clear", false);
  element<HTMLElement>("#configuration-tmdb-token-state").textContent =
    configurationSecretLabel(editable.tmdb_read_access_token_state);
  setConfigurationValue("#configuration-bangumi-url", editable.bangumi_base_url);
  setConfigurationValue("#configuration-bangumi-proxy", editable.bangumi_proxy_url ?? "");
  setConfigurationValue(
    "#configuration-bangumi-timeout",
    editable.bangumi_http_timeout_seconds,
  );
  setConfigurationValue(
    "#configuration-bangumi-retry-count",
    editable.bangumi_retry_count,
  );
  setConfigurationValue(
    "#configuration-bangumi-retry-delay",
    editable.bangumi_retry_delay_seconds,
  );
  setConfigurationChecked("#configuration-fail-skip", editable.season_failure_skip);
  setConfigurationChecked("#configuration-fail-backtrace", editable.season_failure_backtrace);
  setConfigurationChecked("#configuration-fail-title", editable.season_failure_use_title_season);
  setConfigurationChecked("#configuration-fail-first", editable.season_failure_use_first_season);
  setConfigurationChecked(
    "#configuration-ai-metadata",
    editable.ai_use_metadata_match,
  );
  setConfigurationChecked("#configuration-bangumi-fallback", editable.tmdb_failure_use_bangumi);
  setConfigurationChecked("#configuration-offset-cache", editable.mikan_trusted_offset_cache_enabled);
  setConfigurationValue("#configuration-ai-timeout", editable.ai_http_timeout_seconds);
  setConfigurationValue("#configuration-torrent-timeout", editable.torrent_http_timeout_seconds);
  setConfigurationValue("#configuration-torrent-bytes", editable.torrent_max_response_bytes);
  setConfigurationValue("#configuration-torrent-redirects", editable.torrent_max_redirects);
  setConfigurationValue("#configuration-torrent-ttl", editable.torrent_staging_ttl_seconds);
  setConfigurationChecked(
    "#configuration-data-update-enabled",
    editable.data_update_enabled,
  );
  setConfigurationValue("#configuration-data-update-cron", editable.data_update_cron);
  setConfigurationValue(
    "#configuration-data-update-manifest",
    editable.data_update_manifest_url ?? "",
  );
  setConfigurationChecked(
    "#configuration-data-update-auto-download",
    editable.data_update_auto_download,
  );
  setConfigurationChecked(
    "#configuration-data-update-auto-import",
    editable.data_update_auto_import,
  );
  setConfigurationValue(
    "#configuration-data-update-keep",
    editable.data_update_keep_versions,
  );
  setConfigurationValue(
    "#configuration-data-update-timeout",
    editable.data_update_http_timeout_seconds,
  );
  applyConfigurationLocks(editable.locked_fields);
  element<HTMLElement>("#configuration-message").textContent =
    `正在编辑 revision ${currentConfiguration.configuration_revision}`;
  syncConfigurationSecretInputs();
  configurationDialog.showModal();
}

const configurationFieldLabels: Record<string, string> = {
  tmdb_base_url: "TMDB API 地址",
  tmdb_proxy_url: "TMDB 代理",
  tmdb_language: "TMDB 语言",
  tmdb_http_timeout_seconds: "TMDB 超时（秒）",
  tmdb_retry_count: "TMDB 额外重试次数",
  tmdb_retry_delay_seconds: "TMDB 重试间隔（秒）",
  tmdb_api_key: "TMDB API Key",
  tmdb_read_access_token: "TMDB Read Token",
  bangumi_base_url: "Bangumi API 地址",
  bangumi_proxy_url: "Bangumi 代理",
  bangumi_http_timeout_seconds: "Bangumi 超时（秒）",
  bangumi_retry_count: "Bangumi 额外重试次数",
  bangumi_retry_delay_seconds: "Bangumi 重试间隔（秒）",
  season_failure_skip: "TMDBFailSkip",
  season_failure_backtrace: "TMDBFailBacktrace",
  season_failure_use_title_season: "TMDBFailUseTitleSeason",
  season_failure_use_first_season: "TMDBFailUseFirstSeason",
  ai_use_metadata_match: "AI 元数据匹配",
  ai_http_timeout_seconds: "AI 超时（秒）",
  tmdb_failure_use_bangumi: "Bangumi 完全兜底",
  mikan_trusted_offset_cache_enabled: "可信 offset 缓存",
  torrent_http_timeout_seconds: "Torrent HTTP 超时（秒）",
  torrent_max_response_bytes: "Torrent 最大响应（bytes）",
  torrent_max_redirects: "Torrent 最大跳转",
  torrent_staging_ttl_seconds: "Torrent 暂存 TTL（秒）",
  data_update_enabled: "AnimeGoNetData 定时更新",
  data_update_cron: "AnimeGoNetData Cron",
  data_update_manifest_url: "AnimeGoNetData Manifest URL",
  data_update_auto_download: "AnimeGoNetData 自动下载",
  data_update_auto_import: "AnimeGoNetData 自动导入",
  data_update_keep_versions: "AnimeGoNetData 保留版本数",
  data_update_http_timeout_seconds: "AnimeGoNetData HTTP 超时（秒）",
};

function configurationRequest(): ConfigurationUpdatePayload {
  if (!currentConfiguration) {
    throw new Error("配置尚未载入");
  }
  return {
    tmdb_base_url: element<HTMLInputElement>("#configuration-tmdb-url").value,
    tmdb_proxy_url:
      element<HTMLInputElement>("#configuration-tmdb-proxy").value || null,
    tmdb_language: element<HTMLInputElement>("#configuration-tmdb-language").value,
    tmdb_http_timeout_seconds:
      element<HTMLInputElement>("#configuration-tmdb-timeout").valueAsNumber,
    tmdb_retry_count:
      element<HTMLInputElement>("#configuration-tmdb-retry-count").valueAsNumber,
    tmdb_retry_delay_seconds:
      element<HTMLInputElement>("#configuration-tmdb-retry-delay").valueAsNumber,
    tmdb_api_key: element<HTMLInputElement>("#configuration-tmdb-key").value || null,
    clear_tmdb_api_key:
      element<HTMLInputElement>("#configuration-tmdb-key-clear").checked,
    tmdb_read_access_token:
      element<HTMLInputElement>("#configuration-tmdb-token").value || null,
    clear_tmdb_read_access_token:
      element<HTMLInputElement>("#configuration-tmdb-token-clear").checked,
    bangumi_base_url:
      element<HTMLInputElement>("#configuration-bangumi-url").value,
    bangumi_proxy_url:
      element<HTMLInputElement>("#configuration-bangumi-proxy").value || null,
    bangumi_http_timeout_seconds:
      element<HTMLInputElement>("#configuration-bangumi-timeout").valueAsNumber,
    bangumi_retry_count:
      element<HTMLInputElement>("#configuration-bangumi-retry-count").valueAsNumber,
    bangumi_retry_delay_seconds:
      element<HTMLInputElement>("#configuration-bangumi-retry-delay").valueAsNumber,
    season_failure_skip:
      element<HTMLInputElement>("#configuration-fail-skip").checked,
    season_failure_backtrace:
      element<HTMLInputElement>("#configuration-fail-backtrace").checked,
    season_failure_use_title_season:
      element<HTMLInputElement>("#configuration-fail-title").checked,
    season_failure_use_first_season:
      element<HTMLInputElement>("#configuration-fail-first").checked,
    ai_use_metadata_match:
      element<HTMLInputElement>("#configuration-ai-metadata").checked,
    ai_http_timeout_seconds:
      element<HTMLInputElement>("#configuration-ai-timeout").valueAsNumber,
    tmdb_failure_use_bangumi:
      element<HTMLInputElement>("#configuration-bangumi-fallback").checked,
    mikan_trusted_offset_cache_enabled:
      element<HTMLInputElement>("#configuration-offset-cache").checked,
    torrent_http_timeout_seconds:
      element<HTMLInputElement>("#configuration-torrent-timeout").valueAsNumber,
    torrent_max_response_bytes:
      element<HTMLInputElement>("#configuration-torrent-bytes").valueAsNumber,
    torrent_max_redirects:
      element<HTMLInputElement>("#configuration-torrent-redirects").valueAsNumber,
    torrent_staging_ttl_seconds:
      element<HTMLInputElement>("#configuration-torrent-ttl").valueAsNumber,
    data_update_enabled:
      element<HTMLInputElement>("#configuration-data-update-enabled").checked,
    data_update_cron:
      element<HTMLInputElement>("#configuration-data-update-cron").value,
    data_update_manifest_url:
      element<HTMLInputElement>("#configuration-data-update-manifest").value || null,
    data_update_auto_download:
      element<HTMLInputElement>("#configuration-data-update-auto-download").checked,
    data_update_auto_import:
      element<HTMLInputElement>("#configuration-data-update-auto-import").checked,
    data_update_keep_versions:
      element<HTMLInputElement>("#configuration-data-update-keep").valueAsNumber,
    data_update_http_timeout_seconds:
      element<HTMLInputElement>("#configuration-data-update-timeout").valueAsNumber,
    expected_configuration_revision: currentConfiguration.configuration_revision,
  };
}

function clearConfigurationPreview(message?: string): void {
  pendingConfigurationRequest = null;
  const preview = element<HTMLElement>("#configuration-preview");
  preview.hidden = true;
  element<HTMLElement>("#configuration-preview-summary").textContent = "";
  element<HTMLElement>("#configuration-diff-list").replaceChildren();
  element<HTMLButtonElement>("#configuration-confirm").disabled = true;
  if (message) {
    element<HTMLElement>("#configuration-message").textContent = message;
  }
}

function configurationPreviewValue(
  value: string | null,
  sensitive: boolean,
): string {
  if (sensitive) {
    switch (value) {
      case "inherit": return "继承部署配置";
      case "configured": return "已配置（值已隐藏）";
      case "cleared": return "已明确清除";
      default: return "值已隐藏";
    }
  }
  if (value === null || value.length === 0) return "未配置";
  if (value === "true") return "已启用";
  if (value === "false") return "已关闭";
  return value;
}

function renderConfigurationPreview(preview: ConfigurationPreview): void {
  const panel = element<HTMLElement>("#configuration-preview");
  const summary = element<HTMLElement>("#configuration-preview-summary");
  const list = element<HTMLElement>("#configuration-diff-list");
  panel.hidden = false;

  const restartChanges = preview.changes.filter((change) => change.effect === "restart").length;
  const hotChanges = preview.changes.filter((change) => change.effect === "hot_reload").length;
  summary.textContent = preview.changes.length === 0
    ? "没有检测到配置差异，无需保存。"
    : `共 ${preview.changes.length} 项：${hotChanges} 项保存后即时生效，`
      + `${restartChanges} 项需要重启；写入前会备份当前私有 revision。`;

  if (preview.changes.length === 0) {
    list.replaceChildren();
    return;
  }

  list.replaceChildren(...preview.changes.map((change) => {
    const item = document.createElement("article");
    item.className = "configuration-diff-item";

    const heading = document.createElement("div");
    heading.className = "configuration-diff-heading";
    const field = document.createElement("strong");
    field.textContent = configurationFieldLabels[change.field] ?? change.field;
    const effect = document.createElement("span");
    effect.className = `configuration-effect ${change.effect}`;
    effect.textContent = change.effect === "hot_reload" ? "即时生效" : "重启生效";
    heading.append(field, effect);

    const values = document.createElement("div");
    values.className = "configuration-diff-values";
    const before = document.createElement("span");
    before.textContent = configurationPreviewValue(change.before, change.sensitive);
    const arrow = document.createElement("span");
    arrow.className = "configuration-diff-arrow";
    arrow.textContent = "→";
    const after = document.createElement("span");
    after.textContent = configurationPreviewValue(change.after, change.sensitive);
    values.append(before, arrow, after);
    item.append(heading, values);
    return item;
  }));
}

async function previewConfiguration(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  if (!currentConfiguration) return;
  const previewButton = element<HTMLButtonElement>("#configuration-save");
  const message = element<HTMLElement>("#configuration-message");
  clearConfigurationPreview();
  previewButton.disabled = true;
  message.textContent = "正在验证并生成脱敏差异…";
  try {
    const request = configurationRequest();
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/config/preview", {
      method: "POST",
      headers: requestHeaders,
      body: JSON.stringify(request),
    });
    if (!response.ok) throw new Error(await responseError(response));
    const preview = await response.json() as ConfigurationPreview;
    renderConfigurationPreview(preview);
    pendingConfigurationRequest = preview.changes.length > 0 ? request : null;
    element<HTMLButtonElement>("#configuration-confirm").disabled =
      pendingConfigurationRequest === null;
    message.textContent = preview.changes.length === 0
      ? "服务端验证通过；当前表单与已保存配置一致。"
      : preview.restart_required
        ? "服务端验证通过；确认后保存，进程仍需重启以应用非热更新字段。"
        : "服务端验证通过；确认后保存，所列字段可即时生效。";
  } catch (error) {
    clearConfigurationPreview();
    message.textContent =
      `预览失败：${errorMessage(error, "未知错误")}；revision 冲突时请刷新后重试。`;
  } finally {
    previewButton.disabled = false;
  }
}

async function confirmConfiguration(): Promise<void> {
  const request = pendingConfigurationRequest;
  if (!request) return;
  const previewButton = element<HTMLButtonElement>("#configuration-save");
  const confirm = element<HTMLButtonElement>("#configuration-confirm");
  const message = element<HTMLElement>("#configuration-message");
  previewButton.disabled = true;
  confirm.disabled = true;
  message.textContent = "正在备份当前 revision 并写入私密配置覆盖…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/config", {
      method: "PUT",
      headers: requestHeaders,
      body: JSON.stringify(request),
    });
    if (!response.ok) throw new Error(await responseError(response));
    const saved = await response.json() as ConfigurationWriteResult;
    clearConfigurationPreview();
    configurationDialog.close();
    await loadConfiguration();
    const backup = saved.backup_revision === null
      ? "这是首个私有 revision，无旧版本需要备份"
      : `已备份 revision ${saved.backup_revision}`;
    element<HTMLElement>("#configuration-status").textContent = saved.restart_required
      ? `已保存 revision ${saved.configuration_revision}；${backup}；非热更新字段需重启。`
      : `已保存 revision ${saved.configuration_revision}；${backup}；修改已即时生效。`;
  } catch (error) {
    clearConfigurationPreview();
    message.textContent =
      `保存失败：${errorMessage(error, "未知错误")}；请重新预览后再保存。`;
  } finally {
    previewButton.disabled = false;
  }
}

async function resetConfiguration(): Promise<void> {
  if (!currentConfiguration || currentConfiguration.configuration_revision === 0) return;
  if (!window.confirm(
    "恢复部署默认配置？当前私有 revision 会先备份；数据更新策略会立即恢复，其他修改仍需重启。",
  )) return;
  const status = element<HTMLElement>("#configuration-status");
  status.textContent = "正在移除私密配置覆盖…";
  try {
    const response = await fetch(
      `/api/v1/config?expected_revision=${currentConfiguration.configuration_revision}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const saved = await response.json() as ConfigurationWriteResult;
    await loadConfiguration();
    const backup = saved.backup_revision === null
      ? "没有需要备份的私有 revision"
      : `已备份 revision ${saved.backup_revision}`;
    status.textContent = saved.restart_required
      ? `已恢复部署默认；${backup}；非热更新字段需重启。`
      : `已恢复部署默认；${backup}；修改已即时生效。`;
  } catch (error) {
    status.textContent = `恢复失败：${errorMessage(error, "未知错误")}`;
  }
}

function formatBytes(value: number): string {
  if (value < 1024) return `${value} B`;
  const units = ["KiB", "MiB", "GiB", "TiB"];
  let size = value;
  let unit = -1;
  do {
    size /= 1024;
    unit += 1;
  } while (size >= 1024 && unit + 1 < units.length);
  return `${size.toFixed(size >= 10 ? 1 : 2)} ${units[unit]}`;
}

function downloadControlButton(
  item: DownloadItem,
  action: "pause" | "resume" | "retry",
  label: string,
): HTMLButtonElement {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "secondary-button";
  button.textContent = label;
  button.addEventListener("click", () =>
    void controlDownload(item, action, button));
  return button;
}

async function controlDownload(
  item: DownloadItem,
  action: "pause" | "resume" | "retry",
  button: HTMLButtonElement,
): Promise<void> {
  button.disabled = true;
  const original = button.textContent ?? action;
  button.textContent = `${original}…`;
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(
      `/api/v1/downloads/${encodeURIComponent(item.job_id)}/${action}`,
      {
        method: "POST",
        headers: requestHeaders,
        body: JSON.stringify({ expected_revision: item.revision }),
      },
    );
    if (!response.ok) throw new Error(await responseError(response));
    await loadDownloads();
  } catch (error) {
    button.disabled = false;
    button.textContent = errorMessage(error, `${original}失败`);
  }
}

async function loadDownloadDetail(
  item: DownloadItem,
  target: HTMLDivElement,
  button: HTMLButtonElement,
): Promise<void> {
  expandedDownloadJobIds.add(item.job_id);
  button.disabled = true;
  button.textContent = "读取文件与时间线…";
  button.setAttribute("aria-expanded", "true");
  try {
    const response = await fetch(
      `/api/v1/downloads/${encodeURIComponent(item.job_id)}`,
      { headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const detail = await response.json() as DownloadDetail;
    const stages = document.createElement("dl");
    stages.className = "download-stage-grid";
    for (const [label, stage] of [
      ["下载前准备", detail.preparation],
      ["整理与清理", detail.organization],
    ] as const) {
      const group = document.createElement("div");
      const term = document.createElement("dt");
      term.textContent = label;
      const value = document.createElement("dd");
      value.textContent = `${stage.state} · 尝试 ${stage.attempt_count}`
        + (stage.failure_code ? ` · ${stage.failure_code}` : "")
        + (stage.next_attempt_at_utc
          ? ` · 下次 ${new Date(stage.next_attempt_at_utc).toLocaleString()}`
          : "");
      group.append(term, value);
      stages.append(group);
    }
    if (detail.task_failure_kind || detail.task_failure_reason) {
      const failure = document.createElement("p");
      failure.className = "download-detail-failure";
      failure.textContent =
        `${textOrDash(detail.task_failure_kind)} · ${textOrDash(detail.task_failure_reason)}`;
      stages.append(failure);
    }

    const snapshot = document.createElement("p");
    snapshot.className = detail.file_snapshot_state === "live"
      ? "download-file-snapshot ready"
      : "download-file-snapshot error";
    snapshot.textContent = detail.file_snapshot_state === "live"
      ? "文件进度：qBittorrent 实时快照"
      : `文件进度不可用：${textOrDash(detail.file_snapshot_failure_code)}；仍显示 SQLite 已保存的 wanted / priority。`;

    const files = document.createElement("div");
    files.className = "download-file-list";
    if (detail.files.length === 0) {
      const empty = document.createElement("p");
      empty.className = "muted metadata-attempt-empty";
      empty.textContent = "该任务尚无文件分配记录。";
      files.append(empty);
    } else {
      for (const file of detail.files) {
        const row = document.createElement("article");
        row.className = `download-file ${file.wanted === false ? "unwanted" : ""}`;
        const heading = document.createElement("div");
        heading.className = "download-file-heading";
        const name = document.createElement("strong");
        name.textContent = file.relative_path;
        const wanted = document.createElement("span");
        wanted.className = `badge ${file.wanted === false ? "error" : "ready"}`;
        wanted.textContent = file.wanted === null
          ? "尚未分配"
          : file.wanted ? `Wanted · P${file.priority}` : "跳过下载";
        heading.append(name, wanted);
        const progress = document.createElement("progress");
        progress.max = 1;
        progress.value = file.progress ?? 0;
        const evidence = document.createElement("p");
        evidence.textContent = `#${textOrDash(file.file_index)} · ${formatBytes(file.downloaded_bytes ?? 0)} / ${formatBytes(file.size_bytes)} · ${(100 * (file.progress ?? 0)).toFixed(1)}% · ${file.disposition}${file.other_reason ? ` · ${file.other_reason}` : ""}`;
        row.append(heading, progress, evidence);
        files.append(row);
      }
    }

    const timelineHeading = document.createElement("h4");
    timelineHeading.textContent = "状态时间线";
    const timeline = document.createElement("div");
    timeline.className = "download-timeline";
    if (detail.timeline.length === 0) {
      const empty = document.createElement("p");
      empty.className = "muted metadata-attempt-empty";
      empty.textContent = "尚无下载审计事件。";
      timeline.append(empty);
    } else {
      for (const event of detail.timeline) {
        const row = document.createElement("article");
        row.className = `download-event ${event.result === "failed" ? "failed" : ""}`;
        const heading = document.createElement("strong");
        heading.textContent = `${event.kind} · ${event.result}`;
        const state = document.createElement("p");
        state.textContent = `${textOrDash(event.from_state)} → ${textOrDash(event.to_state)} · ${new Date(event.created_at_utc).toLocaleString()}${event.failure_code ? ` · ${event.failure_code}` : ""}`;
        row.append(heading, state);
        timeline.append(row);
      }
    }

    const controls = document.createElement("div");
    controls.className = "download-detail-actions";
    if (detail.can_pause) {
      controls.append(downloadControlButton(detail.summary, "pause", "暂停"));
    }
    if (detail.can_resume) {
      controls.append(downloadControlButton(detail.summary, "resume", "恢复"));
    }
    if (detail.can_retry) {
      controls.append(downloadControlButton(detail.summary, "retry", "业务重试"));
    }
    target.replaceChildren(stages, snapshot, files, timelineHeading, timeline, controls);
    button.disabled = false;
    button.textContent = "收起文件与时间线";
    button.onclick = () => {
      expandedDownloadJobIds.delete(item.job_id);
      target.replaceChildren();
      button.textContent = "查看文件与时间线";
      button.setAttribute("aria-expanded", "false");
      button.onclick = () => void loadDownloadDetail(item, target, button);
    };
  } catch (error) {
    target.textContent = `下载详情读取失败：${errorMessage(error, "未知错误")}`;
    button.disabled = false;
    button.textContent = "重试文件与时间线";
  }
}

function renderDownloadSummary(body: DownloadListPage): void {
  const summary = body.summary;
  const metrics: Array<[string, string, string?]> = [
    ["活动", String(summary.active_jobs)],
    ["暂停", String(summary.paused_jobs)],
    ["失败", String(summary.failed_jobs), summary.latest_failure_code ?? undefined],
    ["等待整理", String(summary.waiting_organization_jobs)],
    ["已完成", String(summary.completed_jobs)],
    ["连接速度", formatBytes(summary.connected_download_speed_bytes_per_second) + "/s"],
    ["过期快照", String(summary.stale_jobs)],
    ["离线实例", String(summary.offline_instance_count)],
  ];
  const cards = metrics.map(([label, value, detail]) => {
    const card = document.createElement("article");
    card.className = label === "失败" && summary.failed_jobs > 0
      ? "download-summary-card error"
      : "download-summary-card";
    const term = document.createElement("span");
    term.textContent = label;
    const strong = document.createElement("strong");
    strong.textContent = value;
    card.append(term, strong);
    if (detail) {
      const note = document.createElement("small");
      note.textContent = detail;
      card.append(note);
    }
    return card;
  });
  const footer = document.createElement("p");
  footer.className = "download-summary-footer";
  footer.textContent = `共 ${summary.total_jobs} 个任务`
    + ` · 准备失败 ${summary.preparation_failed_jobs}`
    + ` · 整理失败 ${summary.organization_failed_jobs}`
    + ` · 最近同步成功 ${summary.last_downloader_success_at_utc
      ? new Date(summary.last_downloader_success_at_utc).toLocaleString()
      : "尚无"}`;
  element<HTMLElement>("#download-summary").replaceChildren(...cards, footer);
}

function renderDownloadPage(body: DownloadListPage): void {
  renderDownloadSummary(body);
  const container = element<HTMLElement>("#downloads");
  const totalPages = Math.max(1, Math.ceil(body.total_items / body.page_size));
  element<HTMLElement>("#download-list-status").textContent =
    `${body.total_items} 个任务 · 第 ${body.page} 页`;
  element<HTMLElement>("#download-page-status").textContent =
    `第 ${body.page} / ${totalPages} 页`;
  element<HTMLButtonElement>("#download-previous").disabled = body.page <= 1;
  element<HTMLButtonElement>("#download-next").disabled = body.page >= totalPages;
  if (body.items.length === 0) {
    const empty = document.createElement("p");
    empty.className = "muted empty";
    empty.textContent = body.total_items === 0
      ? "暂无符合筛选条件的下载任务"
      : "当前页没有任务，请返回上一页。";
    container.replaceChildren(empty);
    return;
  }

  container.replaceChildren(...body.items.map((item) => {
    const card = document.createElement("article");
    card.className = `download-card ${item.is_stale ? "stale" : ""}`;
    const heading = document.createElement("div");
    heading.className = "download-heading";
    const title = document.createElement("strong");
    title.textContent = item.title;
    const state = document.createElement("span");
    state.className = `badge ${item.is_stale ? "error" : "ready"}`;
    state.textContent = item.is_stale
      ? `快照过期 · ${item.downloader_failure_code ?? "离线"}`
      : `${item.state} · ${statusLabels[item.business_status] ?? item.business_status}`;
    heading.append(title, state);
    const progress = document.createElement("progress");
    progress.max = 1;
    progress.value = item.progress;
    const details = document.createElement("p");
    details.className = "download-details";
    details.textContent = `${item.source} → ${item.downloader_id} · ${(item.progress * 100).toFixed(1)}% · ${formatBytes(item.downloaded_bytes)} / ${formatBytes(item.total_bytes)} · ${formatBytes(item.speed_bytes_per_second)}/s · Seeds ${item.seeds} · Peers ${item.peers}`;
    const actions = document.createElement("div");
    actions.className = "download-actions";
    const expand = document.createElement("button");
    expand.type = "button";
    expand.className = "secondary-button";
    expand.textContent = "查看文件与时间线";
    expand.setAttribute("aria-expanded", "false");
    const detailTarget = document.createElement("div");
    detailTarget.className = "download-detail";
    expand.onclick = () => void loadDownloadDetail(item, detailTarget, expand);
    actions.append(expand);
    if (item.state === "paused") {
      actions.append(downloadControlButton(item, "resume", "恢复"));
    } else if (["waiting", "downloading", "moving", "seeding"].includes(item.state)) {
      actions.append(downloadControlButton(item, "pause", "暂停"));
    } else if (item.state === "error") {
      actions.append(downloadControlButton(item, "retry", "业务重试"));
    }
    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "delete-button";
    remove.textContent = "删除…";
    remove.addEventListener("click", () => void openDeletePreview(item.task_id));
    actions.append(remove);
    card.append(heading, progress, details, actions, detailTarget);
    if (expandedDownloadJobIds.has(item.job_id)) {
      void loadDownloadDetail(item, detailTarget, expand);
    }
    return card;
  }));
}

async function loadDownloads(): Promise<void> {
  const container = element<HTMLElement>("#downloads");
  const query = new URLSearchParams({
    page: String(downloadState.page),
    page_size: String(downloadState.page_size),
  });
  if (downloadState.search) query.set("search", downloadState.search);
  if (downloadState.state) query.set("state", downloadState.state);
  if (downloadState.business_status) {
    query.set("business_status", downloadState.business_status);
  }
  if (downloadState.downloader_id) {
    query.set("downloader_id", downloadState.downloader_id);
  }
  if (downloadState.source) query.set("source", downloadState.source);
  try {
    const response = await fetch(`/api/v1/downloads?${query}`, { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as DownloadListPage;
    if (body.items.length === 0 && body.total_items > 0 && downloadState.page > 1) {
      downloadState.page = Math.max(1, Math.ceil(body.total_items / body.page_size));
      saveDownloadState();
      await loadDownloads();
      return;
    }
    downloadState.page = body.page;
    downloadState.page_size = body.page_size as 10 | 25 | 50;
    saveDownloadState();
    renderDownloadPage(body);
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = `下载状态读取失败：${errorMessage(error, "未知错误")}`;
    container.replaceChildren(failed);
    element<HTMLElement>("#download-list-status").textContent = "下载任务读取失败";
  }
}

async function loadTrustedOffsets(): Promise<void> {
  const container = element<HTMLElement>("#trusted-offsets");
  try {
    const response = await fetch("/api/v1/mikan/trusted-offsets", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as { items: MikanTrustedOffsetItem[] };
    if (body.items.length === 0) {
      const empty = document.createElement("p");
      empty.className = "muted empty";
      empty.textContent = "暂无自动 offset 学习证据";
      container.replaceChildren(empty);
      return;
    }

    container.replaceChildren(...body.items.map((item) => {
      const card = document.createElement("article");
      card.className = "offset-card";
      const summary = document.createElement("div");
      const heading = document.createElement("div");
      heading.className = "download-heading";
      const title = document.createElement("strong");
      title.textContent = `Mikan ${item.mikanid} · Group ${item.groupid}`;
      const state = document.createElement("span");
      state.className = `badge ${item.state}`;
      state.textContent = item.state === "trusted"
        ? "Trusted"
        : item.state === "conflict_reset" ? "Conflict reset" : "Learning";
      heading.append(title, state);
      const details = document.createElement("p");
      details.className = "muted";
      const signedOffset = item.episode_offset >= 0
        ? `+${item.episode_offset}` : `${item.episode_offset}`;
      details.textContent = `TMDB ${item.tmdb_series_id} · S${String(item.tmdb_season_number).padStart(2, "0")} · offset ${signedOffset} · ${item.distinct_episode_count}/${item.required_episode_count}`;
      summary.append(heading, details);
      const clear = document.createElement("button");
      clear.type = "button";
      clear.className = "delete-button";
      clear.textContent = "清理自动缓存";
      clear.addEventListener("click", () => void clearTrustedOffset(item));
      card.append(summary, clear);
      return card;
    }));
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = `可信 offset 读取失败：${errorMessage(error, "未知错误")}`;
    container.replaceChildren(failed);
  }
}

async function clearTrustedOffset(item: MikanTrustedOffsetItem): Promise<void> {
  if (!window.confirm(
    `清理 Mikan ${item.mikanid} / Group ${item.groupid} 的自动证据与缓存？人工规则、完成记录和媒体文件不会删除。`,
  )) return;
  try {
    const response = await fetch(
      `/api/v1/mikan/trusted-offsets/${item.mikanid}/${item.groupid}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    await loadTrustedOffsets();
  } catch (error) {
    window.alert(`清理失败：${errorMessage(error, "未知错误")}`);
  }
}

function selectedDeleteInput(flag: DeleteFlag): HTMLInputElement | null {
  return document.querySelector<HTMLInputElement>(`#delete-options input[name="${flag}"]`);
}

function updateDeleteConfirm(): void {
  deleteConfirm.disabled = !activeDeletePreview || !deleteGroups.some(({ flag }) => selectedDeleteInput(flag)?.checked);
}

async function openDeletePreview(taskId: string): Promise<void> {
  const options = element<HTMLElement>("#delete-options");
  const targets = element<HTMLElement>("#delete-targets");
  const message = element<HTMLElement>("#delete-message");
  options.replaceChildren();
  targets.replaceChildren();
  message.textContent = "正在读取不可变目标…";
  deleteConfirm.disabled = true;
  deleteDialog.showModal();
  try {
    const response = await fetch(`/api/v1/delete/tasks/${encodeURIComponent(taskId)}/preview`, { headers });
    if (!response.ok) throw new Error(await responseError(response));
    activeDeletePreview = await response.json() as DeletePreview;
    element<HTMLElement>("#delete-summary").textContent = `${activeDeletePreview.title} · ${statusLabels[activeDeletePreview.task_status] ?? activeDeletePreview.task_status}`;
    for (const group of deleteGroups) {
      const groupTargets = activeDeletePreview[group.collection];
      const option = document.createElement("label");
      option.className = "delete-option";
      const input = document.createElement("input");
      input.type = "checkbox";
      input.name = group.flag;
      input.disabled = groupTargets.length === 0;
      input.addEventListener("change", updateDeleteConfirm);
      const text = document.createElement("span");
      const strong = document.createElement("strong");
      strong.textContent = `${group.label} · ${groupTargets.length} 项`;
      const small = document.createElement("small");
      small.textContent = group.help;
      text.append(strong, small);
      option.append(input, text);
      options.append(option);

      if (groupTargets.length > 0) {
        const section = document.createElement("section");
        const heading = document.createElement("h3");
        heading.textContent = group.label;
        const list = document.createElement("ul");
        for (const target of groupTargets) {
          const row = document.createElement("li");
          row.textContent = target.display_value;
          list.append(row);
        }
        section.append(heading, list);
        targets.append(section);
      }
    }
    message.textContent = "目标已冻结预览。勾选动作后才可确认；执行结果可按 execution ID 查询。";
    updateDeleteConfirm();
  } catch (error) {
    activeDeletePreview = null;
    message.textContent = `预览失败：${errorMessage(error, "未知错误")}`;
  }
}

deleteConfirm.addEventListener("click", async () => {
  if (!activeDeletePreview) return;
  deleteConfirm.disabled = true;
  deleteConfirm.textContent = "正在创建…";
  const request: Record<DeleteFlag, boolean> & { fingerprint: string } = {
    fingerprint: activeDeletePreview.fingerprint,
    delete_business_record: false,
    delete_downloader_task: false,
    delete_source_files: false,
    delete_media_files: false,
  };
  for (const { flag } of deleteGroups) request[flag] = Boolean(selectedDeleteInput(flag)?.checked);
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(`/api/v1/delete/tasks/${encodeURIComponent(activeDeletePreview.task_id)}`, {
      method: "POST",
      headers: requestHeaders,
      body: JSON.stringify(request),
    });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as DeleteCreateResponse;
    element<HTMLElement>("#delete-message").textContent = `删除任务已创建：${body.execution_id}（${body.selected_target_count} 项）`;
    deleteConfirm.textContent = "已创建";
    window.setTimeout(() => deleteDialog.close(), 1600);
  } catch (error) {
    element<HTMLElement>("#delete-message").textContent = errorMessage(error, "创建失败");
    deleteConfirm.textContent = "确认创建删除任务";
    updateDeleteConfirm();
  }
});

deleteDialog.addEventListener("close", () => {
  activeDeletePreview = null;
  deleteConfirm.textContent = "确认创建删除任务";
});

function textOrDash(value: unknown): string {
  return value === null || value === undefined || value === "" ? "—" : String(value);
}

async function retryMetadataTask(taskId: string, button: HTMLButtonElement): Promise<void> {
  button.disabled = true;
  button.textContent = "重新入队中…";
  try {
    const response = await fetch(`/api/v1/metadata/tasks/${encodeURIComponent(taskId)}/retry`, { method: "POST", headers });
    if (!response.ok) throw new Error(await responseError(response));
    await loadMetadataTasks();
  } catch (error) {
    button.disabled = false;
    button.textContent = errorMessage(error, "重试失败");
  }
}

const expandedMetadataTaskIds = new Set<string>();
const expandedMetadataDetailIds = new Set<string>();

async function loadMetadataDetail(
  taskId: string,
  target: HTMLDivElement,
  button: HTMLButtonElement,
): Promise<void> {
  expandedMetadataDetailIds.add(taskId);
  button.disabled = true;
  button.textContent = "读取来源 / TMDB 对照…";
  button.setAttribute("aria-expanded", "true");
  try {
    const response = await fetch(
      `/api/v1/metadata/tasks/${encodeURIComponent(taskId)}`,
      { headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const detail = await response.json() as MetadataTaskDetail;
    const ai = document.createElement("article");
    ai.className = `metadata-ai ${detail.ai.status === "matched" ? "verified" : ""}`;
    const aiHeading = document.createElement("strong");
    aiHeading.textContent = `AI：${detail.ai.status === "not_attempted" ? "未调用" : detail.ai.status}`;
    const confidence = document.createElement("span");
    confidence.className =
      `badge ${detail.ai.confidence_basis === "tmdb_verified" ? "ready" : ""}`;
    confidence.textContent = detail.ai.confidence_basis === "tmdb_verified"
      ? "可信依据：TMDB 已验证"
      : "可信依据：未建立";
    const aiMeta = document.createElement("p");
    aiMeta.textContent = detail.ai.attempted_at_utc
      ? `${textOrDash(detail.ai.stage)} 阶段 · ${detail.ai.duration_ms} ms · ${new Date(detail.ai.attempted_at_utc).toLocaleString()}`
      : "模型自报置信度不被采信；只有 TMDB Series / Season / Episode 验证通过才建立可信结果。";
    ai.append(aiHeading, confidence, aiMeta);
    if (detail.ai.error_code || detail.ai.reason) {
      const reason = document.createElement("p");
      reason.className = "metadata-detail-reason";
      reason.textContent =
        `${textOrDash(detail.ai.error_code)} · ${textOrDash(detail.ai.reason)}`;
      ai.append(reason);
    }

    const files = document.createElement("div");
    files.className = "metadata-file-comparisons";
    if (detail.files.length === 0) {
      const empty = document.createElement("p");
      empty.className = "muted metadata-attempt-empty";
      empty.textContent = "该任务尚无文件条目。";
      files.append(empty);
    } else {
      for (const file of detail.files) {
        const row = document.createElement("article");
        row.className = `metadata-file-comparison ${file.disposition}`;
        const source = document.createElement("div");
        source.className = "metadata-file-source";
        const sourceName = document.createElement("strong");
        sourceName.textContent = file.source_name;
        const sourceEvidence = document.createElement("p");
        sourceEvidence.textContent =
          `来源 EP ${textOrDash(file.source_episode)} · 文件名候选 ${textOrDash(file.file_episode_candidate)} · ${formatBytes(file.size_bytes)}`;
        source.append(sourceName, sourceEvidence);
        const arrow = document.createElement("span");
        arrow.className = "metadata-file-arrow";
        arrow.textContent = "→";
        arrow.setAttribute("aria-hidden", "true");
        const canonical = document.createElement("div");
        canonical.className = "metadata-file-canonical";
        const canonicalName = document.createElement("strong");
        canonicalName.textContent = file.tmdb_series_name
          ? `${file.tmdb_series_name} / ${textOrDash(file.tmdb_season_name)}`
          : "尚无经验证的 TMDB 映射";
        const canonicalEpisode = document.createElement("p");
        canonicalEpisode.textContent = file.tmdb_episode_number === null
          ? `${file.disposition} · ${textOrDash(file.other_reason)}`
          : `TMDB ${file.tmdb_series_id} · S${String(file.tmdb_season_number).padStart(2, "0")}E${String(file.tmdb_episode_number).padStart(3, "0")} · ${textOrDash(file.tmdb_episode_name)}`;
        canonical.append(canonicalName, canonicalEpisode);
        row.append(source, arrow, canonical);
        files.append(row);
      }
    }

    target.replaceChildren(ai, files);
    button.disabled = false;
    button.textContent = "收起来源 / TMDB 对照";
    button.onclick = () => {
      expandedMetadataDetailIds.delete(taskId);
      target.replaceChildren();
      button.textContent = "查看来源 / TMDB 对照";
      button.setAttribute("aria-expanded", "false");
      button.onclick = () => void loadMetadataDetail(taskId, target, button);
    };
  } catch (error) {
    target.textContent =
      `任务详情读取失败：${errorMessage(error, "未知错误")}`;
    button.disabled = false;
    button.textContent = "重试来源 / TMDB 对照";
  }
}

async function loadMetadataAttempts(
  taskId: string,
  target: HTMLDivElement,
  button: HTMLButtonElement,
): Promise<void> {
  expandedMetadataTaskIds.add(taskId);
  button.disabled = true;
  button.textContent = "读取策略时间线…";
  try {
    const response = await fetch(
      `/api/v1/metadata/tasks/${encodeURIComponent(taskId)}/attempts`,
      { headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as { items: MetadataAttemptItem[] };
    if (body.items.length === 0) {
      const empty = document.createElement("p");
      empty.className = "muted metadata-attempt-empty";
      empty.textContent = "尚无策略尝试记录。任务进入元数据阶段后会在这里显示。";
      target.replaceChildren(empty);
    } else {
      target.replaceChildren(...body.items.map((attempt) => {
        const row = document.createElement("article");
        row.className = `metadata-attempt ${attempt.result === "failed" ? "failed" : ""}`;
        const heading = document.createElement("div");
        heading.className = "metadata-attempt-heading";
        const strategy = document.createElement("strong");
        strategy.textContent = `${attempt.stage} · ${attempt.strategy}`;
        const result = document.createElement("span");
        result.className = `badge ${attempt.result === "failed" ? "error" : "ready"}`;
        result.textContent = attempt.result;
        heading.append(strategy, result);
        const execution = document.createElement("p");
        execution.textContent = `P${textOrDash(attempt.priority)} · Run #${attempt.run_attempt_number} (${attempt.run_status}) · 尝试 #${attempt.attempt_number} · ${attempt.duration_ms} ms · ${new Date(attempt.created_at_utc).toLocaleString()}`;
        row.append(heading, execution);
        if (attempt.error_code || attempt.reason) {
          const reason = document.createElement("p");
          reason.className = "metadata-attempt-reason";
          reason.textContent = `${textOrDash(attempt.error_code)} · ${textOrDash(attempt.reason)} · ${attempt.retryable ? "可自动重试" : "不可自动重试"}`;
          row.append(reason);
        }
        return row;
      }));
    }
    button.disabled = false;
    button.textContent = "收起策略时间线";
    button.onclick = () => {
      expandedMetadataTaskIds.delete(taskId);
      target.replaceChildren();
      button.textContent = "查看策略时间线";
      button.onclick = () => void loadMetadataAttempts(taskId, target, button);
    };
  } catch (error) {
    target.textContent = `策略时间线读取失败：${errorMessage(error, "未知错误")}`;
    button.disabled = false;
    button.textContent = "重试策略时间线";
  }
}

async function loadMetadataTasks(): Promise<void> {
  const container = element<HTMLElement>("#metadata-tasks");
  const query = new URLSearchParams({
    page: String(metadataState.page),
    page_size: String(metadataState.page_size),
    handling: metadataState.handling,
    retryability: metadataState.retryability,
    sort: metadataState.sort,
    direction: metadataState.direction,
  });
  if (metadataState.search) query.set("search", metadataState.search);
  if (metadataState.status) query.set("status", metadataState.status);
  if (metadataState.failure_stage) {
    query.set("failure_stage", metadataState.failure_stage);
  }
  if (metadataState.error_code) query.set("error_code", metadataState.error_code);
  try {
    const response = await fetch(`/api/v1/metadata/tasks?${query}`, { headers });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const body = await response.json() as MetadataTaskListPage;
    if (body.items.length === 0 && body.total_items > 0 && metadataState.page > 1) {
      metadataState.page = Math.max(1, Math.ceil(body.total_items / body.page_size));
      saveMetadataState();
      await loadMetadataTasks();
      return;
    }
    metadataState.page = body.page;
    metadataState.page_size = body.page_size as 10 | 25 | 50;
    const totalPages = Math.max(1, Math.ceil(body.total_items / body.page_size));
    element<HTMLElement>("#metadata-list-status").textContent =
      `${body.total_items} 个任务 · 第 ${body.page} 页 · ${body.sort} ${body.direction}`;
    element<HTMLElement>("#metadata-page-status").textContent =
      `第 ${body.page} / ${totalPages} 页`;
    element<HTMLButtonElement>("#metadata-previous").disabled = body.page <= 1;
    element<HTMLButtonElement>("#metadata-next").disabled = body.page >= totalPages;
    if (body.items.length === 0) {
      const empty = document.createElement("p");
      empty.className = "muted empty";
      empty.textContent = "暂无符合筛选条件的元数据任务";
      container.replaceChildren(empty);
      return;
    }

    const cards = body.items.map((item) => {
      const card = document.createElement("article");
      card.className = `metadata-card ${item.status === "metadata_failed" ? "failed" : ""}`;
      const heading = document.createElement("div");
      heading.className = "metadata-heading";
      const title = document.createElement("strong");
      title.textContent = item.title;
      const state = document.createElement("span");
      state.className = `badge ${item.status === "metadata_failed" ? "error" : "ready"}`;
      state.textContent = statusLabels[item.status] ?? item.status;
      heading.append(title, state);
      const handling = document.createElement("p");
      handling.className = `metadata-handling ${item.handling_category}`;
      const handlingLabels: Record<string, string> = {
        explicit_retry: "可安全重试（需显式）",
        configuration: "需修复配置",
        manual: "需人工处理",
        skipped: "已跳过",
        fallback: "已兜底 · 待补全 TMDB",
        active: "处理中",
        resolved: "已解析",
        other: "其他",
      };
      handling.textContent = handlingLabels[item.handling_category]
        ?? item.handling_category;
      const identity = document.createElement("p");
      identity.className = "metadata-identity";
      identity.textContent = `${item.source} · mikanid ${textOrDash(item.mikanid)} · bgmid ${textOrDash(item.bgmid)} · TMDB ${textOrDash(item.tmdb_series_id)} / S${item.tmdb_season_number === null ? "—" : String(item.tmdb_season_number).padStart(2, "0")}`;
      const stages = document.createElement("dl");
      stages.className = "metadata-stages";
      for (const [label, value] of [
        ["Series", item.series_strategy],
        ["Season", item.season_strategy],
        ["Episode", item.episode_strategy],
      ] as const) {
        const group = document.createElement("div");
        const term = document.createElement("dt");
        term.textContent = String(label);
        const description = document.createElement("dd");
        description.textContent = textOrDash(value);
        group.append(term, description);
        stages.append(group);
      }
      const files = document.createElement("p");
      files.className = "metadata-files";
      files.textContent = `已确认 ${item.episode_file_count} · 已跳过重复 ${item.duplicate_file_count} · Other ${item.other_file_count} · 待处理 ${item.pending_file_count}`;
      card.append(heading, handling, identity, stages, files);
      if (item.failure_kind || item.failure_reason) {
        const failure = document.createElement("p");
        failure.className = "metadata-failure";
        failure.textContent =
          `${textOrDash(item.failure_stage)} · ${textOrDash(item.failure_code ?? item.failure_kind)}`
          + ` · ${item.failure_retryable === null
            ? "可重试性未建立"
            : item.failure_retryable ? "可重试" : "不可重试"}`
          + ` · ${textOrDash(item.failure_reason)}`;
        card.append(failure);
      }
      const actions = document.createElement("div");
      actions.className = "metadata-actions";
      const detailButton = document.createElement("button");
      detailButton.type = "button";
      detailButton.className = "metadata-attempt-button";
      detailButton.textContent = "查看来源 / TMDB 对照";
      detailButton.setAttribute("aria-expanded", "false");
      const detailTarget = document.createElement("div");
      detailTarget.className = "metadata-detail";
      detailButton.onclick = () =>
        void loadMetadataDetail(item.task_id, detailTarget, detailButton);
      const attempts = document.createElement("button");
      attempts.type = "button";
      attempts.className = "metadata-attempt-button";
      attempts.textContent = "查看策略时间线";
      const attemptList = document.createElement("div");
      attemptList.className = "metadata-attempt-list";
      attempts.onclick = () => void loadMetadataAttempts(item.task_id, attemptList, attempts);
      actions.append(detailButton, attempts);
      if (item.status === "metadata_failed") {
        const retry = document.createElement("button");
        retry.type = "button";
        retry.className = "retry-button";
        retry.textContent = "显式重新匹配";
        retry.addEventListener("click", () => void retryMetadataTask(item.task_id, retry));
        actions.append(retry);
      }
      card.append(actions, detailTarget, attemptList);
      if (expandedMetadataDetailIds.has(item.task_id)) {
        void loadMetadataDetail(item.task_id, detailTarget, detailButton);
      }
      if (expandedMetadataTaskIds.has(item.task_id)) {
        void loadMetadataAttempts(item.task_id, attemptList, attempts);
      }
      return card;
    });
    container.replaceChildren(...cards);
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = `元数据状态读取失败：${errorMessage(error, "未知错误")}`;
    container.replaceChildren(failed);
  }
}

function pendingStat(label: string, value: number): HTMLDivElement {
  const group = document.createElement("div");
  const term = document.createElement("dt");
  term.textContent = label;
  const description = document.createElement("dd");
  description.textContent = String(value);
  group.append(term, description);
  return group;
}

function positiveInteger(value: string): number | null {
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
}

function createPendingRecoveryForm(
  bgmid: number,
  detail: PendingTmdbDetail,
): HTMLElement[] {
  const heading = document.createElement("h4");
  heading.textContent = "人工 TMDB 恢复";
  if (detail.recovery_candidates.length === 0) {
    const empty = document.createElement("p");
    empty.className = "muted";
    empty.textContent = "尚无已整理完成、可恢复的 fallback 记录。";
    return [heading, empty];
  }

  const explanation = document.createElement("p");
  explanation.className = "pending-recovery-warning";
  explanation.textContent = "提交后会逐项向 TMDB 验证 Series / Season / Episode。收敛到已有集时标记 DuplicateAfterResolution，不重新下载或删除文件。";
  const form = document.createElement("form");
  form.className = "pending-recovery-form";
  const seriesLabel = document.createElement("label");
  seriesLabel.textContent = "TMDB Series ID";
  const seriesInput = document.createElement("input");
  seriesInput.type = "number";
  seriesInput.min = "1";
  seriesInput.required = true;
  seriesInput.inputMode = "numeric";
  seriesInput.autocomplete = "off";
  seriesLabel.append(seriesInput);
  form.append(seriesLabel);

  const defaultSeason = detail.summary.season_numbers.length === 1
    ? detail.summary.season_numbers[0]
    : null;
  const fields = detail.recovery_candidates.map((candidate) => {
    const row = document.createElement("fieldset");
    row.className = "pending-recovery-row";
    const legend = document.createElement("legend");
    legend.textContent = `${candidate.source} · 来源 EP ${textOrDash(candidate.source_episode)} · ${candidate.dedup_boundary}`;
    const seasonLabel = document.createElement("label");
    seasonLabel.textContent = "Season";
    const seasonInput = document.createElement("input");
    seasonInput.type = "number";
    seasonInput.min = "1";
    seasonInput.required = true;
    seasonInput.inputMode = "numeric";
    seasonInput.value = defaultSeason === null ? "" : String(defaultSeason);
    const episodeLabel = document.createElement("label");
    episodeLabel.textContent = "Episode";
    const episodeInput = document.createElement("input");
    episodeInput.type = "number";
    episodeInput.min = "1";
    episodeInput.required = true;
    episodeInput.inputMode = "numeric";
    const sourceEpisode = candidate.source_episode === null
      ? null
      : positiveInteger(candidate.source_episode);
    episodeInput.value = sourceEpisode === null ? "" : String(sourceEpisode);
    seasonLabel.append(seasonInput);
    episodeLabel.append(episodeInput);
    row.append(legend, seasonLabel, episodeLabel);
    form.append(row);
    return { candidate, seasonInput, episodeInput };
  });

  const status = document.createElement("p");
  status.className = "pending-recovery-status";
  status.setAttribute("aria-live", "polite");
  const submit = document.createElement("button");
  submit.type = "submit";
  submit.className = "primary-button";
  submit.textContent = "验证并恢复";
  form.append(status, submit);
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    const seriesId = positiveInteger(seriesInput.value);
    const mappings = fields.map(({ candidate, seasonInput, episodeInput }) => ({
      fallback_record_id: candidate.fallback_record_id,
      tmdb_season_number: positiveInteger(seasonInput.value),
      tmdb_episode_number: positiveInteger(episodeInput.value),
    }));
    if (seriesId === null
      || mappings.some((mapping) =>
        mapping.tmdb_season_number === null || mapping.tmdb_episode_number === null)) {
      status.textContent = "Series、Season、Episode 都必须是正整数。";
      return;
    }
    if (!window.confirm(
      `确认用 TMDB Series ${seriesId} 恢复 bgmid ${bgmid} 的 ${mappings.length} 条记录？`,
    )) return;

    submit.disabled = true;
    submit.textContent = "正在向 TMDB 验证…";
    status.textContent = "";
    try {
      const requestHeaders = new Headers(headers);
      requestHeaders.set("Content-Type", "application/json");
      const response = await fetch(
        `/api/v1/metadata/pending-tmdb/${encodeURIComponent(String(bgmid))}/recover`,
        {
          method: "POST",
          headers: requestHeaders,
          body: JSON.stringify({ tmdb_series_id: seriesId, mappings }),
        },
      );
      if (!response.ok) throw new Error(await responseError(response));
      const result = await response.json() as PendingTmdbRecoveryResult;
      const duplicates = result.items.filter(
        (item) => item.state === "DuplicateAfterResolution",
      ).length;
      status.textContent = `已验证并恢复 ${result.items.length} 条；解析后重复 ${duplicates} 条。`;
      await Promise.all([loadPendingTmdb(true), loadMetadataTasks()]);
    } catch (error) {
      status.textContent = `恢复失败：${errorMessage(error, "未知错误")}`;
      submit.disabled = false;
      submit.textContent = "验证并恢复";
    }
  });
  return [heading, explanation, form];
}

async function loadPendingTmdbDetail(
  bgmid: number,
  target: HTMLDivElement,
  button: HTMLButtonElement,
): Promise<void> {
  button.disabled = true;
  button.textContent = "读取中…";
  try {
    const response = await fetch(
      `/api/v1/metadata/pending-tmdb/${encodeURIComponent(String(bgmid))}`,
      { headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const detail = await response.json() as PendingTmdbDetail;
    const sections: HTMLElement[] = [];
    const scopeHeading = document.createElement("h4");
    scopeHeading.textContent = "兜底去重作用域";
    sections.push(scopeHeading);
    if (detail.scopes.length === 0) {
      const empty = document.createElement("p");
      empty.className = "muted";
      empty.textContent = "尚无 claim 或完成记录。";
      sections.push(empty);
    }
    for (const scope of detail.scopes) {
      const row = document.createElement("div");
      row.className = "pending-scope";
      const identity = document.createElement("strong");
      identity.textContent = `${scope.dedup_boundary} · ${scope.state}`;
      const evidence = document.createElement("span");
      evidence.textContent = `${scope.source} · 来源 EP ${textOrDash(scope.source_episode)}`;
      row.append(identity, evidence);
      if (scope.cross_source_duplicate_risk) {
        const warning = document.createElement("em");
        warning.textContent = "可能跨来源重复";
        row.append(warning);
      }
      sections.push(row);
    }
    sections.push(...createPendingRecoveryForm(bgmid, detail));
    const taskHeading = document.createElement("h4");
    taskHeading.textContent = "关联任务";
    sections.push(taskHeading);
    for (const task of detail.tasks) {
      const row = document.createElement("div");
      row.className = "pending-task";
      const title = document.createElement("strong");
      title.textContent = task.title;
      const state = document.createElement("span");
      state.textContent = `${task.source} · ${statusLabels[task.status] ?? task.status} · S${task.season_number === null ? "—" : String(task.season_number).padStart(2, "0")} · Other ${task.other_file_count} · 重复 ${task.duplicate_file_count}`;
      row.append(title, state);
      sections.push(row);
    }
    target.replaceChildren(...sections);
    button.textContent = "收起详情";
    button.disabled = false;
    button.onclick = () => {
      target.replaceChildren();
      button.textContent = "查看详情与人工恢复";
      button.onclick = () => void loadPendingTmdbDetail(bgmid, target, button);
    };
  } catch (error) {
    target.textContent = `详情读取失败：${errorMessage(error, "未知错误")}`;
    button.disabled = false;
    button.textContent = "重试详情";
  }
}

async function loadPendingTmdb(force = false): Promise<void> {
  if (!force && document.querySelector(".pending-recovery-form")) return;
  const container = element<HTMLElement>("#pending-tmdb-list");
  try {
    const response = await fetch("/api/v1/metadata/pending-tmdb", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as { items: PendingTmdbSummary[] };
    if (body.items.length === 0) {
      const empty = document.createElement("p");
      empty.className = "muted empty";
      empty.textContent = "暂无待补全 TMDB 的作品";
      container.replaceChildren(empty);
      return;
    }
    container.replaceChildren(...body.items.map((item) => {
      const card = document.createElement("article");
      card.className = "pending-tmdb-card";
      const heading = document.createElement("div");
      heading.className = "metadata-heading";
      const title = document.createElement("strong");
      title.textContent = item.fallback_name;
      const badge = document.createElement("span");
      badge.className = "badge pending";
      badge.textContent = "TMDB 待补全";
      heading.append(title, badge);
      const identity = document.createElement("p");
      identity.className = "metadata-identity";
      const seasons = item.season_numbers.length === 0
        ? "—"
        : item.season_numbers.map((value) => `S${String(value).padStart(2, "0")}`).join("、");
      identity.textContent = `bgmid ${item.bgmid} · 已确认季度 ${seasons}`;
      const stats = document.createElement("dl");
      stats.className = "pending-tmdb-stats";
      stats.append(
        pendingStat("关联任务", item.task_count),
        pendingStat("已处理文件", item.processed_file_count),
        pendingStat("兜底记录", item.fallback_record_count),
        pendingStat("活动 claim", item.active_claim_count),
        pendingStat("已完成 claim", item.completed_claim_count),
        pendingStat("重复文件", item.duplicate_file_count),
      );
      card.append(heading, identity, stats);
      if (item.latest_failure_kind || item.latest_failure_reason) {
        const failure = document.createElement("p");
        failure.className = "metadata-failure";
        failure.textContent = `${textOrDash(item.latest_failure_kind)} · ${textOrDash(item.latest_failure_reason)}`;
        card.append(failure);
      }
      const warning = document.createElement("p");
      warning.className = "pending-progress-warning";
      warning.textContent = "兜底状态，不显示 TMDB Episode 进度。";
      const button = document.createElement("button");
      button.type = "button";
      button.className = "secondary-button";
      button.textContent = "查看详情与人工恢复";
      const detail = document.createElement("div");
      detail.className = "pending-tmdb-detail";
      button.onclick = () => void loadPendingTmdbDetail(item.bgmid, detail, button);
      card.append(warning, button, detail);
      return card;
    }));
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = `待补全状态读取失败：${errorMessage(error, "未知错误")}`;
    container.replaceChildren(failed);
  }
}

async function testDownloader(id: string, button: HTMLButtonElement): Promise<void> {
  const status = element<HTMLElement>("#downloader-status");
  button.disabled = true;
  button.textContent = "测试中…";
  try {
    const response = await fetch(`/api/v1/downloaders/${encodeURIComponent(id)}/test`, {
      method: "POST",
      headers,
    });
    if (!response.ok) throw new Error(await responseError(response));
    const result = await response.json() as DownloaderConnectionTest;
    status.textContent = result.connected
      ? `${id} 连接成功 · ${textOrDash(result.client_version)} · ${result.task_count ?? 0} 个任务 · ${result.latency_ms} ms · qB 默认路径 ${textOrDash(result.client_default_save_path)}`
      : `${id} 连接失败 · ${result.failure_code ?? "unknown"} · ${result.message}`;
    await loadDownloaders();
  } catch (error) {
    status.textContent = `${id} 测试失败：${errorMessage(error, "未知错误")}`;
  } finally {
    button.disabled = false;
    button.textContent = "测试连接";
  }
}

async function probeDownloaderPath(id: string, button: HTMLButtonElement): Promise<void> {
  const status = element<HTMLElement>("#downloader-status");
  button.disabled = true;
  button.textContent = "探测中…";
  try {
    const response = await fetch(`/api/v1/downloaders/${encodeURIComponent(id)}/path-probe`, {
      method: "POST",
      headers,
    });
    if (!response.ok) throw new Error(await responseError(response));
    const result = await response.json() as DownloaderPathProbe;
    status.textContent = result.success
      ? `${id} 路径可见且支持硬链接 · ${result.download_path} → ${result.save_path}`
      : `${id} 路径探测失败 · ${result.failure_code ?? "unknown"} · ${result.message}`;
  } catch (error) {
    status.textContent = `${id} 路径探测失败：${errorMessage(error, "未知错误")}`;
  } finally {
    button.disabled = false;
    button.textContent = "探测路径";
  }
}

function openDownloaderConfig(instance: DownloaderInstance | null): void {
  activeDownloaderId = instance?.id ?? null;
  const id = element<HTMLInputElement>("#downloader-config-id");
  id.disabled = instance !== null;
  id.value = instance?.id ?? "";
  element<HTMLInputElement>("#downloader-config-url").value = instance?.base_url ?? "http://127.0.0.1:8080/";
  element<HTMLInputElement>("#downloader-config-username").value = "";
  element<HTMLInputElement>("#downloader-config-password").value = "";
  element<HTMLInputElement>("#downloader-config-path").value = instance?.download_path ?? "";
  element<HTMLInputElement>("#downloader-config-enabled").checked = instance?.enabled ?? true;
  element<HTMLInputElement>("#downloader-config-clear-password").checked = false;
  element<HTMLButtonElement>("#downloader-config-delete").disabled =
    instance?.configuration_source !== "private_override";
  element<HTMLElement>("#downloader-config-message").textContent =
    instance?.credentials_configured
      ? "已有凭据已配置；密码字段留空会保留，且不会从服务端读回。"
      : "当前没有已配置凭据。";
  downloaderConfigDialog.showModal();
}

async function saveDownloaderConfig(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  const id = activeDownloaderId ?? element<HTMLInputElement>("#downloader-config-id").value;
  const save = element<HTMLButtonElement>("#downloader-config-save");
  const message = element<HTMLElement>("#downloader-config-message");
  save.disabled = true;
  message.textContent = "正在原子写入私有配置…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(`/api/v1/downloaders/${encodeURIComponent(id)}`, {
      method: "PUT",
      headers: requestHeaders,
      body: JSON.stringify({
        base_url: element<HTMLInputElement>("#downloader-config-url").value,
        username: element<HTMLInputElement>("#downloader-config-username").value || null,
        password: element<HTMLInputElement>("#downloader-config-password").value || null,
        clear_password: element<HTMLInputElement>("#downloader-config-clear-password").checked,
        download_path: element<HTMLInputElement>("#downloader-config-path").value,
        enabled: element<HTMLInputElement>("#downloader-config-enabled").checked,
        expected_configuration_revision: downloaderConfigurationRevision,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    message.textContent = "已保存；请重启主程序以应用新客户端配置。";
    await loadDownloaders();
    window.setTimeout(() => downloaderConfigDialog.close(), 1000);
  } catch (error) {
    message.textContent = `保存失败：${errorMessage(error, "未知错误")}`;
  } finally {
    save.disabled = false;
  }
}

async function deleteDownloaderOverride(): Promise<void> {
  const instance = downloaderInstances.find((item) => item.id === activeDownloaderId);
  if (!instance || instance.configuration_source !== "private_override") return;
  if (!window.confirm(`移除 ${instance.id} 的私有覆盖？服务端会拒绝仍有引用的实例。`)) return;
  const message = element<HTMLElement>("#downloader-config-message");
  try {
    const response = await fetch(
      `/api/v1/downloaders/${encodeURIComponent(instance.id)}?expected_configuration_revision=${downloaderConfigurationRevision}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    await loadDownloaders();
    downloaderConfigDialog.close();
  } catch (error) {
    message.textContent = `移除失败：${errorMessage(error, "未知错误")}`;
  }
}

async function loadDownloaders(): Promise<void> {
  const status = element<HTMLElement>("#downloader-status");
  const list = element<HTMLElement>("#downloader-list");
  status.textContent = "正在读取下载器实例…";
  try {
    const response = await fetch("/api/v1/downloaders", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as DownloaderInstanceList;
    downloaderInstances = body.items;
    downloaderConfigurationRevision = body.configuration_revision;
    refreshSourceDownloaderOptions();
    list.replaceChildren(...downloaderInstances.map((instance) => {
      const card = document.createElement("article");
      card.className = `downloader-card ${instance.connected === true ? "connected" : instance.connected === false ? "failed" : ""}`;
      const heading = document.createElement("div");
      heading.className = "downloader-card-heading";
      const title = document.createElement("h3");
      title.textContent = instance.id;
      const state = document.createElement("span");
      state.className = `badge ${instance.connected === true ? "ready" : instance.connected === false ? "error" : "pending"}`;
      state.textContent = !instance.enabled ? "已停用" : instance.connected === true ? "已连接" : instance.connected === false ? "连接失败" : "未测试";
      heading.append(title, state);
      const facts = document.createElement("dl");
      for (const [label, value] of [
        ["类型", instance.type],
        ["凭据", instance.credentials_configured ? "已配置" : "未配置"],
        ["来源引用", instance.source_profile_count],
        ["任务 / 下载", `${instance.ingest_task_count} / ${instance.download_job_count}`],
        ["熔断", instance.circuit_state === "closed"
          ? "关闭"
          : instance.circuit_state === "open"
            ? `开启 · 失败 ${instance.circuit_failure_count} 次`
            : instance.circuit_state === "half_open"
              ? "等待半开探测"
              : "未运行"],
      ]) {
        const group = document.createElement("div");
        const term = document.createElement("dt");
        term.textContent = String(label);
        const detail = document.createElement("dd");
        detail.textContent = String(value);
        group.append(term, detail);
        facts.append(group);
      }
      const endpoint = document.createElement("p");
      endpoint.className = "downloader-path";
      endpoint.textContent = `${instance.base_url} · ${instance.download_path}${instance.failure_code ? ` · ${instance.failure_code}` : ""}${instance.circuit_retry_at_utc ? ` · 下次尝试 ${new Date(instance.circuit_retry_at_utc).toLocaleString()}` : ""}`;
      const actions = document.createElement("div");
      actions.className = "downloader-actions";
      const edit = button("配置", () => openDownloaderConfig(instance));
      const test = button("测试连接", () => void testDownloader(instance.id, test));
      const probe = button("探测路径", () => void probeDownloaderPath(instance.id, probe));
      test.disabled = !instance.enabled;
      probe.disabled = !instance.enabled;
      actions.append(edit, test, probe);
      card.append(heading, facts, endpoint, actions);
      return card;
    }));
    status.textContent = body.downloads_blocked
      ? `下载已被 ${body.migration_diagnostics.map((item) => item.code).join("、")} 阻断；不会连接或启动任何下载器任务`
      : body.restart_required
      ? `${body.items.length} 个实例 · 私有配置 revision ${body.configuration_revision} 尚未应用，请重启`
      : `${body.items.length} 个 qBittorrent 实例 · 凭据只显示是否配置`;
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = `下载器读取失败：${errorMessage(error, "未知错误")}`;
    list.replaceChildren(failed);
    status.textContent = failed.textContent;
  }
}

function activeSource(): SourceProfile | null {
  return sourceProfiles.find((profile) => profile.id === activeSourceId) ?? null;
}

function refreshSourceDownloaderOptions(): void {
  const ids = [...new Set([
    ...downloaderInstances.filter((instance) => instance.enabled).map((instance) => instance.id),
    ...sourceProfiles.map((profile) => profile.downloader_id),
  ])].sort();
  element<HTMLDataListElement>("#source-downloader-options").replaceChildren(
    ...ids.map((id) => {
      const option = document.createElement("option");
      option.value = id;
      return option;
    }),
  );
}

function updateSourceWarning(): void {
  const strategy = element<HTMLSelectElement>("#source-strategy").value;
  const seeding = element<HTMLInputElement>("#source-seeding-time");
  if (strategy === "move") seeding.value = "0";
  seeding.disabled = strategy === "move";
  element<HTMLElement>("#source-warning").textContent = strategy === "move"
    ? "move 会在下载完成后移动源文件，做种分钟固定为 0；修改只影响之后创建的任务。"
    : "做种分钟：-1 无限、0 不做种、正数为上限；历史任务继续使用原 revision 路由快照。";
}

function updateSourceCredentialInputs(): void {
  const adapter = element<HTMLSelectElement>("#source-adapter").value;
  const input = element<HTMLInputElement>("#source-mikan-cookie");
  const clear = element<HTMLInputElement>("#source-mikan-cookie-clear");
  const current = activeSource();
  const isMikan = adapter === "mikan";
  input.disabled = !isMikan || clear.checked;
  clear.disabled = !isMikan || current === null;
  if (!isMikan || clear.checked) input.value = "";
  element<HTMLElement>("#source-mikan-cookie-state").textContent = !isMikan
    ? "仅 Mikan 适配器可配置登录 Cookie。"
    : current?.mikan_identity_cookie_configured
    ? "已配置（值永不回显）；留空保持不变。"
    : "未配置；可粘贴 Cookie 值或完整 Cookie。";
}

function populateSourceForm(profile: SourceProfile | null): void {
  activeSourceId = profile?.id ?? null;
  const id = element<HTMLInputElement>("#source-id");
  const adapter = element<HTMLSelectElement>("#source-adapter");
  id.disabled = profile !== null;
  adapter.disabled = profile !== null;
  id.value = profile?.id ?? "";
  element<HTMLInputElement>("#source-name").value = profile?.display_name ?? "";
  adapter.value = profile?.adapter ?? "u2";
  element<HTMLInputElement>("#source-downloader").value = profile?.downloader_id ?? "pt";
  element<HTMLSelectElement>("#source-strategy").value = profile?.file_strategy ?? "link";
  element<HTMLInputElement>("#source-category").value = profile?.category ?? "animegonet";
  element<HTMLInputElement>("#source-tags").value = profile?.tags.join(", ") ?? "";
  element<HTMLInputElement>("#source-seeding-time").value =
    String(profile?.seeding_time_minutes ?? 0);
  element<HTMLTextAreaElement>("#source-hosts").value = profile?.allowed_torrent_hosts.join("\n") ?? "";
  element<HTMLInputElement>("#source-enabled").checked = profile?.enabled ?? true;
  element<HTMLInputElement>("#source-filter-enabled").checked = profile?.rss_filter_enabled ?? false;
  element<HTMLInputElement>("#source-priority-enabled").checked = profile?.rss_priority_enabled ?? false;
  element<HTMLInputElement>("#source-mikan-cookie").value = "";
  element<HTMLInputElement>("#source-mikan-cookie-clear").checked = false;
  const remove = element<HTMLButtonElement>("#source-delete");
  remove.disabled = profile === null || profile.is_default;
  remove.title = profile?.is_default ? "默认 Mikan 来源不可删除" : "";
  element<HTMLButtonElement>("#route-preview-run").disabled = profile === null;
  element<HTMLElement>("#route-preview-result").textContent = profile === null
    ? "请先保存来源，再按持久化 revision 计算路由。"
    : `${profile.id} revision ${profile.revision}，等待预览。`;
  updateSourceWarning();
  updateSourceCredentialInputs();
  renderSourceList();
}

function renderSourceList(): void {
  const list = element<HTMLElement>("#source-list");
  if (sourceProfiles.length === 0) {
    const empty = document.createElement("p");
    empty.className = "muted empty";
    empty.textContent = "暂无来源";
    list.replaceChildren(empty);
    return;
  }
  list.replaceChildren(...sourceProfiles.map((profile) => {
    const card = document.createElement("button");
    card.type = "button";
    card.className = `source-card ${profile.id === activeSourceId ? "active" : ""}`;
    const heading = document.createElement("div");
    heading.className = "source-card-heading";
    const name = document.createElement("strong");
    name.textContent = profile.display_name;
    const revision = document.createElement("span");
    revision.textContent = `rev ${profile.revision}${profile.enabled ? "" : " · 已停用"}`;
    heading.append(name, revision);
    const route = document.createElement("p");
    route.textContent = `${profile.adapter} → ${profile.downloader_id} · ${profile.file_strategy} · ${profile.category} · 做种 ${profile.seeding_time_minutes} 分钟 · Mikan Cookie ${profile.mikan_identity_cookie_configured ? "已配置" : "未配置"} · 任务 ${profile.ingest_task_count} / RSS ${profile.rss_batch_count}`;
    card.append(heading, route);
    card.addEventListener("click", () => populateSourceForm(profile));
    return card;
  }));
}

function optionalPositiveNumber(selector: string): number | null {
  const input = element<HTMLInputElement>(selector);
  return input.value === "" || !Number.isFinite(input.valueAsNumber) ? null : input.valueAsNumber;
}

async function previewSourceRoute(): Promise<void> {
  const current = activeSource();
  if (!current) return;
  const output = element<HTMLElement>("#route-preview-result");
  const run = element<HTMLButtonElement>("#route-preview-run");
  run.disabled = true;
  output.textContent = "正在计算路由…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(
      `/api/v1/sources/${encodeURIComponent(current.id)}/route-preview`,
      {
        method: "POST",
        headers: requestHeaders,
        body: JSON.stringify({
          title: element<HTMLInputElement>("#source-name").value.trim(),
          source_work_id: element<HTMLInputElement>("#route-source-work-id").value.trim() || null,
          mikanid: optionalPositiveNumber("#route-mikanid"),
          bgmid: optionalPositiveNumber("#route-bgmid"),
          anidbid: optionalPositiveNumber("#route-anidbid"),
          imdbid: element<HTMLInputElement>("#route-imdbid").value.trim() || null,
        }),
      },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const route = await response.json() as SourceRoutePreview;
    output.textContent = route.valid
      ? [
          `有效 · ${route.source_profile_id} rev ${route.source_profile_revision} (${route.adapter})`,
          `下载器 ${route.downloader_id} · ${route.download_path ?? "路径不可用"}`,
          `媒体库 ${route.save_path}`,
          `策略 ${route.file_strategy} · 分类 ${route.category} · Tags ${route.tags.join(", ") || "—"}`,
          `做种 ${route.seeding_time_minutes} 分钟 · RSS规则 rev ${route.rss_rule_revision ?? "—"}`,
        ].join("\n")
      : `无效\n${route.errors.map((error) => `• ${error}`).join("\n")}`;
  } catch (error) {
    output.textContent = `预览失败：${errorMessage(error, "未知错误")}`;
  } finally {
    run.disabled = false;
  }
}

async function loadSources(selectedId?: string): Promise<void> {
  const status = element<HTMLElement>("#source-status");
  status.textContent = "正在读取来源配置…";
  try {
    const response = await fetch("/api/v1/sources", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as SourceProfileList;
    sourceProfiles = body.items;
    refreshSourceDownloaderOptions();
    refreshManualSourceOptions();
    const selected = sourceProfiles.find((profile) => profile.id === (selectedId ?? activeSourceId))
      ?? sourceProfiles[0]
      ?? null;
    populateSourceForm(selected);
    status.textContent = `${sourceProfiles.length} 个来源 · 修改采用 revision 乐观并发且不改变历史任务路由`;
    if (activeLegacyMikanFilter) renderLegacyMikanFilter();
  } catch (error) {
    sourceProfiles = [];
    activeSourceId = null;
    refreshManualSourceOptions();
    renderSourceList();
    status.textContent = `来源读取失败：${errorMessage(error, "未知错误")}`;
  }
}

function setManualSourceOptions(
  selector: string,
  profiles: SourceProfile[],
  emptyLabel: string,
): void {
  const select = element<HTMLSelectElement>(selector);
  const previous = select.value;
  const options = profiles.map((profile) => {
    const option = document.createElement("option");
    option.value = profile.id;
    option.textContent =
      `${profile.display_name} (${profile.id} → ${profile.downloader_id}, rev ${profile.revision})`;
    return option;
  });
  if (options.length === 0) {
    const option = document.createElement("option");
    option.value = "";
    option.textContent = emptyLabel;
    option.disabled = true;
    option.selected = true;
    options.push(option);
  }
  select.replaceChildren(...options);
  if (profiles.some((profile) => profile.id === previous)) select.value = previous;
  select.disabled = profiles.length === 0;
}

function refreshManualSourceOptions(): void {
  const enabled = sourceProfiles.filter((profile) => profile.enabled);
  setManualSourceOptions(
    "#manual-download-source",
    enabled,
    "没有已启用的输入源",
  );
  setManualSourceOptions(
    "#manual-rss-source",
    enabled.filter((profile) => profile.adapter === "mikan"),
    "没有已启用的 Mikan 输入源",
  );
  element<HTMLButtonElement>("#manual-rss-submit").disabled =
    enabled.every((profile) => profile.adapter !== "mikan");
  updateManualDownloadHint();
}

function updateManualDownloadHint(): void {
  const sourceId = element<HTMLSelectElement>("#manual-download-source").value;
  const profile = sourceProfiles.find((item) => item.id === sourceId);
  const mikanId = element<HTMLInputElement>("#manual-download-mikanid");
  const bangumiId = element<HTMLInputElement>("#manual-download-bgmid");
  const submit = element<HTMLButtonElement>("#manual-download-submit");
  mikanId.required = profile?.adapter === "mikan";
  bangumiId.required = profile?.adapter === "mikan";
  submit.disabled = profile === undefined;
  element<HTMLElement>("#manual-download-hint").textContent = profile === undefined
    ? "请先启用一个输入源。"
    : profile.adapter === "mikan"
      ? `Mikan 手动导入必须提供 mikanid 与 bgmid；将路由到 ${profile.downloader_id}，使用 ${profile.file_strategy}。`
      : `${profile.adapter.toUpperCase()} 的作品级参考 ID 可选；将路由到 ${profile.downloader_id}，使用 ${profile.file_strategy}。`;
}

function manualResultItem(
  title: string,
  detail: string,
  rejected: boolean,
): HTMLElement {
  const row = document.createElement("div");
  row.className = `manual-result-item ${rejected ? "rejected" : ""}`;
  const heading = document.createElement("strong");
  heading.textContent = title;
  const description = document.createElement("span");
  description.textContent = detail;
  row.append(heading, description);
  return row;
}

async function submitManualDownload(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  const sourceId = element<HTMLSelectElement>("#manual-download-source").value;
  const url = element<HTMLInputElement>("#manual-download-url");
  const submit = element<HTMLButtonElement>("#manual-download-submit");
  const result = element<HTMLElement>("#manual-download-result");
  let requestBody = "";
  submit.disabled = true;
  result.replaceChildren(manualResultItem("正在提交", "Torrent URL 已从输入框清除。", false));
  try {
    requestBody = JSON.stringify({
      source: sourceId,
      data: [{
        torrent: url.value,
        info: {
          title: element<HTMLInputElement>("#manual-download-title").value.trim(),
          source_item_id:
            element<HTMLInputElement>("#manual-download-item-id").value.trim() || null,
          source_work_id:
            element<HTMLInputElement>("#manual-download-work-id").value.trim() || null,
          mikanid: optionalPositiveNumber("#manual-download-mikanid"),
          bgmid: optionalPositiveNumber("#manual-download-bgmid"),
          anidbid: optionalPositiveNumber("#manual-download-anidbid"),
          imdbid: element<HTMLInputElement>("#manual-download-imdbid").value.trim() || null,
        },
      }],
    });
    url.value = "";
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const responsePromise = fetch("/api/v1/ingest", {
      method: "POST",
      headers: requestHeaders,
      body: requestBody,
    });
    requestBody = "";
    const response = await responsePromise;
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as ManualIngestResponse;
    const summary = document.createElement("p");
    summary.className = "manual-result-summary";
    summary.textContent =
      `${body.source || sourceId}：接受 ${body.accepted_count}，拒绝 ${body.rejected_count}`;
    result.replaceChildren(
      summary,
      ...body.items.map((item) => manualResultItem(
        item.ingest_id ? `已接收 · ${item.status}` : `已拒绝 · ${item.status}`,
        item.ingest_id
          ? [
              `任务 ${item.ingest_id}`,
              `来源 ${item.source_profile_id} rev ${item.source_profile_revision}`,
              `下载器 ${item.downloader_id}`,
              `文件 ${item.file_count ?? "—"}`,
              `info hash ${item.info_hash ?? "—"}`,
              `URL 指纹 ${item.torrent_url_fingerprint ?? "—"}`,
            ].join(" · ")
          : item.errors.join("；") || "未提供失败原因",
        item.ingest_id === null,
      )),
    );
    void loadDownloads();
    void loadMetadataTasks();
    void loadSources(sourceId);
  } catch (error) {
    result.replaceChildren(manualResultItem(
      "提交失败",
      errorMessage(error, "未知错误"),
      true,
    ));
  } finally {
    requestBody = "";
    url.value = "";
    updateManualDownloadHint();
  }
}

async function submitManualRss(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  const sourceId = element<HTMLSelectElement>("#manual-rss-source").value;
  const url = element<HTMLInputElement>("#manual-rss-url");
  const submit = element<HTMLButtonElement>("#manual-rss-submit");
  const result = element<HTMLElement>("#manual-rss-result");
  let requestBody = "";
  submit.disabled = true;
  result.replaceChildren(manualResultItem("正在处理", "RSS URL 已从输入框清除。", false));
  try {
    requestBody = JSON.stringify({
      source_profile_id: sourceId,
      url: url.value,
    });
    url.value = "";
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const responsePromise = fetch("/api/v1/rss/ingest", {
      method: "POST",
      headers: requestHeaders,
      body: requestBody,
    });
    requestBody = "";
    const response = await responsePromise;
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as ManualRssResponse;
    const accepted = body.items.filter((item) => item.ingest_task_id !== null).length;
    const summary = document.createElement("p");
    summary.className = "manual-result-summary";
    summary.textContent =
      `批次 ${body.batch_id} · mikanid ${body.mikanid ?? "未识别"} · `
      + `bgmid ${body.bgmid ?? "未取得"}（${body.bgmid_discovery_state}`
      + `${body.bgmid_discovery_failure_code ? ` / ${body.bgmid_discovery_failure_code}` : ""}）`
      + ` · 接收 ${accepted}/${body.items.length} · 规则 rev ${body.rule_revision}`;
    result.replaceChildren(
      summary,
      ...body.items.map((item, index) => manualResultItem(
        `候选 ${index + 1} · ${item.status}`,
        [
          item.decision_kind,
          item.decision_reason,
          item.ingest_task_id ? `任务 ${item.ingest_task_id}` : null,
          item.errors.length > 0 ? item.errors.join("；") : null,
        ].filter((value): value is string => value !== null).join(" · "),
        item.ingest_task_id === null,
      )),
    );
    void loadDownloads();
    void loadMetadataTasks();
    void loadSources(sourceId);
  } catch (error) {
    result.replaceChildren(manualResultItem(
      "RSS 处理失败",
      errorMessage(error, "未知错误"),
      true,
    ));
  } finally {
    requestBody = "";
    url.value = "";
    submit.disabled = sourceProfiles.every(
      (profile) => !profile.enabled || profile.adapter !== "mikan",
    );
  }
}

function optionalInteger(selector: string): number | null {
  const input = element<HTMLInputElement>(selector);
  return input.value === "" || !Number.isInteger(input.valueAsNumber)
    ? null
    : input.valueAsNumber;
}

function currentMikanWorkId(): number | null {
  const input = element<HTMLInputElement>("#mikan-work-rule-id");
  return Number.isInteger(input.valueAsNumber) && input.valueAsNumber > 0
    ? input.valueAsNumber
    : null;
}

function invalidateMikanWorkRule(): void {
  loadedMikanWorkId = null;
  activeMikanWorkRule = null;
  activeMikanWorkImpact = null;
  element<HTMLButtonElement>("#mikan-work-rule-save").disabled = true;
  element<HTMLButtonElement>("#mikan-work-rule-delete").disabled = true;
  element<HTMLButtonElement>("#mikan-work-rule-rematch").disabled = true;
  element<HTMLElement>("#mikan-work-rule-status").textContent =
    "mikanid 已改变，请先读取最新规则与影响，避免覆盖现有 revision。";
  element<HTMLElement>("#mikan-work-impact-summary").replaceChildren(
    Object.assign(document.createElement("p"), {
      className: "muted",
      textContent: "尚未读取当前 mikanid。",
    }),
  );
  element<HTMLElement>("#mikan-work-impact-tasks").replaceChildren();
}

function populateMikanWorkRule(rule: MikanWorkRule | null): void {
  element<HTMLInputElement>("#mikan-work-rule-bgmid").value =
    rule?.bgmid?.toString() ?? "";
  element<HTMLInputElement>("#mikan-work-rule-series").value =
    rule?.tmdb_series_id?.toString() ?? "";
  element<HTMLInputElement>("#mikan-work-rule-season").value =
    rule?.tmdb_season_number?.toString() ?? "";
  element<HTMLInputElement>("#mikan-work-rule-offset").value =
    rule?.episode_offset?.toString() ?? "";
  // A sample episode is validation-only and is never persisted with the rule.
  element<HTMLInputElement>("#mikan-work-rule-sample").value = "";
  element<HTMLInputElement>("#mikan-work-rule-enabled").checked = rule?.enabled ?? true;
  element<HTMLButtonElement>("#mikan-work-rule-save").disabled = false;
  element<HTMLButtonElement>("#mikan-work-rule-delete").disabled = rule === null;
}

const mikanImpactLabels: Record<MikanWorkImpactCategory, string> = {
  future: "尚未匹配，将自动使用当前规则",
  retryable_failed: "失败，可显式重新匹配",
  active: "处理中，保持当前租约",
  resolved_protected: "已解析保护，不自动回溯",
  completed_protected: "已整理保护，不移动文件",
  other: "其他状态，不自动改写",
};

function mikanImpactStat(value: number, label: string): HTMLElement {
  const card = document.createElement("div");
  card.className = "mikan-impact-stat";
  const count = document.createElement("strong");
  count.textContent = String(value);
  const description = document.createElement("span");
  description.textContent = label;
  card.append(count, description);
  return card;
}

function renderMikanWorkImpact(impact: MikanWorkImpact): void {
  activeMikanWorkImpact = impact;
  const summary = element<HTMLElement>("#mikan-work-impact-summary");
  summary.replaceChildren(
    mikanImpactStat(impact.total_task_count, "关联任务"),
    mikanImpactStat(impact.future_task_count, "未来自动应用"),
    mikanImpactStat(impact.retryable_failed_task_count, "可显式重试"),
    mikanImpactStat(impact.active_task_count, "活动中保护"),
    mikanImpactStat(impact.resolved_protected_task_count, "已解析保护"),
    mikanImpactStat(impact.completed_protected_task_count, "已整理保护"),
  );
  if (impact.other_task_count > 0 || impact.is_truncated) {
    const note = document.createElement("p");
    note.className = "muted";
    note.textContent = [
      impact.other_task_count > 0 ? `另有 ${impact.other_task_count} 个其他状态` : null,
      impact.is_truncated ? `列表只显示前 ${impact.items.length} 个，统计仍为全量` : null,
    ].filter((value): value is string => value !== null).join("；");
    summary.append(note);
  }

  const tasks = element<HTMLElement>("#mikan-work-impact-tasks");
  if (impact.items.length === 0) {
    const empty = document.createElement("p");
    empty.className = "muted";
    empty.textContent = "该 mikanid 暂无关联任务。";
    tasks.replaceChildren(empty);
  } else {
    tasks.replaceChildren(...impact.items.map((item) => {
      const row = document.createElement("article");
      row.className = `mikan-impact-task ${item.category}`;
      const title = document.createElement("strong");
      title.textContent = item.title;
      const identity = document.createElement("span");
      identity.textContent = [
        item.task_id,
        `${item.source} · ${statusLabels[item.status] ?? item.status}`,
        `bgmid ${item.bgmid ?? "—"}`,
        `TMDB ${item.tmdb_series_id ?? "—"} / S${item.tmdb_season_number ?? "—"}`,
      ].join(" · ");
      const category = document.createElement("span");
      category.textContent =
        `${mikanImpactLabels[item.category]} · 更新 ${new Date(item.updated_at_utc).toLocaleString()}`;
      row.append(title, identity, category);
      return row;
    }));
  }
  element<HTMLButtonElement>("#mikan-work-rule-rematch").disabled =
    impact.retryable_failed_task_count === 0;
}

async function loadMikanWorkRule(): Promise<void> {
  const mikanId = currentMikanWorkId();
  const status = element<HTMLElement>("#mikan-work-rule-status");
  if (mikanId === null) {
    invalidateMikanWorkRule();
    status.textContent = "mikanid 必须是正整数。";
    return;
  }

  const load = element<HTMLButtonElement>("#mikan-work-rule-load");
  load.disabled = true;
  status.textContent = "正在读取规则 revision 与全量影响统计…";
  try {
    const [ruleResponse, impactResponse] = await Promise.all([
      fetch(`/api/v1/mikan/work-rules/${mikanId}`, { headers }),
      fetch(`/api/v1/mikan/work-rules/${mikanId}/impact?limit=100`, { headers }),
    ]);
    if (!ruleResponse.ok && ruleResponse.status !== 404) {
      throw new Error(await responseError(ruleResponse));
    }
    if (!impactResponse.ok) throw new Error(await responseError(impactResponse));
    const rule = ruleResponse.status === 404
      ? null
      : await ruleResponse.json() as MikanWorkRule;
    const impact = await impactResponse.json() as MikanWorkImpact;
    loadedMikanWorkId = mikanId;
    activeMikanWorkRule = rule;
    populateMikanWorkRule(rule);
    renderMikanWorkImpact(impact);
    status.textContent = rule === null
      ? `mikanid ${mikanId} 尚无人工规则；保存时从 revision 0 创建。`
      : `已读取 revision ${rule.revision} · ${rule.enabled ? "人工规则已启用（最高优先级）" : "人工规则已禁用"} · 更新 ${new Date(rule.updated_at_utc).toLocaleString()}`;
  } catch (error) {
    invalidateMikanWorkRule();
    status.textContent = `读取失败：${errorMessage(error, "未知错误")}`;
  } finally {
    load.disabled = false;
  }
}

async function saveMikanWorkRule(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  const mikanId = currentMikanWorkId();
  if (mikanId === null || loadedMikanWorkId !== mikanId) {
    invalidateMikanWorkRule();
    return;
  }
  const save = element<HTMLButtonElement>("#mikan-work-rule-save");
  const status = element<HTMLElement>("#mikan-work-rule-status");
  save.disabled = true;
  status.textContent = "正在保存；填写样例 EP 时会先在线验证 TMDB…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(`/api/v1/mikan/work-rules/${mikanId}`, {
      method: "PUT",
      headers: requestHeaders,
      body: JSON.stringify({
        bgmid: optionalPositiveNumber("#mikan-work-rule-bgmid"),
        tmdb_series_id: optionalPositiveNumber("#mikan-work-rule-series"),
        tmdb_season_number: optionalPositiveNumber("#mikan-work-rule-season"),
        episode_offset: optionalInteger("#mikan-work-rule-offset"),
        sample_source_episode: optionalPositiveNumber("#mikan-work-rule-sample"),
        enabled: element<HTMLInputElement>("#mikan-work-rule-enabled").checked,
        expected_revision: activeMikanWorkRule?.revision ?? 0,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    const saved = await response.json() as MikanWorkRule;
    activeMikanWorkRule = saved;
    await loadMikanWorkRule();
    status.textContent =
      `已保存 revision ${saved.revision}；规则只影响之后的匹配，已解析/已整理任务未改写。`;
  } catch (error) {
    status.textContent =
      `保存失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新读取。`;
    save.disabled = false;
  }
}

async function deleteMikanWorkRule(): Promise<void> {
  const mikanId = currentMikanWorkId();
  const rule = activeMikanWorkRule;
  if (mikanId === null || loadedMikanWorkId !== mikanId || rule === null) return;
  if (!window.confirm(
    `清除 mikanid ${mikanId} 的人工规则？已完成记录和媒体文件不会删除或移动。`,
  )) return;
  const status = element<HTMLElement>("#mikan-work-rule-status");
  status.textContent = "正在清除人工规则…";
  try {
    const response = await fetch(
      `/api/v1/mikan/work-rules/${mikanId}?expected_revision=${rule.revision}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    activeMikanWorkRule = null;
    await loadMikanWorkRule();
    status.textContent =
      "人工规则已清除；之后任务恢复自动匹配，既有解析和媒体文件保持不变。";
  } catch (error) {
    status.textContent =
      `清除失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新读取。`;
  }
}

async function rematchMikanWorkTasks(): Promise<void> {
  const mikanId = currentMikanWorkId();
  const impact = activeMikanWorkImpact;
  if (mikanId === null || loadedMikanWorkId !== mikanId || impact === null) return;
  if (impact.retryable_failed_task_count === 0) return;
  if (!window.confirm(
    `重新匹配 ${impact.retryable_failed_task_count} 个失败任务？`
    + ` ${impact.resolved_protected_task_count} 个已解析和`
    + ` ${impact.completed_protected_task_count} 个已整理任务保持不变，媒体文件不会移动。`,
  )) return;
  const button = element<HTMLButtonElement>("#mikan-work-rule-rematch");
  const status = element<HTMLElement>("#mikan-work-rule-status");
  button.disabled = true;
  status.textContent = "正在按当前规则 revision 重新排队失败任务…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(`/api/v1/mikan/work-rules/${mikanId}/rematch`, {
      method: "POST",
      headers: requestHeaders,
      body: JSON.stringify({
        expected_rule_revision: activeMikanWorkRule?.revision ?? 0,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    const result = await response.json() as MikanWorkRematchResponse;
    await loadMikanWorkRule();
    status.textContent =
      `已重新排队 ${result.retried_task_count} 个失败任务；已解析/已整理任务与媒体文件未改写。`;
    void loadMetadataTasks();
  } catch (error) {
    status.textContent =
      `重新匹配失败：${errorMessage(error, "未知错误")}；请重新读取规则与影响。`;
    button.disabled = false;
  }
}

function sourceHosts(): string[] {
  return element<HTMLTextAreaElement>("#source-hosts").value
    .split(/[\r\n,，]+/u)
    .map((host) => host.trim().toLowerCase())
    .filter(Boolean);
}

function sourceTags(): string[] {
  return element<HTMLInputElement>("#source-tags").value
    .split(/[\r\n,，]+/u)
    .map((tag) => tag.trim())
    .filter(Boolean);
}

async function saveSource(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  const current = activeSource();
  const save = element<HTMLButtonElement>("#source-save");
  const status = element<HTMLElement>("#source-status");
  const common = {
    display_name: element<HTMLInputElement>("#source-name").value.trim(),
    downloader_id: element<HTMLInputElement>("#source-downloader").value.trim(),
    file_strategy: element<HTMLSelectElement>("#source-strategy").value,
    category: element<HTMLInputElement>("#source-category").value.trim(),
    tags: sourceTags(),
    seeding_time_minutes: element<HTMLInputElement>("#source-seeding-time").valueAsNumber,
    allowed_torrent_hosts: sourceHosts(),
    rss_filter_enabled: element<HTMLInputElement>("#source-filter-enabled").checked,
    rss_priority_enabled: element<HTMLInputElement>("#source-priority-enabled").checked,
    enabled: element<HTMLInputElement>("#source-enabled").checked,
    mikan_identity_cookie:
      element<HTMLInputElement>("#source-mikan-cookie").value || null,
  };
  const payload = current
    ? {
        ...common,
        clear_mikan_identity_cookie:
          element<HTMLInputElement>("#source-mikan-cookie-clear").checked,
        expected_revision: current.revision,
      }
    : {
        ...common,
        id: element<HTMLInputElement>("#source-id").value,
        adapter: element<HTMLSelectElement>("#source-adapter").value,
      };
  save.disabled = true;
  status.textContent = current ? "正在保存来源…" : "正在创建来源…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(
      current ? `/api/v1/sources/${encodeURIComponent(current.id)}` : "/api/v1/sources",
      {
        method: current ? "PUT" : "POST",
        headers: requestHeaders,
        body: JSON.stringify(payload),
      },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const saved = await response.json() as SourceProfile;
    await loadSources(saved.id);
    status.textContent = `已保存 ${saved.display_name} · revision ${saved.revision}`;
  } catch (error) {
    status.textContent = `保存失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新选择来源。`;
  } finally {
    save.disabled = false;
  }
}

async function deleteSource(): Promise<void> {
  const current = activeSource();
  if (!current || current.is_default) return;
  if (!window.confirm(`删除来源 ${current.display_name}？已有任务或 RSS batch 引用时服务端会拒绝。`)) return;
  const status = element<HTMLElement>("#source-status");
  status.textContent = "正在删除来源…";
  try {
    const response = await fetch(
      `/api/v1/sources/${encodeURIComponent(current.id)}?expected_revision=${current.revision}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    activeSourceId = null;
    await loadSources();
    status.textContent = `来源 ${current.id} 已删除`;
  } catch (error) {
    status.textContent = `删除失败：${errorMessage(error, "未知错误")}`;
  }
}

function moveItem<T>(items: T[], index: number, delta: number): void {
  const target = index + delta;
  if (target < 0 || target >= items.length) return;
  [items[index], items[target]] = [items[target], items[index]];
}

function nextRuleId(prefix: string): string {
  ruleIdSequence += 1;
  return `${prefix}-${Date.now().toString(36)}-${ruleIdSequence.toString(36)}`;
}

function button(label: string, action: () => void): HTMLButtonElement {
  const result = document.createElement("button");
  result.type = "button";
  result.textContent = label;
  result.addEventListener("click", action);
  return result;
}

function renderArrayEditor(
  rule: RssNamedArray,
  index: number,
  count: number,
  onMove: (delta: number) => void,
  onRemove: () => void,
): HTMLElement {
  const card = document.createElement("article");
  card.className = "rss-array";
  const fields = document.createElement("div");
  fields.className = "rss-array-fields";
  const idLabel = document.createElement("label");
  idLabel.textContent = "ID";
  const id = document.createElement("input");
  id.value = rule.id;
  id.addEventListener("input", () => { rule.id = id.value; });
  idLabel.append(id);
  const nameLabel = document.createElement("label");
  nameLabel.textContent = "名称";
  const name = document.createElement("input");
  name.value = rule.name;
  name.addEventListener("input", () => { rule.name = name.value; });
  nameLabel.append(name);
  const valuesLabel = document.createElement("label");
  valuesLabel.textContent = "匹配值（逗号分隔）";
  const values = document.createElement("input");
  values.value = rule.values.join(", ");
  values.addEventListener("input", () => {
    rule.values = values.value.split(/[,，\n]/u).map((value) => value.trim()).filter(Boolean);
  });
  valuesLabel.append(values);
  const enabledLabel = document.createElement("label");
  enabledLabel.className = "rss-array-enabled";
  const enabled = document.createElement("input");
  enabled.type = "checkbox";
  enabled.checked = rule.enabled;
  enabled.addEventListener("change", () => { rule.enabled = enabled.checked; });
  enabledLabel.append(enabled, "启用");
  fields.append(idLabel, nameLabel, valuesLabel, enabledLabel);
  const actions = document.createElement("div");
  actions.className = "rss-array-actions";
  const up = button("上移", () => onMove(-1));
  up.disabled = index === 0;
  const down = button("下移", () => onMove(1));
  down.disabled = index + 1 === count;
  actions.append(up, down, button("删除", onRemove));
  card.append(fields, actions);
  return card;
}

function renderArrayList(container: HTMLElement, rules: RssNamedArray[]): void {
  container.replaceChildren(...rules.map((rule, index) => renderArrayEditor(
    rule,
    index,
    rules.length,
    (delta) => { moveItem(rules, index, delta); renderRssRules(); },
    () => { rules.splice(index, 1); renderRssRules(); },
  )));
}

function renderRssRules(): void {
  if (!activeRssRules) return;
  element<HTMLElement>("#rss-rule-status").textContent =
    `revision ${activeRssRules.revision} · 旧过滤 ${activeRssRules.rss_filter_enabled ? "开启" : "关闭"} · 批次优选 ${activeRssRules.rss_priority_enabled ? "开启" : "关闭"}`;
  renderArrayList(element<HTMLElement>("#rss-whitelist"), activeRssRules.whitelist);
  renderArrayList(element<HTMLElement>("#rss-blacklist"), activeRssRules.blacklist);
  const snapshots = element<HTMLSelectElement>("#rss-rule-snapshots");
  snapshots.replaceChildren(...activeRssRules.snapshots.map((snapshot) => {
    const option = document.createElement("option");
    option.value = String(snapshot.revision);
    option.textContent =
      `r${snapshot.revision} · ${new Date(snapshot.created_at_utc).toLocaleString()}`;
    return option;
  }));
  const groupContainer = element<HTMLElement>("#rss-priority-groups");
  groupContainer.replaceChildren(...activeRssRules.priority_groups.map((group, groupIndex) => {
    const card = document.createElement("article");
    card.className = "rss-group";
    const heading = document.createElement("div");
    heading.className = "rss-group-heading";
    const idLabel = document.createElement("label");
    idLabel.textContent = "组 ID";
    const id = document.createElement("input");
    id.value = group.id;
    id.addEventListener("input", () => { group.id = id.value; });
    idLabel.append(id);
    const nameLabel = document.createElement("label");
    nameLabel.textContent = "组名称";
    const name = document.createElement("input");
    name.value = group.name;
    name.addEventListener("input", () => { group.name = name.value; });
    nameLabel.append(name);
    const groupActions = document.createElement("div");
    groupActions.className = "rss-group-actions";
    const up = button("上移组", () => { moveItem(activeRssRules!.priority_groups, groupIndex, -1); renderRssRules(); });
    up.disabled = groupIndex === 0;
    const down = button("下移组", () => { moveItem(activeRssRules!.priority_groups, groupIndex, 1); renderRssRules(); });
    down.disabled = groupIndex + 1 === activeRssRules!.priority_groups.length;
    groupActions.append(up, down, button("删除组", () => {
      activeRssRules!.priority_groups.splice(groupIndex, 1);
      renderRssRules();
    }));
    heading.append(idLabel, nameLabel, groupActions);
    const arrays = document.createElement("div");
    arrays.className = "rss-array-list";
    renderArrayList(arrays, group.arrays);
    const add = button("添加组内数组", () => {
      group.arrays.push({ id: nextRuleId("array"), name: "新数组", enabled: true, values: [] });
      renderRssRules();
    });
    card.append(heading, arrays, add);
    return card;
  }));
}

async function loadRssRules(): Promise<void> {
  const status = element<HTMLElement>("#rss-rule-status");
  status.textContent = "正在读取 Mikan 规则…";
  try {
    const response = await fetch("/api/v1/rss-rules/mikan", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    activeRssRules = await response.json() as RssRuleSnapshot;
    renderRssRules();
  } catch (error) {
    activeRssRules = null;
    status.textContent = `规则读取失败：${errorMessage(error, "未知错误")}`;
  }
}

async function saveRssRules(): Promise<void> {
  if (!activeRssRules) return;
  const save = element<HTMLButtonElement>("#rss-save");
  const status = element<HTMLElement>("#rss-rule-status");
  save.disabled = true;
  status.textContent = "正在保存规则…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/rss-rules/mikan", {
      method: "PUT",
      headers: requestHeaders,
      body: JSON.stringify({
        expected_revision: activeRssRules.revision,
        whitelist: activeRssRules.whitelist,
        blacklist: activeRssRules.blacklist,
        priority_groups: activeRssRules.priority_groups,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    activeRssRules = await response.json() as RssRuleSnapshot;
    renderRssRules();
    status.textContent = `保存成功 · revision ${activeRssRules.revision}`;
  } catch (error) {
    status.textContent = `保存失败：${errorMessage(error, "未知错误")}；如有 revision 冲突请重新载入。`;
  } finally {
    save.disabled = false;
  }
}

async function rollbackRssRules(): Promise<void> {
  if (!activeRssRules) return;
  const target = Number(element<HTMLSelectElement>("#rss-rule-snapshots").value);
  if (!Number.isInteger(target) || target < 1 || target === activeRssRules.revision) return;
  if (!window.confirm(
    `将候选规则回滚为 revision ${target}？系统会创建新的 revision，历史快照不会删除。`,
  )) return;
  const status = element<HTMLElement>("#rss-rule-status");
  const rollback = element<HTMLButtonElement>("#rss-rule-rollback");
  rollback.disabled = true;
  status.textContent = `正在回滚到 revision ${target}…`;
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/rss-rules/mikan/rollback", {
      method: "POST",
      headers: requestHeaders,
      body: JSON.stringify({
        expected_revision: activeRssRules.revision,
        target_revision: target,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    activeRssRules = await response.json() as RssRuleSnapshot;
    renderRssRules();
    status.textContent = `已回滚并创建 revision ${activeRssRules.revision}`;
  } catch (error) {
    status.textContent =
      `回滚失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新载入。`;
  } finally {
    rollback.disabled = false;
  }
}

async function previewRssRules(): Promise<void> {
  const results = element<HTMLElement>("#rss-preview-results");
  const titles = element<HTMLTextAreaElement>("#rss-preview-titles").value
    .split(/\r?\n/u).map((title) => title.trim()).filter(Boolean);
  if (titles.length === 0) {
    results.textContent = "请先输入至少一个候选标题。";
    return;
  }

  const mikanIdValue = element<HTMLInputElement>("#rss-preview-mikanid").valueAsNumber;
  const kind = element<HTMLInputElement>("#rss-preview-kind").value.trim();
  const episode = element<HTMLInputElement>("#rss-preview-episode").value.trim();
  results.textContent = "正在执行服务端预览…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/rss-rules/mikan/preview", {
      method: "POST",
      headers: requestHeaders,
      body: JSON.stringify({ candidates: titles.map((title, index) => ({
        id: `candidate-${index + 1}`,
        title,
        mikanid: Number.isFinite(mikanIdValue) ? mikanIdValue : null,
        source_episode_kind: kind || null,
        source_episode: episode || null,
      })) }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as { decisions: RssRuleDecision[] };
    results.replaceChildren(...body.decisions.map((decision, index) => {
      const row = document.createElement("div");
      row.className = `rss-decision ${decision.decision === "winner" ? "winner" : decision.decision.startsWith("rejected") ? "rejected" : "suppressed"}`;
      const groups = decision.evaluated_priority_groups.length > 0
        ? ` · groups ${decision.evaluated_priority_groups.join(" → ")}` : "";
      row.textContent = `${titles[index]} · ${decision.decision} · ${decision.reason}${decision.winner_id ? ` · winner ${decision.winner_id}` : ""}${groups}`;
      return row;
    }));
  } catch (error) {
    results.textContent = `预览失败：${errorMessage(error, "未知错误")}`;
  }
}

function legacyTierRules(tier: number): LegacyMikanFilterRule[] {
  return (activeLegacyMikanFilter?.rules ?? [])
    .filter((rule) => rule.tier === tier)
    .sort((left, right) => left.position - right.position);
}

function normalizeLegacyTier(tier: number): void {
  legacyTierRules(tier).forEach((rule, position) => { rule.position = position; });
}

function moveLegacyRule(rule: LegacyMikanFilterRule, delta: number): void {
  const rules = legacyTierRules(rule.tier);
  const index = rules.indexOf(rule);
  const target = index + delta;
  if (index < 0 || target < 0 || target >= rules.length) return;
  [rules[index].position, rules[target].position] =
    [rules[target].position, rules[index].position];
  renderLegacyMikanFilter();
}

function removeLegacyRule(rule: LegacyMikanFilterRule): void {
  if (!activeLegacyMikanFilter) return;
  const index = activeLegacyMikanFilter.rules.indexOf(rule);
  if (index >= 0) activeLegacyMikanFilter.rules.splice(index, 1);
  normalizeLegacyTier(rule.tier);
  renderLegacyMikanFilter();
}

function renderLegacyRule(rule: LegacyMikanFilterRule): HTMLElement {
  const tierRules = legacyTierRules(rule.tier);
  const tierIndex = tierRules.indexOf(rule);
  const card = document.createElement("div");
  card.className = "legacy-filter-rule";
  card.dataset.ruleIndex = String(activeLegacyMikanFilter!.rules.indexOf(rule));

  const keyLabel = document.createElement("label");
  keyLabel.textContent = "旧版键（区分大小写）";
  const key = document.createElement("input");
  key.value = rule.key;
  key.maxLength = 1024;
  key.addEventListener("input", () => { rule.key = key.value; });
  keyLabel.append(key);

  const switches = document.createElement("div");
  switches.className = "legacy-filter-rule-switches";
  const whitelistSwitch = document.createElement("input");
  whitelistSwitch.type = "checkbox";
  whitelistSwitch.checked = rule.whitelist_enabled;
  whitelistSwitch.addEventListener("change", () => {
    rule.whitelist_enabled = whitelistSwitch.checked;
    renderLegacyWarnings();
  });
  const whitelistSwitchLabel = document.createElement("label");
  whitelistSwitchLabel.append(whitelistSwitch, "启用白名单");
  const blacklistSwitch = document.createElement("input");
  blacklistSwitch.type = "checkbox";
  blacklistSwitch.checked = rule.blacklist_enabled;
  blacklistSwitch.addEventListener("change", () => {
    rule.blacklist_enabled = blacklistSwitch.checked;
    renderLegacyWarnings();
  });
  const blacklistSwitchLabel = document.createElement("label");
  blacklistSwitchLabel.append(blacklistSwitch, "启用黑名单");
  switches.append(whitelistSwitchLabel, blacklistSwitchLabel);

  const valueEditor = (
    title: string,
    kind: "whitelist" | "blacklist",
    values: string[],
  ): HTMLLabelElement => {
    const label = document.createElement("label");
    label.textContent = `${title}（JSON 字符串数组）`;
    const textarea = document.createElement("textarea");
    textarea.className = "legacy-filter-values";
    textarea.dataset.kind = kind;
    textarea.rows = 3;
    textarea.spellcheck = false;
    textarea.value = JSON.stringify(values);
    textarea.addEventListener("input", () => {
      textarea.classList.remove("invalid");
    });
    label.append(textarea);
    return label;
  };

  const actions = document.createElement("div");
  actions.className = "legacy-filter-rule-actions";
  const up = button("上移", () => moveLegacyRule(rule, -1));
  up.disabled = tierIndex === 0;
  const down = button("下移", () => moveLegacyRule(rule, 1));
  down.disabled = tierIndex + 1 === tierRules.length;
  actions.append(up, down, button("删除", () => removeLegacyRule(rule)));
  card.append(
    keyLabel,
    switches,
    valueEditor("白名单", "whitelist", rule.whitelist),
    valueEditor("黑名单", "blacklist", rule.blacklist),
    actions,
  );
  return card;
}

function renderLegacyWarnings(): void {
  const warning = element<HTMLElement>("#legacy-filter-warning");
  if (!activeLegacyMikanFilter) {
    warning.textContent = "";
    return;
  }
  const messages: string[] = [];
  if (legacyTierRules(0).length > 1) {
    messages.push("F0 有多条规则：上游语义是全部执行、最后一条结果覆盖前面结果，不是 AND。");
  }
  const emptyRules = activeLegacyMikanFilter.rules.filter((rule) =>
    (rule.whitelist_enabled && rule.whitelist.includes(""))
    || (rule.blacklist_enabled && rule.blacklist.includes("")));
  if (emptyRules.length > 0) {
    messages.push(`有 ${emptyRules.length} 条启用规则包含空关键词；空字符串会匹配所有标题。`);
  }
  warning.textContent = messages.join(" ");
}

function renderLegacyMikanFilter(): void {
  if (!activeLegacyMikanFilter) return;
  for (let tier = 0; tier <= 4; tier += 1) {
    normalizeLegacyTier(tier);
    element<HTMLElement>(`#legacy-filter-tier-${tier}`).replaceChildren(
      ...legacyTierRules(tier).map(renderLegacyRule),
    );
  }
  const source = sourceProfiles.find((profile) => profile.id === "mikan");
  const enabled = element<HTMLInputElement>("#legacy-filter-enabled");
  enabled.checked = source?.rss_filter_enabled ?? false;
  enabled.disabled = source === undefined;
  element<HTMLElement>("#legacy-filter-status").textContent =
    `revision ${activeLegacyMikanFilter.revision} · 更新来源 ${activeLegacyMikanFilter.updated_source}`
    + ` · 总开关 ${enabled.checked ? "开启" : "关闭"} · 匹配区分大小写`;
  element<HTMLTextAreaElement>("#legacy-filter-json").value =
    activeLegacyMikanFilter.legacy_json;
  const snapshots = element<HTMLSelectElement>("#legacy-filter-snapshots");
  snapshots.replaceChildren(...activeLegacyMikanFilter.snapshots.map((snapshot) => {
    const option = document.createElement("option");
    option.value = String(snapshot.revision);
    option.textContent =
      `r${snapshot.revision} · ${snapshot.updated_source} · ${new Date(snapshot.created_at_utc).toLocaleString()}`;
    return option;
  }));
  renderLegacyWarnings();
}

function readLegacyFilterDraft(): LegacyMikanFilterRule[] {
  if (!activeLegacyMikanFilter) throw new Error("规则尚未载入。");
  for (const card of document.querySelectorAll<HTMLElement>(".legacy-filter-rule")) {
    const index = Number(card.dataset.ruleIndex);
    const rule = activeLegacyMikanFilter.rules[index];
    if (!rule) throw new Error("规则编辑器状态已过期，请重新载入。");
    for (const textarea of card.querySelectorAll<HTMLTextAreaElement>(".legacy-filter-values")) {
      try {
        const parsed = JSON.parse(textarea.value) as unknown;
        if (!Array.isArray(parsed) || !parsed.every((value) => typeof value === "string")) {
          throw new Error("必须是 JSON 字符串数组");
        }
        rule[textarea.dataset.kind as "whitelist" | "blacklist"] = parsed;
        textarea.classList.remove("invalid");
      } catch {
        textarea.classList.add("invalid");
        throw new Error(`F${rule.tier} / ${rule.key || "空键"} 的名单不是有效 JSON 字符串数组。`);
      }
    }
  }
  for (let tier = 0; tier <= 4; tier += 1) normalizeLegacyTier(tier);
  renderLegacyWarnings();
  return activeLegacyMikanFilter.rules;
}

async function loadLegacyMikanFilter(): Promise<void> {
  const status = element<HTMLElement>("#legacy-filter-status");
  status.textContent = "正在读取旧 Mikan 过滤规则…";
  try {
    const response = await fetch("/api/v1/mikan/legacy-filter", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    activeLegacyMikanFilter = await response.json() as LegacyMikanFilterResponse;
    renderLegacyMikanFilter();
  } catch (error) {
    activeLegacyMikanFilter = null;
    status.textContent = `读取失败：${errorMessage(error, "未知错误")}`;
  }
}

async function saveLegacyMikanFilter(): Promise<void> {
  if (!activeLegacyMikanFilter) return;
  const buttonElement = element<HTMLButtonElement>("#legacy-filter-save");
  const status = element<HTMLElement>("#legacy-filter-status");
  try {
    const rules = readLegacyFilterDraft();
    buttonElement.disabled = true;
    status.textContent = "正在保存五级过滤规则…";
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/mikan/legacy-filter", {
      method: "PUT",
      headers: requestHeaders,
      body: JSON.stringify({
        expected_revision: activeLegacyMikanFilter.revision,
        rules,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    activeLegacyMikanFilter = await response.json() as LegacyMikanFilterResponse;
    renderLegacyMikanFilter();
    status.textContent = `保存成功 · revision ${activeLegacyMikanFilter.revision}`;
  } catch (error) {
    status.textContent =
      `保存失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新载入。`;
  } finally {
    buttonElement.disabled = false;
  }
}

function addLegacyMikanRule(tier: number): void {
  if (!activeLegacyMikanFilter) return;
  const rules = legacyTierRules(tier);
  let sequence = rules.length + 1;
  let key = tier === 1 ? "key_3951_12" : tier === 2 ? "3951" : tier === 3 ? "12"
    : tier === 4 ? "Group" : `global-${sequence}`;
  while (rules.some((rule) => rule.key === key)) {
    sequence += 1;
    key = `rule-${sequence}`;
  }
  activeLegacyMikanFilter.rules.push({
    tier,
    position: rules.length,
    key,
    whitelist_enabled: false,
    blacklist_enabled: false,
    whitelist: [],
    blacklist: [],
  });
  renderLegacyMikanFilter();
}

async function importLegacyMikanFilter(): Promise<void> {
  if (!activeLegacyMikanFilter) return;
  const status = element<HTMLElement>("#legacy-filter-status");
  status.textContent = "正在导入旧版 JSON…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/mikan/legacy-filter/import", {
      method: "POST",
      headers: requestHeaders,
      body: JSON.stringify({
        expected_revision: activeLegacyMikanFilter.revision,
        legacy_json: element<HTMLTextAreaElement>("#legacy-filter-json").value,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    activeLegacyMikanFilter = await response.json() as LegacyMikanFilterResponse;
    renderLegacyMikanFilter();
    status.textContent = `导入成功 · revision ${activeLegacyMikanFilter.revision}`;
  } catch (error) {
    status.textContent =
      `导入失败：${errorMessage(error, "未知错误")}；原规则未修改。`;
  }
}

async function rollbackLegacyMikanFilter(): Promise<void> {
  if (!activeLegacyMikanFilter) return;
  const target = Number(element<HTMLSelectElement>("#legacy-filter-snapshots").value);
  if (!Number.isInteger(target) || target < 1 || target === activeLegacyMikanFilter.revision) return;
  if (!window.confirm(`将当前规则回滚为 revision ${target}？系统会创建新的审计 revision，不删除历史。`)) return;
  const status = element<HTMLElement>("#legacy-filter-status");
  status.textContent = `正在回滚到 revision ${target}…`;
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/mikan/legacy-filter/rollback", {
      method: "POST",
      headers: requestHeaders,
      body: JSON.stringify({
        expected_revision: activeLegacyMikanFilter.revision,
        target_revision: target,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    activeLegacyMikanFilter = await response.json() as LegacyMikanFilterResponse;
    renderLegacyMikanFilter();
    status.textContent = `已回滚并创建 revision ${activeLegacyMikanFilter.revision}`;
  } catch (error) {
    status.textContent =
      `回滚失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新载入。`;
  }
}

async function previewLegacyMikanFilter(): Promise<void> {
  const result = element<HTMLElement>("#legacy-filter-preview-result");
  try {
    const rules = readLegacyFilterDraft();
    result.textContent = "正在执行服务端预览…";
    const numberOrNull = (selector: string): number | null => {
      const input = element<HTMLInputElement>(selector);
      return input.value === "" ? null : input.valueAsNumber;
    };
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/mikan/legacy-filter/preview", {
      method: "POST",
      headers: requestHeaders,
      body: JSON.stringify({
        title: element<HTMLInputElement>("#legacy-filter-preview-title").value,
        mikanid: numberOrNull("#legacy-filter-preview-mikanid"),
        groupid: numberOrNull("#legacy-filter-preview-groupid"),
        group_name: element<HTMLInputElement>("#legacy-filter-preview-group-name").value || null,
        rules,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    const preview = await response.json() as LegacyMikanFilterPreview;
    const summary = document.createElement("strong");
    summary.textContent =
      `${preview.accepted ? "接受" : "拒绝"} · ${preview.reason}`
      + ` · 字幕组名 ${preview.derived_group_name || "（空）"}`
      + (preview.matched_scope ? ` · 最后命中 ${preview.matched_scope}/${preview.matched_key ?? ""}` : "");
    result.replaceChildren(summary, ...preview.steps.map((step) => {
      const row = document.createElement("div");
      row.className = `legacy-filter-trace ${
        step.accepted === true ? "accepted" : step.accepted === false ? "rejected" : ""}`;
      row.textContent =
        `${step.tier}${step.key === null ? "" : ` / ${step.key}`}`
        + ` · ${step.applicable ? (step.accepted ? "通过" : "拒绝") : "未执行"}`
        + ` · ${step.reason}`
        + (step.whitelist_matches.length > 0 ? ` · 白名单命中 ${JSON.stringify(step.whitelist_matches)}` : "")
        + (step.blacklist_matches.length > 0 ? ` · 黑名单命中 ${JSON.stringify(step.blacklist_matches)}` : "");
      return row;
    }));
  } catch (error) {
    result.textContent = `预览失败：${errorMessage(error, "未知错误")}`;
  }
}

async function updateLegacyFilterSwitch(): Promise<void> {
  const profile = sourceProfiles.find((item) => item.id === "mikan");
  const toggle = element<HTMLInputElement>("#legacy-filter-enabled");
  if (!profile) return;
  toggle.disabled = true;
  const status = element<HTMLElement>("#legacy-filter-status");
  status.textContent = "正在更新 Mikan 来源总开关…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/sources/mikan", {
      method: "PUT",
      headers: requestHeaders,
      body: JSON.stringify({
        display_name: profile.display_name,
        downloader_id: profile.downloader_id,
        file_strategy: profile.file_strategy,
        category: profile.category,
        tags: profile.tags,
        seeding_time_minutes: profile.seeding_time_minutes,
        allowed_torrent_hosts: profile.allowed_torrent_hosts,
        rss_filter_enabled: toggle.checked,
        rss_priority_enabled: profile.rss_priority_enabled,
        enabled: profile.enabled,
        expected_revision: profile.revision,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    const saved = await response.json() as SourceProfile;
    const index = sourceProfiles.findIndex((item) => item.id === saved.id);
    if (index >= 0) sourceProfiles[index] = saved;
    renderLegacyMikanFilter();
    void loadRssRules();
  } catch (error) {
    toggle.checked = profile.rss_filter_enabled;
    status.textContent =
      `总开关更新失败：${errorMessage(error, "未知错误")}；请重新载入来源。`;
  } finally {
    toggle.disabled = false;
  }
}

element<HTMLButtonElement>("#rss-reload").addEventListener("click", () => void loadRssRules());
element<HTMLSelectElement>("#library-sort").value = libraryState.sort;
element<HTMLSelectElement>("#library-direction").value = libraryState.direction;
element<HTMLSelectElement>("#library-page-size").value = String(libraryState.page_size);
element<HTMLSelectElement>("#library-episode-filter").value = libraryState.episode_filter;
element<HTMLInputElement>("#metadata-search").value = metadataState.search;
element<HTMLSelectElement>("#metadata-status-filter").value = metadataState.status;
element<HTMLSelectElement>("#metadata-handling-filter").value = metadataState.handling;
element<HTMLInputElement>("#metadata-failure-stage").value = metadataState.failure_stage;
element<HTMLInputElement>("#metadata-error-code").value = metadataState.error_code;
element<HTMLSelectElement>("#metadata-retryability-filter").value =
  metadataState.retryability;
element<HTMLSelectElement>("#metadata-sort").value = metadataState.sort;
element<HTMLSelectElement>("#metadata-direction").value = metadataState.direction;
element<HTMLSelectElement>("#metadata-page-size").value = String(metadataState.page_size);
element<HTMLFormElement>("#metadata-filters").addEventListener("submit", (event) => {
  event.preventDefault();
  metadataState.search = element<HTMLInputElement>("#metadata-search").value.trim();
  metadataState.status = element<HTMLSelectElement>("#metadata-status-filter").value;
  metadataState.handling =
    element<HTMLSelectElement>("#metadata-handling-filter").value;
  metadataState.failure_stage =
    element<HTMLInputElement>("#metadata-failure-stage").value.trim().toLowerCase();
  metadataState.error_code =
    element<HTMLInputElement>("#metadata-error-code").value.trim().toLowerCase();
  metadataState.retryability =
    element<HTMLSelectElement>("#metadata-retryability-filter").value;
  metadataState.sort =
    element<HTMLSelectElement>("#metadata-sort").value as MetadataUiState["sort"];
  metadataState.direction =
    element<HTMLSelectElement>("#metadata-direction").value as "asc" | "desc";
  metadataState.page_size = Number(
    element<HTMLSelectElement>("#metadata-page-size").value,
  ) as 10 | 25 | 50;
  metadataState.page = 1;
  saveMetadataState();
  void loadMetadataTasks();
});
element<HTMLButtonElement>("#metadata-filter-reset").addEventListener("click", () => {
  metadataState = {
    page: 1,
    page_size: 25,
    search: "",
    status: "",
    handling: "all",
    failure_stage: "",
    error_code: "",
    retryability: "all",
    sort: "updated",
    direction: "desc",
  };
  element<HTMLInputElement>("#metadata-search").value = "";
  element<HTMLSelectElement>("#metadata-status-filter").value = "";
  element<HTMLSelectElement>("#metadata-handling-filter").value = "all";
  element<HTMLInputElement>("#metadata-failure-stage").value = "";
  element<HTMLInputElement>("#metadata-error-code").value = "";
  element<HTMLSelectElement>("#metadata-retryability-filter").value = "all";
  element<HTMLSelectElement>("#metadata-sort").value = "updated";
  element<HTMLSelectElement>("#metadata-direction").value = "desc";
  element<HTMLSelectElement>("#metadata-page-size").value = "25";
  saveMetadataState();
  void loadMetadataTasks();
});
element<HTMLButtonElement>("#metadata-previous").addEventListener("click", () => {
  if (metadataState.page <= 1) return;
  metadataState.page--;
  saveMetadataState();
  void loadMetadataTasks();
});
element<HTMLButtonElement>("#metadata-next").addEventListener("click", () => {
  metadataState.page++;
  saveMetadataState();
  void loadMetadataTasks();
});
element<HTMLInputElement>("#download-search").value = downloadState.search;
element<HTMLSelectElement>("#download-state").value = downloadState.state;
element<HTMLSelectElement>("#download-business-status").value =
  downloadState.business_status;
element<HTMLInputElement>("#download-downloader").value = downloadState.downloader_id;
element<HTMLInputElement>("#download-source").value = downloadState.source;
element<HTMLSelectElement>("#download-page-size").value = String(downloadState.page_size);
element<HTMLFormElement>("#download-filters").addEventListener("submit", (event) => {
  event.preventDefault();
  downloadState.search = element<HTMLInputElement>("#download-search").value.trim();
  downloadState.state = element<HTMLSelectElement>("#download-state").value;
  downloadState.business_status =
    element<HTMLSelectElement>("#download-business-status").value;
  downloadState.downloader_id =
    element<HTMLInputElement>("#download-downloader").value.trim().toLowerCase();
  downloadState.source =
    element<HTMLInputElement>("#download-source").value.trim().toLowerCase();
  downloadState.page_size = Number(
    element<HTMLSelectElement>("#download-page-size").value,
  ) as 10 | 25 | 50;
  downloadState.page = 1;
  saveDownloadState();
  void loadDownloads();
});
element<HTMLButtonElement>("#download-filter-reset").addEventListener("click", () => {
  downloadState = {
    page: 1,
    page_size: 25,
    search: "",
    state: "",
    business_status: "",
    downloader_id: "",
    source: "",
  };
  element<HTMLInputElement>("#download-search").value = "";
  element<HTMLSelectElement>("#download-state").value = "";
  element<HTMLSelectElement>("#download-business-status").value = "";
  element<HTMLInputElement>("#download-downloader").value = "";
  element<HTMLInputElement>("#download-source").value = "";
  element<HTMLSelectElement>("#download-page-size").value = "25";
  saveDownloadState();
  void loadDownloads();
});
element<HTMLButtonElement>("#download-previous").addEventListener("click", () => {
  if (downloadState.page <= 1) return;
  downloadState.page--;
  saveDownloadState();
  void loadDownloads();
});
element<HTMLButtonElement>("#download-next").addEventListener("click", () => {
  downloadState.page++;
  saveDownloadState();
  void loadDownloads();
});
element<HTMLButtonElement>("#library-reload").addEventListener("click", () => void loadLibrary());
element<HTMLFormElement>("#library-create-form").addEventListener(
  "submit",
  (event) => void createLibrarySeason(event),
);
element<HTMLSelectElement>("#library-sort").addEventListener("change", changeLibraryOrdering);
element<HTMLSelectElement>("#library-direction").addEventListener("change", changeLibraryOrdering);
element<HTMLSelectElement>("#library-page-size").addEventListener("change", changeLibraryOrdering);
element<HTMLButtonElement>("#library-previous").addEventListener("click", () => {
  if (libraryState.page <= 1) return;
  libraryState.page--;
  closeLibraryDetail();
  saveLibraryState();
  void loadLibrary();
});
element<HTMLButtonElement>("#library-next").addEventListener("click", () => {
  libraryState.page++;
  closeLibraryDetail();
  saveLibraryState();
  void loadLibrary();
});
element<HTMLButtonElement>("#library-detail-close").addEventListener("click", () => {
  const activeCard = document.querySelector<HTMLButtonElement>(".library-card.active");
  closeLibraryDetail();
  activeCard?.focus();
});
element<HTMLButtonElement>("#library-detail-refresh").addEventListener(
  "click",
  () => void refreshLibrarySeason(),
);
element<HTMLButtonElement>("#library-detail-delete").addEventListener(
  "click",
  () => void deleteLibrarySeason(),
);
element<HTMLSelectElement>("#library-episode-filter").addEventListener("change", () => {
  libraryState.episode_filter = element<HTMLSelectElement>("#library-episode-filter")
    .value as AnimeEpisodeFilter;
  saveLibraryState();
  if (activeLibraryDetail) renderLibraryEpisodes(activeLibraryDetail);
});
element<HTMLButtonElement>("#trusted-offsets-reload").addEventListener(
  "click",
  () => void loadTrustedOffsets(),
);
element<HTMLButtonElement>("#pending-tmdb-reload").addEventListener(
  "click",
  () => void loadPendingTmdb(true),
);
element<HTMLButtonElement>("#configuration-reload").addEventListener("click", () => void loadConfiguration());
element<HTMLButtonElement>("#configuration-edit").addEventListener("click", openConfigurationEditor);
element<HTMLButtonElement>("#configuration-reset").addEventListener("click", () => void resetConfiguration());
element<HTMLButtonElement>("#configuration-close").addEventListener("click", () => configurationDialog.close());
element<HTMLFormElement>("#configuration-form").addEventListener(
  "submit",
  (event) => void previewConfiguration(event),
);
element<HTMLButtonElement>("#configuration-confirm").addEventListener(
  "click",
  () => void confirmConfiguration(),
);
element<HTMLFormElement>("#configuration-form").addEventListener("input", () => {
  const preview = element<HTMLElement>("#configuration-preview");
  if (pendingConfigurationRequest || !preview.hidden) {
    clearConfigurationPreview("配置已修改，请重新预览差异。");
  }
});
configurationDialog.addEventListener("close", () => {
  clearConfigurationPreview();
  element<HTMLInputElement>("#configuration-tmdb-key").value = "";
  element<HTMLInputElement>("#configuration-tmdb-token").value = "";
});
element<HTMLInputElement>("#configuration-tmdb-key-clear").addEventListener(
  "change",
  syncConfigurationSecretInputs,
);
element<HTMLInputElement>("#configuration-tmdb-token-clear").addEventListener(
  "change",
  syncConfigurationSecretInputs,
);
element<HTMLButtonElement>("#rss-save").addEventListener("click", () => void saveRssRules());
element<HTMLButtonElement>("#rss-rule-rollback").addEventListener(
  "click",
  () => void rollbackRssRules(),
);
element<HTMLButtonElement>("#rss-add-whitelist").addEventListener("click", () => {
  activeRssRules?.whitelist.push({ id: nextRuleId("whitelist"), name: "新白名单", enabled: true, values: [] });
  renderRssRules();
});
element<HTMLButtonElement>("#rss-add-blacklist").addEventListener("click", () => {
  activeRssRules?.blacklist.push({ id: nextRuleId("blacklist"), name: "新黑名单", enabled: true, values: [] });
  renderRssRules();
});
element<HTMLButtonElement>("#rss-add-group").addEventListener("click", () => {
  activeRssRules?.priority_groups.push({ id: nextRuleId("group"), name: "新优先级组", arrays: [] });
  renderRssRules();
});
element<HTMLButtonElement>("#rss-preview-run").addEventListener("click", () => void previewRssRules());
element<HTMLButtonElement>("#legacy-filter-reload").addEventListener(
  "click",
  () => void loadLegacyMikanFilter(),
);
element<HTMLButtonElement>("#legacy-filter-save").addEventListener(
  "click",
  () => void saveLegacyMikanFilter(),
);
element<HTMLButtonElement>("#legacy-filter-export").addEventListener("click", () => {
  if (activeLegacyMikanFilter) {
    element<HTMLTextAreaElement>("#legacy-filter-json").value =
      activeLegacyMikanFilter.legacy_json;
  }
});
element<HTMLButtonElement>("#legacy-filter-import").addEventListener(
  "click",
  () => void importLegacyMikanFilter(),
);
element<HTMLButtonElement>("#legacy-filter-rollback").addEventListener(
  "click",
  () => void rollbackLegacyMikanFilter(),
);
element<HTMLButtonElement>("#legacy-filter-preview-run").addEventListener(
  "click",
  () => void previewLegacyMikanFilter(),
);
element<HTMLInputElement>("#legacy-filter-enabled").addEventListener(
  "change",
  () => void updateLegacyFilterSwitch(),
);
for (const addButton of document.querySelectorAll<HTMLButtonElement>("[data-legacy-add-tier]")) {
  addButton.addEventListener("click", () => {
    addLegacyMikanRule(Number(addButton.dataset.legacyAddTier));
  });
}
element<HTMLButtonElement>("#source-new").addEventListener("click", () => populateSourceForm(null));
element<HTMLFormElement>("#source-form").addEventListener("submit", (event) => void saveSource(event));
element<HTMLSelectElement>("#source-adapter").addEventListener(
  "change",
  updateSourceCredentialInputs,
);
element<HTMLInputElement>("#source-mikan-cookie-clear").addEventListener(
  "change",
  updateSourceCredentialInputs,
);
element<HTMLButtonElement>("#source-delete").addEventListener("click", () => void deleteSource());
element<HTMLSelectElement>("#source-strategy").addEventListener("change", updateSourceWarning);
element<HTMLButtonElement>("#route-preview-run").addEventListener("click", () => void previewSourceRoute());
element<HTMLSelectElement>("#manual-download-source").addEventListener(
  "change",
  updateManualDownloadHint,
);
element<HTMLFormElement>("#manual-download-form").addEventListener(
  "submit",
  (event) => void submitManualDownload(event),
);
element<HTMLFormElement>("#manual-rss-form").addEventListener(
  "submit",
  (event) => void submitManualRss(event),
);
element<HTMLInputElement>("#mikan-work-rule-id").addEventListener(
  "input",
  invalidateMikanWorkRule,
);
element<HTMLButtonElement>("#mikan-work-rule-load").addEventListener(
  "click",
  () => void loadMikanWorkRule(),
);
element<HTMLFormElement>("#mikan-work-rule-form").addEventListener(
  "submit",
  (event) => void saveMikanWorkRule(event),
);
element<HTMLButtonElement>("#mikan-work-rule-delete").addEventListener(
  "click",
  () => void deleteMikanWorkRule(),
);
element<HTMLButtonElement>("#mikan-work-rule-rematch").addEventListener(
  "click",
  () => void rematchMikanWorkTasks(),
);
element<HTMLButtonElement>("#downloader-reload").addEventListener("click", () => void loadDownloaders());
element<HTMLButtonElement>("#downloader-new").addEventListener("click", () => openDownloaderConfig(null));
element<HTMLButtonElement>("#downloader-config-close").addEventListener("click", () => downloaderConfigDialog.close());
element<HTMLFormElement>("#downloader-config-form").addEventListener("submit", (event) => void saveDownloaderConfig(event));
element<HTMLButtonElement>("#downloader-config-delete").addEventListener("click", () => void deleteDownloaderOverride());
element<HTMLButtonElement>("#directory-database-refresh").addEventListener(
  "click",
  () => void loadDirectoryDatabase(true),
);
element<HTMLButtonElement>("#data-update-reload").addEventListener(
  "click",
  () => void loadDataUpdate(),
);
element<HTMLButtonElement>("#data-update-check").addEventListener(
  "click",
  () => void runDataUpdateAction(
    "/api/v1/data-update/check",
    "正在检查 manifest…",
  ),
);
element<HTMLButtonElement>("#data-update-download").addEventListener(
  "click",
  () => void runDataUpdateAction(
    "/api/v1/data-update/download",
    "正在下载并校验数据包…",
  ),
);
element<HTMLButtonElement>("#data-update-apply").addEventListener(
  "click",
  () => void runDataUpdateAction(
    "/api/v1/data-update/update",
    "正在下载、校验并导入数据包…",
  ),
);
element<HTMLButtonElement>("#data-update-rollback").addEventListener(
  "click",
  () => void runDataUpdateAction(
    "/api/v1/data-update/rollback",
    "正在回滚上一可用版本…",
    "确认把上一可用数据版本切换为 active？当前版本仍会保留，可再次回滚。",
  ),
);
element<HTMLInputElement>("#data-update-offline-package").addEventListener(
  "change",
  () => {
    element<HTMLButtonElement>("#data-update-offline-import").disabled =
      dataUpdateActionRunning
      || element<HTMLInputElement>("#data-update-offline-package").files?.length !== 1;
  },
);
element<HTMLFormElement>("#data-update-offline-form").addEventListener(
  "submit",
  (event) => void importOfflineDataPackage(event),
);
element<HTMLSelectElement>("#live-log-level").addEventListener(
  "change",
  renderLiveLogs,
);
element<HTMLButtonElement>("#live-log-reconnect").addEventListener(
  "click",
  () => connectLiveLogs(true),
);
element<HTMLButtonElement>("#live-log-pause").addEventListener(
  "click",
  toggleLiveLogPause,
);
element<HTMLButtonElement>("#live-log-clear").addEventListener("click", () => {
  liveLogEntries = [];
  renderLiveLogs();
});
window.addEventListener("beforeunload", () => {
  liveLogShouldReconnect = false;
  if (liveLogReconnectTimer !== null) {
    window.clearTimeout(liveLogReconnectTimer);
    liveLogReconnectTimer = null;
  }
  disconnectCurrentLiveLogSocket();
});

void loadStatus();
void loadDirectoryDatabase();
void loadDataUpdate();
connectLiveLogs();
void loadLibrary();
void loadConfiguration();
void loadDownloads();
void loadMetadataTasks();
void loadPendingTmdb();
void loadDownloaders();
void loadSources();
void loadRssRules();
void loadLegacyMikanFilter();
void loadTrustedOffsets();
window.setInterval(() => void loadDownloads(), 5000);
window.setInterval(() => void loadMetadataTasks(), 5000);
window.setInterval(() => void loadPendingTmdb(), 10000);
window.setInterval(() => {
  if (!document.hidden) void loadDataUpdate(true);
}, 3000);
window.setInterval(() => {
  if (!document.hidden && activeLibraryDetail === null) void loadLibrary();
}, 15000);
