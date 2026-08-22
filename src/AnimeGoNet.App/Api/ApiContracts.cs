using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeGoNet.Core.Sources;
using AnimeGoNet.Core.Metadata;

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

public sealed record LegacyConfigurationPutRequest(
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("backup")] bool? Backup,
    [property: JsonPropertyName("config")] JsonElement Config,
    [property: JsonPropertyName("config_raw")] string? ConfigRaw);

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

public sealed record CacheBrowserBucketResponse(
    [property: JsonPropertyName("bucket_id")] string BucketId,
    [property: JsonPropertyName("bucket_name")] string BucketName,
    [property: JsonPropertyName("entry_count")] int EntryCount);

public sealed record CacheBrowserBucketListResponse(
    [property: JsonPropertyName("database")] string Database,
    [property: JsonPropertyName("read_only")] bool ReadOnly,
    [property: JsonPropertyName("items")] IReadOnlyList<CacheBrowserBucketResponse> Items);

public sealed record CacheBrowserEntryResponse(
    [property: JsonPropertyName("entry_id")] string EntryId,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("delete_token")] string DeleteToken,
    [property: JsonPropertyName("value_bytes")] int ValueBytes,
    [property: JsonPropertyName("expires_at_utc")] DateTimeOffset? ExpiresAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record CacheBrowserEntryListResponse(
    [property: JsonPropertyName("database")] string Database,
    [property: JsonPropertyName("read_only")] bool ReadOnly,
    [property: JsonPropertyName("bucket_id")] string BucketId,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total_count")] int TotalCount,
    [property: JsonPropertyName("items")] IReadOnlyList<CacheBrowserEntryResponse> Items);

public sealed record CacheBrowserEntryDetailResponse(
    [property: JsonPropertyName("database")] string Database,
    [property: JsonPropertyName("read_only")] bool ReadOnly,
    [property: JsonPropertyName("bucket_id")] string BucketId,
    [property: JsonPropertyName("bucket_name")] string BucketName,
    [property: JsonPropertyName("entry_id")] string EntryId,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value_json")] string ValueJson,
    [property: JsonPropertyName("value_bytes")] int ValueBytes,
    [property: JsonPropertyName("expires_at_utc")] DateTimeOffset? ExpiresAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record CacheBrowserDeleteRequest(
    [property: JsonPropertyName("database")] string? Database,
    [property: JsonPropertyName("bucket_id")] string? BucketId,
    [property: JsonPropertyName("delete_token")] string? DeleteToken);

public sealed record CacheBrowserDeleteResponse(
    [property: JsonPropertyName("database")] string Database,
    [property: JsonPropertyName("bucket_id")] string BucketId,
    [property: JsonPropertyName("entry_id")] string EntryId,
    [property: JsonPropertyName("deleted")] bool Deleted);

public sealed record PingData(string Version, long Time);

public sealed record AiMetadataTestFileRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("size_bytes")] long SizeBytes);

public sealed record AiMetadataTestRequest(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("files")] IReadOnlyList<AiMetadataTestFileRequest>? Files,
    [property: JsonPropertyName("bgmid")] int? BangumiSubjectId,
    [property: JsonPropertyName("anidbid")] int? AniDbAnimeId,
    [property: JsonPropertyName("imdbid")] string? ImdbTitleId,
    [property: JsonPropertyName("torrent_file_count")] int? TorrentFileCount,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("bgm_episode_candidate")] int? BangumiEpisodeCandidate,
    [property: JsonPropertyName("use_bangumi_pubdate_first")] bool UseBangumiPubDateFirst,
    [property: JsonPropertyName("expected_tmdbid")] int? ExpectedTmdbId,
    [property: JsonPropertyName("expected_season")] int? ExpectedSeason,
    [property: JsonPropertyName("prompt_template")] string? PromptTemplate,
    [property: JsonPropertyName("enable_tmdb_mcp")] bool? EnableTmdbMcp,
    [property: JsonPropertyName("enable_bangumi_mcp")] bool? EnableBangumiMcp,
    [property: JsonPropertyName("enable_anidb_lookup")] bool? EnableAniDbLookup,
    [property: JsonPropertyName("ai_base_url")] string? AiBaseUrl,
    [property: JsonPropertyName("ai_api_key")] string? AiApiKey,
    [property: JsonPropertyName("ai_model")] string? AiModel,
    [property: JsonPropertyName("api_mode")] string? ApiMode,
    [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort,
    [property: JsonPropertyName("web_search_enabled")] bool? WebSearchEnabled,
    [property: JsonPropertyName("ai_http_timeout_seconds")] int? AiHttpTimeoutSeconds,
    [property: JsonPropertyName("ai_retry_count")] int? AiRetryCount,
    [property: JsonPropertyName("http_proxy_url")] string? HttpProxyUrl,
    [property: JsonPropertyName("tmdb_mcp_url")] string? TmdbMcpUrl,
    [property: JsonPropertyName("bangumi_mcp_url")] string? BangumiMcpUrl);

public sealed record AiMetadataTestPromptResponse(
    [property: JsonPropertyName("prompt_version")] string PromptVersion,
    [property: JsonPropertyName("template")] string Template,
    [property: JsonPropertyName("maximum_length")] int MaximumLength,
    [property: JsonPropertyName("default_template")] string DefaultTemplate,
    [property: JsonPropertyName("customized")] bool Customized);

public sealed record AiMetadataTestMikanImportRequest(
    [property: JsonPropertyName("episode_url")] string? EpisodeUrl);

public sealed record AiMetadataTestMikanImportResponse(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("groupid")] int GroupId,
    [property: JsonPropertyName("bgmid")] int? BangumiSubjectId,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("torrent_file_count")] int TorrentFileCount,
    [property: JsonPropertyName("files")] IReadOnlyList<AiMetadataTestFileResponse> Files);

public sealed record AiMetadataTestFileResponse(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size_bytes")] long SizeBytes);

public sealed record AiMetadataTestTraceItem(
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("duration_ms")] long? DurationMilliseconds);

public sealed record AiMetadataTestValidatedFile(
    [property: JsonPropertyName("input_name")] string InputName,
    [property: JsonPropertyName("season")] int Season,
    [property: JsonPropertyName("episode")] int? Episode,
    [property: JsonPropertyName("episode_name")] string? EpisodeName,
    [property: JsonPropertyName("other_reason")] string? OtherReason);

public sealed record AiMetadataTestValidationResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("tmdbid")] int? TmdbId,
    [property: JsonPropertyName("series_name")] string? SeriesName,
    [property: JsonPropertyName("failure_kind")] string? FailureKind,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("tmdb_access_confirmed")] bool? TmdbAccessConfirmed,
    [property: JsonPropertyName("files")] IReadOnlyList<AiMetadataTestValidatedFile> Files);

public sealed record AiMetadataTestUsageResponse(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("prompt_tokens")] long? PromptTokens,
    [property: JsonPropertyName("completion_tokens")] long? CompletionTokens,
    [property: JsonPropertyName("total_tokens")] long? TotalTokens,
    [property: JsonPropertyName("request_count")] int RequestCount,
    [property: JsonPropertyName("tool_call_count")] int ToolCallCount,
    [property: JsonPropertyName("reasoning_tokens")] long? ReasoningTokens);

public sealed record AiMetadataTestFeatureResponse(
    [property: JsonPropertyName("tmdb_mcp")] bool TmdbMcp,
    [property: JsonPropertyName("bangumi_mcp")] bool BangumiMcp,
    [property: JsonPropertyName("anidb_lookup")] bool AniDbLookup,
    [property: JsonPropertyName("imdb_lookup")] bool ImdbLookup,
    [property: JsonPropertyName("bangumi_pubdate_first")] bool BangumiPubDateFirst);

public sealed record AiMetadataTestResponse(
    [property: JsonPropertyName("succeeded")] bool Succeeded,
    [property: JsonPropertyName("prompt_version")] string PromptVersion,
    [property: JsonPropertyName("api_mode")] string ApiMode,
    [property: JsonPropertyName("rendered_prompt")] string RenderedPrompt,
    [property: JsonPropertyName("raw_output")] string? RawOutput,
    [property: JsonPropertyName("candidate")] AiMetadataMatchCandidate? Candidate,
    [property: JsonPropertyName("validation")] AiMetadataTestValidationResponse? Validation,
    [property: JsonPropertyName("usage")] AiMetadataTestUsageResponse? Usage,
    [property: JsonPropertyName("duration_ms")] long DurationMilliseconds,
    [property: JsonPropertyName("error_kind")] string? ErrorKind,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("effective_features")] AiMetadataTestFeatureResponse EffectiveFeatures,
    [property: JsonPropertyName("trace")] IReadOnlyList<AiMetadataTestTraceItem> Trace);

public sealed record RuntimeStatus(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("database_schema_version")] int DatabaseSchemaVersion,
    [property: JsonPropertyName("native_aot")] bool NativeAot,
    [property: JsonPropertyName("runtime_identifier")] string RuntimeIdentifier,
    [property: JsonPropertyName("paths")] RuntimePaths Paths,
    [property: JsonPropertyName("capabilities")] RuntimeCapabilities Capabilities,
    [property: JsonPropertyName("downloads_blocked")] bool DownloadsBlocked,
    [property: JsonPropertyName("migration_diagnostics")]
    IReadOnlyList<ConfigurationMigrationDiagnosticResponse> MigrationDiagnostics,
    [property: JsonPropertyName("external_plugins")]
    ExternalPluginRuntimeStatusResponse ExternalPlugins);

public sealed record ExternalPluginRuntimeStatusResponse(
    [property: JsonPropertyName("packages")]
    IReadOnlyList<ExternalPluginPackageResponse> Packages,
    [property: JsonPropertyName("errors")]
    IReadOnlyList<ExternalPluginPackageErrorResponse> Errors,
    [property: JsonPropertyName("runtimes")]
    IReadOnlyList<ExternalPluginRuntimeResponse> Runtimes);

public sealed record ExternalPluginRuntimeResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("consecutive_failures")] int ConsecutiveFailures,
    [property: JsonPropertyName("retry_at_utc")] DateTimeOffset? RetryAtUtc,
    [property: JsonPropertyName("last_failure_code")] string? LastFailureCode);

public sealed record ExternalPluginPackageResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("rid")] string Rid,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities,
    [property: JsonPropertyName("configured")] bool Configured,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("entry_revision")] long EntryRevision);

public sealed record ExternalPluginPackageErrorResponse(
    [property: JsonPropertyName("package_directory_name")] string PackageDirectoryName,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

public sealed record ExternalPluginConfigurationListResponse(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("items")]
    IReadOnlyList<ExternalPluginConfigurationResponse> Items);

public sealed record ExternalPluginConfigurationResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("rid")] string Rid,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities,
    [property: JsonPropertyName("configured")] bool Configured,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("entry_revision")] long EntryRevision,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset? UpdatedAtUtc,
    [property: JsonPropertyName("args")] JsonElement Args,
    [property: JsonPropertyName("vars")] JsonElement Vars,
    [property: JsonPropertyName("configured_write_only_paths")]
    IReadOnlyList<string> ConfiguredWriteOnlyPaths,
    [property: JsonPropertyName("schema")] JsonElement Schema);

public sealed record ExternalPluginConfigurationUpdateRequest(
    [property: JsonPropertyName("expected_revision")] long ExpectedRevision,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("args")] JsonElement Args,
    [property: JsonPropertyName("vars")] JsonElement Vars,
    [property: JsonPropertyName("clear_write_only_paths")]
    IReadOnlyList<string>? ClearWriteOnlyPaths);

public sealed record ExternalPluginConfigurationMutationResponse(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("item")] ExternalPluginConfigurationResponse Item);

public sealed record ExternalPluginConfigurationDeleteResponse(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("id")] string Id);

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
    [property: JsonPropertyName("downloads_blocked")] bool DownloadsBlocked,
    [property: JsonPropertyName("migration_diagnostics")]
    IReadOnlyList<ConfigurationMigrationDiagnosticResponse> MigrationDiagnostics,
    [property: JsonPropertyName("paths")] RuntimePaths Paths,
    [property: JsonPropertyName("deployment")] DeploymentConfigurationResponse Deployment,
    [property: JsonPropertyName("outbound_proxy")] OutboundProxyConfigurationResponse OutboundProxy,
    [property: JsonPropertyName("metadata")] MetadataConfigurationResponse Metadata,
    [property: JsonPropertyName("torrent_fetch")] TorrentFetchConfigurationResponse TorrentFetch,
    [property: JsonPropertyName("data_update")] DataUpdateConfigurationResponse DataUpdate,
    [property: JsonPropertyName("editable")] EditableConfigurationResponse Editable);

public sealed record ConfigurationMigrationDiagnosticResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("legacy_downloader_type")] string LegacyDownloaderType,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("blocks_downloads")] bool BlocksDownloads);

public sealed record EditableConfigurationResponse(
    [property: JsonPropertyName("download_path")] string DownloadPath,
    [property: JsonPropertyName("save_path")] string SavePath,
    [property: JsonPropertyName("outbound_proxy_url")] string? OutboundProxyUrl,
    [property: JsonPropertyName("outbound_proxy_hosts")] IReadOnlyList<string> OutboundProxyHosts,
    [property: JsonPropertyName("mikan_base_url")] string MikanBaseUrl,
    [property: JsonPropertyName("mikan_episode_identity_cache_hours")] double MikanEpisodeIdentityCacheHours,
    [property: JsonPropertyName("mikan_bangumi_identity_cache_hours")] double MikanBangumiIdentityCacheHours,
    [property: JsonPropertyName("tmdb_base_url")] string TmdbBaseUrl,
    [property: JsonPropertyName("tmdb_image_base_url")] string TmdbImageBaseUrl,
    [property: JsonPropertyName("tmdb_language")] string TmdbLanguage,
    [property: JsonPropertyName("tmdb_http_timeout_seconds")] double TmdbHttpTimeoutSeconds,
    [property: JsonPropertyName("tmdb_retry_count")] int TmdbRetryCount,
    [property: JsonPropertyName("tmdb_retry_delay_seconds")] double TmdbRetryDelaySeconds,
    [property: JsonPropertyName("tmdb_cache_hours")] double TmdbCacheHours,
    [property: JsonPropertyName("tmdb_api_key_state")] string TmdbApiKeyState,
    [property: JsonPropertyName("tmdb_api_key")] string? TmdbApiKey,
    [property: JsonPropertyName("tmdb_read_access_token_state")] string TmdbReadAccessTokenState,
    [property: JsonPropertyName("tmdb_read_access_token")] string? TmdbReadAccessToken,
    [property: JsonPropertyName("bangumi_base_url")] string BangumiBaseUrl,
    [property: JsonPropertyName("bangumi_http_timeout_seconds")] double BangumiHttpTimeoutSeconds,
    [property: JsonPropertyName("bangumi_retry_count")] int BangumiRetryCount,
    [property: JsonPropertyName("bangumi_retry_delay_seconds")] double BangumiRetryDelaySeconds,
    [property: JsonPropertyName("season_failure_skip")] bool SeasonFailureSkip,
    [property: JsonPropertyName("season_failure_backtrace")] bool SeasonFailureBacktrace,
    [property: JsonPropertyName("season_failure_use_title_season")] bool SeasonFailureUseTitleSeason,
    [property: JsonPropertyName("season_failure_use_first_season")] bool SeasonFailureUseFirstSeason,
    [property: JsonPropertyName("ai_base_url")] string? AiBaseUrl,
    [property: JsonPropertyName("ai_model")] string? AiModel,
    [property: JsonPropertyName("ai_prompt_template")] string AiPromptTemplate,
    [property: JsonPropertyName("ai_api_key_state")] string AiApiKeyState,
    [property: JsonPropertyName("ai_api_key")] string? AiApiKey,
    [property: JsonPropertyName("ai_tmdb_mcp_url")] string AiTmdbMcpUrl,
    [property: JsonPropertyName("ai_bangumi_mcp_url")] string AiBangumiMcpUrl,
    [property: JsonPropertyName("ai_use_metadata_match")] bool AiUseMetadataMatch,
    [property: JsonPropertyName("ai_use_season_match")] bool AiUseSeasonMatch,
    [property: JsonPropertyName("ai_use_episode_match")] bool AiUseEpisodeMatch,
    [property: JsonPropertyName("ai_debug_mode")] bool AiDebugMode,
    [property: JsonPropertyName("ai_http_timeout_seconds")] double AiHttpTimeoutSeconds,
    [property: JsonPropertyName("tmdb_failure_use_bangumi")] bool TmdbFailureUseBangumi,
    [property: JsonPropertyName("write_bangumi_id_when_tmdb_matched")]
    bool WriteBangumiIdWhenTmdbMatched,
    [property: JsonPropertyName("mikan_trusted_offset_cache_enabled")] bool MikanTrustedOffsetCacheEnabled,
    [property: JsonPropertyName("torrent_http_timeout_seconds")] double TorrentHttpTimeoutSeconds,
    [property: JsonPropertyName("torrent_max_response_bytes")] long TorrentMaxResponseBytes,
    [property: JsonPropertyName("torrent_max_redirects")] int TorrentMaxRedirects,
    [property: JsonPropertyName("torrent_staging_ttl_seconds")] double TorrentStagingTtlSeconds,
    [property: JsonPropertyName("data_update_enabled")] bool DataUpdateEnabled,
    [property: JsonPropertyName("data_update_cron")] string DataUpdateCron,
    [property: JsonPropertyName("data_update_manifest_url")] string? DataUpdateManifestUrl,
    [property: JsonPropertyName("data_update_auto_download")] bool DataUpdateAutoDownload,
    [property: JsonPropertyName("data_update_auto_import")] bool DataUpdateAutoImport,
    [property: JsonPropertyName("data_update_keep_versions")] int DataUpdateKeepVersions,
    [property: JsonPropertyName("data_update_http_timeout_seconds")] double DataUpdateHttpTimeoutSeconds,
    [property: JsonPropertyName("locked_fields")] IReadOnlyList<ConfigurationFieldLockResponse> LockedFields,
    [property: JsonPropertyName("ai_reasoning_effort")] string AiReasoningEffort = "none",
    [property: JsonPropertyName("mikan_trusted_offset_required_episodes")]
    int MikanTrustedOffsetRequiredEpisodes = 3);

public sealed record ConfigurationFieldLockResponse(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("environment_variables")] IReadOnlyList<string> EnvironmentVariables,
    [property: JsonPropertyName("command_line_arguments")] IReadOnlyList<string> CommandLineArguments,
    [property: JsonPropertyName("controlling_keys")] IReadOnlyList<string> ControllingKeys);

