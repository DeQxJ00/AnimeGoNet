using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnimeGoNet.App.Api;

public sealed record LegacyRssRequest(
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("rss")] LegacyRssLocation? Rss,
    [property: JsonPropertyName("is_select_ep")] bool IsSelectEp,
    [property: JsonPropertyName("ep_links")] IReadOnlyList<string>? EpLinks);

public sealed record LegacyRssLocation(
    [property: JsonPropertyName("url")] string? Url);

public sealed record RssIngestRequest(
    [property: JsonPropertyName("source_profile_id")] string? SourceProfileId,
    [property: JsonPropertyName("url")] string? Url);

public sealed record LegacyApiResponse<T>(int Code, string Msg, T Data);

public sealed record LegacyPluginConfigUploadRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("data")] string? Data);

public sealed record LegacyPluginResponse(
    [property: JsonPropertyName("name")] string Name);

public sealed record LegacyPluginConfigResponse(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("data")] string Data);

public sealed record LegacyBoltListResponse(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("bucket")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Bucket,
    [property: JsonPropertyName("data")] IReadOnlyList<string> Data);

public sealed record LegacyBoltGetResponse(
    [property: JsonPropertyName("bucket")] string Bucket,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("ttl")] long Ttl,
    [property: JsonPropertyName("value")] JsonElement Value);

public sealed record LegacyBoltDeleteResponse;

public sealed record PingData(string Version, long Time);

public sealed record RuntimeStatus(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("database_schema_version")] int DatabaseSchemaVersion,
    [property: JsonPropertyName("native_aot")] bool NativeAot,
    [property: JsonPropertyName("runtime_identifier")] string RuntimeIdentifier,
    [property: JsonPropertyName("paths")] RuntimePaths Paths,
    [property: JsonPropertyName("capabilities")] RuntimeCapabilities Capabilities);

public sealed record RuntimePaths(
    [property: JsonPropertyName("data_path")] string DataPath,
    [property: JsonPropertyName("download_path")] string DownloadPath,
    [property: JsonPropertyName("save_path")] string SavePath);

public sealed record RuntimeCapabilities(
    [property: JsonPropertyName("configuration")] bool Configuration,
    [property: JsonPropertyName("sqlite")] bool Sqlite,
    [property: JsonPropertyName("unified_ingest")] bool UnifiedIngest,
    [property: JsonPropertyName("rss_rules")] bool RssRules,
    [property: JsonPropertyName("qbittorrent")] bool Qbittorrent,
    [property: JsonPropertyName("tmdb")] bool Tmdb,
    [property: JsonPropertyName("organizer")] bool Organizer,
    [property: JsonPropertyName("deletion")] bool Deletion);

public sealed record ConfigurationResponse(
    [property: JsonPropertyName("configuration_revision")] long ConfigurationRevision,
    [property: JsonPropertyName("applied_configuration_revision")] long AppliedConfigurationRevision,
    [property: JsonPropertyName("restart_required")] bool RestartRequired,
    [property: JsonPropertyName("paths")] RuntimePaths Paths,
    [property: JsonPropertyName("deployment")] DeploymentConfigurationResponse Deployment,
    [property: JsonPropertyName("metadata")] MetadataConfigurationResponse Metadata,
    [property: JsonPropertyName("torrent_fetch")] TorrentFetchConfigurationResponse TorrentFetch,
    [property: JsonPropertyName("editable")] EditableConfigurationResponse Editable);

