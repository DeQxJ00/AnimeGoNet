using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.App.Ingest;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.App.Library;
using AnimeGoNet.App.Serialization;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Cache;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Deletion;
using AnimeGoNet.Data.Library;
using AnimeGoNet.Data.Mikan;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Rules;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.App.Api;

public static class ApiEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/ping", Ping);
        app.MapGet("/sha256", Sha256);
        app.MapGet("/api/v1/status", Status);
        app.MapGet("/api/v1/config", Configuration);
        app.MapPut("/api/v1/config", PutConfiguration);
        app.MapDelete("/api/v1/config", DeleteConfigurationOverride);
        app.MapGet("/api/v1/downloads", Downloads);
        app.MapGet("/api/v1/downloads/{jobId}", DownloadDetail);
        app.MapPost("/api/v1/downloads/{jobId}/pause", PauseDownload);
        app.MapPost("/api/v1/downloads/{jobId}/resume", ResumeDownload);
        app.MapPost("/api/v1/downloads/{jobId}/retry", RetryDownload);
        app.MapGet("/api/v1/downloaders", ListDownloaders);
        app.MapPut("/api/v1/downloaders/{downloaderId}", PutDownloader);
        app.MapDelete("/api/v1/downloaders/{downloaderId}", DeleteDownloaderOverride);
        app.MapPost("/api/v1/downloaders/{downloaderId}/test", TestDownloader);
        app.MapPost("/api/v1/downloaders/{downloaderId}/path-probe", ProbeDownloaderPath);
        app.MapGet("/api/v1/sources", ListSourceProfiles);
        app.MapGet("/api/v1/sources/{sourceProfileId}", GetSourceProfile);
        app.MapPost("/api/v1/sources", CreateSourceProfile);
        app.MapPut("/api/v1/sources/{sourceProfileId}", UpdateSourceProfile);
        app.MapDelete("/api/v1/sources/{sourceProfileId}", DeleteSourceProfile);
        app.MapPost("/api/v1/sources/{sourceProfileId}/route-preview", PreviewSourceRoute);
        app.MapGet("/api/v1/rss-rules/{sourceProfileId}", GetRssRules);
        app.MapPut("/api/v1/rss-rules/{sourceProfileId}", PutRssRules);
        app.MapPost("/api/v1/rss-rules/{sourceProfileId}/preview", PreviewRssRules);
        app.MapGet("/api/v1/delete/tasks/{taskId}/preview", DeletePreview);
        app.MapPost("/api/v1/delete/tasks/{taskId}", CreateDeleteExecution);
        app.MapGet("/api/v1/delete/executions/{executionId}", DeleteExecutionStatus);
        app.MapGet("/api/v1/mikan/work-rules/{mikanId:int}", GetMikanWorkRule);
        app.MapPut("/api/v1/mikan/work-rules/{mikanId:int}", PutMikanWorkRule);
        app.MapDelete("/api/v1/mikan/work-rules/{mikanId:int}", DeleteMikanWorkRule);
        app.MapGet("/api/v1/mikan/work-rules/{mikanId:int}/impact", GetMikanWorkImpact);
        app.MapPost("/api/v1/mikan/work-rules/{mikanId:int}/rematch", RematchMikanWorkTasks);
        app.MapGet("/api/v1/mikan/trusted-offsets", ListMikanTrustedOffsets);
        app.MapDelete(
            "/api/v1/mikan/trusted-offsets/{mikanId:int}/{groupId:int}",
            ClearMikanTrustedOffset);
        app.MapGet("/api/v1/mikan/legacy-filter", GetLegacyMikanFilter);
        app.MapPut("/api/v1/mikan/legacy-filter", PutLegacyMikanFilter);
        app.MapPost("/api/v1/mikan/legacy-filter/import", ImportLegacyMikanFilter);
        app.MapPost("/api/v1/mikan/legacy-filter/rollback", RollbackLegacyMikanFilter);
        app.MapPost("/api/v1/mikan/legacy-filter/preview", PreviewLegacyMikanFilter);
        app.MapPost("/api/v1/metadata/tasks/{taskId}/retry", RetryMetadataTask);
        app.MapGet("/api/v1/metadata/tasks", MetadataTasks);
        app.MapGet("/api/v1/metadata/tasks/{taskId}", MetadataTaskDetail);
        app.MapGet("/api/v1/metadata/tasks/{taskId}/attempts", MetadataTaskAttempts);
        app.MapGet("/api/v1/library/seasons", LibrarySeasons);
        app.MapGet(
            "/api/v1/library/seasons/{tmdbSeriesId:int}/{seasonNumber:int}",
            LibrarySeasonDetail);
        app.MapGet(
            "/api/v1/library/covers/{tmdbSeriesId:int}/{seasonNumber:int}",
            LibraryCover);
        app.MapGet("/api/v1/metadata/pending-tmdb", PendingTmdbSeries);
        app.MapGet("/api/v1/metadata/pending-tmdb/{bangumiSubjectId:int}", PendingTmdbDetail);
        app.MapPost(
            "/api/v1/metadata/pending-tmdb/{bangumiSubjectId:int}/recover",
            RecoverPendingTmdb);
        app.MapPost("/api/v1/ingest", Ingest);
        app.MapPost("/api/v1/rss/ingest", RssIngest);
        app.MapPost("/api/rss", LegacyRss);
        app.MapPost("/api/download/manager", LegacyDownloadManager);
        app.MapPost("/api/plugin/config", LegacyPluginConfigPost);
        app.MapGet("/api/plugin/config", LegacyPluginConfigGet);
        app.MapGet("/api/bolt", LegacyBoltList);
        app.MapGet("/api/bolt/value", LegacyBoltGet);
        app.MapDelete("/api/bolt/value", LegacyBoltDelete);
    }

    private static Ok<LegacyApiResponse<PingData>> Ping()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        return TypedResults.Ok(new LegacyApiResponse<PingData>(
            200,
            "pong",
            new PingData(version, DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
    }

    private static Ok<LegacyApiResponse<string>> Sha256(string accessKey)
    {
        var hash = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(accessKey)));
        return TypedResults.Ok(new LegacyApiResponse<string>(200, "Access-Key", hash));
    }

    private static async Task<Ok<LegacyApiResponse<LegacyPluginResponse?>>> LegacyPluginConfigPost(
        LegacyPluginConfigUploadRequest request,
        LegacyMikanFilterStore store,
        CancellationToken cancellationToken)
    {
        if (!TryResolveLegacyMikanPlugin(request.Name, out var responseName))
        {
            return TypedResults.Ok(new LegacyApiResponse<LegacyPluginResponse?>(
                300, "不支持的插件配置", null));
        }
        if (!TryDecodeLegacyPluginConfig(request.Data, out var config))
        {
            return TypedResults.Ok(new LegacyApiResponse<LegacyPluginResponse?>(
                300, "配置解析错误", null));
        }

        const int maxAttempts = 32;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await store.SaveLegacyAsync(
                    "mikan", config!, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
                return TypedResults.Ok(new LegacyApiResponse<LegacyPluginResponse?>(
                    200, "写入插件配置文件成功", new LegacyPluginResponse(responseName)));
            }
            catch (LegacyMikanFilterRevisionException) when (attempt < maxAttempts)
            {
                // The legacy client has no revision field. Retrying preserves its last-full-upload-wins contract.
            }
        }
    }

    private static async Task<Ok<LegacyApiResponse<LegacyPluginConfigResponse?>>> LegacyPluginConfigGet(
        string? name,
        LegacyMikanFilterStore store,
        CancellationToken cancellationToken)
    {
        if (!TryResolveLegacyMikanPlugin(name, out var responseName))
        {
            return TypedResults.Ok(new LegacyApiResponse<LegacyPluginConfigResponse?>(
                300, "不支持的插件配置", null));
        }
        var snapshot = await store.GetAsync("mikan", cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return TypedResults.Ok(new LegacyApiResponse<LegacyPluginConfigResponse?>(
                300, "读取插件配置文件失败", null));
        }
        var data = Convert.ToBase64String(LegacyMikanFilterCodec.Encode(snapshot.Config));
        return TypedResults.Ok(new LegacyApiResponse<LegacyPluginConfigResponse?>(
            200,
            "读取插件配置文件成功",
            new LegacyPluginConfigResponse(responseName, data)));
    }

    private static bool TryResolveLegacyMikanPlugin(string? value, out string responseName)
    {
        responseName = value ?? string.Empty;
        var normalized = responseName.Trim().Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("plugin/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["plugin/".Length..];
        }
        return normalized is "filter/mikan_tool.py" or "filter/mikan_tool"
            or "mikan_tool.py" or "mikan_tool";
    }

    private static bool TryDecodeLegacyPluginConfig(
        string? value,
        out LegacyMikanFilterConfig? config)
    {
        config = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1_400_000) return false;
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length is 0 or > 1_048_576) return false;
            config = LegacyMikanFilterCodec.Parse(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static async Task<Ok<LegacyApiResponse<LegacyBoltListResponse?>>> LegacyBoltList(
        string? db,
        string? type,
        string? bucket,
        SqliteJsonCacheStore store,
        CancellationToken cancellationToken)
    {
        var databaseName = string.IsNullOrWhiteSpace(db) ? "bolt" : db;
        var listType = type?.Trim().ToLowerInvariant();
        try
        {
            IReadOnlyList<string> values;
            string? responseBucket = null;
            if (listType == "bucket")
            {
                values = await store.ListBucketsAsync(
                    databaseName,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (listType == "key")
            {
                if (string.IsNullOrWhiteSpace(bucket))
                {
                    return TypedResults.Ok(new LegacyApiResponse<LegacyBoltListResponse?>(
                        300, "参数错误，type为 key 时，需要 bucket 参数", null));
                }
                responseBucket = bucket.Trim();
                values = await store.ListKeysAsync(
                    databaseName,
                    responseBucket,
                    DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return TypedResults.Ok(new LegacyApiResponse<LegacyBoltListResponse?>(
                    300, "参数错误，type 仅支持 bucket 和 key", null));
            }

            return TypedResults.Ok(new LegacyApiResponse<LegacyBoltListResponse?>(
                200,
                "列表",
                new LegacyBoltListResponse(listType, responseBucket, values)));
        }
        catch (ArgumentException)
        {
            return TypedResults.Ok(new LegacyApiResponse<LegacyBoltListResponse?>(
                300, "参数错误，未找到数据库或缓存标识无效", null));
        }
    }

    private static async Task<Ok<LegacyApiResponse<LegacyBoltGetResponse?>>> LegacyBoltGet(
        string? db,
        string? bucket,
        string? key,
        SqliteJsonCacheStore store,
        CancellationToken cancellationToken)
    {
        var databaseName = string.IsNullOrWhiteSpace(db) ? "bolt" : db;
        try
        {
            var value = await store.GetJsonAsync(
                databaseName,
                bucket ?? string.Empty,
                key ?? string.Empty,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            if (value is null)
            {
                return TypedResults.Ok(new LegacyApiResponse<LegacyBoltGetResponse?>(
                    300, "查询失败，Key不存在或已过期", null));
            }

            using var document = JsonDocument.Parse(value.ValueJson);
            return TypedResults.Ok(new LegacyApiResponse<LegacyBoltGetResponse?>(
                200,
                "查询结果",
                new LegacyBoltGetResponse(
                    value.Bucket,
                    value.Key,
                    value.ExpiresAtUtc?.ToUnixTimeSeconds() ?? 0,
                    document.RootElement.Clone())));
        }
        catch (ArgumentException)
        {
            return TypedResults.Ok(new LegacyApiResponse<LegacyBoltGetResponse?>(
                300, "参数错误，未找到数据库或缓存标识无效", null));
        }
    }

    private static async Task<Ok<LegacyApiResponse<LegacyBoltDeleteResponse?>>> LegacyBoltDelete(
        string? db,
        string? bucket,
        string? key,
        SqliteJsonCacheStore store,
        CancellationToken cancellationToken)
    {
        var databaseName = string.IsNullOrWhiteSpace(db) ? "bolt" : db.Trim();
        if (!string.Equals(databaseName, "bolt", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Ok(new LegacyApiResponse<LegacyBoltDeleteResponse?>(
                300, "参数错误，只能删除 bolt 数据库中的数据", null));
        }

        try
        {
            await store.DeleteAsync(
                "bolt",
                bucket ?? string.Empty,
                key ?? string.Empty,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new LegacyApiResponse<LegacyBoltDeleteResponse?>(
                200, "删除成功", null));
        }
        catch (ArgumentException)
        {
            return TypedResults.Ok(new LegacyApiResponse<LegacyBoltDeleteResponse?>(
                300, "参数错误，缓存标识无效", null));
        }
    }

    private static Ok<RuntimeStatus> Status(AnimeGoOptions options)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        return TypedResults.Ok(new RuntimeStatus(
            version,
            DatabaseSchema.CurrentVersion,
            !RuntimeFeature.IsDynamicCodeSupported,
            System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            new RuntimePaths(
                options.Paths.DataPath,
                options.Paths.DownloadPath,
                options.Paths.SavePath),
            new RuntimeCapabilities(
                Configuration: true,
                Sqlite: true,
                UnifiedIngest: true,
                RssRules: true,
                Qbittorrent: true,
                Tmdb: !string.IsNullOrWhiteSpace(options.Metadata.Tmdb.ApiKey)
                    || !string.IsNullOrWhiteSpace(options.Metadata.Tmdb.ReadAccessToken),
                Organizer: true,
                Deletion: true)));
    }

    private static async Task<Ok<ConfigurationResponse>> Configuration(
        AnimeGoOptions options,
        RuntimeConfigurationState runtime,
        DeploymentConfigurationOptions deployment,
        DeploymentConfigurationLocks locks,
        ApplicationOverrideStore store,
        ApplicationConfigurationRuntimeState applied,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var desired = locks.Reapply(
            deployment.Value,
            ApplicationOverrideStore.Apply(deployment.Value, snapshot));
        return TypedResults.Ok(ToConfigurationResponse(
            options,
            desired,
            snapshot.Settings,
            runtime,
            locks,
            snapshot.Revision,
            applied.AppliedRevision));
    }

    private static async Task<IResult> PutConfiguration(
        ConfigurationUpdateRequest request,
        DeploymentConfigurationOptions deployment,
        DeploymentConfigurationLocks locks,
        ApplicationOverrideStore store,
        ApplicationConfigurationRuntimeState applied,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (request.ClearTmdbApiKey && !string.IsNullOrWhiteSpace(request.TmdbApiKey))
            {
                throw new ArgumentException("tmdb_api_key and clear_tmdb_api_key cannot both be set.");
            }

            if (request.ClearTmdbReadAccessToken
                && !string.IsNullOrWhiteSpace(request.TmdbReadAccessToken))
            {
                throw new ArgumentException(
                    "tmdb_read_access_token and clear_tmdb_read_access_token cannot both be set.");
            }

            var requestedSettings = CreateApplicationOverride(
                request,
                current.Settings,
                DateTimeOffset.UtcNow);
            var requestedCandidate = ApplicationOverrideStore.Apply(
                deployment.Value,
                new ApplicationOverrideSnapshot(
                    1,
                    current.Revision + 1,
                    requestedSettings));
            var changedLockedFields = locks
                .FindChangedLockedFields(deployment.Value, requestedCandidate)
                .ToList();
            if (locks.IsLocked("tmdb_api_key")
                && (request.ClearTmdbApiKey || !string.IsNullOrWhiteSpace(request.TmdbApiKey)))
            {
                changedLockedFields.Add("tmdb_api_key");
            }
            if (locks.IsLocked("tmdb_read_access_token")
                && (request.ClearTmdbReadAccessToken
                    || !string.IsNullOrWhiteSpace(request.TmdbReadAccessToken)))
            {
                changedLockedFields.Add("tmdb_read_access_token");
            }
            if (changedLockedFields.Count > 0)
            {
                throw new ConfigurationFieldLockedException(
                    changedLockedFields.Distinct(StringComparer.Ordinal).ToArray());
            }

            var settings = locks.PreserveLockedOverrides(
                current.Settings,
                requestedSettings);
            var candidate = locks.Reapply(
                deployment.Value,
                ApplicationOverrideStore.Apply(
                    deployment.Value,
                    new ApplicationOverrideSnapshot(
                        1,
                        current.Revision + 1,
                        settings)));
            var errors = AnimeGoOptionsValidator.Validate(candidate);
            if (errors.Count > 0)
            {
                throw new ArgumentException(string.Join("; ", errors));
            }

            var saved = await store.SaveAsync(
                settings,
                request.ExpectedConfigurationRevision,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new ConfigurationWriteResponse(
                saved.Revision,
                RestartRequired: saved.Revision != applied.AppliedRevision,
                RevertedToDeploymentDefault: false));
        }
        catch (ApplicationOverrideRevisionException)
        {
            return TypedResults.Conflict(Error(
                "configuration_revision_conflict",
                "Configuration changed concurrently; reload before saving."));
        }
        catch (ConfigurationFieldLockedException exception)
        {
            return TypedResults.BadRequest(Error(
                "configuration_field_locked",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("configuration_invalid", exception.Message));
        }
    }

    private static async Task<IResult> DeleteConfigurationOverride(
        [FromQuery(Name = "expected_revision")] long expectedRevision,
        ApplicationOverrideStore store,
        ApplicationConfigurationRuntimeState applied,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await store.DeleteAsync(expectedRevision, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new ConfigurationWriteResponse(
                saved.Revision,
                RestartRequired: saved.Revision != applied.AppliedRevision,
                RevertedToDeploymentDefault: true));
        }
        catch (ApplicationOverrideRevisionException)
        {
            return TypedResults.Conflict(Error(
                "configuration_revision_conflict",
                "Configuration changed concurrently; reload before reverting."));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return TypedResults.BadRequest(Error("configuration_invalid", exception.Message));
        }
    }

    private static ConfigurationResponse ToConfigurationResponse(
        AnimeGoOptions options,
        AnimeGoOptions desired,
        ApplicationOverrideEntry? settings,
        RuntimeConfigurationState runtime,
        DeploymentConfigurationLocks locks,
        long configurationRevision,
        long appliedConfigurationRevision)
    {
        var tmdb = options.Metadata.Tmdb;
        var bangumi = options.Metadata.Bangumi;
        var season = options.Metadata.SeasonFailure;
        var ai = options.Metadata.Ai;
        var fetch = options.TorrentFetch;
        return new ConfigurationResponse(
            configurationRevision,
            appliedConfigurationRevision,
            configurationRevision != appliedConfigurationRevision,
            new RuntimePaths(
                options.Paths.DataPath,
                options.Paths.DownloadPath,
                options.Paths.SavePath),
            new DeploymentConfigurationResponse(
                runtime.RunningInContainer,
                runtime.BackgroundWorkersEnabled,
                runtime.AccessKeyConfigured,
                PathsRestartRequired: true),
            new MetadataConfigurationResponse(
                new TmdbConfigurationResponse(
                    tmdb.BaseUrl.AbsoluteUri,
                    tmdb.ProxyUrl?.AbsoluteUri,
                    tmdb.Language,
                    tmdb.HttpTimeout.TotalSeconds,
                    !string.IsNullOrWhiteSpace(tmdb.ApiKey),
                    !string.IsNullOrWhiteSpace(tmdb.ReadAccessToken)),
                new BangumiConfigurationResponse(
                    bangumi.BaseUrl.AbsoluteUri,
                    bangumi.ProxyUrl?.AbsoluteUri,
                    bangumi.HttpTimeout.TotalSeconds),
                new SeasonFailureConfigurationResponse(
                    season.Skip,
                    season.Backtrace,
                    season.UseTitleSeason,
                    season.UseFirstSeason),
                new AiConfigurationResponse(
                    ai.Provider,
                    ai.BaseUrl?.AbsoluteUri,
                    ai.Model,
                    !string.IsNullOrWhiteSpace(ai.ApiKey),
                    ai.UseMetadataMatch,
                    ai.UseMetadataMatch,
                    ai.UseMetadataMatch,
                    ai.HttpTimeout.TotalSeconds,
                    ai.RetryCount,
                    ai.UseBangumiPubDateFirst,
                    ai.TmdbMcpUrl.AbsoluteUri,
                    ai.BangumiMcpUrl.AbsoluteUri),
                options.Metadata.TmdbFailureUseBangumi,
                options.Metadata.MikanTrustedOffsetCacheEnabled),
            new TorrentFetchConfigurationResponse(
                fetch.Timeout.TotalSeconds,
                fetch.MaxResponseBytes,
                fetch.MaxRedirects,
                fetch.StagingTtl.TotalSeconds),
            ToEditableConfiguration(desired, settings, locks));
    }

    private static EditableConfigurationResponse ToEditableConfiguration(
        AnimeGoOptions desired,
        ApplicationOverrideEntry? settings,
        DeploymentConfigurationLocks locks)
    {
        var tmdb = desired.Metadata.Tmdb;
        var bangumi = desired.Metadata.Bangumi;
        var season = desired.Metadata.SeasonFailure;
        var ai = desired.Metadata.Ai;
        var fetch = desired.TorrentFetch;
        return new EditableConfigurationResponse(
            tmdb.BaseUrl.AbsoluteUri,
            tmdb.ProxyUrl?.AbsoluteUri,
            tmdb.Language,
            tmdb.HttpTimeout.TotalSeconds,
            SecretState(settings?.TmdbApiKeyOverridden == true, settings?.TmdbApiKey),
            SecretState(
                settings?.TmdbReadAccessTokenOverridden == true,
                settings?.TmdbReadAccessToken),
            bangumi.BaseUrl.AbsoluteUri,
            bangumi.ProxyUrl?.AbsoluteUri,
            bangumi.HttpTimeout.TotalSeconds,
            season.Skip,
            season.Backtrace,
            season.UseTitleSeason,
            season.UseFirstSeason,
            ai.UseMetadataMatch,
            ai.UseMetadataMatch,
            ai.UseMetadataMatch,
            ai.HttpTimeout.TotalSeconds,
            desired.Metadata.TmdbFailureUseBangumi,
            desired.Metadata.MikanTrustedOffsetCacheEnabled,
            fetch.Timeout.TotalSeconds,
            fetch.MaxResponseBytes,
            fetch.MaxRedirects,
            fetch.StagingTtl.TotalSeconds,
            locks.Items.Select(item => new ConfigurationFieldLockResponse(
                item.Field,
                "environment",
                item.EnvironmentVariables)).ToArray());
    }

    private static string SecretState(bool overridden, string? value) =>
        !overridden ? "inherit" : value is null ? "cleared" : "configured";

    private static ApplicationOverrideEntry CreateApplicationOverride(
        ConfigurationUpdateRequest request,
        ApplicationOverrideEntry? current,
        DateTimeOffset utcNow)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(request.ExpectedConfigurationRevision);
        var baseUrl = request.TmdbBaseUrl?.Trim()
            ?? throw new ArgumentException("tmdb_base_url is required.");
        var language = request.TmdbLanguage?.Trim()
            ?? throw new ArgumentException("tmdb_language is required.");
        var bangumiBaseUrl = request.BangumiBaseUrl?.Trim()
            ?? throw new ArgumentException("bangumi_base_url is required.");
        if (baseUrl.Length is < 1 or > 2048)
        {
            throw new ArgumentException("tmdb_base_url must contain 1 to 2048 characters.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("tmdb_base_url must be an absolute URL.");
        }
        var tmdbProxyUrl = NormalizeOptionalUrl(request.TmdbProxyUrl, "tmdb_proxy_url");

        if (bangumiBaseUrl.Length is < 1 or > 2048
            || !Uri.TryCreate(bangumiBaseUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "bangumi_base_url must contain an absolute URL of at most 2048 characters.");
        }
        var bangumiProxyUrl = NormalizeOptionalUrl(
            request.BangumiProxyUrl,
            "bangumi_proxy_url");

        if (language.Length is < 1 or > 32)
        {
            throw new ArgumentException("tmdb_language must contain 1 to 32 characters.");
        }

        ValidateSeconds(request.TmdbHttpTimeoutSeconds, "tmdb_http_timeout_seconds", 86_400);
        ValidateSeconds(
            request.BangumiHttpTimeoutSeconds,
            "bangumi_http_timeout_seconds",
            86_400);
        ValidateSeconds(request.AiHttpTimeoutSeconds, "ai_http_timeout_seconds", 86_400);
        ValidateSeconds(request.TorrentHttpTimeoutSeconds, "torrent_http_timeout_seconds", 86_400);
        ValidateSeconds(request.TorrentStagingTtlSeconds, "torrent_staging_ttl_seconds", 604_800);
        if (request.TorrentMaxResponseBytes is < 1 or > 1_073_741_824)
        {
            throw new ArgumentException(
                "torrent_max_response_bytes must be between 1 and 1073741824.");
        }

        if (request.TorrentMaxRedirects is < 0 or > 10)
        {
            throw new ArgumentException("torrent_max_redirects must be between 0 and 10.");
        }

        var apiKey = NormalizeSecret(request.TmdbApiKey, "tmdb_api_key");
        var readToken = NormalizeSecret(
            request.TmdbReadAccessToken,
            "tmdb_read_access_token");
        var apiKeyOverridden = request.ClearTmdbApiKey
            || apiKey is not null
            || current?.TmdbApiKeyOverridden == true;
        var readTokenOverridden = request.ClearTmdbReadAccessToken
            || readToken is not null
            || current?.TmdbReadAccessTokenOverridden == true;
        var aiUseMetadataMatch = request.AiUseMetadataMatch
            ?? (request.AiUseSeasonMatch.GetValueOrDefault()
                || request.AiUseEpisodeMatch.GetValueOrDefault());
        return new ApplicationOverrideEntry(
            baseUrl,
            language,
            request.TmdbHttpTimeoutSeconds,
            apiKeyOverridden,
            request.ClearTmdbApiKey ? null : apiKey ?? current?.TmdbApiKey,
            readTokenOverridden,
            request.ClearTmdbReadAccessToken ? null : readToken ?? current?.TmdbReadAccessToken,
            request.SeasonFailureSkip,
            request.SeasonFailureBacktrace,
            request.SeasonFailureUseTitleSeason,
            request.SeasonFailureUseFirstSeason,
            aiUseMetadataMatch,
            aiUseMetadataMatch,
            request.AiHttpTimeoutSeconds,
            request.TmdbFailureUseBangumi,
            request.MikanTrustedOffsetCacheEnabled,
            request.TorrentHttpTimeoutSeconds,
            request.TorrentMaxResponseBytes,
            request.TorrentMaxRedirects,
            request.TorrentStagingTtlSeconds,
            utcNow,
            TmdbProxyUrlOverridden: true,
            TmdbProxyUrl: tmdbProxyUrl,
            BangumiBaseUrl: bangumiBaseUrl,
            BangumiProxyUrlOverridden: true,
            BangumiProxyUrl: bangumiProxyUrl,
            BangumiHttpTimeoutSeconds: request.BangumiHttpTimeoutSeconds,
            AiUseMetadataMatch: aiUseMetadataMatch);
    }

    private static void ValidateSeconds(double value, string name, double maximum)
    {
        if (!double.IsFinite(value) || value <= 0 || value > maximum)
        {
            throw new ArgumentException($"{name} must be greater than 0 and at most {maximum}.");
        }
    }

    private static string? NormalizeSecret(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 8192 || normalized.Any(character => character is '\r' or '\n'))
        {
            throw new ArgumentException($"{name} is invalid.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalUrl(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 2048
            || !Uri.TryCreate(normalized, UriKind.Absolute, out _))
        {
            throw new ArgumentException($"{name} must be an absolute URL of at most 2048 characters.");
        }
        return normalized;
    }

    private static async Task<IResult> Downloads(
        [FromQuery] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? state,
        [FromQuery(Name = "business_status")] string? businessStatus,
        [FromQuery(Name = "downloader_id")] string? downloaderId,
        [FromQuery] string? source,
        DownloadJobStore jobs,
        CancellationToken cancellationToken)
    {
        var resolvedPage = page ?? 1;
        var resolvedPageSize = pageSize ?? 25;
        if (resolvedPage < 1 || resolvedPageSize is < 1 or > 100)
        {
            return TypedResults.BadRequest(Error(
                "download_pagination_invalid",
                "Download page must be positive and page_size must be between 1 and 100."));
        }

        try
        {
            var records = await jobs.ListPageAsync(
                new DownloadJobListQuery(
                    resolvedPage,
                    resolvedPageSize,
                    search,
                    state,
                    businessStatus,
                    downloaderId,
                    source),
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new DownloadListResponse(
                records.Page,
                records.PageSize,
                records.TotalItems,
                NormalizeEcho(search),
                NormalizeEcho(state),
                NormalizeEcho(businessStatus),
                NormalizeEcho(downloaderId),
                NormalizeEcho(source),
                new DownloadDashboardSummary(
                    records.Summary.TotalJobs,
                    records.Summary.ActiveJobs,
                    records.Summary.PausedJobs,
                    records.Summary.FailedJobs,
                    records.Summary.StaleJobs,
                    records.Summary.WaitingOrganizationJobs,
                    records.Summary.CompletedJobs,
                    records.Summary.PreparationFailedJobs,
                    records.Summary.OrganizationFailedJobs,
                    records.Summary.ConnectedDownloadSpeedBytesPerSecond,
                    records.Summary.OfflineInstanceCount,
                    records.Summary.LatestFailureCode,
                    records.Summary.LastDownloaderSuccessAtUtc),
                records.Items.Select(ToResponse).ToArray()));
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest(Error(
                "download_filter_invalid",
                "Download filters must use short printable search text and stable lowercase identifiers."));
        }
    }

    private static async Task<IResult> DownloadDetail(
        string jobId,
        [FromQuery(Name = "timeline_limit")] int? timelineLimit,
        DownloadJobStore jobs,
        DownloadClientOperationCoordinator clients,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return TypedResults.BadRequest(Error(
                "download_job_id_invalid",
                "Download job ID is required."));
        }

        var resolvedLimit = timelineLimit ?? 100;
        if (resolvedLimit is < 1 or > 500)
        {
            return TypedResults.BadRequest(Error(
                "download_timeline_limit_invalid",
                "Download timeline limit must be between 1 and 500."));
        }

        var detail = await jobs.GetDetailAsync(jobId, resolvedLimit, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return TypedResults.NotFound(Error(
                "download_job_not_found",
                "Download job was not found."));
        }

        var liveFiles = new Dictionary<int, DownloadFileSnapshot>();
        var fileSnapshotState = "live";
        string? fileSnapshotFailureCode = null;
        try
        {
            var snapshots = await clients.ExecuteAsync(
                detail.Summary.DownloaderId,
                async (client, token) =>
                {
                    await client.ConnectAsync(token).ConfigureAwait(false);
                    return await client.ListFilesAsync(detail.Summary.InfoHash, token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            liveFiles = snapshots
                .GroupBy(file => file.Index)
                .ToDictionary(group => group.Key, group => group.First());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (DownloadFailureCode(exception) is not null)
        {
            fileSnapshotState = "unavailable";
            fileSnapshotFailureCode = DownloadFailureCode(exception);
        }

        var liveFilesByPath = liveFiles.Values
            .GroupBy(file => NormalizeDownloadFilePath(file.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var matchedLiveFileIndexes = new HashSet<int>();
        var files = detail.Files.Select(file =>
        {
            var normalizedPath = NormalizeDownloadFilePath(file.RelativePath);
            var live = file.DownloadFileIndex is int index
                && liveFiles.TryGetValue(index, out var snapshot)
                    ? snapshot
                    : liveFilesByPath.GetValueOrDefault(normalizedPath);
            if (live is not null)
            {
                matchedLiveFileIndexes.Add(live.Index);
            }
            var wanted = live?.Wanted ?? file.Wanted;
            var priority = live?.Priority ?? file.Priority;
            return new DownloadFileDetail(
                normalizedPath,
                file.SizeBytes,
                file.DownloadFileIndex ?? live?.Index,
                wanted,
                priority,
                live?.Progress,
                live is null
                    ? null
                    : (long)Math.Floor(Math.Clamp(live.Progress, 0, 1) * live.SizeBytes),
                file.Disposition,
                file.OtherReason);
        }).ToList();
        files.AddRange(liveFiles.Values
            .Where(file => !matchedLiveFileIndexes.Contains(file.Index))
            .OrderBy(file => file.Index)
            .Select(file => new DownloadFileDetail(
                NormalizeDownloadFilePath(file.RelativePath),
                file.SizeBytes,
                file.Index,
                file.Wanted,
                file.Priority,
                file.Progress,
                (long)Math.Floor(Math.Clamp(file.Progress, 0, 1) * file.SizeBytes),
                "unassigned",
                null)));
        var canRetry = CanRetry(detail);
        return TypedResults.Ok(new DownloadDetailResponse(
            ToResponse(detail.Summary),
            detail.TaskFailureKind,
            detail.TaskFailureReason,
            new DownloadStageDetail(
                detail.PreparationState,
                detail.PreparationAttemptCount,
                detail.PreparationNextAttemptAtUtc,
                detail.PreparationFailureCode),
            new DownloadStageDetail(
                detail.OrganizationState,
                detail.OrganizationAttemptCount,
                detail.OrganizationNextAttemptAtUtc,
                detail.OrganizationFailureCode),
            fileSnapshotState,
            fileSnapshotFailureCode,
            detail.Summary.State is "waiting" or "downloading" or "moving" or "seeding",
            detail.Summary.State == "paused",
            canRetry,
            files,
            detail.Events.Select(item => new DownloadTimelineItem(
                item.EventId,
                item.Kind,
                item.Result,
                item.FromState,
                item.ToState,
                item.FailureCode,
                item.CreatedAtUtc)).ToArray()));
    }

    private static Task<IResult> PauseDownload(
        string jobId,
        DownloadControlRequest request,
        DownloadJobStore jobs,
        DownloadClientOperationCoordinator clients,
        CancellationToken cancellationToken) =>
        ControlDownload(
            jobId, request, "pause", "paused", jobs, clients, cancellationToken);

    private static Task<IResult> ResumeDownload(
        string jobId,
        DownloadControlRequest request,
        DownloadJobStore jobs,
        DownloadClientOperationCoordinator clients,
        CancellationToken cancellationToken) =>
        ControlDownload(
            jobId, request, "resume", "waiting", jobs, clients, cancellationToken);

    private static async Task<IResult> RetryDownload(
        string jobId,
        DownloadControlRequest request,
        DownloadJobStore jobs,
        DownloadClientOperationCoordinator clients,
        CancellationToken cancellationToken)
    {
        var target = await ValidateControlTargetAsync(
            jobId, request, jobs, cancellationToken).ConfigureAwait(false);
        if (target.Error is not null)
        {
            return target.Error;
        }

        var value = target.Value!;
        if ((value.PreparationState == "pending"
             && value.PreparationLeaseToken is null
             && value.PreparationFailureCode is not null)
            || ((value.OrganizationState is "pending" or "cleanup")
                && value.OrganizationLeaseToken is null
                && value.OrganizationFailureCode is not null))
        {
            var result = await jobs.RetryBusinessStageAsync(
                value,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return await ControlResultAsync(
                value.JobId,
                "retry",
                result,
                jobs,
                cancellationToken).ConfigureAwait(false);
        }

        return await ControlDownload(
            jobId,
            request,
            "retry_download",
            "waiting",
            jobs,
            clients,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> ControlDownload(
        string jobId,
        DownloadControlRequest request,
        string kind,
        string targetState,
        DownloadJobStore jobs,
        DownloadClientOperationCoordinator clients,
        CancellationToken cancellationToken)
    {
        var target = await ValidateControlTargetAsync(
            jobId, request, jobs, cancellationToken).ConfigureAwait(false);
        if (target.Error is not null)
        {
            return target.Error;
        }

        var value = target.Value!;
        if (!ControlStateAllowed(kind, value.State))
        {
            return TypedResults.Conflict(Error(
                "download_action_invalid_state",
                $"Download action '{kind}' is not allowed from state '{value.State}'."));
        }

        try
        {
            await clients.ExecuteAsync(
                value.DownloaderId,
                async (client, token) =>
                {
                    await client.ConnectAsync(token).ConfigureAwait(false);
                    if (kind == "pause")
                    {
                        await client.PauseAsync([value.InfoHash], token).ConfigureAwait(false);
                    }
                    else
                    {
                        await client.ResumeAsync([value.InfoHash], token).ConfigureAwait(false);
                    }

                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (DownloadFailureCode(exception) is { } failureCode)
        {
            await jobs.RecordControlFailureAsync(
                value.JobId,
                kind,
                failureCode,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Json(
                Error(failureCode, "Downloader action failed."),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var result = await jobs.ApplyRemoteControlAsync(
            value,
            kind,
            targetState,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return await ControlResultAsync(
            value.JobId,
            kind == "retry_download" ? "retry" : kind,
            result,
            jobs,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(
        DownloadJobControlTarget? Value,
        IResult? Error)> ValidateControlTargetAsync(
        string jobId,
        DownloadControlRequest request,
        DownloadJobStore jobs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId) || request.ExpectedRevision <= 0)
        {
            return (null, TypedResults.BadRequest(Error(
                "download_control_invalid",
                "Download control requires a job ID and positive expected_revision.")));
        }

        var target = await jobs.GetControlTargetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return (null, TypedResults.NotFound(Error(
                "download_job_not_found",
                "Download job was not found.")));
        }

        if (target.Revision != request.ExpectedRevision)
        {
            return (null, TypedResults.Conflict(Error(
                "download_revision_conflict",
                "Download job changed; reload before retrying the action.")));
        }

        return (target, null);
    }

    private static async Task<IResult> ControlResultAsync(
        string jobId,
        string action,
        DownloadJobControlUpdateResult result,
        DownloadJobStore jobs,
        CancellationToken cancellationToken)
    {
        if (result == DownloadJobControlUpdateResult.Updated)
        {
            var updated = await jobs.GetControlTargetAsync(jobId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Updated download job disappeared.");
            return TypedResults.Ok(new DownloadControlResponse(
                updated.JobId,
                action,
                updated.State,
                updated.Revision));
        }

        return result switch
        {
            DownloadJobControlUpdateResult.NotFound => TypedResults.NotFound(Error(
                "download_job_not_found",
                "Download job was not found.")),
            DownloadJobControlUpdateResult.RevisionConflict => TypedResults.Conflict(Error(
                "download_revision_conflict",
                "Download job changed; reload before retrying the action.")),
            _ => TypedResults.Conflict(Error(
                "download_action_invalid_state",
                "Download action is not allowed in the current stage.")),
        };
    }

    private static async Task<Ok<DownloaderInstanceListResponse>> ListDownloaders(
        AnimeGoOptions options,
        DownloaderAdminStore admin,
        DownloaderOverrideStore overrides,
        DownloaderConfigurationRuntimeState runtimeState,
        DownloadClientOperationCoordinator clients,
        CancellationToken cancellationToken)
    {
        var snapshot = await overrides.LoadAsync(cancellationToken).ConfigureAwait(false);
        var restartRequired = snapshot.Revision != runtimeState.AppliedRevision;
        var items = new List<DownloaderInstanceResponse>();
        var ids = options.Downloaders.Keys
            .Concat(snapshot.Downloaders.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            snapshot.Downloaders.TryGetValue(id, out var pending);
            var downloader = pending is null
                ? options.Downloaders[id]
                : ToOptions(pending);
            var usage = await admin.GetUsageAsync(id, cancellationToken).ConfigureAwait(false);
            items.Add(ToResponse(
                id, downloader, usage,
                pending is null ? "deployment" : "private_override",
                pending?.Revision,
                restartRequired,
                clients.GetCircuitSnapshot(id)));
        }
        return TypedResults.Ok(new DownloaderInstanceListResponse(
            snapshot.Revision, runtimeState.AppliedRevision, restartRequired, items));
    }

    private static async Task<IResult> PutDownloader(
        string downloaderId,
        DownloaderInstanceUpsertRequest request,
        AnimeGoOptions options,
        DownloaderAdminStore admin,
        DownloaderOverrideStore overrides,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = RequireCanonicalStableId(downloaderId, "downloader id");
            if (request.ExpectedConfigurationRevision < 0)
                throw new ArgumentException("expected_configuration_revision must not be negative.");
            var snapshot = await overrides.LoadAsync(cancellationToken).ConfigureAwait(false);
            snapshot.Downloaders.TryGetValue(id, out var currentOverride);
            options.Downloaders.TryGetValue(id, out var currentRuntime);
            if (!request.Enabled)
            {
                var usage = await admin.GetUsageAsync(id, cancellationToken).ConfigureAwait(false);
                if (usage.SourceProfileCount + usage.IngestTaskCount + usage.DownloadJobCount > 0)
                {
                    return TypedResults.Conflict(Error(
                        "downloader_in_use", "Referenced downloader instances cannot be disabled."));
                }
            }
            var baseUrl = ValidateDownloaderBaseUrl(request.BaseUrl);
            var downloadPath = request.DownloadPath?.Trim() ?? string.Empty;
            if (!PathBoundary.IsAbsolute(downloadPath)
                || !PathBoundary.IsWithin(options.Paths.DownloadPath, downloadPath))
            {
                throw new ArgumentException("download_path must be inside the configured download root.");
            }
            if (request.ClearPassword && request.Password is not null)
                throw new ArgumentException("password and clear_password cannot be supplied together.");
            var password = request.ClearPassword
                ? null
                : request.Password ?? currentOverride?.Password ?? currentRuntime?.Password;
            var username = request.Username is null
                ? currentOverride?.Username ?? currentRuntime?.Username
                : string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim();
            if (password?.Length > 1024)
                throw new ArgumentException("password must not exceed 1024 characters.");
            var saved = await overrides.UpsertAsync(
                id,
                new DownloaderOverrideEntry(
                    baseUrl.AbsoluteUri,
                    username,
                    password,
                    downloadPath,
                    request.Enabled,
                    0,
                    DateTimeOffset.UtcNow),
                request.ExpectedConfigurationRevision,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new DownloaderConfigurationWriteResponse(
                id, saved.Revision, saved.Downloaders[id].Revision, true, false));
        }
        catch (DownloaderOverrideRevisionException)
        {
            return TypedResults.Conflict(Error(
                "downloader_configuration_revision_conflict",
                "Downloader configuration changed; reload before saving."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("downloader_configuration_invalid", exception.Message));
        }
    }

    private static async Task<IResult> DeleteDownloaderOverride(
        string downloaderId,
        [FromQuery(Name = "expected_configuration_revision")] long expectedConfigurationRevision,
        AnimeGoOptions options,
        DownloaderAdminStore admin,
        DownloaderOverrideStore overrides,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = RequireCanonicalStableId(downloaderId, "downloader id");
            var usage = await admin.GetUsageAsync(id, cancellationToken).ConfigureAwait(false);
            if (usage.SourceProfileCount + usage.IngestTaskCount + usage.DownloadJobCount > 0)
            {
                return TypedResults.Conflict(Error(
                    "downloader_in_use", "Referenced downloader overrides cannot be removed."));
            }
            var saved = await overrides.DeleteAsync(
                id, expectedConfigurationRevision, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new DownloaderConfigurationWriteResponse(
                id, saved.Revision, null, true, options.Downloaders.ContainsKey(id)));
        }
        catch (DownloaderOverrideRevisionException)
        {
            return TypedResults.Conflict(Error(
                "downloader_configuration_revision_conflict",
                "Downloader configuration changed; reload before deleting."));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound(Error(
                "downloader_override_not_found", "Downloader private override was not found."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("downloader_configuration_invalid", exception.Message));
        }
    }

    private static async Task<IResult> TestDownloader(
        string downloaderId,
        AnimeGoOptions options,
        DownloadClientOperationCoordinator clients,
        DownloaderAdminStore admin,
        CancellationToken cancellationToken)
    {
        var id = downloaderId.Trim().ToLowerInvariant();
        if (!options.Downloaders.TryGetValue(id, out var optionsForInstance))
        {
            return TypedResults.NotFound(Error("downloader_not_found", "Downloader instance was not found."));
        }
        if (!optionsForInstance.Enabled)
        {
            return TypedResults.Conflict(Error("downloader_disabled", "Downloader instance is disabled."));
        }

        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var probe = await clients.ExecuteProbeAsync(
                id,
                async (client, token) =>
                {
                    await client.ConnectAsync(token).ConfigureAwait(false);
                    var tasks = await client.ListAsync(token).ConfigureAwait(false);
                    var version = client is IDownloadClientDiagnostics diagnostics
                        ? await diagnostics.GetVersionAsync(token).ConfigureAwait(false)
                        : null;
                    var defaultSavePath = client is IDownloadClientDiagnostics pathDiagnostics
                        ? await pathDiagnostics.GetDefaultSavePathAsync(token).ConfigureAwait(false)
                        : null;
                    return (TaskCount: tasks.Count, Version: version, DefaultSavePath: defaultSavePath);
                },
                cancellationToken).ConfigureAwait(false);
            timer.Stop();
            await admin.RecordConnectionTestAsync(
                id, true, null, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new DownloaderConnectionTestResponse(
                id, true, probe.TaskCount, timer.ElapsedMilliseconds, null,
                "qBittorrent authentication and task listing succeeded.",
                probe.Version,
                probe.DefaultSavePath));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await FailedDownloaderTest(
                id, "timeout", "qBittorrent connection test timed out.", timer, admin, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return await FailedDownloaderTest(
                id, "authentication_failed", "qBittorrent authentication failed.", timer, admin, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return await FailedDownloaderTest(
                id, "connection_failed", "qBittorrent could not be reached or returned an HTTP error.",
                timer, admin, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IResult> FailedDownloaderTest(
        string id,
        string failureCode,
        string message,
        System.Diagnostics.Stopwatch timer,
        DownloaderAdminStore admin,
        CancellationToken cancellationToken)
    {
        timer.Stop();
        await admin.RecordConnectionTestAsync(
            id, false, failureCode, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new DownloaderConnectionTestResponse(
            id, false, null, timer.ElapsedMilliseconds, failureCode, message, null, null));
    }

    private static IResult ProbeDownloaderPath(
        string downloaderId,
        AnimeGoOptions options)
    {
        var id = downloaderId.Trim().ToLowerInvariant();
        if (!options.Downloaders.TryGetValue(id, out var downloader))
        {
            return TypedResults.NotFound(Error("downloader_not_found", "Downloader instance was not found."));
        }
        if (!downloader.Enabled)
        {
            return TypedResults.Conflict(Error("downloader_disabled", "Downloader instance is disabled."));
        }
        var downloadPath = downloader.DownloadPath;
        var savePath = options.Paths.SavePath;
        if (!Directory.Exists(downloadPath) || !Directory.Exists(savePath))
        {
            return TypedResults.Ok(new DownloaderPathProbeResponse(
                id, false, false, downloadPath, savePath, "directory_missing",
                "Both download_path and save_path must already exist and be visible to AnimeGoNet."));
        }

        var token = Guid.NewGuid().ToString("N");
        var source = Path.Combine(downloadPath, $".animegonet-hardlink-{token}.tmp");
        var target = Path.Combine(savePath, $".animegonet-hardlink-{token}-target.tmp");
        try
        {
            File.WriteAllBytes(source, [0x41, 0x47, 0x4e]);
            HardLinkCapability.Create(target, source);
            var valid = File.Exists(target) && new FileInfo(target).Length == 3;
            return TypedResults.Ok(new DownloaderPathProbeResponse(
                id, valid, valid, downloadPath, savePath,
                valid ? null : "hard_link_verification_failed",
                valid
                    ? "Temporary hard link creation and verification succeeded."
                    : "The hard link target could not be verified."));
        }
        catch (UnauthorizedAccessException)
        {
            return PathProbeFailure(id, downloadPath, savePath, "permission_denied",
                "AnimeGoNet cannot write probe files in one or both configured directories.");
        }
        catch (IOException)
        {
            return PathProbeFailure(id, downloadPath, savePath, "hard_link_unavailable",
                "Hard links are unavailable; paths may be on different filesystems or mounts.");
        }
        catch (PlatformNotSupportedException)
        {
            return PathProbeFailure(id, downloadPath, savePath, "platform_not_supported",
                "This platform does not support the hard link probe.");
        }
        finally
        {
            TryDeleteProbeFile(target);
            TryDeleteProbeFile(source);
        }
    }

    private static Ok<DownloaderPathProbeResponse> PathProbeFailure(
        string id,
        string downloadPath,
        string savePath,
        string failureCode,
        string message) =>
        TypedResults.Ok(new DownloaderPathProbeResponse(
            id, false, false, downloadPath, savePath, failureCode, message));

    private static void TryDeleteProbeFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup must not replace the sanitized probe result.
        }
        catch (UnauthorizedAccessException)
        {
            // The probe result already reports permission failure without exposing the path exception.
        }
    }

    private static async Task<Ok<SourceProfileListResponse>> ListSourceProfiles(
        SourceProfileStore profiles,
        CancellationToken cancellationToken)
    {
        var records = await profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new SourceProfileListResponse(records.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> GetSourceProfile(
        string sourceProfileId,
        SourceProfileStore profiles,
        CancellationToken cancellationToken)
    {
        var record = await profiles.GetAsync(sourceProfileId, cancellationToken).ConfigureAwait(false);
        return record is null
            ? TypedResults.NotFound(Error("source_profile_not_found", "Source profile was not found."))
            : TypedResults.Ok(ToResponse(record));
    }

    private static async Task<IResult> CreateSourceProfile(
        SourceProfileCreateRequest request,
        AnimeGoOptions options,
        SourceProfileStore profiles,
        MikanRssRuleStore rules,
        LegacyMikanFilterStore legacyFilters,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = RequireCanonicalStableId(request.Id, "id");
            var definition = ToDefinition(
                request.DisplayName,
                request.Adapter,
                request.DownloaderId,
                request.FileStrategy,
                request.AllowedTorrentHosts,
                request.Category,
                request.Tags,
                request.SeedingTimeMinutes,
                request.RssFilterEnabled,
                request.RssPriorityEnabled,
                request.Enabled,
                current: null,
                options);
            var now = DateTimeOffset.UtcNow;
            var created = await profiles.CreateAsync(id, definition, now, cancellationToken).ConfigureAwait(false);
            await rules.EnsureDefaultAsync(
                id, MikanRssRuleDefaults.Create(), now, cancellationToken).ConfigureAwait(false);
            if (definition.Adapter == "mikan")
            {
                await legacyFilters.EnsureDefaultAsync(id, now, cancellationToken).ConfigureAwait(false);
            }
            return TypedResults.Created($"/api/v1/sources/{id}", ToResponse(created));
        }
        catch (SourceProfileDuplicateException)
        {
            return TypedResults.Conflict(Error(
                "source_profile_duplicate", "A source profile with this id already exists."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("source_profile_invalid", exception.Message));
        }
    }

    private static async Task<IResult> UpdateSourceProfile(
        string sourceProfileId,
        SourceProfileUpdateRequest request,
        AnimeGoOptions options,
        SourceProfileStore profiles,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = RequireCanonicalStableId(sourceProfileId, "source profile id");
            var current = await profiles.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                return TypedResults.NotFound(Error("source_profile_not_found", "Source profile was not found."));
            }
            if (request.ExpectedRevision < 1)
            {
                throw new ArgumentException("expected_revision must be at least 1.");
            }
            var definition = ToDefinition(
                request.DisplayName,
                current.Adapter,
                request.DownloaderId,
                request.FileStrategy,
                request.AllowedTorrentHosts,
                request.Category,
                request.Tags,
                request.SeedingTimeMinutes,
                request.RssFilterEnabled,
                request.RssPriorityEnabled,
                request.Enabled,
                current,
                options);
            var saved = await profiles.UpdateAsync(
                id, definition, request.ExpectedRevision, DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);
            return TypedResults.Ok(ToResponse(saved));
        }
        catch (SourceProfileRevisionException)
        {
            return TypedResults.Conflict(Error(
                "source_profile_revision_conflict", "Source profile changed; reload before saving."));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound(Error("source_profile_not_found", "Source profile was not found."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("source_profile_invalid", exception.Message));
        }
    }

    private static async Task<IResult> DeleteSourceProfile(
        string sourceProfileId,
        [FromQuery(Name = "expected_revision")] long expectedRevision,
        SourceProfileStore profiles,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = RequireCanonicalStableId(sourceProfileId, "source profile id");
            if (expectedRevision < 1)
            {
                throw new ArgumentException("expected_revision must be at least 1.");
            }
            await profiles.DeleteAsync(id, expectedRevision, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new SourceProfileDeleteResponse(id, true));
        }
        catch (SourceProfileRevisionException)
        {
            return TypedResults.Conflict(Error(
                "source_profile_revision_conflict", "Source profile changed; reload before deleting."));
        }
        catch (SourceProfileConflictException exception)
        {
            return TypedResults.Conflict(Error("source_profile_in_use", exception.Message));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound(Error("source_profile_not_found", "Source profile was not found."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("source_profile_invalid", exception.Message));
        }
    }

    private static async Task<IResult> PreviewSourceRoute(
        string sourceProfileId,
        SourceRoutePreviewRequest request,
        AnimeGoOptions options,
        SourceProfileStore profiles,
        MikanRssRuleStore rules,
        AnimeGo.Plugin.Abstractions.PluginCatalog plugins,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.GetAsync(sourceProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return TypedResults.NotFound(Error("source_profile_not_found", "Source profile was not found."));
        }

        var host = profile.AllowedTorrentHosts.Count > 0
            ? profile.AllowedTorrentHosts[0]
            : "preview.invalid";
        if (host.StartsWith("*.", StringComparison.Ordinal))
        {
            host = host[2..];
        }
        var command = new IngestItemCommand(
            $"https://{host}/animegonet-route-preview.torrent",
            new IngestItemInfo(
                request.Title, null, request.SourceItemId, request.SourceWorkId,
                request.MikanUrl, null, request.MikanId, request.BangumiId,
                request.AniDbId, request.ImdbId));
        var validation = await IngestCommandNormalizer.NormalizeAsync(
            plugins,
            profile.Adapter,
            command,
            requireModernMetadata: true,
            cancellationToken).ConfigureAwait(false);
        var errors = validation.Errors.ToList();
        if (!profile.Enabled)
        {
            errors.Add("source profile is disabled");
        }
        var downloaderExists = options.Downloaders.TryGetValue(profile.DownloaderId, out var downloader);
        if (!downloaderExists || !downloader!.Enabled)
        {
            errors.Add("bound downloader is missing or disabled");
        }
        var ruleRevision = (await rules.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false))?.Revision;
        return TypedResults.Ok(new SourceRoutePreviewResponse(
            errors.Count == 0,
            errors,
            profile.Id,
            profile.Revision,
            profile.Adapter,
            profile.DownloaderId,
            downloaderExists && downloader!.Enabled,
            downloaderExists ? downloader!.DownloadPath : null,
            options.Paths.SavePath,
            profile.FileStrategy,
            profile.Category,
            profile.Tags,
            profile.SeedingTimeMinutes,
            profile.RssFilterEnabled,
            profile.RssPriorityEnabled,
            ruleRevision));
    }

    private static async Task<IResult> GetRssRules(
        string sourceProfileId,
        SourceProfileStore profiles,
        MikanRssRuleStore rules,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.GetEnabledAsync(sourceProfileId.Trim().ToLowerInvariant(), cancellationToken)
            .ConfigureAwait(false);
        var snapshot = await rules.GetAsync(sourceProfileId, cancellationToken).ConfigureAwait(false);
        return profile is null || snapshot is null
            ? TypedResults.NotFound(Error("rss_rule_set_not_found", "RSS rule set was not found."))
            : TypedResults.Ok(ToResponse(profile, snapshot));
    }

    private static async Task<IResult> PutRssRules(
        string sourceProfileId,
        RssRuleSetRequest request,
        SourceProfileStore profiles,
        MikanRssRuleStore rules,
        CancellationToken cancellationToken)
    {
        try
        {
            var profileId = sourceProfileId.Trim().ToLowerInvariant();
            var profile = await profiles.GetEnabledAsync(profileId, cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                return TypedResults.NotFound(Error("rss_rule_set_not_found", "RSS source profile was not found."));
            }

            var saved = await rules.SaveAsync(
                profileId, ToRuleSet(request), request.ExpectedRevision,
                DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ToResponse(profile, saved));
        }
        catch (MikanRssRuleRevisionException)
        {
            return TypedResults.Conflict(Error(
                "rss_rule_revision_conflict", "RSS rules changed; reload before saving."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("rss_rule_set_invalid", exception.Message));
        }
    }

    private static async Task<IResult> PreviewRssRules(
        string sourceProfileId,
        RssRulePreviewRequest request,
        SourceProfileStore profiles,
        MikanRssRuleStore rules,
        CancellationToken cancellationToken)
    {
        try
        {
            var profileId = sourceProfileId.Trim().ToLowerInvariant();
            var profile = await profiles.GetEnabledAsync(profileId, cancellationToken).ConfigureAwait(false);
            var snapshot = await rules.GetAsync(profileId, cancellationToken).ConfigureAwait(false);
            if (profile is null || snapshot is null)
            {
                return TypedResults.NotFound(Error("rss_rule_set_not_found", "RSS rule set was not found."));
            }

            var candidates = (request.Candidates ?? []).Select((candidate, index) =>
            {
                if (candidate is null
                    || string.IsNullOrWhiteSpace(candidate.Id)
                    || string.IsNullOrWhiteSpace(candidate.Title))
                {
                    throw new ArgumentException($"Candidate {index} requires id and title.");
                }

                return new MikanRssCandidate(
                    candidate.Id.Trim(), candidate.Title, candidate.MikanId,
                    candidate.SourceEpisodeKind, candidate.SourceEpisode);
            }).ToArray();
            if (candidates.Select(candidate => candidate.Id).Distinct(StringComparer.Ordinal).Count() != candidates.Length)
            {
                throw new ArgumentException("Candidate ids must be unique.");
            }

            var decisions = profile.RssPriorityEnabled
                ? MikanRssRuleEngine.Evaluate(candidates, snapshot.Rules)
                : candidates.Select(candidate => new MikanRssDecision(
                    candidate.Id, MikanRssDecisionKind.Winner, "SkippedByConfiguration",
                    candidate.Id, [])).ToArray();
            return TypedResults.Ok(new RssRulePreviewResponse(
                profile.Id, snapshot.Revision, profile.RssPriorityEnabled,
                decisions.Select(decision => new RssRuleDecisionResponse(
                    decision.CandidateId, ToApiValue(decision.Kind), decision.Reason,
                    decision.WinnerId, decision.EvaluatedPriorityGroups)).ToArray()));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("rss_rule_preview_invalid", exception.Message));
        }
    }

    private static async Task<IResult> DeletePreview(
        string taskId,
        DeletePlanStore plans,
        CancellationToken cancellationToken)
    {
        var preview = await plans.GetPreviewAsync(taskId, cancellationToken).ConfigureAwait(false);
        return preview is null
            ? TypedResults.NotFound(Error("delete_task_not_found", "Delete task was not found."))
            : TypedResults.Ok(new DeletePreviewResponse(
                preview.TaskId, preview.TaskTitle, preview.TaskStatus, preview.Fingerprint,
                preview.BusinessRecords.Select(ToResponse).ToArray(),
                preview.DownloaderTasks.Select(ToResponse).ToArray(),
                preview.SourceFiles.Select(ToResponse).ToArray(),
                preview.MediaFiles.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> CreateDeleteExecution(
        string taskId,
        CreateDeleteExecutionRequest request,
        DeletePlanStore plans,
        CancellationToken cancellationToken)
    {
        try
        {
            var plan = await plans.CreateAsync(
                taskId,
                request.Fingerprint ?? string.Empty,
                new DeleteSelection(
                    request.DeleteBusinessRecord,
                    request.DeleteDownloaderTask,
                    request.DeleteSourceFiles,
                    request.DeleteMediaFiles),
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Accepted(
                $"/api/v1/delete/executions/{plan.ExecutionId}",
                new CreateDeleteExecutionResponse(
                    plan.ExecutionId, plan.TaskId, plan.State, plan.Targets.Count));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound(Error("delete_task_not_found", "Delete task was not found."));
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Conflict(Error("delete_preview_stale", exception.Message));
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            return TypedResults.Conflict(Error(
                "delete_execution_active", "This task already has an active delete execution."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("delete_request_invalid", exception.Message));
        }
    }

    private static async Task<IResult> DeleteExecutionStatus(
        string executionId,
        DeleteExecutionStore executions,
        CancellationToken cancellationToken)
    {
        var execution = await executions.GetAsync(executionId, cancellationToken).ConfigureAwait(false);
        return execution is null
            ? TypedResults.NotFound(Error("delete_execution_not_found", "Delete execution was not found."))
            : TypedResults.Ok(new DeleteExecutionStatusResponse(
                execution.ExecutionId, execution.TaskId, execution.State, execution.FailureReason,
                execution.AttemptCount, execution.CreatedAtUtc, execution.CompletedAtUtc,
                execution.Items.Select(item => new DeleteTargetResponse(
                    item.ItemKind, item.TargetKey, item.RootPath, item.DownloaderId,
                    item.DisplayValue, item.State)).ToArray()));
    }

    private static async Task<IResult> GetMikanWorkRule(
        int mikanId,
        MikanWorkMetadataRuleStore rules,
        CancellationToken cancellationToken)
    {
        if (mikanId <= 0)
        {
            return TypedResults.BadRequest(Error("mikanid_invalid", "mikanid must be a positive integer."));
        }

        var rule = await rules.GetAsync(mikanId, cancellationToken).ConfigureAwait(false);
        return rule is null
            ? TypedResults.NotFound(Error("mikan_rule_not_found", "Mikan work metadata rule was not found."))
            : TypedResults.Ok(ToResponse(rule));
    }

    private static async Task<IResult> PutMikanWorkRule(
        int mikanId,
        MikanWorkRuleRequest request,
        MikanWorkMetadataRuleStore rules,
        ITmdbClient tmdb,
        CancellationToken cancellationToken)
    {
        try
        {
            var validationError = await ValidateMikanWorkRuleSampleAsync(
                request,
                tmdb,
                cancellationToken).ConfigureAwait(false);
            if (validationError is not null)
            {
                return validationError;
            }

            var saved = await rules.SaveAsync(
                new MikanWorkMetadataRuleUpdate(
                    mikanId,
                    request.BangumiSubjectId,
                    request.TmdbSeriesId,
                    request.TmdbSeasonNumber,
                    request.EpisodeOffset,
                    request.Enabled),
                request.ExpectedRevision,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ToResponse(saved));
        }
        catch (MikanWorkMetadataRuleRevisionException)
        {
            return TypedResults.Conflict(Error(
                "mikan_rule_revision_conflict",
                "Mikan work metadata rule changed; reload it before saving."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("mikan_rule_invalid", exception.Message));
        }
    }

    private static async Task<IResult?> ValidateMikanWorkRuleSampleAsync(
        MikanWorkRuleRequest request,
        ITmdbClient tmdb,
        CancellationToken cancellationToken)
    {
        if (request.SampleSourceEpisode is null)
        {
            return null;
        }

        if (request.SampleSourceEpisode <= 0
            || request.TmdbSeriesId is null or <= 0
            || request.TmdbSeasonNumber is null or <= 0
            || request.EpisodeOffset is null)
        {
            return TypedResults.BadRequest(Error(
                "mikan_rule_sample_invalid",
                "Sample source Episode requires positive TMDB Series/Season and an Episode offset."));
        }

        int targetEpisode;
        try
        {
            targetEpisode = checked(request.SampleSourceEpisode.Value + request.EpisodeOffset.Value);
        }
        catch (OverflowException)
        {
            targetEpisode = 0;
        }

        if (targetEpisode <= 0)
        {
            return TypedResults.BadRequest(Error(
                "mikan_rule_sample_target_invalid",
                "Sample source Episode plus offset must produce a positive TMDB Episode."));
        }

        try
        {
            var series = await tmdb.GetSeriesAsync(
                request.TmdbSeriesId.Value,
                cancellationToken).ConfigureAwait(false);
            if (series?.Id != request.TmdbSeriesId.Value)
            {
                return TypedResults.BadRequest(Error(
                    "mikan_rule_tmdb_series_not_found",
                    "TMDB TV Series could not be verified."));
            }

            var season = await tmdb.GetSeasonAsync(
                series.Id,
                request.TmdbSeasonNumber.Value,
                cancellationToken).ConfigureAwait(false);
            if (season?.SeriesId != series.Id
                || season.SeasonNumber != request.TmdbSeasonNumber.Value)
            {
                return TypedResults.BadRequest(Error(
                    "mikan_rule_tmdb_season_not_found",
                    "TMDB Season could not be verified."));
            }

            var episode = await tmdb.GetEpisodeAsync(
                series.Id,
                season.SeasonNumber,
                targetEpisode,
                cancellationToken).ConfigureAwait(false);
            if (episode?.SeriesId != series.Id
                || episode.SeasonNumber != season.SeasonNumber
                || episode.EpisodeNumber != targetEpisode)
            {
                return TypedResults.BadRequest(Error(
                    "mikan_rule_tmdb_episode_not_found",
                    "The sample Episode mapping could not be verified by TMDB."));
            }
        }
        catch (TmdbClientException exception)
        {
            var status = exception.Kind is MetadataFailureKind.Network or MetadataFailureKind.RemoteService
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status502BadGateway;
            return TypedResults.Json(
                Error(exception.SafeCode, "TMDB sample validation failed."),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: status);
        }

        return null;
    }

    private static async Task<IResult> DeleteMikanWorkRule(
        int mikanId,
        [FromQuery(Name = "expected_revision")] long expectedRevision,
        MikanWorkMetadataRuleStore rules,
        CancellationToken cancellationToken)
    {
        try
        {
            await rules.DeleteAsync(mikanId, expectedRevision, cancellationToken).ConfigureAwait(false);
            return TypedResults.NoContent();
        }
        catch (MikanWorkMetadataRuleRevisionException)
        {
            return TypedResults.Conflict(Error(
                "mikan_rule_revision_conflict",
                "Mikan work metadata rule changed; reload it before deleting."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("mikan_rule_invalid", exception.Message));
        }
    }

    private static async Task<IResult> GetMikanWorkImpact(
        int mikanId,
        [FromQuery] int? limit,
        MetadataResolutionStore resolutions,
        CancellationToken cancellationToken)
    {
        if (mikanId <= 0)
        {
            return TypedResults.BadRequest(Error("mikanid_invalid", "mikanid must be a positive integer."));
        }

        var resolvedLimit = limit ?? 100;
        if (resolvedLimit is < 1 or > 500)
        {
            return TypedResults.BadRequest(Error(
                "mikan_impact_limit_invalid",
                "Impact task limit must be between 1 and 500."));
        }

        var impact = await resolutions
            .GetMikanWorkImpactAsync(mikanId, resolvedLimit, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(new MikanWorkImpactResponse(
            impact.MikanId,
            impact.TotalTaskCount,
            impact.FutureTaskCount,
            impact.RetryableFailedTaskCount,
            impact.ActiveTaskCount,
            impact.ResolvedProtectedTaskCount,
            impact.CompletedProtectedTaskCount,
            impact.OtherTaskCount,
            impact.IsTruncated,
            impact.Tasks.Select(task => new MikanWorkImpactTaskResponse(
                task.TaskId,
                task.Title,
                task.SourceId,
                task.Status,
                task.BangumiSubjectId,
                task.TmdbSeriesId,
                task.TmdbSeasonNumber,
                task.OrganizationState,
                ToApiValue(task.Category),
                task.UpdatedAtUtc)).ToArray()));
    }

    private static async Task<IResult> RematchMikanWorkTasks(
        int mikanId,
        MikanWorkRematchRequest request,
        MetadataResolutionStore resolutions,
        CancellationToken cancellationToken)
    {
        if (mikanId <= 0)
        {
            return TypedResults.BadRequest(Error("mikanid_invalid", "mikanid must be a positive integer."));
        }

        if (request.ExpectedRuleRevision < 0)
        {
            return TypedResults.BadRequest(Error(
                "mikan_rule_revision_invalid",
                "expected_rule_revision cannot be negative."));
        }

        try
        {
            var retried = await resolutions.RematchFailedMikanTasksAsync(
                mikanId,
                request.ExpectedRuleRevision,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new MikanWorkRematchResponse(
                mikanId,
                request.ExpectedRuleRevision,
                retried));
        }
        catch (MikanWorkRuleRematchRevisionException)
        {
            return TypedResults.Conflict(Error(
                "mikan_rule_revision_conflict",
                "Mikan work metadata rule changed; reload impact before rematching."));
        }
    }

    private static async Task<IResult> ListMikanTrustedOffsets(
        [FromQuery(Name = "mikanid")] int? mikanId,
        [FromQuery(Name = "groupid")] int? groupId,
        MikanTrustedOffsetStore offsets,
        CancellationToken cancellationToken)
    {
        try
        {
            var values = await offsets.ListAsync(mikanId, groupId, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new MikanTrustedOffsetListResponse(
                values.Select(value => new MikanTrustedOffsetItemResponse(
                    value.MikanId,
                    value.GroupId,
                    value.TmdbSeriesId,
                    value.TmdbSeasonNumber,
                    value.EpisodeOffset,
                    value.DistinctEpisodeCount,
                    MikanTrustedOffsetStore.RequiredDistinctEpisodes,
                    value.State,
                    value.UpdatedAtUtc)).ToArray()));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return TypedResults.BadRequest(Error("mikan_offset_key_invalid", exception.Message));
        }
    }

    private static async Task<IResult> ClearMikanTrustedOffset(
        int mikanId,
        int groupId,
        MikanTrustedOffsetStore offsets,
        CancellationToken cancellationToken)
    {
        try
        {
            return await offsets.ClearAsync(mikanId, groupId, cancellationToken).ConfigureAwait(false)
                ? TypedResults.NoContent()
                : TypedResults.NotFound(Error(
                    "mikan_offset_not_found",
                    "Mikan trusted offset evidence was not found."));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return TypedResults.BadRequest(Error("mikan_offset_key_invalid", exception.Message));
        }
    }

    private static async Task<IResult> GetLegacyMikanFilter(
        LegacyMikanFilterStore store,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.GetAsync("mikan", cancellationToken).ConfigureAwait(false);
        return snapshot is null
            ? TypedResults.NotFound(Error(
                "mikan_legacy_filter_not_found",
                "The default Mikan legacy filter was not initialized."))
            : TypedResults.Ok(await ToResponseAsync(store, snapshot, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> PutLegacyMikanFilter(
        LegacyMikanFilterWriteRequest request,
        LegacyMikanFilterStore store,
        CancellationToken cancellationToken)
    {
        try
        {
            var config = ToLegacyMikanFilterConfig(request.Rules);
            var saved = await store.SaveAsync(
                "mikan",
                config,
                request.ExpectedRevision,
                "web",
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(
                await ToResponseAsync(store, saved, cancellationToken).ConfigureAwait(false));
        }
        catch (LegacyMikanFilterRevisionException)
        {
            return LegacyMikanFilterRevisionConflict();
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("mikan_legacy_filter_invalid", exception.Message));
        }
    }

    private static async Task<IResult> ImportLegacyMikanFilter(
        LegacyMikanFilterImportRequest request,
        LegacyMikanFilterStore store,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.LegacyJson is null)
            {
                throw new ArgumentException("legacy_json is required.");
            }

            var json = Encoding.UTF8.GetBytes(request.LegacyJson);
            if (json.Length is 0 or > 1_048_576)
            {
                throw new ArgumentException("legacy_json must be between 1 byte and 1 MiB.");
            }

            var config = LegacyMikanFilterCodec.Parse(json);
            ValidateLegacyMikanFilterConfig(config);
            var saved = await store.SaveAsync(
                "mikan",
                config,
                request.ExpectedRevision,
                "web",
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(
                await ToResponseAsync(store, saved, cancellationToken).ConfigureAwait(false));
        }
        catch (LegacyMikanFilterRevisionException)
        {
            return LegacyMikanFilterRevisionConflict();
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or JsonException)
        {
            return TypedResults.BadRequest(Error(
                "mikan_legacy_filter_import_invalid",
                exception.Message));
        }
    }

    private static async Task<IResult> RollbackLegacyMikanFilter(
        LegacyMikanFilterRollbackRequest request,
        LegacyMikanFilterStore store,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.TargetRevision < 1)
            {
                throw new ArgumentException("target_revision must be a positive integer.");
            }

            var saved = await store.RollbackAsync(
                "mikan",
                request.TargetRevision,
                request.ExpectedRevision,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(
                await ToResponseAsync(store, saved, cancellationToken).ConfigureAwait(false));
        }
        catch (LegacyMikanFilterRevisionException)
        {
            return LegacyMikanFilterRevisionConflict();
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound(Error(
                "mikan_legacy_filter_snapshot_not_found",
                "The requested legacy filter snapshot was not found."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error(
                "mikan_legacy_filter_rollback_invalid",
                exception.Message));
        }
    }

    private static IResult PreviewLegacyMikanFilter(LegacyMikanFilterPreviewRequest request)
    {
        try
        {
            var title = request.Title;
            if (string.IsNullOrWhiteSpace(title) || title.Length > 1_000)
            {
                throw new ArgumentException("title is required and cannot exceed 1000 characters.");
            }
            if (request.MikanId is <= 0)
            {
                throw new ArgumentException("mikanid must be a positive integer when supplied.");
            }
            if (request.GroupId is <= 0)
            {
                throw new ArgumentException("groupid must be a positive integer when supplied.");
            }

            var groupName = request.GroupName is null
                ? LegacyMikanFilterEngine.ParseGroupName(title)
                : request.GroupName;
            if (groupName.Length > 256)
            {
                throw new ArgumentException("group_name cannot exceed 256 characters.");
            }

            var config = ToLegacyMikanFilterConfig(request.Rules);
            var preview = LegacyMikanFilterEngine.Preview(
                new LegacyMikanFilterCandidate(
                    title,
                    request.MikanId,
                    request.GroupId,
                    groupName),
                config);
            return TypedResults.Ok(new LegacyMikanFilterPreviewResponse(
                preview.Result.Accepted,
                preview.Result.Reason,
                preview.Result.MatchedScope,
                preview.Result.MatchedKey,
                groupName,
                preview.Steps.Select(step => new LegacyMikanFilterTraceItem(
                    step.Tier,
                    step.Key,
                    step.Applicable,
                    step.Accepted,
                    step.WhitelistMatches,
                    step.BlacklistMatches,
                    step.Reason)).ToArray()));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error(
                "mikan_legacy_filter_preview_invalid",
                exception.Message));
        }
    }

    private static async Task<IResult> RetryMetadataTask(
        string taskId,
        MetadataResolutionStore resolutions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return TypedResults.BadRequest(Error("metadata_task_id_invalid", "Metadata task ID is required."));
        }

        var result = await resolutions.RetryFailedAsync(
            taskId,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        if (result == MetadataRetryResult.Retried)
        {
            var status = await resolutions.GetTaskStatusAsync(taskId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Retried metadata task disappeared.");
            return TypedResults.Ok(new MetadataRetryResponse(taskId, status));
        }

        return result switch
        {
            MetadataRetryResult.NotFound => TypedResults.NotFound(Error(
                "metadata_task_not_found",
                "Metadata task was not found.")),
            MetadataRetryResult.ActiveLease => TypedResults.Conflict(Error(
                "metadata_task_active",
                "Metadata task has an active resolution lease.")),
            _ => TypedResults.Conflict(Error(
                "metadata_task_not_failed",
                "Only failed metadata tasks can be retried.")),
        };
    }

    private static async Task<IResult> MetadataTasks(
        [FromQuery] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery(Name = "failure_stage")] string? failureStage,
        [FromQuery(Name = "error_code")] string? errorCode,
        [FromQuery] string? retryability,
        [FromQuery] string? handling,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        MetadataResolutionStore resolutions,
        CancellationToken cancellationToken)
    {
        var resolvedPage = page ?? 1;
        var resolvedPageSize = pageSize ?? 25;
        var resolvedSort = string.IsNullOrWhiteSpace(sort) ? "updated" : sort.Trim().ToLowerInvariant();
        var resolvedDirection = string.IsNullOrWhiteSpace(direction)
            ? "desc"
            : direction.Trim().ToLowerInvariant();
        var resolvedRetryability = string.IsNullOrWhiteSpace(retryability)
            ? "all"
            : retryability.Trim().ToLowerInvariant();
        var resolvedHandling = string.IsNullOrWhiteSpace(handling)
            ? "all"
            : handling.Trim().ToLowerInvariant();
        var resolvedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var resolvedStatus = NormalizeMetadataFilter(status);
        var resolvedFailureStage = NormalizeMetadataFilter(failureStage);
        var resolvedErrorCode = NormalizeMetadataFilter(errorCode);
        if (resolvedPage < 1
            || resolvedPageSize is < 1 or > 100
            || resolvedSearch is { Length: > 200 }
            || resolvedSearch?.Any(char.IsControl) == true
            || !IsMetadataFilterValid(resolvedStatus)
            || !IsMetadataFilterValid(resolvedFailureStage)
            || !IsMetadataFilterValid(resolvedErrorCode)
            || resolvedSort is not ("updated" or "title" or "status" or "failure")
            || resolvedDirection is not ("asc" or "desc")
            || resolvedRetryability is not ("all" or "retryable" or "non_retryable" or "unknown")
            || resolvedHandling is not ("all" or "explicit_retry" or "configuration"
                or "manual" or "skipped" or "fallback" or "active" or "resolved" or "other"))
        {
            return TypedResults.BadRequest(Error(
                "metadata_task_filter_invalid",
                "Metadata task filters, sorting or pagination are invalid."));
        }

        IEnumerable<MetadataTaskListProjection> filtered =
            await resolutions.ListTasksAsync(500, cancellationToken).ConfigureAwait(false);
        if (resolvedSearch is not null)
        {
            filtered = filtered.Where(item =>
                item.Title.Contains(resolvedSearch, StringComparison.OrdinalIgnoreCase)
                || item.TaskId.Contains(resolvedSearch, StringComparison.OrdinalIgnoreCase)
                || item.SourceId.Contains(resolvedSearch, StringComparison.OrdinalIgnoreCase)
                || (item.FailureCode?.Contains(resolvedSearch, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.FailureReason?.Contains(resolvedSearch, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (resolvedStatus is not null)
        {
            filtered = filtered.Where(item =>
                string.Equals(item.Status, resolvedStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (resolvedFailureStage is not null)
        {
            filtered = filtered.Where(item =>
                string.Equals(item.FailureStage, resolvedFailureStage, StringComparison.OrdinalIgnoreCase));
        }

        if (resolvedErrorCode is not null)
        {
            filtered = filtered.Where(item =>
                string.Equals(item.FailureCode, resolvedErrorCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.FailureKind, resolvedErrorCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.FailureReason, resolvedErrorCode, StringComparison.OrdinalIgnoreCase));
        }

        if (resolvedRetryability != "all")
        {
            filtered = filtered.Where(item => resolvedRetryability switch
            {
                "retryable" => item.FailureRetryable == true,
                "non_retryable" => item.FailureRetryable == false,
                _ => item.FailureRetryable is null,
            });
        }

        if (resolvedHandling != "all")
        {
            filtered = filtered.Where(item =>
                string.Equals(item.HandlingCategory, resolvedHandling, StringComparison.Ordinal));
        }

        var ordered = OrderMetadataTasks(filtered, resolvedSort, resolvedDirection);
        var materialized = ordered.ToArray();
        var pageItems = materialized
            .Skip(checked((resolvedPage - 1) * resolvedPageSize))
            .Take(resolvedPageSize)
            .Select(ToResponse)
            .ToArray();
        return TypedResults.Ok(new MetadataTaskListResponse(
            resolvedPage,
            resolvedPageSize,
            materialized.Length,
            resolvedSort,
            resolvedDirection,
            pageItems));
    }

    private static async Task<IResult> MetadataTaskAttempts(
        string taskId,
        [FromQuery] int? limit,
        MetadataResolutionStore resolutions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return TypedResults.BadRequest(Error(
                "metadata_task_id_invalid",
                "Metadata task ID is required."));
        }

        var resolvedLimit = limit ?? 200;
        if (resolvedLimit is < 1 or > 500)
        {
            return TypedResults.BadRequest(Error(
                "metadata_attempt_limit_invalid",
                "Metadata attempt limit must be between 1 and 500."));
        }

        if (await resolutions.GetTaskStatusAsync(taskId, cancellationToken).ConfigureAwait(false) is null)
        {
            return TypedResults.NotFound(Error(
                "metadata_task_not_found",
                "Metadata task was not found."));
        }

        var attempts = await resolutions
            .ListAttemptsAsync(taskId, resolvedLimit, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(new MetadataAttemptListResponse(
            taskId,
            attempts.Select(attempt => new MetadataAttemptItemResponse(
                attempt.AttemptId,
                attempt.RunId,
                attempt.RunAttemptNumber,
                attempt.RunStatus,
                attempt.Stage,
                attempt.Strategy,
                attempt.Priority,
                attempt.Result,
                attempt.ErrorCode,
                attempt.Reason,
                attempt.Retryable,
                attempt.AttemptNumber,
                attempt.DurationMilliseconds,
                attempt.CreatedAtUtc,
                attempt.RunStartedAtUtc,
                attempt.RunCompletedAtUtc)).ToArray()));
    }

    private static async Task<IResult> MetadataTaskDetail(
        string taskId,
        MetadataResolutionStore resolutions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return TypedResults.BadRequest(Error(
                "metadata_task_id_invalid",
                "Metadata task ID is required."));
        }

        var detail = await resolutions.GetTaskDetailAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return TypedResults.NotFound(Error(
                "metadata_task_not_found",
                "Metadata task was not found."));
        }

        var item = detail.Summary;
        var ai = detail.Ai;
        return TypedResults.Ok(new MetadataTaskDetailResponse(
            ToResponse(item),
            ai is null
                ? new MetadataTaskAiItem(
                    "not_attempted",
                    null,
                    null,
                    null,
                    "not_established",
                    null,
                    null)
                : new MetadataTaskAiItem(
                    ai.Result,
                    ai.Stage,
                    ai.ErrorCode,
                    ai.Reason,
                    ai.Result == "matched" ? "tmdb_verified" : "not_established",
                    ai.DurationMilliseconds,
                    ai.AttemptedAtUtc),
            detail.Files.Select(file => new MetadataTaskFileItem(
                file.RelativePath,
                file.SizeBytes,
                file.SourceEpisode,
                file.FileEpisodeCandidate,
                file.Disposition,
                file.OtherReason,
                file.TmdbSeriesId,
                file.TmdbSeriesName,
                file.TmdbSeasonNumber,
                file.TmdbSeasonName,
                file.TmdbEpisodeNumber,
                file.TmdbEpisodeName)).ToArray()));
    }

    private static async Task<IResult> LibrarySeasons(
        [FromQuery] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        AnimeLibraryStore library,
        CancellationToken cancellationToken)
    {
        var resolvedPage = page ?? 1;
        if (resolvedPage < 1)
        {
            return TypedResults.BadRequest(Error(
                "library_page_invalid",
                "Library page must be a positive integer."));
        }

        var resolvedPageSize = pageSize ?? 24;
        if (resolvedPageSize is < 1 or > 100)
        {
            return TypedResults.BadRequest(Error(
                "library_page_size_invalid",
                "Library page size must be between 1 and 100."));
        }

        if (!TryParseLibrarySort(sort, out var resolvedSort))
        {
            return TypedResults.BadRequest(Error(
                "library_sort_invalid",
                "Library sort must be last_updated, name, air_date or added_at."));
        }

        if (!TryParseLibraryDirection(direction, out var resolvedDirection))
        {
            return TypedResults.BadRequest(Error(
                "library_direction_invalid",
                "Library direction must be asc or desc."));
        }

        var result = await library.ListSeasonsAsync(
            new AnimeSeasonListQuery(
                resolvedPage,
                resolvedPageSize,
                resolvedSort,
                resolvedDirection),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new AnimeSeasonListResponse(
            result.Page,
            result.PageSize,
            result.TotalItems,
            LibrarySortName(resolvedSort),
            resolvedDirection == AnimeLibrarySortDirection.Ascending ? "asc" : "desc",
            result.Items.Select(item =>
            {
                var posterPath = item.SeasonPosterPath ?? item.SeriesPosterPath;
                var posterSource = item.SeasonPosterPath is not null
                    ? "season"
                    : item.SeriesPosterPath is not null
                        ? "series"
                        : "placeholder";
                return new AnimeSeasonListItemResponse(
                    $"tmdb:{item.TmdbSeriesId}:s{item.TmdbSeasonNumber}",
                    item.TmdbSeriesId,
                    item.TmdbSeasonNumber,
                    item.DisplayName,
                    item.SortName,
                    item.SeasonName,
                    posterPath,
                    posterSource,
                    LibraryCoverUrl(item.TmdbSeriesId, item.TmdbSeasonNumber),
                    item.AirDate,
                    item.AddedAt,
                    item.LastUpdatedAt,
                    item.EpisodeTotal,
                    item.EpisodeSnapshotCount,
                    item.EpisodeDownloaded,
                    item.SeriesResolutionSource,
                    item.SeasonResolutionSource,
                    item.ValidationStatus,
                    item.LastResolutionRunId,
                    item.Warnings);
            }).ToArray()));
    }

    private static async Task<IResult> LibrarySeasonDetail(
        int tmdbSeriesId,
        int seasonNumber,
        AnimeLibraryStore library,
        CancellationToken cancellationToken)
    {
        if (tmdbSeriesId <= 0)
        {
            return TypedResults.BadRequest(Error(
                "library_series_id_invalid",
                "TMDB Series ID must be a positive integer."));
        }

        if (seasonNumber <= 0)
        {
            return TypedResults.BadRequest(Error(
                "library_season_number_invalid",
                "TMDB Season number must be a positive integer."));
        }

        var detail = await library.GetSeasonAsync(
            tmdbSeriesId,
            seasonNumber,
            cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return TypedResults.NotFound(Error(
                "library_season_not_found",
                "The requested TMDB season was not found in the local library."));
        }

        var season = detail.Season;
        var posterPath = season.SeasonPosterPath ?? season.SeriesPosterPath;
        var posterSource = season.SeasonPosterPath is not null
            ? "season"
            : season.SeriesPosterPath is not null
                ? "series"
                : "placeholder";
        return TypedResults.Ok(new AnimeSeasonDetailResponse(
            $"tmdb:{season.TmdbSeriesId}:s{season.TmdbSeasonNumber}",
            season.TmdbSeriesId,
            season.TmdbSeasonNumber,
            season.DisplayName,
            season.SeasonName,
            posterPath,
            posterSource,
            LibraryCoverUrl(season.TmdbSeriesId, season.TmdbSeasonNumber),
            season.AirDate,
            season.AddedAt,
            season.LastUpdatedAt,
            season.EpisodeTotal,
            season.EpisodeSnapshotCount,
            season.EpisodeDownloaded,
            season.SeriesResolutionSource,
            season.SeasonResolutionSource,
            season.ValidationStatus,
            season.LastResolutionRunId,
            season.Warnings,
            detail.Episodes.Select(episode => new AnimeEpisodeItemResponse(
                $"tmdb-episode:{episode.TmdbEpisodeId}",
                episode.TmdbEpisodeId,
                episode.EpisodeNumber,
                episode.Name,
                episode.AirDate,
                episode.RuntimeMinutes,
                episode.FetchedAtUtc,
                episode.Downloaded ? "downloaded" : "not_downloaded",
                episode.DownloadSourceId,
                episode.DownloadedAtUtc,
                episode.MediaPathKnown)).ToArray()));
    }

    private static async Task<IResult> LibraryCover(
        int tmdbSeriesId,
        int seasonNumber,
        AnimeCoverService covers,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (tmdbSeriesId <= 0)
        {
            return TypedResults.BadRequest(Error(
                "library_series_id_invalid",
                "TMDB Series ID must be a positive integer."));
        }

        if (seasonNumber <= 0)
        {
            return TypedResults.BadRequest(Error(
                "library_season_number_invalid",
                "TMDB Season number must be a positive integer."));
        }

        var cover = await covers
            .GetAsync(tmdbSeriesId, seasonNumber, cancellationToken)
            .ConfigureAwait(false);
        if (cover is null)
        {
            return TypedResults.NotFound(Error(
                "library_season_not_found",
                "The requested TMDB season was not found in the local library."));
        }

        context.Response.Headers["X-AnimeGoNet-Cover-Source"] = cover.Source;
        context.Response.Headers["X-AnimeGoNet-Cover-Cache"] =
            cover.CacheHit ? "hit" : "miss";
        if (cover.WarningCode is not null)
        {
            context.Response.Headers["X-AnimeGoNet-Cover-Warning"] = cover.WarningCode;
        }
        context.Response.Headers.CacheControl = cover.Source == "placeholder"
            ? "public, max-age=60"
            : "public, max-age=86400";
        return Results.Bytes(cover.Content, cover.ContentType);
    }

    private static string LibraryCoverUrl(int tmdbSeriesId, int seasonNumber) =>
        $"/api/v1/library/covers/{tmdbSeriesId}/{seasonNumber}";

    private static bool TryParseLibrarySort(string? value, out AnimeLibrarySort sort)
    {
        sort = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "last_updated" => AnimeLibrarySort.LastUpdated,
            "name" => AnimeLibrarySort.Name,
            "air_date" => AnimeLibrarySort.AirDate,
            "added_at" => AnimeLibrarySort.AddedAt,
            _ => 0,
        };
        return sort != 0;
    }

    private static bool TryParseLibraryDirection(
        string? value,
        out AnimeLibrarySortDirection direction)
    {
        direction = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "desc" => AnimeLibrarySortDirection.Descending,
            "asc" => AnimeLibrarySortDirection.Ascending,
            _ => 0,
        };
        return direction != 0;
    }

    private static string LibrarySortName(AnimeLibrarySort sort) =>
        sort switch
        {
            AnimeLibrarySort.LastUpdated => "last_updated",
            AnimeLibrarySort.Name => "name",
            AnimeLibrarySort.AirDate => "air_date",
            AnimeLibrarySort.AddedAt => "added_at",
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };

    private static async Task<Ok<PendingTmdbListResponse>> PendingTmdbSeries(
        PendingTmdbStore pending,
        CancellationToken cancellationToken)
    {
        var items = await pending.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new PendingTmdbListResponse(items.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> PendingTmdbDetail(
        int bangumiSubjectId,
        PendingTmdbStore pending,
        CancellationToken cancellationToken)
    {
        if (bangumiSubjectId <= 0)
        {
            return TypedResults.BadRequest(Error(
                "pending_tmdb_bgmid_invalid",
                "Bangumi Subject ID must be positive."));
        }

        var detail = await pending.GetAsync(bangumiSubjectId, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return TypedResults.NotFound(Error(
                "pending_tmdb_not_found",
                "Pending TMDB Series was not found."));
        }

        return TypedResults.Ok(new PendingTmdbDetailResponse(
            ToResponse(detail.Summary),
            detail.Tasks.Select(task => new PendingTmdbTaskItem(
                task.TaskId,
                task.Title,
                task.SourceId,
                task.Status,
                task.SeasonNumber,
                task.OtherFileCount,
                task.DuplicateFileCount,
                task.FailureKind,
                task.FailureReason,
                task.UpdatedAtUtc)).ToArray(),
            detail.Scopes.Select(scope => new PendingTmdbScopeItem(
                scope.Kind,
                scope.State,
                scope.SourceId,
                scope.SourceEpisode,
                ScopeBoundary(scope.Kind),
                scope.Kind != "bangumi_episode",
                scope.CompletedAtUtc)).ToArray(),
            detail.RecoveryCandidates.Select(candidate => new PendingTmdbRecoveryCandidateItem(
                candidate.FallbackCompletionId,
                candidate.SourceId,
                candidate.SourceEpisode,
                ScopeBoundary(candidate.ScopeKind),
                candidate.CompletedAtUtc)).ToArray()));
    }

    private static async Task<IResult> RecoverPendingTmdb(
        int bangumiSubjectId,
        PendingTmdbRecoveryRequest request,
        PendingTmdbRecoveryStore recovery,
        ITmdbClient tmdb,
        CancellationToken cancellationToken)
    {
        if (bangumiSubjectId <= 0)
        {
            return TypedResults.BadRequest(Error(
                "pending_tmdb_bgmid_invalid",
                "Bangumi Subject ID must be positive."));
        }

        var mappings = request.Mappings;
        if (request.TmdbSeriesId <= 0
            || mappings is null
            || mappings.Count == 0
            || mappings.Any(mapping =>
                string.IsNullOrWhiteSpace(mapping.FallbackRecordId)
                || mapping.TmdbSeasonNumber <= 0
                || mapping.TmdbEpisodeNumber <= 0)
            || mappings.Select(mapping => mapping.FallbackRecordId)
                .Distinct(StringComparer.Ordinal).Count() != mappings.Count)
        {
            return TypedResults.BadRequest(Error(
                "pending_tmdb_recovery_invalid",
                "Recovery requires a positive TMDB Series and unique positive Season/Episode mappings."));
        }

        try
        {
            var series = await tmdb.GetSeriesAsync(
                request.TmdbSeriesId,
                cancellationToken).ConfigureAwait(false);
            if (series?.Id != request.TmdbSeriesId)
            {
                return TypedResults.BadRequest(Error(
                    "pending_tmdb_series_not_found",
                    "TMDB TV Series could not be verified."));
            }

            var seasons = new Dictionary<int, TmdbSeason>();
            foreach (var seasonNumber in mappings.Select(value => value.TmdbSeasonNumber).Distinct())
            {
                var season = await tmdb.GetSeasonAsync(
                    series.Id,
                    seasonNumber,
                    cancellationToken).ConfigureAwait(false);
                if (season?.SeriesId != series.Id || season.SeasonNumber != seasonNumber)
                {
                    return TypedResults.BadRequest(Error(
                        "pending_tmdb_season_not_found",
                        $"TMDB Season {seasonNumber} could not be verified."));
                }

                seasons.Add(seasonNumber, season);
            }

            var episodes = new Dictionary<(int Season, int Episode), TmdbEpisode>();
            foreach (var identity in mappings
                         .Select(value => (value.TmdbSeasonNumber, value.TmdbEpisodeNumber))
                         .Distinct())
            {
                var episode = await tmdb.GetEpisodeAsync(
                    series.Id,
                    identity.TmdbSeasonNumber,
                    identity.TmdbEpisodeNumber,
                    cancellationToken).ConfigureAwait(false);
                if (episode?.SeriesId != series.Id
                    || episode.SeasonNumber != identity.TmdbSeasonNumber
                    || episode.EpisodeNumber != identity.TmdbEpisodeNumber)
                {
                    return TypedResults.BadRequest(Error(
                        "pending_tmdb_episode_not_found",
                        $"TMDB S{identity.TmdbSeasonNumber}E{identity.TmdbEpisodeNumber} could not be verified."));
                }

                episodes.Add(identity, episode);
            }

            var result = await recovery.RecoverAsync(
                new AnimeGoNet.Data.Metadata.PendingTmdbRecoveryRequest(
                    bangumiSubjectId,
                    series,
                    mappings.Select(mapping => new PendingTmdbRecoveryMapping(
                        mapping.FallbackRecordId!,
                        seasons[mapping.TmdbSeasonNumber],
                        episodes[(mapping.TmdbSeasonNumber, mapping.TmdbEpisodeNumber)])).ToArray(),
                    "manual"),
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new PendingTmdbRecoveryResponse(
                result.BangumiSubjectId,
                result.TmdbSeriesId,
                result.HasPendingFallbackRecords,
                result.Items.Select(item => new PendingTmdbRecoveryItemResponse(
                    item.FallbackCompletionId,
                    item.TmdbSeasonNumber,
                    item.TmdbEpisodeNumber,
                    item.State == "duplicate_after_resolution"
                        ? "DuplicateAfterResolution"
                        : "Resolved")).ToArray()));
        }
        catch (TmdbClientException exception)
        {
            var status = exception.Kind is MetadataFailureKind.Network or MetadataFailureKind.RemoteService
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status502BadGateway;
            return TypedResults.Json(
                Error(exception.SafeCode, "TMDB recovery validation failed."),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: status);
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.Conflict(Error(
                "pending_tmdb_recovery_stale",
                "Pending TMDB recovery data changed; reload the detail before retrying."));
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Conflict(Error(
                "pending_tmdb_recovery_conflict",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error(
                "pending_tmdb_recovery_invalid",
                exception.Message));
        }
    }

    private static PendingTmdbListItem ToResponse(PendingTmdbSeriesSummary item) =>
        new(
            item.BangumiSubjectId,
            item.CanonicalName,
            item.SeasonNumbers,
            item.TaskCount,
            item.ProcessedFileCount,
            item.CompletionRecordCount,
            item.ActiveClaimCount,
            item.CompletedClaimCount,
            item.DuplicateFileCount,
            item.LatestFailureKind,
            item.LatestFailureReason,
            item.UpdatedAtUtc);

    private static string ScopeBoundary(string kind) => kind switch
    {
        "bangumi_episode" => "Bangumi Episode",
        "mikan_episode" => "仅同一 mikanid",
        "source_work_episode" => "仅当前来源作品",
        _ => "仅相同 Torrent/文件",
    };

    private static async Task<Ok<IngestBatchResponse>> Ingest(
        IngestBatchRequest request,
        UnifiedIngestProcessor processor,
        CancellationToken cancellationToken)
    {
        var response = await ProcessIngestAsync(
            request,
            processor,
            requireModernMetadata: true,
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<MikanRssIngestResult>, BadRequest<ApiErrorResponse>>> RssIngest(
        RssIngestRequest request,
        AnimeGo.Plugin.Abstractions.PluginCatalog plugins,
        SourceProfileStore profiles,
        MikanRssIngestProcessor processor,
        CancellationToken cancellationToken)
    {
        var sourceProfileId = request.SourceProfileId?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(sourceProfileId))
        {
            return TypedResults.BadRequest(Error(
                "rss_source_profile_required",
                "source_profile_id is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return TypedResults.BadRequest(Error("rss_url_required", "url is required."));
        }

        var profile = await profiles.GetEnabledAsync(sourceProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return TypedResults.BadRequest(Error(
                "rss_source_profile_missing",
                "Enabled RSS source profile was not found."));
        }

        if (!string.Equals(profile.Adapter, "mikan", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.BadRequest(Error(
                "rss_source_profile_invalid",
                "RSS source profile must use the Mikan adapter."));
        }

        try
        {
            var feed = await FetchMikanFeedAsync(
                request.Url,
                plugins,
                cancellationToken).ConfigureAwait(false);
            var result = await processor
                .ProcessAsync(feed, profile.Id, cancellationToken)
                .ConfigureAwait(false);
            return TypedResults.Ok(result);
        }
        catch (RssFeedException exception)
        {
            return TypedResults.BadRequest(Error(
                exception.Code,
                "RSS processing failed."));
        }
    }

    private static async Task<Ok<LegacyApiResponse<MikanRssIngestResult?>>> LegacyRss(
        LegacyRssRequest request,
        AnimeGo.Plugin.Abstractions.PluginCatalog plugins,
        MikanRssIngestProcessor processor,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Source?.Trim(), "mikan", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(request.Rss?.Url))
        {
            return TypedResults.Ok(new LegacyApiResponse<MikanRssIngestResult?>(
                300, "source and rss.url are required", null));
        }

        try
        {
            var feed = await FetchMikanFeedAsync(
                request.Rss.Url,
                plugins,
                cancellationToken).ConfigureAwait(false);
            if (request.IsSelectEp)
            {
                var selected = new HashSet<string>(request.EpLinks ?? [], StringComparer.Ordinal);
                feed = feed with
                {
                    Items = feed.Items.Where(item => selected.Contains(item.MikanUrl)).ToArray(),
                };
            }

            var result = await processor.ProcessAsync(feed, "mikan", cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new LegacyApiResponse<MikanRssIngestResult?>(
                200, $"开始处理{feed.Items.Count}个下载项", result));
        }
        catch (RssFeedException exception)
        {
            return TypedResults.Ok(new LegacyApiResponse<MikanRssIngestResult?>(
                300, $"RSS processing failed: {exception.Code}", null));
        }
    }

    private static async Task<RssFeedDocument> FetchMikanFeedAsync(
        string url,
        AnimeGo.Plugin.Abstractions.PluginCatalog plugins,
        CancellationToken cancellationToken)
    {
        var fetched = await plugins
            .Require<AnimeGo.Plugin.Abstractions.IFeedPlugin>("mikan-rss")
            .FetchAsync(
                new AnimeGo.Plugin.Abstractions.FeedContext(
                    "mikan",
                    url,
                    EmptyPluginArguments),
                cancellationToken)
            .ConfigureAwait(false);
        var fetchError = fetched.Errors.Count > 0 ? fetched.Errors[0] : null;
        if (fetchError is not null)
        {
            throw new RssFeedException(fetchError.Code, fetchError.Message);
        }

        var mikanId = fetched.Metadata.TryGetValue("mikanid", out var mikanIdValue)
            && int.TryParse(
                mikanIdValue,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedMikanId)
            && parsedMikanId > 0
                ? parsedMikanId
                : (int?)null;
        return new RssFeedDocument(
            fetched.Items.Select(item => new RssFeedItem(
                item.Title,
                item.SourceUrl ?? string.Empty,
                item.TorrentUrl,
                item.ContentType ?? string.Empty,
                item.Length,
                item.PublishedAtRaw)).ToArray(),
            mikanId);
    }

    private static readonly Dictionary<string, string> EmptyPluginArguments =
        new(StringComparer.Ordinal);

    private static async Task<Ok<LegacyApiResponse<IngestBatchResponse?>>> LegacyDownloadManager(
        IngestBatchRequest request,
        UnifiedIngestProcessor processor,
        CancellationToken cancellationToken)
    {
        var legacyData = (request.Data ?? []).Select(item =>
        {
            if (item?.Info is null
                || !string.IsNullOrWhiteSpace(item.Info.Title)
                || !string.IsNullOrWhiteSpace(item.Info.Name))
            {
                return item;
            }

            return item with
            {
                Info = item.Info with { Title = item.Info.MikanUrl ?? item.Info.Url },
            };
        }).ToArray();
        var response = await ProcessIngestAsync(
            request with { Data = legacyData },
            processor,
            requireModernMetadata: false,
            cancellationToken).ConfigureAwait(false);
        var success = response.RejectedCount == 0;
        var message = success
            ? $"开始处理{response.AcceptedCount}个下载项"
            : string.Join("; ", response.Items.SelectMany(item => item.Errors));
        return TypedResults.Ok(new LegacyApiResponse<IngestBatchResponse?>(
            success ? 200 : 300,
            message,
            response));
    }

    private static async Task<IngestBatchResponse> ProcessIngestAsync(
        IngestBatchRequest request,
        UnifiedIngestProcessor processor,
        bool requireModernMetadata,
        CancellationToken cancellationToken)
    {
        var data = request.Data ?? [];
        var responses = new List<IngestItemResponse>(data.Count);
        for (var index = 0; index < data.Count; index++)
        {
            if (data[index]?.Info is null)
            {
                responses.Add(Rejected(index, ["info is required"]));
                continue;
            }

            var command = ToCommand(data[index]!);
            var result = await processor.ProcessAsync(
                request.Source ?? string.Empty,
                command,
                requireModernMetadata,
                cancellationToken).ConfigureAwait(false);
            responses.Add(new IngestItemResponse(
                index, result.Status, result.IngestId, result.SourceProfileId,
                result.SourceProfileRevision, result.DownloaderId, result.TorrentUrlFingerprint,
                result.InfoHash, result.FileCount, result.Errors));
        }

        var accepted = responses.Count(item => item.IngestId is not null);
        return new IngestBatchResponse(
            (request.Source ?? string.Empty).Trim().ToLowerInvariant(),
            accepted,
            responses.Count - accepted,
            responses);
    }

    private static IngestItemCommand ToCommand(IngestItemRequest request) =>
        new(
            request.Torrent,
            new IngestItemInfo(
                request.Info!.Title,
                request.Info.Name,
                request.Info.SourceItemId,
                request.Info.SourceWorkId,
                request.Info.MikanUrl,
                request.Info.Url,
                request.Info.MikanId,
                request.Info.BangumiId,
                request.Info.AniDbId,
                request.Info.ImdbId));

    private static IngestItemResponse Rejected(int index, IReadOnlyList<string> errors) =>
        new(index, "rejected", null, null, null, null, null, null, null, errors);

    private static async Task<LegacyMikanFilterResponse> ToResponseAsync(
        LegacyMikanFilterStore store,
        LegacyMikanFilterSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var snapshots = await store
            .ListSnapshotsAsync(snapshot.SourceProfileId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var rules = new List<LegacyMikanFilterRuleResponse>();
        AddLegacyMikanFilterTier(rules, 0, snapshot.Config.Filiter0);
        AddLegacyMikanFilterTier(rules, 1, snapshot.Config.Filiter1);
        AddLegacyMikanFilterTier(rules, 2, snapshot.Config.Filiter2);
        AddLegacyMikanFilterTier(rules, 3, snapshot.Config.Filiter3);
        AddLegacyMikanFilterTier(rules, 4, snapshot.Config.Filiter4);
        return new LegacyMikanFilterResponse(
            snapshot.SourceProfileId,
            snapshot.Revision,
            snapshot.UpdatedSource,
            snapshot.CreatedAtUtc,
            snapshot.UpdatedAtUtc,
            Encoding.UTF8.GetString(LegacyMikanFilterCodec.Encode(snapshot.Config)),
            rules,
            snapshots.Select(item => new LegacyMikanFilterSnapshotItem(
                item.Revision,
                item.UpdatedSource,
                item.CreatedAtUtc)).ToArray());
    }

    private static void AddLegacyMikanFilterTier(
        List<LegacyMikanFilterRuleResponse> target,
        int tier,
        IEnumerable<KeyValuePair<string, LegacyMikanFilterRule>> rules)
    {
        var position = 0;
        foreach (var pair in rules)
        {
            target.Add(new LegacyMikanFilterRuleResponse(
                tier,
                position++,
                pair.Key,
                pair.Value.IsEnableWhitelist,
                pair.Value.IsEnableBlacklist,
                pair.Value.Whitelist,
                pair.Value.Blacklist));
        }
    }

    private static LegacyMikanFilterConfig ToLegacyMikanFilterConfig(
        IReadOnlyList<LegacyMikanFilterRuleResponse>? rules)
    {
        if (rules is null)
        {
            throw new ArgumentException("rules is required.");
        }
        if (rules.Count > 1_000)
        {
            throw new ArgumentException("rules cannot contain more than 1000 entries.");
        }

        var tiers = new List<KeyValuePair<string, LegacyMikanFilterRule>>[5];
        for (var index = 0; index < tiers.Length; index++)
        {
            tiers[index] = [];
        }

        foreach (var group in rules.GroupBy(rule => rule.Tier))
        {
            if (group.Key is < 0 or > 4)
            {
                throw new ArgumentException("rule tier must be between 0 and 4.");
            }

            var positions = new HashSet<int>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in group.OrderBy(rule => rule.Position))
            {
                if (item.Position < 0 || !positions.Add(item.Position))
                {
                    throw new ArgumentException(
                        $"Filiter{group.Key} rule positions must be unique non-negative integers.");
                }
                ValidateLegacyMikanFilterKey(item.Key, group.Key);
                if (!keys.Add(item.Key))
                {
                    throw new ArgumentException(
                        $"Filiter{group.Key} rule keys must be unique with case-sensitive comparison.");
                }

                var whitelist = ValidateLegacyMikanFilterValues(
                    item.Whitelist, $"Filiter{group.Key}.{item.Key}.whitelist");
                var blacklist = ValidateLegacyMikanFilterValues(
                    item.Blacklist, $"Filiter{group.Key}.{item.Key}.blacklist");
                tiers[group.Key].Add(new KeyValuePair<string, LegacyMikanFilterRule>(
                    item.Key,
                    new LegacyMikanFilterRule(
                        item.WhitelistEnabled,
                        item.BlacklistEnabled,
                        whitelist,
                        blacklist)));
            }
        }

        return new LegacyMikanFilterConfig(
            tiers[0],
            ToLegacyMikanFilterDictionary(tiers[1]),
            ToLegacyMikanFilterDictionary(tiers[2]),
            ToLegacyMikanFilterDictionary(tiers[3]),
            ToLegacyMikanFilterDictionary(tiers[4]));
    }

    private static void ValidateLegacyMikanFilterConfig(LegacyMikanFilterConfig config)
    {
        var rules = new List<LegacyMikanFilterRuleResponse>();
        AddLegacyMikanFilterTier(rules, 0, config.Filiter0);
        AddLegacyMikanFilterTier(rules, 1, config.Filiter1);
        AddLegacyMikanFilterTier(rules, 2, config.Filiter2);
        AddLegacyMikanFilterTier(rules, 3, config.Filiter3);
        AddLegacyMikanFilterTier(rules, 4, config.Filiter4);
        _ = ToLegacyMikanFilterConfig(rules);
    }

    private static string[] ValidateLegacyMikanFilterValues(
        IReadOnlyList<string>? values,
        string path)
    {
        if (values is null)
        {
            throw new ArgumentException($"{path} is required.");
        }
        if (values.Count > 500)
        {
            throw new ArgumentException($"{path} cannot contain more than 500 values.");
        }

        var result = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index]
                ?? throw new ArgumentException($"{path}[{index}] must be a string.");
            if (value.Length > 4_096)
            {
                throw new ArgumentException(
                    $"{path}[{index}] cannot exceed 4096 characters.");
            }
            result[index] = value;
        }
        return result;
    }

    private static void ValidateLegacyMikanFilterKey(string? key, int tier)
    {
        if (key is null)
        {
            throw new ArgumentException($"Filiter{tier} rule key is required.");
        }
        if (key.Length > 1_024)
        {
            throw new ArgumentException(
                $"Filiter{tier} rule key cannot exceed 1024 characters.");
        }
    }

    private static Dictionary<string, LegacyMikanFilterRule> ToLegacyMikanFilterDictionary(
        IEnumerable<KeyValuePair<string, LegacyMikanFilterRule>> rules)
    {
        var result = new Dictionary<string, LegacyMikanFilterRule>(StringComparer.Ordinal);
        foreach (var pair in rules)
        {
            result.Add(pair.Key, pair.Value);
        }
        return result;
    }

    private static Conflict<ApiErrorResponse> LegacyMikanFilterRevisionConflict() =>
        TypedResults.Conflict(Error(
            "mikan_legacy_filter_revision_conflict",
            "The Mikan legacy filter changed; reload it before saving."));

    private static MikanWorkRuleResponse ToResponse(MikanWorkMetadataRule rule) =>
        new(
            rule.MikanId,
            rule.BangumiSubjectId,
            rule.TmdbSeriesId,
            rule.TmdbSeasonNumber,
            rule.EpisodeOffset,
            rule.Enabled,
            rule.Revision,
            rule.CreatedAtUtc,
            rule.UpdatedAtUtc);

    private static SourceProfileResponse ToResponse(SourceProfileAdminRecord profile) =>
        new(
            profile.Id,
            profile.DisplayName,
            profile.Adapter,
            profile.DownloaderId,
            profile.FileStrategy,
            profile.AllowedTorrentHosts,
            profile.Category,
            profile.Tags,
            profile.SeedingTimeMinutes,
            profile.RssFilterEnabled,
            profile.RssPriorityEnabled,
            profile.Enabled,
            profile.Revision,
            profile.IngestTaskCount,
            profile.RssBatchCount,
            profile.Id == "mikan",
            profile.FileStrategy == "move"
                ? "move transfers completed files and does not preserve seeding."
                : null,
            profile.CreatedAtUtc,
            profile.UpdatedAtUtc);

    private static DownloaderInstanceResponse ToResponse(
        string id,
        QbittorrentInstanceOptions downloader,
        DownloaderUsageRecord usage,
        string configurationSource,
        long? overrideRevision,
        bool restartRequired,
        DownloadClientCircuitSnapshot? circuit)
    {
        var safeUrl = new UriBuilder(downloader.BaseUrl)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        }.Uri.AbsoluteUri.TrimEnd('/');
        return new DownloaderInstanceResponse(
            id,
            downloader.Type,
            safeUrl,
            downloader.DownloadPath,
            downloader.Enabled,
            !string.IsNullOrWhiteSpace(downloader.Username) && !string.IsNullOrWhiteSpace(downloader.Password),
            configurationSource,
            overrideRevision,
            restartRequired,
            usage.SourceProfileCount,
            usage.IngestTaskCount,
            usage.DownloadJobCount,
            usage.Connected,
            usage.FailureCode,
            usage.LastSuccessAtUtc,
            usage.UpdatedAtUtc,
            circuit?.Status switch
            {
                DownloadClientCircuitStatus.Closed => "closed",
                DownloadClientCircuitStatus.Open => "open",
                DownloadClientCircuitStatus.HalfOpen => "half_open",
                _ => null,
            },
            circuit?.ConsecutiveFailures ?? 0,
            circuit?.RetryAtUtc);
    }

    private static QbittorrentInstanceOptions ToOptions(DownloaderOverrideEntry entry) => new()
    {
        Type = DownloaderTypes.Qbittorrent,
        BaseUrl = new Uri(entry.BaseUrl, UriKind.Absolute),
        Username = entry.Username,
        Password = entry.Password,
        DownloadPath = entry.DownloadPath,
        Enabled = entry.Enabled,
    };

    private static Uri ValidateDownloaderBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "base_url must be an absolute HTTP(S) URL without credentials, query or fragment.");
        }
        return new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" }.Uri;
    }

    private static SourceProfileDefinition ToDefinition(
        string? displayName,
        string? adapter,
        string? downloaderId,
        string? fileStrategy,
        IReadOnlyList<string?>? allowedTorrentHosts,
        string? category,
        IReadOnlyList<string?>? tags,
        int? seedingTimeMinutes,
        bool rssFilterEnabled,
        bool rssPriorityEnabled,
        bool enabled,
        SourceProfileAdminRecord? current,
        AnimeGoOptions options)
    {
        var name = displayName?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 128)
        {
            throw new ArgumentException("display_name must contain 1 to 128 characters.");
        }
        var normalizedAdapter = adapter?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedAdapter is not ("mikan" or "u2" or "ttg"))
        {
            throw new ArgumentException("adapter must be mikan, u2 or ttg.");
        }
        var normalizedDownloader = RequireCanonicalStableId(downloaderId, "downloader_id");
        if (!options.Downloaders.TryGetValue(normalizedDownloader, out var downloader)
            || !downloader.Enabled
            || downloader.Type != DownloaderTypes.Qbittorrent)
        {
            throw new ArgumentException(
                "downloader_id must reference an enabled configured qBittorrent instance.");
        }
        var normalizedStrategy = fileStrategy?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedStrategy is not ("link" or "link_delete" or "move" or "wait_move"))
        {
            throw new ArgumentException("file_strategy must be link, link_delete, move or wait_move.");
        }
        if (allowedTorrentHosts is null || allowedTorrentHosts.Count is < 1 or > 32)
        {
            throw new ArgumentException("allowed_torrent_hosts must contain 1 to 32 host patterns.");
        }
        var hosts = allowedTorrentHosts
            .Select(host => host?.Trim().ToLowerInvariant() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (hosts.Any(host => !AnimeGoOptionsValidator.IsValidTorrentHostPattern(host)))
        {
            throw new ArgumentException(
                "allowed_torrent_hosts contains an invalid DNS host or wildcard pattern.");
        }
        var normalizedCategory = SourceDownloadPolicy.NormalizeCategory(
            category ?? current?.Category ?? "animegonet");
        var normalizedTags = SourceDownloadPolicy.NormalizeTags(
            tags ?? current?.Tags.Select(value => (string?)value) ?? []);
        var normalizedSeedingTime = SourceDownloadPolicy.ValidateSeedingTimeMinutes(
            normalizedStrategy,
            seedingTimeMinutes
                ?? (current is not null && current.FileStrategy == normalizedStrategy
                    ? current.SeedingTimeMinutes
                    : 0));
        return new SourceProfileDefinition(
            name,
            normalizedAdapter,
            normalizedDownloader,
            normalizedStrategy,
            hosts,
            normalizedCategory,
            normalizedTags,
            normalizedSeedingTime,
            rssFilterEnabled,
            rssPriorityEnabled,
            enabled);
    }

    private static string RequireCanonicalStableId(string? value, string name)
    {
        var candidate = value ?? string.Empty;
        if (!AnimeGoOptionsValidator.IsStableId(candidate)
            || !candidate.Equals(candidate.Trim().ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{name} must already be lowercase and contain only letters, digits, '.', '_' or '-'.");
        }
        return candidate;
    }

    private static ApiErrorResponse Error(string code, string message) => new(code, message);

    private static MetadataTaskListItem ToResponse(MetadataTaskListProjection item) =>
        new(
            item.TaskId,
            item.Title,
            item.SourceId,
            item.Status,
            item.MikanId,
            item.BangumiSubjectId,
            item.TmdbSeriesId,
            item.TmdbSeasonNumber,
            item.SeriesStrategy,
            item.SeasonStrategy,
            item.EpisodeStrategy,
            item.FailureKind,
            item.FailureReason,
            item.FailureStage,
            item.FailureCode,
            item.FailureRetryable,
            item.HandlingCategory,
            item.EpisodeFileCount,
            item.OtherFileCount,
            item.DuplicateFileCount,
            item.PendingFileCount,
            item.UpdatedAtUtc);

    private static IOrderedEnumerable<MetadataTaskListProjection> OrderMetadataTasks(
        IEnumerable<MetadataTaskListProjection> items,
        string sort,
        string direction)
    {
        var descending = direction == "desc";
        IOrderedEnumerable<MetadataTaskListProjection> ordered = sort switch
        {
            "title" when descending => items.OrderByDescending(
                item => item.Title, StringComparer.OrdinalIgnoreCase),
            "title" => items.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
            "status" when descending => items.OrderByDescending(
                item => item.Status, StringComparer.Ordinal),
            "status" => items.OrderBy(item => item.Status, StringComparer.Ordinal),
            "failure" when descending => items.OrderByDescending(
                item => $"{item.HandlingCategory}\u001f{item.FailureCode}",
                StringComparer.OrdinalIgnoreCase),
            "failure" => items.OrderBy(
                item => $"{item.HandlingCategory}\u001f{item.FailureCode}",
                StringComparer.OrdinalIgnoreCase),
            _ when descending => items.OrderByDescending(item => item.UpdatedAtUtc),
            _ => items.OrderBy(item => item.UpdatedAtUtc),
        };
        return ordered.ThenBy(item => item.TaskId, StringComparer.Ordinal);
    }

    private static string? NormalizeMetadataFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static bool IsMetadataFilterValid(string? value) =>
        value is null
        || (value.Length <= 128
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '_' or '-' or '.'));

    private static DownloadListItem ToResponse(DownloadJobListItemRecord record) =>
        new(
            record.JobId,
            record.TaskId,
            record.Title,
            record.SourceId,
            record.DownloaderId,
            record.InfoHash,
            record.State,
            record.BusinessStatus,
            record.Progress,
            record.DownloadedBytes,
            record.TotalBytes,
            record.SpeedBytesPerSecond,
            record.EtaSeconds,
            record.Seeds,
            record.Peers,
            record.IsStale,
            record.Revision,
            record.SnapshotAtUtc,
            record.UpdatedAtUtc,
            record.DownloaderConnected,
            record.DownloaderFailureCode,
            record.DownloaderLastSuccessAtUtc);

    private static bool CanRetry(DownloadJobDetailRecord detail) =>
        detail.Summary.State == "error"
        || (detail.PreparationState == "pending" && detail.PreparationFailureCode is not null)
        || (detail.OrganizationState is "pending" or "cleanup"
            && detail.OrganizationFailureCode is not null);

    private static bool ControlStateAllowed(string kind, string state) =>
        kind switch
        {
            "pause" => state is "waiting" or "downloading" or "moving" or "seeding",
            "resume" => state == "paused",
            "retry_download" => state == "error",
            _ => false,
        };

    private static string? DownloadFailureCode(Exception exception) =>
        exception switch
        {
            DownloadClientCircuitOpenException => "downloader_circuit_open",
            KeyNotFoundException => "downloader_not_configured",
            HttpRequestException => "downloader_unavailable",
            TaskCanceledException => "downloader_timeout",
            IOException => "downloader_io_error",
            InvalidOperationException => "downloader_operation_failed",
            _ => null,
        };

    private static string? NormalizeEcho(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeDownloadFilePath(string value)
    {
        var normalized = value.Replace('\\', '/').Trim();
        if (Path.IsPathFullyQualified(normalized))
        {
            return Path.GetFileName(normalized);
        }

        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment is not "." and not "..");
        return string.Join('/', segments);
    }

    private static DeleteTargetResponse ToResponse(DeletePlanTarget target) =>
        new(target.ItemKind, target.TargetKey, target.RootPath, target.DownloaderId, target.DisplayValue);

    private static MikanRssRuleSet ToRuleSet(RssRuleSetRequest request) => new(
        (request.Whitelist ?? []).Select((item, index) => ToArray(item, $"whitelist[{index}]")).ToArray(),
        (request.Blacklist ?? []).Select((item, index) => ToArray(item, $"blacklist[{index}]")).ToArray(),
        (request.PriorityGroups ?? []).Select((group, index) =>
        {
            if (group is null)
            {
                throw new ArgumentException($"priority_groups[{index}] is required.");
            }

            return new PriorityGroup(
                group.Id ?? string.Empty,
                group.Name ?? string.Empty,
                (group.Arrays ?? []).Select((item, arrayIndex) =>
                    ToArray(item, $"priority_groups[{index}].arrays[{arrayIndex}]")).ToArray());
        }).ToArray());

    private static NamedMatchArray ToArray(RssNamedArrayRequest? request, string path)
    {
        if (request is null)
        {
            throw new ArgumentException($"{path} is required.");
        }

        return new NamedMatchArray(
            request.Id ?? string.Empty,
            request.Name ?? string.Empty,
            request.Enabled,
            (request.Values ?? []).Select(value => value ?? string.Empty).ToArray());
    }

    private static RssRuleSetResponse ToResponse(
        SourceProfileRecord profile,
        MikanRssRuleSnapshot snapshot) =>
        new(
            snapshot.SourceProfileId, profile.RssFilterEnabled, profile.RssPriorityEnabled,
            snapshot.Revision,
            snapshot.Rules.Whitelist.Select(ToResponse).ToArray(),
            snapshot.Rules.Blacklist.Select(ToResponse).ToArray(),
            snapshot.Rules.PriorityGroups.Select(group => new RssPriorityGroupResponse(
                group.Id, group.Name, group.Arrays.Select(ToResponse).ToArray())).ToArray(),
            snapshot.CreatedAtUtc, snapshot.UpdatedAtUtc);

    private static RssNamedArrayResponse ToResponse(NamedMatchArray array) =>
        new(array.Id, array.Name, array.Enabled, array.Values);

    private static string ToApiValue(MikanRssDecisionKind value) => value switch
    {
        MikanRssDecisionKind.Winner => "winner",
        MikanRssDecisionKind.RejectedByBlacklist => "rejected_by_blacklist",
        MikanRssDecisionKind.RejectedByWhitelist => "rejected_by_whitelist",
        MikanRssDecisionKind.SuppressedByHigherPriority => "suppressed_by_higher_priority",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToApiValue(MikanWorkImpactCategory value) => value switch
    {
        MikanWorkImpactCategory.Future => "future",
        MikanWorkImpactCategory.RetryableFailed => "retryable_failed",
        MikanWorkImpactCategory.Active => "active",
        MikanWorkImpactCategory.ResolvedProtected => "resolved_protected",
        MikanWorkImpactCategory.CompletedProtected => "completed_protected",
        _ => "other",
    };
}