public sealed record ConfigurationUpdateRequest(
    [property: JsonPropertyName("mikan_base_url")] string? MikanBaseUrl,
    [property: JsonPropertyName("tmdb_base_url")] string? TmdbBaseUrl,
    [property: JsonPropertyName("tmdb_image_base_url")] string? TmdbImageBaseUrl,
    [property: JsonPropertyName("tmdb_language")] string? TmdbLanguage,
    [property: JsonPropertyName("tmdb_http_timeout_seconds")] double TmdbHttpTimeoutSeconds,
    [property: JsonPropertyName("tmdb_retry_count")] int? TmdbRetryCount,
    [property: JsonPropertyName("tmdb_retry_delay_seconds")] double? TmdbRetryDelaySeconds,
    [property: JsonPropertyName("tmdb_cache_hours")] double? TmdbCacheHours,
    [property: JsonPropertyName("tmdb_api_key")] string? TmdbApiKey,
    [property: JsonPropertyName("clear_tmdb_api_key")] bool ClearTmdbApiKey,
    [property: JsonPropertyName("tmdb_read_access_token")] string? TmdbReadAccessToken,
    [property: JsonPropertyName("clear_tmdb_read_access_token")] bool ClearTmdbReadAccessToken,
    [property: JsonPropertyName("bangumi_base_url")] string? BangumiBaseUrl,
    [property: JsonPropertyName("bangumi_http_timeout_seconds")] double BangumiHttpTimeoutSeconds,
    [property: JsonPropertyName("bangumi_retry_count")] int? BangumiRetryCount,
    [property: JsonPropertyName("bangumi_retry_delay_seconds")] double? BangumiRetryDelaySeconds,
    [property: JsonPropertyName("season_failure_skip")] bool SeasonFailureSkip,
    [property: JsonPropertyName("season_failure_backtrace")] bool SeasonFailureBacktrace,
    [property: JsonPropertyName("season_failure_use_title_season")] bool SeasonFailureUseTitleSeason,
    [property: JsonPropertyName("season_failure_use_first_season")] bool SeasonFailureUseFirstSeason,
    [property: JsonPropertyName("ai_use_metadata_match")] bool? AiUseMetadataMatch,
    [property: JsonPropertyName("ai_use_season_match")] bool? AiUseSeasonMatch,
    [property: JsonPropertyName("ai_use_episode_match")] bool? AiUseEpisodeMatch,
    [property: JsonPropertyName("ai_debug_mode")] bool? AiDebugMode,
    [property: JsonPropertyName("ai_http_timeout_seconds")] double AiHttpTimeoutSeconds,
    [property: JsonPropertyName("tmdb_failure_use_bangumi")] bool TmdbFailureUseBangumi,
    [property: JsonPropertyName("write_bangumi_id_when_tmdb_matched")]
    bool WriteBangumiIdWhenTmdbMatched,
    [property: JsonPropertyName("mikan_trusted_offset_cache_enabled")] bool MikanTrustedOffsetCacheEnabled,
    [property: JsonPropertyName("torrent_http_timeout_seconds")] double TorrentHttpTimeoutSeconds,
    [property: JsonPropertyName("torrent_max_response_bytes")] long TorrentMaxResponseBytes,
    [property: JsonPropertyName("torrent_max_redirects")] int TorrentMaxRedirects,
    [property: JsonPropertyName("torrent_staging_ttl_seconds")] double TorrentStagingTtlSeconds,
    [property: JsonPropertyName("data_update_enabled")] bool DataUpdateEnabled,
    [property: JsonPropertyName("data_update_cron")] string? DataUpdateCron,
    [property: JsonPropertyName("data_update_manifest_url")] string? DataUpdateManifestUrl,
    [property: JsonPropertyName("data_update_auto_download")] bool DataUpdateAutoDownload,
    [property: JsonPropertyName("data_update_auto_import")] bool DataUpdateAutoImport,
    [property: JsonPropertyName("data_update_keep_versions")] int DataUpdateKeepVersions,
    [property: JsonPropertyName("data_update_http_timeout_seconds")] double DataUpdateHttpTimeoutSeconds,
    [property: JsonPropertyName("expected_configuration_revision")] long ExpectedConfigurationRevision,
    [property: JsonPropertyName("outbound_proxy_url")] string? OutboundProxyUrl = null,
    [property: JsonPropertyName("outbound_proxy_hosts")] IReadOnlyList<string>? OutboundProxyHosts = null,
    [property: JsonPropertyName("ai_base_url")] string? AiBaseUrl = null,
    [property: JsonPropertyName("ai_model")] string? AiModel = null,
    [property: JsonPropertyName("ai_api_key")] string? AiApiKey = null,
    [property: JsonPropertyName("clear_ai_api_key")] bool ClearAiApiKey = false,
    [property: JsonPropertyName("ai_tmdb_mcp_url")] string? AiTmdbMcpUrl = null,
    [property: JsonPropertyName("ai_bangumi_mcp_url")] string? AiBangumiMcpUrl = null,
    [property: JsonPropertyName("ai_prompt_template")] string? AiPromptTemplate = null,
    [property: JsonPropertyName("mikan_episode_identity_cache_hours")] double? MikanEpisodeIdentityCacheHours = null,
    [property: JsonPropertyName("mikan_bangumi_identity_cache_hours")] double? MikanBangumiIdentityCacheHours = null,
    [property: JsonPropertyName("ai_reasoning_effort")] string? AiReasoningEffort = null,
    [property: JsonPropertyName("mikan_trusted_offset_required_episodes")]
    int? MikanTrustedOffsetRequiredEpisodes = null,
    [property: JsonPropertyName("download_path")] string? DownloadPath = null,
    [property: JsonPropertyName("save_path")] string? SavePath = null);

public sealed record ConfigurationWriteResponse(
    [property: JsonPropertyName("configuration_revision")] long ConfigurationRevision,
    [property: JsonPropertyName("restart_required")] bool RestartRequired,
    [property: JsonPropertyName("reverted_to_deployment_default")] bool RevertedToDeploymentDefault,
    [property: JsonPropertyName("backup_revision")] long? BackupRevision);

public sealed record ConfigurationPreviewResponse(
    [property: JsonPropertyName("expected_configuration_revision")] long ExpectedConfigurationRevision,
    [property: JsonPropertyName("current_configuration_revision")] long CurrentConfigurationRevision,
    [property: JsonPropertyName("restart_required")] bool RestartRequired,
    [property: JsonPropertyName("data_update_hot_reload")] bool DataUpdateHotReload,
    [property: JsonPropertyName("changes")] IReadOnlyList<ConfigurationChangeResponse> Changes);

public sealed record ConfigurationChangeResponse(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("before")] string? Before,
    [property: JsonPropertyName("after")] string? After,
    [property: JsonPropertyName("effect")] string Effect,
    [property: JsonPropertyName("sensitive")] bool Sensitive);

public sealed record DeploymentConfigurationResponse(
    [property: JsonPropertyName("running_in_container")] bool RunningInContainer,
    [property: JsonPropertyName("background_workers_enabled")] bool BackgroundWorkersEnabled,
    [property: JsonPropertyName("inner_plugin_mikan_access_key_configured")] bool InnerPluginMikanAccessKeyConfigured,
    [property: JsonPropertyName("webui_access_key_configured")] bool WebUiAccessKeyConfigured,
    [property: JsonPropertyName("web_host")] string WebHost,
    [property: JsonPropertyName("web_port")] int WebPort,
    [property: JsonPropertyName("paths_restart_required")] bool PathsRestartRequired);

public sealed record DataUpdateConfigurationResponse(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("cron")] string Cron,
    [property: JsonPropertyName("manifest_url")] string? ManifestUrl,
    [property: JsonPropertyName("auto_download")] bool AutoDownload,
    [property: JsonPropertyName("auto_import")] bool AutoImport,
    [property: JsonPropertyName("keep_versions")] int KeepVersions,
    [property: JsonPropertyName("http_timeout_seconds")] double HttpTimeoutSeconds,
    [property: JsonPropertyName("hot_reload_supported")] bool HotReloadSupported);

public sealed record MetadataConfigurationResponse(
    [property: JsonPropertyName("mikan")] MikanConfigurationResponse Mikan,
    [property: JsonPropertyName("tmdb")] TmdbConfigurationResponse Tmdb,
    [property: JsonPropertyName("bangumi")] BangumiConfigurationResponse Bangumi,
    [property: JsonPropertyName("season_failure")] SeasonFailureConfigurationResponse SeasonFailure,
    [property: JsonPropertyName("ai")] AiConfigurationResponse Ai,
    [property: JsonPropertyName("tmdb_failure_use_bangumi")] bool TmdbFailureUseBangumi,
    [property: JsonPropertyName("write_bangumi_id_when_tmdb_matched")]
    bool WriteBangumiIdWhenTmdbMatched,
    [property: JsonPropertyName("mikan_trusted_offset_cache_enabled")] bool MikanTrustedOffsetCacheEnabled,
    [property: JsonPropertyName("mikan_trusted_offset_required_episodes")]
    int MikanTrustedOffsetRequiredEpisodes);

public sealed record MikanConfigurationResponse(
    [property: JsonPropertyName("base_url")] string BaseUrl,
    [property: JsonPropertyName("episode_identity_cache_hours")] double EpisodeIdentityCacheHours,
    [property: JsonPropertyName("bangumi_identity_cache_hours")] double BangumiIdentityCacheHours);

public sealed record OutboundProxyConfigurationResponse(
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("hosts")] IReadOnlyList<string> Hosts);

public sealed record TmdbConfigurationResponse(
    [property: JsonPropertyName("base_url")] string BaseUrl,
    [property: JsonPropertyName("image_base_url")] string ImageBaseUrl,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("http_timeout_seconds")] double HttpTimeoutSeconds,
    [property: JsonPropertyName("retry_count")] int RetryCount,
    [property: JsonPropertyName("retry_delay_seconds")] double RetryDelaySeconds,
    [property: JsonPropertyName("cache_hours")] double CacheHours,
    [property: JsonPropertyName("api_key_configured")] bool ApiKeyConfigured,
    [property: JsonPropertyName("read_access_token_configured")] bool ReadAccessTokenConfigured);

public sealed record BangumiConfigurationResponse(
    [property: JsonPropertyName("base_url")] string BaseUrl,
    [property: JsonPropertyName("http_timeout_seconds")] double HttpTimeoutSeconds,
    [property: JsonPropertyName("retry_count")] int RetryCount,
    [property: JsonPropertyName("retry_delay_seconds")] double RetryDelaySeconds);

public sealed record SeasonFailureConfigurationResponse(
    [property: JsonPropertyName("skip")] bool Skip,
    [property: JsonPropertyName("backtrace")] bool Backtrace,
    [property: JsonPropertyName("use_title_season")] bool UseTitleSeason,
    [property: JsonPropertyName("use_first_season")] bool UseFirstSeason);