public sealed record EditableConfigurationResponse(
    [property: JsonPropertyName("tmdb_base_url")] string TmdbBaseUrl,
    [property: JsonPropertyName("tmdb_proxy_url")] string? TmdbProxyUrl,
    [property: JsonPropertyName("tmdb_language")] string TmdbLanguage,
    [property: JsonPropertyName("tmdb_http_timeout_seconds")] double TmdbHttpTimeoutSeconds,
    [property: JsonPropertyName("tmdb_api_key_state")] string TmdbApiKeyState,
    [property: JsonPropertyName("tmdb_read_access_token_state")] string TmdbReadAccessTokenState,
    [property: JsonPropertyName("bangumi_base_url")] string BangumiBaseUrl,
    [property: JsonPropertyName("bangumi_proxy_url")] string? BangumiProxyUrl,
    [property: JsonPropertyName("bangumi_http_timeout_seconds")] double BangumiHttpTimeoutSeconds,
    [property: JsonPropertyName("season_failure_skip")] bool SeasonFailureSkip,
    [property: JsonPropertyName("season_failure_backtrace")] bool SeasonFailureBacktrace,
    [property: JsonPropertyName("season_failure_use_title_season")] bool SeasonFailureUseTitleSeason,
    [property: JsonPropertyName("season_failure_use_first_season")] bool SeasonFailureUseFirstSeason,
    [property: JsonPropertyName("ai_use_metadata_match")] bool AiUseMetadataMatch,
    [property: JsonPropertyName("ai_use_season_match")] bool AiUseSeasonMatch,
    [property: JsonPropertyName("ai_use_episode_match")] bool AiUseEpisodeMatch,
    [property: JsonPropertyName("ai_http_timeout_seconds")] double AiHttpTimeoutSeconds,
    [property: JsonPropertyName("tmdb_failure_use_bangumi")] bool TmdbFailureUseBangumi,
    [property: JsonPropertyName("mikan_trusted_offset_cache_enabled")] bool MikanTrustedOffsetCacheEnabled,
    [property: JsonPropertyName("torrent_http_timeout_seconds")] double TorrentHttpTimeoutSeconds,
    [property: JsonPropertyName("torrent_max_response_bytes")] long TorrentMaxResponseBytes,
    [property: JsonPropertyName("torrent_max_redirects")] int TorrentMaxRedirects,
    [property: JsonPropertyName("torrent_staging_ttl_seconds")] double TorrentStagingTtlSeconds);

public sealed record ConfigurationUpdateRequest(
    [property: JsonPropertyName("tmdb_base_url")] string? TmdbBaseUrl,
    [property: JsonPropertyName("tmdb_proxy_url")] string? TmdbProxyUrl,
    [property: JsonPropertyName("tmdb_language")] string? TmdbLanguage,
    [property: JsonPropertyName("tmdb_http_timeout_seconds")] double TmdbHttpTimeoutSeconds,
    [property: JsonPropertyName("tmdb_api_key")] string? TmdbApiKey,
    [property: JsonPropertyName("clear_tmdb_api_key")] bool ClearTmdbApiKey,
    [property: JsonPropertyName("tmdb_read_access_token")] string? TmdbReadAccessToken,
    [property: JsonPropertyName("clear_tmdb_read_access_token")] bool ClearTmdbReadAccessToken,
    [property: JsonPropertyName("bangumi_base_url")] string? BangumiBaseUrl,
    [property: JsonPropertyName("bangumi_proxy_url")] string? BangumiProxyUrl,
    [property: JsonPropertyName("bangumi_http_timeout_seconds")] double BangumiHttpTimeoutSeconds,
    [property: JsonPropertyName("season_failure_skip")] bool SeasonFailureSkip,
    [property: JsonPropertyName("season_failure_backtrace")] bool SeasonFailureBacktrace,
    [property: JsonPropertyName("season_failure_use_title_season")] bool SeasonFailureUseTitleSeason,
    [property: JsonPropertyName("season_failure_use_first_season")] bool SeasonFailureUseFirstSeason,
    [property: JsonPropertyName("ai_use_metadata_match")] bool? AiUseMetadataMatch,
    [property: JsonPropertyName("ai_use_season_match")] bool? AiUseSeasonMatch,
    [property: JsonPropertyName("ai_use_episode_match")] bool? AiUseEpisodeMatch,
    [property: JsonPropertyName("ai_http_timeout_seconds")] double AiHttpTimeoutSeconds,
    [property: JsonPropertyName("tmdb_failure_use_bangumi")] bool TmdbFailureUseBangumi,
    [property: JsonPropertyName("mikan_trusted_offset_cache_enabled")] bool MikanTrustedOffsetCacheEnabled,
    [property: JsonPropertyName("torrent_http_timeout_seconds")] double TorrentHttpTimeoutSeconds,
    [property: JsonPropertyName("torrent_max_response_bytes")] long TorrentMaxResponseBytes,
    [property: JsonPropertyName("torrent_max_redirects")] int TorrentMaxRedirects,
    [property: JsonPropertyName("torrent_staging_ttl_seconds")] double TorrentStagingTtlSeconds,
    [property: JsonPropertyName("expected_configuration_revision")] long ExpectedConfigurationRevision);

public sealed record ConfigurationWriteResponse(
    [property: JsonPropertyName("configuration_revision")] long ConfigurationRevision,
    [property: JsonPropertyName("restart_required")] bool RestartRequired,
    [property: JsonPropertyName("reverted_to_deployment_default")] bool RevertedToDeploymentDefault);

