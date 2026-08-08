using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Compatibility;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.DataUpdate;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Sources;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.App.Ingest;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.App.Library;
using AnimeGoNet.App.Plugins;
using AnimeGoNet.App.Scheduling;
using AnimeGoNet.App.Serialization;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Cache;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Deletion;
using AnimeGoNet.Data.DataUpdate;
using AnimeGoNet.Data.Feeds;
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
        app.MapGet("/api/v1/plugins", ExternalPluginConfigurations);
        app.MapPut(
            "/api/v1/plugins/{pluginId}/configuration",
            PutExternalPluginConfiguration);
        app.MapDelete(
            "/api/v1/plugins/{pluginId}/configuration",
            DeleteExternalPluginConfiguration);
        app.MapPost("/api/v1/plugins/{pluginId}/reset", ResetExternalPlugin);
        app.MapGet("/api/v1/config", Configuration);
        app.MapPost("/api/v1/config/preview", PreviewConfiguration);
        app.MapPut("/api/v1/config", PutConfiguration);
        app.MapDelete("/api/v1/config", DeleteConfigurationOverride);
        app.MapGet("/api/v1/cache/buckets", CacheBrowserBuckets);
        app.MapGet("/api/v1/cache/entries", CacheBrowserEntries);
        app.MapDelete("/api/v1/cache/entries/{entryId}", DeleteCacheBrowserEntry);
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
        app.MapPost("/api/v1/rss-rules/{sourceProfileId}/rollback", RollbackRssRules);
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
        app.MapPost("/api/v1/library/seasons", CreateLibrarySeason);
        app.MapGet("/api/v1/library/directory-database", DirectoryDatabaseStatus);
        app.MapPost("/api/v1/library/directory-database/refresh", RefreshDirectoryDatabase);
        app.MapGet("/api/v1/data-update", GetDataUpdateStatus);
        app.MapPost("/api/v1/data-update/check", CheckDataUpdate);
        app.MapPost("/api/v1/data-update/download", DownloadDataUpdate);
        app.MapPost("/api/v1/data-update/update", ApplyDataUpdate);
        app.MapPost(
            "/api/v1/data-update/downloads/{dataVersion}/import",
            ImportDownloadedDataUpdate);
        app.MapPost("/api/v1/data-update/offline/import", ImportOfflineDataUpdate);
        app.MapPost("/api/v1/data-update/rollback", RollbackDataUpdate);
        app.MapGet(
            "/api/v1/library/seasons/{tmdbSeriesId:int}/{seasonNumber:int}",
            LibrarySeasonDetail);
        app.MapPut(
            "/api/v1/library/seasons/{tmdbSeriesId:int}/{seasonNumber:int}",
            RefreshLibrarySeason);
        app.MapDelete(
            "/api/v1/library/seasons/{tmdbSeriesId:int}/{seasonNumber:int}",
            DeleteLibrarySeason);
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
        app.MapGet("/api/config", LegacyConfigurationGet);
        app.MapPut("/api/config", LegacyConfigurationPut);
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
        var hash = StableHash.Sha256LowerHex(accessKey);
        return TypedResults.Ok(new LegacyApiResponse<string>(200, "Access-Key", hash));
    }

    private static async Task<Ok<LegacyApiResponse<JsonElement>>> LegacyConfigurationGet(
        [FromQuery] string? key,
        LegacyDeploymentConfigurationFile configuration,
        CancellationToken cancellationToken)
    {
        var normalized = string.IsNullOrWhiteSpace(key)
            ? "raw"
            : key.Trim().ToLowerInvariant();
        try
        {
            var data = normalized switch
            {
                "all" => await configuration.ReadJsonAsync(
                    useDefaults: false,
                    cancellationToken).ConfigureAwait(false),
                "default" => await configuration.ReadJsonAsync(
                    useDefaults: true,
                    cancellationToken).ConfigureAwait(false),
                "comment" => LegacyConfigurationComments,
                "raw" => JsonSerializer.SerializeToElement(
                    Convert.ToBase64String(await configuration.ReadRawAsync(
                        useDefaults: false,
                        cancellationToken).ConfigureAwait(false)),
                    ApiJsonContext.Default.String),
                _ => LegacyJsonNull,
            };
            if (normalized is not ("all" or "default" or "comment" or "raw"))
            {
                return LegacyConfigurationFailure(
                    $"暂不支持 {normalized}，目前仅支持 'all', 'default', 'comment', 'raw'");
            }

            var message = normalized switch
            {
                "all" => "配置项值",
                "default" => "配置项默认值",
                "comment" => "配置项说明",
                _ => "配置文件",
            };
            return TypedResults.Ok(new LegacyApiResponse<JsonElement>(200, message, data));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is DeploymentYamlException
                or IOException
                or UnauthorizedAccessException)
        {
            return LegacyConfigurationFailure("打开配置文件失败");
        }
    }

    private static async Task<Ok<LegacyApiResponse<JsonElement>>> LegacyConfigurationPut(
        HttpContext context,
        [FromQuery] string? key,
        [FromQuery] bool? backup,
        LegacyDeploymentConfigurationFile configuration,
        CancellationToken cancellationToken)
    {
        LegacyConfigurationPutRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                ApiJsonContext.Default.LegacyConfigurationPutRequest,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return LegacyConfigurationFailure("参数错误");
        }
        if (request is null)
        {
            return LegacyConfigurationFailure("参数错误");
        }

        var normalized = string.IsNullOrWhiteSpace(key)
            ? string.IsNullOrWhiteSpace(request.Key) ? "raw" : request.Key.Trim().ToLowerInvariant()
            : key.Trim().ToLowerInvariant();
        var createBackup = backup ?? request.Backup ?? true;
        try
        {
            if (normalized == "raw")
            {
                if (request.ConfigRaw is null)
                {
                    return LegacyConfigurationFailure("参数错误，未传入对应数据");
                }
                byte[] decoded;
                try
                {
                    decoded = Convert.FromBase64String(request.ConfigRaw);
                }
                catch (FormatException)
                {
                    return LegacyConfigurationFailure("参数格式错误");
                }
                await configuration.WriteRawAsync(
                    decoded,
                    createBackup,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (normalized == "all")
            {
                if (request.Config.ValueKind != JsonValueKind.Object)
                {
                    return LegacyConfigurationFailure("参数错误，未传入对应数据");
                }
                await configuration.WriteJsonAsync(
                    request.Config,
                    createBackup,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return LegacyConfigurationFailure(
                    $"暂不支持 {normalized}，目前仅支持 'all', 'raw'");
            }

            return TypedResults.Ok(new LegacyApiResponse<JsonElement>(
                200,
                "更新成功，需要重启AnimeGo以应用配置",
                LegacyJsonNull));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is DeploymentYamlException
                or DecoderFallbackException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ArgumentException)
        {
            return LegacyConfigurationFailure("参数格式错误");
        }
    }

    private static Ok<LegacyApiResponse<JsonElement>> LegacyConfigurationFailure(string message) =>
        TypedResults.Ok(new LegacyApiResponse<JsonElement>(300, message, LegacyJsonNull));

    private static readonly JsonElement LegacyJsonNull =
        JsonDocument.Parse("null").RootElement.Clone();

    private static readonly JsonElement LegacyConfigurationComments = JsonDocument.Parse(
        """
        {
          "version": "配置文件版本；当前为 1.7.1",
          "paths": {
            "data_path": "AnimeGoNet 私有数据目录",
            "download_path": "下载器与主程序共享的下载目录",
            "save_path": "整理后的媒体库目录"
          },
          "web": { "access_key": "Web/API 访问密钥" },
          "downloaders": "按稳定 ID 配置 qBittorrent 实例",
          "sources": "按输入源绑定下载器、规则与文件策略",
          "metadata": "TMDB、Bangumi、AI 与季度失败链",
          "torrent_fetch": "Torrent URL 安全获取边界",
          "data_update": "Bangumi Archive 数据更新策略"
        }
        """).RootElement.Clone();

    private static async Task<Ok<DirectoryDatabaseStatusResponse>> DirectoryDatabaseStatus(
        DirectoryDatabaseIndexStore store,
        AnimeGoOptions options,
        CancellationToken cancellationToken)
    {
        var status = await store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(status, options.Schedule.RefreshDatabaseCron));
    }

    private static async Task<Ok<DirectoryDatabaseStatusResponse>> RefreshDirectoryDatabase(
        DirectoryDatabaseIndexStore store,
        AnimeGoOptions options,
        CancellationToken cancellationToken)
    {
        await store.RefreshAsync(
            options.Paths.SavePath,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        var status = await store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(status, options.Schedule.RefreshDatabaseCron));
    }

    private static DirectoryDatabaseStatusResponse ToResponse(
        DirectoryDatabaseStatus status,
        string refreshCron) =>
        new(
            refreshCron,
            status.EntryCount,
            status.LastRunId,
            status.LastRunStatus,
            status.LastScannedCount,
            status.LastIndexedCount,
            status.LastRejectedCount,
            status.LastFailureCode,
            status.LastStartedAtUtc,
            status.LastCompletedAtUtc);

    private static async Task<Ok<DataUpdateStatusResponse>> GetDataUpdateStatus(
        DataUpdateRuntimeState runtimeOptions,
        DataPackageStore packages,
        DataUpdateTransferStore transfers,
        CancellationToken cancellationToken)
    {
        var package = await packages.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var downloads = await transfers.ListDownloadsAsync(cancellationToken).ConfigureAwait(false);
        var transfer = await transfers.GetLastRunAsync(cancellationToken).ConfigureAwait(false);
        var options = runtimeOptions.Value;
        return TypedResults.Ok(new DataUpdateStatusResponse(
            options.Enabled,
            options.Cron,
            options.ManifestUrl is not null,
            options.AutoDownload,
            options.AutoImport,
            options.KeepVersions,
            package.ActiveVersion,
            package.PreviousVersion,
            package.UpdatedAtUtc,
            package.Versions.Select(version => new DataUpdateVersionResponse(
                version.DataVersion,
                version.State,
                version.SubjectCount,
                version.EpisodeCount,
                version.InstalledAtUtc,
                version.ActivatedAtUtc)).ToArray(),
            downloads.Select(download => new DataUpdateDownloadResponse(
                download.DataVersion,
                download.State,
                download.DownloadedAtUtc,
                download.ImportedAtUtc)).ToArray(),
            package.LastRun is null
                ? null
                : new DataUpdatePackageRunResponse(
                    package.LastRun.RunId,
                    package.LastRun.Operation,
                    package.LastRun.DataVersion,
                    package.LastRun.Status,
                    package.LastRun.FailureCode,
                    package.LastRun.SubjectCount,
                    package.LastRun.EpisodeCount,
                    package.LastRun.StartedAtUtc,
                    package.LastRun.CompletedAtUtc),
            transfer is null
                ? null
                : new DataUpdateTransferRunResponse(
                    transfer.RunId,
                    transfer.TriggerKind,
                    transfer.RequestedAction,
                    transfer.Status,
                    transfer.DataVersion,
                    transfer.FailureCode,
                    transfer.DownloadedBytes,
                    transfer.TotalBytes,
                    transfer.StartedAtUtc,
                    transfer.CompletedAtUtc)));
    }

    private static Task<IResult> CheckDataUpdate(
        IDataUpdateService service,
        CancellationToken cancellationToken) =>
        ExecuteDataUpdateAsync(service, DataUpdateActions.Check, cancellationToken);

    private static Task<IResult> DownloadDataUpdate(
        IDataUpdateService service,
        CancellationToken cancellationToken) =>
        ExecuteDataUpdateAsync(service, DataUpdateActions.Download, cancellationToken);

    private static Task<IResult> ApplyDataUpdate(
        IDataUpdateService service,
        CancellationToken cancellationToken) =>
        ExecuteDataUpdateAsync(service, DataUpdateActions.DownloadImport, cancellationToken);

    private static async Task<IResult> ExecuteDataUpdateAsync(
        IDataUpdateService service,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ExecuteAsync(
                DataUpdateTriggerKinds.Manual,
                action,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ToResponse(result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DataUpdateServiceException exception)
        {
            return DataUpdateFailure(exception);
        }
    }

    private static async Task<IResult> ImportDownloadedDataUpdate(
        string dataVersion,
        IDataUpdateService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ImportDownloadedAsync(
                dataVersion,
                DataUpdateTriggerKinds.Manual,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ToResponse(result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DataUpdateServiceException exception)
        {
            return DataUpdateFailure(exception);
        }
    }

    private static async Task<IResult> ImportOfflineDataUpdate(
        HttpContext context,
        IDataUpdateService service,
        CancellationToken cancellationToken)
    {
        var mediaType = context.Request.ContentType?.Split(';', 2)[0].Trim();
        if (!string.Equals(mediaType, "application/zip", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                mediaType,
                "application/octet-stream",
                StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                Error(
                    "data_offline_content_type_invalid",
                    "Offline data packages must be uploaded as a ZIP request body."),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }
        if (context.Request.ContentLength is > OfflineDataPackageArchive.MaximumArchiveBytes)
        {
            return Results.Json(
                Error(
                    "data_offline_archive_size_invalid",
                    "The offline data archive exceeds the supported size."),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }
        var requestSize = context.Features.Get<
            Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (requestSize is { IsReadOnly: false })
        {
            requestSize.MaxRequestBodySize = OfflineDataPackageArchive.MaximumArchiveBytes;
        }

        try
        {
            var result = await service.ImportOfflineArchiveAsync(
                context.Request.Body,
                context.Request.ContentLength,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ToResponse(result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BadHttpRequestException exception)
            when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Results.Json(
                Error(
                    "data_offline_archive_size_invalid",
                    "The offline data archive exceeds the supported size."),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }
        catch (DataUpdateServiceException exception)
        {
            return DataUpdateFailure(exception, offlineUpload: true);
        }
    }

    private static async Task<IResult> RollbackDataUpdate(
        DataPackageStore packages,
        CancellationToken cancellationToken)
    {
        try
        {
            var rollback = await packages.RollbackAsync(
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new DataUpdateActionResponse(
                rollback.RunId,
                "rolled_back",
                rollback.ActiveVersion,
                rollback.ActiveVersion,
                false,
                false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DataPackageException exception)
        {
            return DataUpdateFailure(new DataUpdateServiceException(
                exception.Code,
                exception.Message,
                exception));
        }
    }

    private static DataUpdateActionResponse ToResponse(DataUpdateExecutionResult result) =>
        new(
            result.RunId,
            result.Status,
            result.DataVersion,
            result.ActiveVersion,
            result.Downloaded,
            result.Imported);

    private static IResult DataUpdateFailure(
        DataUpdateServiceException exception,
        bool offlineUpload = false)
    {
        var statusCode = offlineUpload
            ? exception.Code switch
            {
                "data_update_busy" => StatusCodes.Status409Conflict,
                "data_version_immutable_conflict" => StatusCodes.Status409Conflict,
                "data_update_cancelled" => 499,
                "data_update_storage_failed" => StatusCodes.Status500InternalServerError,
                "data_update_import_failed" => StatusCodes.Status500InternalServerError,
                _ => StatusCodes.Status400BadRequest,
            }
            : exception.Code switch
            {
                "data_update_busy" => StatusCodes.Status409Conflict,
                "data_download_not_found" => StatusCodes.Status404NotFound,
                "data_rollback_version_unavailable" => StatusCodes.Status409Conflict,
                "data_manifest_url_missing" => StatusCodes.Status400BadRequest,
                "data_client_version_too_old" => StatusCodes.Status409Conflict,
                "data_version_immutable_conflict" => StatusCodes.Status409Conflict,
                "data_update_cancelled" => 499,
                _ => StatusCodes.Status502BadGateway,
            };
        return Results.Json(
            Error(exception.Code, exception.Message),
            ApiJsonContext.Default.ApiErrorResponse,
            statusCode: statusCode);
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

    private static async Task<Results<
        Ok<CacheBrowserBucketListResponse>,
        BadRequest<ApiErrorResponse>>> CacheBrowserBuckets(
        [FromQuery] string? database,
        SqliteJsonCacheStore store,
        CancellationToken cancellationToken)
    {
        var normalizedDatabase = string.IsNullOrWhiteSpace(database)
            ? "bolt"
            : database.Trim().ToLowerInvariant();
        try
        {
            var buckets = await store.ListBrowserBucketsAsync(
                normalizedDatabase,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new CacheBrowserBucketListResponse(
                normalizedDatabase,
                string.Equals(normalizedDatabase, "bolt_sub", StringComparison.Ordinal),
                buckets.Select(static bucket => new CacheBrowserBucketResponse(
                    bucket.BucketId,
                    bucket.EntryCount)).ToArray()));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("cache_database_invalid", exception.Message));
        }
    }

    private static async Task<Results<
        Ok<CacheBrowserEntryListResponse>,
        BadRequest<ApiErrorResponse>,
        NotFound<ApiErrorResponse>>> CacheBrowserEntries(
        [FromQuery] string? database,
        [FromQuery(Name = "bucket_id")] string? bucketId,
        [FromQuery] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        SqliteJsonCacheStore store,
        CancellationToken cancellationToken)
    {
        var normalizedDatabase = string.IsNullOrWhiteSpace(database)
            ? "bolt"
            : database.Trim().ToLowerInvariant();
        try
        {
            var result = await store.ListBrowserEntriesAsync(
                normalizedDatabase,
                bucketId ?? string.Empty,
                page ?? 1,
                pageSize ?? 50,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                return TypedResults.NotFound(Error(
                    "cache_bucket_not_found",
                    "Cache bucket does not exist."));
            }
            return TypedResults.Ok(new CacheBrowserEntryListResponse(
                normalizedDatabase,
                string.Equals(normalizedDatabase, "bolt_sub", StringComparison.Ordinal),
                result.BucketId,
                result.Page,
                result.PageSize,
                result.TotalCount,
                result.Items.Select(static item => new CacheBrowserEntryResponse(
                    item.EntryId,
                    item.DeleteToken,
                    item.ValueBytes,
                    item.ExpiresAtUtc,
                    item.UpdatedAtUtc)).ToArray()));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("cache_query_invalid", exception.Message));
        }
    }

    private static async Task<Results<
        Ok<CacheBrowserDeleteResponse>,
        BadRequest<ApiErrorResponse>,
        NotFound<ApiErrorResponse>,
        Conflict<ApiErrorResponse>>> DeleteCacheBrowserEntry(
        string entryId,
        CacheBrowserDeleteRequest request,
        SqliteJsonCacheStore store,
        CancellationToken cancellationToken)
    {
        var normalizedDatabase = string.IsNullOrWhiteSpace(request.Database)
            ? "bolt"
            : request.Database.Trim().ToLowerInvariant();
        try
        {
            var result = await store.DeleteBrowserEntryAsync(
                normalizedDatabase,
                request.BucketId ?? string.Empty,
                entryId,
                request.DeleteToken ?? string.Empty,
                cancellationToken).ConfigureAwait(false);
            return result switch
            {
                CacheBrowserDeleteResult.Deleted => TypedResults.Ok(
                    new CacheBrowserDeleteResponse(
                        normalizedDatabase,
                        request.BucketId!.Trim().ToLowerInvariant(),
                        entryId.Trim().ToLowerInvariant(),
                        true)),
                CacheBrowserDeleteResult.NotFound => TypedResults.NotFound(Error(
                    "cache_entry_not_found",
                    "Cache entry does not exist.")),
                CacheBrowserDeleteResult.Changed => TypedResults.Conflict(Error(
                    "cache_entry_changed",
                    "Cache entry changed after it was listed. Refresh before deleting.")),
                CacheBrowserDeleteResult.ReadOnly => TypedResults.Conflict(Error(
                    "cache_namespace_read_only",
                    "The bolt_sub namespace is read-only.")),
                _ => TypedResults.Conflict(Error(
                    "cache_delete_failed",
                    "Cache entry could not be deleted.")),
            };
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("cache_delete_invalid", exception.Message));
        }
    }

    private static Ok<RuntimeStatus> Status(
        AnimeGoOptions options,
        LegacyDownloaderMigrationState legacyMigration,
        ExternalPluginDiscoveryResult externalPlugins,
        ExternalPluginHostManager externalPluginHost,
        ExternalPluginConfigurationService externalPluginConfigurations)
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
                UnifiedIngest: !legacyMigration.BlocksDownloads,
                RssRules: true,
                Qbittorrent: !legacyMigration.BlocksDownloads,
                Tmdb: !string.IsNullOrWhiteSpace(options.Metadata.Tmdb.ApiKey)
                    || !string.IsNullOrWhiteSpace(options.Metadata.Tmdb.ReadAccessToken),
                Organizer: true,
                Deletion: true),
            legacyMigration.BlocksDownloads,
            ToResponse(legacyMigration),
            new ExternalPluginRuntimeStatusResponse(
                externalPlugins.Packages.Select(package =>
                {
                    var configuration = externalPluginConfigurations
                        .GetOrDefault(package.Manifest.Id);
                    return new ExternalPluginPackageResponse(
                        package.Manifest.Id,
                        package.Manifest.Name,
                        package.Manifest.Version,
                        package.Manifest.Type,
                        package.Manifest.Rid,
                        package.Manifest.Capabilities,
                        configuration.Revision > 0,
                        configuration.Enabled,
                        configuration.Revision);
                }).ToArray(),
                externalPlugins.Errors.Select(error =>
                    new ExternalPluginPackageErrorResponse(
                        error.PackageDirectoryName,
                        error.Code,
                        error.Message)).ToArray(),
                externalPluginHost.GetSnapshots().Select(runtime =>
                    ToResponse(runtime)).ToArray())));
    }

    private static async Task<IResult> ResetExternalPlugin(
        string pluginId,
        ExternalPluginHostManager manager,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = RequireCanonicalStableId(pluginId, "plugin id");
            if (manager.GetSnapshot(id) is null)
            {
                return TypedResults.NotFound(Error(
                    "external_plugin_not_found",
                    "External plugin was not found."));
            }
            await manager.ResetAsync(id, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ToResponse(manager.GetSnapshot(id)!));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error(
                "external_plugin_id_invalid",
                exception.Message));
        }
    }

    private static async Task<IResult> ExternalPluginConfigurations(
        ExternalPluginConfigurationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = await service.ListAsync(cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new ExternalPluginConfigurationListResponse(
                service.Current.Revision,
                items.Select(ToResponse).ToArray()));
        }
        catch (ExternalPluginConfigurationValidationException exception)
        {
            return TypedResults.Conflict(Error(exception.Code, exception.Message));
        }
        catch (ExternalPluginManifestException exception)
        {
            return TypedResults.Conflict(Error(exception.Code, exception.Message));
        }
        catch (ExternalPluginProtocolException exception)
        {
            return TypedResults.Conflict(Error(exception.Code, exception.Message));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return TypedResults.Conflict(Error(
                "external_plugin_package_unreadable",
                "An external plugin package could not be read safely."));
        }
    }

    private static async Task<IResult> PutExternalPluginConfiguration(
        string pluginId,
        ExternalPluginConfigurationUpdateRequest request,
        ExternalPluginConfigurationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = RequireCanonicalStableId(pluginId, "plugin id");
            var saved = await service.SaveSafeAsync(
                id,
                request.Enabled,
                request.Args,
                request.Vars,
                request.ClearWriteOnlyPaths,
                request.ExpectedRevision,
                cancellationToken).ConfigureAwait(false);
            var item = await service.GetAsync(id, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new ExternalPluginConfigurationMutationResponse(
                saved.Revision,
                ToResponse(item)));
        }
        catch (ExternalPluginConfigurationRevisionException)
        {
            return TypedResults.Conflict(Error(
                "external_plugin_configuration_revision_conflict",
                "External plugin configuration changed; reload before saving."));
        }
        catch (ExternalPluginConfigurationValidationException exception)
        {
            return TypedResults.BadRequest(Error(
                exception.Code,
                $"{exception.Path}: {exception.Message}"));
        }
        catch (ExternalPluginUnavailableException exception) when (
            exception.Code == "plugin_not_found")
        {
            return TypedResults.NotFound(Error(
                "external_plugin_not_found",
                "External plugin was not found."));
        }
        catch (ExternalPluginManifestException exception)
        {
            return TypedResults.Conflict(Error(exception.Code, exception.Message));
        }
        catch (ExternalPluginProtocolException exception)
        {
            return TypedResults.Conflict(Error(exception.Code, exception.Message));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error(
                "external_plugin_configuration_invalid",
                exception.Message));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return TypedResults.Conflict(Error(
                "external_plugin_package_unreadable",
                "The external plugin package could not be read safely."));
        }
    }

    private static async Task<IResult> DeleteExternalPluginConfiguration(
        string pluginId,
        [FromQuery(Name = "expected_revision")] long expectedRevision,
        ExternalPluginConfigurationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = RequireCanonicalStableId(pluginId, "plugin id");
            var saved = await service.DeleteAsync(
                id,
                expectedRevision,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new ExternalPluginConfigurationDeleteResponse(
                saved.Revision,
                id));
        }
        catch (ExternalPluginConfigurationRevisionException)
        {
            return TypedResults.Conflict(Error(
                "external_plugin_configuration_revision_conflict",
                "External plugin configuration changed; reload before deleting."));
        }
        catch (ExternalPluginUnavailableException exception) when (
            exception.Code == "plugin_not_found")
        {
            return TypedResults.NotFound(Error(
                "external_plugin_not_found",
                "External plugin was not found."));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound(Error(
                "external_plugin_configuration_not_found",
                "External plugin does not have saved configuration."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error(
                "external_plugin_configuration_invalid",
                exception.Message));
        }
    }

    private static async Task<Ok<ConfigurationResponse>> Configuration(
        AnimeGoOptions options,
        RuntimeConfigurationState runtime,
        DeploymentConfigurationOptions deployment,
        DeploymentConfigurationLocks locks,
        ApplicationOverrideStore store,
        ApplicationConfigurationRuntimeState applied,
        DataUpdateRuntimeState dataUpdateRuntime,
        LegacyDownloaderMigrationState legacyMigration,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var desired = locks.Reapply(
            deployment.Value,
            ApplicationOverrideStore.Apply(deployment.Value, snapshot));
        var runtimeOptions = options with { DataUpdate = dataUpdateRuntime.Value };
        return TypedResults.Ok(ToConfigurationResponse(
            runtimeOptions,
            desired,
            snapshot.Settings,
            runtime,
            locks,
            snapshot.Revision,
            applied.AppliedRevision,
            legacyMigration));
    }

    private static async Task<IResult> PreviewConfiguration(
        ConfigurationUpdateRequest request,
        DeploymentConfigurationOptions deployment,
        DeploymentConfigurationLocks locks,
        ApplicationOverrideStore store,
        AnimeGoOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentOutOfRangeException.ThrowIfNegative(
                request.ExpectedConfigurationRevision);
            var current = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (current.Revision != request.ExpectedConfigurationRevision)
            {
                throw new ApplicationOverrideRevisionException();
            }
            var currentDesired = locks.Reapply(
                deployment.Value,
                ApplicationOverrideStore.Apply(deployment.Value, current));
            var (settings, candidate) = BuildConfigurationCandidate(
                request,
                current,
                deployment.Value,
                locks);
            var changes = ConfigurationChanges(
                request,
                currentDesired,
                candidate,
                current.Settings,
                settings);
            return TypedResults.Ok(new ConfigurationPreviewResponse(
                request.ExpectedConfigurationRevision,
                current.Revision,
                RequiresRestart(options, candidate),
                changes.Any(change => change.Effect == "hot_reload"),
                changes));
        }
        catch (ApplicationOverrideRevisionException)
        {
            return TypedResults.Conflict(Error(
                "configuration_revision_conflict",
                "Configuration changed concurrently; reload before previewing."));
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

    private static async Task<IResult> PutConfiguration(
        ConfigurationUpdateRequest request,
        DeploymentConfigurationOptions deployment,
        DeploymentConfigurationLocks locks,
        ApplicationOverrideStore store,
        ApplicationConfigurationRuntimeState applied,
        AnimeGoOptions options,
        DataUpdateScheduleManager dataUpdateSchedules,
        IHostApplicationLifetime applicationLifetime,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var (settings, candidate) = BuildConfigurationCandidate(
                request,
                current,
                deployment.Value,
                locks);

            var saved = await store.SaveAsync(
                settings,
                request.ExpectedConfigurationRevision,
                cancellationToken).ConfigureAwait(false);
            await dataUpdateSchedules.ApplyAsync(
                candidate.DataUpdate,
                applicationLifetime.ApplicationStopping).ConfigureAwait(false);
            if (!RequiresRestart(options, candidate))
            {
                applied.MarkApplied(saved.Revision);
            }
            return TypedResults.Ok(new ConfigurationWriteResponse(
                saved.Revision,
                RestartRequired: saved.Revision != applied.AppliedRevision,
                RevertedToDeploymentDefault: false,
                BackupRevision: current.Revision > 0 ? current.Revision : null));
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
        catch (PluginScheduleException)
        {
            return Results.Json(
                Error(
                    "configuration_hot_apply_failed",
                    "Configuration was saved but data update scheduling could not be applied; restart the application."),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("configuration_invalid", exception.Message));
        }
    }

    private static async Task<IResult> DeleteConfigurationOverride(
        [FromQuery(Name = "expected_revision")] long expectedRevision,
        AnimeGoOptions options,
        DeploymentConfigurationOptions deployment,
        DeploymentConfigurationLocks locks,
        ApplicationOverrideStore store,
        ApplicationConfigurationRuntimeState applied,
        DataUpdateScheduleManager dataUpdateSchedules,
        IHostApplicationLifetime applicationLifetime,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await store.DeleteAsync(expectedRevision, cancellationToken).ConfigureAwait(false);
            var candidate = locks.Reapply(deployment.Value, deployment.Value);
            await dataUpdateSchedules.ApplyAsync(
                candidate.DataUpdate,
                applicationLifetime.ApplicationStopping).ConfigureAwait(false);
            if (!RequiresRestart(options, candidate))
            {
                applied.MarkApplied(saved.Revision);
            }
            return TypedResults.Ok(new ConfigurationWriteResponse(
                saved.Revision,
                RestartRequired: saved.Revision != applied.AppliedRevision,
                RevertedToDeploymentDefault: true,
                BackupRevision: saved.Revision > expectedRevision
                    ? expectedRevision
                    : null));
        }
        catch (ApplicationOverrideRevisionException)
        {
            return TypedResults.Conflict(Error(
                "configuration_revision_conflict",
                "Configuration changed concurrently; reload before reverting."));
        }
        catch (PluginScheduleException)
        {
            return Results.Json(
                Error(
                    "configuration_hot_apply_failed",
                    "Configuration override was removed but data update scheduling could not be applied; restart the application."),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: StatusCodes.Status500InternalServerError);
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
        long appliedConfigurationRevision,
        LegacyDownloaderMigrationState legacyMigration)
    {
        var tmdb = options.Metadata.Tmdb;
        var bangumi = options.Metadata.Bangumi;
        var season = options.Metadata.SeasonFailure;
        var ai = options.Metadata.Ai;
        var fetch = options.TorrentFetch;
        var dataUpdate = options.DataUpdate;
        return new ConfigurationResponse(
            configurationRevision,
            appliedConfigurationRevision,
            configurationRevision != appliedConfigurationRevision,
            legacyMigration.BlocksDownloads,
            ToResponse(legacyMigration),
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
                    tmdb.RetryCount,
                    tmdb.RetryDelay.TotalSeconds,
                    tmdb.CacheTtl.TotalHours,
                    !string.IsNullOrWhiteSpace(tmdb.ApiKey),
                    !string.IsNullOrWhiteSpace(tmdb.ReadAccessToken)),
                new BangumiConfigurationResponse(
                    bangumi.BaseUrl.AbsoluteUri,
                    bangumi.ProxyUrl?.AbsoluteUri,
                    bangumi.HttpTimeout.TotalSeconds,
                    bangumi.RetryCount,
                    bangumi.RetryDelay.TotalSeconds),
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
            new DataUpdateConfigurationResponse(
                dataUpdate.Enabled,
                dataUpdate.Cron,
                dataUpdate.ManifestUrl?.AbsoluteUri,
                dataUpdate.AutoDownload,
                dataUpdate.AutoImport,
                dataUpdate.KeepVersions,
                dataUpdate.HttpTimeout.TotalSeconds,
                HotReloadSupported: true),
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
        var dataUpdate = desired.DataUpdate;
        return new EditableConfigurationResponse(
            tmdb.BaseUrl.AbsoluteUri,
            tmdb.ProxyUrl?.AbsoluteUri,
            tmdb.Language,
            tmdb.HttpTimeout.TotalSeconds,
            tmdb.RetryCount,
            tmdb.RetryDelay.TotalSeconds,
            tmdb.CacheTtl.TotalHours,
            SecretState(settings?.TmdbApiKeyOverridden == true, settings?.TmdbApiKey),
            SecretState(
                settings?.TmdbReadAccessTokenOverridden == true,
                settings?.TmdbReadAccessToken),
            bangumi.BaseUrl.AbsoluteUri,
            bangumi.ProxyUrl?.AbsoluteUri,
            bangumi.HttpTimeout.TotalSeconds,
            bangumi.RetryCount,
            bangumi.RetryDelay.TotalSeconds,
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
            dataUpdate.Enabled,
            dataUpdate.Cron,
            dataUpdate.ManifestUrl?.AbsoluteUri,
            dataUpdate.AutoDownload,
            dataUpdate.AutoImport,
            dataUpdate.KeepVersions,
            dataUpdate.HttpTimeout.TotalSeconds,
            locks.Items.Select(item => new ConfigurationFieldLockResponse(
                item.Field,
                item.Source,
                item.EnvironmentVariables,
                item.CommandLineArguments,
                item.ControllingKeys)).ToArray());
    }

    private static string SecretState(bool overridden, string? value) =>
        !overridden ? "inherit" : value is null ? "cleared" : "configured";

    private static (ApplicationOverrideEntry Settings, AnimeGoOptions Candidate)
        BuildConfigurationCandidate(
            ConfigurationUpdateRequest request,
            ApplicationOverrideSnapshot current,
            AnimeGoOptions deployment,
            DeploymentConfigurationLocks locks)
    {
        if (request.ClearTmdbApiKey && !string.IsNullOrWhiteSpace(request.TmdbApiKey))
        {
            throw new ArgumentException(
                "tmdb_api_key and clear_tmdb_api_key cannot both be set.");
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
            deployment.Metadata.Tmdb.CacheTtl.TotalHours,
            DateTimeOffset.UtcNow);
        var requestedCandidate = ApplicationOverrideStore.Apply(
            deployment,
            new ApplicationOverrideSnapshot(
                1,
                current.Revision + 1,
                requestedSettings));
        var changedLockedFields = locks
            .FindChangedLockedFields(deployment, requestedCandidate)
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
            deployment,
            ApplicationOverrideStore.Apply(
                deployment,
                new ApplicationOverrideSnapshot(
                    1,
                    current.Revision + 1,
                    settings)));
        var errors = AnimeGoOptionsValidator.Validate(candidate);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors));
        }
        return (settings, candidate);
    }

    private static List<ConfigurationChangeResponse> ConfigurationChanges(
        ConfigurationUpdateRequest request,
        AnimeGoOptions current,
        AnimeGoOptions candidate,
        ApplicationOverrideEntry? currentSettings,
        ApplicationOverrideEntry candidateSettings)
    {
        var changes = new List<ConfigurationChangeResponse>();
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var beforeTmdb = current.Metadata.Tmdb;
        var afterTmdb = candidate.Metadata.Tmdb;
        var beforeBangumi = current.Metadata.Bangumi;
        var afterBangumi = candidate.Metadata.Bangumi;
        var beforeSeason = current.Metadata.SeasonFailure;
        var afterSeason = candidate.Metadata.SeasonFailure;
        var beforeAi = current.Metadata.Ai;
        var afterAi = candidate.Metadata.Ai;
        var beforeTorrent = current.TorrentFetch;
        var afterTorrent = candidate.TorrentFetch;
        var beforeDataUpdate = current.DataUpdate;
        var afterDataUpdate = candidate.DataUpdate;

        Add("tmdb_base_url", beforeTmdb.BaseUrl.AbsoluteUri, afterTmdb.BaseUrl.AbsoluteUri);
        Add("tmdb_proxy_url", beforeTmdb.ProxyUrl?.AbsoluteUri, afterTmdb.ProxyUrl?.AbsoluteUri);
        Add("tmdb_language", beforeTmdb.Language, afterTmdb.Language);
        AddSeconds(
            "tmdb_http_timeout_seconds",
            beforeTmdb.HttpTimeout,
            afterTmdb.HttpTimeout);
        Add(
            "tmdb_retry_count",
            beforeTmdb.RetryCount.ToString(invariant),
            afterTmdb.RetryCount.ToString(invariant));
        AddSeconds(
            "tmdb_retry_delay_seconds",
            beforeTmdb.RetryDelay,
            afterTmdb.RetryDelay);
        Add(
            "tmdb_cache_hours",
            beforeTmdb.CacheTtl.TotalHours.ToString(invariant),
            afterTmdb.CacheTtl.TotalHours.ToString(invariant));
        AddSecret(
            "tmdb_api_key",
            request.TmdbApiKey,
            request.ClearTmdbApiKey,
            currentSettings?.TmdbApiKeyOverridden == true,
            currentSettings?.TmdbApiKey);
        AddSecret(
            "tmdb_read_access_token",
            request.TmdbReadAccessToken,
            request.ClearTmdbReadAccessToken,
            currentSettings?.TmdbReadAccessTokenOverridden == true,
            currentSettings?.TmdbReadAccessToken);
        Add(
            "bangumi_base_url",
            beforeBangumi.BaseUrl.AbsoluteUri,
            afterBangumi.BaseUrl.AbsoluteUri);
        Add(
            "bangumi_proxy_url",
            beforeBangumi.ProxyUrl?.AbsoluteUri,
            afterBangumi.ProxyUrl?.AbsoluteUri);
        AddSeconds(
            "bangumi_http_timeout_seconds",
            beforeBangumi.HttpTimeout,
            afterBangumi.HttpTimeout);
        Add(
            "bangumi_retry_count",
            beforeBangumi.RetryCount.ToString(invariant),
            afterBangumi.RetryCount.ToString(invariant));
        AddSeconds(
            "bangumi_retry_delay_seconds",
            beforeBangumi.RetryDelay,
            afterBangumi.RetryDelay);
        AddBool("season_failure_skip", beforeSeason.Skip, afterSeason.Skip);
        AddBool("season_failure_backtrace", beforeSeason.Backtrace, afterSeason.Backtrace);
        AddBool(
            "season_failure_use_title_season",
            beforeSeason.UseTitleSeason,
            afterSeason.UseTitleSeason);
        AddBool(
            "season_failure_use_first_season",
            beforeSeason.UseFirstSeason,
            afterSeason.UseFirstSeason);
        AddBool(
            "ai_use_metadata_match",
            beforeAi.UseMetadataMatch,
            afterAi.UseMetadataMatch);
        AddSeconds(
            "ai_http_timeout_seconds",
            beforeAi.HttpTimeout,
            afterAi.HttpTimeout);
        AddBool(
            "tmdb_failure_use_bangumi",
            current.Metadata.TmdbFailureUseBangumi,
            candidate.Metadata.TmdbFailureUseBangumi);
        AddBool(
            "mikan_trusted_offset_cache_enabled",
            current.Metadata.MikanTrustedOffsetCacheEnabled,
            candidate.Metadata.MikanTrustedOffsetCacheEnabled);
        AddSeconds(
            "torrent_http_timeout_seconds",
            beforeTorrent.Timeout,
            afterTorrent.Timeout);
        Add(
            "torrent_max_response_bytes",
            beforeTorrent.MaxResponseBytes.ToString(invariant),
            afterTorrent.MaxResponseBytes.ToString(invariant));
        Add(
            "torrent_max_redirects",
            beforeTorrent.MaxRedirects.ToString(invariant),
            afterTorrent.MaxRedirects.ToString(invariant));
        AddSeconds(
            "torrent_staging_ttl_seconds",
            beforeTorrent.StagingTtl,
            afterTorrent.StagingTtl);
        AddBool(
            "data_update_enabled",
            beforeDataUpdate.Enabled,
            afterDataUpdate.Enabled,
            "hot_reload");
        Add(
            "data_update_cron",
            beforeDataUpdate.Cron,
            afterDataUpdate.Cron,
            "hot_reload");
        Add(
            "data_update_manifest_url",
            beforeDataUpdate.ManifestUrl?.AbsoluteUri,
            afterDataUpdate.ManifestUrl?.AbsoluteUri,
            "hot_reload");
        AddBool(
            "data_update_auto_download",
            beforeDataUpdate.AutoDownload,
            afterDataUpdate.AutoDownload,
            "hot_reload");
        AddBool(
            "data_update_auto_import",
            beforeDataUpdate.AutoImport,
            afterDataUpdate.AutoImport,
            "hot_reload");
        Add(
            "data_update_keep_versions",
            beforeDataUpdate.KeepVersions.ToString(invariant),
            afterDataUpdate.KeepVersions.ToString(invariant),
            "hot_reload");
        AddSeconds(
            "data_update_http_timeout_seconds",
            beforeDataUpdate.HttpTimeout,
            afterDataUpdate.HttpTimeout,
            "hot_reload");
        return changes;

        void Add(
            string field,
            string? before,
            string? after,
            string effect = "restart",
            bool sensitive = false,
            bool force = false)
        {
            if (!force && string.Equals(before, after, StringComparison.Ordinal))
            {
                return;
            }
            changes.Add(new ConfigurationChangeResponse(
                field,
                before,
                after,
                effect,
                sensitive));
        }

        void AddBool(
            string field,
            bool before,
            bool after,
            string effect = "restart") =>
            Add(
                field,
                before ? "true" : "false",
                after ? "true" : "false",
                effect);

        void AddSeconds(
            string field,
            TimeSpan before,
            TimeSpan after,
            string effect = "restart") =>
            Add(
                field,
                before.TotalSeconds.ToString("0.###", invariant),
                after.TotalSeconds.ToString("0.###", invariant),
                effect);

        void AddSecret(
            string field,
            string? requestedValue,
            bool clear,
            bool currentOverridden,
            string? currentValue)
        {
            if (!clear && string.IsNullOrWhiteSpace(requestedValue))
            {
                return;
            }
            Add(
                field,
                SecretState(currentOverridden, currentValue),
                clear
                    ? "cleared"
                    : SecretState(
                        field == "tmdb_api_key"
                            ? candidateSettings.TmdbApiKeyOverridden
                            : candidateSettings.TmdbReadAccessTokenOverridden,
                        field == "tmdb_api_key"
                            ? candidateSettings.TmdbApiKey
                            : candidateSettings.TmdbReadAccessToken),
                sensitive: true,
                force: true);
        }
    }

    private static ApplicationOverrideEntry CreateApplicationOverride(
        ConfigurationUpdateRequest request,
        ApplicationOverrideEntry? current,
        double deploymentTmdbCacheHours,
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
        if (request.TmdbRetryCount is { } tmdbRetryCount)
        {
            ValidateRetryCount(tmdbRetryCount, "tmdb_retry_count");
        }
        if (request.TmdbRetryDelaySeconds is { } tmdbRetryDelay)
        {
            ValidateNonNegativeSeconds(
                tmdbRetryDelay,
                "tmdb_retry_delay_seconds",
                300);
        }
        var tmdbCacheHours = request.TmdbCacheHours
            ?? current?.TmdbCacheHours
            ?? deploymentTmdbCacheHours;
        ValidateSeconds(tmdbCacheHours, "tmdb_cache_hours", 24 * 365);
        ValidateSeconds(
            request.BangumiHttpTimeoutSeconds,
            "bangumi_http_timeout_seconds",
            86_400);
        if (request.BangumiRetryCount is { } bangumiRetryCount)
        {
            ValidateRetryCount(bangumiRetryCount, "bangumi_retry_count");
        }
        if (request.BangumiRetryDelaySeconds is { } bangumiRetryDelay)
        {
            ValidateNonNegativeSeconds(
                bangumiRetryDelay,
                "bangumi_retry_delay_seconds",
                300);
        }
        ValidateSeconds(request.AiHttpTimeoutSeconds, "ai_http_timeout_seconds", 86_400);
        ValidateSeconds(request.TorrentHttpTimeoutSeconds, "torrent_http_timeout_seconds", 86_400);
        ValidateSeconds(request.TorrentStagingTtlSeconds, "torrent_staging_ttl_seconds", 604_800);
        ValidateSeconds(
            request.DataUpdateHttpTimeoutSeconds,
            "data_update_http_timeout_seconds",
            3_600);
        var dataUpdateCron = request.DataUpdateCron?.Trim()
            ?? throw new ArgumentException("data_update_cron is required.");
        if (dataUpdateCron.Length is < 1 or > 256)
        {
            throw new ArgumentException("data_update_cron must contain 1 to 256 characters.");
        }
        var dataUpdateManifestUrl = NormalizeOptionalUrl(
            request.DataUpdateManifestUrl,
            "data_update_manifest_url");
        if (request.DataUpdateKeepVersions is < 2 or > 10)
        {
            throw new ArgumentException(
                "data_update_keep_versions must be between 2 and 10.");
        }
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
            AiUseMetadataMatch: aiUseMetadataMatch,
            DataUpdateEnabled: request.DataUpdateEnabled,
            DataUpdateCron: dataUpdateCron,
            DataUpdateManifestUrlOverridden: true,
            DataUpdateManifestUrl: dataUpdateManifestUrl,
            DataUpdateAutoDownload: request.DataUpdateAutoDownload,
            DataUpdateAutoImport: request.DataUpdateAutoImport,
            DataUpdateKeepVersions: request.DataUpdateKeepVersions,
            DataUpdateHttpTimeoutSeconds: request.DataUpdateHttpTimeoutSeconds,
            TmdbRetryCount: request.TmdbRetryCount
                ?? current?.TmdbRetryCount,
            TmdbRetryDelaySeconds: request.TmdbRetryDelaySeconds
                ?? current?.TmdbRetryDelaySeconds,
            BangumiRetryCount: request.BangumiRetryCount
                ?? current?.BangumiRetryCount,
            BangumiRetryDelaySeconds: request.BangumiRetryDelaySeconds
                ?? current?.BangumiRetryDelaySeconds,
            TmdbCacheHours: tmdbCacheHours);
    }

    private static bool RequiresRestart(AnimeGoOptions current, AnimeGoOptions candidate) =>
        current != candidate with { DataUpdate = current.DataUpdate };

    private static void ValidateSeconds(double value, string name, double maximum)
    {
        if (!double.IsFinite(value) || value <= 0 || value > maximum)
        {
            throw new ArgumentException($"{name} must be greater than 0 and at most {maximum}.");
        }
    }

    private static void ValidateNonNegativeSeconds(
        double value,
        string name,
        double maximum)
    {
        if (!double.IsFinite(value) || value < 0 || value > maximum)
        {
            throw new ArgumentException(
                $"{name} must be at least 0 and at most {maximum}.");
        }
    }

    private static void ValidateRetryCount(int value, string name)
    {
        if (value is < 0 or > 10)
        {
            throw new ArgumentException(
                $"{name} must be between 0 and 10.");
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
                detail.PreparationFailureCode,
                null,
                null,
                null,
                null),
            new DownloadStageDetail(
                detail.OrganizationState,
                detail.OrganizationAttemptCount,
                detail.OrganizationNextAttemptAtUtc,
                detail.OrganizationFailureCode,
                detail.OrganizationPhase,
                detail.OrganizationCompletedUnits,
                detail.OrganizationTotalUnits,
                detail.OrganizationTotalUnits == 0
                    ? 0
                    : (double)detail.OrganizationCompletedUnits / detail.OrganizationTotalUnits),
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
        LegacyDownloaderMigrationState legacyMigration,
        CancellationToken cancellationToken) =>
        legacyMigration.BlockingDiagnostic is { } diagnostic
            ? Task.FromResult<IResult>(MigrationBlocked(diagnostic))
            : ControlDownload(
                jobId, request, "pause", "paused", jobs, clients, cancellationToken);

    private static Task<IResult> ResumeDownload(
        string jobId,
        DownloadControlRequest request,
        DownloadJobStore jobs,
        DownloadClientOperationCoordinator clients,
        LegacyDownloaderMigrationState legacyMigration,
        CancellationToken cancellationToken) =>
        legacyMigration.BlockingDiagnostic is { } diagnostic
            ? Task.FromResult<IResult>(MigrationBlocked(diagnostic))
            : ControlDownload(
                jobId, request, "resume", "waiting", jobs, clients, cancellationToken);

    private static async Task<IResult> RetryDownload(
        string jobId,
        DownloadControlRequest request,
        DownloadJobStore jobs,
        DownloadClientOperationCoordinator clients,
        LegacyDownloaderMigrationState legacyMigration,
        CancellationToken cancellationToken)
    {
        if (legacyMigration.BlockingDiagnostic is { } diagnostic)
        {
            return MigrationBlocked(diagnostic);
        }

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
        DownloaderDeploymentLocks locks,
        DownloaderConfigurationRuntimeState runtimeState,
        DownloadClientOperationCoordinator clients,
        LegacyDownloaderMigrationState legacyMigration,
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
            if (pending is not null
                && options.Downloaders.TryGetValue(id, out var deployed))
            {
                downloader = locks.Reapply(id, deployed, downloader);
            }
            if (legacyMigration.BlocksDownloads)
            {
                downloader = downloader with { Enabled = false };
            }
            var usage = await admin.GetUsageAsync(id, cancellationToken).ConfigureAwait(false);
            items.Add(ToResponse(
                id, downloader, usage,
                legacyMigration.BlocksDownloads
                    ? "blocked_by_legacy_migration"
                    : pending is null ? "deployment" : "private_override",
                locks.ForDownloader(id),
                pending?.Revision,
                restartRequired,
                clients.GetCircuitSnapshot(id)));
        }
        return TypedResults.Ok(new DownloaderInstanceListResponse(
            snapshot.Revision,
            runtimeState.AppliedRevision,
            restartRequired,
            legacyMigration.BlocksDownloads,
            ToResponse(legacyMigration),
            items));
    }

    private static async Task<IResult> PutDownloader(
        string downloaderId,
        DownloaderInstanceUpsertRequest request,
        AnimeGoOptions options,
        DownloaderAdminStore admin,
        DownloaderOverrideStore overrides,
        DownloaderDeploymentLocks locks,
        CancellationToken cancellationToken)
    {
        var changedLockedFields = new List<string>();
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
            var candidatePassword = request.ClearPassword
                ? null
                : request.Password ?? currentOverride?.Password ?? currentRuntime?.Password;
            var candidateUsername = request.Username is null
                ? currentOverride?.Username ?? currentRuntime?.Username
                : string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim();
            if (candidatePassword?.Length > 1024)
                throw new ArgumentException("password must not exceed 1024 characters.");
            AddIfLockedAndChanged(
                "base_url",
                currentRuntime?.BaseUrl,
                baseUrl);
            AddIfLockedAndChanged(
                "username",
                currentRuntime?.Username,
                request.Username is null && locks.IsLocked(id, "username")
                    ? currentRuntime?.Username
                    : candidateUsername);
            AddIfLockedAndChanged(
                "password",
                currentRuntime?.Password,
                request.Password is null
                    && !request.ClearPassword
                    && locks.IsLocked(id, "password")
                        ? currentRuntime?.Password
                        : candidatePassword);
            if (locks.IsLocked(id, "download_path")
                && currentRuntime is not null
                && !SamePath(currentRuntime.DownloadPath, downloadPath))
            {
                changedLockedFields.Add("download_path");
            }
            AddIfLockedAndChanged(
                "enabled",
                currentRuntime?.Enabled,
                request.Enabled);
            if (changedLockedFields.Count > 0)
            {
                return TypedResults.BadRequest(Error(
                    "downloader_field_locked",
                    "Downloader field(s) are controlled by deployment configuration: "
                    + string.Join(
                        ", ",
                        changedLockedFields.Distinct(StringComparer.Ordinal))));
            }

            var storedBaseUrl = locks.IsLocked(id, "base_url")
                ? currentOverride?.BaseUrl
                    ?? currentRuntime?.BaseUrl.AbsoluteUri
                    ?? baseUrl.AbsoluteUri
                : baseUrl.AbsoluteUri;
            var storedUsername = locks.IsLocked(id, "username")
                ? currentOverride?.Username
                : candidateUsername;
            var storedPassword = locks.IsLocked(id, "password")
                ? currentOverride?.Password
                : candidatePassword;
            var storedDownloadPath = locks.IsLocked(id, "download_path")
                ? currentOverride?.DownloadPath
                    ?? currentRuntime?.DownloadPath
                    ?? downloadPath
                : downloadPath;
            var storedEnabled = locks.IsLocked(id, "enabled")
                ? currentOverride?.Enabled
                    ?? currentRuntime?.Enabled
                    ?? request.Enabled
                : request.Enabled;
            var saved = await overrides.UpsertAsync(
                id,
                new DownloaderOverrideEntry(
                    storedBaseUrl,
                    storedUsername,
                    storedPassword,
                    storedDownloadPath,
                    storedEnabled,
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

        void AddIfLockedAndChanged<T>(
            string field,
            T current,
            T requested)
        {
            if (locks.IsLocked(downloaderId, field)
                && !EqualityComparer<T>.Default.Equals(current, requested))
            {
                changedLockedFields.Add(field);
            }
        }

        static bool SamePath(string left, string right) =>
            PathBoundary.IsWithin(left, right)
            && PathBoundary.IsWithin(right, left);
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
        LegacyDownloaderMigrationState legacyMigration,
        CancellationToken cancellationToken)
    {
        if (legacyMigration.BlockingDiagnostic is { } diagnostic)
        {
            return MigrationBlocked(diagnostic);
        }

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
        AnimeGoOptions options,
        LegacyDownloaderMigrationState legacyMigration)
    {
        if (legacyMigration.BlockingDiagnostic is { } diagnostic)
        {
            return MigrationBlocked(diagnostic);
        }

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
        SourceRssScheduleManager schedules,
        SourceProfileDeploymentLocks locks,
        CancellationToken cancellationToken)
    {
        var records = await profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new SourceProfileListResponse(
            records.Select(profile => ToResponse(
                profile,
                schedules,
                locks.ForSource(profile.Id))).ToArray()));
    }

    private static async Task<IResult> GetSourceProfile(
        string sourceProfileId,
        SourceProfileStore profiles,
        SourceRssScheduleManager schedules,
        SourceProfileDeploymentLocks locks,
        CancellationToken cancellationToken)
    {
        var record = await profiles.GetAsync(sourceProfileId, cancellationToken).ConfigureAwait(false);
        return record is null
            ? TypedResults.NotFound(Error("source_profile_not_found", "Source profile was not found."))
            : TypedResults.Ok(ToResponse(record, schedules, locks.ForSource(record.Id)));
    }

    private static async Task<IResult> CreateSourceProfile(
        SourceProfileCreateRequest request,
        AnimeGoOptions options,
        AnimeGo.Plugin.Abstractions.PluginCatalog plugins,
        SourceProfileStore profiles,
        MikanRssRuleStore rules,
        LegacyMikanFilterStore legacyFilters,
        SourceRssScheduleManager schedules,
        SourceProfileDeploymentLocks locks,
        IHostApplicationLifetime applicationLifetime,
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
                request.DynamicTagTemplate,
                request.SeedingTimeMinutes,
                request.RssFilterEnabled,
                request.RssPriorityEnabled,
                request.DuplicateNotificationEnabled,
                request.Enabled,
                request.MikanIdentityCookie,
                clearMikanIdentityCookie: false,
                request.RssFeedUrl,
                clearRssFeedUrl: false,
                request.RssScheduleEnabled ?? false,
                request.RssScheduleCron,
                current: null,
                options,
                plugins);
            var now = DateTimeOffset.UtcNow;
            var created = await profiles.CreateAsync(id, definition, now, cancellationToken).ConfigureAwait(false);
            await rules.EnsureDefaultAsync(
                id, MikanRssRuleDefaults.Create(), now, cancellationToken).ConfigureAwait(false);
            if (definition.Adapter == "mikan")
            {
                await legacyFilters.EnsureDefaultAsync(id, now, cancellationToken).ConfigureAwait(false);
            }
            await schedules.ApplyAsync(
                created,
                applicationLifetime.ApplicationStopping).ConfigureAwait(false);
            return TypedResults.Created(
                $"/api/v1/sources/{id}",
                ToResponse(created, schedules, locks.ForSource(created.Id)));
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
        AnimeGo.Plugin.Abstractions.PluginCatalog plugins,
        SourceProfileStore profiles,
        SourceRssScheduleManager schedules,
        SourceProfileDeploymentLocks locks,
        IHostApplicationLifetime applicationLifetime,
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
            if (request.ClearMikanIdentityCookie
                && !string.IsNullOrWhiteSpace(
                    request.MikanIdentityCookie))
            {
                throw new ArgumentException(
                    "mikan_identity_cookie and clear_mikan_identity_cookie cannot both be set.");
            }
            if (request.ClearRssFeedUrl
                && !string.IsNullOrWhiteSpace(request.RssFeedUrl))
            {
                throw new ArgumentException(
                    "rss_feed_url and clear_rss_feed_url cannot both be set.");
            }
            var definition = ToDefinition(
                request.DisplayName,
                current.Adapter,
                request.DownloaderId,
                request.FileStrategy,
                request.AllowedTorrentHosts,
                request.Category,
                request.Tags,
                request.DynamicTagTemplate,
                request.SeedingTimeMinutes,
                request.RssFilterEnabled,
                request.RssPriorityEnabled,
                request.DuplicateNotificationEnabled,
                request.Enabled,
                request.MikanIdentityCookie,
                request.ClearMikanIdentityCookie,
                request.RssFeedUrl,
                request.ClearRssFeedUrl,
                request.RssScheduleEnabled
                    ?? (request.Enabled
                        && !request.ClearRssFeedUrl
                        && current.RssScheduleEnabled),
                request.RssScheduleCron,
                current,
                options,
                plugins);
            var changedLockedFields = new List<string>();
            AddLockedChange("category", current.Category, definition.Category);
            AddLockedChange(
                "dynamic_tag_template",
                current.DynamicTagTemplate,
                definition.DynamicTagTemplate);
            AddLockedChange(
                "mikan_identity_cookie",
                current.MikanIdentityCookie,
                definition.MikanIdentityCookie);
            if (changedLockedFields.Count > 0)
            {
                return TypedResults.BadRequest(Error(
                    "source_profile_field_locked",
                    "Deployment-controlled source fields cannot be changed: "
                    + string.Join(", ", changedLockedFields)));
            }
            var saved = await profiles.UpdateAsync(
                id, definition, request.ExpectedRevision, DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);
            await schedules.ApplyAsync(
                saved,
                applicationLifetime.ApplicationStopping).ConfigureAwait(false);
            return TypedResults.Ok(ToResponse(
                saved,
                schedules,
                locks.ForSource(saved.Id)));

            void AddLockedChange(string field, string? before, string? after)
            {
                if (locks.IsLocked(id, field)
                    && !string.Equals(before, after, StringComparison.Ordinal))
                {
                    changedLockedFields.Add(field);
                }
            }
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
        SourceRssScheduleManager schedules,
        IHostApplicationLifetime applicationLifetime,
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
            await schedules.RemoveAsync(
                id,
                applicationLifetime.ApplicationStopping).ConfigureAwait(false);
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
            profile.DynamicTagTemplate,
            profile.SeedingTimeMinutes,
            profile.RssFilterEnabled,
            profile.RssPriorityEnabled,
            profile.DuplicateNotificationEnabled,
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
            : TypedResults.Ok(await ToResponseAsync(
                profile, snapshot, rules, cancellationToken).ConfigureAwait(false));
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
            return TypedResults.Ok(await ToResponseAsync(
                profile, saved, rules, cancellationToken).ConfigureAwait(false));
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

    private static async Task<IResult> RollbackRssRules(
        string sourceProfileId,
        RssRuleRollbackRequest request,
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
                return TypedResults.NotFound(Error(
                    "rss_rule_set_not_found", "RSS source profile was not found."));
            }
            var saved = await rules.RollbackAsync(
                profileId,
                request.TargetRevision,
                request.ExpectedRevision,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(await ToResponseAsync(
                profile, saved, rules, cancellationToken).ConfigureAwait(false));
        }
        catch (MikanRssRuleRevisionException)
        {
            return TypedResults.Conflict(Error(
                "rss_rule_revision_conflict", "RSS rules changed; reload before rolling back."));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound(Error(
                "rss_rule_snapshot_not_found", "RSS rule snapshot was not found."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("rss_rule_rollback_invalid", exception.Message));
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
        MikanRssTaskEvidenceStore rssEvidence,
        PendingTmdbNfoRewriteStore nfoRewrites,
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
        var rssEntries = await rssEvidence.ListForTaskAsync(taskId, cancellationToken)
            .ConfigureAwait(false);
        var rewriteJobs = await nfoRewrites.ListForTaskAsync(taskId, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(new MetadataTaskDetailResponse(
            ToResponse(item),
            new MetadataTaskSourceEvidenceItem(
                detail.Source.SourceProfileId,
                detail.Source.SourceProfileRevision,
                detail.Source.SourceId,
                detail.Source.SourceTitle,
                detail.Source.SourceItemIdFingerprint,
                detail.Source.SourceWorkIdFingerprint,
                detail.Source.MikanId,
                detail.Source.GroupId,
                detail.Source.BangumiSubjectId,
                detail.Source.AniDbAnimeId,
                detail.Source.ImdbTitleId,
                detail.Source.SourcePublishedAtRawAvailable,
                detail.Source.SourcePublishedAt),
            rssEntries.Select(entry => new MetadataTaskRssEvidenceItem(
                entry.BatchId,
                entry.EntryOrdinal,
                entry.SourceProfileId,
                entry.RuleRevision,
                entry.PriorityEnabled,
                entry.LegacyFilterRevision,
                entry.LegacyFilterEnabled,
                entry.MikanId,
                entry.SourceEpisodeKind,
                entry.SourceEpisode,
                entry.DecisionKind,
                entry.DecisionReason,
                entry.EvaluatedPriorityGroups,
                entry.LegacyFilterState,
                entry.LegacyFilterReason,
                entry.LegacyFilterScope,
                entry.IdentityMikanId,
                entry.IdentityGroupId,
                entry.EffectState,
                entry.BatchCreatedAtUtc)).ToArray(),
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
            rewriteJobs.Select(job => new MetadataTaskNfoRewriteItem(
                job.JobId,
                job.BangumiSubjectId,
                job.TmdbSeriesId,
                job.State,
                job.AttemptCount,
                job.FailureCode,
                job.NextAttemptAtUtc,
                job.UpdatedAtUtc,
                job.CompletedAtUtc)).ToArray(),
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
                file.TmdbEpisodeName,
                file.EpisodeResolution?.Strategy,
                file.EpisodeResolution?.RunId,
                file.EpisodeResolution?.AttemptId)).ToArray()));
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
                    item.ResourceRevision,
                    item.EpisodeTotal,
                    item.EpisodeSnapshotCount,
                    item.EpisodeDownloaded,
                    item.SeriesResolutionSource,
                    item.SeriesResolutionRunId,
                    item.SeriesResolutionAttemptId,
                    item.SeasonResolutionSource,
                    item.SeasonResolutionRunId,
                    item.SeasonResolutionAttemptId,
                    item.ValidationStatus,
                    item.LastResolutionRunId,
                    item.Warnings);
            }).ToArray()));
    }

    private static async Task<IResult> CreateLibrarySeason(
        AnimeSeasonCreateRequest request,
        ITmdbClient tmdb,
        AnimeLibraryAdminStore admin,
        CancellationToken cancellationToken)
    {
        if (request.TmdbSeriesId <= 0)
        {
            return TypedResults.BadRequest(Error(
                "library_series_id_invalid",
                "TMDB Series ID must be a positive integer."));
        }

        if (request.TmdbSeasonNumber <= 0)
        {
            return TypedResults.BadRequest(Error(
                "library_season_number_invalid",
                "TMDB Season number must be a positive integer."));
        }

        try
        {
            var series = await tmdb.GetSeriesAsync(
                request.TmdbSeriesId,
                cancellationToken).ConfigureAwait(false);
            if (series?.Id != request.TmdbSeriesId)
            {
                return TypedResults.NotFound(Error(
                    "library_tmdb_series_not_found",
                    "TMDB TV Series could not be verified."));
            }

            var season = await tmdb.GetSeasonAsync(
                series.Id,
                request.TmdbSeasonNumber,
                cancellationToken).ConfigureAwait(false);
            if (season?.SeriesId != series.Id
                || season.SeasonNumber != request.TmdbSeasonNumber)
            {
                return TypedResults.NotFound(Error(
                    "library_tmdb_season_not_found",
                    "TMDB Season could not be verified."));
            }

            var result = await admin.CreateAsync(
                series,
                season,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            if (result.Status == AnimeLibraryMutationStatus.AlreadyExists)
            {
                return TypedResults.Conflict(Error(
                    "library_season_exists",
                    "The TMDB Season already exists; refresh the existing resource instead."));
            }

            var response = new AnimeSeasonMutationResponse(
                "created",
                result.TmdbSeriesId,
                result.SeasonNumber,
                result.ResourceRevision!);
            return Results.Json(
                response,
                ApiJsonContext.Default.AnimeSeasonMutationResponse,
                statusCode: StatusCodes.Status201Created);
        }
        catch (TmdbClientException exception)
        {
            return LibraryTmdbFailure(exception, "TMDB library creation failed.");
        }
        catch (InvalidOperationException)
        {
            return TypedResults.Conflict(Error(
                "library_tmdb_identity_conflict",
                "TMDB identity conflicts with the existing canonical library."));
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest(Error(
                "library_tmdb_payload_invalid",
                "TMDB returned an invalid Series or Season snapshot."));
        }
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
            season.ResourceRevision,
            season.EpisodeTotal,
            season.EpisodeSnapshotCount,
            season.EpisodeDownloaded,
            season.SeriesResolutionSource,
            season.SeriesResolutionRunId,
            season.SeriesResolutionAttemptId,
            season.SeasonResolutionSource,
            season.SeasonResolutionRunId,
            season.SeasonResolutionAttemptId,
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
                episode.MediaPathKnown)).ToArray(),
            detail.Audit.ManualOffsets.Select(value =>
                new AnimeSeasonManualOffsetResponse(
                    value.MikanId,
                    value.BangumiSubjectId,
                    value.TmdbSeriesId,
                    value.TmdbSeasonNumber,
                    value.EpisodeOffset,
                    value.Enabled,
                    value.Revision,
                    value.UpdatedAtUtc)).ToArray(),
            detail.Audit.RelatedTaskTotal,
            detail.Audit.RelatedTasksTruncated,
            detail.Audit.RelatedTasks.Select(value =>
                new AnimeSeasonRelatedTaskResponse(
                    value.TaskId,
                    value.Title,
                    value.SourceId,
                    value.Status,
                    value.MikanId,
                    value.BangumiSubjectId,
                    value.LatestRunAttemptNumber,
                    value.LatestRunStatus,
                    value.UpdatedAtUtc)).ToArray(),
            detail.Audit.ResolutionAttemptTotal,
            detail.Audit.ResolutionAttemptsTruncated,
            detail.Audit.ResolutionAttempts.Select(value =>
                new AnimeSeasonResolutionAttemptResponse(
                    value.TaskId,
                    value.TaskTitle,
                    value.RunAttemptNumber,
                    value.RunStatus,
                    value.Stage,
                    value.Strategy,
                    value.Priority,
                    value.Result,
                    value.ErrorCode,
                    value.Reason,
                    value.Retryable,
                    value.AttemptNumber,
                    value.DurationMilliseconds,
                    value.CreatedAtUtc)).ToArray()));
    }

    private static async Task<IResult> RefreshLibrarySeason(
        int tmdbSeriesId,
        int seasonNumber,
        AnimeSeasonRefreshRequest request,
        ITmdbClient tmdb,
        AnimeLibraryStore library,
        AnimeLibraryAdminStore admin,
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

        if (!IsLibraryRevision(request.ExpectedRevision))
        {
            return TypedResults.BadRequest(Error(
                "library_revision_invalid",
                "A 64-character resource revision is required."));
        }

        var current = await library.GetSeasonAsync(
            tmdbSeriesId,
            seasonNumber,
            cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return TypedResults.NotFound(Error(
                "library_season_not_found",
                "The requested TMDB season was not found in the local library."));
        }

        if (!string.Equals(
            current.Season.ResourceRevision,
            request.ExpectedRevision,
            StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Conflict(Error(
                "library_revision_conflict",
                "The library season changed; reload it before refreshing."));
        }

        try
        {
            var series = await tmdb.GetSeriesAsync(
                tmdbSeriesId,
                cancellationToken).ConfigureAwait(false);
            if (series?.Id != tmdbSeriesId)
            {
                return TypedResults.NotFound(Error(
                    "library_tmdb_series_not_found",
                    "TMDB TV Series could not be verified."));
            }

            var season = await tmdb.GetSeasonAsync(
                series.Id,
                seasonNumber,
                cancellationToken).ConfigureAwait(false);
            if (season?.SeriesId != series.Id
                || season.SeasonNumber != seasonNumber)
            {
                return TypedResults.NotFound(Error(
                    "library_tmdb_season_not_found",
                    "TMDB Season could not be verified."));
            }

            var result = await admin.RefreshAsync(
                series,
                season,
                request.ExpectedRevision!,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            if (result.Status == AnimeLibraryMutationStatus.NotFound)
            {
                return TypedResults.NotFound(Error(
                    "library_season_not_found",
                    "The requested TMDB season was not found in the local library."));
            }

            if (result.Status == AnimeLibraryMutationStatus.RevisionConflict)
            {
                return TypedResults.Conflict(Error(
                    "library_revision_conflict",
                    "The library season changed; reload it before refreshing."));
            }

            return TypedResults.Ok(new AnimeSeasonMutationResponse(
                "refreshed",
                result.TmdbSeriesId,
                result.SeasonNumber,
                result.ResourceRevision!));
        }
        catch (TmdbClientException exception)
        {
            return LibraryTmdbFailure(exception, "TMDB library refresh failed.");
        }
        catch (InvalidOperationException)
        {
            return TypedResults.Conflict(Error(
                "library_tmdb_identity_conflict",
                "TMDB identity conflicts with the existing canonical library."));
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest(Error(
                "library_tmdb_payload_invalid",
                "TMDB returned an invalid Series or Season snapshot."));
        }
    }

    private static async Task<IResult> DeleteLibrarySeason(
        int tmdbSeriesId,
        int seasonNumber,
        [FromQuery(Name = "expected_revision")] string? expectedRevision,
        AnimeLibraryAdminStore admin,
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

        if (!IsLibraryRevision(expectedRevision))
        {
            return TypedResults.BadRequest(Error(
                "library_revision_invalid",
                "A 64-character resource revision is required."));
        }

        var result = await admin.DeleteAsync(
            tmdbSeriesId,
            seasonNumber,
            expectedRevision!,
            cancellationToken).ConfigureAwait(false);
        return result.Status switch
        {
            AnimeLibraryMutationStatus.Deleted =>
                TypedResults.Ok(new AnimeSeasonDeleteResponse(
                    "deleted",
                    result.TmdbSeriesId,
                    result.SeasonNumber,
                    result.SeriesRemoved)),
            AnimeLibraryMutationStatus.NotFound =>
                TypedResults.NotFound(Error(
                    "library_season_not_found",
                    "The requested TMDB season was not found in the local library.")),
            AnimeLibraryMutationStatus.RevisionConflict =>
                TypedResults.Conflict(Error(
                    "library_revision_conflict",
                    "The library season changed; reload it before deleting.")),
            AnimeLibraryMutationStatus.InUse =>
                TypedResults.Conflict(Error(
                    "library_season_in_use",
                    LibraryReferenceMessage(result.References!))),
            _ => throw new InvalidOperationException(
                "Unexpected library delete result."),
        };
    }

    private static JsonHttpResult<ApiErrorResponse> LibraryTmdbFailure(
        TmdbClientException exception,
        string message)
    {
        var status = exception.Kind is MetadataFailureKind.Network
            or MetadataFailureKind.RemoteService
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status502BadGateway;
        return TypedResults.Json(
            Error(exception.SafeCode, message),
            ApiJsonContext.Default.ApiErrorResponse,
            statusCode: status);
    }

    private static bool IsLibraryRevision(string? value) =>
        value is { Length: 64 }
        && value.All(Uri.IsHexDigit);

    private static string LibraryReferenceMessage(
        AnimeLibraryReferenceSummary references) =>
        "Library projection is still referenced "
        + $"(task files: {references.TaskFiles}, "
        + $"completion records: {references.CompletionRecords}, "
        + $"episode claims: {references.EpisodeClaims}, "
        + $"Mikan work rules: {references.MikanWorkRules}, "
        + $"fallback records: {references.FallbackCompletionRecords}, "
        + $"pending NFO rewrites: {references.PendingNfoRewriteJobs}). "
        + "Use the four-part deletion workflow for business data, downloader tasks, "
        + "source files or media files.";

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

    private static async Task<Results<
        Ok<MikanRssIngestResult>,
        BadRequest<ApiErrorResponse>,
        Conflict<ApiErrorResponse>>> RssIngest(
        RssIngestRequest request,
        AnimeGo.Plugin.Abstractions.PluginCatalog plugins,
        SourceProfileStore profiles,
        MikanRssIngestProcessor processor,
        LegacyDownloaderMigrationState legacyMigration,
        CancellationToken cancellationToken)
    {
        if (legacyMigration.BlockingDiagnostic is { } diagnostic)
        {
            return MigrationBlocked(diagnostic);
        }

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
                sourceProfileId,
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
        LegacyDownloaderMigrationState legacyMigration,
        CancellationToken cancellationToken)
    {
        if (legacyMigration.BlockingDiagnostic is { } diagnostic)
        {
            return TypedResults.Ok(new LegacyApiResponse<MikanRssIngestResult?>(
                300,
                $"{diagnostic.Code}: {diagnostic.Message}",
                null));
        }

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
                "mikan",
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
        string sourceProfileId,
        AnimeGo.Plugin.Abstractions.PluginCatalog plugins,
        CancellationToken cancellationToken)
    {
        var fetched = await plugins
            .Require<AnimeGo.Plugin.Abstractions.IFeedPlugin>("mikan-rss")
            .FetchAsync(
                new AnimeGo.Plugin.Abstractions.FeedContext(
                    sourceProfileId,
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

    private static SourceProfileResponse ToResponse(
        SourceProfileAdminRecord profile,
        SourceRssScheduleManager schedules,
        IReadOnlyList<SourceProfileDeploymentFieldLock> locks)
    {
        var schedule = schedules.Get(profile.Id);
        return new(
            profile.Id,
            profile.DisplayName,
            profile.Adapter,
            profile.DownloaderId,
            profile.FileStrategy,
            profile.AllowedTorrentHosts,
            profile.Category,
            profile.Tags,
            profile.DynamicTagTemplate,
            profile.SeedingTimeMinutes,
            profile.RssFilterEnabled,
            profile.RssPriorityEnabled,
            profile.DuplicateNotificationEnabled,
            profile.Enabled,
            locks.Select(value => new SourceProfileFieldLockResponse(
                value.Field,
                value.Source,
                value.ControllingKeys)).ToArray(),
            profile.MikanIdentityCookie is not null,
            profile.Revision,
            profile.IngestTaskCount,
            profile.RssBatchCount,
            profile.Id == "mikan",
            profile.FileStrategy == "move"
                ? "move transfers completed files and does not preserve seeding."
                : null,
            profile.CreatedAtUtc,
            profile.UpdatedAtUtc,
            profile.RssFeedUrl is not null,
            profile.RssScheduleEnabled,
            profile.RssScheduleCron,
            schedule is not null,
            schedule?.NextTime,
            profile.RssLastRunState,
            profile.RssLastStartedAtUtc,
            profile.RssLastCompletedAtUtc,
            profile.RssLastFailureCode,
            profile.RssLastBatchId);
    }

    private static DownloaderInstanceResponse ToResponse(
        string id,
        QbittorrentInstanceOptions downloader,
        DownloaderUsageRecord usage,
        string configurationSource,
        IReadOnlyList<DownloaderDeploymentFieldLock> locks,
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
            locks.Select(value => new DownloaderFieldLockResponse(
                value.Field,
                value.Source,
                value.EnvironmentVariables
                    .Concat(value.CommandLineArguments)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray())).ToArray(),
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
        string? dynamicTagTemplate,
        int? seedingTimeMinutes,
        bool rssFilterEnabled,
        bool rssPriorityEnabled,
        bool? duplicateNotificationEnabled,
        bool enabled,
        string? mikanIdentityCookie,
        bool clearMikanIdentityCookie,
        string? rssFeedUrl,
        bool clearRssFeedUrl,
        bool rssScheduleEnabled,
        string? rssScheduleCron,
        SourceProfileAdminRecord? current,
        AnimeGoOptions options,
        AnimeGo.Plugin.Abstractions.PluginCatalog plugins)
    {
        var name = displayName?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 128)
        {
            throw new ArgumentException("display_name must contain 1 to 128 characters.");
        }
        var normalizedAdapter = adapter?.Trim().ToLowerInvariant() ?? string.Empty;
        if (plugins.Find<AnimeGo.Plugin.Abstractions.IInputSourceAdapter>(normalizedAdapter) is null
            && (current is null
                || !string.Equals(current.Adapter, normalizedAdapter, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "adapter must reference a registered built-in or external source adapter.");
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
        var normalizedDynamicTagTemplate = dynamicTagTemplate is null && current is not null
            ? current.DynamicTagTemplate
            : DownloadDynamicTagTemplate.Normalize(dynamicTagTemplate);
        var normalizedSeedingTime = SourceDownloadPolicy.ValidateSeedingTimeMinutes(
            normalizedStrategy,
            seedingTimeMinutes
                ?? (current is not null && current.FileStrategy == normalizedStrategy
                    ? current.SeedingTimeMinutes
                    : 0));
        var normalizedMikanIdentityCookie = clearMikanIdentityCookie
            ? null
            : !string.IsNullOrWhiteSpace(mikanIdentityCookie)
            ? MikanIdentityCookie.NormalizeOptional(mikanIdentityCookie)
            : current?.MikanIdentityCookie;
        if (normalizedMikanIdentityCookie is not null
            && normalizedAdapter != "mikan")
        {
            throw new ArgumentException(
                "mikan_identity_cookie can only be configured for a Mikan adapter.");
        }
        var normalizedRssFeedUrl = clearRssFeedUrl
            ? null
            : !string.IsNullOrWhiteSpace(rssFeedUrl)
                ? SourceRssSchedulePolicy.NormalizeFeedUrl(normalizedAdapter, rssFeedUrl)
                : current?.RssFeedUrl;
        if (normalizedRssFeedUrl is not null
            && !TorrentNetworkPolicy.IsHostAllowed(
                new Uri(normalizedRssFeedUrl, UriKind.Absolute).IdnHost,
                hosts))
        {
            throw new ArgumentException(
                "rss_feed_url host must be included in allowed_torrent_hosts.");
        }
        var normalizedRssScheduleCron = SourceRssSchedulePolicy.NormalizeCron(
            rssScheduleCron ?? current?.RssScheduleCron);
        SourceRssSchedulePolicy.ValidateEnabled(
            normalizedAdapter,
            enabled,
            rssScheduleEnabled,
            normalizedRssFeedUrl);
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
            enabled,
            normalizedMikanIdentityCookie,
            normalizedDynamicTagTemplate,
            normalizedRssFeedUrl,
            rssScheduleEnabled,
            normalizedRssScheduleCron,
            duplicateNotificationEnabled
                ?? current?.DuplicateNotificationEnabled
                ?? true);
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

    private static ExternalPluginRuntimeResponse ToResponse(
        ExternalPluginRuntimeSnapshot runtime) =>
        new(
            runtime.PluginId,
            runtime.State switch
            {
                ExternalPluginRuntimeState.Stopped => "stopped",
                ExternalPluginRuntimeState.Starting => "starting",
                ExternalPluginRuntimeState.Ready => "ready",
                ExternalPluginRuntimeState.Backoff => "backoff",
                ExternalPluginRuntimeState.AutoDisabled => "auto_disabled",
                _ => "unknown",
            },
            runtime.ConsecutiveFailures,
            runtime.RetryAtUtc,
            runtime.LastFailureCode);

    private static ExternalPluginConfigurationResponse ToResponse(
        ExternalPluginConfigurationView view) =>
        new(
            view.Package.Manifest.Id,
            view.Package.Manifest.Name,
            view.Package.Manifest.Version,
            view.Package.Manifest.Type,
            view.Package.Manifest.Rid,
            view.Package.Manifest.Capabilities,
            view.Configured,
            view.Enabled,
            view.EntryRevision,
            view.UpdatedAtUtc,
            view.Args,
            view.Vars.Value,
            view.Vars.ConfiguredWriteOnlyPaths,
            view.Schema);

    private static ConfigurationMigrationDiagnosticResponse[] ToResponse(
        LegacyDownloaderMigrationState state) =>
        state.Diagnostics.Select(item => new ConfigurationMigrationDiagnosticResponse(
            item.Code,
            item.Source,
            item.LegacyDownloaderType,
            item.Message,
            item.BlocksDownloads)).ToArray();

    private static Conflict<ApiErrorResponse> MigrationBlocked(
        LegacyConfigurationDiagnostic diagnostic) =>
        TypedResults.Conflict(Error(diagnostic.Code, diagnostic.Message));

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
            item.SeriesResolution?.RunId,
            item.SeriesResolution?.AttemptId,
            item.SeasonResolution?.RunId,
            item.SeasonResolution?.AttemptId,
            item.EpisodeResolution?.RunId,
            item.EpisodeResolution?.AttemptId,
            item.EpisodeResolutionMixed,
            item.FailureKind,
            item.FailureReason,
            item.FailureStage,
            item.FailureCode,
            item.FailureRetryable,
            item.LatestRunStatus,
            item.TmdbAccessConfirmed,
            item.BangumiFallbackEligible,
            item.BangumiFallbackDenialReason,
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
            record.SeedingState,
            record.SeedingTargetMinutes,
            record.SeedingElapsedSeconds,
            record.SeedingCompletedAtUtc,
            record.DynamicTags,
            record.DynamicTagState,
            record.DynamicTagFailureCode,
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

    private static async Task<RssRuleSetResponse> ToResponseAsync(
        SourceProfileRecord profile,
        MikanRssRuleSnapshot snapshot,
        MikanRssRuleStore store,
        CancellationToken cancellationToken)
    {
        var snapshots = await store.ListSnapshotsAsync(
            snapshot.SourceProfileId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new(
            snapshot.SourceProfileId, profile.RssFilterEnabled, profile.RssPriorityEnabled,
            snapshot.Revision,
            snapshot.Rules.Whitelist.Select(ToResponse).ToArray(),
            snapshot.Rules.Blacklist.Select(ToResponse).ToArray(),
            snapshot.Rules.PriorityGroups.Select(group => new RssPriorityGroupResponse(
                group.Id, group.Name, group.Arrays.Select(ToResponse).ToArray())).ToArray(),
            snapshots.Select(item => new RssRuleSnapshotItem(
                item.Revision, item.CreatedAtUtc)).ToArray(),
            snapshot.CreatedAtUtc, snapshot.UpdatedAtUtc);
    }

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