public sealed record AiConfigurationResponse(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("base_url")] string? BaseUrl,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("prompt_version")] string PromptVersion,
    [property: JsonPropertyName("prompt_customized")] bool PromptCustomized,
    [property: JsonPropertyName("api_key_configured")] bool ApiKeyConfigured,
    [property: JsonPropertyName("use_metadata_match")] bool UseMetadataMatch,
    [property: JsonPropertyName("use_season_match")] bool UseSeasonMatch,
    [property: JsonPropertyName("use_episode_match")] bool UseEpisodeMatch,
    [property: JsonPropertyName("debug_mode")] bool DebugMode,
    [property: JsonPropertyName("http_timeout_seconds")] double HttpTimeoutSeconds,
    [property: JsonPropertyName("retry_count")] int RetryCount,
    [property: JsonPropertyName("use_bangumi_pubdate_first")] bool UseBangumiPubDateFirst,
    [property: JsonPropertyName("tmdb_mcp_url")] string TmdbMcpUrl,
    [property: JsonPropertyName("bangumi_mcp_url")] string BangumiMcpUrl,
    [property: JsonPropertyName("reasoning_effort")] string ReasoningEffort = "none");

public sealed record TorrentFetchConfigurationResponse(
    [property: JsonPropertyName("http_timeout_seconds")] double HttpTimeoutSeconds,
    [property: JsonPropertyName("max_response_bytes")] long MaxResponseBytes,
    [property: JsonPropertyName("max_redirects")] int MaxRedirects,
    [property: JsonPropertyName("staging_ttl_seconds")] double StagingTtlSeconds);

public sealed record IngestBatchRequest(
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("data")] IReadOnlyList<IngestItemRequest?>? Data);

public sealed record MikanEpisodeResolveRequest(
    [property: JsonPropertyName("source_profile_id")] string? SourceProfileId,
    [property: JsonPropertyName("episode_url")] string? EpisodeUrl);

public sealed record MikanEpisodeResolveResponse(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("torrent_url")] string TorrentUrl,
    [property: JsonPropertyName("source_item_id")] string SourceItemId,
    [property: JsonPropertyName("source_work_id")] string SourceWorkId,
    [property: JsonPropertyName("mikan_url")] string MikanUrl,
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("groupid")] int GroupId,
    [property: JsonPropertyName("bgmid")] int? BangumiSubjectId,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt);

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
    [property: JsonPropertyName("imdbid")] string? ImdbId,
    [property: JsonPropertyName("groupid")] int? GroupId = null);

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
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total_items")] int TotalItems,
    [property: JsonPropertyName("search")] string? Search,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("business_status")] string? BusinessStatus,
    [property: JsonPropertyName("downloader_id")] string? DownloaderId,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("sort")] string Sort,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("summary_bucket")] string? SummaryBucket,
    [property: JsonPropertyName("summary")] DownloadDashboardSummary Summary,
    [property: JsonPropertyName("items")] IReadOnlyList<DownloadListItem> Items);

public sealed record DownloadDashboardSummary(
    [property: JsonPropertyName("total_jobs")] int TotalJobs,
    [property: JsonPropertyName("active_jobs")] int ActiveJobs,
    [property: JsonPropertyName("paused_jobs")] int PausedJobs,
    [property: JsonPropertyName("failed_jobs")] int FailedJobs,
    [property: JsonPropertyName("stale_jobs")] int StaleJobs,
    [property: JsonPropertyName("waiting_organization_jobs")] int WaitingOrganizationJobs,
    [property: JsonPropertyName("completed_jobs")] int CompletedJobs,
    [property: JsonPropertyName("skipped_duplicate_jobs")] int SkippedDuplicateJobs,
    [property: JsonPropertyName("preparation_failed_jobs")] int PreparationFailedJobs,
    [property: JsonPropertyName("organization_failed_jobs")] int OrganizationFailedJobs,
    [property: JsonPropertyName("connected_download_speed_bytes_per_second")] long ConnectedDownloadSpeedBytesPerSecond,
    [property: JsonPropertyName("offline_instance_count")] int OfflineInstanceCount,
    [property: JsonPropertyName("latest_failure_code")] string? LatestFailureCode,
    [property: JsonPropertyName("last_downloader_success_at_utc")] DateTimeOffset? LastDownloaderSuccessAtUtc);

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
    [property: JsonPropertyName("seeding_state")] string SeedingState,
    [property: JsonPropertyName("seeding_target_minutes")] int SeedingTargetMinutes,
    [property: JsonPropertyName("seeding_elapsed_seconds")] long SeedingElapsedSeconds,
    [property: JsonPropertyName("seeding_completed_at_utc")] DateTimeOffset? SeedingCompletedAtUtc,
    [property: JsonPropertyName("dynamic_tags")] IReadOnlyList<string> DynamicTags,
    [property: JsonPropertyName("dynamic_tag_state")] string DynamicTagState,
    [property: JsonPropertyName("dynamic_tag_failure_code")] string? DynamicTagFailureCode,
    [property: JsonPropertyName("is_stale")] bool IsStale,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("snapshot_at_utc")] DateTimeOffset? SnapshotAtUtc,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("downloader_connected")] bool DownloaderConnected,
    [property: JsonPropertyName("downloader_failure_code")] string? DownloaderFailureCode,
    [property: JsonPropertyName("downloader_last_success_at_utc")] DateTimeOffset? DownloaderLastSuccessAtUtc);

public sealed record DownloadDetailResponse(
    [property: JsonPropertyName("summary")] DownloadListItem Summary,
    [property: JsonPropertyName("task_failure_kind")] string? TaskFailureKind,
    [property: JsonPropertyName("task_failure_reason")] string? TaskFailureReason,
    [property: JsonPropertyName("preparation")] DownloadStageDetail Preparation,
    [property: JsonPropertyName("organization")] DownloadStageDetail Organization,
    [property: JsonPropertyName("file_snapshot_state")] string FileSnapshotState,
    [property: JsonPropertyName("file_snapshot_failure_code")] string? FileSnapshotFailureCode,
    [property: JsonPropertyName("can_pause")] bool CanPause,
    [property: JsonPropertyName("can_resume")] bool CanResume,
    [property: JsonPropertyName("can_retry")] bool CanRetry,
    [property: JsonPropertyName("files")] IReadOnlyList<DownloadFileDetail> Files,
    [property: JsonPropertyName("timeline")] IReadOnlyList<DownloadTimelineItem> Timeline);

public sealed record DownloadStageDetail(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("attempt_count")] int AttemptCount,
    [property: JsonPropertyName("next_attempt_at_utc")] DateTimeOffset? NextAttemptAtUtc,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("phase")] string? Phase,
    [property: JsonPropertyName("completed_units")] int? CompletedUnits,
    [property: JsonPropertyName("total_units")] int? TotalUnits,
    [property: JsonPropertyName("progress")] double? Progress);

public sealed record DownloadFileDetail(
    [property: JsonPropertyName("relative_path")] string RelativePath,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("file_index")] int? FileIndex,
    [property: JsonPropertyName("wanted")] bool? Wanted,
    [property: JsonPropertyName("priority")] int? Priority,
    [property: JsonPropertyName("progress")] double? Progress,
    [property: JsonPropertyName("downloaded_bytes")] long? DownloadedBytes,
    [property: JsonPropertyName("disposition")] string Disposition,
    [property: JsonPropertyName("other_reason")] string? OtherReason);

public sealed record DownloadTimelineItem(
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("from_state")] string? FromState,
    [property: JsonPropertyName("to_state")] string? ToState,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc);

public sealed record DownloadControlRequest(
    [property: JsonPropertyName("expected_revision")] long ExpectedRevision);

public sealed record DownloadControlResponse(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("revision")] long Revision);

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

public sealed record MikanWorkImpactResponse(
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("total_task_count")] int TotalTaskCount,
    [property: JsonPropertyName("future_task_count")] int FutureTaskCount,
    [property: JsonPropertyName("retryable_failed_task_count")] int RetryableFailedTaskCount,
    [property: JsonPropertyName("active_task_count")] int ActiveTaskCount,
    [property: JsonPropertyName("resolved_protected_task_count")] int ResolvedProtectedTaskCount,
    [property: JsonPropertyName("completed_protected_task_count")] int CompletedProtectedTaskCount,
    [property: JsonPropertyName("other_task_count")] int OtherTaskCount,
    [property: JsonPropertyName("is_truncated")] bool IsTruncated,
    [property: JsonPropertyName("items")] IReadOnlyList<MikanWorkImpactTaskResponse> Items);

public sealed record MikanWorkImpactTaskResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("bgmid")] int? BangumiSubjectId,
    [property: JsonPropertyName("tmdb_series_id")] int? TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int? TmdbSeasonNumber,
    [property: JsonPropertyName("organization_state")] string? OrganizationState,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record MikanWorkRematchRequest(
    [property: JsonPropertyName("expected_rule_revision")] long ExpectedRuleRevision);

public sealed record MikanWorkRematchResponse(
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("rule_revision")] long RuleRevision,
    [property: JsonPropertyName("retried_task_count")] int RetriedTaskCount);

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

public sealed record MikanTrustedOffsetBlacklistListResponse(
    [property: JsonPropertyName("items")]
    IReadOnlyList<MikanTrustedOffsetBlacklistItemResponse> Items);

public sealed record MikanTrustedOffsetBlacklistItemResponse(
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("groupid")] int? GroupId,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc);

public sealed record MikanTrustedOffsetBlacklistWriteRequest(
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("groupid")] int? GroupId);

public sealed record NotificationChannelListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<NotificationChannelResponse> Items);