public sealed record DeploymentConfigurationResponse(
    [property: JsonPropertyName("running_in_container")] bool RunningInContainer,
    [property: JsonPropertyName("background_workers_enabled")] bool BackgroundWorkersEnabled,
    [property: JsonPropertyName("access_key_configured")] bool AccessKeyConfigured,
    [property: JsonPropertyName("paths_restart_required")] bool PathsRestartRequired);

public sealed record MetadataConfigurationResponse(
    [property: JsonPropertyName("tmdb")] TmdbConfigurationResponse Tmdb,
    [property: JsonPropertyName("bangumi")] BangumiConfigurationResponse Bangumi,
    [property: JsonPropertyName("season_failure")] SeasonFailureConfigurationResponse SeasonFailure,
    [property: JsonPropertyName("ai")] AiConfigurationResponse Ai,
    [property: JsonPropertyName("tmdb_failure_use_bangumi")] bool TmdbFailureUseBangumi,
    [property: JsonPropertyName("mikan_trusted_offset_cache_enabled")] bool MikanTrustedOffsetCacheEnabled);

public sealed record TmdbConfigurationResponse(
    [property: JsonPropertyName("base_url")] string BaseUrl,
    [property: JsonPropertyName("proxy_url")] string? ProxyUrl,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("http_timeout_seconds")] double HttpTimeoutSeconds,
    [property: JsonPropertyName("api_key_configured")] bool ApiKeyConfigured,
    [property: JsonPropertyName("read_access_token_configured")] bool ReadAccessTokenConfigured);

public sealed record BangumiConfigurationResponse(
    [property: JsonPropertyName("base_url")] string BaseUrl,
    [property: JsonPropertyName("proxy_url")] string? ProxyUrl,
    [property: JsonPropertyName("http_timeout_seconds")] double HttpTimeoutSeconds);

public sealed record SeasonFailureConfigurationResponse(
    [property: JsonPropertyName("skip")] bool Skip,
    [property: JsonPropertyName("backtrace")] bool Backtrace,
    [property: JsonPropertyName("use_title_season")] bool UseTitleSeason,
    [property: JsonPropertyName("use_first_season")] bool UseFirstSeason);

public sealed record AiConfigurationResponse(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("base_url")] string? BaseUrl,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("api_key_configured")] bool ApiKeyConfigured,
    [property: JsonPropertyName("use_metadata_match")] bool UseMetadataMatch,
    [property: JsonPropertyName("use_season_match")] bool UseSeasonMatch,
    [property: JsonPropertyName("use_episode_match")] bool UseEpisodeMatch,
    [property: JsonPropertyName("http_timeout_seconds")] double HttpTimeoutSeconds,
    [property: JsonPropertyName("retry_count")] int RetryCount,
    [property: JsonPropertyName("use_bangumi_pubdate_first")] bool UseBangumiPubDateFirst,
    [property: JsonPropertyName("tmdb_mcp_url")] string TmdbMcpUrl,
    [property: JsonPropertyName("bangumi_mcp_url")] string BangumiMcpUrl);

public sealed record TorrentFetchConfigurationResponse(
    [property: JsonPropertyName("http_timeout_seconds")] double HttpTimeoutSeconds,
    [property: JsonPropertyName("max_response_bytes")] long MaxResponseBytes,
    [property: JsonPropertyName("max_redirects")] int MaxRedirects,
    [property: JsonPropertyName("staging_ttl_seconds")] double StagingTtlSeconds);

public sealed record IngestBatchRequest(
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("data")] IReadOnlyList<IngestItemRequest?>? Data);

public sealed record IngestItemRequest(
    [property: JsonPropertyName("torrent")] string? Torrent,
    [property: JsonPropertyName("info")] IngestItemInfoRequest? Info);

public sealed record IngestItemInfoRequest(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("source_item_id")] string? SourceItemId,
    [property: JsonPropertyName("source_work_id")] string? SourceWorkId,
    [property: JsonPropertyName("mikan_url")] string? MikanUrl,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("bgmid")] int? BangumiId,
    [property: JsonPropertyName("anidbid")] int? AniDbId,
    [property: JsonPropertyName("imdbid")] string? ImdbId);

public sealed record IngestBatchResponse(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("accepted_count")] int AcceptedCount,
    [property: JsonPropertyName("rejected_count")] int RejectedCount,
    [property: JsonPropertyName("items")] IReadOnlyList<IngestItemResponse> Items);

public sealed record IngestItemResponse(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("ingest_id")] string? IngestId,
    [property: JsonPropertyName("source_profile_id")] string? SourceProfileId,
    [property: JsonPropertyName("source_profile_revision")] long? SourceProfileRevision,
    [property: JsonPropertyName("downloader_id")] string? DownloaderId,
    [property: JsonPropertyName("torrent_url_fingerprint")] string? TorrentUrlFingerprint,
    [property: JsonPropertyName("info_hash")] string? InfoHash,
    [property: JsonPropertyName("file_count")] int? FileCount,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record DownloadListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<DownloadListItem> Items);

