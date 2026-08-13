import { ApiClient } from "./api-client.js";
import {
  renderRegionContent,
  renderRegionMessage,
  setRegionState,
  shouldReplacePolledRegion,
} from "./ui-state.js";
import {
  classifyLiveLogEntry,
  classifyLiveLogHttpDirection,
  filterLiveLogEntries,
  parseLiveLogEntry,
  type LiveLogFilter,
  type LiveLogHttpScope,
  type LiveLogDomain,
  type LiveLogLevel,
  type ParsedLiveLogEntry,
} from "./log-view.js";

interface RuntimeStatus {
  database_schema_version: number;
  native_aot: boolean;
  runtime_identifier: string;
  paths: { data_path: string };
  capabilities: Record<string, boolean>;
  external_plugins: ExternalPluginStatus;
}

interface ExternalPluginStatus {
  packages: ExternalPluginPackage[];
  errors: ExternalPluginPackageError[];
  runtimes: ExternalPluginRuntime[];
}

interface ExternalPluginPackage {
  id: string;
  name: string;
  version: string;
  type: "source" | "feed" | "parser" | "filter" | "rename" | "schedule";
  rid: string;
  capabilities: string[];
  configured: boolean;
  enabled: boolean;
  entry_revision: number;
}

interface ExternalPluginSchema {
  type?: "object" | "array" | "string" | "integer" | "number" | "boolean" | "null";
  title?: string;
  description?: string;
  properties?: Record<string, ExternalPluginSchema>;
  required?: string[];
  enum?: unknown[];
  default?: unknown;
  writeOnly?: boolean;
}

interface ExternalPluginConfiguration {
  id: string;
  name: string;
  version: string;
  type: ExternalPluginPackage["type"];
  rid: string;
  capabilities: string[];
  configured: boolean;
  enabled: boolean;
  entry_revision: number;
  updated_at_utc: string | null;
  args: Record<string, unknown>;
  vars: Record<string, unknown>;
  configured_write_only_paths: string[];
  schema: ExternalPluginSchema;
}

interface ExternalPluginConfigurationList {
  revision: number;
  items: ExternalPluginConfiguration[];
}

interface ExternalPluginPackageError {
  package_directory_name: string;
  code: string;
  message: string;
}

interface ExternalPluginRuntime {
  id: string;
  state: "stopped" | "starting" | "ready" | "backoff" | "auto_disabled" | "unknown";
  consecutive_failures: number;
  retry_at_utc: string | null;
  last_failure_code: string | null;
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
  archive_usage: {
    total_hits: number;
    subject_hits: number;
    episode_hits: number;
    relation_hits: number;
    last_hit_at_utc: string | null;
  };
}

interface DataUpdateActionResult {
  run_id: string;
  status: string;
  data_version: string | null;
  active_version: string | null;
  downloaded: boolean;
  imported: boolean;
}

interface BangumiArchiveUsageEvent {
  id: number;
  data_version: string;
  hit_kind: "subject" | "episodes" | "relations";
  subject_id: number;
  result_count: number;
  hit_at_utc: string;
}

interface BangumiArchiveUsagePage {
  page: number;
  page_size: number;
  total_items: number;
  hit_kind: "subject" | "episodes" | "relations" | null;
  items: BangumiArchiveUsageEvent[];
}

interface ConfigurationArchiveCounts {
  application: number;
  downloaders: number;
  external_plugins: number;
  sources: number;
  rss_rule_sets: number;
  legacy_mikan_filters: number;
  mikan_work_rules: number;
}

interface ConfigurationArchivePreview {
  sha256: string;
  exported_at_utc: string;
  counts: ConfigurationArchiveCounts;
  warnings: string[];
}

interface ConfigurationArchiveBackup {
  id: string;
  kind: "manual" | "pre-import" | "pre-restore";
  created_at_utc: string;
  size_bytes: number;
  sha256: string;
}

interface ConfigurationArchiveApplyResult {
  backup_id: string;
  sha256: string;
  counts: ConfigurationArchiveCounts;
  restart_required: boolean;
}

type CacheDatabase = "bolt" | "bolt_sub";

interface CacheBrowserBucket {
  bucket_id: string;
  bucket_name: string;
  entry_count: number;
}

interface CacheBrowserBucketList {
  database: CacheDatabase;
  read_only: boolean;
  items: CacheBrowserBucket[];
}

interface CacheBrowserEntry {
  entry_id: string;
  key: string;
  delete_token: string;
  value_bytes: number;
  expires_at_utc: string | null;
  updated_at_utc: string;
}

interface CacheBrowserEntryList {
  database: CacheDatabase;
  read_only: boolean;
  bucket_id: string;
  page: number;
  page_size: number;
  total_count: number;
  items: CacheBrowserEntry[];
}

interface CacheBrowserEntryDetail {
  database: CacheDatabase;
  read_only: boolean;
  bucket_id: string;
  bucket_name: string;
  entry_id: string;
  key: string;
  value_json: string;
  value_bytes: number;
  expires_at_utc: string | null;
  updated_at_utc: string;
}