public sealed record NotificationChannelResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("endpoint_url")] string EndpointUrl,
    [property: JsonPropertyName("secret")] string? Secret,
    [property: JsonPropertyName("target")] string? Target,
    [property: JsonPropertyName("options")] JsonElement Options,
    [property: JsonPropertyName("events")] IReadOnlyList<string> Events,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record NotificationChannelWriteRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("endpoint_url")] string EndpointUrl,
    [property: JsonPropertyName("secret")] string? Secret,
    [property: JsonPropertyName("target")] string? Target,
    [property: JsonPropertyName("options")] JsonElement Options,
    [property: JsonPropertyName("events")] IReadOnlyList<string> Events);

public sealed record NotificationTestResponse(
    [property: JsonPropertyName("succeeded")] bool Succeeded,
    [property: JsonPropertyName("http_status")] int? HttpStatus,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("response_excerpt")] string? ResponseExcerpt,
    [property: JsonPropertyName("duration_ms")] long DurationMilliseconds);

public sealed record NotificationDeliveryListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<NotificationDeliveryResponse> Items);

public sealed record NotificationDeliveryResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("channel_name")] string ChannelName,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("task_id")] string? TaskId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("http_status")] int? HttpStatus,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("response_excerpt")] string? ResponseExcerpt,
    [property: JsonPropertyName("duration_ms")] long DurationMilliseconds,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc);

public sealed record MetadataRetryResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("status")] string Status);

public sealed record MetadataTaskListResponse(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total_items")] int TotalItems,
    [property: JsonPropertyName("sort")] string Sort,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("attention")] MetadataTaskAttentionSummaryResponse Attention,
    [property: JsonPropertyName("items")] IReadOnlyList<MetadataTaskListItem> Items);

public sealed record MetadataTaskAttentionSummaryResponse(
    [property: JsonPropertyName("other_items")] int OtherItems,
    [property: JsonPropertyName("failed_items")] int FailedItems,
    [property: JsonPropertyName("review_pending_items")] int ReviewPendingItems);

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
    [property: JsonPropertyName("series_run_id")] string? SeriesRunId,
    [property: JsonPropertyName("series_attempt_id")] string? SeriesAttemptId,
    [property: JsonPropertyName("season_run_id")] string? SeasonRunId,
    [property: JsonPropertyName("season_attempt_id")] string? SeasonAttemptId,
    [property: JsonPropertyName("episode_run_id")] string? EpisodeRunId,
    [property: JsonPropertyName("episode_attempt_id")] string? EpisodeAttemptId,
    [property: JsonPropertyName("episode_resolution_mixed")] bool EpisodeResolutionMixed,
    [property: JsonPropertyName("failure_kind")] string? FailureKind,
    [property: JsonPropertyName("failure_reason")] string? FailureReason,
    [property: JsonPropertyName("failure_stage")] string? FailureStage,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("failure_retryable")] bool? FailureRetryable,
    [property: JsonPropertyName("latest_run_status")] string? LatestRunStatus,
    [property: JsonPropertyName("tmdb_access_confirmed")] bool? TmdbAccessConfirmed,
    [property: JsonPropertyName("bangumi_fallback_eligible")] bool? BangumiFallbackEligible,
    [property: JsonPropertyName("bangumi_fallback_denial_reason")]
    string? BangumiFallbackDenialReason,
    [property: JsonPropertyName("handling_category")] string HandlingCategory,
    [property: JsonPropertyName("episode_file_count")] int EpisodeFileCount,
    [property: JsonPropertyName("other_file_count")] int OtherFileCount,
    [property: JsonPropertyName("duplicate_file_count")] int DuplicateFileCount,
    [property: JsonPropertyName("pending_file_count")] int PendingFileCount,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("readaptation_review_state")] string ReadaptationReviewState);

public sealed record OtherFileReadaptationFileResponse(
    [property: JsonPropertyName("task_file_id")] string TaskFileId,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("other_reason")] string OtherReason,
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber,
    [property: JsonPropertyName("source_available")] bool SourceAvailable,
    [property: JsonPropertyName("shared_path_reference_count")] int SharedPathReferenceCount);

public sealed record OtherFileReadaptationPreviewResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("eligible")] bool Eligible,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("files")] IReadOnlyList<OtherFileReadaptationFileResponse> Files);

public sealed record OtherFileReadaptationStartResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("file_count")] int FileCount);

public sealed record OtherFileReadaptationReviewResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("review_state")] string ReviewState);

public sealed record OtherFileReadaptationManualOverrideRequest(
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber,
    [property: JsonPropertyName("tmdb_episode_number")] int TmdbEpisodeNumber);

public sealed record OtherFileReadaptationManualOverrideResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("task_file_id")] string TaskFileId,
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("series_name")] string SeriesName,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber,
    [property: JsonPropertyName("season_name")] string SeasonName,
    [property: JsonPropertyName("tmdb_episode_number")] int TmdbEpisodeNumber,
    [property: JsonPropertyName("episode_name")] string EpisodeName,
    [property: JsonPropertyName("other_action")] string OtherAction);

public sealed record OtherFileReadaptationReviewFileResponse(
    [property: JsonPropertyName("task_file_id")] string TaskFileId,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("before_disposition")] string BeforeDisposition,
    [property: JsonPropertyName("before_other_reason")] string BeforeOtherReason,
    [property: JsonPropertyName("before_tmdb_series_id")] int? BeforeTmdbSeriesId,
    [property: JsonPropertyName("before_series_name")] string? BeforeSeriesName,
    [property: JsonPropertyName("before_tmdb_season_number")] int? BeforeTmdbSeasonNumber,
    [property: JsonPropertyName("before_season_name")] string? BeforeSeasonName,
    [property: JsonPropertyName("before_tmdb_episode_number")] int? BeforeTmdbEpisodeNumber,
    [property: JsonPropertyName("before_episode_name")] string? BeforeEpisodeName,
    [property: JsonPropertyName("after_disposition")] string AfterDisposition,
    [property: JsonPropertyName("after_other_reason")] string? AfterOtherReason,
    [property: JsonPropertyName("after_tmdb_series_id")] int? AfterTmdbSeriesId,
    [property: JsonPropertyName("after_series_name")] string? AfterSeriesName,
    [property: JsonPropertyName("after_tmdb_season_number")] int? AfterTmdbSeasonNumber,
    [property: JsonPropertyName("after_season_name")] string? AfterSeasonName,
    [property: JsonPropertyName("after_tmdb_episode_number")] int? AfterTmdbEpisodeNumber,
    [property: JsonPropertyName("after_episode_name")] string? AfterEpisodeName,
    [property: JsonPropertyName("after_episode_strategy")] string? AfterEpisodeStrategy,
    [property: JsonPropertyName("preserved_shared_source")] bool PreservedSharedSource,
    [property: JsonPropertyName("before_media_path")] string BeforeMediaPath,
    [property: JsonPropertyName("after_media_path")] string? AfterMediaPath);

public sealed record OtherFileReadaptationReviewPreviewResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("task_status")] string TaskStatus,
    [property: JsonPropertyName("review_state")] string ReviewState,
    [property: JsonPropertyName("completion_status")] string CompletionStatus,
    [property: JsonPropertyName("requested_at_utc")] DateTimeOffset RequestedAtUtc,
    [property: JsonPropertyName("completed_at_utc")] DateTimeOffset? CompletedAtUtc,
    [property: JsonPropertyName("reviewed_at_utc")] DateTimeOffset? ReviewedAtUtc,
    [property: JsonPropertyName("files")] IReadOnlyList<OtherFileReadaptationReviewFileResponse> Files);

public sealed record MetadataTaskDetailResponse(
    [property: JsonPropertyName("summary")] MetadataTaskListItem Summary,
    [property: JsonPropertyName("source_evidence")]
    MetadataTaskSourceEvidenceItem SourceEvidence,
    [property: JsonPropertyName("rss_evidence")]
    IReadOnlyList<MetadataTaskRssEvidenceItem> RssEvidence,
    [property: JsonPropertyName("ai")] MetadataTaskAiItem Ai,
    [property: JsonPropertyName("nfo_rewrites")]
    IReadOnlyList<MetadataTaskNfoRewriteItem> NfoRewrites,
    [property: JsonPropertyName("files")] IReadOnlyList<MetadataTaskFileItem> Files);

public sealed record MetadataTaskSourceEvidenceItem(
    [property: JsonPropertyName("source_profile_id")] string SourceProfileId,
    [property: JsonPropertyName("source_profile_revision")] long SourceProfileRevision,
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("source_title")] string SourceTitle,
    [property: JsonPropertyName("source_item_id_fingerprint")] string? SourceItemIdFingerprint,
    [property: JsonPropertyName("source_work_id_fingerprint")] string? SourceWorkIdFingerprint,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("groupid")] int? GroupId,
    [property: JsonPropertyName("bgmid")] int? BangumiSubjectId,
    [property: JsonPropertyName("anidbid")] int? AniDbAnimeId,
    [property: JsonPropertyName("imdbid")] string? ImdbTitleId,
    [property: JsonPropertyName("published_at_raw_available")] bool PublishedAtRawAvailable,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt);

public sealed record MetadataTaskRssEvidenceItem(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("entry_ordinal")] int EntryOrdinal,
    [property: JsonPropertyName("source_profile_id")] string SourceProfileId,
    [property: JsonPropertyName("rule_revision")] long RuleRevision,
    [property: JsonPropertyName("priority_enabled")] bool PriorityEnabled,
    [property: JsonPropertyName("legacy_filter_revision")] long LegacyFilterRevision,
    [property: JsonPropertyName("legacy_filter_enabled")] bool LegacyFilterEnabled,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("source_episode_kind")] string? SourceEpisodeKind,
    [property: JsonPropertyName("source_episode")] string? SourceEpisode,
    [property: JsonPropertyName("decision_kind")] string DecisionKind,
    [property: JsonPropertyName("decision_reason")] string DecisionReason,
    [property: JsonPropertyName("evaluated_priority_groups")]
    IReadOnlyList<string> EvaluatedPriorityGroups,
    [property: JsonPropertyName("legacy_filter_state")] string LegacyFilterState,
    [property: JsonPropertyName("legacy_filter_reason")] string LegacyFilterReason,
    [property: JsonPropertyName("legacy_filter_scope")] string? LegacyFilterScope,
    [property: JsonPropertyName("identity_mikanid")] int? IdentityMikanId,
    [property: JsonPropertyName("identity_groupid")] int? IdentityGroupId,
    [property: JsonPropertyName("effect_state")] string EffectState,
    [property: JsonPropertyName("batch_created_at_utc")] DateTimeOffset BatchCreatedAtUtc);