public sealed record DownloadListItem(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("downloader_id")] string DownloaderId,
    [property: JsonPropertyName("info_hash")] string InfoHash,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("business_status")] string BusinessStatus,
    [property: JsonPropertyName("progress")] double Progress,
    [property: JsonPropertyName("downloaded_bytes")] long DownloadedBytes,
    [property: JsonPropertyName("total_bytes")] long TotalBytes,
    [property: JsonPropertyName("speed_bytes_per_second")] long SpeedBytesPerSecond,
    [property: JsonPropertyName("eta_seconds")] long? EtaSeconds,
    [property: JsonPropertyName("seeds")] int Seeds,
    [property: JsonPropertyName("peers")] int Peers,
    [property: JsonPropertyName("is_stale")] bool IsStale,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("snapshot_at_utc")] DateTimeOffset? SnapshotAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("downloader_connected")] bool DownloaderConnected,
    [property: JsonPropertyName("downloader_failure_code")] string? DownloaderFailureCode,
    [property: JsonPropertyName("downloader_last_success_at_utc")] DateTimeOffset? DownloaderLastSuccessAtUtc);

public sealed record MikanWorkRuleRequest(
    [property: JsonPropertyName("bgmid")] int? BangumiSubjectId,
    [property: JsonPropertyName("tmdb_series_id")] int? TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int? TmdbSeasonNumber,
    [property: JsonPropertyName("episode_offset")] int? EpisodeOffset,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("expected_revision")] long ExpectedRevision,
    [property: JsonPropertyName("sample_source_episode")] int? SampleSourceEpisode = null);

public sealed record MikanWorkRuleResponse(
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("bgmid")] int? BangumiSubjectId,
    [property: JsonPropertyName("tmdb_series_id")] int? TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int? TmdbSeasonNumber,
    [property: JsonPropertyName("episode_offset")] int? EpisodeOffset,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record MikanTrustedOffsetListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<MikanTrustedOffsetItemResponse> Items);

public sealed record MikanTrustedOffsetItemResponse(
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("groupid")] int GroupId,
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber,
    [property: JsonPropertyName("episode_offset")] int EpisodeOffset,
    [property: JsonPropertyName("distinct_episode_count")] int DistinctEpisodeCount,
    [property: JsonPropertyName("required_episode_count")] int RequiredEpisodeCount,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record MetadataRetryResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("status")] string Status);

public sealed record MetadataTaskListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<MetadataTaskListItem> Items);

public sealed record MetadataTaskListItem(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("bgmid")] int? BangumiSubjectId,
    [property: JsonPropertyName("tmdb_series_id")] int? TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int? TmdbSeasonNumber,
    [property: JsonPropertyName("series_strategy")] string? SeriesStrategy,
    [property: JsonPropertyName("season_strategy")] string? SeasonStrategy,
    [property: JsonPropertyName("episode_strategy")] string? EpisodeStrategy,
    [property: JsonPropertyName("failure_kind")] string? FailureKind,
    [property: JsonPropertyName("failure_reason")] string? FailureReason,
    [property: JsonPropertyName("episode_file_count")] int EpisodeFileCount,
    [property: JsonPropertyName("other_file_count")] int OtherFileCount,
    [property: JsonPropertyName("duplicate_file_count")] int DuplicateFileCount,
    [property: JsonPropertyName("pending_file_count")] int PendingFileCount,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record MetadataAttemptListResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("items")] IReadOnlyList<MetadataAttemptItemResponse> Items);

public sealed record MetadataAttemptItemResponse(
    [property: JsonPropertyName("attempt_id")] string AttemptId,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("run_attempt_number")] int RunAttemptNumber,
    [property: JsonPropertyName("run_status")] string RunStatus,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("strategy")] string Strategy,
    [property: JsonPropertyName("priority")] int? Priority,
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("retryable")] bool Retryable,
    [property: JsonPropertyName("attempt_number")] int AttemptNumber,
    [property: JsonPropertyName("duration_ms")] long DurationMilliseconds,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("run_started_at_utc")] DateTimeOffset RunStartedAtUtc,
    [property: JsonPropertyName("run_completed_at_utc")] DateTimeOffset? RunCompletedAtUtc);