interface CacheBrowserDeleteResponse {
  database: CacheDatabase;
  bucket_id: string;
  entry_id: string;
  deleted: boolean;
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
  outbound_proxy: {
    url: string | null;
    hosts: string[];
  };
  metadata: {
    mikan: {
      base_url: string;
      episode_identity_cache_hours: number;
      bangumi_identity_cache_hours: number;
    };
    tmdb: {
      base_url: string;
      image_base_url: string;
      language: string;
      http_timeout_seconds: number;
      retry_count: number;
      retry_delay_seconds: number;
      cache_hours: number;
      api_key_configured: boolean;
      read_access_token_configured: boolean;
    };
    bangumi: {
      base_url: string;
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
      provider: string;
      base_url: string | null;
      model: string | null;
      reasoning_effort: "none" | "low" | "medium" | "high";
      prompt_version: string;
      prompt_customized: boolean;
      api_key_configured: boolean;
      use_metadata_match: boolean;
      use_season_match: boolean;
      use_episode_match: boolean;
      debug_mode: boolean;
      http_timeout_seconds: number;
      retry_count: number;
      use_bangumi_pubdate_first: boolean;
      tmdb_mcp_url: string;
      bangumi_mcp_url: string;
    };
    tmdb_failure_use_bangumi: boolean;
    write_bangumi_id_when_tmdb_matched: boolean;
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
    outbound_proxy_url: string | null;
    outbound_proxy_hosts: string[];
    mikan_base_url: string;
    mikan_episode_identity_cache_hours: number;
    mikan_bangumi_identity_cache_hours: number;
    tmdb_base_url: string;
    tmdb_image_base_url: string;
    tmdb_language: string;
    tmdb_http_timeout_seconds: number;
    tmdb_retry_count: number;
    tmdb_retry_delay_seconds: number;
    tmdb_cache_hours: number;
    tmdb_api_key_state: "inherit" | "configured" | "cleared";
    tmdb_api_key: string | null;
    tmdb_read_access_token_state: "inherit" | "configured" | "cleared";
    tmdb_read_access_token: string | null;
    bangumi_base_url: string;
    bangumi_http_timeout_seconds: number;
    bangumi_retry_count: number;
    bangumi_retry_delay_seconds: number;
    season_failure_skip: boolean;
    season_failure_backtrace: boolean;
    season_failure_use_title_season: boolean;
    season_failure_use_first_season: boolean;
    ai_base_url: string | null;
    ai_model: string | null;
    ai_reasoning_effort: "none" | "low" | "medium" | "high";
    ai_prompt_template: string;
    ai_api_key_state: "inherit" | "configured" | "cleared";
    ai_api_key: string | null;
    ai_tmdb_mcp_url: string;
    ai_bangumi_mcp_url: string;
    ai_use_metadata_match: boolean;
    ai_use_season_match: boolean;
    ai_use_episode_match: boolean;
    ai_debug_mode: boolean;
    ai_http_timeout_seconds: number;
    tmdb_failure_use_bangumi: boolean;
    write_bangumi_id_when_tmdb_matched: boolean;
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
      source: "environment" | "command_line" | "environment_and_command_line";
      environment_variables: string[];
      command_line_arguments: string[];
      controlling_keys: string[];
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
  outbound_proxy_url: string | null;
  outbound_proxy_hosts: string[];
  mikan_base_url: string;
  mikan_episode_identity_cache_hours: number;
  mikan_bangumi_identity_cache_hours: number;
  tmdb_base_url: string;
  tmdb_image_base_url: string;
  tmdb_language: string;
  tmdb_http_timeout_seconds: number;
  tmdb_retry_count: number;
  tmdb_retry_delay_seconds: number;
  tmdb_cache_hours: number;
  tmdb_api_key: string | null;
  clear_tmdb_api_key: boolean;
  tmdb_read_access_token: string | null;
  clear_tmdb_read_access_token: boolean;
  bangumi_base_url: string;
  bangumi_http_timeout_seconds: number;
  bangumi_retry_count: number;
  bangumi_retry_delay_seconds: number;
  season_failure_skip: boolean;
  season_failure_backtrace: boolean;
  season_failure_use_title_season: boolean;
  season_failure_use_first_season: boolean;
  ai_base_url: string | null;
  ai_model: string | null;
  ai_reasoning_effort: "none" | "low" | "medium" | "high";
  ai_prompt_template: string;
  ai_api_key: string | null;
  clear_ai_api_key: boolean;
  ai_tmdb_mcp_url: string;
  ai_bangumi_mcp_url: string;
  ai_use_metadata_match: boolean;
  ai_debug_mode: boolean;
  ai_http_timeout_seconds: number;
  tmdb_failure_use_bangumi: boolean;
  write_bangumi_id_when_tmdb_matched: boolean;
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
  seeding_state: "not_required" | "waiting" | "seeding" | "completed";
  seeding_target_minutes: number;
  seeding_elapsed_seconds: number;
  seeding_completed_at_utc: string | null;
  dynamic_tags: string[];
  dynamic_tag_state: "not_configured" | "pending" | "applied" | "skipped";
  dynamic_tag_failure_code: string | null;
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
    phase: string | null;
    completed_units: number | null;
    total_units: number | null;
    progress: number | null;
  };
  organization: {
    state: string;
    attempt_count: number;
    next_attempt_at_utc: string | null;
    failure_code: string | null;
    phase: string | null;
    completed_units: number | null;
    total_units: number | null;
    progress: number | null;
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
  series_run_id: string | null;
  series_attempt_id: string | null;
  season_run_id: string | null;
  season_attempt_id: string | null;
  episode_run_id: string | null;
  episode_attempt_id: string | null;
  episode_resolution_mixed: boolean;
  failure_kind: string | null;
  failure_reason: string | null;
  failure_stage: string | null;
  failure_code: string | null;
  failure_retryable: boolean | null;
  latest_run_status: string | null;
  tmdb_access_confirmed: boolean | null;
  bangumi_fallback_eligible: boolean | null;
  bangumi_fallback_denial_reason: string | null;
  handling_category: string;
  episode_file_count: number;
  other_file_count: number;
  duplicate_file_count: number;
  pending_file_count: number;
  readaptation_review_state: "not_required" | "pending" | "approved";
}

interface MetadataTaskListPage {
  page: number;
  page_size: number;
  total_items: number;
  sort: "updated" | "title" | "status" | "failure";
  direction: "asc" | "desc";
  items: MetadataItem[];
}

interface OtherFileReadaptationPreview {
  task_id: string;
  title: string;
  eligible: boolean;
  reason: string | null;
  files: Array<{
    task_file_id: string;
    source_name: string;
    size_bytes: number;
    other_reason: string;
    tmdb_series_id: number;
    tmdb_season_number: number;
    source_available: boolean;
    shared_path_reference_count: number;
  }>;
}

interface OtherFileReadaptationReviewPreview {
  task_id: string;
  title: string;
  task_status: string;
  review_state: "not_required" | "pending" | "approved";
  completion_status: "processing" | "awaiting_review" | "review_completed";
  requested_at_utc: string;
  completed_at_utc: string | null;
  reviewed_at_utc: string | null;
  files: Array<{
    task_file_id: string;
    source_name: string;
    before_disposition: string;
    before_other_reason: string;
    before_tmdb_series_id: number | null;
    before_series_name: string | null;
    before_tmdb_season_number: number | null;
    before_season_name: string | null;
    before_tmdb_episode_number: number | null;
    before_episode_name: string | null;
    after_disposition: string;
    after_other_reason: string | null;
    after_tmdb_series_id: number | null;
    after_series_name: string | null;
    after_tmdb_season_number: number | null;
    after_season_name: string | null;
    after_tmdb_episode_number: number | null;
    after_episode_name: string | null;
    after_episode_strategy: string | null;
    preserved_shared_source: boolean;
    before_media_path: string;
    after_media_path: string | null;
  }>;
}

interface ActiveOtherFileReadaptationReview {
  preview: OtherFileReadaptationReviewPreview;
  triggerButton: HTMLButtonElement;
  defaultLabel: string;
}

interface MetadataUiState {
  page: number;
  page_size: 10 | 25 | 50;
  search: string;
  status: string;
  handling: string;
  file_state: string;
  failure_stage: string;
  error_code: string;
  retryability: string;
  sort: "updated" | "title" | "status" | "failure";
  direction: "asc" | "desc";
}

interface MetadataTaskDetail {
  summary: MetadataItem;
  source_evidence: {
    source_profile_id: string;
    source_profile_revision: number;
    source_id: string;
    source_title: string;
    source_item_id_fingerprint: string | null;
    source_work_id_fingerprint: string | null;
    mikanid: number | null;
    groupid: number | null;
    bgmid: number | null;
    anidbid: number | null;
    imdbid: string | null;
    published_at_raw_available: boolean;
    published_at: string | null;
  };
  rss_evidence: Array<{
    batch_id: string;
    entry_ordinal: number;
    source_profile_id: string;
    rule_revision: number;
    priority_enabled: boolean;
    legacy_filter_revision: number;
    legacy_filter_enabled: boolean;
    mikanid: number | null;
    source_episode_kind: string | null;
    source_episode: string | null;
    decision_kind: string;
    decision_reason: string;
    evaluated_priority_groups: string[];
    legacy_filter_state: string;
    legacy_filter_reason: string;
    legacy_filter_scope: string | null;
    identity_mikanid: number | null;
    identity_groupid: number | null;
    effect_state: string;
    batch_created_at_utc: string;
  }>;
  ai: {
    status: string;
    stage: string | null;
    error_code: string | null;
    reason: string | null;
    confidence_basis: "tmdb_verified" | "not_established";
    duration_ms: number | null;
    attempted_at_utc: string | null;
    model: string | null;
    prompt_tokens: number | null;
    completion_tokens: number | null;
    total_tokens: number | null;
    request_count: number | null;
    tool_call_count: number | null;
  };
  nfo_rewrites: Array<{
    job_id: string;
    bgmid: number;
    tmdb_series_id: number;
    state: "pending" | "writing" | "completed" | "failed";
    attempt_count: number;
    failure_code: string | null;
    next_attempt_at_utc: string | null;
    updated_at_utc: string;
    completed_at_utc: string | null;
  }>;
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
    episode_strategy: string | null;
    episode_run_id: string | null;
    episode_attempt_id: string | null;
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
  ai_model: string | null;
  ai_prompt_tokens: number | null;
  ai_completion_tokens: number | null;
  ai_total_tokens: number | null;
  ai_request_count: number | null;
  ai_tool_call_count: number | null;
}

interface AiInvocationLogItem {
  attempt_id: string;
  run_id: string;
  task_id: string;
  title: string;
  source_id: string;
  mikanid: number | null;
  bgmid: number | null;
  tmdb_series_id: number | null;
  tmdb_season_number: number | null;
  run_status: string;
  stage: string;
  strategy: string;
  result: string;
  error_code: string | null;
  reason: string | null;
  retryable: boolean;
  duration_ms: number;
  created_at_utc: string;
  model: string;
  prompt_tokens: number | null;
  completion_tokens: number | null;
  total_tokens: number | null;
  request_count: number;
  tool_call_count: number;
  debug_available: boolean;
}

interface AiDebugExchange {
  sequence: number;
  channel: string;
  operation: string;
  endpoint: string;
  request_body: string | null;
  status_code: number | null;
  response_body: string | null;
  duration_milliseconds: number;
  error: string | null;
}

interface AiDebugPreAiAttempt {
  attempt_id: string;
  stage: string;
  strategy: string;
  priority: number | null;
  result: string;
  error_code: string | null;
  reason: string | null;
  retryable: boolean;
  duration_milliseconds: number;
  created_at_utc: string;
}

interface AiDebugDocument {
  format_version: number;
  chain: {
    trace_id: string;
    run_id: string | null;
    task_id: string | null;
    started_at_utc: string;
    completed_at_utc: string;
    prompt_version: string;
    api_mode: string;
    model: string;
    pre_ai_context: null | {
      trigger_stage: string;
      input: Record<string, unknown>;
      expected_tmdb_series_id: number | null;
      expected_season_number: number | null;
      attempted_tmdb_search_titles: string[];
      torrent_published_at: string | null;
      bangumi_episode_candidate: number | null;
      use_bangumi_pub_date_first: boolean;
      publication_result: string;
      publication_error_code: string | null;
      attempts: AiDebugPreAiAttempt[];
    };
    prompt_template: string;
    rendered_prompt: string;
    exchanges: AiDebugExchange[];
    raw_output: string | null;
    candidate: unknown;
    usage: unknown;
    failure_code: string | null;
  };
  validation: unknown;
}

interface AiInvocationLogList {
  page: number;
  page_size: number;
  total_items: number;
  summary: {
    matched_items: number;
    failed_items: number;
    prompt_tokens: number;
    completion_tokens: number;
    total_tokens: number;
    request_count: number;
    tool_call_count: number;
  };
  items: AiInvocationLogItem[];
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
  series_resolution_run_id: string | null;
  series_resolution_attempt_id: string | null;
  season_resolution_source: string | null;
  season_resolution_run_id: string | null;
  season_resolution_attempt_id: string | null;
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
  series_resolution_run_id: string | null;
  series_resolution_attempt_id: string | null;
  season_resolution_source: string | null;
  season_resolution_run_id: string | null;
  season_resolution_attempt_id: string | null;
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

interface ExternalMediaImportItem {
  tmdb_series_id: number;
  tmdb_season_number: number;
  tmdb_episode_number: number | null;
  relative_path: string;
  status: "imported" | "already_recorded" | "skipped";
  reason_code: string | null;
}

interface ExternalMediaImportResult {
  scanned_season_count: number;
  candidate_file_count: number;
  imported_count: number;
  already_recorded_count: number;
  skipped_count: number;
  items: ExternalMediaImportItem[];
}

interface AnimeLibraryUiState {
  search: string;
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
  | "delete_media_files"
  | "delete_task_record";
type DeleteCollection = "business_records" | "downloader_tasks" | "source_files" | "media_files" | "task_records";

interface DeletePreview {
  task_id: string;
  title: string;
  task_status: string;
  fingerprint: string;
  business_records: DeleteTarget[];
  downloader_tasks: DeleteTarget[];
  source_files: DeleteTarget[];
  media_files: DeleteTarget[];
  task_records: DeleteTarget[];
  task_record_deletion_allowed: boolean;
  task_record_deletion_denial_reason: string | null;
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
  identity_mikanid: number | null;
  identity_groupid: number | null;
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
  batches?: ManualRssResponse[] | null;
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
  adapter: string;
  downloader_id: string;
  file_strategy: "link" | "link_delete" | "move" | "wait_move";
  allowed_torrent_hosts: string[];
  category: string;
  tags: string[];
  dynamic_tag_template: string | null;
  seeding_time_minutes: number;
  rss_filter_enabled: boolean;
  rss_priority_enabled: boolean;
  duplicate_notification_enabled: boolean;
  enabled: boolean;
  locked_fields: Array<{
    field: "category" | "dynamic_tag_template" | "mikan_identity_cookie";
    source: "environment" | "command_line" | "environment_and_command_line";
    controlling_keys: string[];
  }>;
  mikan_identity_cookie_configured: boolean;
  mikan_identity_cookie: string | null;
  rss_feed_url_configured: boolean;
  rss_feed_url: string | null;
  rss_schedule_enabled: boolean;
  rss_schedule_cron: string;
  rss_schedule_registered: boolean;
  rss_schedule_next_at_utc: string | null;
  rss_last_run_state: "never" | "running" | "succeeded" | "failed";
  rss_last_started_at_utc: string | null;
  rss_last_completed_at_utc: string | null;
  rss_last_failure_code: string | null;
  rss_last_batch_id: string | null;
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
  dynamic_tag_template: string | null;
  seeding_time_minutes: number;
  rss_filter_enabled: boolean;
  rss_priority_enabled: boolean;
  duplicate_notification_enabled: boolean;
  rss_rule_revision: number | null;
}

interface DownloaderInstance {
  id: string;
  type: string;
  base_url: string;
  download_path: string;
  enabled: boolean;
  credentials_configured: boolean;
  username: string | null;
  password: string | null;
  configuration_source: string;
  locked_fields: Array<{
    field: string;
    source: "environment" | "command_line" | "environment_and_command_line";
    controlling_keys: string[];
  }>;
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
const api = new ApiClient(accessKey);
const headers = new Headers();
if (accessKey) headers.set("Access-Key", accessKey);
const deleteDialog = element<HTMLDialogElement>("#delete-dialog");
const deleteConfirm = element<HTMLButtonElement>("#delete-confirm");
const downloaderConfigDialog = element<HTMLDialogElement>("#downloader-config-dialog");
const configurationDialog = element<HTMLDialogElement>("#configuration-dialog");
const cacheEntryDialog = element<HTMLDialogElement>("#cache-entry-dialog");
const aiDebugDialog = element<HTMLDialogElement>("#ai-debug-dialog");
const otherReadaptationReviewDialog = element<HTMLDialogElement>("#other-readaptation-review-dialog");
const otherReadaptationReviewConfirm = element<HTMLButtonElement>("#other-readaptation-review-confirm");
const otherReadaptationReviewCancel = element<HTMLButtonElement>("#other-readaptation-review-cancel");
let activeDeletePreview: DeletePreview | null = null;
let activeOtherReadaptationReview: ActiveOtherFileReadaptationReview | null = null;
let otherReadaptationReviewSubmitting = false;
let currentConfiguration: RuntimeConfiguration | null = null;
let pendingConfigurationArchive: File | null = null;
let pendingConfigurationArchivePreview: ConfigurationArchivePreview | null = null;
let activeRssRules: RssRuleSnapshot | null = null;
let activeLegacyMikanFilter: LegacyMikanFilterResponse | null = null;
let sourceProfiles: SourceProfile[] = [];
let activeSourceId: string | null = null;
let externalSourceAdapters: ExternalPluginConfiguration[] = [];
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
let metadataListRequestSequence = 0;
let metadataRenderSignature: string | null = null;
let downloadListRequestSequence = 0;
let downloadRenderSignature: string | null = null;
const expandedDownloadJobIds = new Set<string>();
let activeLibraryDetail: AnimeSeasonDetail | null = null;
let externalImportDetailIdentity: string | null = null;
let libraryListRequestSequence = 0;
let libraryDetailRequestSequence = 0;
let activeMikanWorkRule: MikanWorkRule | null = null;
let loadedMikanWorkId: number | null = null;
let activeMikanWorkImpact: MikanWorkImpact | null = null;
let activeConfigurationLockedFields = new Set<string>();
let pendingConfigurationRequest: ConfigurationUpdatePayload | null = null;
let cacheDatabase: CacheDatabase = "bolt";
let cacheBuckets: CacheBrowserBucket[] = [];
let activeCacheBucketId: string | null = null;
let cachePage = 1;
let cacheTotalCount = 0;
let cacheReadOnly = false;
let cacheRequestSequence = 0;
const cachePageSize = 25;
const maximumRenderedLogs = 500;
let liveLogSocket: WebSocket | null = null;
let liveLogReconnectTimer: number | null = null;
let liveLogReconnectAttempt = 0;
let liveLogShouldReconnect = true;
let liveLogPaused = false;
let liveLogControlPending = false;
let liveLogEntries: ParsedLiveLogEntry[] = [];
let liveLogDomain: LiveLogDomain | "all" = "all";
let aiLogPage = 1;
let aiLogPageSize = 25;
let aiLogTotalItems = 0;
let aiLogRequestSequence = 0;
let activeAiDebugRunId: string | null = null;
let aiTestDefaultPrompt: AiMetadataTestPrompt | null = null;
const aiTestPromptDraftKey = "animegonet.ai-test-prompt.v1";

type WorkspaceId =
  | "overview"
  | "library"
  | "tasks"
  | "mikan"
  | "bangumi-cache"
  | "download-tools"
  | "connections"
  | "tools"
  | "logs"
  | "system";

interface AiTesterUsage {
  input_tokens: number | null;
  output_tokens: number | null;
  reasoning_tokens: number | null;
  total_tokens: number | null;
}

interface AiTesterToolTimelineEntry {
  source: string;
  name: string;
  phase: string;
  elapsed_milliseconds: number;
  success: boolean;
  message: string | null;
  request_content: string | null;
  response_content: string | null;
}

interface AiTesterApiRequestEntry {
  step: number;
  endpoint: string;
  content: string;
}

interface AiTesterProgress {
  type: string;
  step: number;
  message: string;
  usage: AiTesterUsage | null;
  tool: AiTesterToolTimelineEntry | null;
  content: string | null;
  endpoint: string | null;
}

interface AiTesterRunResult {
  success: boolean;
  status_code: number;
  raw_response: string;
  model_json: string | null;
  usage: AiTesterUsage;
  elapsed_milliseconds: number;
  error_message: string | null;
  result_json_valid: boolean;
  result_json_error: string | null;
  request_identity: string | null;
  tool_timeline: AiTesterToolTimelineEntry[] | null;
  pub_date_priority: {
    use_bangumi_pub_date_first: boolean;
    torrent_file_count: number | null;
    bgm_episode_candidate: number | null;
    normalized_pub_date: string | null;
    reason: string;
  } | null;
  rendered_prompt: string;
  ai_api_requests: AiTesterApiRequestEntry[] | null;
  local_episode_offset: unknown;
  file_episode_candidates: Array<{ name: string; file_episode_candidate: number | null }> | null;
  production_validation: unknown;
}

interface AiTesterStreamEnvelope {
  type: "progress" | "result" | "stopped" | "error";
  progress: AiTesterProgress | null;
  result: AiTesterRunResult | null;
  error: string | null;
}

interface AiTesterRunRequest {
  base_url: string;
  api_key: string;
  model: string;
  mode: string;
  reasoning_effort: string;
  web_search_enabled: boolean;
  timeout_seconds: number;
  proxy_url: string;
  prompt_template: string | null;
  title: string;
  files_json: string;
  bgmid: string;
  anidbid: string;
  mikan_pub_date?: string;
  bgm_episode_candidate: string;
  use_bangumi_pubdate_first: boolean;
  torrent_import_id: string;
  is_mikan_rss_source: boolean;
  bgm_mcp_url: string;
  tmdb_mcp_url: string;
  enable_bgm_mcp: boolean;
  enable_tmdb_mcp: boolean;
  enable_anidb_lookup: boolean;
  anidb_mapping_url_template: string;
  run_id: string;
}

interface AiMetadataTestPrompt {
  prompt_version: string;
  template: string;
  maximum_length: number;
  default_template: string;
  customized: boolean;
}

interface AiMetadataTestMikanImport {
  success: boolean;
  title: string | null;
  mikan_id: number | null;
  group_id: number | null;
  bgmid: number | null;
  mikan_pub_date: string | null;
  torrent_url: string | null;
  files: Array<{ name: string; size_bytes: number; file_episode_candidate: number | null }> | null;
  import_id: string | null;
  torrent_file_count: number | null;
  file_episode_candidates: Array<{ name: string; file_episode_candidate: number | null }> | null;
  error_message: string | null;
}

interface AiTesterBootstrap {
  defaults: {
    base_url: string;
    api_key: string;
    model: string;
    mode: number;
    reasoning_effort: string | null;
    web_search_enabled: boolean;
    timeout_seconds: number;
    proxy_url: string | null;
    bgm_mcp_url: string;
    tmdb_mcp_url: string;
    enable_bgm_mcp: boolean;
    enable_tmdb_mcp: boolean;
    enable_anidb_lookup: boolean;
    ani_db_mapping_url_template: string;
    is_mikan_rss_source: boolean;
  };
  prompt_template: string;
}

interface WorkspaceDefinition {
  title: string;
  description: string;
  defaultSubview: string;
  tabs: Array<{ id: string; label: string }>;
}

const workspaceDefinitions: Record<WorkspaceId, WorkspaceDefinition> = {
  overview: {
    title: "总览",
    description: "运行状态、模块能力和常用管理入口。",
    defaultSubview: "status",
    tabs: [
      { id: "status", label: "运行状态" },
      { id: "shortcuts", label: "管理入口" },
    ],
  },
  library: {
    title: "动画库",
    description: "以 TMDB 为准查看作品、季度、EP 进度和待补全项目。",
    defaultSubview: "seasons",
    tabs: [
      { id: "seasons", label: "作品与季度" },
      { id: "pending", label: "待补全 TMDB" },
    ],
  },
  tasks: {
    title: "任务中心",
    description: "查看下载、匹配、整理和失败原因。",
    defaultSubview: "downloads",
    tabs: [
      { id: "downloads", label: "下载任务" },
      { id: "metadata", label: "匹配与整理" },
    ],
  },
  mikan: {
    title: "Mikan 手动设置",
    description: "统一导入、人工覆盖、候选优选和五级过滤。",
    defaultSubview: "ingest",
    tabs: [
      { id: "ingest", label: "导入任务" },
      { id: "manual-rules", label: "人工规则" },
      { id: "offsets", label: "可信 Offset" },
      { id: "candidate-rules", label: "候选规则" },
      { id: "legacy-filter", label: "五级过滤" },
    ],
  },
  "bangumi-cache": {
    title: "Bangumi缓存",
    description: "管理 AnimeGoNetData 离线 Bangumi Subject、Episode 与前传关系档案。",
    defaultSubview: "versions",
    tabs: [
      { id: "versions", label: "数据版本与更新" },
    ],
  },
  "download-tools": {
    title: "下载工具配置",
    description: "管理 qBittorrent 实例、连接验证和跨容器路径映射。",
    defaultSubview: "qbittorrent",
    tabs: [
      { id: "qbittorrent", label: "qBittorrent" },
    ],
  },
  connections: {
    title: "设置与备份",
    description: "管理应用设置、输入源凭据、外部插件和总配置备份。",
    defaultSubview: "application",
    tabs: [
      { id: "application", label: "应用配置" },
      { id: "archive", label: "导入导出与备份" },
      { id: "sources", label: "输入源" },
      { id: "plugins", label: "外部插件" },
    ],
  },
  tools: {
    title: "AI 匹配测试工具",
    description: "以只读方式验证生产 Prompt、AI 工具调用与 TMDB 最终校验。",
    defaultSubview: "ai-metadata",
    tabs: [
      { id: "ai-metadata", label: "AI 元数据测试" },
    ],
  },
  logs: {
    title: "日志",
    description: "筛选运行诊断，并跨任务查询持久化的 AI 调用审计。",
    defaultSubview: "runtime",
    tabs: [
      { id: "runtime", label: "运行日志" },
      { id: "ai-invocations", label: "AI 调用日志" },
    ],
  },
  system: {
    title: "系统缓存",
    description: "维护通用 HTTP 缓存和后台基础设施。",
    defaultSubview: "cache",
    tabs: [
      { id: "cache", label: "缓存管理" },
    ],
  },
};

function isWorkspaceId(value: string): value is WorkspaceId {
  return Object.hasOwn(workspaceDefinitions, value);
}

function workspaceFromHash(): { workspace: WorkspaceId; subview: string } {
  const [rawWorkspace = "", rawSubview = ""] = window.location.hash
    .replace(/^#\/?/, "")
    .split("/", 2);
  const workspace = isWorkspaceId(rawWorkspace) ? rawWorkspace : "overview";
  const definition = workspaceDefinitions[workspace];
  const subview = definition.tabs.some(tab => tab.id === rawSubview)
    ? rawSubview
    : definition.defaultSubview;
  return { workspace, subview };
}

function isSubviewVisible(workspace: WorkspaceId, subview: string): boolean {
  if (document.hidden) return false;
  const current = workspaceFromHash();
  return current.workspace === workspace && current.subview === subview;
}

function hasFocusedEditorWithin(selector: string): boolean {
  const active = document.activeElement;
  return active instanceof HTMLElement
    && active.matches("input, select, textarea, [contenteditable='true']")
    && element<HTMLElement>(selector).contains(active);
}

function hasOpenDialog(): boolean {
  return document.querySelector("dialog[open]") !== null;
}

function closeMobileSidebar(): void {
  const sidebar = element<HTMLElement>("#app-sidebar");
  const toggle = element<HTMLButtonElement>("#sidebar-toggle");
  sidebar.classList.remove("open");
  toggle.setAttribute("aria-expanded", "false");
  toggle.setAttribute("aria-label", "打开菜单");
}

function selectWorkspace(
  workspace: WorkspaceId,
  subview: string,
  updateHash = true,
): void {
  const definition = workspaceDefinitions[workspace];
  const selectedSubview = definition.tabs.some(tab => tab.id === subview)
    ? subview
    : definition.defaultSubview;
  document.querySelectorAll<HTMLElement>("#main-content > section[data-workspace]")
    .forEach(section => {
      section.hidden = section.dataset.workspace !== workspace
        || section.dataset.subview !== selectedSubview;
    });
  document.querySelectorAll<HTMLButtonElement>("[data-workspace-target]")
    .forEach(button => {
      const selected = button.dataset.workspaceTarget === workspace;
      if (selected) button.setAttribute("aria-current", "page");
      else button.removeAttribute("aria-current");
    });
  element<HTMLElement>("#workspace-title").textContent = definition.title;
  element<HTMLElement>("#workspace-description").textContent = definition.description;
  const tabs = element<HTMLElement>("#workspace-tabs");
  tabs.replaceChildren(...definition.tabs.map(tab => {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = tab.label;
    button.dataset.subviewTarget = tab.id;
    if (tab.id === selectedSubview) button.setAttribute("aria-current", "page");
    button.addEventListener("click", () => selectWorkspace(workspace, tab.id));
    return button;
  }));
  document.title = `${definition.title} · AnimeGoNet`;
  if (updateHash) {
    const nextHash = `#/${workspace}/${selectedSubview}`;
    if (window.location.hash !== nextHash) history.pushState(null, "", nextHash);
  }
  if (workspace === "tasks" && selectedSubview === "downloads") {
    void loadDownloads(true);
  }
  if (workspace === "tasks" && selectedSubview === "metadata") {
    void loadMetadataTasks(true);
  }
  if (workspace === "library" && selectedSubview === "seasons"
    && activeLibraryDetail === null) {
    void loadLibrary();
  }
  if (workspace === "library" && selectedSubview === "pending") {
    void loadPendingTmdb();
  }
  if (workspace === "bangumi-cache") void loadBangumiArchiveUsage(true);
  if (workspace === "bangumi-cache" && selectedSubview === "versions"
    && !dataUpdateActionRunning) {
    void loadDataUpdate(true);
  }
  if (workspace === "logs" && selectedSubview === "ai-invocations") void loadAiInvocationLogs();
  closeMobileSidebar();
  window.scrollTo({ top: 0, behavior: "auto" });
}

function initializeWorkspaceNavigation(): void {
  document.querySelectorAll<HTMLButtonElement>("[data-workspace-target]")
    .forEach(button => button.addEventListener("click", () => {
      const requested = button.dataset.workspaceTarget ?? "";
      if (isWorkspaceId(requested)) {
        selectWorkspace(
          requested,
          workspaceDefinitions[requested].defaultSubview,
        );
      }
    }));
  element<HTMLButtonElement>("#sidebar-toggle").addEventListener("click", () => {
    const sidebar = element<HTMLElement>("#app-sidebar");
    const toggle = element<HTMLButtonElement>("#sidebar-toggle");
    const open = sidebar.classList.toggle("open");
    toggle.setAttribute("aria-expanded", open ? "true" : "false");
    toggle.setAttribute("aria-label", open ? "关闭菜单" : "打开菜单");
  });
  window.addEventListener("hashchange", () => {
    const target = workspaceFromHash();
    selectWorkspace(target.workspace, target.subview, false);
  });
  const initial = workspaceFromHash();
  selectWorkspace(initial.workspace, initial.subview, true);
}

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
  already_completed: "同一来源集已完成，已跳过",
};

const rssStatusLabels: Record<string, string> = {
  staged: "已暂存",
  blocked: "规则未选中",
  already_ingested: "批次已导入",
  already_claimed: "正在由另一请求处理",
  already_completed: "同一 mikanid 与来源 EP 已完成，已跳过",
  bgmid_discovery_failed: "Bangumi Subject 获取失败",
  rejected: "导入被拒绝",
};

const deleteGroups: DeleteGroup[] = [
  { flag: "delete_business_record", label: "业务完成记录", collection: "business_records", help: "删除后该 TMDB 单集可重新导入" },
  { flag: "delete_downloader_task", label: "qBittorrent 任务", collection: "downloader_tasks", help: "只删除任务，永不让 qB 删除文件" },
  { flag: "delete_source_files", label: "下载源文件", collection: "source_files", help: "精确删除捕获下载根目录内的文件" },
  { flag: "delete_media_files", label: "媒体库文件", collection: "media_files", help: "精确删除捕获媒体库根目录内的文件" },
  { flag: "delete_task_record", label: "AnimeGoNet 任务记录", collection: "task_records", help: "删除任务及其内部审计链；Other 重新适配必须先完成人工审核" },
];

const externalPluginStateLabels: Record<ExternalPluginRuntime["state"], string> = {
  stopped: "未启动",
  starting: "正在启动",
  ready: "运行中",
  backoff: "故障退避",
  auto_disabled: "已自动禁用",
  unknown: "未知状态",
};

function externalPluginPointer(propertyName: string): string {
  return `/${propertyName.replaceAll("~", "~0").replaceAll("/", "~1")}`;
}

function createExternalPluginVarField(
  propertyName: string,
  schema: ExternalPluginSchema,
  value: unknown,
  required: boolean,
  configuredWriteOnlyPaths: Set<string>,
): HTMLElement {
  const field = document.createElement("label");
  field.className = "external-plugin-field";
  const label = document.createElement("span");
  label.textContent = `${schema.title ?? propertyName}${required ? " *" : ""}`;
  field.append(label);
  const pointer = externalPluginPointer(propertyName);
  let control: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
  if (schema.writeOnly && (schema.type === "string" || schema.type === undefined)) {
    const input = document.createElement("input");
    input.type = "text";
    input.autocomplete = "off";
    input.value = typeof value === "string" ? value : "";
    input.dataset.pluginVarKind = "write-only";
    input.placeholder = "输入敏感值";
    control = input;
  } else if (schema.writeOnly) {
    const textarea = document.createElement("textarea");
    textarea.rows = 4;
    textarea.value = value === undefined ? "" : JSON.stringify(value, null, 2);
    textarea.placeholder = "输入 JSON 值";
    textarea.dataset.pluginVarKind = "write-only-json";
    control = textarea;
  } else if (schema.type === "boolean") {
    const input = document.createElement("input");
    input.type = "checkbox";
    input.checked = typeof value === "boolean"
      ? value
      : schema.default === true;
    input.dataset.pluginVarKind = "boolean";
    control = input;
  } else if (schema.type === "integer" || schema.type === "number") {
    const input = document.createElement("input");
    input.type = "number";
    input.step = schema.type === "integer" ? "1" : "any";
    input.value = typeof value === "number" ? String(value) : "";
    input.dataset.pluginVarKind = schema.type;
    control = input;
  } else if (schema.type === "string" && schema.enum?.every(item => typeof item === "string")) {
    const select = document.createElement("select");
    if (!required) {
      const empty = document.createElement("option");
      empty.value = "";
      empty.textContent = "未设置";
      select.append(empty);
    }
    for (const choice of schema.enum) {
      const option = document.createElement("option");
      option.value = JSON.stringify(choice);
      option.textContent = String(choice);
      option.selected = choice === value;
      select.append(option);
    }
    select.dataset.pluginVarKind = "enum";
    control = select;
  } else if (schema.type === "string" || schema.type === undefined) {
    const input = document.createElement("input");
    input.type = "text";
    input.autocomplete = "off";
    input.value = typeof value === "string" ? value : "";
    input.dataset.pluginVarKind = "string";
    control = input;
  } else {
    const textarea = document.createElement("textarea");
    textarea.rows = 4;
    textarea.value = value === undefined ? "" : JSON.stringify(value, null, 2);
    textarea.placeholder = schema.type === "array" ? "[]" : "{}";
    textarea.dataset.pluginVarKind = "json";
    control = textarea;
  }
  control.dataset.pluginVar = propertyName;
  control.dataset.pluginVarRequired = required ? "true" : "false";
  field.append(control);
  if (schema.description) {
    const description = document.createElement("small");
    description.className = "muted";
    description.textContent = schema.description;
    field.append(description);
  }
  if (configuredWriteOnlyPaths.has(pointer)) {
    const clear = document.createElement("label");
    clear.className = "external-plugin-clear";
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.dataset.clearWriteOnly = pointer;
    const text = document.createElement("span");
    text.textContent = "清除已保存值";
    clear.append(checkbox, text);
    field.append(clear);
  }
  return field;
}

function createExternalPluginNestedSecretClear(path: string): HTMLElement {
  const clear = document.createElement("label");
  clear.className = "external-plugin-clear";
  const checkbox = document.createElement("input");
  checkbox.type = "checkbox";
  checkbox.dataset.clearWriteOnly = path;
  const text = document.createElement("span");
  text.textContent = `清除已保存值 ${path}`;
  clear.append(checkbox, text);
  return clear;
}

function collectExternalPluginVars(
  form: HTMLFormElement,
): { vars: Record<string, unknown>; clearWriteOnlyPaths: string[] } {
  const vars: Record<string, unknown> = {};
  const clearWriteOnlyPaths = Array.from(
    form.querySelectorAll<HTMLInputElement>("[data-clear-write-only]"),
  ).filter(input => input.checked).map(input => input.dataset.clearWriteOnly!);
  const cleared = new Set(clearWriteOnlyPaths);
  const controls = Array.from(form.querySelectorAll<
    HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement
  >("[data-plugin-var]"));
  for (const control of controls) {
    const name = control.dataset.pluginVar;
    const kind = control.dataset.pluginVarKind;
    const required = control.dataset.pluginVarRequired === "true";
    if (!name || !kind) continue;
    if (cleared.has(externalPluginPointer(name))) continue;
    if (kind === "boolean" && control instanceof HTMLInputElement) {
      vars[name] = control.checked;
    } else if ((kind === "integer" || kind === "number") && control.value !== "") {
      const parsed = Number(control.value);
      if (!Number.isFinite(parsed) || (kind === "integer" && !Number.isInteger(parsed))) {
        throw new Error(`${name} 必须是${kind === "integer" ? "整数" : "数字"}。`);
      }
      vars[name] = parsed;
    } else if (kind === "enum" && control.value !== "") {
      vars[name] = JSON.parse(control.value) as unknown;
    } else if (kind === "json" && control.value.trim() !== "") {
      vars[name] = JSON.parse(control.value) as unknown;
    } else if (kind === "write-only-json" && control.value.trim() !== "") {
      vars[name] = JSON.parse(control.value) as unknown;
    } else if (kind === "write-only") {
      if (control.value !== "") vars[name] = control.value;
    } else if (kind === "string" && (required || control.value !== "")) {
      vars[name] = control.value;
    }
  }
  return { vars, clearWriteOnlyPaths };
}

function externalPluginConfigurationForm(
  configuration: ExternalPluginConfiguration,
  configurationRevision: number,
): HTMLElement {
  const details = document.createElement("details");
  details.className = "external-plugin-configuration";
  const summary = document.createElement("summary");
  summary.textContent = configuration.configured ? "启停与参数（已保存）" : "启停与参数（默认禁用）";
  const form = document.createElement("form");
  form.className = "external-plugin-form";
  const enableLabel = document.createElement("label");
  enableLabel.className = "checkbox-row";
  const enabled = document.createElement("input");
  enabled.type = "checkbox";
  enabled.checked = configuration.enabled;
  const enabledText = document.createElement("span");
  enabledText.textContent = "启用此插件";
  enableLabel.append(enabled, enabledText);
  const argsLabel = document.createElement("label");
  argsLabel.className = "external-plugin-field";
  const argsTitle = document.createElement("span");
  argsTitle.textContent = "默认 args（JSON 对象）";
  const args = document.createElement("textarea");
  args.rows = 5;
  args.value = JSON.stringify(configuration.args, null, 2);
  const argsHelp = document.createElement("small");
  argsHelp.className = "muted";
  argsHelp.textContent = "实际任务同名字段优先；凭据请放在 schema 标记 writeOnly 的 vars 中。";
  argsLabel.append(argsTitle, args, argsHelp);
  const vars = document.createElement("fieldset");
  vars.className = "external-plugin-vars";
  const legend = document.createElement("legend");
  legend.textContent = "vars / config schema";
  vars.append(legend);
  const configuredSecrets = new Set(configuration.configured_write_only_paths);
  const required = new Set(configuration.schema.required ?? []);
  const directPointers = new Set<string>();
  for (const [name, schema] of Object.entries(configuration.schema.properties ?? {})) {
    directPointers.add(externalPluginPointer(name));
    vars.append(createExternalPluginVarField(
      name,
      schema,
      configuration.vars[name],
      required.has(name),
      configuredSecrets,
    ));
  }
  for (const path of configuredSecrets) {
    if (!directPointers.has(path)) {
      vars.append(createExternalPluginNestedSecretClear(path));
    }
  }
  if (Object.keys(configuration.schema.properties ?? {}).length === 0) {
    const empty = document.createElement("p");
    empty.className = "muted empty";
    empty.textContent = "该插件没有声明可编辑 vars。";
    vars.append(empty);
  }
  const actions = document.createElement("div");
  actions.className = "external-plugin-actions";
  const save = document.createElement("button");
  save.type = "submit";
  save.textContent = "保存配置";
  actions.append(save);
  if (configuration.configured) {
    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "danger-button";
    remove.textContent = "恢复默认禁用";
    remove.addEventListener("click", () => void deleteExternalPluginConfiguration(
      configuration,
      configurationRevision,
      remove,
    ));
    actions.append(remove);
  }
  const message = document.createElement("small");
  message.className = "external-plugin-form-message muted";
  form.append(enableLabel, argsLabel, vars, actions, message);
  form.addEventListener("submit", event => void saveExternalPluginConfiguration(
    event,
    configuration,
    configurationRevision,
    enabled,
    args,
    message,
  ));
  details.append(summary, form);
  return details;
}

async function saveExternalPluginConfiguration(
  event: SubmitEvent,
  configuration: ExternalPluginConfiguration,
  configurationRevision: number,
  enabled: HTMLInputElement,
  argsInput: HTMLTextAreaElement,
  message: HTMLElement,
): Promise<void> {
  event.preventDefault();
  const form = event.currentTarget as HTMLFormElement;
  const submit = form.querySelector<HTMLButtonElement>('button[type="submit"]');
  if (submit) submit.disabled = true;
  message.textContent = "正在校验并保存…";
  try {
    const args = JSON.parse(argsInput.value) as unknown;
    if (args === null || Array.isArray(args) || typeof args !== "object") {
      throw new Error("args 必须是 JSON 对象。");
    }
    const collected = collectExternalPluginVars(form);
    const response = await fetch(
      `/api/v1/plugins/${encodeURIComponent(configuration.id)}/configuration`,
      {
        method: "PUT",
        headers: new Headers([...headers, ["Content-Type", "application/json"]]),
        body: JSON.stringify({
          expected_revision: configurationRevision,
          enabled: enabled.checked,
          args,
          vars: collected.vars,
          clear_write_only_paths: collected.clearWriteOnlyPaths,
        }),
      },
    );
    if (!response.ok) throw new Error(await responseError(response));
    message.textContent = "已保存；运行中的旧会话已停止。";
    await loadStatus();
  } catch (error) {
    message.textContent = errorMessage(error, "插件配置保存失败");
    if (submit) submit.disabled = false;
  }
}

async function deleteExternalPluginConfiguration(
  configuration: ExternalPluginConfiguration,
  configurationRevision: number,
  button: HTMLButtonElement,
): Promise<void> {
  if (!window.confirm(`恢复 ${configuration.id} 为未配置且默认禁用？已保存 args/vars 将被删除。`)) return;
  button.disabled = true;
  try {
    const response = await fetch(
      `/api/v1/plugins/${encodeURIComponent(configuration.id)}/configuration?expected_revision=${configurationRevision}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    await loadStatus();
  } catch (error) {
    button.textContent = errorMessage(error, "恢复失败");
    button.disabled = false;
  }
}

function renderExternalPlugins(
  status: ExternalPluginStatus,
  configurations: ExternalPluginConfigurationList,
): void {
  const target = element<HTMLElement>("#external-plugin-list");
  const runtimes = new Map(status.runtimes.map(runtime => [runtime.id, runtime]));
  const configurationById = new Map(
    configurations.items.map(configuration => [configuration.id, configuration]),
  );
  const cards: HTMLElement[] = [];
  for (const plugin of status.packages) {
    const runtime = runtimes.get(plugin.id);
    const card = document.createElement("article");
    card.className = `external-plugin-card ${runtime?.state ?? "unknown"}`;
    const heading = document.createElement("div");
    heading.className = "external-plugin-card-heading";
    const name = document.createElement("strong");
    name.textContent = plugin.name;
    const badge = document.createElement("span");
    badge.className = `badge ${runtime?.state === "ready" ? "ready" : runtime?.state === "auto_disabled" ? "error" : "pending"}`;
    badge.textContent = externalPluginStateLabels[runtime?.state ?? "unknown"];
    heading.append(name, badge);
    const identity = document.createElement("code");
    identity.textContent = plugin.id;
    const metadata = document.createElement("p");
    metadata.className = "muted";
    metadata.textContent = `${plugin.type} · ${plugin.version} · ${plugin.rid} · ${plugin.enabled ? "已启用" : "已禁用"}`;
    card.append(heading, identity, metadata);
    if (plugin.capabilities.length > 0) {
      const capabilities = document.createElement("small");
      capabilities.textContent = `能力：${plugin.capabilities.join("、")}`;
      card.append(capabilities);
    }
    if (runtime && (runtime.consecutive_failures > 0 || runtime.last_failure_code)) {
      const failure = document.createElement("small");
      const retry = runtime.retry_at_utc
        ? `；可重试 ${new Date(runtime.retry_at_utc).toLocaleString()}`
        : "";
      failure.className = "external-plugin-failure";
      failure.textContent = `连续失败 ${runtime.consecutive_failures} 次；${runtime.last_failure_code ?? "未分类"}${retry}`;
      card.append(failure);
      const reset = document.createElement("button");
      reset.type = "button";
      reset.className = "secondary-button";
      reset.textContent = "清除故障状态";
      reset.addEventListener("click", () => void resetExternalPlugin(plugin.id, reset));
      card.append(reset);
    }
    const configuration = configurationById.get(plugin.id);
    if (configuration) {
      card.append(externalPluginConfigurationForm(
        configuration,
        configurations.revision,
      ));
    } else {
      const missing = document.createElement("small");
      missing.className = "external-plugin-failure";
      missing.textContent = "配置模型不可用；请刷新或检查插件 schema。";
      card.append(missing);
    }
    cards.push(card);
  }
  for (const error of status.errors) {
    const card = document.createElement("article");
    card.className = "external-plugin-card invalid";
    const heading = document.createElement("strong");
    heading.textContent = error.package_directory_name;
    const code = document.createElement("code");
    code.textContent = error.code;
    const message = document.createElement("small");
    message.textContent = error.message;
    card.append(heading, code, message);
    cards.push(card);
  }
  if (cards.length === 0) {
    renderRegionMessage(
      target,
      "empty",
      "没有发现外部插件包。内置 C# 插件不在此处重复显示。",
    );
    return;
  }
  renderRegionContent(target, ...cards);
}

async function resetExternalPlugin(pluginId: string, button: HTMLButtonElement): Promise<void> {
  const original = button.textContent ?? "清除故障状态";
  button.disabled = true;
  button.textContent = "正在清除…";
  try {
    const response = await fetch(`/api/v1/plugins/${encodeURIComponent(pluginId)}/reset`, {
      method: "POST",
      headers,
    });
    if (!response.ok) throw new Error(await responseError(response));
    await loadStatus();
  } catch (error) {
    button.textContent = errorMessage(error, "清除失败");
    button.disabled = false;
    return;
  }
  button.textContent = original;
}

async function loadStatus(): Promise<void> {
  const health = element<HTMLElement>("#health");
  const modulesTarget = element<HTMLElement>("#modules");
  const pluginsTarget = element<HTMLElement>("#external-plugin-list");
  setRegionState(modulesTarget, "loading");
  setRegionState(pluginsTarget, "loading");
  try {
    const [status, pluginConfigurations] = await Promise.all([
      api.get<RuntimeStatus>("/api/v1/status"),
      api.get<ExternalPluginConfigurationList>("/api/v1/plugins"),
    ]);
    externalSourceAdapters = pluginConfigurations.items.filter(
      (configuration) => configuration.type === "source",
    );
    refreshSourceAdapterOptions();
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
      state.textContent = enabled ? "已启用" : "当前不可用";
      item.append(title, state);
      return item;
    });
    renderRegionContent(modulesTarget, ...modules);
    renderExternalPlugins(status.external_plugins, pluginConfigurations);
    health.textContent = "运行中";
    health.className = "badge ready";
  } catch (error) {
    const message = errorMessage(error, "连接失败");
    health.textContent = message;
    health.className = "badge error";
    renderRegionMessage(modulesTarget, "error", `模块状态读取失败：${message}`);
    renderRegionMessage(pluginsTarget, "error", `外部插件状态读取失败：${message}`);
  }
}

async function loadDirectoryDatabase(refresh = false): Promise<void> {
  const target = element<HTMLElement>("#directory-database-status");
  const button = element<HTMLButtonElement>("#directory-database-refresh");
  button.disabled = true;
  if (refresh) target.textContent = "正在刷新…";
  try {
    const status = await api.request<DirectoryDatabaseStatus>(
      refresh
        ? "/api/v1/library/directory-database/refresh"
        : "/api/v1/library/directory-database",
      { method: refresh ? "POST" : "GET" },
    );
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
let dataUpdateUsagePage = 1;
let dataUpdateUsagePageSize: 25 | 50 | 100 = 25;
let dataUpdateUsageKind: "" | "subject" | "episodes" | "relations" = "";

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

const bangumiArchiveHitKindLabels: Record<BangumiArchiveUsageEvent["hit_kind"], string> = {
  subject: "Subject",
  episodes: "Episode 集",
  relations: "前传关系",
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

function bangumiArchiveUsageMessage(text: string, alert = false): HTMLElement {
  const message = document.createElement("p");
  message.className = "muted empty";
  message.textContent = text;
  if (alert) message.setAttribute("role", "alert");
  return message;
}

function renderBangumiArchiveUsage(page: BangumiArchiveUsagePage): void {
  const target = element<HTMLElement>("#data-update-usage-list");
  if (page.items.length === 0) {
    target.replaceChildren(bangumiArchiveUsageMessage(
      page.hit_kind === null
        ? "暂无本地缓存命中明细；新的命中会从这里开始逐条记录。"
        : "当前类型没有本地缓存命中明细。",
    ));
  } else {
    target.replaceChildren(...page.items.map(item => {
      const card = document.createElement("dl");
      card.className = "data-update-usage-item";
      const fields: Array<[string, string]> = [
        ["命中类型", bangumiArchiveHitKindLabels[item.hit_kind]],
        ["Bangumi Subject ID", String(item.subject_id)],
        ["返回记录数", `${item.result_count} 条`],
        ["AnimeGoNetData 版本", item.data_version],
        ["命中时间", dataUpdateTime(item.hit_at_utc)],
        ["明细序号", String(item.id)],
      ];
      for (const [label, value] of fields) {
        const field = document.createElement("div");
        const term = document.createElement("dt");
        term.textContent = label;
        const description = document.createElement("dd");
        description.textContent = value;
        field.append(term, description);
        card.append(field);
      }
      return card;
    }));
  }

  const totalPages = Math.max(1, Math.ceil(page.total_items / page.page_size));
  element<HTMLElement>("#data-update-usage-page").textContent =
    `第 ${page.page} / ${totalPages} 页 · 共 ${page.total_items} 条`;
  element<HTMLButtonElement>("#data-update-usage-previous").disabled = page.page <= 1;
  element<HTMLButtonElement>("#data-update-usage-next").disabled = page.page >= totalPages;
}

async function loadBangumiArchiveUsage(silent = false): Promise<void> {
  const message = element<HTMLElement>("#data-update-usage-status");
  if (!silent) message.textContent = "正在读取命中明细…";
  const query = new URLSearchParams({
    page: String(dataUpdateUsagePage),
    page_size: String(dataUpdateUsagePageSize),
  });
  if (dataUpdateUsageKind) query.set("hit_kind", dataUpdateUsageKind);
  try {
    const response = await fetch(
      `/api/v1/data-update/archive-usage?${query.toString()}`,
      { headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const page = await response.json() as BangumiArchiveUsagePage;
    const totalPages = Math.max(1, Math.ceil(page.total_items / page.page_size));
    if (dataUpdateUsagePage > totalPages) {
      dataUpdateUsagePage = totalPages;
      await loadBangumiArchiveUsage(silent);
      return;
    }

    dataUpdateUsagePage = page.page;
    message.textContent = page.hit_kind === null
      ? `全部类型 · 共 ${page.total_items} 条`
      : `${bangumiArchiveHitKindLabels[page.hit_kind]} · 共 ${page.total_items} 条`;
    renderBangumiArchiveUsage(page);
  } catch (error) {
    message.textContent = errorMessage(error, "本地缓存命中明细读取失败");
    element<HTMLElement>("#data-update-usage-list").replaceChildren(
      bangumiArchiveUsageMessage("命中明细暂时不可用。", true),
    );
  }
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
      configurationCard("AnimeGoNetData 本地缓存使用记录", [
        ["累计命中", `${status.archive_usage.total_hits} 次`],
        ["Subject 命中", `${status.archive_usage.subject_hits} 次`],
        ["完整 Episode 集命中", `${status.archive_usage.episode_hits} 次`],
        ["前传关系命中", `${status.archive_usage.relation_hits} 次`],
        ["最近命中", dataUpdateTime(status.archive_usage.last_hit_at_utc)],
      ]),
    );
    if (workspaceFromHash().workspace === "bangumi-cache") {
      await loadBangumiArchiveUsage(silent);
    }
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

function setCacheBusy(busy: boolean): void {
  element<HTMLSelectElement>("#cache-database").disabled = busy;
  element<HTMLButtonElement>("#cache-reload").disabled = busy;
  for (const target of [
    element<HTMLElement>("#cache-buckets"),
    element<HTMLElement>("#cache-entries"),
  ]) {
    if (busy) {
      setRegionState(target, "loading");
    } else if (target.dataset.uiState === "loading") {
      setRegionState(target, "ready");
    }
  }
}

function renderCacheBuckets(): void {
  const target = element<HTMLElement>("#cache-buckets");
  if (cacheBuckets.length === 0) {
    renderRegionMessage(target, "empty", "当前命名空间没有 bucket。");
    return;
  }
  renderRegionContent(target, ...cacheBuckets.map(bucket => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "secondary-button cache-bucket-button";
    button.setAttribute("aria-current", String(bucket.bucket_id === activeCacheBucketId));
    const label = document.createElement("code");
    label.textContent = bucket.bucket_name;
    const count = document.createElement("span");
    count.textContent = `${bucket.entry_count} 项`;
    button.append(label, count);
    button.addEventListener("click", () => {
      if (activeCacheBucketId === bucket.bucket_id) return;
      activeCacheBucketId = bucket.bucket_id;
      cachePage = 1;
      renderCacheBuckets();
      void loadCacheEntries();
    });
    return button;
  }));
}

function renderCacheEntries(page: CacheBrowserEntryList): void {
  const target = element<HTMLElement>("#cache-entries");
  if (page.items.length === 0) {
    renderRegionMessage(
      target,
      "empty",
      page.bucket_id === ""
        ? "当前命名空间没有 bucket。"
        : page.total_count === 0 ? "此 bucket 没有有效条目。" : "当前页没有条目。",
    );
  } else {
    renderRegionContent(target, ...page.items.map(item => {
      const card = document.createElement("article");
      card.className = "cache-entry";
      const details = document.createElement("div");
      const identity = document.createElement("code");
      identity.textContent = item.key;
      const metadata = document.createElement("p");
      metadata.className = "muted";
      metadata.textContent = `${formatBytes(item.value_bytes)} · 更新 ${dataUpdateTime(item.updated_at_utc)}`
        + ` · ${item.expires_at_utc ? `过期 ${dataUpdateTime(item.expires_at_utc)}` : "永久"}`;
      details.append(identity, metadata);
      card.append(details);
      const actions = document.createElement("div");
      actions.className = "cache-entry-actions";
      const view = document.createElement("button");
      view.type = "button";
      view.className = "secondary-button";
      view.textContent = "查看完整内容";
      view.addEventListener("click", () => void openCacheEntry(item, view));
      actions.append(view);
      if (!page.read_only) {
        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "danger-button";
        remove.textContent = "删除此缓存项";
        remove.addEventListener("click", () => void deleteCacheEntry(item, remove));
        actions.append(remove);
      } else {
        const state = document.createElement("span");
        state.className = "badge pending";
        state.textContent = "只读";
        actions.append(state);
      }
      card.append(actions);
      return card;
    }));
  }
  cacheTotalCount = page.total_count;
  const totalPages = Math.max(1, Math.ceil(cacheTotalCount / cachePageSize));
  element<HTMLElement>("#cache-page-label").textContent =
    `第 ${page.page} / ${totalPages} 页 · ${page.total_count} 项`;
  element<HTMLButtonElement>("#cache-previous").disabled = page.page <= 1;
  element<HTMLButtonElement>("#cache-next").disabled = page.page >= totalPages;
}

async function openCacheEntry(
  item: CacheBrowserEntry,
  button: HTMLButtonElement,
): Promise<void> {
  if (!activeCacheBucketId) return;
  button.disabled = true;
  element<HTMLElement>("#cache-entry-detail-database").textContent = cacheDatabase;
  element<HTMLElement>("#cache-entry-detail-bucket").textContent = "正在读取…";
  element<HTMLElement>("#cache-entry-detail-key").textContent = item.key;
  element<HTMLElement>("#cache-entry-detail-size").textContent = formatBytes(item.value_bytes);
  element<HTMLElement>("#cache-entry-detail-updated").textContent = dataUpdateTime(item.updated_at_utc);
  element<HTMLElement>("#cache-entry-detail-expiry").textContent = item.expires_at_utc
    ? dataUpdateTime(item.expires_at_utc)
    : "永久";
  element<HTMLElement>("#cache-entry-detail-status").textContent = "正在读取未截断的完整内容…";
  element<HTMLElement>("#cache-entry-detail-value").textContent = "";
  cacheEntryDialog.showModal();
  try {
    const query = new URLSearchParams({
      database: cacheDatabase,
      bucket_id: activeCacheBucketId,
    });
    const detail = await api.get<CacheBrowserEntryDetail>(
      `/api/v1/cache/entries/${encodeURIComponent(item.entry_id)}?${query}`,
    );
    element<HTMLElement>("#cache-entry-detail-database").textContent =
      `${detail.database}${detail.read_only ? "（只读）" : ""}`;
    element<HTMLElement>("#cache-entry-detail-bucket").textContent = detail.bucket_name;
    element<HTMLElement>("#cache-entry-detail-key").textContent = detail.key;
    element<HTMLElement>("#cache-entry-detail-size").textContent = formatBytes(detail.value_bytes);
    element<HTMLElement>("#cache-entry-detail-updated").textContent = dataUpdateTime(detail.updated_at_utc);
    element<HTMLElement>("#cache-entry-detail-expiry").textContent = detail.expires_at_utc
      ? dataUpdateTime(detail.expires_at_utc)
      : "永久";
    element<HTMLElement>("#cache-entry-detail-value").textContent = detail.value_json;
    element<HTMLElement>("#cache-entry-detail-status").textContent =
      `完整原始 JSON · ${formatBytes(detail.value_bytes)} · 未截断`;
  } catch (error) {
    element<HTMLElement>("#cache-entry-detail-status").textContent =
      errorMessage(error, "缓存完整内容读取失败");
    element<HTMLElement>("#cache-entry-detail-value").textContent = "";
  } finally {
    button.disabled = false;
  }
}

async function loadCacheBuckets(): Promise<void> {
  const sequence = ++cacheRequestSequence;
  const status = element<HTMLElement>("#cache-status");
  setCacheBusy(true);
  status.textContent = "正在读取安全缓存索引…";
  try {
    const result = await api.get<CacheBrowserBucketList>(
      `/api/v1/cache/buckets?database=${cacheDatabase}`,
    );
    if (sequence !== cacheRequestSequence) return;
    cacheReadOnly = result.read_only;
    cacheBuckets = result.items;
    if (!cacheBuckets.some(bucket => bucket.bucket_id === activeCacheBucketId)) {
      activeCacheBucketId = cacheBuckets[0]?.bucket_id ?? null;
      cachePage = 1;
    }
    renderCacheBuckets();
    status.textContent = `${result.database} · ${result.read_only ? "只读" : "可精确删除"} · ${result.items.length} 个 bucket`;
    if (activeCacheBucketId) {
      await loadCacheEntries(sequence);
    } else {
      renderCacheEntries({
        database: cacheDatabase,
        read_only: cacheReadOnly,
        bucket_id: "",
        page: 1,
        page_size: cachePageSize,
        total_count: 0,
        items: [],
      });
    }
  } catch (error) {
    if (sequence !== cacheRequestSequence) return;
    status.textContent = errorMessage(error, "缓存索引读取失败");
    cacheBuckets = [];
    activeCacheBucketId = null;
    renderRegionMessage(
      element<HTMLElement>("#cache-buckets"),
      "error",
      "缓存 bucket 读取失败，请刷新。",
    );
    renderRegionMessage(
      element<HTMLElement>("#cache-entries"),
      "error",
      "缓存索引读取失败，无法读取条目。",
    );
  } finally {
    if (sequence === cacheRequestSequence) setCacheBusy(false);
  }
}

async function loadCacheEntries(parentSequence?: number): Promise<void> {
  if (!activeCacheBucketId) return;
  const sequence = parentSequence ?? ++cacheRequestSequence;
  const status = element<HTMLElement>("#cache-status");
  const entries = element<HTMLElement>("#cache-entries");
  setRegionState(entries, "loading");
  try {
    const query = new URLSearchParams({
      database: cacheDatabase,
      bucket_id: activeCacheBucketId,
      page: String(cachePage),
      page_size: String(cachePageSize),
    });
    const result = await api.get<CacheBrowserEntryList>(`/api/v1/cache/entries?${query}`);
    if (sequence !== cacheRequestSequence) return;
    cacheReadOnly = result.read_only;
    renderCacheEntries(result);
  } catch (error) {
    if (sequence !== cacheRequestSequence) return;
    status.textContent = errorMessage(error, "缓存条目读取失败");
    renderRegionMessage(entries, "error", "缓存条目读取失败，请刷新。");
    element<HTMLButtonElement>("#cache-previous").disabled = true;
    element<HTMLButtonElement>("#cache-next").disabled = true;
  } finally {
    if (sequence === cacheRequestSequence && entries.dataset.uiState === "loading") {
      setRegionState(entries, "ready");
    }
  }
}

async function deleteCacheEntry(
  item: CacheBrowserEntry,
  button: HTMLButtonElement,
): Promise<void> {
  if (!activeCacheBucketId || cacheReadOnly) return;
  const label = `key ${item.key}`;
  if (!window.confirm(`确认删除 ${label}？只删除这一条 bolt 缓存，不删除业务记录或文件。`)) return;
  button.disabled = true;
  const status = element<HTMLElement>("#cache-status");
  status.textContent = `正在删除 ${label}…`;
  try {
    const result = await api.delete<CacheBrowserDeleteResponse, {
      database: CacheDatabase;
      bucket_id: string;
      delete_token: string;
    }>(
      `/api/v1/cache/entries/${item.entry_id}`,
      {
        database: cacheDatabase,
        bucket_id: activeCacheBucketId,
        delete_token: item.delete_token,
      },
    );
    if (!result.deleted || result.entry_id !== item.entry_id) {
      throw new Error("缓存删除响应无效，请刷新后确认条目状态。");
    }
    const remainingAfterDelete = Math.max(0, cacheTotalCount - 1);
    if (cachePage > 1 && (cachePage - 1) * cachePageSize >= remainingAfterDelete) {
      cachePage--;
    }
    await loadCacheBuckets();
    status.textContent = `${label} 已删除；列表已刷新。`;
  } catch (error) {
    status.textContent = errorMessage(error, "缓存删除失败");
    button.disabled = false;
    await loadCacheEntries();
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

function liveLogFilter(): LiveLogFilter {
  const minimum = element<HTMLSelectElement>("#live-log-level").value;
  return {
    minimumLevel: minimum === "all" ? "all" : minimum as LiveLogLevel,
    query: element<HTMLInputElement>("#live-log-search").value,
    category: element<HTMLInputElement>("#live-log-category").value,
    eventId: element<HTMLInputElement>("#live-log-event-id").value,
    domain: liveLogDomain,
    httpScope: element<HTMLSelectElement>("#live-log-http-scope").value as LiveLogHttpScope,
    fromUtc: localDateTimeToUtc("#live-log-from"),
    toUtc: localDateTimeToUtc("#live-log-to"),
    exceptionOnly: element<HTMLInputElement>("#live-log-exception-only").checked,
  };
}

function visibleLiveLogEntries(): ParsedLiveLogEntry[] {
  return filterLiveLogEntries(liveLogEntries, liveLogFilter());
}

function liveLogTime(timestamp: string | null): string {
  if (!timestamp) return "时间未知";
  const parsed = new Date(timestamp);
  return Number.isNaN(parsed.getTime())
    ? timestamp
    : parsed.toLocaleTimeString("zh-CN", {
        hour12: false,
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
        fractionalSecondDigits: 3,
      });
}

function liveLogDetail(label: string, value: string): HTMLDivElement {
  const row = document.createElement("div");
  const term = document.createElement("dt");
  term.textContent = label;
  const description = document.createElement("dd");
  description.textContent = value;
  row.append(term, description);
  return row;
}

function renderLiveLogs(): void {
  const visible = visibleLiveLogEntries();
  const stream = element<HTMLElement>("#live-log-stream");
  stream.classList.toggle(
    "nowrap",
    !element<HTMLInputElement>("#live-log-wrap").checked,
  );
  if (visible.length === 0) {
    stream.replaceChildren(Object.assign(document.createElement("p"), {
      className: "muted empty",
      textContent: liveLogEntries.length === 0
        ? "等待日志…"
        : "当前组合筛选下没有日志。",
    }));
  } else {
    const nodes = visible.map(entry => {
      const line = document.createElement("details");
      line.className = `live-log-entry ${entry.level}`;
      const summary = document.createElement("summary");
      const time = document.createElement("time");
      time.textContent = liveLogTime(entry.timestamp);
      if (entry.timestamp) time.dateTime = entry.timestamp;
      const level = document.createElement("strong");
      level.textContent = entry.level.toUpperCase();
      const category = document.createElement("span");
      category.className = "live-log-category";
      category.textContent = entry.category;
      const eventId = document.createElement("span");
      eventId.className = "live-log-event";
      eventId.textContent = entry.eventId === null ? "—" : `#${entry.eventId}`;
      const message = document.createElement("span");
      message.className = "live-log-message";
      message.textContent = entry.message;
      summary.append(time, level, category, eventId, message);
      const details = document.createElement("dl");
      details.className = "live-log-detail";
      details.append(
        liveLogDetail("UTC 时间", entry.timestamp ?? "未知"),
        liveLogDetail("级别", entry.level),
        liveLogDetail("类别", entry.category),
        liveLogDetail("Event ID", entry.eventId === null ? "无" : String(entry.eventId)),
        liveLogDetail("消息", entry.message),
      );
      if (entry.exception) details.append(liveLogDetail("异常", entry.exception));
      details.append(liveLogDetail("脱敏原文", entry.text));
      line.append(summary, details);
      return line;
    });
    stream.replaceChildren(...nodes);
    if (element<HTMLInputElement>("#live-log-auto-scroll").checked) {
      stream.scrollTop = stream.scrollHeight;
    }
  }
  element<HTMLElement>("#live-log-count").textContent =
    `本页 ${liveLogEntries.length} / ${maximumRenderedLogs} 条`
    + (visible.length === liveLogEntries.length ? "" : ` · 显示 ${visible.length}`);
  element<HTMLElement>("#live-log-total").textContent = String(liveLogEntries.length);
  element<HTMLElement>("#live-log-warning-count").textContent = String(
    liveLogEntries.filter(entry => entry.level === "warning").length,
  );
  element<HTMLElement>("#live-log-error-count").textContent = String(
    liveLogEntries.filter(entry => entry.level === "error" || entry.level === "critical").length,
  );
  element<HTMLElement>("#live-log-ai-count").textContent = String(
    liveLogEntries.filter(entry => classifyLiveLogEntry(entry) === "ai").length,
  );
  const httpSummary = liveLogEntries.reduce(
    (summary, entry) => {
      const direction = classifyLiveLogHttpDirection(entry);
      if (direction !== "none") summary[direction]++;
      return summary;
    },
    { inbound: 0, outbound: 0 },
  );
  element<HTMLElement>("#live-log-http-count").textContent =
    `${httpSummary.outbound} / ${httpSummary.inbound}`;
}

function localDateTimeToUtc(selector: string): string | undefined {
  const value = element<HTMLInputElement>(selector).value;
  if (!value) return undefined;
  const parsed = new Date(value);
  return Number.isNaN(parsed.valueOf()) ? undefined : parsed.toISOString();
}

function setLiveLogDomain(domain: LiveLogDomain | "all"): void {
  liveLogDomain = domain;
  document.querySelectorAll<HTMLButtonElement>("[data-log-domain]").forEach(button => {
    button.classList.toggle("active", button.dataset.logDomain === domain);
  });
  renderLiveLogs();
}

function aiLogResultLabel(value: string): string {
  return ({
    matched: "成功",
    not_matched: "未匹配",
    error: "调用 / 验证错误",
    failed: "失败",
    skipped: "跳过",
    not_applicable: "不适用",
  } as Record<string, string>)[value] ?? value;
}

function aiLogNumber(value: number | null): string {
  return value === null ? "—" : value.toLocaleString("zh-CN");
}

function renderAiInvocationLogSummary(result: AiInvocationLogList): void {
  element<HTMLElement>("#ai-log-total").textContent = result.total_items.toLocaleString("zh-CN");
  element<HTMLElement>("#ai-log-result-total").textContent =
    `${result.summary.matched_items.toLocaleString("zh-CN")} / ${result.summary.failed_items.toLocaleString("zh-CN")}`;
  element<HTMLElement>("#ai-log-token-total").textContent =
    result.summary.total_tokens.toLocaleString("zh-CN");
  element<HTMLElement>("#ai-log-request-total").textContent =
    `${result.summary.request_count.toLocaleString("zh-CN")} / ${result.summary.tool_call_count.toLocaleString("zh-CN")}`;
}

function openAiLogTask(item: AiInvocationLogItem): void {
  metadataState.search = item.task_id;
  metadataState.page = 1;
  element<HTMLInputElement>("#metadata-search").value = item.task_id;
  saveMetadataState();
  selectWorkspace("tasks", "metadata");
  void loadMetadataTasks();
}

function aiDebugText(value: unknown): string {
  if (typeof value === "string") {
    try {
      return JSON.stringify(JSON.parse(value) as unknown, null, 2);
    } catch {
      return value;
    }
  }
  return JSON.stringify(value, null, 2);
}

function aiDebugCodeBlock(value: unknown, className = "ai-debug-payload"): HTMLElement {
  const pre = document.createElement("pre");
  pre.className = className;
  const code = document.createElement("code");
  code.textContent = aiDebugText(value);
  pre.append(code);
  return pre;
}

function aiDebugDisclosure(title: string, value: unknown, open = false): HTMLElement {
  const details = document.createElement("details");
  details.className = "ai-debug-disclosure";
  details.open = open;
  const summary = document.createElement("summary");
  summary.textContent = title;
  details.append(summary, aiDebugCodeBlock(value));
  return details;
}

function renderAiDebugDocument(documentValue: AiDebugDocument): void {
  const chain = documentValue.chain;
  const content = element<HTMLElement>("#ai-debug-content");
  const metadata = document.createElement("dl");
  metadata.className = "live-log-detail ai-debug-summary";
  metadata.append(
    liveLogDetail("Trace / Run", `${chain.trace_id} / ${textOrDash(chain.run_id)}`),
    liveLogDetail("任务 ID", textOrDash(chain.task_id)),
    liveLogDetail("模型", `${chain.model} · ${chain.api_mode} · ${chain.prompt_version}`),
    liveLogDetail("时间", `${dataUpdateTime(chain.started_at_utc)} → ${dataUpdateTime(chain.completed_at_utc)}`),
    liveLogDetail("最终状态", chain.failure_code ?? "本地验证已执行"),
    liveLogDetail("记录格式", `v${documentValue.format_version}`),
  );

  const sections: HTMLElement[] = [metadata];
  const preAi = chain.pre_ai_context;
  if (preAi) {
    const section = document.createElement("section");
    section.className = "ai-debug-section";
    const heading = document.createElement("div");
    heading.className = "ai-debug-section-heading";
    const title = document.createElement("h3");
    title.textContent = "1. AI 前置链路";
    const badge = document.createElement("span");
    badge.className = "badge pending";
    badge.textContent = preAi.trigger_stage === "episode" ? "EP 失败后进入 AI" : "Series / Season 失败后进入 AI";
    heading.append(title, badge);
    const evidence = document.createElement("dl");
    evidence.className = "live-log-detail ai-debug-evidence";
    evidence.append(
      liveLogDetail("Torrent 发布时间", textOrDash(preAi.torrent_published_at)),
      liveLogDetail("Bangumi EP 候选", textOrDash(preAi.bangumi_episode_candidate)),
      liveLogDetail("发布时间优先", preAi.use_bangumi_pub_date_first ? "启用" : "未启用"),
      liveLogDetail("发布时间证据", `${preAi.publication_result}${preAi.publication_error_code ? ` · ${preAi.publication_error_code}` : ""}`),
      liveLogDetail("模型外锁定 TMDB", preAi.expected_tmdb_series_id === null
        ? "未锁定"
        : `${preAi.expected_tmdb_series_id} / S${String(preAi.expected_season_number ?? 0).padStart(2, "0")}`),
      liveLogDetail("已尝试 TMDB 搜索词", preAi.attempted_tmdb_search_titles.length === 0
        ? "无"
        : preAi.attempted_tmdb_search_titles.join(" → ")),
    );
    section.append(heading, evidence, aiDebugDisclosure("任务输入快照", preAi.input, true));
    const timeline = document.createElement("div");
    timeline.className = "ai-debug-timeline";
    if (preAi.attempts.length === 0) {
      const empty = document.createElement("p");
      empty.className = "muted";
      empty.textContent = "本次调用前没有已落库的确定性尝试记录。";
      timeline.append(empty);
    } else {
      for (const [index, attempt] of preAi.attempts.entries()) {
        const row = document.createElement("details");
        row.className = `ai-debug-step ${attempt.result}`;
        const summary = document.createElement("summary");
        summary.textContent = `${index + 1}. ${attempt.stage} · ${attempt.strategy} → ${attempt.result}${attempt.error_code ? ` · ${attempt.error_code}` : ""}`;
        row.append(summary, aiDebugCodeBlock(attempt));
        timeline.append(row);
      }
    }
    section.append(timeline);
    sections.push(section);
  }

  const promptSection = document.createElement("section");
  promptSection.className = "ai-debug-section";
  const promptTitle = document.createElement("h3");
  promptTitle.textContent = "2. 当次提示词快照";
  promptSection.append(
    promptTitle,
    aiDebugDisclosure("原始 Prompt 模板", chain.prompt_template),
    aiDebugDisclosure("最终渲染 Prompt（实际发给模型）", chain.rendered_prompt, true),
  );
  sections.push(promptSection);

  const exchangeSection = document.createElement("section");
  exchangeSection.className = "ai-debug-section";
  const exchangeTitle = document.createElement("h3");
  exchangeTitle.textContent = `3. AI / MCP 请求链（${chain.exchanges.length} 步）`;
  exchangeSection.append(exchangeTitle);
  const exchanges = document.createElement("div");
  exchanges.className = "ai-debug-timeline";
  for (const exchange of chain.exchanges) {
    const item = document.createElement("details");
    item.className = `ai-debug-step ${exchange.error ? "error" : "matched"}`;
    const summary = document.createElement("summary");
    summary.textContent = `#${exchange.sequence} ${exchange.channel} · ${exchange.operation} · HTTP ${textOrDash(exchange.status_code)} · ${exchange.duration_milliseconds} ms`;
    const endpoint = document.createElement("p");
    endpoint.className = "ai-debug-endpoint";
    endpoint.textContent = exchange.endpoint;
    item.append(summary, endpoint);
    if (exchange.request_body !== null) item.append(aiDebugDisclosure("请求 Body", exchange.request_body));
    if (exchange.response_body !== null) item.append(aiDebugDisclosure("响应 Body", exchange.response_body));
    if (exchange.error !== null) item.append(aiDebugDisclosure("错误", exchange.error, true));
    exchanges.append(item);
  }
  exchangeSection.append(exchanges);
  sections.push(exchangeSection);

  const resultSection = document.createElement("section");
  resultSection.className = "ai-debug-section";
  const resultTitle = document.createElement("h3");
  resultTitle.textContent = "4. 模型结果与本地验证";
  resultSection.append(
    resultTitle,
    aiDebugDisclosure("模型原始输出", chain.raw_output ?? "—", true),
    aiDebugDisclosure("解析候选", chain.candidate),
    aiDebugDisclosure("TMDB 本地验证", documentValue.validation, true),
    aiDebugDisclosure("用量", chain.usage),
  );
  sections.push(resultSection, aiDebugDisclosure("完整原始 Debug JSON", documentValue));
  content.replaceChildren(...sections);
}

async function openAiDebugChain(item: AiInvocationLogItem): Promise<void> {
  activeAiDebugRunId = item.run_id;
  element<HTMLElement>("#ai-debug-dialog-title").textContent = `AI Debug · ${item.title}`;
  element<HTMLElement>("#ai-debug-status").textContent = "正在读取完整链路…";
  element<HTMLElement>("#ai-debug-content").replaceChildren();
  element<HTMLButtonElement>("#ai-debug-delete").disabled = true;
  aiDebugDialog.showModal();
  try {
    const documentValue = await api.get<AiDebugDocument>(
      `/api/v1/logs/ai-invocations/${encodeURIComponent(item.run_id)}/debug`,
    );
    if (activeAiDebugRunId !== item.run_id) return;
    renderAiDebugDocument(documentValue);
    element<HTMLElement>("#ai-debug-status").textContent =
      "本机敏感调试记录：包含任务标题、文件名、Prompt 与 AI/MCP Body；不包含 API Key、Cookie、passkey 或 Authorization Header。";
    element<HTMLButtonElement>("#ai-debug-delete").disabled = false;
  } catch (error) {
    if (activeAiDebugRunId !== item.run_id) return;
    element<HTMLElement>("#ai-debug-status").textContent =
      errorMessage(error, "AI Debug 链路读取失败");
  }
}

async function deleteActiveAiDebugChain(): Promise<void> {
  if (activeAiDebugRunId === null) return;
  if (!window.confirm("只删除这一次 AI Debug 完整链路文件？任务、匹配结果和普通 AI 调用日志不会删除。")) return;
  const runId = activeAiDebugRunId;
  const button = element<HTMLButtonElement>("#ai-debug-delete");
  button.disabled = true;
  try {
    await api.delete<void>(
      `/api/v1/logs/ai-invocations/${encodeURIComponent(runId)}/debug`,
    );
    aiDebugDialog.close();
    await loadAiInvocationLogs();
  } catch (error) {
    button.disabled = false;
    element<HTMLElement>("#ai-debug-status").textContent =
      errorMessage(error, "AI Debug 链路删除失败");
  }
}

function renderAiInvocationLogs(result: AiInvocationLogList): void {
  const list = element<HTMLElement>("#ai-log-list");
  if (result.items.length === 0) {
    renderRegionMessage(list, "empty", "当前筛选下没有 AI 调用记录。");
  } else {
    const entries = result.items.map(item => {
      const entry = document.createElement("details");
      entry.className = `ai-log-entry ${item.result}`;
      const summary = document.createElement("summary");
      const time = document.createElement("time");
      time.dateTime = item.created_at_utc;
      time.textContent = dataUpdateTime(item.created_at_utc);
      const resultBadge = document.createElement("strong");
      resultBadge.className = `badge ${item.result === "matched" ? "ok" : ["error", "not_matched", "failed"].includes(item.result) ? "error" : "pending"}`;
      resultBadge.textContent = aiLogResultLabel(item.result);
      const title = document.createElement("span");
      title.className = "ai-log-title";
      title.textContent = item.title;
      const stage = document.createElement("span");
      stage.className = "ai-log-stage";
      stage.textContent = `${item.stage} · ${item.strategy}`;
      const model = document.createElement("span");
      model.className = "ai-log-model";
      model.textContent = item.model;
      summary.append(time, resultBadge, title, stage, model);

      const details = document.createElement("dl");
      details.className = "live-log-detail ai-log-detail";
      details.append(
        liveLogDetail("任务 ID", item.task_id),
        liveLogDetail("Run / Attempt", `${item.run_id} / ${item.attempt_id}`),
        liveLogDetail("来源", item.source_id),
        liveLogDetail("作品参考", `mikanid ${textOrDash(item.mikanid)} · bgmid ${textOrDash(item.bgmid)}`),
        liveLogDetail("TMDB 验证", item.tmdb_series_id === null
          ? "未确认"
          : `${item.tmdb_series_id} / S${String(item.tmdb_season_number ?? 0).padStart(2, "0")}`),
        liveLogDetail("运行状态", `${item.run_status} · ${item.retryable ? "可重试" : "不可重试"}`),
        liveLogDetail("耗时", `${item.duration_ms.toLocaleString("zh-CN")} ms`),
        liveLogDetail("Token", `Prompt ${aiLogNumber(item.prompt_tokens)} · Completion ${aiLogNumber(item.completion_tokens)} · Total ${aiLogNumber(item.total_tokens)}`),
        liveLogDetail("请求", `HTTP ${item.request_count} · 工具 ${item.tool_call_count}`),
        liveLogDetail("错误码", textOrDash(item.error_code)),
        liveLogDetail("结果原因", textOrDash(item.reason)),
      );
      const actions = document.createElement("div");
      actions.className = "ai-log-entry-actions";
      const taskButton = document.createElement("button");
      taskButton.type = "button";
      taskButton.className = "secondary-button";
      taskButton.textContent = "打开任务详情";
      taskButton.addEventListener("click", event => {
        event.preventDefault();
        openAiLogTask(item);
      });
      actions.append(taskButton);
      if (item.debug_available) {
        const debugButton = document.createElement("button");
        debugButton.type = "button";
        debugButton.className = "secondary-button";
        debugButton.textContent = "查看完整链路";
        debugButton.addEventListener("click", event => {
          event.preventDefault();
          void openAiDebugChain(item);
        });
        actions.append(debugButton);
      }
      entry.append(summary, details, actions);
      return entry;
    });
    list.replaceChildren(...entries);
    setRegionState(list, "ready");
  }
  aiLogPage = result.page;
  aiLogPageSize = result.page_size;
  aiLogTotalItems = result.total_items;
  const lastPage = Math.max(1, Math.ceil(result.total_items / result.page_size));
  element<HTMLElement>("#ai-log-page-label").textContent =
    `第 ${result.page} / ${lastPage} 页 · ${result.total_items.toLocaleString("zh-CN")} 条`;
  element<HTMLButtonElement>("#ai-log-previous").disabled = result.page <= 1;
  element<HTMLButtonElement>("#ai-log-next").disabled = result.page >= lastPage;
  element<HTMLElement>("#ai-log-status").textContent =
    `已读取 ${result.items.length} 条；汇总基于当前全部筛选结果。`;
  renderAiInvocationLogSummary(result);
}

async function loadAiInvocationLogs(): Promise<void> {
  const sequence = ++aiLogRequestSequence;
  const list = element<HTMLElement>("#ai-log-list");
  setRegionState(list, "loading");
  element<HTMLElement>("#ai-log-status").textContent = "正在读取 AI 调用记录…";
  const query = new URLSearchParams({
    page: String(aiLogPage),
    page_size: String(aiLogPageSize),
  });
  const values: Array<[string, string]> = [
    ["search", element<HTMLInputElement>("#ai-log-search").value.trim()],
    ["stage", element<HTMLSelectElement>("#ai-log-stage").value],
    ["result", element<HTMLSelectElement>("#ai-log-result").value],
    ["model", element<HTMLInputElement>("#ai-log-model").value.trim()],
    ["from_utc", localDateTimeToUtc("#ai-log-from") ?? ""],
    ["to_utc", localDateTimeToUtc("#ai-log-to") ?? ""],
  ];
  for (const [key, value] of values) if (value) query.set(key, value);
  try {
    const result = await api.get<AiInvocationLogList>(`/api/v1/logs/ai-invocations?${query}`);
    if (sequence !== aiLogRequestSequence) return;
    renderAiInvocationLogs(result);
  } catch (error) {
    if (sequence !== aiLogRequestSequence) return;
    renderRegionMessage(list, "error", errorMessage(error, "AI 调用日志读取失败"));
    element<HTMLElement>("#ai-log-status").textContent = "AI 调用日志读取失败。";
  }
}

function appendLiveLogs(lines: string[]): void {
  const entries = lines
    .filter(line => line.length > 0)
    .map(parseLiveLogEntry);
  if (entries.length === 0) return;
  liveLogEntries.push(...entries);
  if (liveLogEntries.length > maximumRenderedLogs) {
    liveLogEntries.splice(0, liveLogEntries.length - maximumRenderedLogs);
  }
  renderLiveLogs();
}

async function copyVisibleLiveLogs(): Promise<void> {
  const visible = visibleLiveLogEntries();
  if (visible.length === 0) {
    setLiveLogStatus("当前没有可复制的日志", "empty");
    return;
  }
  try {
    await navigator.clipboard.writeText(visible.map(entry => entry.text).join("\n"));
    setLiveLogStatus(`已复制 ${visible.length} 条脱敏日志`, "connected");
  } catch {
    setLiveLogStatus("浏览器拒绝剪贴板访问，请使用系统选择复制", "error");
  }
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
    search: "",
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
      search: typeof stored.search === "string" && stored.search.length <= 200
        ? stored.search : defaults.search,
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
    file_state: "all",
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
      file_state: stored.file_state === "has_other" ? "has_other" : "all",
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
    manual_mikan_override: "人工 Mikan TMDB 覆盖",
    tmdb_title: "TMDB 标题搜索",
    tmdb_air_date: "TMDB 开播日期验证",
    backtrace: "P3 Bangumi 回溯验证",
    ai_metadata: "AI 统一匹配 + TMDB 验证",
    title_season: "P2 本地任务 title 季度（未验证）",
    first_season: "P1 本地 S01（未验证）",
    pending_tmdb_manual: "待补全 TMDB 人工恢复",
    pending_tmdb_automatic: "待补全 TMDB 自动恢复",
    manual_mikan_offset: "人工 Mikan EP offset + TMDB 验证",
    trusted_mikan_offset: "可信 Mikan EP offset + TMDB 验证",
    tmdb_episode_number: "文件名 EP + TMDB Episode 验证",
    tmdb_episode_bangumi_date: "Bangumi/TMDB EP ±1 日 + TMDB 验证",
    tmdb_episode_bangumi_nearest_date: "单文件 7 日最近日期 + 文件名 EP + TMDB 验证",
    subtitle_association: "字幕关联已确认 EP",
    manual_review_override: "人工审核 TMDB 修正 + TMDB 验证",
  };
  return value ? labels[value] ?? value : "未记录";
}

function resolutionReference(
  runId: string | null,
  attemptId: string | null,
): string {
  if (!runId || !attemptId) return "证据引用未记录";
  return `Run ${runId.slice(0, 8)}… · Attempt ${attemptId.slice(0, 8)}…`;
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

function externalMediaImportReason(value: string | null): string {
  const labels: Record<string, string> = {
    external_media_season_path_ambiguous: "多个 TMDB 季度映射到同一个目录",
    external_media_season_path_unsafe: "季度目录是符号链接或重解析点",
    external_media_file_unsafe: "媒体文件是符号链接或重解析点",
    external_media_file_unreadable: "媒体文件无法读取",
    external_media_file_empty: "媒体文件为空",
    external_media_filename_invalid: "文件名不符合 E###.<视频扩展名>",
    external_media_tmdb_episode_missing: "TMDB Episode snapshot 中不存在该 EP",
    external_media_episode_ambiguous: "同一 TMDB EP 找到多个视频文件",
    external_media_file_changed: "扫描期间文件被删除、清空或替换",
  };
  return value ? labels[value] ?? value : "无";
}

function renderExternalMediaImportResult(
  target: string,
  result: ExternalMediaImportResult,
): void {
  const container = element<HTMLElement>(target);
  container.hidden = false;
  const summary = document.createElement("p");
  summary.textContent =
    `已扫描 ${result.scanned_season_count} 个季度、${result.candidate_file_count} 个视频文件；`
    + `新增 ${result.imported_count}，已有记录 ${result.already_recorded_count}，跳过 ${result.skipped_count}。`;
  if (result.items.length === 0) {
    container.replaceChildren(summary);
    return;
  }

  const details = document.createElement("details");
  details.open = result.skipped_count > 0;
  const heading = document.createElement("summary");
  heading.textContent = `查看 ${result.items.length} 条扫描明细`;
  const list = document.createElement("div");
  list.className = "external-import-list";
  list.replaceChildren(...result.items.map((item) => {
    const row = document.createElement("article");
    row.className = `external-import-item ${item.status}`;
    const path = document.createElement("strong");
    path.textContent = item.relative_path;
    const metadata = document.createElement("span");
    const status = item.status === "imported"
      ? "已补录"
      : item.status === "already_recorded"
        ? "已有完成记录"
        : `已跳过：${externalMediaImportReason(item.reason_code)}`;
    metadata.textContent =
      `TMDB ${item.tmdb_series_id} / S${String(item.tmdb_season_number).padStart(2, "0")}`
      + ` / ${item.tmdb_episode_number === null
        ? "EP 未识别" : `EP ${String(item.tmdb_episode_number).padStart(3, "0")}`}`
      + ` · ${status}`;
    row.append(path, metadata);
    return row;
  }));
  details.append(heading, list);
  container.replaceChildren(summary, details);
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
  const pageCount = Math.max(1, Math.ceil(page.total_items / page.page_size));
  element<HTMLElement>("#library-status").textContent =
    `${page.total_items} 个季度 · ${librarySortLabel(page.sort)} · `
    + (page.direction === "asc" ? "升序" : "降序")
    + (libraryState.search ? ` · 搜索“${libraryState.search}”` : "");
  element<HTMLElement>("#library-page-label").textContent =
    `第 ${page.page} / ${pageCount} 页`;
  element<HTMLButtonElement>("#library-previous").disabled = page.page <= 1;
  element<HTMLButtonElement>("#library-next").disabled = page.page >= pageCount;
  if (page.items.length === 0) {
    renderRegionMessage(
      list,
      "empty",
      libraryState.search
        ? `没有找到与“${libraryState.search}”匹配的作品或季度。`
        : "作品库暂时为空。只有已确认 TMDB Series 与普通 Season 的作品会显示在这里；tmdbid=0 条目请到“待补全 TMDB”处理。",
    );
    return;
  }

  renderRegionContent(list, ...page.items.map((item) => {
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
    [
      "Series 取得",
      `${libraryStrategy(detail.series_resolution_source)} · ${
        resolutionReference(
          detail.series_resolution_run_id,
          detail.series_resolution_attempt_id,
        )
      }`,
    ],
    [
      "Season 取得",
      `${libraryStrategy(detail.season_resolution_source)} · ${
        resolutionReference(
          detail.season_resolution_run_id,
          detail.season_resolution_attempt_id,
        )
      }`,
    ],
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
  element<HTMLButtonElement>("#library-detail-external-import").disabled = false;
  element<HTMLButtonElement>("#library-detail-delete").disabled = false;
  element<HTMLElement>("#library-detail-action-status").textContent =
    "外部媒体只在手动点击时扫描；TMDB 刷新只更新权威投影；删除不处理业务记录、下载器任务或文件。";
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
  const detailIdentity = `${tmdbSeriesId}:${seasonNumber}`;
  if (externalImportDetailIdentity !== detailIdentity) {
    const importResult = element<HTMLElement>("#library-detail-external-import-result");
    importResult.hidden = true;
    importResult.replaceChildren();
  }
  const panel = element<HTMLElement>("#library-detail");
  panel.hidden = false;
  element<HTMLElement>("#library-detail-title").textContent = "正在读取季度详情…";
  element<HTMLElement>("#library-detail-summary").replaceChildren();
  element<HTMLElement>("#library-audit").replaceChildren();
  element<HTMLElement>("#library-episodes").replaceChildren();
  element<HTMLElement>("#library-episode-status").textContent = "";
  element<HTMLButtonElement>("#library-detail-refresh").disabled = true;
  element<HTMLButtonElement>("#library-detail-external-import").disabled = true;
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
  setRegionState(list, "loading");
  element<HTMLElement>("#library-status").textContent = "正在读取作品库…";
  const query = new URLSearchParams({
    page: String(libraryState.page),
    page_size: String(libraryState.page_size),
    sort: libraryState.sort,
    direction: libraryState.direction,
  });
  if (libraryState.search) query.set("search", libraryState.search);
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
    renderRegionMessage(
      list,
      "error",
      `作品库读取失败：${errorMessage(error, "未知错误")}`,
    );
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

async function importExternalMedia(scope: "all" | "season"): Promise<void> {
  const detail = activeLibraryDetail;
  if (scope === "season" && !detail) return;
  const message = scope === "all"
    ? "手动扫描整个动画作品库？只读取标准的 <动画名>/Sxx/E###.<视频扩展名>，不会移动或删除文件。"
    : `手动扫描 ${detail!.display_name} / ${detail!.season_name}？只补录已通过 TMDB Episode snapshot 验证的标准视频文件。`;
  if (!window.confirm(message)) return;

  const globalButton = element<HTMLButtonElement>("#library-external-import");
  const seasonButton = element<HTMLButtonElement>("#library-detail-external-import");
  const refreshButton = element<HTMLButtonElement>("#library-detail-refresh");
  const deleteButton = element<HTMLButtonElement>("#library-detail-delete");
  const target = scope === "all"
    ? "#library-external-import-result"
    : "#library-detail-external-import-result";
  const resultContainer = element<HTMLElement>(target);
  if (scope === "season" && detail) {
    externalImportDetailIdentity = `${detail.tmdb_series_id}:${detail.tmdb_season_number}`;
  }
  globalButton.disabled = true;
  seasonButton.disabled = true;
  refreshButton.disabled = true;
  deleteButton.disabled = true;
  resultContainer.hidden = false;
  resultContainer.textContent = "正在扫描外部媒体并验证 TMDB Episode…";
  try {
    const url = scope === "all"
      ? "/api/v1/library/external-media/import"
      : `/api/v1/library/seasons/${detail!.tmdb_series_id}/${detail!.tmdb_season_number}/external-media/import`;
    const response = await fetch(url, { method: "POST", headers });
    if (!response.ok) throw new Error(await responseError(response));
    const result = await response.json() as ExternalMediaImportResult;
    renderExternalMediaImportResult(target, result);
    await loadLibrary();
    if (scope === "season" && detail) {
      await loadLibraryDetail(detail.tmdb_series_id, detail.tmdb_season_number);
    }
  } catch (error) {
    resultContainer.textContent = `外部媒体补录失败：${errorMessage(error, "未知错误")}`;
  } finally {
    globalButton.disabled = false;
    if (activeLibraryDetail) {
      seasonButton.disabled = false;
      refreshButton.disabled = false;
      deleteButton.disabled = false;
    }
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
  externalImportDetailIdentity = null;
  libraryState.active_series_id = null;
  libraryState.active_season_number = null;
  saveLibraryState();
  element<HTMLElement>("#library-detail").hidden = true;
  element<HTMLElement>("#library-detail-external-import-result").hidden = true;
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

function searchLibrary(event: SubmitEvent): void {
  event.preventDefault();
  libraryState.search = element<HTMLInputElement>("#library-search").value.trim();
  libraryState.page = 1;
  closeLibraryDetail();
  saveLibraryState();
  void loadLibrary();
}

function clearLibrarySearch(): void {
  libraryState.search = "";
  libraryState.page = 1;
  element<HTMLInputElement>("#library-search").value = "";
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

function cacheHoursLabel(hours: number): string {
  return hours === 0 ? "永久（不自动过期）" : `${hours} 小时`;
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
  const card = configurationCard("Mikan、TMDB 与季度失败链", [
    ["Mikan 地址", config.metadata.mikan.base_url],
    [
      "Mikan Episode 身份缓存",
      cacheHoursLabel(config.metadata.mikan.episode_identity_cache_hours),
    ],
    [
      "Mikan → Bangumi 映射缓存",
      cacheHoursLabel(config.metadata.mikan.bangumi_identity_cache_hours),
    ],
    ["TMDB API Key", config.editable.tmdb_api_key ?? "未配置"],
    ["TMDB Read Access Token", config.editable.tmdb_read_access_token ?? "未配置"],
    ["API / 语言", `${config.metadata.tmdb.base_url} · ${config.metadata.tmdb.language}`],
    ["超时", `${config.metadata.tmdb.http_timeout_seconds} 秒`],
    [
      "TMDB 重试",
      `${config.metadata.tmdb.retry_count} 次 · 间隔 `
      + `${config.metadata.tmdb.retry_delay_seconds} 秒`,
    ],
    ["TMDB 缓存", `${config.metadata.tmdb.cache_hours} 小时（仅缓存验证成功的响应）`],
    ["Bangumi API", config.metadata.bangumi.base_url],
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
    [
      "TMDB 成功时写 Bangumi ID",
      `${enabledLabel(config.metadata.write_bangumi_id_when_tmdb_matched)} · `
      + "默认关闭；关闭时仅 tmdbid=0 的 Bangumi 完全兜底写入 bangumiid",
    ],
  ]);
  card.append(seasonFailurePriority(config.metadata));
  return card;
}

async function loadConfiguration(): Promise<void> {
  const status = element<HTMLElement>("#configuration-status");
  const container = element<HTMLElement>("#configuration");
  status.textContent = "正在读取包含已保存凭据的生效配置…";
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
      configurationCard("全局选择性代理", [
        ["代理地址", config.outbound_proxy.url ?? "未配置（全部直连）"],
        [
          "代理域名",
          config.outbound_proxy.hosts.length === 0
            ? "未配置"
            : config.outbound_proxy.hosts.join("、"),
        ],
        ["匹配规则", "精确域名或 *.example.com；未命中的地址保持直连"],
      ]),
      metadataConfigurationCard(config),
      configurationCard("AI、偏移与 Torrent", [
        ["OpenAI API", config.metadata.ai.base_url ?? "未配置"],
        ["模型", config.metadata.ai.model ?? "未配置"],
        ["API Key", config.editable.ai_api_key ?? "未配置"],
        ["TMDB MCP", config.metadata.ai.tmdb_mcp_url],
        ["Bangumi MCP", config.metadata.ai.bangumi_mcp_url],
        [
          "AI 匹配",
          `任务级 ${enabledLabel(
            config.metadata.ai.use_metadata_match,
          )} · 单提示词 · `
          + `${config.metadata.ai.http_timeout_seconds} 秒`,
        ],
        [
          "AI Debug",
          `${enabledLabel(config.metadata.ai.debug_mode)} · 完整链路写入 data_path/ai-debug`,
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
      : `当前进程的生效值 · revision ${config.configuration_revision}；配置编辑器会回填已保存凭据。`;
  } catch (error) {
    currentConfiguration = null;
    container.replaceChildren();
    status.textContent = `配置读取失败：${errorMessage(error, "未知错误")}`;
  }
}

function configurationArchiveCountText(counts: ConfigurationArchiveCounts): string {
  return [
    `应用 ${counts.application}`,
    `下载器 ${counts.downloaders}`,
    `插件 ${counts.external_plugins}`,
    `来源 ${counts.sources}`,
    `RSS 规则 ${counts.rss_rule_sets}`,
    `五级过滤 ${counts.legacy_mikan_filters}`,
    `人工规则 ${counts.mikan_work_rules}`,
  ].join(" · ");
}

async function downloadConfigurationArchive(path: string, fallbackName: string): Promise<void> {
  const response = await fetch(path, { headers });
  if (!response.ok) throw new Error(await responseError(response));
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fallbackName;
    anchor.click();
  } finally {
    window.setTimeout(() => URL.revokeObjectURL(url), 0);
  }
}

function clearConfigurationArchivePreview(message?: string): void {
  pendingConfigurationArchivePreview = null;
  element<HTMLButtonElement>("#configuration-archive-import").disabled = true;
  const preview = element<HTMLElement>("#configuration-archive-preview-result");
  preview.hidden = true;
  preview.replaceChildren();
  if (message) element<HTMLElement>("#configuration-archive-status").textContent = message;
}

function renderConfigurationArchivePreview(preview: ConfigurationArchivePreview): void {
  const container = element<HTMLElement>("#configuration-archive-preview-result");
  const digest = document.createElement("p");
  digest.textContent = `SHA-256：${preview.sha256}`;
  const exported = document.createElement("p");
  exported.textContent = `导出时间：${new Date(preview.exported_at_utc).toLocaleString()}`;
  const counts = document.createElement("p");
  counts.textContent = configurationArchiveCountText(preview.counts);
  const warnings = document.createElement("ul");
  for (const warning of preview.warnings) {
    const item = document.createElement("li");
    item.textContent = warning;
    warnings.append(item);
  }
  container.replaceChildren(digest, exported, counts, warnings);
  container.hidden = false;
}

async function previewConfigurationArchive(): Promise<void> {
  const file = pendingConfigurationArchive;
  if (!file) return;
  const status = element<HTMLElement>("#configuration-archive-status");
  status.textContent = "正在校验归档格式、引用关系和配置值…";
  clearConfigurationArchivePreview();
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/configuration-archive/import/preview", {
      method: "POST",
      headers: requestHeaders,
      body: file,
    });
    if (!response.ok) throw new Error(await responseError(response));
    const preview = await response.json() as ConfigurationArchivePreview;
    pendingConfigurationArchivePreview = preview;
    renderConfigurationArchivePreview(preview);
    element<HTMLButtonElement>("#configuration-archive-import").disabled = false;
    status.textContent = "预检通过。确认导入前会自动创建一份本机安全备份。";
  } catch (error) {
    status.textContent = `预检失败：${errorMessage(error, "未知错误")}`;
  }
}

async function importConfigurationArchive(): Promise<void> {
  const file = pendingConfigurationArchive;
  const preview = pendingConfigurationArchivePreview;
  if (!file || !preview) return;
  if (!window.confirm("确认导入这份总配置？同 ID 配置会被覆盖，导入后需要重启主程序完整生效。")) return;
  const status = element<HTMLElement>("#configuration-archive-status");
  const button = element<HTMLButtonElement>("#configuration-archive-import");
  button.disabled = true;
  status.textContent = "正在创建安全备份并导入配置…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(
      `/api/v1/configuration-archive/import?expected_sha256=${encodeURIComponent(preview.sha256)}`,
      { method: "POST", headers: requestHeaders, body: file },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const result = await response.json() as ConfigurationArchiveApplyResult;
    status.textContent = `导入完成；安全备份 ${result.backup_id}。请重启主程序使全部配置生效。`;
    pendingConfigurationArchive = null;
    element<HTMLInputElement>("#configuration-archive-file").value = "";
    clearConfigurationArchivePreview();
    await Promise.all([loadConfigurationBackups(), loadConfiguration(), loadSources(), loadDownloaders()]);
  } catch (error) {
    status.textContent = `导入失败：${errorMessage(error, "未知错误")}`;
    button.disabled = false;
  }
}

async function loadConfigurationBackups(): Promise<void> {
  const container = element<HTMLElement>("#configuration-backup-list");
  container.setAttribute("aria-busy", "true");
  try {
    const response = await fetch("/api/v1/configuration-archive/backups", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const backups = await response.json() as ConfigurationArchiveBackup[];
    if (backups.length === 0) {
      const empty = document.createElement("p");
      empty.className = "muted empty";
      empty.textContent = "暂无总配置备份。";
      container.replaceChildren(empty);
      return;
    }
    container.replaceChildren(...backups.map(backup => {
      const item = document.createElement("article");
      item.className = "configuration-backup-item";
      const summary = document.createElement("div");
      const title = document.createElement("strong");
      title.textContent = backup.kind === "manual" ? "手动备份"
        : backup.kind === "pre-restore" ? "恢复前安全备份" : "导入前安全备份";
      const detail = document.createElement("small");
      detail.textContent = `${new Date(backup.created_at_utc).toLocaleString()} · ${formatBytes(backup.size_bytes)} · ${backup.sha256.slice(0, 12)}…`;
      summary.append(title, detail);
      const actions = document.createElement("div");
      const download = document.createElement("button");
      download.type = "button";
      download.className = "secondary-button";
      download.textContent = "下载";
      download.addEventListener("click", () => void downloadConfigurationArchive(
        `/api/v1/configuration-archive/backups/${encodeURIComponent(backup.id)}/download`,
        `${backup.id}.json`,
      ).catch(error => {
        element<HTMLElement>("#configuration-archive-status").textContent =
          `备份下载失败：${errorMessage(error, "未知错误")}`;
      }));
      const restore = document.createElement("button");
      restore.type = "button";
      restore.className = "primary-button";
      restore.textContent = "恢复";
      restore.addEventListener("click", () => void restoreConfigurationBackup(backup));
      const remove = document.createElement("button");
      remove.type = "button";
      remove.className = "danger-button";
      remove.textContent = "删除";
      remove.addEventListener("click", () => void deleteConfigurationBackup(backup));
      actions.append(download, restore, remove);
      item.append(summary, actions);
      return item;
    }));
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = `备份读取失败：${errorMessage(error, "未知错误")}`;
    container.replaceChildren(failed);
  } finally {
    container.setAttribute("aria-busy", "false");
  }
}

async function createConfigurationBackup(): Promise<void> {
  const status = element<HTMLElement>("#configuration-archive-status");
  status.textContent = "正在生成手动备份…";
  try {
    const response = await fetch("/api/v1/configuration-archive/backups", {
      method: "POST",
      headers,
    });
    if (!response.ok) throw new Error(await responseError(response));
    const backup = await response.json() as ConfigurationArchiveBackup;
    status.textContent = `已创建手动备份：${backup.id}`;
    await loadConfigurationBackups();
  } catch (error) {
    status.textContent = `备份失败：${errorMessage(error, "未知错误")}`;
  }
}

async function restoreConfigurationBackup(backup: ConfigurationArchiveBackup): Promise<void> {
  if (!window.confirm(`确认恢复 ${backup.id}？恢复前会再创建一份安全备份，完成后需要重启。`)) return;
  const status = element<HTMLElement>("#configuration-archive-status");
  status.textContent = "正在创建恢复前安全备份并应用配置…";
  try {
    const response = await fetch(
      `/api/v1/configuration-archive/backups/${encodeURIComponent(backup.id)}/restore`,
      { method: "POST", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const result = await response.json() as ConfigurationArchiveApplyResult;
    status.textContent = `恢复完成；恢复前安全备份 ${result.backup_id}。请重启主程序。`;
    await Promise.all([loadConfigurationBackups(), loadConfiguration(), loadSources(), loadDownloaders()]);
  } catch (error) {
    status.textContent = `恢复失败：${errorMessage(error, "未知错误")}`;
  }
}

async function deleteConfigurationBackup(backup: ConfigurationArchiveBackup): Promise<void> {
  if (!window.confirm(`确认永久删除备份 ${backup.id}？此操作不可恢复。`)) return;
  const status = element<HTMLElement>("#configuration-archive-status");
  try {
    const response = await fetch(
      `/api/v1/configuration-archive/backups/${encodeURIComponent(backup.id)}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    status.textContent = `已删除备份：${backup.id}`;
    await loadConfigurationBackups();
  } catch (error) {
    status.textContent = `删除失败：${errorMessage(error, "未知错误")}`;
  }
}

function configurationSecretLabel(state: "inherit" | "configured" | "cleared"): string {
  switch (state) {
    case "configured": return "当前私密覆盖：已配置并已回填";
    case "cleared": return "当前私密覆盖：已明确清除";
    default: return "当前私密覆盖：继承部署配置；有效值存在时已回填";
  }
}

function configurationSecretUpdateValue(id: string, current: string | null): string | null {
  const value = element<HTMLInputElement>(id).value;
  return value === (current ?? "") ? null : value || null;
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
  const clearAiKey = element<HTMLInputElement>("#configuration-ai-key-clear").checked;
  const key = element<HTMLInputElement>("#configuration-tmdb-key");
  const token = element<HTMLInputElement>("#configuration-tmdb-token");
  const aiKey = element<HTMLInputElement>("#configuration-ai-key");
  const keyLocked = activeConfigurationLockedFields.has("tmdb_api_key");
  const tokenLocked = activeConfigurationLockedFields.has("tmdb_read_access_token");
  const aiKeyLocked = activeConfigurationLockedFields.has("ai_api_key");
  key.disabled = keyLocked || clearKey;
  token.disabled = tokenLocked || clearToken;
  aiKey.disabled = aiKeyLocked || clearAiKey;
  element<HTMLInputElement>("#configuration-tmdb-key-clear").disabled = keyLocked;
  element<HTMLInputElement>("#configuration-tmdb-token-clear").disabled = tokenLocked;
  element<HTMLInputElement>("#configuration-ai-key-clear").disabled = aiKeyLocked;
  if (clearKey) key.value = "";
  if (clearToken) token.value = "";
  if (clearAiKey) aiKey.value = "";
}

const configurationLockSelectors: Record<string, string[]> = {
  outbound_proxy_url: ["#configuration-outbound-proxy-url"],
  outbound_proxy_hosts: ["#configuration-outbound-proxy-hosts"],
  mikan_base_url: ["#configuration-mikan-url"],
  mikan_episode_identity_cache_hours: ["#configuration-mikan-episode-cache-hours"],
  mikan_bangumi_identity_cache_hours: ["#configuration-mikan-bangumi-cache-hours"],
  tmdb_base_url: ["#configuration-tmdb-url"],
  tmdb_image_base_url: ["#configuration-tmdb-image-url"],
  tmdb_language: ["#configuration-tmdb-language"],
  tmdb_http_timeout_seconds: ["#configuration-tmdb-timeout"],
  tmdb_retry_count: ["#configuration-tmdb-retry-count"],
  tmdb_retry_delay_seconds: ["#configuration-tmdb-retry-delay"],
  tmdb_cache_hours: ["#configuration-tmdb-cache-hours"],
  tmdb_api_key: ["#configuration-tmdb-key", "#configuration-tmdb-key-clear"],
  tmdb_read_access_token: ["#configuration-tmdb-token", "#configuration-tmdb-token-clear"],
  bangumi_base_url: ["#configuration-bangumi-url"],
  bangumi_http_timeout_seconds: ["#configuration-bangumi-timeout"],
  bangumi_retry_count: ["#configuration-bangumi-retry-count"],
  bangumi_retry_delay_seconds: ["#configuration-bangumi-retry-delay"],
  ai_base_url: ["#configuration-ai-base-url"],
  ai_model: ["#configuration-ai-model"],
  ai_reasoning_effort: ["#configuration-ai-reasoning-effort"],
  ai_prompt_template: ["#configuration-ai-prompt-template", "#configuration-ai-prompt-reset"],
  ai_api_key: ["#configuration-ai-key", "#configuration-ai-key-clear"],
  ai_tmdb_mcp_url: ["#configuration-ai-tmdb-mcp-url"],
  ai_bangumi_mcp_url: ["#configuration-ai-bangumi-mcp-url"],
  ai_use_metadata_match: ["#configuration-ai-metadata"],
  ai_debug_mode: ["#configuration-ai-debug"],
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
        input.title = `由部署键 ${lock.controlling_keys.join(", ")} 控制`;
      } else {
        input.removeAttribute("title");
      }
    }
  }

  const summary = element<HTMLElement>("#configuration-lock-summary");
  if (locks.length === 0) {
    summary.textContent = "当前没有环境变量或命令行锁定的可编辑字段。";
    summary.className = "configuration-lock-summary muted";
    return;
  }

  summary.textContent = `以下字段由部署环境或命令行控制，Web 只读：${locks
    .map((lock) => `${lock.field} (${lock.controlling_keys.join(", ")})`)
    .join("；")}`;
  summary.className = "configuration-lock-summary active";
}

function openConfigurationEditor(): void {
  if (!currentConfiguration) return;
  clearConfigurationPreview();
  const editable = currentConfiguration.editable;
  setConfigurationValue(
    "#configuration-outbound-proxy-url",
    editable.outbound_proxy_url ?? "",
  );
  element<HTMLTextAreaElement>("#configuration-outbound-proxy-hosts").value =
    editable.outbound_proxy_hosts.join("\n");
  setConfigurationValue("#configuration-mikan-url", editable.mikan_base_url);
  setConfigurationValue(
    "#configuration-mikan-episode-cache-hours",
    editable.mikan_episode_identity_cache_hours,
  );
  setConfigurationValue(
    "#configuration-mikan-bangumi-cache-hours",
    editable.mikan_bangumi_identity_cache_hours,
  );
  setConfigurationValue("#configuration-tmdb-url", editable.tmdb_base_url);
  setConfigurationValue(
    "#configuration-tmdb-image-url",
    editable.tmdb_image_base_url,
  );
  setConfigurationValue("#configuration-tmdb-language", editable.tmdb_language);
  setConfigurationValue("#configuration-tmdb-timeout", editable.tmdb_http_timeout_seconds);
  setConfigurationValue("#configuration-tmdb-retry-count", editable.tmdb_retry_count);
  setConfigurationValue(
    "#configuration-tmdb-retry-delay",
    editable.tmdb_retry_delay_seconds,
  );
  setConfigurationValue("#configuration-tmdb-cache-hours", editable.tmdb_cache_hours);
  setConfigurationValue("#configuration-tmdb-key", editable.tmdb_api_key ?? "");
  setConfigurationChecked("#configuration-tmdb-key-clear", false);
  element<HTMLElement>("#configuration-tmdb-key-state").textContent =
    configurationSecretLabel(editable.tmdb_api_key_state);
  setConfigurationValue("#configuration-tmdb-token", editable.tmdb_read_access_token ?? "");
  setConfigurationChecked("#configuration-tmdb-token-clear", false);
  element<HTMLElement>("#configuration-tmdb-token-state").textContent =
    configurationSecretLabel(editable.tmdb_read_access_token_state);
  setConfigurationValue("#configuration-bangumi-url", editable.bangumi_base_url);
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
  setConfigurationValue("#configuration-ai-base-url", editable.ai_base_url ?? "");
  setConfigurationValue("#configuration-ai-model", editable.ai_model ?? "");
  setConfigurationValue(
    "#configuration-ai-reasoning-effort",
    editable.ai_reasoning_effort,
  );
  element<HTMLTextAreaElement>("#configuration-ai-prompt-template").value =
    editable.ai_prompt_template;
  element<HTMLElement>("#configuration-ai-prompt-status").textContent =
    `${currentConfiguration.metadata.ai.prompt_version} · ${currentConfiguration.metadata.ai.prompt_customized ? "自定义模板" : "程序默认模板"}；后台 Worker 与测试工具共用，保存后重启生效。`;
  setConfigurationValue("#configuration-ai-key", editable.ai_api_key ?? "");
  setConfigurationChecked("#configuration-ai-key-clear", false);
  element<HTMLElement>("#configuration-ai-key-state").textContent =
    configurationSecretLabel(editable.ai_api_key_state);
  setConfigurationValue("#configuration-ai-tmdb-mcp-url", editable.ai_tmdb_mcp_url);
  setConfigurationValue("#configuration-ai-bangumi-mcp-url", editable.ai_bangumi_mcp_url);
  setConfigurationChecked(
    "#configuration-ai-metadata",
    editable.ai_use_metadata_match,
  );
  setConfigurationChecked("#configuration-ai-debug", editable.ai_debug_mode);
  setConfigurationChecked("#configuration-bangumi-fallback", editable.tmdb_failure_use_bangumi);
  setConfigurationChecked(
    "#configuration-write-bangumi-with-tmdb",
    editable.write_bangumi_id_when_tmdb_matched,
  );
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
  outbound_proxy_url: "全局代理地址",
  outbound_proxy_hosts: "使用代理的域名",
  mikan_base_url: "Mikan 地址",
  mikan_episode_identity_cache_hours: "Mikan Episode 身份缓存（小时）",
  mikan_bangumi_identity_cache_hours: "Mikan → Bangumi 映射缓存（小时）",
  tmdb_base_url: "TMDB API 地址",
  tmdb_image_base_url: "TMDB 图片地址",
  tmdb_language: "TMDB 语言",
  tmdb_http_timeout_seconds: "TMDB 超时（秒）",
  tmdb_retry_count: "TMDB 额外重试次数",
  tmdb_retry_delay_seconds: "TMDB 重试间隔（秒）",
  tmdb_cache_hours: "TMDB 成功响应缓存（小时）",
  tmdb_api_key: "TMDB API Key",
  tmdb_read_access_token: "TMDB Read Token",
  bangumi_base_url: "Bangumi API 地址",
  bangumi_http_timeout_seconds: "Bangumi 超时（秒）",
  bangumi_retry_count: "Bangumi 额外重试次数",
  bangumi_retry_delay_seconds: "Bangumi 重试间隔（秒）",
  season_failure_skip: "TMDBFailSkip",
  season_failure_backtrace: "TMDBFailBacktrace",
  season_failure_use_title_season: "TMDBFailUseTitleSeason",
  season_failure_use_first_season: "TMDBFailUseFirstSeason",
  ai_base_url: "OpenAI-compatible API 地址",
  ai_model: "AI 模型",
  ai_reasoning_effort: "AI 推理程度",
  ai_prompt_template: "正式 AI Prompt",
  ai_api_key: "AI API Key",
  ai_tmdb_mcp_url: "TMDB MCP 地址",
  ai_bangumi_mcp_url: "Bangumi MCP 地址",
  ai_use_metadata_match: "AI 元数据匹配",
  ai_debug_mode: "AI Debug 完整链路",
  ai_http_timeout_seconds: "AI 超时（秒）",
  tmdb_failure_use_bangumi: "Bangumi 完全兜底",
  write_bangumi_id_when_tmdb_matched: "TMDB 成功时写 Bangumi ID",
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
    outbound_proxy_url:
      element<HTMLInputElement>("#configuration-outbound-proxy-url").value || null,
    outbound_proxy_hosts:
      element<HTMLTextAreaElement>("#configuration-outbound-proxy-hosts")
        .value.split(/[,;\r\n]+/u)
        .map(value => value.trim().toLowerCase())
        .filter(value => value.length > 0),
    mikan_base_url:
      element<HTMLInputElement>("#configuration-mikan-url").value,
    mikan_episode_identity_cache_hours:
      element<HTMLInputElement>("#configuration-mikan-episode-cache-hours").valueAsNumber,
    mikan_bangumi_identity_cache_hours:
      element<HTMLInputElement>("#configuration-mikan-bangumi-cache-hours").valueAsNumber,
    tmdb_base_url: element<HTMLInputElement>("#configuration-tmdb-url").value,
    tmdb_image_base_url:
      element<HTMLInputElement>("#configuration-tmdb-image-url").value,
    tmdb_language: element<HTMLInputElement>("#configuration-tmdb-language").value,
    tmdb_http_timeout_seconds:
      element<HTMLInputElement>("#configuration-tmdb-timeout").valueAsNumber,
    tmdb_retry_count:
      element<HTMLInputElement>("#configuration-tmdb-retry-count").valueAsNumber,
    tmdb_retry_delay_seconds:
      element<HTMLInputElement>("#configuration-tmdb-retry-delay").valueAsNumber,
    tmdb_cache_hours:
      element<HTMLInputElement>("#configuration-tmdb-cache-hours").valueAsNumber,
    tmdb_api_key: configurationSecretUpdateValue(
      "#configuration-tmdb-key",
      currentConfiguration.editable.tmdb_api_key,
    ),
    clear_tmdb_api_key:
      element<HTMLInputElement>("#configuration-tmdb-key-clear").checked,
    tmdb_read_access_token: configurationSecretUpdateValue(
      "#configuration-tmdb-token",
      currentConfiguration.editable.tmdb_read_access_token,
    ),
    clear_tmdb_read_access_token:
      element<HTMLInputElement>("#configuration-tmdb-token-clear").checked,
    bangumi_base_url:
      element<HTMLInputElement>("#configuration-bangumi-url").value,
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
    ai_base_url:
      element<HTMLInputElement>("#configuration-ai-base-url").value || null,
    ai_model:
      element<HTMLInputElement>("#configuration-ai-model").value || null,
    ai_reasoning_effort:
      element<HTMLSelectElement>("#configuration-ai-reasoning-effort").value as
        ConfigurationUpdatePayload["ai_reasoning_effort"],
    ai_prompt_template:
      element<HTMLTextAreaElement>("#configuration-ai-prompt-template").value,
    ai_api_key: configurationSecretUpdateValue(
      "#configuration-ai-key",
      currentConfiguration.editable.ai_api_key,
    ),
    clear_ai_api_key:
      element<HTMLInputElement>("#configuration-ai-key-clear").checked,
    ai_tmdb_mcp_url:
      element<HTMLInputElement>("#configuration-ai-tmdb-mcp-url").value,
    ai_bangumi_mcp_url:
      element<HTMLInputElement>("#configuration-ai-bangumi-mcp-url").value,
    ai_use_metadata_match:
      element<HTMLInputElement>("#configuration-ai-metadata").checked,
    ai_debug_mode:
      element<HTMLInputElement>("#configuration-ai-debug").checked,
    ai_http_timeout_seconds:
      element<HTMLInputElement>("#configuration-ai-timeout").valueAsNumber,
    tmdb_failure_use_bangumi:
      element<HTMLInputElement>("#configuration-bangumi-fallback").checked,
    write_bangumi_id_when_tmdb_matched:
      element<HTMLInputElement>("#configuration-write-bangumi-with-tmdb").checked,
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
  _sensitive: boolean,
): string {
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
  message.textContent = "正在验证并生成明文差异…";
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

async function resetConfigurationAiPrompt(): Promise<void> {
  const button = element<HTMLButtonElement>("#configuration-ai-prompt-reset");
  const status = element<HTMLElement>("#configuration-ai-prompt-status");
  button.disabled = true;
  try {
    aiTestDefaultPrompt ??=
      await api.get<AiMetadataTestPrompt>("/api/v1/ai-test/prompt");
    element<HTMLTextAreaElement>("#configuration-ai-prompt-template").value =
      aiTestDefaultPrompt.default_template;
    status.textContent =
      `已载入 ${aiTestDefaultPrompt.prompt_version} 程序默认模板；预览并保存后重启生效。`;
    clearConfigurationPreview("Prompt 已恢复为程序默认，请重新预览差异。");
  } catch (error) {
    status.textContent = `读取程序默认 Prompt 失败：${errorMessage(error, "未知错误")}`;
  } finally {
    button.disabled = activeConfigurationLockedFields.has("ai_prompt_template");
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

function formatDuration(totalSeconds: number): string {
  const seconds = Math.max(0, Math.floor(totalSeconds));
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  if (days > 0) return `${days}天 ${hours}小时`;
  if (hours > 0) return `${hours}小时 ${minutes}分钟`;
  if (minutes > 0) return `${minutes}分钟`;
  return `${seconds}秒`;
}

function seedingDescription(item: DownloadItem): string {
  if (item.seeding_target_minutes === 0) return "做种：不要求";
  const state = {
    waiting: "等待开始",
    seeding: "进行中",
    completed: "已完成",
    not_required: "不要求",
  }[item.seeding_state];
  const elapsed = formatDuration(item.seeding_elapsed_seconds);
  if (item.seeding_target_minutes === -1) {
    return `做种：${state} · 无限目标 · 已 ${elapsed}`;
  }
  const targetSeconds = item.seeding_target_minutes * 60;
  const percentage = Math.min(100, 100 * item.seeding_elapsed_seconds / targetSeconds);
  return `做种：${state} · ${elapsed} / ${formatDuration(targetSeconds)} · ${percentage.toFixed(1)}%`;
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
      if (stage.phase) {
        const phase = document.createElement("small");
        phase.className = "download-stage-phase";
        phase.textContent = organizationPhaseLabel(stage.phase)
          + (stage.total_units && stage.completed_units !== null
            ? ` · ${stage.completed_units}/${stage.total_units}`
            : "");
        group.append(phase);
        if (stage.total_units && stage.progress !== null) {
          const progress = document.createElement("progress");
          progress.className = "download-stage-progress";
          progress.max = 1;
          progress.value = Math.min(1, Math.max(0, stage.progress));
          progress.setAttribute("aria-label", `${label}：${phase.textContent}`);
          group.append(progress);
        }
      }
      stages.append(group);
    }
    const seedingGroup = document.createElement("div");
    const seedingTerm = document.createElement("dt");
    seedingTerm.textContent = "做种目标";
    const seedingValue = document.createElement("dd");
    seedingValue.textContent = seedingDescription(detail.summary)
      + (detail.summary.seeding_completed_at_utc
        ? ` · 完成于 ${new Date(detail.summary.seeding_completed_at_utc).toLocaleString()}`
        : "");
    seedingGroup.append(seedingTerm, seedingValue);
    stages.append(seedingGroup);
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

function organizationPhaseLabel(phase: string): string {
  return ({
    not_started: "尚未开始",
    rename_planning: "文件解析与重命名规划",
    media_transfer: "媒体移动或链接",
    subtitle_transfer: "字幕关联与移动",
    nfo_write: "NFO 写入",
    directory_index: "目录数据库与索引",
    cleanup_downloader: "下载器清理",
    completed: "整理完成",
  } as Record<string, string>)[phase] ?? phase;
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

function renderDownloadPage(body: DownloadListPage, background = false): void {
  renderDownloadSummary(body);
  const container = element<HTMLElement>("#downloads");
  const totalPages = Math.max(1, Math.ceil(body.total_items / body.page_size));
  element<HTMLElement>("#download-list-status").textContent =
    `${body.total_items} 个任务 · 第 ${body.page} 页`;
  element<HTMLElement>("#download-page-status").textContent =
    `第 ${body.page} / ${totalPages} 页`;
  element<HTMLButtonElement>("#download-previous").disabled = body.page <= 1;
  element<HTMLButtonElement>("#download-next").disabled = body.page >= totalPages;
  const renderSignature = JSON.stringify(body);
  const replaceContent = shouldReplacePolledRegion({
    background,
    signatureChanged: renderSignature !== downloadRenderSignature,
    hasExpandedContent: expandedDownloadJobIds.size > 0,
    hasFocusedEditor: hasFocusedEditorWithin("#download-tasks-workspace"),
    hasOpenDialog: hasOpenDialog(),
  });
  if (!replaceContent) {
    setRegionState(container, "ready");
    return;
  }
  const visibleAnchor = background
    ? Array.from(container.querySelectorAll<HTMLElement>(".download-card"))
      .find((card) => {
        const bounds = card.getBoundingClientRect();
        return bounds.bottom > 0 && bounds.top < window.innerHeight;
      })
    : undefined;
  const anchorJobId = visibleAnchor?.dataset.jobId ?? null;
  const anchorViewportTop = visibleAnchor?.getBoundingClientRect().top ?? null;
  if (body.items.length === 0) {
    downloadRenderSignature = renderSignature;
    renderRegionMessage(
      container,
      "empty",
      body.total_items === 0
        ? "暂无符合筛选条件的下载任务"
        : "当前页没有任务，请返回上一页。",
    );
    return;
  }

  renderRegionContent(container, ...body.items.map((item) => {
    const card = document.createElement("article");
    card.className = `download-card ${item.is_stale ? "stale" : ""}`;
    card.dataset.jobId = item.job_id;
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
    const seeding = document.createElement("p");
    seeding.className = `download-seeding ${item.seeding_state}`;
    seeding.textContent = seedingDescription(item);
    const dynamicTags = document.createElement("p");
    dynamicTags.className = `download-dynamic-tags ${item.dynamic_tag_state}`;
    dynamicTags.textContent = item.dynamic_tag_state === "applied"
      ? `动态 Tags：${item.dynamic_tags.join(", ")}`
      : item.dynamic_tag_state === "skipped"
      ? `动态 Tags：已跳过（${item.dynamic_tag_failure_code ?? "未知原因"}）`
      : item.dynamic_tag_state === "pending"
      ? "动态 Tags：等待元数据确认"
      : "动态 Tags：未配置";
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
    card.append(heading, progress, details, seeding, dynamicTags, actions, detailTarget);
    if (expandedDownloadJobIds.has(item.job_id)) {
      void loadDownloadDetail(item, detailTarget, expand);
    }
    return card;
  }));
  downloadRenderSignature = renderSignature;
  if (anchorJobId !== null && anchorViewportTop !== null) {
    const replacementAnchor = Array.from(
      container.querySelectorAll<HTMLElement>(".download-card"),
    ).find((card) => card.dataset.jobId === anchorJobId);
    if (replacementAnchor) {
      window.scrollBy(
        0,
        replacementAnchor.getBoundingClientRect().top - anchorViewportTop,
      );
    }
  }
}

async function loadDownloads(background = false): Promise<void> {
  const container = element<HTMLElement>("#downloads");
  const hadReadyContent = container.dataset.uiState === "ready";
  if (!background || !hadReadyContent) setRegionState(container, "loading");
  const requestSequence = ++downloadListRequestSequence;
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
    if (requestSequence !== downloadListRequestSequence) return;
    if (body.items.length === 0 && body.total_items > 0 && downloadState.page > 1) {
      downloadState.page = Math.max(1, Math.ceil(body.total_items / body.page_size));
      saveDownloadState();
      await loadDownloads(background);
      return;
    }
    downloadState.page = body.page;
    downloadState.page_size = body.page_size as 10 | 25 | 50;
    saveDownloadState();
    renderDownloadPage(body, background);
  } catch (error) {
    if (requestSequence !== downloadListRequestSequence) return;
    if (background && hadReadyContent) {
      setRegionState(container, "ready");
      element<HTMLElement>("#download-list-status").textContent =
        `自动刷新失败，保留当前列表：${errorMessage(error, "未知错误")}`;
      return;
    }
    renderRegionMessage(
      container,
      "error",
      `下载状态读取失败：${errorMessage(error, "未知错误")}`,
    );
    element<HTMLElement>("#download-list-status").textContent = "下载任务读取失败";
  }
}

async function loadTrustedOffsets(): Promise<void> {
  const container = element<HTMLElement>("#trusted-offsets");
  setRegionState(container, "loading");
  try {
    const response = await fetch("/api/v1/mikan/trusted-offsets", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as { items: MikanTrustedOffsetItem[] };
    if (body.items.length === 0) {
      renderRegionMessage(container, "empty", "暂无自动 offset 学习证据");
      return;
    }

    renderRegionContent(container, ...body.items.map((item) => {
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
    renderRegionMessage(
      container,
      "error",
      `可信 offset 读取失败：${errorMessage(error, "未知错误")}`,
    );
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
      input.disabled = groupTargets.length === 0
        || (group.flag === "delete_task_record" && !activeDeletePreview.task_record_deletion_allowed);
      input.addEventListener("change", () => {
        if (group.flag === "delete_task_record" && input.checked
          && activeDeletePreview?.downloader_tasks.length) {
          const downloader = selectedDeleteInput("delete_downloader_task");
          if (downloader && !downloader.disabled) downloader.checked = true;
        }
        updateDeleteConfirm();
      });
      const text = document.createElement("span");
      const strong = document.createElement("strong");
      strong.textContent = `${group.label} · ${groupTargets.length} 项`;
      const small = document.createElement("small");
      small.textContent = group.flag === "delete_task_record"
        && !activeDeletePreview.task_record_deletion_allowed
        ? activeDeletePreview.task_record_deletion_denial_reason ?? group.help
        : group.help;
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
    delete_task_record: false,
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

const bangumiFallbackDenialLabels: Record<string, string> = {
  tmdb_access_not_attempted: "尚未访问 TMDB",
  tmdb_access_not_confirmed: "TMDB 权威访问未确认（网络、服务、认证、配置或协议失败）",
  bangumi_subject_missing: "缺少有效 bgmid",
  bangumi_fallback_disabled: "Bangumi 完全兜底开关未启用",
  tmdb_series_resolved: "已经取得有效 TMDB Series；完全兜底不适用",
  metadata_lease_expired: "解析租约过期，必须重新匹配",
  tmdb_episode_validation_failed: "TMDB Episode 验证失败；不能降级为完全兜底",
  bangumi_fallback_pending: "满足前置条件，等待 Bangumi 完全兜底",
};

function metadataFallbackDecision(item: MetadataItem): HTMLParagraphElement | null {
  if (item.latest_run_status !== "failed"
    && item.latest_run_status !== "fallback_resolved") {
    return null;
  }

  const decision = document.createElement("p");
  const used = item.latest_run_status === "fallback_resolved";
  const eligible = item.bangumi_fallback_eligible === true;
  decision.className = `metadata-fallback-decision ${used || eligible ? "allowed" : "denied"}`;
  if (used) {
    decision.textContent =
      "Bangumi 完全兜底：已允许并使用 · TMDB 权威访问已确认 · 固定本地 S01 · 不提供有效 tmdbid";
    return decision;
  }

  const access = item.tmdb_access_confirmed === true
    ? "TMDB 权威访问已确认"
    : "TMDB 权威访问未确认";
  const eligibility = eligible ? "允许" : "拒绝";
  const reason = item.bangumi_fallback_denial_reason === null
    ? "未记录原因"
    : bangumiFallbackDenialLabels[item.bangumi_fallback_denial_reason]
      ?? item.bangumi_fallback_denial_reason;
  decision.textContent = `Bangumi 完全兜底：${eligibility} · ${access} · ${reason}`;
  return decision;
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

async function readaptOtherFiles(taskId: string, button: HTMLButtonElement): Promise<void> {
  const defaultLabel = "重新适配 Other";
  button.disabled = true;
  button.textContent = "读取预览…";
  try {
    const previewResponse = await fetch(
      `/api/v1/metadata/tasks/${encodeURIComponent(taskId)}/other-readaptation/preview`,
      { headers },
    );
    if (!previewResponse.ok) throw new Error(await responseError(previewResponse));
    const preview = await previewResponse.json() as OtherFileReadaptationPreview;
    if (!preview.eligible) {
      throw new Error(preview.reason ?? "当前任务不能重新适配 Other 文件");
    }

    const files = preview.files
      .map((file) => `• ${file.source_name}\n  当前原因：${file.other_reason}`
        + (file.shared_path_reference_count > 1
          ? `\n  与 ${file.shared_path_reference_count} 个任务共享：本次将复制文件，不移动源文件`
          : ""))
      .join("\n");
    if (!window.confirm(
      `将重新适配 ${preview.files.length} 个 Other 文件。\n\n`
      + "从保存的 Mikan Episode 来源页重新解析 mikanid / groupid / bgmid；跳过旧的 Mikan、Bangumi、TMDB 匹配缓存，重新执行 Series、Season、Episode。需要 AI 时会真实调用 AI。\n\n"
      + "仅复用已经下载或整理的实体文件，不重复下载、不操作 qBittorrent；历史任务、策略时间线和 AI 日志保留。完成后必须人工审核，才能删除任务记录。\n\n"
      + files,
    )) {
      button.disabled = false;
      button.textContent = defaultLabel;
      return;
    }

    button.textContent = "重新入队中…";
    const response = await fetch(
      `/api/v1/metadata/tasks/${encodeURIComponent(taskId)}/other-readaptation`,
      { method: "POST", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    await loadMetadataTasks();
  } catch (error) {
    button.disabled = false;
    button.textContent = errorMessage(error, "Other 重新适配失败");
    window.setTimeout(() => {
      if (button.isConnected) button.textContent = defaultLabel;
    }, 5000);
  }
}

const readaptationDispositionLabels: Record<string, string> = {
  episode: "正片",
  duplicate: "重复跳过",
  other: "Other",
  pending: "待处理",
  ignored: "忽略",
  subtitle: "字幕",
};

function readaptationDisposition(value: string): string {
  return readaptationDispositionLabels[value] ?? value;
}

function readaptationIdentity(
  prefix: "TMDB" | "S" | "E",
  value: number | null,
  name: string | null,
): string {
  if (value === null) return "未知（旧批次可能未固化）";
  const number = prefix === "TMDB" ? String(value) : String(value).padStart(2, "0");
  return `${prefix} ${number}${name ? ` · ${name}` : ""}`;
}

function readaptationSummaryItem(label: string, value: string): HTMLDivElement {
  const item = document.createElement("div");
  const term = document.createElement("dt");
  const detail = document.createElement("dd");
  term.textContent = label;
  detail.textContent = value;
  item.append(term, detail);
  return item;
}

function readaptationCell(value: string, code = false): HTMLTableCellElement {
  const cell = document.createElement("td");
  if (code) {
    const content = document.createElement("code");
    content.textContent = value;
    cell.append(content);
  } else {
    cell.textContent = value;
  }
  return cell;
}

function appendReadaptationRow(
  body: HTMLTableSectionElement,
  label: string,
  before: string,
  after: string,
  code = false,
): void {
  const row = document.createElement("tr");
  const heading = document.createElement("th");
  heading.scope = "row";
  heading.textContent = label;
  row.append(heading, readaptationCell(before, code), readaptationCell(after, code));
  body.append(row);
}

function readaptationOtherAction(
  file: OtherFileReadaptationReviewPreview["files"][number],
): string {
  if (file.after_disposition === "episode") {
    return file.preserved_shared_source
      ? "共享源保留；复制到规范 Episode 路径"
      : "独占文件迁出 Other；旧 Other 路径随移动消失";
  }
  if (file.after_other_reason === "episode_already_completed"
      || file.after_other_reason === "episode_claimed_by_another_task") {
    return "目标 Episode 已完成或被占用；保留当前 Other，不自动删除";
  }
  return "仍归类为 Other；保留当前文件，不自动删除";
}

function readaptationNumberInput(
  label: string,
  value: number | null,
): { field: HTMLLabelElement; input: HTMLInputElement } {
  const field = document.createElement("label");
  field.textContent = label;
  const input = document.createElement("input");
  input.type = "number";
  input.min = "1";
  input.step = "1";
  input.required = true;
  input.value = value === null ? "" : String(value);
  field.append(input);
  return { field, input };
}

function createReadaptationManualOverride(
  preview: OtherFileReadaptationReviewPreview,
  file: OtherFileReadaptationReviewPreview["files"][number],
): HTMLFormElement {
  const form = document.createElement("form");
  form.className = "readaptation-manual-override";
  const heading = document.createElement("strong");
  heading.textContent = "人工修正 TMDB 归属";
  const fields = document.createElement("div");
  fields.className = "readaptation-manual-fields";
  const series = readaptationNumberInput(
    "TMDB Series ID",
    file.after_tmdb_series_id ?? file.before_tmdb_series_id);
  const season = readaptationNumberInput(
    "Season",
    file.after_tmdb_season_number ?? file.before_tmdb_season_number);
  const episode = readaptationNumberInput(
    "Episode",
    file.after_tmdb_episode_number ?? file.before_tmdb_episode_number);
  const submit = document.createElement("button");
  submit.type = "submit";
  submit.className = "primary-button";
  submit.textContent = "验证并重新整理";
  fields.append(series.field, season.field, episode.field, submit);
  const note = document.createElement("p");
  note.className = "muted";
  note.textContent = "服务端会重新验证 TMDB Series / Season / Episode。新目标未占用时安全迁出或复制；目标已完成时继续保留 Other，不自动删除。";
  form.append(heading, fields, note);
  form.addEventListener("submit", event => {
    event.preventDefault();
    if (otherReadaptationReviewSubmitting) return;
    otherReadaptationReviewSubmitting = true;
    submit.disabled = true;
    submit.textContent = "正在验证 TMDB…";
    const message = element<HTMLElement>("#other-readaptation-review-message");
    message.textContent = "正在验证 TMDB 身份；验证通过前不会修改任务或文件。";
    void (async () => {
      try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch(
          `/api/v1/metadata/tasks/${encodeURIComponent(preview.task_id)}`
            + `/other-readaptation/review/files/${encodeURIComponent(file.task_file_id)}`
            + "/manual-override",
          {
            method: "POST",
            headers: requestHeaders,
            body: JSON.stringify({
              tmdb_series_id: Number(series.input.value),
              tmdb_season_number: Number(season.input.value),
              tmdb_episode_number: Number(episode.input.value),
            }),
          },
        );
        if (!response.ok) throw new Error(await responseError(response));
        const result = await response.json() as { result: string };
        if (result.result === "organization_queued") {
          otherReadaptationReviewDialog.close();
          await loadMetadataTasks();
          return;
        }
        const refreshedResponse = await fetch(
          `/api/v1/metadata/tasks/${encodeURIComponent(preview.task_id)}`
            + "/other-readaptation/review",
          { headers },
        );
        if (!refreshedResponse.ok) throw new Error(await responseError(refreshedResponse));
        const refreshed = await refreshedResponse.json() as OtherFileReadaptationReviewPreview;
        if (activeOtherReadaptationReview !== null) {
          activeOtherReadaptationReview.preview = refreshed;
        }
        renderOtherReadaptationReview(refreshed);
        element<HTMLElement>("#other-readaptation-review-message").textContent =
          "TMDB 已验证；目标 Episode 已完成或被占用，因此当前 Other 文件已保留，等待人工审核。";
      } catch (error) {
        message.textContent = errorMessage(error, "人工修正失败");
        submit.disabled = false;
        submit.textContent = "验证并重新整理";
      } finally {
        otherReadaptationReviewSubmitting = false;
      }
    })();
  });
  return form;
}

function renderOtherReadaptationReview(preview: OtherFileReadaptationReviewPreview): void {
  const summary = element<HTMLDListElement>("#other-readaptation-review-summary");
  const episodeCount = preview.files.filter(file => file.after_disposition === "episode").length;
  const otherCount = preview.files.filter(file => file.after_disposition === "other").length;
  const duplicateCount = preview.files.filter(file =>
    file.after_disposition === "duplicate"
    || file.after_other_reason === "episode_already_completed"
    || file.after_other_reason === "episode_claimed_by_another_task").length;
  const completionLabels: Record<OtherFileReadaptationReviewPreview["completion_status"], string> = {
    processing: "重新适配执行中",
    awaiting_review: "执行完成 · 等待人工审核",
    review_completed: "执行完成 · 人工审核完成",
  };
  summary.replaceChildren(
    readaptationSummaryItem("任务", preview.title),
    readaptationSummaryItem("任务 ID", preview.task_id),
    readaptationSummaryItem("完成后状态", completionLabels[preview.completion_status]),
    readaptationSummaryItem("最终文件", `正片 ${episodeCount} · Other ${otherCount} · 重复目标 ${duplicateCount}`),
    readaptationSummaryItem("开始", libraryDate(preview.requested_at_utc, true)),
    readaptationSummaryItem("执行完成", libraryDate(preview.completed_at_utc, true)),
    readaptationSummaryItem("审核完成", libraryDate(preview.reviewed_at_utc, true)),
  );

  const files = element<HTMLDivElement>("#other-readaptation-review-files");
  const cards = preview.files.map((file, index) => {
    const card = document.createElement("section");
    card.className = "readaptation-review-file";
    const title = document.createElement("h3");
    title.textContent = `文件 ${index + 1} / ${preview.files.length} · ${file.source_name}`;
    const wrapper = document.createElement("div");
    wrapper.className = "readaptation-review-table-wrap";
    const table = document.createElement("table");
    table.className = "readaptation-review-table";
    const head = document.createElement("thead");
    const headRow = document.createElement("tr");
    for (const label of ["信息项", "适配前", "适配后"]) {
      const heading = document.createElement("th");
      heading.scope = "col";
      heading.textContent = label;
      headRow.append(heading);
    }
    head.append(headRow);
    const body = document.createElement("tbody");

    const dispositionRow = document.createElement("tr");
    const dispositionHeading = document.createElement("th");
    dispositionHeading.scope = "row";
    dispositionHeading.textContent = "归类";
    const beforeDisposition = document.createElement("td");
    const beforeBadge = document.createElement("span");
    beforeBadge.className = `readaptation-review-disposition ${file.before_disposition}`;
    beforeBadge.textContent = readaptationDisposition(file.before_disposition);
    beforeDisposition.append(beforeBadge);
    const afterDisposition = document.createElement("td");
    const afterBadge = document.createElement("span");
    afterBadge.className = `readaptation-review-disposition ${file.after_disposition}`;
    afterBadge.textContent = readaptationDisposition(file.after_disposition);
    afterDisposition.append(afterBadge);
    dispositionRow.append(dispositionHeading, beforeDisposition, afterDisposition);
    body.append(dispositionRow);

    appendReadaptationRow(body, "TMDB Series",
      readaptationIdentity("TMDB", file.before_tmdb_series_id, file.before_series_name),
      readaptationIdentity("TMDB", file.after_tmdb_series_id, file.after_series_name));
    appendReadaptationRow(body, "Season",
      readaptationIdentity("S", file.before_tmdb_season_number, file.before_season_name),
      readaptationIdentity("S", file.after_tmdb_season_number, file.after_season_name));
    appendReadaptationRow(body, "Episode",
      readaptationIdentity("E", file.before_tmdb_episode_number, file.before_episode_name),
      readaptationIdentity("E", file.after_tmdb_episode_number, file.after_episode_name));
    appendReadaptationRow(body, "原因",
      file.before_other_reason || "未记录",
      file.after_other_reason || "无",
      true);
    appendReadaptationRow(body, "媒体位置",
      file.before_media_path,
      file.after_media_path || "未生成新的整理目标",
      true);
    appendReadaptationRow(body, "Episode 取得",
      "原 Other，未取得",
      libraryStrategy(file.after_episode_strategy));
    appendReadaptationRow(body, "共享文件",
      file.preserved_shared_source ? "是" : "否",
      file.preserved_shared_source
        ? "复制整理并保留共享源文件"
        : "不需要共享复制");
    appendReadaptationRow(body, "Other 处理", "原文件位于 Other", readaptationOtherAction(file));

    table.append(head, body);
    wrapper.append(table);
    card.append(title, wrapper);
    if (preview.review_state === "pending"
        && preview.task_status === "organized"
        && file.after_disposition === "other") {
      card.append(createReadaptationManualOverride(preview, file));
    }
    return card;
  });
  files.replaceChildren(...cards);
  element<HTMLElement>("#other-readaptation-review-message").textContent = "";
}

async function approveOtherReadaptation(taskId: string, button: HTMLButtonElement): Promise<void> {
  const defaultLabel = button.textContent ?? "确认人工审核";
  button.disabled = true;
  button.textContent = "读取前后对照…";
  try {
    const previewResponse = await fetch(
      `/api/v1/metadata/tasks/${encodeURIComponent(taskId)}/other-readaptation/review`,
      { headers },
    );
    if (!previewResponse.ok) throw new Error(await responseError(previewResponse));
    const preview = await previewResponse.json() as OtherFileReadaptationReviewPreview;
    activeOtherReadaptationReview = { preview, triggerButton: button, defaultLabel };
    renderOtherReadaptationReview(preview);
    button.textContent = "审核窗口已打开";
    otherReadaptationReviewConfirm.hidden = preview.review_state !== "pending";
    otherReadaptationReviewConfirm.disabled = preview.review_state !== "pending";
    otherReadaptationReviewCancel.disabled = false;
    otherReadaptationReviewCancel.textContent = preview.review_state === "pending" ? "返回检查" : "关闭";
    otherReadaptationReviewDialog.showModal();
  } catch (error) {
    button.disabled = false;
    button.textContent = errorMessage(error, "审核对照读取失败");
    window.setTimeout(() => {
      if (button.isConnected) button.textContent = defaultLabel;
    }, 5000);
  }
}

async function confirmOtherReadaptationReview(): Promise<void> {
  const active = activeOtherReadaptationReview;
  if (active === null || otherReadaptationReviewSubmitting) return;
  otherReadaptationReviewSubmitting = true;
  otherReadaptationReviewConfirm.disabled = true;
  otherReadaptationReviewCancel.disabled = true;
  otherReadaptationReviewConfirm.textContent = "确认中…";
  const message = element<HTMLElement>("#other-readaptation-review-message");
  message.textContent = "正在写入人工审核结果…";
  try {
    const response = await fetch(
      `/api/v1/metadata/tasks/${encodeURIComponent(active.preview.task_id)}/other-readaptation/review`,
      { method: "POST", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    message.textContent = "人工审核已确认。";
    otherReadaptationReviewDialog.close();
    await loadMetadataTasks();
  } catch (error) {
    message.textContent = errorMessage(error, "审核确认失败");
    otherReadaptationReviewConfirm.disabled = false;
    otherReadaptationReviewCancel.disabled = false;
  } finally {
    otherReadaptationReviewSubmitting = false;
    otherReadaptationReviewConfirm.textContent = "确认审核通过";
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
    if (detail.ai.model !== null) {
      const usage = document.createElement("p");
      usage.className = "metadata-ai-usage";
      usage.textContent =
        `模型 ${detail.ai.model} · Prompt ${textOrDash(detail.ai.prompt_tokens)}`
        + ` · Completion ${textOrDash(detail.ai.completion_tokens)}`
        + ` · Total ${textOrDash(detail.ai.total_tokens)}`
        + ` · HTTP ${textOrDash(detail.ai.request_count)}`
        + ` · Tool calls ${textOrDash(detail.ai.tool_call_count)}`;
      ai.append(usage);
    }
    if (detail.ai.error_code || detail.ai.reason) {
      const reason = document.createElement("p");
      reason.className = "metadata-detail-reason";
      reason.textContent =
        `${textOrDash(detail.ai.error_code)} · ${textOrDash(detail.ai.reason)}`;
      ai.append(reason);
    }

    const sourceEvidence = document.createElement("section");
    sourceEvidence.className = "metadata-source-evidence";
    const sourceHeading = document.createElement("h4");
    sourceHeading.textContent = "来源持久证据（不作为 TMDB 规范字段）";
    const sourceTitle = document.createElement("strong");
    sourceTitle.textContent = detail.source_evidence.source_title;
    const sourceRoute = document.createElement("p");
    sourceRoute.textContent =
      `${detail.source_evidence.source_id} / ${detail.source_evidence.source_profile_id}`
      + ` rev ${detail.source_evidence.source_profile_revision}`;
    const sourceIds = document.createElement("p");
    sourceIds.textContent =
      `mikanid ${detail.source_evidence.mikanid ?? "—"} · groupid ${detail.source_evidence.groupid ?? "—"}`
      + ` · bgmid ${detail.source_evidence.bgmid ?? "—"} · AniDB ${detail.source_evidence.anidbid ?? "—"}`
      + ` · IMDb ${detail.source_evidence.imdbid ?? "—"}`;
    const sourceOpaqueIds = document.createElement("p");
    sourceOpaqueIds.textContent =
      `来源条目指纹 ${detail.source_evidence.source_item_id_fingerprint?.slice(0, 12) ?? "—"}`
      + ` · 来源作品指纹 ${detail.source_evidence.source_work_id_fingerprint?.slice(0, 12) ?? "—"}`;
    const sourcePublished = document.createElement("p");
    sourcePublished.textContent = detail.source_evidence.published_at
      ? `来源发布时间 ${new Date(detail.source_evidence.published_at).toLocaleString()}`
      : detail.source_evidence.published_at_raw_available
        ? "来源发布时间原文已保存，但没有可靠的规范时间"
        : "没有来源发布时间证据";
    sourceEvidence.append(
      sourceHeading,
      sourceTitle,
      sourceRoute,
      sourceIds,
      sourceOpaqueIds,
      sourcePublished,
    );

    const rssEvidence = document.createElement("section");
    rssEvidence.className = "metadata-rss-evidence";
    if (detail.rss_evidence.length > 0) {
      const rssHeading = document.createElement("h4");
      rssHeading.textContent = "RSS 入口与文件候选审计";
      const rssExplanation = document.createElement("p");
      rssExplanation.className = "muted";
      rssExplanation.textContent =
        "按持久化关联展示 RSS 批次、筛选决策和统一导入任务；下方逐文件候选来自该任务实际 Torrent 文件名解析。";
      rssEvidence.append(rssHeading, rssExplanation);
      for (const evidence of detail.rss_evidence) {
        const row = document.createElement("article");
        row.className = "metadata-rss-evidence-row";
        const heading = document.createElement("div");
        const title = document.createElement("strong");
        title.textContent =
          `${evidence.source_profile_id} · batch ${evidence.batch_id.slice(0, 12)}… · entry ${evidence.entry_ordinal}`;
        title.title = `batch_id=${evidence.batch_id}\nentry_ordinal=${evidence.entry_ordinal}`;
        const state = document.createElement("span");
        state.className = `badge ${evidence.effect_state === "ingested" ? "ready" : ""}`;
        state.textContent = evidence.effect_state;
        heading.append(title, state);
        const identity = document.createElement("p");
        identity.textContent =
          `mikanid ${evidence.mikanid ?? "—"} · RSS EP ${textOrDash(evidence.source_episode_kind)}:${textOrDash(evidence.source_episode)} · ${new Date(evidence.batch_created_at_utc).toLocaleString()}`;
        const rules = document.createElement("p");
        rules.textContent =
          `规则 rev ${evidence.rule_revision}（优选${evidence.priority_enabled ? "开启" : "关闭"}） · Legacy rev ${evidence.legacy_filter_revision}（${evidence.legacy_filter_enabled ? "开启" : "关闭"}）`;
        const decision = document.createElement("p");
        decision.textContent =
          `${evidence.decision_kind} · ${evidence.decision_reason} · 有序规则组 ${evidence.evaluated_priority_groups.length === 0 ? "未执行" : evidence.evaluated_priority_groups.join(" → ")}`;
        const legacy = document.createElement("p");
        legacy.textContent =
          `Legacy ${evidence.legacy_filter_state} · ${evidence.legacy_filter_reason} · ${textOrDash(evidence.legacy_filter_scope)} · identity ${evidence.identity_mikanid ?? "—"}/${evidence.identity_groupid ?? "—"}`;
        row.append(heading, identity, rules, decision, legacy);
        rssEvidence.append(row);
      }
    }

    const nfoRewrites = document.createElement("section");
    nfoRewrites.className = "metadata-nfo-rewrites";
    if (detail.nfo_rewrites.length > 0) {
      const nfoHeading = document.createElement("h4");
      nfoHeading.textContent = "TMDB 恢复后的 NFO 重写";
      nfoRewrites.append(nfoHeading);
      const stateLabels: Record<string, string> = {
        pending: "等待写入",
        writing: "正在写入",
        failed: "失败，等待自动重试",
        completed: "已完成",
      };
      for (const rewrite of detail.nfo_rewrites) {
        const row = document.createElement("article");
        row.className = `metadata-nfo-rewrite ${rewrite.state}`;
        const heading = document.createElement("div");
        const title = document.createElement("strong");
        title.textContent = `TMDB ${rewrite.tmdb_series_id} · bgmid ${rewrite.bgmid}`;
        const state = document.createElement("span");
        state.className = `badge ${rewrite.state === "failed" ? "error" : "ready"}`;
        state.textContent = stateLabels[rewrite.state] ?? rewrite.state;
        heading.append(title, state);
        const audit = document.createElement("p");
        audit.textContent = `已尝试 ${rewrite.attempt_count} 次 · 更新 ${new Date(rewrite.updated_at_utc).toLocaleString()}`;
        row.append(heading, audit);
        if (rewrite.failure_code !== null || rewrite.next_attempt_at_utc !== null) {
          const retry = document.createElement("p");
          retry.className = "metadata-detail-reason";
          retry.textContent = `${textOrDash(rewrite.failure_code)} · 下次重试 ${rewrite.next_attempt_at_utc === null ? "—" : new Date(rewrite.next_attempt_at_utc).toLocaleString()}`;
          row.append(retry);
        }
        if (rewrite.completed_at_utc !== null) {
          const completed = document.createElement("p");
          completed.textContent = `完成于 ${new Date(rewrite.completed_at_utc).toLocaleString()}`;
          row.append(completed);
        }
        nfoRewrites.append(row);
      }
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
        if (file.episode_strategy) {
          const evidence = document.createElement("p");
          evidence.className = "metadata-resolution-evidence";
          evidence.textContent =
            `Episode 取得：${libraryStrategy(file.episode_strategy)} · `
            + resolutionReference(file.episode_run_id, file.episode_attempt_id);
          evidence.title =
            `run_id=${file.episode_run_id ?? "未记录"}\n`
            + `attempt_id=${file.episode_attempt_id ?? "未记录"}`;
          canonical.append(evidence);
        }
        row.append(source, arrow, canonical);
        files.append(row);
      }
    }

    target.replaceChildren(
      sourceEvidence,
      ai,
      ...(detail.rss_evidence.length > 0 ? [rssEvidence] : []),
      ...(detail.nfo_rewrites.length > 0 ? [nfoRewrites] : []),
      files,
    );
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
        if (attempt.ai_model !== null) {
          const usage = document.createElement("p");
          usage.className = "metadata-ai-usage";
          usage.textContent =
            `AI ${attempt.ai_model} · Prompt ${textOrDash(attempt.ai_prompt_tokens)}`
            + ` · Completion ${textOrDash(attempt.ai_completion_tokens)}`
            + ` · Total ${textOrDash(attempt.ai_total_tokens)}`
            + ` · HTTP ${textOrDash(attempt.ai_request_count)}`
            + ` · Tool calls ${textOrDash(attempt.ai_tool_call_count)}`;
          row.append(usage);
        }
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

async function loadMetadataTasks(background = false): Promise<void> {
  const container = element<HTMLElement>("#metadata-tasks");
  const hadReadyContent = container.dataset.uiState === "ready";
  if (!background || !hadReadyContent) setRegionState(container, "loading");
  const requestSequence = ++metadataListRequestSequence;
  const query = new URLSearchParams({
    page: String(metadataState.page),
    page_size: String(metadataState.page_size),
    handling: metadataState.handling,
    file_state: metadataState.file_state,
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
    if (requestSequence !== metadataListRequestSequence) return;
    if (body.items.length === 0 && body.total_items > 0 && metadataState.page > 1) {
      metadataState.page = Math.max(1, Math.ceil(body.total_items / body.page_size));
      saveMetadataState();
      await loadMetadataTasks(background);
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
    const renderSignature = JSON.stringify(body);
    const replaceContent = shouldReplacePolledRegion({
      background,
      signatureChanged: renderSignature !== metadataRenderSignature,
      hasExpandedContent:
        expandedMetadataTaskIds.size > 0 || expandedMetadataDetailIds.size > 0,
      hasFocusedEditor: hasFocusedEditorWithin("#metadata-tasks-workspace"),
      hasOpenDialog: hasOpenDialog(),
    });
    if (!replaceContent) {
      setRegionState(container, "ready");
      return;
    }
    const visibleAnchor = background
      ? Array.from(container.querySelectorAll<HTMLElement>(".metadata-card"))
        .find((card) => {
          const bounds = card.getBoundingClientRect();
          return bounds.bottom > 0 && bounds.top < window.innerHeight;
        })
      : undefined;
    const anchorTaskId = visibleAnchor?.dataset.taskId ?? null;
    const anchorViewportTop = visibleAnchor?.getBoundingClientRect().top ?? null;
    if (body.items.length === 0) {
      metadataRenderSignature = renderSignature;
      renderRegionMessage(
        container,
        "empty",
        "暂无符合筛选条件的元数据任务",
      );
      return;
    }

    const cards = body.items.map((item) => {
      const card = document.createElement("article");
      card.className = `metadata-card ${item.status === "metadata_failed" ? "failed" : ""}`;
      card.dataset.taskId = item.task_id;
      const heading = document.createElement("div");
      heading.className = "metadata-heading";
      const title = document.createElement("strong");
      title.textContent = item.title;
      const state = document.createElement("span");
      state.className = `badge ${item.status === "metadata_failed" ? "error" : "ready"}`;
      state.textContent = item.readaptation_review_state === "pending" && item.status === "organized"
        ? "重新适配待审核"
        : item.readaptation_review_state === "approved"
          ? "重新适配审核完成"
          : statusLabels[item.status] ?? item.status;
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
      for (const [label, value, runId, attemptId, mixed] of [
        ["Series", item.series_strategy, item.series_run_id, item.series_attempt_id, false],
        ["Season", item.season_strategy, item.season_run_id, item.season_attempt_id, false],
        [
          "Episode",
          item.episode_strategy,
          item.episode_run_id,
          item.episode_attempt_id,
          item.episode_resolution_mixed,
        ],
      ] as const) {
        const group = document.createElement("div");
        const term = document.createElement("dt");
        term.textContent = String(label);
        const description = document.createElement("dd");
        description.textContent = mixed
          ? "多个文件使用不同来源或证据（见文件详情）"
          : libraryStrategy(value);
        if (!mixed && value) {
          description.title =
            `${resolutionReference(runId, attemptId)}\n`
            + `run_id=${runId ?? "未记录"}\n`
            + `attempt_id=${attemptId ?? "未记录"}`;
          const reference = document.createElement("small");
          reference.className = "metadata-resolution-reference";
          reference.textContent = resolutionReference(runId, attemptId);
          description.append(document.createElement("br"), reference);
        }
        group.append(term, description);
        stages.append(group);
      }
      const files = document.createElement("p");
      files.className = "metadata-files";
      files.textContent = `已确认 ${item.episode_file_count} · 已跳过重复 ${item.duplicate_file_count} · Other ${item.other_file_count} · 待处理 ${item.pending_file_count}`;
      card.append(heading, handling, identity, stages, files);
      if (item.readaptation_review_state === "pending") {
        const review = document.createElement("p");
        review.className = "metadata-failure";
        review.textContent = item.status === "organized"
          ? "Other 重新适配已完成，等待人工审核"
          : "Other 重新适配执行中；完成后需人工审核";
        card.append(review);
      } else if (item.readaptation_review_state === "approved") {
        const review = document.createElement("p");
        review.className = "metadata-handling resolved";
        review.textContent = "Other 重新适配已人工审核";
        card.append(review);
      }
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
      const fallbackDecision = metadataFallbackDecision(item);
      if (fallbackDecision) card.append(fallbackDecision);
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
      if (item.status === "organized" && item.other_file_count > 0) {
        const readapt = document.createElement("button");
        readapt.type = "button";
        readapt.className = "retry-button";
        readapt.textContent = "重新适配 Other";
        readapt.addEventListener("click", () => void readaptOtherFiles(item.task_id, readapt));
        actions.append(readapt);
      }
      if (item.status === "organized" && item.readaptation_review_state === "pending") {
        const approve = document.createElement("button");
        approve.type = "button";
        approve.className = "retry-button";
        approve.textContent = "确认人工审核";
        approve.addEventListener("click", () => void approveOtherReadaptation(item.task_id, approve));
        actions.append(approve);
      }
      if (item.status === "organized" && item.readaptation_review_state === "approved") {
        const reviewResult = document.createElement("button");
        reviewResult.type = "button";
        reviewResult.className = "metadata-attempt-button";
        reviewResult.textContent = "查看审核结果";
        reviewResult.addEventListener(
          "click",
          () => void approveOtherReadaptation(item.task_id, reviewResult),
        );
        actions.append(reviewResult);
      }
      const remove = document.createElement("button");
      remove.type = "button";
      remove.className = "delete-button";
      remove.textContent = "删除…";
      remove.addEventListener("click", () => void openDeletePreview(item.task_id));
      actions.append(remove);
      card.append(actions, detailTarget, attemptList);
      if (expandedMetadataDetailIds.has(item.task_id)) {
        void loadMetadataDetail(item.task_id, detailTarget, detailButton);
      }
      if (expandedMetadataTaskIds.has(item.task_id)) {
        void loadMetadataAttempts(item.task_id, attemptList, attempts);
      }
      return card;
    });
    metadataRenderSignature = renderSignature;
    renderRegionContent(container, ...cards);
    if (anchorTaskId !== null && anchorViewportTop !== null) {
      const replacementAnchor = Array.from(
        container.querySelectorAll<HTMLElement>(".metadata-card"),
      ).find((card) => card.dataset.taskId === anchorTaskId);
      if (replacementAnchor) {
        window.scrollBy(
          0,
          replacementAnchor.getBoundingClientRect().top - anchorViewportTop,
        );
      }
    }
  } catch (error) {
    if (requestSequence !== metadataListRequestSequence) return;
    if (background && hadReadyContent) {
      element<HTMLElement>("#metadata-list-status").textContent =
        `自动刷新失败，保留当前列表：${errorMessage(error, "未知错误")}`;
      return;
    }
    renderRegionMessage(
      container,
      "error",
      `元数据状态读取失败：${errorMessage(error, "未知错误")}`,
    );
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
  setRegionState(container, "loading");
  try {
    const response = await fetch("/api/v1/metadata/pending-tmdb", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as { items: PendingTmdbSummary[] };
    if (body.items.length === 0) {
      renderRegionMessage(container, "empty", "暂无待补全 TMDB 的作品");
      return;
    }
    renderRegionContent(container, ...body.items.map((item) => {
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
    renderRegionMessage(
      container,
      "error",
      `待补全状态读取失败：${errorMessage(error, "未知错误")}`,
    );
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

const downloaderEditableFields = [
  ["base_url", "#downloader-config-url"],
  ["username", "#downloader-config-username"],
  ["password", "#downloader-config-password"],
  ["download_path", "#downloader-config-path"],
  ["enabled", "#downloader-config-enabled"],
] as const;

function applyDownloaderFieldLocks(instance: DownloaderInstance | null): void {
  const locks = new Map(
    (instance?.locked_fields ?? []).map((lock) => [lock.field, lock]),
  );
  for (const [field, selector] of downloaderEditableFields) {
    const input = element<HTMLInputElement>(selector);
    const lock = locks.get(field);
    input.disabled = lock !== undefined;
    const label = input.closest("label");
    label?.classList.toggle("configuration-field-locked", lock !== undefined);
    if (lock) {
      label?.setAttribute(
        "title",
        `由 ${lock.source} 控制：${lock.controlling_keys.join(", ")}`,
      );
    } else {
      label?.removeAttribute("title");
    }
  }

  const passwordLocked = locks.has("password");
  const clearPassword = element<HTMLInputElement>(
    "#downloader-config-clear-password",
  );
  clearPassword.disabled = passwordLocked;
  clearPassword.closest("label")?.classList.toggle(
    "configuration-field-locked",
    passwordLocked,
  );
  element<HTMLButtonElement>("#downloader-config-save").disabled =
    instance !== null
    && downloaderEditableFields.every(([field]) => locks.has(field));
}

function openDownloaderConfig(instance: DownloaderInstance | null): void {
  activeDownloaderId = instance?.id ?? null;
  const id = element<HTMLInputElement>("#downloader-config-id");
  id.disabled = instance !== null;
  id.value = instance?.id ?? "";
  element<HTMLInputElement>("#downloader-config-url").value = instance?.base_url ?? "http://127.0.0.1:8080/";
  element<HTMLInputElement>("#downloader-config-username").value = instance?.username ?? "";
  element<HTMLInputElement>("#downloader-config-password").value = instance?.password ?? "";
  element<HTMLInputElement>("#downloader-config-path").value = instance?.download_path ?? "";
  element<HTMLInputElement>("#downloader-config-enabled").checked = instance?.enabled ?? true;
  element<HTMLInputElement>("#downloader-config-clear-password").checked = false;
  applyDownloaderFieldLocks(instance);
  element<HTMLButtonElement>("#downloader-config-delete").disabled =
    instance?.configuration_source !== "private_override";
  const credentialState = instance?.credentials_configured
    ? "已有凭据已配置并已回填；可直接查看或修改。"
    : "当前没有已配置凭据。";
  const lockState = instance && instance.locked_fields.length > 0
    ? ` 部署锁：${instance.locked_fields.map((lock) =>
      `${lock.field}（${lock.controlling_keys.join(" / ")}）`).join("、")}；锁定字段只读且不会写入私有覆盖。`
    : "";
  element<HTMLElement>("#downloader-config-message").textContent =
    credentialState + lockState;
  downloaderConfigDialog.showModal();
}

async function saveDownloaderConfig(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  const id = activeDownloaderId ?? element<HTMLInputElement>("#downloader-config-id").value;
  const current = downloaderInstances.find(item => item.id === activeDownloaderId) ?? null;
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
        username: element<HTMLInputElement>("#downloader-config-username").value === (current?.username ?? "")
          ? null
          : element<HTMLInputElement>("#downloader-config-username").value || null,
        password: element<HTMLInputElement>("#downloader-config-password").value === (current?.password ?? "")
          ? null
          : element<HTMLInputElement>("#downloader-config-password").value || null,
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
    const instance = downloaderInstances.find(
      (item) => item.id === activeDownloaderId,
    ) ?? null;
    const allFieldsLocked = instance !== null
      && downloaderEditableFields.every(([field]) =>
        instance.locked_fields.some((lock) => lock.field === field));
    save.disabled = allFieldsLocked;
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
  setRegionState(list, "loading");
  status.textContent = "正在读取下载器实例…";
  try {
    const response = await fetch("/api/v1/downloaders", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as DownloaderInstanceList;
    downloaderInstances = body.items;
    downloaderConfigurationRevision = body.configuration_revision;
    refreshSourceDownloaderOptions();
    const cards = downloaderInstances.map((instance) => {
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
        ["部署锁", instance.locked_fields.length === 0
          ? "无"
          : instance.locked_fields.map((lock) => lock.field).join("、")],
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
      edit.className = "secondary-button";
      test.className = "secondary-button";
      probe.className = "secondary-button";
      test.disabled = !instance.enabled;
      probe.disabled = !instance.enabled;
      actions.append(edit, test, probe);
      card.append(heading, facts, endpoint, actions);
      return card;
    });
    if (cards.length === 0) {
      renderRegionMessage(list, "empty", "尚未配置 qBittorrent 实例。");
    } else {
      renderRegionContent(list, ...cards);
    }
    status.textContent = body.downloads_blocked
      ? `下载已被 ${body.migration_diagnostics.map((item) => item.code).join("、")} 阻断；不会连接或启动任何下载器任务`
      : body.restart_required
      ? `${body.items.length} 个实例 · 私有配置 revision ${body.configuration_revision} 尚未应用，请重启`
      : `${body.items.length} 个 qBittorrent 实例 · 用户名和密码可在配置中直接查看`;
  } catch (error) {
    const message = `下载器读取失败：${errorMessage(error, "未知错误")}`;
    renderRegionMessage(list, "error", message);
    status.textContent = message;
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
  const cookieLock = current?.locked_fields.find(
    (lock) => lock.field === "mikan_identity_cookie",
  );
  input.disabled = !isMikan || clear.checked || cookieLock !== undefined;
  clear.disabled = !isMikan || current === null || cookieLock !== undefined;
  if (!isMikan || clear.checked) input.value = "";
  element<HTMLElement>("#source-mikan-cookie-state").textContent = cookieLock
    ? `部署锁只读（${cookieLock.controlling_keys.join(" / ")}），当前有效值已回填。`
    : !isMikan
    ? "仅 Mikan 适配器可配置登录 Cookie。"
    : current?.mikan_identity_cookie_configured
    ? "已配置并已回填；可直接查看或修改。"
    : "未配置；可粘贴 Cookie 值或完整 Cookie。";

  const rssUrl = element<HTMLInputElement>("#source-rss-url");
  const clearRssUrl = element<HTMLInputElement>("#source-rss-url-clear");
  const rssCron = element<HTMLInputElement>("#source-rss-cron");
  const scheduleEnabled = element<HTMLInputElement>("#source-rss-schedule-enabled");
  const sourceEnabled = element<HTMLInputElement>("#source-enabled").checked;
  rssUrl.disabled = !isMikan || clearRssUrl.checked;
  clearRssUrl.disabled = !isMikan || current === null;
  rssCron.disabled = !isMikan;
  scheduleEnabled.disabled = !isMikan || !sourceEnabled;
  if (!isMikan || clearRssUrl.checked) rssUrl.value = "";
  if (!isMikan || !sourceEnabled || clearRssUrl.checked) scheduleEnabled.checked = false;
  element<HTMLElement>("#source-rss-url-state").textContent = !isMikan
    ? "仅 Mikan 适配器可配置 RSS URL。"
    : clearRssUrl.checked
    ? "保存后明确清除 RSS URL，并关闭自动调度。"
    : current?.rss_feed_url_configured
    ? "已保存于服务端数据目录并已回填；可直接查看或修改。"
    : "未配置；启用自动调度前必须填写。";

  const scheduleState = element<HTMLElement>("#source-rss-schedule-state");
  if (!scheduleEnabled.checked) {
    scheduleState.textContent = current?.rss_schedule_enabled
      ? "保存后关闭并移除自动调度。"
      : "RSS 自动调度未启用。";
  } else if (!current || !current.rss_schedule_enabled) {
    scheduleState.textContent = "保存后注册自动调度。";
  } else {
    const registered = current.rss_schedule_registered
      ? `已注册 · 下次 ${dataUpdateTime(current.rss_schedule_next_at_utc)}`
      : "已配置但当前未注册（后台工作器未运行）";
    const last = current.rss_last_run_state === "never"
      ? "尚未执行"
      : `${current.rss_last_run_state} · 完成 ${dataUpdateTime(current.rss_last_completed_at_utc)}`;
    scheduleState.textContent = `${registered} · ${last}${current.rss_last_failure_code ? ` · ${current.rss_last_failure_code}` : ""}${current.rss_last_batch_id ? ` · batch ${current.rss_last_batch_id}` : ""}`;
  }
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
  const category = element<HTMLInputElement>("#source-category");
  const dynamicTag = element<HTMLInputElement>("#source-dynamic-tag");
  const categoryLock = profile?.locked_fields.find((lock) => lock.field === "category");
  const dynamicTagLock = profile?.locked_fields.find(
    (lock) => lock.field === "dynamic_tag_template",
  );
  category.value = profile?.category ?? "animegonet";
  category.disabled = categoryLock !== undefined;
  category.title = categoryLock
    ? `部署锁：${categoryLock.controlling_keys.join(" / ")}`
    : "";
  element<HTMLInputElement>("#source-tags").value = profile?.tags.join(", ") ?? "";
  dynamicTag.value = profile?.dynamic_tag_template ?? "";
  dynamicTag.disabled = dynamicTagLock !== undefined;
  dynamicTag.title = dynamicTagLock
    ? `部署锁：${dynamicTagLock.controlling_keys.join(" / ")}`
    : "";
  element<HTMLInputElement>("#source-seeding-time").value =
    String(profile?.seeding_time_minutes ?? 0);
  element<HTMLTextAreaElement>("#source-hosts").value = profile?.allowed_torrent_hosts.join("\n") ?? "";
  element<HTMLInputElement>("#source-enabled").checked = profile?.enabled ?? true;
  element<HTMLInputElement>("#source-filter-enabled").checked = profile?.rss_filter_enabled ?? false;
  element<HTMLInputElement>("#source-priority-enabled").checked = profile?.rss_priority_enabled ?? false;
  element<HTMLInputElement>("#source-duplicate-notification-enabled").checked =
    profile?.duplicate_notification_enabled ?? true;
  element<HTMLInputElement>("#source-mikan-cookie").value = profile?.mikan_identity_cookie ?? "";
  element<HTMLInputElement>("#source-mikan-cookie-clear").checked = false;
  element<HTMLInputElement>("#source-rss-url").value = profile?.rss_feed_url ?? "";
  element<HTMLInputElement>("#source-rss-url-clear").checked = false;
  element<HTMLInputElement>("#source-rss-cron").value =
    profile?.rss_schedule_cron ?? "0 0/15 * * * ?";
  element<HTMLInputElement>("#source-rss-schedule-enabled").checked =
    profile?.rss_schedule_enabled ?? false;
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
    renderRegionMessage(list, "empty", "暂无来源");
    return;
  }
  renderRegionContent(list, ...sourceProfiles.map((profile) => {
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
    const lockState = profile.locked_fields.length > 0
      ? ` · 部署锁 ${profile.locked_fields.map((lock) => lock.field).join("/")}`
      : "";
    route.textContent = `${profile.adapter} → ${profile.downloader_id} · ${profile.file_strategy} · ${profile.category} · 重复通知 ${profile.duplicate_notification_enabled ? "开启" : "关闭"} · 动态 Tag ${profile.dynamic_tag_template ?? "关闭"} · 做种 ${profile.seeding_time_minutes} 分钟 · Mikan Cookie ${profile.mikan_identity_cookie_configured ? "已配置" : "未配置"}${lockState} · RSS 调度 ${profile.rss_schedule_enabled ? profile.rss_last_run_state : "关闭"} · 任务 ${profile.ingest_task_count} / RSS ${profile.rss_batch_count}`;
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
          `动态 Tag 模板 ${route.dynamic_tag_template ?? "关闭"}`,
          `做种 ${route.seeding_time_minutes} 分钟 · RSS规则 rev ${route.rss_rule_revision ?? "—"}`,
          `重复命中通知 ${route.duplicate_notification_enabled ? "开启" : "关闭"}（不改变全局去重）`,
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
  const list = element<HTMLElement>("#source-list");
  setRegionState(list, "loading");
  status.textContent = "正在读取来源配置…";
  try {
    const response = await fetch("/api/v1/sources", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as SourceProfileList;
    sourceProfiles = body.items;
    refreshSourceAdapterOptions();
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
    refreshSourceAdapterOptions();
    refreshManualSourceOptions();
    const message = `来源读取失败：${errorMessage(error, "未知错误")}`;
    renderRegionMessage(list, "error", message);
    status.textContent = message;
  }
}

function refreshSourceAdapterOptions(): void {
  const select = element<HTMLSelectElement>("#source-adapter");
  const previous = select.value;
  const entries = new Map<string, { label: string; enabled: boolean }>([
    ["mikan", { label: "Mikan", enabled: true }],
    ["u2", { label: "U2", enabled: true }],
    ["ttg", { label: "TTG", enabled: true }],
  ]);
  for (const adapter of externalSourceAdapters) {
    entries.set(adapter.id, {
      label: `${adapter.name} (${adapter.id})${adapter.enabled ? "" : " · 插件未启用"}`,
      enabled: adapter.enabled,
    });
  }
  for (const profile of sourceProfiles) {
    if (!entries.has(profile.adapter)) {
      entries.set(profile.adapter, {
        label: `${profile.adapter} · 插件包不可用`,
        enabled: false,
      });
    }
  }

  const options = [...entries.entries()].map(([id, entry]) => {
    const option = document.createElement("option");
    option.value = id;
    option.textContent = entry.label;
    option.disabled = !entry.enabled;
    return option;
  });
  select.replaceChildren(...options);
  select.value = entries.has(previous) ? previous : "u2";
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
  updateSavedRssRunState();
  updateManualDownloadHint();
}

function updateSavedRssRunState(): void {
  const sourceId = element<HTMLSelectElement>("#manual-rss-source").value;
  const profile = sourceProfiles.find((item) => item.id === sourceId);
  const run = element<HTMLButtonElement>("#manual-rss-run-saved");
  const manage = element<HTMLButtonElement>("#manual-rss-manage-source");
  manage.disabled = profile?.adapter !== "mikan";
  manage.title = profile?.adapter === "mikan"
    ? "打开设置与备份 / 输入源，并直接显示这个来源已保存的 Mikan Cookie。"
    : "请先选择一个已启用的 Mikan 来源。";
  run.disabled = profile?.adapter !== "mikan"
    || !profile.enabled
    || !profile.rss_feed_url_configured;
  run.title = profile?.rss_feed_url_configured
    ? "使用服务端为此来源保存的 RSS URL 立即执行；不要求开启自动调度。"
    : "请先在来源管理中保存这个 Mikan 来源的 RSS URL。";
}

function openSelectedMikanSourceSettings(): void {
  const sourceId = element<HTMLSelectElement>("#manual-rss-source").value;
  const profile = sourceProfiles.find((item) => item.id === sourceId) ?? null;
  populateSourceForm(profile);
  selectWorkspace("connections", "sources");
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
    renderManualRssResponse(body, result);
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

function renderManualRssResponse(body: ManualRssResponse, result: HTMLElement): void {
  const accepted = body.items.filter((item) => item.ingest_task_id !== null).length;
  const summary = document.createElement("p");
  summary.className = "manual-result-summary";
  const batches = body.batches?.length ? body.batches : [body];
  summary.textContent = batches.length > 1
    ? `聚合 RSS · ${batches.length} 个番组批次 · 接收 ${accepted}/${body.items.length} · 规则 rev ${body.rule_revision}`
    : `批次 ${body.batch_id} · mikanid ${body.mikanid ?? "未识别"} · `
      + `bgmid ${body.bgmid ?? "未取得"}（${body.bgmid_discovery_state}`
      + `${body.bgmid_discovery_failure_code ? ` / ${body.bgmid_discovery_failure_code}` : ""}）`
      + ` · 接收 ${accepted}/${body.items.length} · 规则 rev ${body.rule_revision}`;
  const renderedItems = batches.flatMap((batch, batchIndex) => {
    const batchSummary = batches.length > 1
      ? [manualResultItem(
          `番组批次 ${batchIndex + 1} · mikanid ${batch.mikanid ?? "未识别"}`,
          `批次 ${batch.batch_id} · bgmid ${batch.bgmid ?? "未取得"}`
            + `（${batch.bgmid_discovery_state}`
            + `${batch.bgmid_discovery_failure_code ? ` / ${batch.bgmid_discovery_failure_code}` : ""}）`,
          batch.bgmid_discovery_state !== "Resolved",
        )]
      : [];
    return [
      ...batchSummary,
      ...batch.items.map((item, index) => manualResultItem(
        `候选 ${index + 1} · ${rssStatusLabels[item.status] ?? item.status}`,
        [
          item.decision_kind,
          item.decision_reason,
          item.identity_mikanid ? `mikanid ${item.identity_mikanid}` : null,
          item.identity_groupid ? `groupid ${item.identity_groupid}` : null,
          item.status === "already_completed"
            ? "命中完成记录的来源别名，未抓取 Torrent"
            : null,
          item.ingest_task_id ? `任务 ${item.ingest_task_id}` : null,
          item.errors.length > 0 ? item.errors.join("；") : null,
        ].filter((value): value is string => value !== null).join(" · "),
        !["staged", "blocked", "already_ingested", "already_completed"]
          .includes(item.status),
      )),
    ];
  });
  result.replaceChildren(summary, ...renderedItems);
}

async function runSavedSourceRss(): Promise<void> {
  const sourceId = element<HTMLSelectElement>("#manual-rss-source").value;
  const run = element<HTMLButtonElement>("#manual-rss-run-saved");
  const result = element<HTMLElement>("#manual-rss-result");
  run.disabled = true;
  result.replaceChildren(manualResultItem(
    "正在处理已保存 RSS",
    "本次直接读取服务端保存地址，不要求开启自动调度。",
    false,
  ));
  try {
    const response = await fetch(
      `/api/v1/sources/${encodeURIComponent(sourceId)}/rss/run`,
      { method: "POST", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as ManualRssResponse;
    renderManualRssResponse(body, result);
    void loadDownloads();
    void loadMetadataTasks();
    void loadSources(sourceId);
  } catch (error) {
    result.replaceChildren(manualResultItem(
      "已保存 RSS 处理失败",
      errorMessage(error, "未知错误"),
      true,
    ));
  } finally {
    updateSavedRssRunState();
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
    dynamic_tag_template: element<HTMLInputElement>("#source-dynamic-tag").value,
    seeding_time_minutes: element<HTMLInputElement>("#source-seeding-time").valueAsNumber,
    allowed_torrent_hosts: sourceHosts(),
    rss_filter_enabled: element<HTMLInputElement>("#source-filter-enabled").checked,
    rss_priority_enabled: element<HTMLInputElement>("#source-priority-enabled").checked,
    duplicate_notification_enabled:
      element<HTMLInputElement>("#source-duplicate-notification-enabled").checked,
    enabled: element<HTMLInputElement>("#source-enabled").checked,
    mikan_identity_cookie:
      element<HTMLInputElement>("#source-mikan-cookie").value === (current?.mikan_identity_cookie ?? "")
        ? null
        : element<HTMLInputElement>("#source-mikan-cookie").value || null,
    rss_feed_url:
      element<HTMLInputElement>("#source-rss-url").value === (current?.rss_feed_url ?? "")
        ? null
        : element<HTMLInputElement>("#source-rss-url").value || null,
    rss_schedule_enabled:
      element<HTMLInputElement>("#source-rss-schedule-enabled").checked,
    rss_schedule_cron: element<HTMLInputElement>("#source-rss-cron").value.trim(),
  };
  const payload = current
    ? {
        ...common,
        clear_mikan_identity_cookie:
          element<HTMLInputElement>("#source-mikan-cookie-clear").checked,
        clear_rss_feed_url:
          element<HTMLInputElement>("#source-rss-url-clear").checked,
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

function optionalPositiveInteger(selector: string): number | null {
  const raw = element<HTMLInputElement>(selector).value.trim();
  return raw === "" ? null : Number(raw);
}

function optionalNonNegativeInteger(selector: string): number | null {
  const raw = element<HTMLInputElement>(selector).value.trim();
  return raw === "" ? null : Number(raw);
}

function readAiPromptDraft(version: string): string | null {
  try {
    const raw = localStorage.getItem(aiTestPromptDraftKey);
    if (!raw) return null;
    const value = JSON.parse(raw) as { version?: unknown; template?: unknown };
    return value.version === version && typeof value.template === "string"
      ? value.template
      : null;
  } catch {
    return null;
  }
}

function saveAiPromptDraft(): void {
  if (!aiTestDefaultPrompt) return;
  try {
    localStorage.setItem(aiTestPromptDraftKey, JSON.stringify({
      version: aiTestDefaultPrompt.prompt_version,
      template: element<HTMLTextAreaElement>("#ai-test-prompt-template").value,
    }));
  } catch {
    // Browser storage is optional; the current in-memory edit remains usable.
  }
}

async function loadAiTestPrompt(): Promise<void> {
  const editor = element<HTMLTextAreaElement>("#ai-test-prompt-template");
  const status = element<HTMLElement>("#ai-test-prompt-status");
  try {
    const [prompt, bootstrap] = await Promise.all([
      api.get<AiMetadataTestPrompt>("/api/v1/ai-test/prompt"),
      api.get<AiTesterBootstrap>("/api/v1/ai-test/bootstrap"),
    ]);
    aiTestDefaultPrompt = prompt;
    editor.maxLength = prompt.maximum_length;
    editor.value = readAiPromptDraft(prompt.prompt_version) ?? prompt.template;
    editor.disabled = false;
    const defaults = bootstrap.defaults;
    element<HTMLInputElement>("#ai-test-base-url").value = defaults.base_url;
    element<HTMLInputElement>("#ai-test-api-key").value = defaults.api_key;
    element<HTMLInputElement>("#ai-test-model").value = defaults.model;
    element<HTMLSelectElement>("#ai-test-api-mode").value = defaults.mode === 0
      ? "responses"
      : "chat-completions";
    element<HTMLSelectElement>("#ai-test-reasoning-effort").value = defaults.reasoning_effort ?? "none";
    element<HTMLInputElement>("#ai-test-timeout").value = String(defaults.timeout_seconds);
    element<HTMLInputElement>("#ai-test-http-proxy").value = defaults.proxy_url ?? "";
    element<HTMLInputElement>("#ai-test-tmdb-mcp-url").value = defaults.tmdb_mcp_url;
    element<HTMLInputElement>("#ai-test-bgm-mcp-url").value = defaults.bgm_mcp_url;
    element<HTMLInputElement>("#ai-test-anidb-template").value = defaults.ani_db_mapping_url_template;
    element<HTMLInputElement>("#ai-test-enable-tmdb-mcp").checked = defaults.enable_tmdb_mcp;
    element<HTMLInputElement>("#ai-test-enable-bgm-mcp").checked = defaults.enable_bgm_mcp;
    element<HTMLInputElement>("#ai-test-enable-anidb").checked = defaults.enable_anidb_lookup;
    element<HTMLInputElement>("#ai-test-web-search").checked = defaults.web_search_enabled;
    element<HTMLInputElement>("#ai-test-is-mikan-source").checked = defaults.is_mikan_rss_source;
    restoreAiTesterForm();
    updateAiTesterSourceStates();
    status.textContent = `当前 ${prompt.prompt_version}；协议、续轮和工具执行来自已验证 Tester。`;
  } catch (error) {
    editor.disabled = true;
    status.textContent = `Prompt 读取失败：${errorMessage(error, "未知错误")}`;
  }
}

function resetAiTestPrompt(): void {
  if (!aiTestDefaultPrompt) return;
  const editor = element<HTMLTextAreaElement>("#ai-test-prompt-template");
  editor.value = aiTestDefaultPrompt.template;
  try {
    localStorage.removeItem(aiTestPromptDraftKey);
  } catch {
  }
  element<HTMLElement>("#ai-test-prompt-status").textContent =
    `已恢复 ${aiTestDefaultPrompt.prompt_version} 程序默认；尚未运行。`;
}

async function importAiTestMikanEpisode(): Promise<void> {
  const button = element<HTMLButtonElement>("#ai-test-mikan-import");
  const status = element<HTMLElement>("#ai-test-mikan-status");
  const episodeUrl = element<HTMLInputElement>("#ai-test-mikan-url").value.trim();
  button.disabled = true;
  status.textContent = "正在读取 Mikan 页面、RSS、作品关联和 Torrent…";
  try {
    const imported = await aiTesterPost<AiMetadataTestMikanImport>(
      "/api/v1/ai-test/mikan-import",
      { episode_url: episodeUrl, proxy_url: null },
    );
    if (!imported.success || !imported.files) throw new Error(imported.error_message ?? "Mikan import failed");
    element<HTMLTextAreaElement>("#ai-test-title").value = imported.title ?? "";
    element<HTMLTextAreaElement>("#ai-test-files-json").value = JSON.stringify(
      imported.files.map(file => ({ name: file.name, size_bytes: file.size_bytes })),
      null,
      2,
    );
    element<HTMLInputElement>("#ai-test-bgmid").value =
      imported.bgmid == null ? "" : String(imported.bgmid);
    element<HTMLInputElement>("#ai-test-file-count").value =
      String(imported.torrent_file_count ?? "unavailable");
    element<HTMLInputElement>("#ai-test-published-at").value =
      imported.mikan_pub_date ?? "";
    element<HTMLInputElement>("#ai-test-torrent-import-id").value = imported.import_id ?? "";
    element<HTMLInputElement>("#ai-test-mikan-scope").value =
      `${imported.mikan_id ?? "?"} / ${imported.group_id ?? "?"}`;
    element<HTMLInputElement>("#ai-test-is-mikan-source").checked = true;
    renderAiTestFileCandidates(imported.file_episode_candidates);
    status.textContent = `解析完成：mikanid=${imported.mikan_id ?? "未找到"}，groupid=${imported.group_id ?? "未找到"}，`
      + `bgmid=${imported.bgmid ?? "未找到"}，视频 ${imported.files.length} / Torrent 文件 ${imported.torrent_file_count ?? "?"}。`
      + "Torrent URL 已脱敏，尚未运行 AI。";
    persistAiTesterForm();
    updateAiTesterSourceStates();
  } catch (error) {
    status.textContent = `Mikan 解析失败：${errorMessage(error, "未知错误")}`;
  } finally {
    button.disabled = false;
  }
}

async function importAiTestTorrent(file: File): Promise<void> {
  const dataBase64 = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new Error("Torrent 文件读取失败"));
    reader.onload = () => resolve(String(reader.result).split(",", 2)[1] ?? "");
    reader.readAsDataURL(file);
  });
  const imported = await aiTesterPost<{
    success: boolean;
    files: Array<{ name: string; size_bytes: number }> | null;
    error_message: string | null;
    import_id: string | null;
    torrent_file_count: number | null;
    file_episode_candidates: Array<{ name: string; file_episode_candidate: number | null }> | null;
  }>("/api/v1/ai-test/torrent-import", { data_base64: dataBase64 });
  if (!imported.success || !imported.files) throw new Error(imported.error_message ?? "Torrent import failed");
  element<HTMLTextAreaElement>("#ai-test-files-json").value = JSON.stringify(imported.files, null, 2);
  element<HTMLInputElement>("#ai-test-torrent-import-id").value = imported.import_id ?? "";
  element<HTMLInputElement>("#ai-test-file-count").value = String(imported.torrent_file_count ?? "unavailable");
  renderAiTestFileCandidates(imported.file_episode_candidates);
  persistAiTesterForm();
}

function aiTestSummaryItem(label: string, value: string): HTMLDivElement {
  const container = document.createElement("div");
  const term = document.createElement("span");
  const content = document.createElement("strong");
  term.textContent = label;
  content.textContent = value;
  container.append(term, content);
  return container;
}

function renderAiTestResult(result: AiTesterRunResult): void {
  const summary = element<HTMLElement>("#ai-test-summary");
  const usage = result.usage;
  const calls = (result.tool_timeline ?? []).filter(item =>
    (item.phase === "call" || item.phase === "cache-hit")
      && (item.name.includes("__") || item.name === "lookup_anidb_tmdbtv"),
  );
  const production = result.production_validation as { success?: boolean; failure_code?: string | null } | null;
  summary.replaceChildren(
    aiTestSummaryItem("HTTP", result.status_code ? String(result.status_code) : "unavailable"),
    aiTestSummaryItem("Tester 请求", result.success ? "成功" : "失败"),
    aiTestSummaryItem("Result JSON", result.result_json_valid ? "有效" : "无效"),
    aiTestSummaryItem("主程序 TMDB 验证", production?.success ? "通过" : production?.failure_code ?? "未执行/未通过"),
    aiTestSummaryItem("耗时", `${result.elapsed_milliseconds} ms`),
    aiTestSummaryItem("请求 / 工具", `${result.ai_api_requests?.length ?? 0} / ${calls.length}`),
    aiTestSummaryItem("Input Tokens", String(usage.input_tokens ?? "—")),
    aiTestSummaryItem("Output Tokens", String(usage.output_tokens ?? "—")),
    aiTestSummaryItem("Reasoning Tokens", String(usage.reasoning_tokens ?? "—")),
    aiTestSummaryItem("Total Tokens", String(usage.total_tokens ?? "—")),
    aiTestSummaryItem("Request Identity", result.request_identity ?? "—"),
    aiTestSummaryItem("错误", result.error_message ?? result.result_json_error ?? "—"),
  );
  summary.dataset.uiState = result.success && result.result_json_valid ? "ready" : "error";
  const badge = element<HTMLElement>("#ai-test-prompt-version");
  badge.textContent = aiTestDefaultPrompt?.prompt_version ?? "tmdb-ai-match-v16";
  badge.className = `badge ${result.success && result.result_json_valid ? "ok" : "error"}`;
  element<HTMLElement>("#ai-test-raw-output").textContent =
    result.raw_response || "模型未返回响应。";
  element<HTMLElement>("#ai-test-parsed-output").textContent = JSON.stringify({
    model_json: parseJsonOrText(result.model_json),
    result_json_valid: result.result_json_valid,
    result_json_error: result.result_json_error,
    production_validation: result.production_validation,
    pub_date_priority: result.pub_date_priority,
    file_episode_candidates: result.file_episode_candidates,
  }, null, 2);
  element<HTMLElement>("#ai-test-rendered-prompt").textContent = result.rendered_prompt;
  element<HTMLElement>("#ai-test-local-offset").textContent = JSON.stringify(result.local_episode_offset, null, 2);
  renderAiTesterApiRequests(result.ai_api_requests ?? []);
  renderAiTesterTools(result.tool_timeline ?? []);
  if (result.pub_date_priority) {
    element<HTMLInputElement>("#ai-test-pubdate-effective").value = String(result.pub_date_priority.use_bangumi_pub_date_first);
    element<HTMLInputElement>("#ai-test-pubdate-reason").value = result.pub_date_priority.reason;
  }
}

async function runAiMetadataTest(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  const button = element<HTMLButtonElement>("#ai-test-run");
  const stop = element<HTMLButtonElement>("#ai-test-stop");
  const message = element<HTMLElement>("#ai-test-message");
  button.disabled = true;
  stop.disabled = false;
  resetAiTesterLiveOutput();
  message.textContent = "正在执行已验证 Tester 流程；进度会实时显示…";
  try {
    const request = buildAiTesterRunRequest();
    activeAiTesterRunId = request.run_id;
    persistAiTesterForm();
    const result = await runAiTesterStream(request);
    if (result) {
      renderAiTestResult(result);
      message.textContent = result.success
        ? `测试完成：Tester JSON ${result.result_json_valid ? "有效" : "无效"}。`
        : `测试完成但 API 失败：${result.error_message ?? "unknown"}`;
    }
  } catch (error) {
    message.textContent = `测试失败：${errorMessage(error, "未知错误")}`;
  } finally {
    activeAiTesterRunId = null;
    button.disabled = false;
    stop.disabled = true;
  }
}

function fillAiMetadataTestExample(): void {
  element<HTMLTextAreaElement>("#ai-test-title").value =
    "[黒ネズミたち] 说出这边交给我你们先走以后十年过去成了传说。 / Kokoore - 06 (CR 1920x1080 AVC AAC MKV)";
  element<HTMLTextAreaElement>("#ai-test-files-json").value = JSON.stringify([
    { name: "Kokoore - 06.mkv", size_bytes: 734003200 },
  ], null, 2);
  element<HTMLInputElement>("#ai-test-bgmid").value = "590786";
  element<HTMLInputElement>("#ai-test-bgm-episode").value = "6";
  element<HTMLInputElement>("#ai-test-published-at").value = "2026-08-10T12:00:00";
  element<HTMLInputElement>("#ai-test-use-bgm-pubdate").checked = true;
  element<HTMLInputElement>("#ai-test-is-mikan-source").checked = true;
  element<HTMLInputElement>("#ai-test-enable-tmdb-mcp").checked = true;
  element<HTMLInputElement>("#ai-test-enable-bgm-mcp").checked = true;
  element<HTMLInputElement>("#ai-test-enable-anidb").checked = true;
  updateAiTesterSourceStates();
}

let activeAiTesterRunId: string | null = null;
const aiTesterFormStorageKey = "animegonet.aiTester.form.v2";
const aiTesterPersistedFields = [
  "ai-test-use-bgm-pubdate", "ai-test-title", "ai-test-bgmid", "ai-test-anidbid", "ai-test-mikan-url",
  "ai-test-published-at", "ai-test-bgm-episode", "ai-test-is-mikan-source",
  "ai-test-torrent-import-id", "ai-test-file-count", "ai-test-files-json",
  "ai-test-file-candidates",
];

function buildAiTesterRunRequest(): AiTesterRunRequest {
  JSON.parse(element<HTMLTextAreaElement>("#ai-test-files-json").value);
  const useBangumiPubDateFirst = element<HTMLInputElement>("#ai-test-use-bgm-pubdate").checked;
  return {
    base_url: element<HTMLInputElement>("#ai-test-base-url").value.trim(),
    api_key: element<HTMLInputElement>("#ai-test-api-key").value,
    model: element<HTMLInputElement>("#ai-test-model").value.trim(),
    mode: element<HTMLSelectElement>("#ai-test-api-mode").value,
    reasoning_effort: element<HTMLSelectElement>("#ai-test-reasoning-effort").value,
    web_search_enabled: element<HTMLInputElement>("#ai-test-web-search").checked,
    timeout_seconds: Number(element<HTMLInputElement>("#ai-test-timeout").value),
    proxy_url: element<HTMLInputElement>("#ai-test-http-proxy").value.trim(),
    prompt_template: element<HTMLTextAreaElement>("#ai-test-prompt-template").value || null,
    title: element<HTMLTextAreaElement>("#ai-test-title").value,
    files_json: element<HTMLTextAreaElement>("#ai-test-files-json").value,
    bgmid: element<HTMLInputElement>("#ai-test-bgmid").value.trim(),
    anidbid: element<HTMLInputElement>("#ai-test-anidbid").value.trim(),
    ...(useBangumiPubDateFirst
      ? { mikan_pub_date: element<HTMLInputElement>("#ai-test-published-at").value.trim() }
      : {}),
    bgm_episode_candidate: element<HTMLInputElement>("#ai-test-bgm-episode").value.trim(),
    use_bangumi_pubdate_first: useBangumiPubDateFirst,
    torrent_import_id: element<HTMLInputElement>("#ai-test-torrent-import-id").value,
    is_mikan_rss_source: element<HTMLInputElement>("#ai-test-is-mikan-source").checked,
    bgm_mcp_url: element<HTMLInputElement>("#ai-test-bgm-mcp-url").value.trim(),
    tmdb_mcp_url: element<HTMLInputElement>("#ai-test-tmdb-mcp-url").value.trim(),
    enable_bgm_mcp: element<HTMLInputElement>("#ai-test-enable-bgm-mcp").checked,
    enable_tmdb_mcp: element<HTMLInputElement>("#ai-test-enable-tmdb-mcp").checked,
    enable_anidb_lookup: element<HTMLInputElement>("#ai-test-enable-anidb").checked,
    anidb_mapping_url_template: element<HTMLInputElement>("#ai-test-anidb-template").value.trim(),
    run_id: crypto.randomUUID(),
  };
}

async function runAiTesterStream(request: AiTesterRunRequest): Promise<AiTesterRunResult | null> {
  const requestHeaders = new Headers(headers);
  requestHeaders.set("Content-Type", "application/json");
  const response = await fetch("/api/v1/ai-test/run-stream", {
    method: "POST",
    headers: requestHeaders,
    body: JSON.stringify(request),
  });
  if (!response.body) throw new Error(`HTTP ${response.status}: empty stream`);
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let pending = "";
  let finalResult: AiTesterRunResult | null = null;
  while (true) {
    const { value, done } = await reader.read();
    pending += decoder.decode(value, { stream: !done });
    const lines = pending.split("\n");
    pending = lines.pop() ?? "";
    for (const line of lines) {
      if (!line.trim()) continue;
      const envelope = JSON.parse(line) as AiTesterStreamEnvelope;
      if (envelope.type === "progress" && envelope.progress) renderAiTesterProgress(envelope.progress);
      if (envelope.type === "result" && envelope.result) finalResult = envelope.result;
      if (envelope.type === "stopped") throw new Error(envelope.error ?? "Execution stopped by user.");
      if (envelope.type === "error") throw new Error(envelope.error ?? "Tester stream failed.");
    }
    if (done) break;
  }
  return finalResult;
}

async function stopAiMetadataTest(): Promise<void> {
  if (!activeAiTesterRunId) return;
  await aiTesterPost<{ stopped: boolean; message: string }>("/api/v1/ai-test/stop", {
    run_id: activeAiTesterRunId,
  });
  element<HTMLElement>("#ai-test-message").textContent = "已发送停止请求。";
}

async function aiTesterPost<T>(path: string, body: unknown): Promise<T> {
  const requestHeaders = new Headers(headers);
  requestHeaders.set("Content-Type", "application/json");
  const response = await fetch(path, { method: "POST", headers: requestHeaders, body: JSON.stringify(body) });
  const value = await response.json() as T & { error_message?: string | null; message?: string | null };
  if (!response.ok) throw new Error(value.error_message ?? value.message ?? `HTTP ${response.status}`);
  return value;
}

function renderAiTesterProgress(progress: AiTesterProgress): void {
  const log = element<HTMLElement>("#ai-test-execution-log");
  if (log.querySelector(".muted")) log.replaceChildren();
  const row = document.createElement("div");
  row.className = "ai-test-log-entry";
  const time = document.createElement("span");
  time.className = "ai-test-log-time";
  time.textContent = new Date().toLocaleTimeString();
  const message = document.createElement("span");
  message.textContent = `[${progress.type}] ${progress.message}`;
  row.append(time, message);
  log.append(row);
  log.scrollTop = log.scrollHeight;
  if (progress.type === "model-start" && progress.content) {
    appendAiTesterApiRequest({ step: progress.step, endpoint: progress.endpoint ?? "unavailable", content: progress.content });
  }
  if (progress.type === "tool-complete" && progress.tool) appendAiTesterTool(progress.tool);
}

function renderAiTesterApiRequests(items: AiTesterApiRequestEntry[]): void {
  const target = element<HTMLElement>("#ai-test-api-requests");
  target.replaceChildren();
  items.forEach(appendAiTesterApiRequest);
  if (!items.length) setAiTesterEmpty(target, "暂无请求。");
}

function appendAiTesterApiRequest(item: AiTesterApiRequestEntry): void {
  const target = element<HTMLElement>("#ai-test-api-requests");
  if (target.querySelector(".muted")) target.replaceChildren();
  const card = document.createElement("div");
  card.className = "ai-test-audit-card";
  const header = document.createElement("header");
  const index = document.createElement("span");
  index.textContent = `#${item.step}`;
  const title = document.createElement("strong");
  title.textContent = "AI API request";
  const endpoint = document.createElement("span");
  endpoint.textContent = item.endpoint;
  header.append(index, title, endpoint);
  const pre = document.createElement("pre");
  pre.textContent = prettyJson(item.content);
  card.append(header, pre);
  target.append(card);
}

function renderAiTesterTools(items: AiTesterToolTimelineEntry[]): void {
  const target = element<HTMLElement>("#ai-test-tool-order");
  target.replaceChildren();
  const calls = items.filter(item => item.phase === "call" || item.phase === "cache-hit");
  calls.forEach(appendAiTesterTool);
  if (!calls.length) setAiTesterEmpty(target, "暂无工具调用。");
}

function appendAiTesterTool(item: AiTesterToolTimelineEntry): void {
  const target = element<HTMLElement>("#ai-test-tool-order");
  if (target.querySelector(".muted")) target.replaceChildren();
  const card = document.createElement("div");
  card.className = "ai-test-audit-card";
  const header = document.createElement("header");
  const source = document.createElement("span");
  source.textContent = item.source;
  const name = document.createElement("strong");
  name.textContent = item.name;
  const result = document.createElement("span");
  result.textContent = `${item.elapsed_milliseconds} ms · ${item.success ? "成功" : "失败"}`;
  header.append(source, name, result);
  card.append(header);
  for (const [label, content] of [["请求 Content", item.request_content], ["返回 Content", item.response_content]] as const) {
    const section = document.createElement("div");
    section.className = "ai-test-tool-payload";
    const strong = document.createElement("strong");
    strong.textContent = label;
    const pre = document.createElement("pre");
    pre.textContent = prettyJson(content ?? "unavailable");
    section.append(strong, pre);
    card.append(section);
  }
  target.append(card);
}

function renderAiTestFileCandidates(items: Array<{ name: string; file_episode_candidate: number | null }> | null): void {
  element<HTMLTextAreaElement>("#ai-test-file-candidates").value = items
    ? JSON.stringify(items, null, 2)
    : "unavailable";
  element<HTMLInputElement>("#ai-test-candidate-status").value = items
    ? `${items.filter(item => item.file_episode_candidate != null).length} / ${items.length}`
    : "unavailable";
}

function parseJsonOrText(value: string | null): unknown {
  if (!value) return null;
  try { return JSON.parse(value); } catch { return value; }
}

function prettyJson(value: string): string {
  try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; }
}

function resetAiTesterLiveOutput(): void {
  setAiTesterEmpty(element<HTMLElement>("#ai-test-execution-log"), "等待第一条进度…");
  setAiTesterEmpty(element<HTMLElement>("#ai-test-api-requests"), "暂无请求。");
  setAiTesterEmpty(element<HTMLElement>("#ai-test-tool-order"), "暂无工具调用。");
}

function setAiTesterEmpty(target: HTMLElement, message: string): void {
  const empty = document.createElement("p");
  empty.className = "muted";
  empty.textContent = message;
  target.replaceChildren(empty);
}

function persistAiTesterForm(): void {
  const data: Record<string, string | boolean> = {};
  for (const id of aiTesterPersistedFields) {
    const field = document.getElementById(id) as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement | null;
    if (!field) continue;
    data[id] = field instanceof HTMLInputElement && field.type === "checkbox" ? field.checked : field.value;
  }
  localStorage.setItem(aiTesterFormStorageKey, JSON.stringify(data));
}

function restoreAiTesterForm(): void {
  try {
    const data = JSON.parse(localStorage.getItem(aiTesterFormStorageKey) ?? "{}") as Record<string, unknown>;
    for (const id of aiTesterPersistedFields) {
      const field = document.getElementById(id) as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement | null;
      const value = data[id];
      if (!field || value == null) continue;
      if (field instanceof HTMLInputElement && field.type === "checkbox" && typeof value === "boolean") field.checked = value;
      else if (typeof value === "string") field.value = value;
    }
  } catch {
  }
}

function updateAiTesterSourceStates(): void {
  const mikan = element<HTMLInputElement>("#ai-test-is-mikan-source").checked;
  const anidb = element<HTMLInputElement>("#ai-test-anidbid").value.trim();
  const tmdbEnabled = element<HTMLInputElement>("#ai-test-enable-tmdb-mcp").checked;
  const tmdbState = element<HTMLElement>("#ai-test-tmdb-state");
  tmdbState.textContent = tmdbEnabled ? "TMDB MCP 已启用" : "TMDB MCP 已关闭";
  tmdbState.className = `badge ${tmdbEnabled ? "ok" : "error"}`;
  const mikanState = element<HTMLElement>("#ai-test-mikan-state");
  mikanState.textContent = mikan ? "Mikan RSS 来源" : "未启用来源";
  mikanState.className = `badge ${mikan ? "ok" : "pending"}`;
  const u2State = element<HTMLElement>("#ai-test-u2-state");
  u2State.textContent = anidb ? `AniDB ${anidb}` : "未填写 ID";
  u2State.className = `badge ${anidb ? "ok" : "pending"}`;
}

element<HTMLButtonElement>("#rss-reload").addEventListener("click", () => void loadRssRules());
element<HTMLFormElement>("#ai-test-form").addEventListener(
  "submit",
  event => void runAiMetadataTest(event),
);
element<HTMLButtonElement>("#ai-test-fill-example").addEventListener(
  "click",
  fillAiMetadataTestExample,
);
element<HTMLButtonElement>("#ai-test-mikan-import").addEventListener(
  "click",
  () => void importAiTestMikanEpisode(),
);
element<HTMLInputElement>("#ai-test-torrent-file").addEventListener("change", event => {
  const file = (event.currentTarget as HTMLInputElement).files?.[0];
  if (!file) return;
  element<HTMLElement>("#ai-test-message").textContent = "正在解析 Torrent…";
  void importAiTestTorrent(file).then(() => {
    element<HTMLElement>("#ai-test-message").textContent = "Torrent 已解析并建立可信 import_id。";
  }).catch(error => {
    element<HTMLElement>("#ai-test-message").textContent = `Torrent 解析失败：${errorMessage(error, "未知错误")}`;
  });
});
element<HTMLButtonElement>("#ai-test-stop").addEventListener(
  "click",
  () => void stopAiMetadataTest(),
);
element<HTMLButtonElement>("#ai-test-prompt-reset").addEventListener(
  "click",
  resetAiTestPrompt,
);
element<HTMLTextAreaElement>("#ai-test-prompt-template").addEventListener(
  "input",
  saveAiPromptDraft,
);
element<HTMLFormElement>("#ai-test-form").addEventListener("input", event => {
  const target = event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;
  persistAiTesterForm();
  updateAiTesterSourceStates();
});
element<HTMLSelectElement>("#library-sort").value = libraryState.sort;
element<HTMLInputElement>("#library-search").value = libraryState.search;
element<HTMLSelectElement>("#library-direction").value = libraryState.direction;
element<HTMLSelectElement>("#library-page-size").value = String(libraryState.page_size);
element<HTMLSelectElement>("#library-episode-filter").value = libraryState.episode_filter;
element<HTMLInputElement>("#metadata-search").value = metadataState.search;
element<HTMLSelectElement>("#metadata-status-filter").value = metadataState.status;
element<HTMLSelectElement>("#metadata-handling-filter").value = metadataState.handling;
element<HTMLSelectElement>("#metadata-file-state-filter").value = metadataState.file_state;
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
  metadataState.file_state =
    element<HTMLSelectElement>("#metadata-file-state-filter").value;
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
    file_state: "all",
    failure_stage: "",
    error_code: "",
    retryability: "all",
    sort: "updated",
    direction: "desc",
  };
  element<HTMLInputElement>("#metadata-search").value = "";
  element<HTMLSelectElement>("#metadata-status-filter").value = "";
  element<HTMLSelectElement>("#metadata-handling-filter").value = "all";
  element<HTMLSelectElement>("#metadata-file-state-filter").value = "all";
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
element<HTMLButtonElement>("#library-external-import").addEventListener(
  "click",
  () => void importExternalMedia("all"),
);
element<HTMLFormElement>("#library-search-form").addEventListener(
  "submit",
  searchLibrary,
);
element<HTMLButtonElement>("#library-search-clear").addEventListener(
  "click",
  clearLibrarySearch,
);
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
element<HTMLButtonElement>("#library-detail-external-import").addEventListener(
  "click",
  () => void importExternalMedia("season"),
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
element<HTMLButtonElement>("#configuration-archive-export").addEventListener("click", () => {
  void downloadConfigurationArchive(
    "/api/v1/configuration-archive/export",
    `animegonet-config-${new Date().toISOString().replace(/[:.]/g, "-")}.json`,
  ).then(() => {
    element<HTMLElement>("#configuration-archive-status").textContent =
      "配置已导出。文件包含敏感凭据，请妥善保管。";
  }).catch(error => {
    element<HTMLElement>("#configuration-archive-status").textContent =
      `导出失败：${errorMessage(error, "未知错误")}`;
  });
});
element<HTMLInputElement>("#configuration-archive-file").addEventListener("change", event => {
  pendingConfigurationArchive = (event.currentTarget as HTMLInputElement).files?.[0] ?? null;
  clearConfigurationArchivePreview(
    pendingConfigurationArchive
      ? `已选择 ${pendingConfigurationArchive.name}（${formatBytes(pendingConfigurationArchive.size)}），请先预检。`
      : "请选择由 AnimeGoNet 导出的 JSON。预检不会修改任何配置。",
  );
  element<HTMLButtonElement>("#configuration-archive-preview").disabled =
    pendingConfigurationArchive === null;
});
element<HTMLButtonElement>("#configuration-archive-preview").addEventListener(
  "click",
  () => void previewConfigurationArchive(),
);
element<HTMLButtonElement>("#configuration-archive-import").addEventListener(
  "click",
  () => void importConfigurationArchive(),
);
element<HTMLButtonElement>("#configuration-backup-create").addEventListener(
  "click",
  () => void createConfigurationBackup(),
);
element<HTMLButtonElement>("#configuration-backup-reload").addEventListener(
  "click",
  () => void loadConfigurationBackups(),
);
element<HTMLButtonElement>("#configuration-close").addEventListener("click", () => configurationDialog.close());
element<HTMLFormElement>("#configuration-form").addEventListener(
  "submit",
  (event) => void previewConfiguration(event),
);
element<HTMLButtonElement>("#configuration-confirm").addEventListener(
  "click",
  () => void confirmConfiguration(),
);
element<HTMLButtonElement>("#configuration-ai-prompt-reset").addEventListener(
  "click",
  () => void resetConfigurationAiPrompt(),
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
  element<HTMLInputElement>("#configuration-ai-key").value = "";
});
element<HTMLInputElement>("#configuration-tmdb-key-clear").addEventListener(
  "change",
  syncConfigurationSecretInputs,
);
element<HTMLInputElement>("#configuration-tmdb-token-clear").addEventListener(
  "change",
  syncConfigurationSecretInputs,
);
element<HTMLInputElement>("#configuration-ai-key-clear").addEventListener(
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
element<HTMLInputElement>("#source-rss-url-clear").addEventListener(
  "change",
  updateSourceCredentialInputs,
);
element<HTMLInputElement>("#source-enabled").addEventListener(
  "change",
  updateSourceCredentialInputs,
);
element<HTMLInputElement>("#source-rss-schedule-enabled").addEventListener(
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
element<HTMLSelectElement>("#manual-rss-source").addEventListener(
  "change",
  updateSavedRssRunState,
);
element<HTMLButtonElement>("#manual-rss-run-saved").addEventListener(
  "click",
  () => void runSavedSourceRss(),
);
element<HTMLButtonElement>("#manual-rss-manage-source").addEventListener(
  "click",
  openSelectedMikanSourceSettings,
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
element<HTMLButtonElement>("#external-plugin-reload").addEventListener(
  "click",
  () => void loadStatus(),
);
element<HTMLButtonElement>("#data-update-reload").addEventListener(
  "click",
  () => void loadDataUpdate(),
);
element<HTMLSelectElement>("#data-update-usage-kind").addEventListener(
  "change",
  event => {
    dataUpdateUsageKind = (event.currentTarget as HTMLSelectElement).value as
      typeof dataUpdateUsageKind;
    dataUpdateUsagePage = 1;
    void loadBangumiArchiveUsage();
  },
);
element<HTMLSelectElement>("#data-update-usage-page-size").addEventListener(
  "change",
  event => {
    dataUpdateUsagePageSize = Number(
      (event.currentTarget as HTMLSelectElement).value,
    ) as typeof dataUpdateUsagePageSize;
    dataUpdateUsagePage = 1;
    void loadBangumiArchiveUsage();
  },
);
element<HTMLButtonElement>("#data-update-usage-previous").addEventListener(
  "click",
  () => {
    if (dataUpdateUsagePage <= 1) return;
    dataUpdateUsagePage--;
    void loadBangumiArchiveUsage();
  },
);
element<HTMLButtonElement>("#data-update-usage-next").addEventListener(
  "click",
  () => {
    dataUpdateUsagePage++;
    void loadBangumiArchiveUsage();
  },
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
element<HTMLSelectElement>("#cache-database").addEventListener("change", event => {
  cacheDatabase = (event.currentTarget as HTMLSelectElement).value as CacheDatabase;
  activeCacheBucketId = null;
  cachePage = 1;
  void loadCacheBuckets();
});
element<HTMLButtonElement>("#cache-reload").addEventListener(
  "click",
  () => void loadCacheBuckets(),
);
element<HTMLButtonElement>("#cache-entry-dialog-close").addEventListener(
  "click",
  () => cacheEntryDialog.close(),
);
element<HTMLButtonElement>("#other-readaptation-review-close").addEventListener(
  "click",
  () => {
    if (!otherReadaptationReviewSubmitting) otherReadaptationReviewDialog.close();
  },
);
otherReadaptationReviewCancel.addEventListener("click", () => {
  if (!otherReadaptationReviewSubmitting) otherReadaptationReviewDialog.close();
});
otherReadaptationReviewConfirm.addEventListener(
  "click",
  () => void confirmOtherReadaptationReview(),
);
otherReadaptationReviewDialog.addEventListener("cancel", event => {
  if (otherReadaptationReviewSubmitting) event.preventDefault();
});
otherReadaptationReviewDialog.addEventListener("close", () => {
  const active = activeOtherReadaptationReview;
  if (active?.triggerButton.isConnected) {
    active.triggerButton.disabled = false;
    active.triggerButton.textContent = active.defaultLabel;
  }
  activeOtherReadaptationReview = null;
  element<HTMLElement>("#other-readaptation-review-summary").replaceChildren();
  element<HTMLElement>("#other-readaptation-review-files").replaceChildren();
  element<HTMLElement>("#other-readaptation-review-message").textContent = "";
  otherReadaptationReviewConfirm.disabled = false;
  otherReadaptationReviewConfirm.hidden = false;
  otherReadaptationReviewCancel.disabled = false;
  otherReadaptationReviewCancel.textContent = "返回检查";
  otherReadaptationReviewConfirm.textContent = "确认审核通过";
});
element<HTMLButtonElement>("#cache-previous").addEventListener("click", () => {
  if (cachePage <= 1) return;
  cachePage--;
  void loadCacheEntries();
});
element<HTMLButtonElement>("#cache-next").addEventListener("click", () => {
  if (cachePage * cachePageSize >= cacheTotalCount) return;
  cachePage++;
  void loadCacheEntries();
});
element<HTMLSelectElement>("#live-log-level").addEventListener(
  "change",
  renderLiveLogs,
);
for (const selector of [
  "#live-log-search",
  "#live-log-category",
  "#live-log-http-scope",
  "#live-log-event-id",
  "#live-log-from",
  "#live-log-to",
]) {
  element<HTMLInputElement>(selector).addEventListener("input", renderLiveLogs);
}
element<HTMLInputElement>("#live-log-exception-only").addEventListener(
  "change",
  renderLiveLogs,
);
document.querySelectorAll<HTMLButtonElement>("[data-log-domain]").forEach(button => {
  button.addEventListener("click", () => {
    const domain = button.dataset.logDomain ?? "all";
    if (domain === "all" || ["ai", "tmdb", "metadata", "download", "rss", "organize", "system"].includes(domain)) {
      setLiveLogDomain(domain as LiveLogDomain | "all");
    }
  });
});
element<HTMLInputElement>("#live-log-auto-scroll").addEventListener(
  "change",
  renderLiveLogs,
);
element<HTMLInputElement>("#live-log-wrap").addEventListener(
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
element<HTMLButtonElement>("#live-log-copy").addEventListener(
  "click",
  () => void copyVisibleLiveLogs(),
);
element<HTMLButtonElement>("#live-log-clear").addEventListener("click", () => {
  liveLogEntries = [];
  renderLiveLogs();
});
element<HTMLFormElement>("#ai-log-filters").addEventListener("submit", event => {
  event.preventDefault();
  aiLogPage = 1;
  aiLogPageSize = Number(element<HTMLSelectElement>("#ai-log-page-size").value);
  void loadAiInvocationLogs();
});
element<HTMLButtonElement>("#ai-log-reset").addEventListener("click", () => {
  element<HTMLFormElement>("#ai-log-filters").reset();
  element<HTMLSelectElement>("#ai-log-page-size").value = "25";
  aiLogPage = 1;
  aiLogPageSize = 25;
  void loadAiInvocationLogs();
});
element<HTMLButtonElement>("#ai-log-reload").addEventListener(
  "click",
  () => void loadAiInvocationLogs(),
);
element<HTMLButtonElement>("#ai-log-previous").addEventListener("click", () => {
  if (aiLogPage <= 1) return;
  aiLogPage--;
  void loadAiInvocationLogs();
});
element<HTMLButtonElement>("#ai-log-next").addEventListener("click", () => {
  if (aiLogPage * aiLogPageSize >= aiLogTotalItems) return;
  aiLogPage++;
  void loadAiInvocationLogs();
});
element<HTMLButtonElement>("#ai-debug-close").addEventListener("click", () => aiDebugDialog.close());
element<HTMLButtonElement>("#ai-debug-delete").addEventListener(
  "click",
  () => void deleteActiveAiDebugChain(),
);
aiDebugDialog.addEventListener("close", () => {
  activeAiDebugRunId = null;
  element<HTMLElement>("#ai-debug-content").replaceChildren();
});
window.addEventListener("beforeunload", () => {
  liveLogShouldReconnect = false;
  if (liveLogReconnectTimer !== null) {
    window.clearTimeout(liveLogReconnectTimer);
    liveLogReconnectTimer = null;
  }
  disconnectCurrentLiveLogSocket();
});

initializeWorkspaceNavigation();
void loadAiTestPrompt();
void loadStatus();
void loadDirectoryDatabase();
void loadDataUpdate();
void loadCacheBuckets();
connectLiveLogs();
void loadLibrary();
void loadConfiguration();
void loadConfigurationBackups();
void loadDownloads();
void loadMetadataTasks();
void loadPendingTmdb();
void loadDownloaders();
void loadSources();
void loadRssRules();
void loadLegacyMikanFilter();
void loadTrustedOffsets();
window.setInterval(() => {
  if (isSubviewVisible("tasks", "downloads")) void loadDownloads(true);
}, 5000);
window.setInterval(() => {
  if (isSubviewVisible("tasks", "metadata")) void loadMetadataTasks(true);
}, 5000);
window.setInterval(() => {
  if (isSubviewVisible("library", "pending")
    && !hasFocusedEditorWithin("#pending-tmdb") && !hasOpenDialog()) {
    void loadPendingTmdb();
  }
}, 10000);
window.setInterval(() => {
  if (isSubviewVisible("bangumi-cache", "versions")
    && !dataUpdateActionRunning
    && !hasFocusedEditorWithin("#data-update") && !hasOpenDialog()) {
    void loadDataUpdate(true);
  }
}, 3000);
window.setInterval(() => {
  if (isSubviewVisible("library", "seasons") && activeLibraryDetail === null
    && !hasFocusedEditorWithin("#anime-library") && !hasOpenDialog()) {
    void loadLibrary();
  }
}, 15000);