public sealed record MetadataTaskAiItem(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("stage")] string? Stage,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("confidence_basis")] string ConfidenceBasis,
    [property: JsonPropertyName("duration_ms")] long? DurationMilliseconds,
    [property: JsonPropertyName("attempted_at_utc")] DateTimeOffset? AttemptedAtUtc,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("prompt_tokens")] long? PromptTokens,
    [property: JsonPropertyName("completion_tokens")] long? CompletionTokens,
    [property: JsonPropertyName("total_tokens")] long? TotalTokens,
    [property: JsonPropertyName("request_count")] int? RequestCount,
    [property: JsonPropertyName("tool_call_count")] int? ToolCallCount);

public sealed record MetadataTaskNfoRewriteItem(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("bgmid")] int BangumiSubjectId,
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("attempt_count")] int AttemptCount,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("next_attempt_at_utc")] DateTimeOffset? NextAttemptAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("completed_at_utc")] DateTimeOffset? CompletedAtUtc);

public sealed record MetadataTaskFileItem(
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("source_episode")] string? SourceEpisode,
    [property: JsonPropertyName("file_episode_candidate")] string? FileEpisodeCandidate,
    [property: JsonPropertyName("disposition")] string Disposition,
    [property: JsonPropertyName("other_reason")] string? OtherReason,
    [property: JsonPropertyName("tmdb_series_id")] int? TmdbSeriesId,
    [property: JsonPropertyName("tmdb_series_name")] string? TmdbSeriesName,
    [property: JsonPropertyName("tmdb_season_number")] int? TmdbSeasonNumber,
    [property: JsonPropertyName("tmdb_season_name")] string? TmdbSeasonName,
    [property: JsonPropertyName("tmdb_episode_number")] int? TmdbEpisodeNumber,
    [property: JsonPropertyName("tmdb_episode_name")] string? TmdbEpisodeName,
    [property: JsonPropertyName("episode_strategy")] string? EpisodeStrategy,
    [property: JsonPropertyName("episode_run_id")] string? EpisodeRunId,
    [property: JsonPropertyName("episode_attempt_id")] string? EpisodeAttemptId);

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
    [property: JsonPropertyName("run_completed_at_utc")] DateTimeOffset? RunCompletedAtUtc,
    [property: JsonPropertyName("ai_model")] string? AiModel,
    [property: JsonPropertyName("ai_prompt_tokens")] long? AiPromptTokens,
    [property: JsonPropertyName("ai_completion_tokens")] long? AiCompletionTokens,
    [property: JsonPropertyName("ai_total_tokens")] long? AiTotalTokens,
    [property: JsonPropertyName("ai_request_count")] int? AiRequestCount,
    [property: JsonPropertyName("ai_tool_call_count")] int? AiToolCallCount);

public sealed record AiInvocationLogListResponse(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total_items")] int TotalItems,
    [property: JsonPropertyName("summary")] AiInvocationLogSummaryResponse Summary,
    [property: JsonPropertyName("items")] IReadOnlyList<AiInvocationLogItemResponse> Items);

public sealed record AiInvocationLogSummaryResponse(
    [property: JsonPropertyName("matched_items")] int MatchedItems,
    [property: JsonPropertyName("failed_items")] int FailedItems,
    [property: JsonPropertyName("output_format_failed_items")] int OutputFormatFailedItems,
    [property: JsonPropertyName("prompt_tokens")] long PromptTokens,
    [property: JsonPropertyName("completion_tokens")] long CompletionTokens,
    [property: JsonPropertyName("total_tokens")] long TotalTokens,
    [property: JsonPropertyName("request_count")] long RequestCount,
    [property: JsonPropertyName("tool_call_count")] long ToolCallCount);

public sealed record AiInvocationLogItemResponse(
    [property: JsonPropertyName("attempt_id")] string AttemptId,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("bgmid")] int? BangumiSubjectId,
    [property: JsonPropertyName("tmdb_series_id")] int? TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int? TmdbSeasonNumber,
    [property: JsonPropertyName("run_status")] string RunStatus,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("strategy")] string Strategy,
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("error_category")] string ErrorCategory,
    [property: JsonPropertyName("ai_trigger_reason")] string? AiTriggerReason,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("retryable")] bool Retryable,
    [property: JsonPropertyName("duration_ms")] long DurationMilliseconds,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("prompt_tokens")] long? PromptTokens,
    [property: JsonPropertyName("completion_tokens")] long? CompletionTokens,
    [property: JsonPropertyName("total_tokens")] long? TotalTokens,
    [property: JsonPropertyName("request_count")] int RequestCount,
    [property: JsonPropertyName("tool_call_count")] int ToolCallCount,
    [property: JsonPropertyName("validated_episodes")]
    IReadOnlyList<AiInvocationValidatedEpisodeResponse> ValidatedEpisodes,
    [property: JsonPropertyName("debug_available")] bool DebugAvailable);

public sealed record AiInvocationValidatedEpisodeResponse(
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("season_number")] int SeasonNumber,
    [property: JsonPropertyName("episode_number")] int EpisodeNumber,
    [property: JsonPropertyName("episode_name")] string? EpisodeName);

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
    [property: JsonPropertyName("resource_revision")] string ResourceRevision,
    [property: JsonPropertyName("episode_total")] int EpisodeTotal,
    [property: JsonPropertyName("episode_snapshot_count")] int EpisodeSnapshotCount,
    [property: JsonPropertyName("episode_downloaded")] int EpisodeDownloaded,
    [property: JsonPropertyName("series_resolution_source")] string? SeriesResolutionSource,
    [property: JsonPropertyName("series_resolution_run_id")] string? SeriesResolutionRunId,
    [property: JsonPropertyName("series_resolution_attempt_id")] string? SeriesResolutionAttemptId,
    [property: JsonPropertyName("season_resolution_source")] string? SeasonResolutionSource,
    [property: JsonPropertyName("season_resolution_run_id")] string? SeasonResolutionRunId,
    [property: JsonPropertyName("season_resolution_attempt_id")] string? SeasonResolutionAttemptId,
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
    [property: JsonPropertyName("resource_revision")] string ResourceRevision,
    [property: JsonPropertyName("episode_total")] int EpisodeTotal,
    [property: JsonPropertyName("episode_snapshot_count")] int EpisodeSnapshotCount,
    [property: JsonPropertyName("episode_downloaded")] int EpisodeDownloaded,
    [property: JsonPropertyName("series_resolution_source")] string? SeriesResolutionSource,
    [property: JsonPropertyName("series_resolution_run_id")] string? SeriesResolutionRunId,
    [property: JsonPropertyName("series_resolution_attempt_id")] string? SeriesResolutionAttemptId,
    [property: JsonPropertyName("season_resolution_source")] string? SeasonResolutionSource,
    [property: JsonPropertyName("season_resolution_run_id")] string? SeasonResolutionRunId,
    [property: JsonPropertyName("season_resolution_attempt_id")] string? SeasonResolutionAttemptId,
    [property: JsonPropertyName("validation_status")] string ValidationStatus,
    [property: JsonPropertyName("last_resolution_run_id")] string? LastResolutionRunId,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("episodes")] IReadOnlyList<AnimeEpisodeItemResponse> Episodes,
    [property: JsonPropertyName("manual_offsets")] IReadOnlyList<AnimeSeasonManualOffsetResponse> ManualOffsets,
    [property: JsonPropertyName("mikan_bindings")] IReadOnlyList<AnimeSeasonMikanBindingResponse> MikanBindings,
    [property: JsonPropertyName("related_task_total")] int RelatedTaskTotal,
    [property: JsonPropertyName("related_tasks_truncated")] bool RelatedTasksTruncated,
    [property: JsonPropertyName("related_tasks")] IReadOnlyList<AnimeSeasonRelatedTaskResponse> RelatedTasks,
    [property: JsonPropertyName("resolution_attempt_total")] int ResolutionAttemptTotal,
    [property: JsonPropertyName("resolution_attempts_truncated")] bool ResolutionAttemptsTruncated,
    [property: JsonPropertyName("resolution_attempts")] IReadOnlyList<AnimeSeasonResolutionAttemptResponse> ResolutionAttempts);

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

public sealed record AnimeSeasonManualOffsetResponse(
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("bgmid")] int? BangumiSubjectId,
    [property: JsonPropertyName("tmdb_series_id")] int? TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int? TmdbSeasonNumber,
    [property: JsonPropertyName("episode_offset")] int EpisodeOffset,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record AnimeSeasonMikanBindingResponse(
    [property: JsonPropertyName("source_profile_id")] string SourceProfileId,
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("groupid")] int GroupId,
    [property: JsonPropertyName("last_used_at_utc")] DateTimeOffset LastUsedAtUtc);

public sealed record AnimeSeasonRelatedTaskResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("groupid")] int? GroupId,
    [property: JsonPropertyName("bgmid")] int? BangumiSubjectId,
    [property: JsonPropertyName("latest_run_attempt_number")] int? LatestRunAttemptNumber,
    [property: JsonPropertyName("latest_run_status")] string? LatestRunStatus,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record AnimeSeasonResolutionAttemptResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("task_title")] string TaskTitle,
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
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc);

public sealed record AnimeSeasonCreateRequest(
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber);