public sealed record AnimeSeasonListResponse(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total_items")] int TotalItems,
    [property: JsonPropertyName("sort")] string Sort,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("items")] IReadOnlyList<AnimeSeasonListItemResponse> Items);

public sealed record AnimeSeasonListItemResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("sort_name")] string SortName,
    [property: JsonPropertyName("season_name")] string SeasonName,
    [property: JsonPropertyName("poster_path")] string? PosterPath,
    [property: JsonPropertyName("poster_source")] string PosterSource,
    [property: JsonPropertyName("poster_url")] string PosterUrl,
    [property: JsonPropertyName("air_date")] DateOnly? AirDate,
    [property: JsonPropertyName("added_at_utc")] DateTimeOffset AddedAtUtc,
    [property: JsonPropertyName("last_updated_at_utc")] DateTimeOffset LastUpdatedAtUtc,
    [property: JsonPropertyName("episode_total")] int EpisodeTotal,
    [property: JsonPropertyName("episode_snapshot_count")] int EpisodeSnapshotCount,
    [property: JsonPropertyName("episode_downloaded")] int EpisodeDownloaded,
    [property: JsonPropertyName("series_resolution_source")] string? SeriesResolutionSource,
    [property: JsonPropertyName("season_resolution_source")] string? SeasonResolutionSource,
    [property: JsonPropertyName("validation_status")] string ValidationStatus,
    [property: JsonPropertyName("last_resolution_run_id")] string? LastResolutionRunId,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

public sealed record AnimeSeasonDetailResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("season_name")] string SeasonName,
    [property: JsonPropertyName("poster_path")] string? PosterPath,
    [property: JsonPropertyName("poster_source")] string PosterSource,
    [property: JsonPropertyName("poster_url")] string PosterUrl,
    [property: JsonPropertyName("air_date")] DateOnly? AirDate,
    [property: JsonPropertyName("added_at_utc")] DateTimeOffset AddedAtUtc,
    [property: JsonPropertyName("last_updated_at_utc")] DateTimeOffset LastUpdatedAtUtc,
    [property: JsonPropertyName("episode_total")] int EpisodeTotal,
    [property: JsonPropertyName("episode_snapshot_count")] int EpisodeSnapshotCount,
    [property: JsonPropertyName("episode_downloaded")] int EpisodeDownloaded,
    [property: JsonPropertyName("series_resolution_source")] string? SeriesResolutionSource,
    [property: JsonPropertyName("season_resolution_source")] string? SeasonResolutionSource,
    [property: JsonPropertyName("validation_status")] string ValidationStatus,
    [property: JsonPropertyName("last_resolution_run_id")] string? LastResolutionRunId,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("episodes")] IReadOnlyList<AnimeEpisodeItemResponse> Episodes);

public sealed record AnimeEpisodeItemResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("tmdb_episode_id")] int TmdbEpisodeId,
    [property: JsonPropertyName("episode_number")] int EpisodeNumber,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("air_date")] DateOnly? AirDate,
    [property: JsonPropertyName("runtime_minutes")] int? RuntimeMinutes,
    [property: JsonPropertyName("fetched_at_utc")] DateTimeOffset FetchedAtUtc,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("downloaded_at_utc")] DateTimeOffset? DownloadedAtUtc,
    [property: JsonPropertyName("media_path_known")] bool MediaPathKnown);

public sealed record PendingTmdbListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<PendingTmdbListItem> Items);

public sealed record PendingTmdbListItem(
    [property: JsonPropertyName("bgmid")] int BangumiSubjectId,
    [property: JsonPropertyName("fallback_name")] string FallbackName,
    [property: JsonPropertyName("season_numbers")] IReadOnlyList<int> SeasonNumbers,
    [property: JsonPropertyName("task_count")] int TaskCount,
    [property: JsonPropertyName("processed_file_count")] int ProcessedFileCount,
    [property: JsonPropertyName("fallback_record_count")] int FallbackRecordCount,
    [property: JsonPropertyName("active_claim_count")] int ActiveClaimCount,
    [property: JsonPropertyName("completed_claim_count")] int CompletedClaimCount,
    [property: JsonPropertyName("duplicate_file_count")] int DuplicateFileCount,
    [property: JsonPropertyName("latest_failure_kind")] string? LatestFailureKind,
    [property: JsonPropertyName("latest_failure_reason")] string? LatestFailureReason,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record PendingTmdbDetailResponse(
    [property: JsonPropertyName("summary")] PendingTmdbListItem Summary,
    [property: JsonPropertyName("tasks")] IReadOnlyList<PendingTmdbTaskItem> Tasks,
    [property: JsonPropertyName("scopes")] IReadOnlyList<PendingTmdbScopeItem> Scopes,
    [property: JsonPropertyName("recovery_candidates")]
    IReadOnlyList<PendingTmdbRecoveryCandidateItem> RecoveryCandidates);

public sealed record PendingTmdbTaskItem(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("season_number")] int? SeasonNumber,
    [property: JsonPropertyName("other_file_count")] int OtherFileCount,
    [property: JsonPropertyName("duplicate_file_count")] int DuplicateFileCount,
    [property: JsonPropertyName("failure_kind")] string? FailureKind,
    [property: JsonPropertyName("failure_reason")] string? FailureReason,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record PendingTmdbScopeItem(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("source_episode")] string? SourceEpisode,
    [property: JsonPropertyName("dedup_boundary")] string DedupBoundary,
    [property: JsonPropertyName("cross_source_duplicate_risk")] bool CrossSourceDuplicateRisk,
    [property: JsonPropertyName("completed_at_utc")] DateTimeOffset? CompletedAtUtc);

