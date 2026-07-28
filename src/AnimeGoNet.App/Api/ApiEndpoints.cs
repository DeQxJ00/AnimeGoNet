using System.Reflection;
using System.Runtime.CompilerServices;
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
using AnimeGoNet.App.Serialization;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Deletion;
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
        app.MapGet("/api/v1/mikan/trusted-offsets", ListMikanTrustedOffsets);
        app.MapDelete(
            "/api/v1/mikan/trusted-offsets/{mikanId:int}/{groupId:int}",
            ClearMikanTrustedOffset);
        app.MapPost("/api/v1/metadata/tasks/{taskId}/retry", RetryMetadataTask);
        app.MapGet("/api/v1/metadata/tasks", MetadataTasks);
        app.MapGet("/api/v1/metadata/pending-tmdb", PendingTmdbSeries);
        app.MapGet("/api/v1/metadata/pending-tmdb/{bangumiSubjectId:int}", PendingTmdbDetail);
        app.MapPost(
            "/api/v1/metadata/pending-tmdb/{bangumiSubjectId:int}/recover",
            RecoverPendingTmdb);
        app.MapPost("/api/v1/ingest", Ingest);
        app.MapPost("/api/rss", LegacyRss);
        app.MapPost("/api/download/manager", LegacyDownloadManager);
        app.MapPost("/api/plugin/config", LegacyPluginConfigPost);
        app.MapGet("/api/plugin/config", LegacyPluginConfigGet);
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
        ApplicationOverrideStore store,
        ApplicationConfigurationRuntimeState applied,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var desired = ApplicationOverrideStore.Apply(deployment.Value, snapshot);
        return TypedResults.Ok(ToConfigurationResponse(
            options,
            desired,
            snapshot.Settings,
            runtime,
            snapshot.Revision,
            applied.AppliedRevision));
    }

    private static async Task<IResult> PutConfiguration(
        ConfigurationUpdateRequest request,
        DeploymentConfigurationOptions deployment,
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

            var settings = CreateApplicationOverride(request, current.Settings, DateTimeOffset.UtcNow);
            var candidate = ApplicationOverrideStore.Apply(
                deployment.Value,
                new ApplicationOverrideSnapshot(1, current.Revision + 1, settings));
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
        long configurationRevision,
        long appliedConfigurationRevision)
    {
        var tmdb = options.Metadata.Tmdb;
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
                    tmdb.Language,
                    tmdb.HttpTimeout.TotalSeconds,
                    !string.IsNullOrWhiteSpace(tmdb.ApiKey),
                    !string.IsNullOrWhiteSpace(tmdb.ReadAccessToken)),
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
                    ai.UseSeasonMatch,
                    ai.UseEpisodeMatch,
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
            ToEditableConfiguration(desired, settings));
    }

    private static EditableConfigurationResponse ToEditableConfiguration(
        AnimeGoOptions desired,
        ApplicationOverrideEntry? settings)
    {
        var tmdb = desired.Metadata.Tmdb;
        var season = desired.Metadata.SeasonFailure;
        var ai = desired.Metadata.Ai;
        var fetch = desired.TorrentFetch;
        return new EditableConfigurationResponse(
            tmdb.BaseUrl.AbsoluteUri,
            tmdb.Language,
            tmdb.HttpTimeout.TotalSeconds,
            SecretState(settings?.TmdbApiKeyOverridden == true, settings?.TmdbApiKey),
            SecretState(
                settings?.TmdbReadAccessTokenOverridden == true,
                settings?.TmdbReadAccessToken),
            season.Skip,
            season.Backtrace,
            season.UseTitleSeason,
            season.UseFirstSeason,
            ai.UseSeasonMatch,
            ai.UseEpisodeMatch,
            ai.HttpTimeout.TotalSeconds,
            desired.Metadata.TmdbFailureUseBangumi,
            desired.Metadata.MikanTrustedOffsetCacheEnabled,
            fetch.Timeout.TotalSeconds,
            fetch.MaxResponseBytes,
            fetch.MaxRedirects,
            fetch.StagingTtl.TotalSeconds);
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
        if (baseUrl.Length is < 1 or > 2048)
        {
            throw new ArgumentException("tmdb_base_url must contain 1 to 2048 characters.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("tmdb_base_url must be an absolute URL.");
        }

        if (language.Length is < 1 or > 32)
        {
            throw new ArgumentException("tmdb_language must contain 1 to 32 characters.");
        }

        ValidateSeconds(request.TmdbHttpTimeoutSeconds, "tmdb_http_timeout_seconds", 86_400);
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
            request.AiUseSeasonMatch,
            request.AiUseEpisodeMatch,
            request.AiHttpTimeoutSeconds,
            request.TmdbFailureUseBangumi,
            request.MikanTrustedOffsetCacheEnabled,
            request.TorrentHttpTimeoutSeconds,
            request.TorrentMaxResponseBytes,
            request.TorrentMaxRedirects,
            request.TorrentStagingTtlSeconds,
            utcNow);
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

    private static async Task<Ok<DownloadListResponse>> Downloads(
        DownloadJobStore jobs,
        CancellationToken cancellationToken)
    {
        var records = await jobs.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new DownloadListResponse(records.Select(record => new DownloadListItem(
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
            record.DownloaderLastSuccessAtUtc)).ToArray()));
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
        var validation = IngestCommandNormalizer.Normalize(profile.Adapter, command, requireModernMetadata: true);
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

    private static async Task<Ok<MetadataTaskListResponse>> MetadataTasks(
        MetadataResolutionStore resolutions,
        CancellationToken cancellationToken)
    {
        var items = await resolutions.ListTasksAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new MetadataTaskListResponse(items.Select(item => new MetadataTaskListItem(
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
            item.EpisodeFileCount,
            item.OtherFileCount,
            item.DuplicateFileCount,
            item.PendingFileCount,
            item.UpdatedAtUtc)).ToArray()));
    }

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

    private static async Task<Ok<LegacyApiResponse<MikanRssIngestResult?>>> LegacyRss(
        LegacyRssRequest request,
        RssFeedReader reader,
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
            var feed = await reader.ParseUrlAsync(request.Rss.Url, cancellationToken).ConfigureAwait(false);
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
}