public sealed record AnimeSeasonRefreshRequest(
    [property: JsonPropertyName("expected_revision")] string? ExpectedRevision);

public sealed record AnimeSeasonMutationResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber,
    [property: JsonPropertyName("resource_revision")] string ResourceRevision);

public sealed record AnimeSeasonDeleteResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber,
    [property: JsonPropertyName("series_removed")] bool SeriesRemoved);

public sealed record MikanSeasonCompletionPreviewRequest(
    [property: JsonPropertyName("source_profile_id")] string? SourceProfileId,
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("groupid")] int GroupId);

public sealed record MikanSeasonCompletionConfirmRequest(
    [property: JsonPropertyName("source_profile_id")] string? SourceProfileId,
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("groupid")] int GroupId,
    [property: JsonPropertyName("expected_resource_revision")] string? ExpectedResourceRevision,
    [property: JsonPropertyName("selected_candidate_ids")] IReadOnlyList<string>? SelectedCandidateIds);

public sealed record MikanSeasonCompletionCandidateResponse(
    [property: JsonPropertyName("candidate_id")] string CandidateId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("length")] long Length,
    [property: JsonPropertyName("published_date")] string? PublishedDate,
    [property: JsonPropertyName("source_episode_kind")] string? SourceEpisodeKind,
    [property: JsonPropertyName("source_episode")] int? SourceEpisode,
    [property: JsonPropertyName("target_episode")] int? TargetEpisode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("default_selected")] bool DefaultSelected);

public sealed record MikanSeasonCompletionPreviewResponse(
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber,
    [property: JsonPropertyName("resource_revision")] string ResourceRevision,
    [property: JsonPropertyName("source_profile_id")] string SourceProfileId,
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("groupid")] int GroupId,
    [property: JsonPropertyName("offset_source")] string? OffsetSource,
    [property: JsonPropertyName("episode_offset")] int? EpisodeOffset,
    [property: JsonPropertyName("items")] IReadOnlyList<MikanSeasonCompletionCandidateResponse> Items);

public sealed record ExternalMediaImportResponse(
    [property: JsonPropertyName("scanned_season_count")] int ScannedSeasonCount,
    [property: JsonPropertyName("candidate_file_count")] int CandidateFileCount,
    [property: JsonPropertyName("imported_count")] int ImportedCount,
    [property: JsonPropertyName("already_recorded_count")] int AlreadyRecordedCount,
    [property: JsonPropertyName("skipped_count")] int SkippedCount,
    [property: JsonPropertyName("items")] IReadOnlyList<ExternalMediaImportItemResponse> Items);

public sealed record ExternalMediaImportItemResponse(
    [property: JsonPropertyName("tmdb_series_id")] int TmdbSeriesId,
    [property: JsonPropertyName("tmdb_season_number")] int TmdbSeasonNumber,
    [property: JsonPropertyName("tmdb_episode_number")] int? TmdbEpisodeNumber,
    [property: JsonPropertyName("relative_path")] string RelativePath,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason_code")] string? ReasonCode);

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
    [property: JsonPropertyName("media_files")] IReadOnlyList<DeleteTargetResponse> MediaFiles,
    [property: JsonPropertyName("task_records")] IReadOnlyList<DeleteTargetResponse> TaskRecords,
    [property: JsonPropertyName("task_record_deletion_allowed")] bool TaskRecordDeletionAllowed,
    [property: JsonPropertyName("task_record_deletion_denial_reason")] string? TaskRecordDeletionDenialReason);

public sealed record CreateDeleteExecutionRequest(
    [property: JsonPropertyName("fingerprint")] string? Fingerprint,
    [property: JsonPropertyName("delete_business_record")] bool DeleteBusinessRecord,
    [property: JsonPropertyName("delete_downloader_task")] bool DeleteDownloaderTask,
    [property: JsonPropertyName("delete_source_files")] bool DeleteSourceFiles,
    [property: JsonPropertyName("delete_media_files")] bool DeleteMediaFiles,
    [property: JsonPropertyName("delete_task_record")] bool DeleteTaskRecord = false);

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
    [property: JsonPropertyName("items")] IReadOnlyList<DeleteTargetResponse> Items,
    [property: JsonPropertyName("reused_existing_execution")]
    bool ReusedExistingExecution = false);

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

public sealed record RssRuleSnapshotItem(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc);

public sealed record RssRuleSetResponse(
    [property: JsonPropertyName("source_profile_id")] string SourceProfileId,
    [property: JsonPropertyName("rss_filter_enabled")] bool RssFilterEnabled,
    [property: JsonPropertyName("rss_priority_enabled")] bool RssPriorityEnabled,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("whitelist")] IReadOnlyList<RssNamedArrayResponse> Whitelist,
    [property: JsonPropertyName("blacklist")] IReadOnlyList<RssNamedArrayResponse> Blacklist,
    [property: JsonPropertyName("priority_groups")] IReadOnlyList<RssPriorityGroupResponse> PriorityGroups,
    [property: JsonPropertyName("snapshots")] IReadOnlyList<RssRuleSnapshotItem> Snapshots,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record RssRuleRollbackRequest(
    [property: JsonPropertyName("expected_revision")] long ExpectedRevision,
    [property: JsonPropertyName("target_revision")] long TargetRevision);

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
    [property: JsonPropertyName("dynamic_tag_template")] string? DynamicTagTemplate,
    [property: JsonPropertyName("seeding_time_minutes")] int? SeedingTimeMinutes,
    [property: JsonPropertyName("rss_filter_enabled")] bool RssFilterEnabled,
    [property: JsonPropertyName("rss_priority_enabled")] bool RssPriorityEnabled,
    [property: JsonPropertyName("duplicate_notification_enabled")]
    bool? DuplicateNotificationEnabled,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("mikan_identity_cookie")] string? MikanIdentityCookie,
    [property: JsonPropertyName("rss_feed_url")] string? RssFeedUrl = null,
    [property: JsonPropertyName("rss_schedule_enabled")] bool? RssScheduleEnabled = null,
    [property: JsonPropertyName("rss_schedule_cron")] string? RssScheduleCron = null);

public sealed record SourceProfileUpdateRequest(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("downloader_id")] string? DownloaderId,
    [property: JsonPropertyName("file_strategy")] string? FileStrategy,
    [property: JsonPropertyName("allowed_torrent_hosts")] IReadOnlyList<string?>? AllowedTorrentHosts,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("tags")] IReadOnlyList<string?>? Tags,
    [property: JsonPropertyName("dynamic_tag_template")] string? DynamicTagTemplate,
    [property: JsonPropertyName("seeding_time_minutes")] int? SeedingTimeMinutes,
    [property: JsonPropertyName("rss_filter_enabled")] bool RssFilterEnabled,
    [property: JsonPropertyName("rss_priority_enabled")] bool RssPriorityEnabled,
    [property: JsonPropertyName("duplicate_notification_enabled")]
    bool? DuplicateNotificationEnabled,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("mikan_identity_cookie")] string? MikanIdentityCookie,
    [property: JsonPropertyName("clear_mikan_identity_cookie")] bool ClearMikanIdentityCookie,
    [property: JsonPropertyName("expected_revision")] long ExpectedRevision,
    [property: JsonPropertyName("rss_feed_url")] string? RssFeedUrl = null,
    [property: JsonPropertyName("clear_rss_feed_url")] bool ClearRssFeedUrl = false,
    [property: JsonPropertyName("rss_schedule_enabled")] bool? RssScheduleEnabled = null,
    [property: JsonPropertyName("rss_schedule_cron")] string? RssScheduleCron = null);

public sealed record SourceProfileResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("adapter")] string Adapter,
    [property: JsonPropertyName("downloader_id")] string DownloaderId,
    [property: JsonPropertyName("file_strategy")] string FileStrategy,
    [property: JsonPropertyName("allowed_torrent_hosts")] IReadOnlyList<string> AllowedTorrentHosts,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("dynamic_tag_template")] string? DynamicTagTemplate,
    [property: JsonPropertyName("seeding_time_minutes")] int SeedingTimeMinutes,
    [property: JsonPropertyName("rss_filter_enabled")] bool RssFilterEnabled,
    [property: JsonPropertyName("rss_priority_enabled")] bool RssPriorityEnabled,
    [property: JsonPropertyName("duplicate_notification_enabled")]
    bool DuplicateNotificationEnabled,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("locked_fields")]
    IReadOnlyList<SourceProfileFieldLockResponse> LockedFields,
    [property: JsonPropertyName("mikan_identity_cookie_configured")]
    bool MikanIdentityCookieConfigured,
    [property: JsonPropertyName("mikan_identity_cookie")] string? MikanIdentityCookie,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("ingest_task_count")] long IngestTaskCount,
    [property: JsonPropertyName("rss_batch_count")] long RssBatchCount,
    [property: JsonPropertyName("is_default")] bool IsDefault,
    [property: JsonPropertyName("file_strategy_warning")] string? FileStrategyWarning,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("rss_feed_url_configured")] bool RssFeedUrlConfigured = false,
    [property: JsonPropertyName("rss_feed_url")] string? RssFeedUrl = null,
    [property: JsonPropertyName("rss_schedule_enabled")] bool RssScheduleEnabled = false,
    [property: JsonPropertyName("rss_schedule_cron")] string RssScheduleCron =
        SourceRssSchedulePolicy.DefaultCron,
    [property: JsonPropertyName("rss_schedule_registered")] bool RssScheduleRegistered = false,
    [property: JsonPropertyName("rss_schedule_next_at_utc")] DateTimeOffset? RssScheduleNextAtUtc = null,
    [property: JsonPropertyName("rss_last_run_state")] string RssLastRunState = "never",
    [property: JsonPropertyName("rss_last_started_at_utc")] DateTimeOffset? RssLastStartedAtUtc = null,
    [property: JsonPropertyName("rss_last_completed_at_utc")] DateTimeOffset? RssLastCompletedAtUtc = null,
    [property: JsonPropertyName("rss_last_failure_code")] string? RssLastFailureCode = null,
    [property: JsonPropertyName("rss_last_batch_id")] string? RssLastBatchId = null);