public sealed record PendingTmdbRecoveryCandidateItem(
    [property: JsonPropertyName("fallback_record_id")] string FallbackRecordId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("source_episode")] string? SourceEpisode,
    [property: JsonPropertyName("dedup_boundary")] string DedupBoundary,
    [property: JsonPropertyName("completed_at_utc")] DateTimeOffset CompletedAtUtc);

public sealed record PendingTmdbRecoveryRequest(
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("mappings")] IReadOnlyList<PendingTmdbRecoveryMappingRequest>? Mappings);

public sealed record PendingTmdbRecoveryMappingRequest(
    [property: JsonPropertyName("fallback_record_id")] string? FallbackRecordId,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber,
    [property: JsonPropertyName("tmdb_episode_number")] int TmdbEpisodeNumber);

public sealed record PendingTmdbRecoveryResponse(
    [property: JsonPropertyName("bgmid")] int BangumiSubjectId,
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("has_pending_fallback_records")] bool HasPendingFallbackRecords,
    [property: JsonPropertyName("items")] IReadOnlyList<PendingTmdbRecoveryItemResponse> Items);

public sealed record PendingTmdbRecoveryItemResponse(
    [property: JsonPropertyName("fallback_record_id")] string FallbackRecordId,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber,
    [property: JsonPropertyName("tmdb_episode_number")] int TmdbEpisodeNumber,
    [property: JsonPropertyName("state")] string State);

public sealed record ApiErrorResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

public sealed record DeleteTargetResponse(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("root_path")] string? RootPath,
    [property: JsonPropertyName("downloader_id")] string? DownloaderId,
    [property: JsonPropertyName("display_value")] string DisplayValue,
    [property: JsonPropertyName("state")] string? State = null);

public sealed record DeletePreviewResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("task_status")] string TaskStatus,
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("business_records")] IReadOnlyList<DeleteTargetResponse> BusinessRecords,
    [property: JsonPropertyName("downloader_tasks")] IReadOnlyList<DeleteTargetResponse> DownloaderTasks,
    [property: JsonPropertyName("source_files")] IReadOnlyList<DeleteTargetResponse> SourceFiles,
    [property: JsonPropertyName("media_files")] IReadOnlyList<DeleteTargetResponse> MediaFiles);

public sealed record CreateDeleteExecutionRequest(
    [property: JsonPropertyName("fingerprint")] string? Fingerprint,
    [property: JsonPropertyName("delete_business_record")] bool DeleteBusinessRecord,
    [property: JsonPropertyName("delete_downloader_task")] bool DeleteDownloaderTask,
    [property: JsonPropertyName("delete_source_files")] bool DeleteSourceFiles,
    [property: JsonPropertyName("delete_media_files")] bool DeleteMediaFiles);

public sealed record CreateDeleteExecutionResponse(
    [property: JsonPropertyName("execution_id")] string ExecutionId,
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("selected_target_count")] int SelectedTargetCount);

public sealed record DeleteExecutionStatusResponse(
    [property: JsonPropertyName("execution_id")] string ExecutionId,
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("failure_reason")] string? FailureReason,
    [property: JsonPropertyName("attempt_count")] int AttemptCount,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("completed_at_utc")] DateTimeOffset? CompletedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<DeleteTargetResponse> Items);

public sealed record RssNamedArrayRequest(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("values")] IReadOnlyList<string?>? Values);

public sealed record RssPriorityGroupRequest(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("arrays")] IReadOnlyList<RssNamedArrayRequest?>? Arrays);

public sealed record RssRuleSetRequest(
    [property: JsonPropertyName("expected_revision")] long ExpectedRevision,
    [property: JsonPropertyName("whitelist")] IReadOnlyList<RssNamedArrayRequest?>? Whitelist,
    [property: JsonPropertyName("blacklist")] IReadOnlyList<RssNamedArrayRequest?>? Blacklist,
    [property: JsonPropertyName("priority_groups")] IReadOnlyList<RssPriorityGroupRequest?>? PriorityGroups);

public sealed record RssNamedArrayResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("values")] IReadOnlyList<string> Values);

public sealed record RssPriorityGroupResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arrays")] IReadOnlyList<RssNamedArrayResponse> Arrays);

public sealed record RssRuleSetResponse(
    [property: JsonPropertyName("source_profile_id")] string SourceProfileId,
    [property: JsonPropertyName("rss_filter_enabled")] bool RssFilterEnabled,
    [property: JsonPropertyName("rss_priority_enabled")] bool RssPriorityEnabled,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("whitelist")] IReadOnlyList<RssNamedArrayResponse> Whitelist,
    [property: JsonPropertyName("blacklist")] IReadOnlyList<RssNamedArrayResponse> Blacklist,
    [property: JsonPropertyName("priority_groups")] IReadOnlyList<RssPriorityGroupResponse> PriorityGroups,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record RssPreviewCandidateRequest(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("source_episode_kind")] string? SourceEpisodeKind,
    [property: JsonPropertyName("source_episode")] string? SourceEpisode);

public sealed record RssRulePreviewRequest(
    [property: JsonPropertyName("candidates")] IReadOnlyList<RssPreviewCandidateRequest?>? Candidates);

public sealed record RssRuleDecisionResponse(
    [property: JsonPropertyName("candidate_id")] string CandidateId,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("winner_id")] string? WinnerId,
    [property: JsonPropertyName("evaluated_priority_groups")] IReadOnlyList<string> EvaluatedPriorityGroups);

public sealed record RssRulePreviewResponse(
    [property: JsonPropertyName("source_profile_id")] string SourceProfileId,
    [property: JsonPropertyName("rule_revision")] long RuleRevision,
    [property: JsonPropertyName("rss_priority_enabled")] bool RssPriorityEnabled,
    [property: JsonPropertyName("decisions")] IReadOnlyList<RssRuleDecisionResponse> Decisions);

public sealed record SourceProfileCreateRequest(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("adapter")] string? Adapter,
    [property: JsonPropertyName("downloader_id")] string? DownloaderId,
    [property: JsonPropertyName("file_strategy")] string? FileStrategy,
    [property: JsonPropertyName("allowed_torrent_hosts")] IReadOnlyList<string?>? AllowedTorrentHosts,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("tags")] IReadOnlyList<string?>? Tags,
    [property: JsonPropertyName("seeding_time_minutes")] int? SeedingTimeMinutes,
    [property: JsonPropertyName("rss_filter_enabled")] bool RssFilterEnabled,
    [property: JsonPropertyName("rss_priority_enabled")] bool RssPriorityEnabled,
    [property: JsonPropertyName("enabled")] bool Enabled);

public sealed record SourceProfileUpdateRequest(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("downloader_id")] string? DownloaderId,
    [property: JsonPropertyName("file_strategy")] string? FileStrategy,
    [property: JsonPropertyName("allowed_torrent_hosts")] IReadOnlyList<string?>? AllowedTorrentHosts,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("tags")] IReadOnlyList<string?>? Tags,
    [property: JsonPropertyName("seeding_time_minutes")] int? SeedingTimeMinutes,
    [property: JsonPropertyName("rss_filter_enabled")] bool RssFilterEnabled,
    [property: JsonPropertyName("rss_priority_enabled")] bool RssPriorityEnabled,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("expected_revision")] long ExpectedRevision);

public sealed record SourceProfileResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("adapter")] string Adapter,
    [property: JsonPropertyName("downloader_id")] string DownloaderId,
    [property: JsonPropertyName("file_strategy")] string FileStrategy,
    [property: JsonPropertyName("allowed_torrent_hosts")] IReadOnlyList<string> AllowedTorrentHosts,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("seeding_time_minutes")] int SeedingTimeMinutes,
    [property: JsonPropertyName("rss_filter_enabled")] bool RssFilterEnabled,
    [property: JsonPropertyName("rss_priority_enabled")] bool RssPriorityEnabled,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("ingest_task_count")] long IngestTaskCount,
    [property: JsonPropertyName("rss_batch_count")] long RssBatchCount,
    [property: JsonPropertyName("is_default")] bool IsDefault,
    [property: JsonPropertyName("file_strategy_warning")] string? FileStrategyWarning,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record SourceProfileListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<SourceProfileResponse> Items);