public sealed record SourceProfileFieldLockResponse(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("controlling_keys")] IReadOnlyList<string> ControllingKeys);

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
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("password")] string? Password,
    [property: JsonPropertyName("configuration_source")] string ConfigurationSource,
    [property: JsonPropertyName("locked_fields")]
    IReadOnlyList<DownloaderFieldLockResponse> LockedFields,
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

public sealed record DownloaderFieldLockResponse(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("controlling_keys")]
    IReadOnlyList<string> ControllingKeys);

public sealed record DownloaderInstanceListResponse(
    [property: JsonPropertyName("configuration_revision")] long ConfigurationRevision,
    [property: JsonPropertyName("applied_configuration_revision")] long AppliedConfigurationRevision,
    [property: JsonPropertyName("restart_required")] bool RestartRequired,
    [property: JsonPropertyName("downloads_blocked")] bool DownloadsBlocked,
    [property: JsonPropertyName("migration_diagnostics")]
    IReadOnlyList<ConfigurationMigrationDiagnosticResponse> MigrationDiagnostics,
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
    [property: JsonPropertyName("dynamic_tag_template")] string? DynamicTagTemplate,
    [property: JsonPropertyName("seeding_time_minutes")] int SeedingTimeMinutes,
    [property: JsonPropertyName("rss_filter_enabled")] bool RssFilterEnabled,
    [property: JsonPropertyName("rss_priority_enabled")] bool RssPriorityEnabled,
    [property: JsonPropertyName("duplicate_notification_enabled")]
    bool DuplicateNotificationEnabled,
    [property: JsonPropertyName("rss_rule_revision")] long? RssRuleRevision);

public sealed record LegacyMikanFilterRuleResponse(
    [property: JsonPropertyName("tier")] int Tier,
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("whitelist_enabled")] bool WhitelistEnabled,
    [property: JsonPropertyName("blacklist_enabled")] bool BlacklistEnabled,
    [property: JsonPropertyName("whitelist")] IReadOnlyList<string> Whitelist,
    [property: JsonPropertyName("blacklist")] IReadOnlyList<string> Blacklist);

public sealed record LegacyMikanFilterSnapshotItem(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("updated_source")] string UpdatedSource,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc);

public sealed record LegacyMikanFilterResponse(
    [property: JsonPropertyName("source_profile_id")] string SourceProfileId,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("updated_source")] string UpdatedSource,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("legacy_json")] string LegacyJson,
    [property: JsonPropertyName("rules")] IReadOnlyList<LegacyMikanFilterRuleResponse> Rules,
    [property: JsonPropertyName("snapshots")] IReadOnlyList<LegacyMikanFilterSnapshotItem> Snapshots);

public sealed record LegacyMikanFilterWriteRequest(
    [property: JsonPropertyName("expected_revision")] long ExpectedRevision,
    [property: JsonPropertyName("rules")] IReadOnlyList<LegacyMikanFilterRuleResponse>? Rules);

public sealed record LegacyMikanFilterImportRequest(
    [property: JsonPropertyName("expected_revision")] long ExpectedRevision,
    [property: JsonPropertyName("legacy_json")] string? LegacyJson);

public sealed record LegacyMikanFilterRollbackRequest(
    [property: JsonPropertyName("expected_revision")] long ExpectedRevision,
    [property: JsonPropertyName("target_revision")] long TargetRevision);

public sealed record LegacyMikanFilterPreviewRequest(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("groupid")] int? GroupId,
    [property: JsonPropertyName("group_name")] string? GroupName,
    [property: JsonPropertyName("rules")] IReadOnlyList<LegacyMikanFilterRuleResponse>? Rules);

public sealed record LegacyMikanFilterTraceItem(
    [property: JsonPropertyName("tier")] string Tier,
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("applicable")] bool Applicable,
    [property: JsonPropertyName("accepted")] bool? Accepted,
    [property: JsonPropertyName("whitelist_matches")] IReadOnlyList<string> WhitelistMatches,
    [property: JsonPropertyName("blacklist_matches")] IReadOnlyList<string> BlacklistMatches,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record LegacyMikanFilterPreviewResponse(
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("matched_scope")] string? MatchedScope,
    [property: JsonPropertyName("matched_key")] string? MatchedKey,
    [property: JsonPropertyName("derived_group_name")] string DerivedGroupName,
    [property: JsonPropertyName("steps")] IReadOnlyList<LegacyMikanFilterTraceItem> Steps);

public sealed record DirectoryDatabaseStatusResponse(
    [property: JsonPropertyName("refresh_cron")] string RefreshCron,
    [property: JsonPropertyName("entry_count")] int EntryCount,
    [property: JsonPropertyName("last_run_id")] string? LastRunId,
    [property: JsonPropertyName("last_run_status")] string? LastRunStatus,
    [property: JsonPropertyName("last_scanned_count")] int LastScannedCount,
    [property: JsonPropertyName("last_indexed_count")] int LastIndexedCount,
    [property: JsonPropertyName("last_rejected_count")] int LastRejectedCount,
    [property: JsonPropertyName("last_failure_code")] string? LastFailureCode,
    [property: JsonPropertyName("last_started_at_utc")] DateTimeOffset? LastStartedAtUtc,
    [property: JsonPropertyName("last_completed_at_utc")] DateTimeOffset? LastCompletedAtUtc);

public sealed record DataUpdateVersionResponse(
    [property: JsonPropertyName("data_version")] string DataVersion,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("subject_count")] long SubjectCount,
    [property: JsonPropertyName("episode_count")] long EpisodeCount,
    [property: JsonPropertyName("installed_at_utc")] DateTimeOffset InstalledAtUtc,
    [property: JsonPropertyName("activated_at_utc")] DateTimeOffset? ActivatedAtUtc);

public sealed record DataUpdateDownloadResponse(
    [property: JsonPropertyName("data_version")] string DataVersion,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("downloaded_at_utc")] DateTimeOffset DownloadedAtUtc,
    [property: JsonPropertyName("imported_at_utc")] DateTimeOffset? ImportedAtUtc);

public sealed record DataUpdatePackageRunResponse(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("data_version")] string? DataVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("subject_count")] long SubjectCount,
    [property: JsonPropertyName("episode_count")] long EpisodeCount,
    [property: JsonPropertyName("started_at_utc")] DateTimeOffset StartedAtUtc,
    [property: JsonPropertyName("completed_at_utc")] DateTimeOffset? CompletedAtUtc);

public sealed record DataUpdateTransferRunResponse(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("trigger_kind")] string TriggerKind,
    [property: JsonPropertyName("requested_action")] string RequestedAction,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("data_version")] string? DataVersion,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("downloaded_bytes")] long DownloadedBytes,
    [property: JsonPropertyName("total_bytes")] long TotalBytes,
    [property: JsonPropertyName("started_at_utc")] DateTimeOffset StartedAtUtc,
    [property: JsonPropertyName("completed_at_utc")] DateTimeOffset? CompletedAtUtc);

public sealed record BangumiArchiveUsageResponse(
    [property: JsonPropertyName("total_hits")] long TotalHits,
    [property: JsonPropertyName("subject_hits")] long SubjectHits,
    [property: JsonPropertyName("episode_hits")] long EpisodeHits,
    [property: JsonPropertyName("relation_hits")] long RelationHits,
    [property: JsonPropertyName("last_hit_at_utc")] DateTimeOffset? LastHitAtUtc);

public sealed record BangumiArchiveUsageEventResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("data_version")] string DataVersion,
    [property: JsonPropertyName("hit_kind")] string HitKind,
    [property: JsonPropertyName("subject_id")] int SubjectId,
    [property: JsonPropertyName("result_count")] int ResultCount,
    [property: JsonPropertyName("hit_at_utc")] DateTimeOffset HitAtUtc);

public sealed record BangumiArchiveUsageListResponse(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total_items")] long TotalItems,
    [property: JsonPropertyName("hit_kind")] string? HitKind,
    [property: JsonPropertyName("items")] IReadOnlyList<BangumiArchiveUsageEventResponse> Items);

public sealed record DataUpdateStatusResponse(
    [property: JsonPropertyName("scheduled_enabled")] bool ScheduledEnabled,
    [property: JsonPropertyName("cron")] string Cron,
    [property: JsonPropertyName("manifest_configured")] bool ManifestConfigured,
    [property: JsonPropertyName("auto_download")] bool AutoDownload,
    [property: JsonPropertyName("auto_import")] bool AutoImport,
    [property: JsonPropertyName("keep_versions")] int KeepVersions,
    [property: JsonPropertyName("active_version")] string? ActiveVersion,
    [property: JsonPropertyName("previous_version")] string? PreviousVersion,
    [property: JsonPropertyName("state_updated_at_utc")] DateTimeOffset StateUpdatedAtUtc,
    [property: JsonPropertyName("versions")] IReadOnlyList<DataUpdateVersionResponse> Versions,
    [property: JsonPropertyName("downloads")] IReadOnlyList<DataUpdateDownloadResponse> Downloads,
    [property: JsonPropertyName("last_package_run")] DataUpdatePackageRunResponse? LastPackageRun,
    [property: JsonPropertyName("last_transfer_run")] DataUpdateTransferRunResponse? LastTransferRun,
    [property: JsonPropertyName("archive_usage")] BangumiArchiveUsageResponse ArchiveUsage);

public sealed record DataUpdateActionResponse(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("data_version")] string? DataVersion,
    [property: JsonPropertyName("active_version")] string? ActiveVersion,
    [property: JsonPropertyName("downloaded")] bool Downloaded,
    [property: JsonPropertyName("imported")] bool Imported);