public sealed record SourceProfileDeleteResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("deleted")] bool Deleted);

public sealed record DownloaderInstanceResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("base_url")] string BaseUrl,
    [property: JsonPropertyName("download_path")] string DownloadPath,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("credentials_configured")] bool CredentialsConfigured,
    [property: JsonPropertyName("configuration_source")] string ConfigurationSource,
    [property: JsonPropertyName("override_revision")] long? OverrideRevision,
    [property: JsonPropertyName("restart_required")] bool RestartRequired,
    [property: JsonPropertyName("source_profile_count")] long SourceProfileCount,
    [property: JsonPropertyName("ingest_task_count")] long IngestTaskCount,
    [property: JsonPropertyName("download_job_count")] long DownloadJobCount,
    [property: JsonPropertyName("connected")] bool? Connected,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("last_success_at_utc")] DateTimeOffset? LastSuccessAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset? UpdatedAtUtc,
    [property: JsonPropertyName("circuit_state")] string? CircuitState,
    [property: JsonPropertyName("circuit_failure_count")] int CircuitFailureCount,
    [property: JsonPropertyName("circuit_retry_at_utc")] DateTimeOffset? CircuitRetryAtUtc);

public sealed record DownloaderInstanceListResponse(
    [property: JsonPropertyName("configuration_revision")] long ConfigurationRevision,
    [property: JsonPropertyName("applied_configuration_revision")] long AppliedConfigurationRevision,
    [property: JsonPropertyName("restart_required")] bool RestartRequired,
    [property: JsonPropertyName("items")] IReadOnlyList<DownloaderInstanceResponse> Items);

public sealed record DownloaderConnectionTestResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("connected")] bool Connected,
    [property: JsonPropertyName("task_count")] int? TaskCount,
    [property: JsonPropertyName("latency_ms")] long LatencyMs,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("client_version")] string? ClientVersion,
    [property: JsonPropertyName("client_default_save_path")] string? ClientDefaultSavePath);

public sealed record DownloaderPathProbeResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("hard_link_supported")] bool HardLinkSupported,
    [property: JsonPropertyName("download_path")] string DownloadPath,
    [property: JsonPropertyName("save_path")] string SavePath,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("message")] string Message);

public sealed record DownloaderInstanceUpsertRequest(
    [property: JsonPropertyName("base_url")] string? BaseUrl,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("password")] string? Password,
    [property: JsonPropertyName("clear_password")] bool ClearPassword,
    [property: JsonPropertyName("download_path")] string? DownloadPath,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("expected_configuration_revision")] long ExpectedConfigurationRevision);

public sealed record DownloaderConfigurationWriteResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("configuration_revision")] long ConfigurationRevision,
    [property: JsonPropertyName("instance_revision")] long? InstanceRevision,
    [property: JsonPropertyName("restart_required")] bool RestartRequired,
    [property: JsonPropertyName("reverted_to_deployment_default")] bool RevertedToDeploymentDefault);

public sealed record SourceRoutePreviewRequest(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("source_item_id")] string? SourceItemId,
    [property: JsonPropertyName("source_work_id")] string? SourceWorkId,
    [property: JsonPropertyName("mikan_url")] string? MikanUrl,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("bgmid")] int? BangumiId,
    [property: JsonPropertyName("anidbid")] int? AniDbId,
    [property: JsonPropertyName("imdbid")] string? ImdbId);

public sealed record SourceRoutePreviewResponse(
    [property: JsonPropertyName("valid")] bool Valid,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors,
    [property: JsonPropertyName("source_profile_id")] string SourceProfileId,
    [property: JsonPropertyName("source_profile_revision")] long Revision,
    [property: JsonPropertyName("adapter")] string Adapter,
    [property: JsonPropertyName("downloader_id")] string DownloaderId,
    [property: JsonPropertyName("downloader_enabled")] bool DownloaderEnabled,
    [property: JsonPropertyName("download_path")] string? DownloadPath,
    [property: JsonPropertyName("save_path")] string SavePath,
    [property: JsonPropertyName("file_strategy")] string FileStrategy,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("seeding_time_minutes")] int SeedingTimeMinutes,
    [property: JsonPropertyName("rss_filter_enabled")] bool RssFilterEnabled,
    [property: JsonPropertyName("rss_priority_enabled")] bool RssPriorityEnabled,
    [property: JsonPropertyName("rss_rule_revision")] long? RssRuleRevision);
