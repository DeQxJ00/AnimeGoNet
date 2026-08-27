using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Compatibility;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.DataUpdate;
using AnimeGoNet.App.Deletion;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Media;
using AnimeGoNet.Core.Sources;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.App.Ingest;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.App.Hosting;
using AnimeGoNet.App.Library;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.App.Notifications;
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
using AnimeGoNet.Data.Notifications;
using AnimeGoNet.Data.Rules;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;
using AnimeGoNet.Data.U2;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace AnimeGoNet.App.Api;

public static class ApiEndpoints
{
    public static void Map(WebApplication app)
    {
        ConfigurationArchiveEndpoints.Map(app);
        app.MapGet("/ping", Ping);
        app.MapGet("/sha256", Sha256);
        app.MapGet("/api/v1/status", Status);
        app.MapPost("/api/v1/runtime/restart", RestartRuntime);
        app.MapGet("/api/v1/ai-test/prompt", GetAiMetadataTestPrompt);
        app.MapGet("/api/v1/configuration/subtitle-ai-prompt", GetSubtitleAiPrompt);
        app.MapPut("/api/v1/configuration/subtitle-ai-prompt", PutSubtitleAiPrompt);
        app.MapDelete("/api/v1/configuration/subtitle-ai-prompt", ResetSubtitleAiPrompt);
        AiTesterApiEndpoints.Map(app);
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
        app.MapPost(
            "/api/v1/config/sections/{section}/preview",
            PreviewConfigurationSection);
        app.MapPut(
            "/api/v1/config/sections/{section}",
            PutConfigurationSection);
        app.MapDelete("/api/v1/config", DeleteConfigurationOverride);
        app.MapGet("/api/v1/cache/buckets", CacheBrowserBuckets);
        app.MapGet("/api/v1/cache/entries", CacheBrowserEntries);
        app.MapGet("/api/v1/cache/entries/{entryId}", GetCacheBrowserEntry);
        app.MapDelete("/api/v1/cache/entries/{entryId}", DeleteCacheBrowserEntry);
        app.MapGet("/api/v1/cache/anidb", GetAnidbTitleCacheStatus);
        app.MapGet("/api/v1/cache/anidb/titles", ListAnidbTitles);
        app.MapPost("/api/v1/cache/anidb/refresh", RefreshAnidbTitleCache);
        app.MapPut("/api/v1/cache/anidb/settings", PutAnidbTitleCacheSettings);
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
        app.MapPost("/api/v1/sources/{sourceProfileId}/rss/run", RunSourceRssNow);
        app.MapGet("/api/v1/rss-rules/{sourceProfileId}", GetRssRules);
        app.MapPut("/api/v1/rss-rules/{sourceProfileId}", PutRssRules);
        app.MapPost("/api/v1/rss-rules/{sourceProfileId}/preview", PreviewRssRules);
        app.MapPost("/api/v1/rss-rules/{sourceProfileId}/rollback", RollbackRssRules);
        app.MapGet("/api/v1/delete/tasks/{taskId}/preview", DeletePreview);
        app.MapPost("/api/v1/delete/tasks/{taskId}", CreateDeleteExecution);
        app.MapPost("/api/v1/delete/tasks/{taskId}/execute", ExecuteDeleteAndWait);
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
        app.MapGet(
            "/api/v1/mikan/manual-series-mappings",
            ListMikanManualSeriesMappings);
        app.MapDelete(
            "/api/v1/mikan/manual-series-mappings/{mikanId:int}/{groupId:int}",
            DeleteMikanManualSeriesMapping);
        app.MapGet("/api/v1/mikan/publish-groups", ListMikanPublishGroups);
        app.MapPut("/api/v1/mikan/publish-groups/{groupId:int}", UpdateMikanPublishGroup);
        app.MapPost("/api/v1/mikan/publish-groups/{groupId:int}/refresh", RefreshMikanPublishGroup);
        app.MapGet(
            "/api/v1/mikan/trusted-offset-blacklist",
            ListMikanTrustedOffsetBlacklist);
        app.MapPost(
            "/api/v1/mikan/trusted-offset-blacklist",
            AddMikanTrustedOffsetBlacklist);
        app.MapDelete(
            "/api/v1/mikan/trusted-offset-blacklist",
            RemoveMikanTrustedOffsetBlacklist);
        app.MapGet("/api/v1/mikan/legacy-filter", GetLegacyMikanFilter);
        app.MapPut("/api/v1/mikan/legacy-filter", PutLegacyMikanFilter);
        app.MapPost("/api/v1/mikan/legacy-filter/import", ImportLegacyMikanFilter);
        app.MapPost("/api/v1/mikan/legacy-filter/rollback", RollbackLegacyMikanFilter);
        app.MapPost("/api/v1/mikan/legacy-filter/preview", PreviewLegacyMikanFilter);
        app.MapPost("/api/v1/metadata/tasks/{taskId}/retry", RetryMetadataTask);
        app.MapGet(
            "/api/v1/metadata/tasks/{taskId}/other-readaptation/preview",
            PreviewOtherFileReadaptation);
        app.MapPost(
            "/api/v1/metadata/tasks/{taskId}/other-readaptation",
            StartOtherFileReadaptation);
        app.MapPost(
            "/api/v1/metadata/tasks/{taskId}/other-attention/ignore",
            IgnoreOtherAttention);
        app.MapGet(
            "/api/v1/metadata/tasks/{taskId}/mixed-media-postprocess/preview",
            PreviewMixedMediaPostprocess);
        app.MapPost(
            "/api/v1/metadata/tasks/{taskId}/mixed-media-postprocess",
            StartMixedMediaPostprocess);
        app.MapGet("/api/v1/tmdb/movies/search", SearchTmdbMovies);
        app.MapPost(
            "/api/v1/metadata/tasks/{taskId}/other-readaptation/review",
            ApproveOtherFileReadaptationReview);
        app.MapGet(
            "/api/v1/metadata/tasks/{taskId}/other-readaptation/review",
            PreviewOtherFileReadaptationReview);
        app.MapPost(
            "/api/v1/metadata/tasks/{taskId}/ai-series-change-review/accept",
            AcceptAiSeriesChangeReview);
        app.MapPost(
            "/api/v1/metadata/tasks/{taskId}/ai-series-change-review/reject",
            RejectAiSeriesChangeReview);
        app.MapPost(
            "/api/v1/metadata/tasks/{taskId}/other-readaptation/review/files/{taskFileId}/manual-override",
            ApplyOtherFileReadaptationManualOverride);
        app.MapGet("/api/v1/metadata/tasks", MetadataTasks);
        app.MapGet("/api/v1/metadata/tasks/{taskId}", MetadataTaskDetail);
        app.MapGet("/api/v1/metadata/tasks/{taskId}/attempts", MetadataTaskAttempts);
        app.MapGet("/api/v1/logs/ai-invocations", AiInvocationLogs);
        app.MapGet("/api/v1/logs/mikan-plugin-calls", ListMikanPluginCallLogs);
        app.MapGet("/api/v1/logs/u2-plugin-calls", ListU2PluginCallLogs);
        app.MapGet("/api/v1/logs/ai-invocations/{runId}/debug", AiInvocationDebug);
        app.MapDelete("/api/v1/logs/ai-invocations/{runId}/debug", DeleteAiInvocationDebug);
        app.MapGet("/api/v1/notifications/channels", ListNotificationChannels);
        app.MapPost("/api/v1/notifications/channels", CreateNotificationChannel);
        app.MapPut("/api/v1/notifications/channels/{channelId}", UpdateNotificationChannel);
        app.MapDelete("/api/v1/notifications/channels/{channelId}", DeleteNotificationChannel);
        app.MapPost("/api/v1/notifications/channels/{channelId}/test", TestNotificationChannel);
        app.MapGet("/api/v1/notifications/deliveries", ListNotificationDeliveries);
        app.MapGet("/api/v1/library/seasons", LibrarySeasons);
        app.MapGet("/api/v1/library/movies", LibraryMovies);
        app.MapPost("/api/v1/library/seasons", CreateLibrarySeason);
        app.MapPost("/api/v1/library/external-media/import", ImportExternalMedia);
        app.MapPost("/api/v1/library/subtitle-archives/import", ImportSubtitleArchive);
        app.MapPost("/api/v1/library/subtitle-archives/{sessionId}/ai-match", AiMatchSubtitleArchive);
        app.MapPost("/api/v1/library/subtitle-archives/{sessionId}/confirm", ConfirmSubtitleArchive);
        app.MapGet("/api/v1/library/directory-database", DirectoryDatabaseStatus);
        app.MapPost("/api/v1/library/directory-database/refresh", RefreshDirectoryDatabase);
        app.MapGet("/api/v1/data-update", GetDataUpdateStatus);
        app.MapGet(
            "/api/v1/data-update/archive-usage",
            ListBangumiArchiveUsage);
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
        app.MapPost(
            "/api/v1/library/seasons/{tmdbSeriesId:int}/{seasonNumber:int}/external-media/import",
            ImportExternalMediaSeason);
        app.MapPost(
            "/api/v1/library/seasons/{tmdbSeriesId:int}/{seasonNumber:int}/mikan-completion/preview",
            PreviewMikanSeasonCompletion);
        app.MapPost(
            "/api/v1/library/seasons/{tmdbSeriesId:int}/{seasonNumber:int}/mikan-completion/groups",
            DiscoverMikanSeasonCompletionGroups);
        app.MapPost(
            "/api/v1/library/seasons/{tmdbSeriesId:int}/{seasonNumber:int}/mikan-completion",
            ConfirmMikanSeasonCompletion);
        app.MapGet(
            "/api/v1/library/covers/{tmdbSeriesId:int}/{seasonNumber:int}",
            LibraryCover);
        app.MapGet("/api/v1/library/movie-covers/{tmdbMovieId:int}", LibraryMovieCover);
        app.MapGet("/api/v1/metadata/pending-tmdb", PendingTmdbSeries);
        app.MapGet("/api/v1/metadata/pending-tmdb/{bangumiSubjectId:int}", PendingTmdbDetail);
        app.MapPost(
            "/api/v1/metadata/pending-tmdb/{bangumiSubjectId:int}/recover",
            RecoverPendingTmdb);
        app.MapPost("/api/v1/ingest", Ingest);
        app.MapPost("/api/v1/plugins/inner_plugin_u2/ingest", IngestU2Plugin);
        app.MapPost("/api/v1/ingest/manual", Ingest);
        app.MapPost("/api/v1/ingest/mikan/resolve", ResolveMikanEpisode);
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

    private static async Task<IResult> RunAiMetadataTest(
        AiMetadataTestRequest request,
        IAiMetadataMatcher matcher,
        AnimeGoOptions applicationOptions,
        AiMetadataResultValidator validator,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        var files = request.Files;
        var torrentFileCount = request.TorrentFileCount ?? files?.Count ?? 0;
        if (string.IsNullOrWhiteSpace(title)
            || title.Length > 1000
            || files is null
            || files.Count is < 1 or > 200
            || files.Any(file => string.IsNullOrWhiteSpace(file.Name)
                || file.Name.Length > 1000
                || file.SizeBytes < 0)
            || torrentFileCount < files.Count
            || request.BangumiSubjectId is <= 0
            || request.AniDbAnimeId is <= 0
            || request.ExpectedTmdbId is <= 0
            || request.ExpectedSeason is < 0
            || request.ImdbTitleId?.Length > 32
            || request.PromptTemplate?.Length > AiMetadataPromptRenderer.MaximumTemplateLength
            || request.AiApiKey?.Length > 16_384
            || request.AiModel?.Length > 200
            || !IsOptionalAiApiMode(request.ApiMode)
            || !IsOptionalReasoningEffort(request.ReasoningEffort)
            || (request.WebSearchEnabled == true
                && ParseAiApiMode(request.ApiMode) == AiApiMode.ChatCompletions)
            || request.AiHttpTimeoutSeconds is < 1 or > 3600
            || request.AiRetryCount is < 0 or > 10
            || !IsOptionalHttpUrl(request.AiBaseUrl)
            || !IsOptionalHttpUrl(request.HttpProxyUrl)
            || !IsOptionalHttpUrl(request.TmdbMcpUrl)
            || !IsOptionalHttpUrl(request.BangumiMcpUrl)
            || (request.UseBangumiPubDateFirst
                && (torrentFileCount != 1
                    || request.BangumiSubjectId is null
                    || request.PublishedAt is null
                    || request.BangumiEpisodeCandidate is null)))
        {
            return TypedResults.BadRequest(Error(
                "ai_test_input_invalid",
                "Title, file list, identifiers or expected TMDB values are invalid."));
        }

        var input = new AiMetadataMatchInput(
            title,
            files.Select(file => new AiMetadataFileInput(file.Name!.Trim(), file.SizeBytes)).ToArray(),
            request.BangumiSubjectId,
            request.AniDbAnimeId,
            string.IsNullOrWhiteSpace(request.ImdbTitleId) ? null : request.ImdbTitleId.Trim(),
            torrentFileCount,
            request.PublishedAt,
            request.BangumiEpisodeCandidate,
            request.UseBangumiPubDateFirst)
        {
            PromptTemplateOverride = string.IsNullOrWhiteSpace(request.PromptTemplate)
                ? null
                : request.PromptTemplate,
            PromptFeaturesOverride = new AiMetadataPromptFeatures(
                request.EnableTmdbMcp ?? true,
                request.EnableBangumiMcp ?? true,
                request.EnableAniDbLookup ?? true,
                request.UseBangumiPubDateFirst)
            {
                ImdbLookup = true,
            },
        };
        var effectiveFeatures = ToAiTestFeatures(AiMetadataPromptFeatures.Resolve(input));
        string prompt;
        try
        {
            prompt = AiMetadataPromptRenderer.LoadAndRender(input);
        }
        catch (AiMetadataMatcherException exception)
        {
            return TypedResults.BadRequest(Error(exception.SafeCode, "Prompt template is invalid."));
        }
        var timer = Stopwatch.StartNew();
        var effectiveApiMode = ParseAiApiMode(request.ApiMode)
            ?? applicationOptions.Metadata.Ai.ApiMode;
        using var requestMatcher = CreateAiTestMatcher(request, applicationOptions);
        var effectiveMatcher = requestMatcher ?? matcher;
        try
        {
            var match = await effectiveMatcher.MatchAsync(input, cancellationToken).ConfigureAwait(false);
            var validation = await validator.ValidateAsync(
                input,
                match.Candidate,
                request.ExpectedTmdbId,
                request.ExpectedSeason,
                cancellationToken).ConfigureAwait(false);
            timer.Stop();
            var trace = match.Trace.Select(item => new AiMetadataTestTraceItem(
                item.Sequence,
                item.Stage,
                item.Detail,
                item.DurationMilliseconds)).ToList();
            trace.Add(new AiMetadataTestTraceItem(
                trace.Count + 1,
                "tmdb_validation",
                validation.IsSuccess
                    ? "candidate verified against TMDB"
                    : $"failed: {validation.Failure?.Code ?? "unknown"}",
                null));
            return TypedResults.Ok(new AiMetadataTestResponse(
                validation.IsSuccess,
                AiMetadataPromptRenderer.PromptVersion,
                FormatAiApiMode(effectiveApiMode),
                prompt,
                match.RawOutput,
                match.Candidate,
                ToAiTestValidationResponse(validation),
                ToAiTestUsage(match.Usage),
                timer.ElapsedMilliseconds,
                validation.Failure?.Kind.ToString().ToLowerInvariant(),
                validation.Failure?.Code,
                effectiveFeatures,
                trace));
        }
        catch (AiMetadataMatcherException exception)
        {
            timer.Stop();
            return TypedResults.Ok(new AiMetadataTestResponse(
                false,
                AiMetadataPromptRenderer.PromptVersion,
                FormatAiApiMode(effectiveApiMode),
                prompt,
                null,
                null,
                null,
                ToAiTestUsage(exception.Usage),
                timer.ElapsedMilliseconds,
                exception.Kind.ToString().ToLowerInvariant(),
                exception.SafeCode,
                effectiveFeatures,
                [new AiMetadataTestTraceItem(
                    1,
                    "matcher_failed",
                    exception.SafeCode,
                    timer.ElapsedMilliseconds)]));
        }
    }

    private static Ok<AiMetadataTestPromptResponse> GetAiMetadataTestPrompt(
        AnimeGoOptions options) =>
        TypedResults.Ok(new AiMetadataTestPromptResponse(
            AiMetadataPromptRenderer.PromptVersion,
            options.Metadata.Ai.PromptTemplate ?? AiMetadataPromptRenderer.LoadTemplate(),
            AiMetadataPromptRenderer.MaximumTemplateLength,
            AiMetadataPromptRenderer.LoadTemplate(),
            options.Metadata.Ai.PromptTemplate is not null
                && !string.Equals(
                    options.Metadata.Ai.PromptTemplate,
                    AiMetadataPromptRenderer.LoadTemplate(),
                    StringComparison.Ordinal)));

    private static AiMetadataTestFeatureResponse ToAiTestFeatures(
        AiMetadataPromptFeatures features) =>
        new(
            features.TmdbMcp,
            features.BangumiMcp,
            features.AniDbLookup,
            features.ImdbLookup,
            features.BangumiPubDateFirst);

    private static OpenAiCompatibleMetadataMatcher? CreateAiTestMatcher(
        AiMetadataTestRequest request,
        AnimeGoOptions applicationOptions)
    {
        if (!HasValue(request.AiBaseUrl)
            && !HasValue(request.AiApiKey)
            && !HasValue(request.AiModel)
            && !HasValue(request.ApiMode)
            && !HasValue(request.ReasoningEffort)
            && request.WebSearchEnabled is null
            && request.AiHttpTimeoutSeconds is null
            && request.AiRetryCount is null
            && !HasValue(request.HttpProxyUrl)
            && !HasValue(request.TmdbMcpUrl)
            && !HasValue(request.BangumiMcpUrl))
        {
            return null;
        }

        var current = applicationOptions.Metadata.Ai;
        var aiOptions = current with
        {
            BaseUrl = ParseOptionalHttpUrl(request.AiBaseUrl) ?? current.BaseUrl,
            ApiKey = HasValue(request.AiApiKey) ? request.AiApiKey!.Trim() : current.ApiKey,
            Model = HasValue(request.AiModel) ? request.AiModel!.Trim() : current.Model,
            ApiMode = ParseAiApiMode(request.ApiMode) ?? current.ApiMode,
            ReasoningEffort = ParseReasoningEffort(request.ReasoningEffort, current.ReasoningEffort),
            WebSearchEnabled = request.WebSearchEnabled ?? current.WebSearchEnabled,
            HttpTimeout = request.AiHttpTimeoutSeconds is { } timeout
                ? TimeSpan.FromSeconds(timeout)
                : current.HttpTimeout,
            RetryCount = request.AiRetryCount ?? current.RetryCount,
            TmdbMcpUrl = ParseOptionalHttpUrl(request.TmdbMcpUrl) ?? current.TmdbMcpUrl,
            BangumiMcpUrl = ParseOptionalHttpUrl(request.BangumiMcpUrl) ?? current.BangumiMcpUrl,
        };
        var proxy = ParseOptionalHttpUrl(request.HttpProxyUrl);
        HttpClient CreateClient()
        {
            if (proxy is null)
            {
                return AnimeGoNet.App.Networking.OutboundHttpClientFactory.Create(
                    applicationOptions.OutboundProxy);
            }

            return new HttpClient(new HttpClientHandler
            {
                UseProxy = true,
                Proxy = new System.Net.WebProxy(proxy),
            })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
        }

        return new OpenAiCompatibleMetadataMatcher(
            CreateClient(),
            aiOptions,
            ownsHttpClient: true,
            referenceHttpClient: CreateClient(),
            ownsReferenceHttpClient: true);
    }

    private static async Task<Ok<SubtitleAiPromptSettings>> GetSubtitleAiPrompt(
        SubtitleAiPromptStore prompts,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await prompts.GetSettingsAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> PutSubtitleAiPrompt(
        SubtitleAiPromptUpdate request,
        SubtitleAiPromptStore prompts,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Template))
        {
            return TypedResults.BadRequest(Error("subtitle_ai_prompt_invalid", "字幕 AI Prompt 不能为空。"));
        }

        try
        {
            return TypedResults.Ok(await prompts.SaveAsync(request.Template, cancellationToken).ConfigureAwait(false));
        }
        catch (AiMetadataMatcherException exception)
        {
            return TypedResults.BadRequest(Error(exception.SafeCode, "字幕 AI Prompt 校验失败。"));
        }
    }

    private static async Task<Ok<SubtitleAiPromptSettings>> ResetSubtitleAiPrompt(
        SubtitleAiPromptStore prompts,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await prompts.ResetAsync(cancellationToken).ConfigureAwait(false));

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool IsOptionalAiApiMode(string? value) =>
        !HasValue(value) || ParseAiApiMode(value) is not null;

    private static AiApiMode? ParseAiApiMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "responses" => AiApiMode.Responses,
            "chat-completions" => AiApiMode.ChatCompletions,
            null or "" => null,
            _ => null,
        };

    private static string FormatAiApiMode(AiApiMode value) =>
        value == AiApiMode.Responses ? "responses" : "chat-completions";

    private static bool IsOptionalReasoningEffort(string? value) =>
        !HasValue(value)
        || value!.Trim().ToLowerInvariant() is "none" or "low" or "medium" or "high";

    private static string? ParseReasoningEffort(string? value, string? inherited)
    {
        if (!HasValue(value))
        {
            return inherited;
        }

        var normalized = value!.Trim().ToLowerInvariant();
        return normalized == "none" ? null : normalized;
    }

    private static bool IsOptionalHttpUrl(string? value) =>
        !HasValue(value) || ParseOptionalHttpUrl(value) is not null;

    private static Uri? ParseOptionalHttpUrl(string? value)
    {
        if (!HasValue(value))
        {
            return null;
        }

        return Uri.TryCreate(value!.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
    }

    private static async Task<IResult> ImportAiMetadataTestMikanEpisode(
        AiMetadataTestMikanImportRequest request,
        MikanAiTestImportService importer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EpisodeUrl)
            || request.EpisodeUrl.Length > 2048)
        {
            return TypedResults.BadRequest(Error(
                "ai_test_mikan_episode_url_invalid",
                "Mikan Episode URL is required."));
        }

        try
        {
            var result = await importer.ImportAsync(
                request.EpisodeUrl,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new AiMetadataTestMikanImportResponse(
                result.Title,
                result.MikanId,
                result.GroupId,
                result.BangumiSubjectId,
                result.PublishedAt,
                result.TorrentFileCount,
                result.VideoFiles.Select(file => new AiMetadataTestFileResponse(
                    file.Name,
                    file.SizeBytes)).ToArray()));
        }
        catch (MikanAiTestImportException exception)
        {
            return TypedResults.BadRequest(Error(exception.Code, exception.Message));
        }
    }

    private static AiMetadataTestUsageResponse? ToAiTestUsage(AiMetadataProviderUsage? usage) =>
        usage is null
            ? null
            : new AiMetadataTestUsageResponse(
                usage.Model,
                usage.PromptTokens,
                usage.CompletionTokens,
                usage.TotalTokens,
                usage.RequestCount,
                usage.ToolCallCount,
                usage.ReasoningTokens);

    internal static AiMetadataTestValidationResponse ToAiTestValidationResponse(
        AiMetadataValidationResult validation)
    {
        var value = validation.Value;
        var failure = validation.Failure;
        return new AiMetadataTestValidationResponse(
            validation.IsSuccess,
            value?.Series.Id,
            value?.Series.Name,
            failure?.Kind.ToString().ToLowerInvariant(),
            failure?.Code,
            failure?.TmdbAccessConfirmed,
            value?.Files.Select(file => new AiMetadataTestValidatedFile(
                file.Input.Name,
                file.Season.SeasonNumber,
                file.Episode?.EpisodeNumber,
                file.Episode?.Name,
                file.OtherReason)).ToArray() ?? []);
    }

    private static Ok<LegacyApiResponse<string>> Sha256(
        [FromQuery(Name = "access_key")] string accessKey)
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
          "web": {
            "host": "WebUI 监听 IP、DNS 主机名或 IPv6 地址；127.0.0.1 仅本机，0.0.0.0 监听全部 IPv4",
            "port": "WebUI 监听端口，范围 0-65535；0 由系统分配临时端口",
            "webui_access_key": "WebUI 管理接口访问密钥；可留空"
          },
            "inner_plugin_mikan": {
            "access_key": "AnimeGoHelper (Mikan) 与统一导入 API 访问密钥"
          },
          "inner_plugin_u2": {
            "access_key": "AnimeGoHelper (U2) 专用导入 API 访问密钥"
          },
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
        BangumiArchiveStore bangumiArchive,
        CancellationToken cancellationToken)
    {
        var package = await packages.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var downloads = await transfers.ListDownloadsAsync(cancellationToken).ConfigureAwait(false);
        var transfer = await transfers.GetLastRunAsync(cancellationToken).ConfigureAwait(false);
        var usage = await bangumiArchive.GetUsageAsync(cancellationToken).ConfigureAwait(false);
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
                    transfer.CompletedAtUtc),
            new BangumiArchiveUsageResponse(
                usage.TotalHits,
                usage.SubjectHits,
                usage.EpisodeHits,
                usage.RelationHits,
                usage.LastHitAtUtc)));
    }

    private static async Task<Ok<AnidbTitleCacheStatusResponse>> GetAnidbTitleCacheStatus(
        AnidbTitleCacheStore store,
        DirectoryLayout layout,
        CancellationToken cancellationToken)
    {
        var status = await store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new AnidbTitleCacheStatusResponse(
            status.SourceUrl,
            Path.Combine(layout.CachePath, "anidb", "anime-titles.xml.gz"),
            status.RefreshIntervalHours,
            status.LastAttemptAtUtc,
            status.DownloadedAtUtc,
            status.ImportedAtUtc,
            status.NextCheckAtUtc,
            status.AnimeCount,
            status.TitleCount,
            status.SourceSizeBytes,
            status.LastStatus,
            status.LastFailureCode));
    }

    private static async Task<IResult> ListAnidbTitles(
        [FromQuery] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery] string? query,
        [FromQuery] int? aid,
        AnidbTitleCacheStore store,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await store.ListAsync(
                page ?? 1, pageSize ?? 25, query, aid, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new AnidbTitleCacheListResponse(
                result.Page,
                result.PageSize,
                result.TotalItems,
                result.Query,
                result.Aid,
                result.Items.Select(item => new AnidbTitleCacheEntryResponse(
                    item.Aid, item.Language, item.TitleType, item.Title)).ToArray()));
        }
        catch (ArgumentOutOfRangeException)
        {
            return TypedResults.BadRequest(Error(
                "anidb_title_query_invalid",
                "page and aid must be positive; page_size must be between 1 and 100."));
        }
    }

    private static async Task<IResult> RefreshAnidbTitleCache(
        IAnidbTitleCacheService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.RefreshAsync(force: true, cancellationToken)
                .ConfigureAwait(false);
            return TypedResults.Ok(new AnidbTitleCacheRefreshResponse(
                result.Status,
                result.AnimeCount,
                result.TitleCount,
                result.SourceSizeBytes,
                result.NextCheckAtUtc));
        }
        catch (AnidbTitleCacheException exception)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: exception.Code,
                detail: exception.Message);
        }
    }

    private static async Task<IResult> PutAnidbTitleCacheSettings(
        AnidbTitleCacheSettingsRequest request,
        AnidbTitleCacheStore store,
        DirectoryLayout layout,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await store.SetRefreshIntervalHoursAsync(
                request.RefreshIntervalHours,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new AnidbTitleCacheStatusResponse(
                status.SourceUrl,
                Path.Combine(layout.CachePath, "anidb", "anime-titles.xml.gz"),
                status.RefreshIntervalHours,
                status.LastAttemptAtUtc,
                status.DownloadedAtUtc,
                status.ImportedAtUtc,
                status.NextCheckAtUtc,
                status.AnimeCount,
                status.TitleCount,
                status.SourceSizeBytes,
                status.LastStatus,
                status.LastFailureCode));
        }
        catch (ArgumentOutOfRangeException)
        {
            return TypedResults.BadRequest(Error(
                "anidb_title_interval_invalid",
                "refresh_interval_hours must be between 1 and 720."));
        }
    }

    private static async Task<IResult> ListBangumiArchiveUsage(
        [FromQuery] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery(Name = "hit_kind")] string? hitKind,
        BangumiArchiveStore bangumiArchive,
        CancellationToken cancellationToken)
    {
        var resolvedPage = page ?? 1;
        var resolvedPageSize = pageSize ?? 25;
        if (resolvedPage < 1 || resolvedPageSize is < 1 or > 100)
        {
            return TypedResults.BadRequest(Error(
                "bangumi_archive_usage_pagination_invalid",
                "Bangumi archive usage page must be positive and page_size must be between 1 and 100."));
        }

        try
        {
            var result = await bangumiArchive.ListUsageEventsAsync(
                resolvedPage,
                resolvedPageSize,
                hitKind,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new BangumiArchiveUsageListResponse(
                result.Page,
                result.PageSize,
                result.TotalItems,
                result.HitKind,
                result.Items.Select(item => new BangumiArchiveUsageEventResponse(
                    item.Id,
                    item.DataVersion,
                    item.HitKind,
                    item.SubjectId,
                    item.ResultCount,
                    item.HitAtUtc)).ToArray()));
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest(Error(
                "bangumi_archive_usage_kind_invalid",
                "Bangumi archive usage hit_kind must be subject, episodes or relations."));
        }
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
        return normalized is "inner_plugin_mikan"
            or "filter/mikan_tool.py" or "filter/mikan_tool"
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
                    bucket.BucketName,
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
                    item.Key,
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
        Ok<CacheBrowserEntryDetailResponse>,
        BadRequest<ApiErrorResponse>,
        NotFound<ApiErrorResponse>>> GetCacheBrowserEntry(
        string entryId,
        [FromQuery] string? database,
        [FromQuery(Name = "bucket_id")] string? bucketId,
        SqliteJsonCacheStore store,
        CancellationToken cancellationToken)
    {
        var normalizedDatabase = string.IsNullOrWhiteSpace(database)
            ? "bolt"
            : database.Trim().ToLowerInvariant();
        try
        {
            var result = await store.GetBrowserEntryAsync(
                normalizedDatabase,
                bucketId ?? string.Empty,
                entryId,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                return TypedResults.NotFound(Error(
                    "cache_entry_not_found",
                    "Cache entry does not exist."));
            }

            return TypedResults.Ok(new CacheBrowserEntryDetailResponse(
                normalizedDatabase,
                string.Equals(normalizedDatabase, "bolt_sub", StringComparison.Ordinal),
                result.BucketId,
                result.BucketName,
                result.EntryId,
                result.Key,
                result.ValueJson,
                result.ValueBytes,
                result.ExpiresAtUtc,
                result.UpdatedAtUtc));
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

    private static async Task<Ok<RuntimeStatus>> Status(
        AnimeGoOptions options,
        LegacyDownloaderMigrationState legacyMigration,
        ExternalPluginDiscoveryResult externalPlugins,
        ExternalPluginHostManager externalPluginHost,
        ExternalPluginConfigurationService externalPluginConfigurations,
        RuntimeResourceMetricsService resourceMetrics,
        CancellationToken cancellationToken)
    {
        var resources = await resourceMetrics.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        return TypedResults.Ok(new RuntimeStatus(
            version,
            DatabaseSchema.CurrentVersion,
            !RuntimeFeature.IsDynamicCodeSupported,
            System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            new RuntimePaths(
                options.Paths.DataPath,
                options.Paths.DownloadPath,
                options.Paths.SavePath,
                options.Paths.EffectiveMovieSavePath),
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
                    ToResponse(runtime)).ToArray()),
            new RuntimeResourceStatusResponse(
                resources.WorkingSetBytes,
                resources.CpuPercent,
                resources.LogicalProcessorCount,
                resources.DataPathBytes,
                resources.DataPathScannedAtUtc,
                resources.DataPathScanComplete)));
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
            var (_, candidate) = BuildConfigurationCandidate(
                request,
                current,
                deployment.Value,
                locks);
            var changes = ConfigurationChanges(
                request,
                currentDesired,
                candidate);
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

    private static async Task<IResult> PreviewConfigurationSection(
        string section,
        ConfigurationUpdateRequest request,
        DeploymentConfigurationOptions deployment,
        DeploymentConfigurationLocks locks,
        ApplicationOverrideStore store,
        AnimeGoOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (current.Revision != request.ExpectedConfigurationRevision)
            {
                throw new ApplicationOverrideRevisionException();
            }
            var currentDesired = locks.Reapply(
                deployment.Value,
                ApplicationOverrideStore.Apply(deployment.Value, current));
            var merged = MergeConfigurationSectionRequest(section, request, currentDesired);
            return await PreviewConfiguration(
                merged,
                deployment,
                locks,
                store,
                options,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ApplicationOverrideRevisionException)
        {
            return TypedResults.Conflict(Error(
                "configuration_revision_conflict",
                "Configuration changed concurrently; reload before previewing."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("configuration_section_invalid", exception.Message));
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

    private static async Task<IResult> PutConfigurationSection(
        string section,
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
            if (current.Revision != request.ExpectedConfigurationRevision)
            {
                throw new ApplicationOverrideRevisionException();
            }
            var currentDesired = locks.Reapply(
                deployment.Value,
                ApplicationOverrideStore.Apply(deployment.Value, current));
            var merged = MergeConfigurationSectionRequest(section, request, currentDesired);
            return await PutConfiguration(
                merged,
                deployment,
                locks,
                store,
                applied,
                options,
                dataUpdateSchedules,
                applicationLifetime,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ApplicationOverrideRevisionException)
        {
            return TypedResults.Conflict(Error(
                "configuration_revision_conflict",
                "Configuration changed concurrently; reload before saving."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("configuration_section_invalid", exception.Message));
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
        var mikan = options.Metadata.Mikan;
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
                options.Paths.SavePath,
                options.Paths.EffectiveMovieSavePath),
            new DeploymentConfigurationResponse(
                runtime.RunningInContainer,
                runtime.BackgroundWorkersEnabled,
                runtime.InnerPluginMikanAccessKeyConfigured,
                runtime.InnerPluginU2AccessKeyConfigured,
                runtime.WebUiAccessKeyConfigured,
                options.Web.Host,
                options.Web.Port,
                PathsRestartRequired: true),
            new OutboundProxyConfigurationResponse(
                options.OutboundProxy.Url?.AbsoluteUri,
                options.OutboundProxy.HostPatterns),
            new MetadataConfigurationResponse(
                new MikanConfigurationResponse(
                    mikan.BaseUrl.AbsoluteUri,
                    mikan.EpisodeIdentityCacheTtl.TotalHours,
                    mikan.BangumiIdentityCacheTtl.TotalHours),
                new TmdbConfigurationResponse(
                    tmdb.BaseUrl.AbsoluteUri,
                    tmdb.ImageBaseUrl.AbsoluteUri,
                    tmdb.Language,
                    tmdb.HttpTimeout.TotalSeconds,
                    tmdb.RetryCount,
                    tmdb.RetryDelay.TotalSeconds,
                    tmdb.CacheTtl.TotalHours,
                    !string.IsNullOrWhiteSpace(tmdb.ApiKey),
                    !string.IsNullOrWhiteSpace(tmdb.ReadAccessToken)),
                new BangumiConfigurationResponse(
                    bangumi.BaseUrl.AbsoluteUri,
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
                    AiMetadataPromptRenderer.PromptVersion,
                    ai.PromptTemplate is not null
                        && !string.Equals(
                            ai.PromptTemplate,
                            AiMetadataPromptRenderer.LoadTemplate(),
                            StringComparison.Ordinal),
                    !string.IsNullOrWhiteSpace(ai.ApiKey),
                    ai.UseMetadataMatch,
                    ai.UseMetadataMatch,
                    ai.UseMetadataMatch,
                    ai.DebugMode,
                    ai.HttpTimeout.TotalSeconds,
                    ai.RetryCount,
                    ai.UseBangumiPubDateFirst,
                    ai.TmdbMcpUrl.AbsoluteUri,
                    ai.BangumiMcpUrl.AbsoluteUri,
                    ai.ReasoningEffort ?? "none",
                    FormatAiApiMode(ai.ApiMode),
                    ai.WebSearchEnabled),
                options.Metadata.TmdbFailureUseBangumi,
                options.Metadata.WriteBangumiIdWhenTmdbMatched,
                options.Metadata.MikanTrustedOffsetCacheEnabled,
                options.Metadata.MikanTrustedOffsetRequiredEpisodes),
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
        var mikan = desired.Metadata.Mikan;
        var tmdb = desired.Metadata.Tmdb;
        var bangumi = desired.Metadata.Bangumi;
        var season = desired.Metadata.SeasonFailure;
        var ai = desired.Metadata.Ai;
        var fetch = desired.TorrentFetch;
        var dataUpdate = desired.DataUpdate;
        return new EditableConfigurationResponse(
            desired.Paths.DownloadPath,
            desired.Paths.SavePath,
            desired.OutboundProxy.Url?.AbsoluteUri,
            desired.OutboundProxy.HostPatterns,
            mikan.BaseUrl.AbsoluteUri,
            mikan.EpisodeIdentityCacheTtl.TotalHours,
            mikan.BangumiIdentityCacheTtl.TotalHours,
            tmdb.BaseUrl.AbsoluteUri,
            tmdb.ImageBaseUrl.AbsoluteUri,
            tmdb.Language,
            tmdb.HttpTimeout.TotalSeconds,
            tmdb.RetryCount,
            tmdb.RetryDelay.TotalSeconds,
            tmdb.CacheTtl.TotalHours,
            SecretState(settings?.TmdbApiKeyOverridden == true, settings?.TmdbApiKey),
            string.IsNullOrWhiteSpace(tmdb.ApiKey) ? null : tmdb.ApiKey,
            SecretState(
                settings?.TmdbReadAccessTokenOverridden == true,
                settings?.TmdbReadAccessToken),
            string.IsNullOrWhiteSpace(tmdb.ReadAccessToken) ? null : tmdb.ReadAccessToken,
            bangumi.BaseUrl.AbsoluteUri,
            bangumi.HttpTimeout.TotalSeconds,
            bangumi.RetryCount,
            bangumi.RetryDelay.TotalSeconds,
            season.Skip,
            season.Backtrace,
            season.UseTitleSeason,
            season.UseFirstSeason,
            ai.BaseUrl?.AbsoluteUri,
            ai.Model,
            ai.PromptTemplate ?? AiMetadataPromptRenderer.LoadTemplate(),
            SecretState(settings?.AiApiKeyOverridden == true, settings?.AiApiKey),
            string.IsNullOrWhiteSpace(ai.ApiKey) ? null : ai.ApiKey,
            ai.TmdbMcpUrl.AbsoluteUri,
            ai.BangumiMcpUrl.AbsoluteUri,
            ai.UseMetadataMatch,
            ai.UseMetadataMatch,
            ai.UseMetadataMatch,
            ai.DebugMode,
            ai.HttpTimeout.TotalSeconds,
            desired.Metadata.TmdbFailureUseBangumi,
            desired.Metadata.WriteBangumiIdWhenTmdbMatched,
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
                item.ControllingKeys)).ToArray(),
            AiReasoningEffort: ai.ReasoningEffort ?? "none",
            MikanTrustedOffsetRequiredEpisodes:
                desired.Metadata.MikanTrustedOffsetRequiredEpisodes,
            MovieSavePath: desired.Paths.EffectiveMovieSavePath,
            AiApiMode: FormatAiApiMode(ai.ApiMode),
            AiWebSearchEnabled: ai.WebSearchEnabled,
            AiUseBangumiPubDateFirst: ai.UseBangumiPubDateFirst);
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
        if (request.ClearAiApiKey && !string.IsNullOrWhiteSpace(request.AiApiKey))
        {
            throw new ArgumentException(
                "ai_api_key and clear_ai_api_key cannot both be set.");
        }

        var requestedSettings = CreateApplicationOverride(
            request,
            current.Settings,
            deployment.Metadata.Mikan.BaseUrl.AbsoluteUri,
            deployment.Metadata.Mikan.EpisodeIdentityCacheTtl.TotalHours,
            deployment.Metadata.Mikan.BangumiIdentityCacheTtl.TotalHours,
            deployment.Metadata.Tmdb.ImageBaseUrl.AbsoluteUri,
            deployment.Metadata.Tmdb.CacheTtl.TotalHours,
            deployment.Paths.DownloadPath,
            deployment.Paths.SavePath,
            deployment.Paths.EffectiveMovieSavePath,
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
        if (locks.IsLocked("ai_api_key")
            && (request.ClearAiApiKey || !string.IsNullOrWhiteSpace(request.AiApiKey)))
        {
            changedLockedFields.Add("ai_api_key");
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
        var errors = AnimeGoOptionsValidator.Validate(candidate).ToList();
        if (candidate.Metadata.Ai.PromptTemplate is not null)
        {
            try
            {
                AiMetadataPromptRenderer.ValidateTemplate(
                    candidate.Metadata.Ai.PromptTemplate);
            }
            catch (AiMetadataMatcherException exception)
            {
                errors.Add($"AI Prompt template is invalid ({exception.SafeCode}).");
            }
        }
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors));
        }
        return (settings, candidate);
    }

    private static List<ConfigurationChangeResponse> ConfigurationChanges(
        ConfigurationUpdateRequest request,
        AnimeGoOptions current,
        AnimeGoOptions candidate)
    {
        var changes = new List<ConfigurationChangeResponse>();
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var beforeMikan = current.Metadata.Mikan;
        var afterMikan = candidate.Metadata.Mikan;
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

        Add("download_path", current.Paths.DownloadPath, candidate.Paths.DownloadPath);
        Add("save_path", current.Paths.SavePath, candidate.Paths.SavePath);
        Add(
            "movie_save_path",
            current.Paths.EffectiveMovieSavePath,
            candidate.Paths.EffectiveMovieSavePath);

        Add(
            "outbound_proxy_url",
            current.OutboundProxy.Url?.AbsoluteUri,
            candidate.OutboundProxy.Url?.AbsoluteUri);
        Add(
            "outbound_proxy_hosts",
            string.Join("\n", current.OutboundProxy.HostPatterns),
            string.Join("\n", candidate.OutboundProxy.HostPatterns));
        Add("mikan_base_url", beforeMikan.BaseUrl.AbsoluteUri, afterMikan.BaseUrl.AbsoluteUri);
        Add(
            "mikan_episode_identity_cache_hours",
            beforeMikan.EpisodeIdentityCacheTtl.TotalHours.ToString(invariant),
            afterMikan.EpisodeIdentityCacheTtl.TotalHours.ToString(invariant));
        Add(
            "mikan_bangumi_identity_cache_hours",
            beforeMikan.BangumiIdentityCacheTtl.TotalHours.ToString(invariant),
            afterMikan.BangumiIdentityCacheTtl.TotalHours.ToString(invariant));
        Add("tmdb_base_url", beforeTmdb.BaseUrl.AbsoluteUri, afterTmdb.BaseUrl.AbsoluteUri);
        Add(
            "tmdb_image_base_url",
            beforeTmdb.ImageBaseUrl.AbsoluteUri,
            afterTmdb.ImageBaseUrl.AbsoluteUri);
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
            beforeTmdb.ApiKey,
            afterTmdb.ApiKey);
        AddSecret(
            "tmdb_read_access_token",
            request.TmdbReadAccessToken,
            request.ClearTmdbReadAccessToken,
            beforeTmdb.ReadAccessToken,
            afterTmdb.ReadAccessToken);
        Add(
            "bangumi_base_url",
            beforeBangumi.BaseUrl.AbsoluteUri,
            afterBangumi.BaseUrl.AbsoluteUri);
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
        Add("ai_base_url", beforeAi.BaseUrl?.AbsoluteUri, afterAi.BaseUrl?.AbsoluteUri);
        Add("ai_model", beforeAi.Model, afterAi.Model);
        Add(
            "ai_api_mode",
            FormatAiApiMode(beforeAi.ApiMode),
            FormatAiApiMode(afterAi.ApiMode));
        AddBool(
            "ai_web_search_enabled",
            beforeAi.WebSearchEnabled,
            afterAi.WebSearchEnabled);
        Add(
            "ai_reasoning_effort",
            beforeAi.ReasoningEffort ?? "none",
            afterAi.ReasoningEffort ?? "none");
        Add(
            "ai_prompt_template",
            PromptSummary(beforeAi.PromptTemplate ?? AiMetadataPromptRenderer.LoadTemplate()),
            PromptSummary(afterAi.PromptTemplate ?? AiMetadataPromptRenderer.LoadTemplate()));
        if (request.ClearAiApiKey || !string.IsNullOrWhiteSpace(request.AiApiKey))
        {
            Add(
                "ai_api_key",
                beforeAi.ApiKey,
                afterAi.ApiKey,
                sensitive: true,
                force: true);
        }
        Add(
            "ai_tmdb_mcp_url",
            beforeAi.TmdbMcpUrl.AbsoluteUri,
            afterAi.TmdbMcpUrl.AbsoluteUri);
        Add(
            "ai_bangumi_mcp_url",
            beforeAi.BangumiMcpUrl.AbsoluteUri,
            afterAi.BangumiMcpUrl.AbsoluteUri);
        AddBool(
            "ai_use_bangumi_pubdate_first",
            beforeAi.UseBangumiPubDateFirst,
            afterAi.UseBangumiPubDateFirst);
        AddBool(
            "ai_use_metadata_match",
            beforeAi.UseMetadataMatch,
            afterAi.UseMetadataMatch);
        AddBool(
            "ai_debug_mode",
            beforeAi.DebugMode,
            afterAi.DebugMode);
        AddSeconds(
            "ai_http_timeout_seconds",
            beforeAi.HttpTimeout,
            afterAi.HttpTimeout);
        AddBool(
            "tmdb_failure_use_bangumi",
            current.Metadata.TmdbFailureUseBangumi,
            candidate.Metadata.TmdbFailureUseBangumi);
        AddBool(
            "write_bangumi_id_when_tmdb_matched",
            current.Metadata.WriteBangumiIdWhenTmdbMatched,
            candidate.Metadata.WriteBangumiIdWhenTmdbMatched);
        AddBool(
            "mikan_trusted_offset_cache_enabled",
            current.Metadata.MikanTrustedOffsetCacheEnabled,
            candidate.Metadata.MikanTrustedOffsetCacheEnabled);
        Add(
            "mikan_trusted_offset_required_episodes",
            current.Metadata.MikanTrustedOffsetRequiredEpisodes.ToString(invariant),
            candidate.Metadata.MikanTrustedOffsetRequiredEpisodes.ToString(invariant));
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
            string? currentValue,
            string? candidateValue)
        {
            if (!clear && string.IsNullOrWhiteSpace(requestedValue))
            {
                return;
            }
            Add(
                field,
                currentValue,
                clear ? null : candidateValue,
                sensitive: true,
                force: true);
        }
    }

    private static ConfigurationUpdateRequest MergeConfigurationSectionRequest(
        string section,
        ConfigurationUpdateRequest request,
        AnimeGoOptions current)
    {
        var mikan = current.Metadata.Mikan;
        var tmdb = current.Metadata.Tmdb;
        var bangumi = current.Metadata.Bangumi;
        var season = current.Metadata.SeasonFailure;
        var ai = current.Metadata.Ai;
        var torrent = current.TorrentFetch;
        var dataUpdate = current.DataUpdate;
        var merged = new ConfigurationUpdateRequest(
            MikanBaseUrl: mikan.BaseUrl.AbsoluteUri,
            TmdbBaseUrl: tmdb.BaseUrl.AbsoluteUri,
            TmdbImageBaseUrl: tmdb.ImageBaseUrl.AbsoluteUri,
            TmdbLanguage: tmdb.Language,
            TmdbHttpTimeoutSeconds: tmdb.HttpTimeout.TotalSeconds,
            TmdbRetryCount: tmdb.RetryCount,
            TmdbRetryDelaySeconds: tmdb.RetryDelay.TotalSeconds,
            TmdbCacheHours: tmdb.CacheTtl.TotalHours,
            TmdbApiKey: null,
            ClearTmdbApiKey: false,
            TmdbReadAccessToken: null,
            ClearTmdbReadAccessToken: false,
            BangumiBaseUrl: bangumi.BaseUrl.AbsoluteUri,
            BangumiHttpTimeoutSeconds: bangumi.HttpTimeout.TotalSeconds,
            BangumiRetryCount: bangumi.RetryCount,
            BangumiRetryDelaySeconds: bangumi.RetryDelay.TotalSeconds,
            SeasonFailureSkip: season.Skip,
            SeasonFailureBacktrace: season.Backtrace,
            SeasonFailureUseTitleSeason: season.UseTitleSeason,
            SeasonFailureUseFirstSeason: season.UseFirstSeason,
            AiUseMetadataMatch: ai.UseMetadataMatch,
            AiUseSeasonMatch: ai.UseMetadataMatch,
            AiUseEpisodeMatch: ai.UseMetadataMatch,
            AiDebugMode: ai.DebugMode,
            AiHttpTimeoutSeconds: ai.HttpTimeout.TotalSeconds,
            TmdbFailureUseBangumi: current.Metadata.TmdbFailureUseBangumi,
            WriteBangumiIdWhenTmdbMatched:
                current.Metadata.WriteBangumiIdWhenTmdbMatched,
            MikanTrustedOffsetCacheEnabled:
                current.Metadata.MikanTrustedOffsetCacheEnabled,
            TorrentHttpTimeoutSeconds: torrent.Timeout.TotalSeconds,
            TorrentMaxResponseBytes: torrent.MaxResponseBytes,
            TorrentMaxRedirects: torrent.MaxRedirects,
            TorrentStagingTtlSeconds: torrent.StagingTtl.TotalSeconds,
            DataUpdateEnabled: dataUpdate.Enabled,
            DataUpdateCron: dataUpdate.Cron,
            DataUpdateManifestUrl: dataUpdate.ManifestUrl?.AbsoluteUri,
            DataUpdateAutoDownload: dataUpdate.AutoDownload,
            DataUpdateAutoImport: dataUpdate.AutoImport,
            DataUpdateKeepVersions: dataUpdate.KeepVersions,
            DataUpdateHttpTimeoutSeconds: dataUpdate.HttpTimeout.TotalSeconds,
            ExpectedConfigurationRevision: request.ExpectedConfigurationRevision,
            OutboundProxyUrl: current.OutboundProxy.Url?.AbsoluteUri,
            OutboundProxyHosts: current.OutboundProxy.HostPatterns,
            AiBaseUrl: ai.BaseUrl?.AbsoluteUri,
            AiModel: ai.Model,
            AiApiKey: null,
            ClearAiApiKey: false,
            AiTmdbMcpUrl: ai.TmdbMcpUrl.AbsoluteUri,
            AiBangumiMcpUrl: ai.BangumiMcpUrl.AbsoluteUri,
            AiPromptTemplate: ai.PromptTemplate ?? AiMetadataPromptRenderer.LoadTemplate(),
            MikanEpisodeIdentityCacheHours: mikan.EpisodeIdentityCacheTtl.TotalHours,
            MikanBangumiIdentityCacheHours: mikan.BangumiIdentityCacheTtl.TotalHours,
            AiReasoningEffort: ai.ReasoningEffort ?? "none",
            MikanTrustedOffsetRequiredEpisodes:
                current.Metadata.MikanTrustedOffsetRequiredEpisodes,
            DownloadPath: current.Paths.DownloadPath,
            SavePath: current.Paths.SavePath,
            MovieSavePath: current.Paths.EffectiveMovieSavePath,
            AiApiMode: FormatAiApiMode(ai.ApiMode),
            AiWebSearchEnabled: ai.WebSearchEnabled,
            AiUseBangumiPubDateFirst: ai.UseBangumiPubDateFirst);

        return section.Trim().ToLowerInvariant() switch
        {
            "paths" => merged with
            {
                DownloadPath = request.DownloadPath,
                SavePath = request.SavePath,
                MovieSavePath = request.MovieSavePath,
            },
            "network" => merged with
            {
                OutboundProxyUrl = request.OutboundProxyUrl,
                OutboundProxyHosts = request.OutboundProxyHosts,
                MikanBaseUrl = request.MikanBaseUrl,
                MikanEpisodeIdentityCacheHours = request.MikanEpisodeIdentityCacheHours,
                MikanBangumiIdentityCacheHours = request.MikanBangumiIdentityCacheHours,
                TmdbBaseUrl = request.TmdbBaseUrl,
                TmdbImageBaseUrl = request.TmdbImageBaseUrl,
                TmdbLanguage = request.TmdbLanguage,
                TmdbHttpTimeoutSeconds = request.TmdbHttpTimeoutSeconds,
                TmdbRetryCount = request.TmdbRetryCount,
                TmdbRetryDelaySeconds = request.TmdbRetryDelaySeconds,
                TmdbCacheHours = request.TmdbCacheHours,
                TmdbApiKey = request.TmdbApiKey,
                ClearTmdbApiKey = request.ClearTmdbApiKey,
                TmdbReadAccessToken = request.TmdbReadAccessToken,
                ClearTmdbReadAccessToken = request.ClearTmdbReadAccessToken,
                BangumiBaseUrl = request.BangumiBaseUrl,
                BangumiHttpTimeoutSeconds = request.BangumiHttpTimeoutSeconds,
                BangumiRetryCount = request.BangumiRetryCount,
                BangumiRetryDelaySeconds = request.BangumiRetryDelaySeconds,
                TorrentHttpTimeoutSeconds = request.TorrentHttpTimeoutSeconds,
                TorrentMaxResponseBytes = request.TorrentMaxResponseBytes,
                TorrentMaxRedirects = request.TorrentMaxRedirects,
                TorrentStagingTtlSeconds = request.TorrentStagingTtlSeconds,
                DataUpdateEnabled = request.DataUpdateEnabled,
                DataUpdateCron = request.DataUpdateCron,
                DataUpdateManifestUrl = request.DataUpdateManifestUrl,
                DataUpdateAutoDownload = request.DataUpdateAutoDownload,
                DataUpdateAutoImport = request.DataUpdateAutoImport,
                DataUpdateKeepVersions = request.DataUpdateKeepVersions,
                DataUpdateHttpTimeoutSeconds = request.DataUpdateHttpTimeoutSeconds,
            },
            "ai" => merged with
            {
                SeasonFailureSkip = request.SeasonFailureSkip,
                SeasonFailureBacktrace = request.SeasonFailureBacktrace,
                SeasonFailureUseTitleSeason = request.SeasonFailureUseTitleSeason,
                SeasonFailureUseFirstSeason = request.SeasonFailureUseFirstSeason,
                AiBaseUrl = request.AiBaseUrl,
                AiModel = request.AiModel,
                AiApiMode = request.AiApiMode,
                AiWebSearchEnabled = request.AiWebSearchEnabled,
                AiApiKey = request.AiApiKey,
                ClearAiApiKey = request.ClearAiApiKey,
                AiTmdbMcpUrl = request.AiTmdbMcpUrl,
                AiBangumiMcpUrl = request.AiBangumiMcpUrl,
                AiUseBangumiPubDateFirst = request.AiUseBangumiPubDateFirst,
                AiPromptTemplate = request.AiPromptTemplate,
                AiReasoningEffort = request.AiReasoningEffort,
                AiUseMetadataMatch = request.AiUseMetadataMatch,
                AiUseSeasonMatch = request.AiUseMetadataMatch,
                AiUseEpisodeMatch = request.AiUseMetadataMatch,
                AiDebugMode = request.AiDebugMode,
                AiHttpTimeoutSeconds = request.AiHttpTimeoutSeconds,
                TmdbFailureUseBangumi = request.TmdbFailureUseBangumi,
                WriteBangumiIdWhenTmdbMatched =
                    request.WriteBangumiIdWhenTmdbMatched,
                MikanTrustedOffsetCacheEnabled =
                    request.MikanTrustedOffsetCacheEnabled,
                MikanTrustedOffsetRequiredEpisodes =
                    request.MikanTrustedOffsetRequiredEpisodes,
            },
            _ => throw new ArgumentException(
                "section must be one of: paths, network, ai.",
                nameof(section)),
        };
    }

    private static ApplicationOverrideEntry CreateApplicationOverride(
        ConfigurationUpdateRequest request,
        ApplicationOverrideEntry? current,
        string deploymentMikanBaseUrl,
        double deploymentMikanEpisodeIdentityCacheHours,
        double deploymentMikanBangumiIdentityCacheHours,
        string deploymentTmdbImageBaseUrl,
        double deploymentTmdbCacheHours,
        string deploymentDownloadPath,
        string deploymentSavePath,
        string deploymentMovieSavePath,
        DateTimeOffset utcNow)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(request.ExpectedConfigurationRevision);
        var downloadPath = NormalizeAbsolutePath(
            request.DownloadPath ?? current?.DownloadPath ?? deploymentDownloadPath,
            "download_path");
        var savePath = NormalizeAbsolutePath(
            request.SavePath ?? current?.SavePath ?? deploymentSavePath,
            "save_path");
        var movieSavePath = NormalizeAbsolutePath(
            request.MovieSavePath ?? current?.MovieSavePath ?? deploymentMovieSavePath,
            "movie_save_path");
        var mikanBaseUrl = request.MikanBaseUrl?.Trim()
            ?? current?.MikanBaseUrl
            ?? deploymentMikanBaseUrl;
        var mikanEpisodeIdentityCacheHours = request.MikanEpisodeIdentityCacheHours
            ?? current?.MikanEpisodeIdentityCacheHours
            ?? deploymentMikanEpisodeIdentityCacheHours;
        var mikanBangumiIdentityCacheHours = request.MikanBangumiIdentityCacheHours
            ?? current?.MikanBangumiIdentityCacheHours
            ?? deploymentMikanBangumiIdentityCacheHours;
        var baseUrl = request.TmdbBaseUrl?.Trim()
            ?? throw new ArgumentException("tmdb_base_url is required.");
        var tmdbImageBaseUrl = request.TmdbImageBaseUrl?.Trim()
            ?? current?.TmdbImageBaseUrl
            ?? deploymentTmdbImageBaseUrl;
        var language = request.TmdbLanguage?.Trim()
            ?? throw new ArgumentException("tmdb_language is required.");
        var bangumiBaseUrl = request.BangumiBaseUrl?.Trim()
            ?? throw new ArgumentException("bangumi_base_url is required.");
        if (mikanBaseUrl.Length is < 1 or > 2048
            || !Uri.TryCreate(mikanBaseUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "mikan_base_url must contain an absolute URL of at most 2048 characters.");
        }
        ValidateNonNegativeSeconds(
            mikanEpisodeIdentityCacheHours,
            "mikan_episode_identity_cache_hours",
            24 * 3650);
        ValidateNonNegativeSeconds(
            mikanBangumiIdentityCacheHours,
            "mikan_bangumi_identity_cache_hours",
            24 * 3650);
        if (baseUrl.Length is < 1 or > 2048)
        {
            throw new ArgumentException("tmdb_base_url must contain 1 to 2048 characters.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("tmdb_base_url must be an absolute URL.");
        }
        if (tmdbImageBaseUrl.Length is < 1 or > 2048
            || !Uri.TryCreate(tmdbImageBaseUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "tmdb_image_base_url must contain an absolute URL of at most 2048 characters.");
        }
        if (bangumiBaseUrl.Length is < 1 or > 2048
            || !Uri.TryCreate(bangumiBaseUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "bangumi_base_url must contain an absolute URL of at most 2048 characters.");
        }
        var outboundProxyUrl = NormalizeOptionalUrl(
            request.OutboundProxyUrl,
            "outbound_proxy_url");
        var outboundProxyHosts = NormalizeHostPatterns(
            request.OutboundProxyHosts
                ?? current?.OutboundProxyHosts
                ?? []);
        var aiBaseUrl = NormalizeOptionalUrl(request.AiBaseUrl, "ai_base_url");
        var aiModel = string.IsNullOrWhiteSpace(request.AiModel)
            ? null
            : request.AiModel.Trim();
        if (aiModel is { Length: > 256 })
        {
            throw new ArgumentException("ai_model must contain at most 256 characters.");
        }
        var aiApiMode = ParseAiApiMode(request.AiApiMode)
            ?? current?.AiApiMode
            ?? AiApiMode.Responses;
        if (HasValue(request.AiApiMode) && ParseAiApiMode(request.AiApiMode) is null)
        {
            throw new ArgumentException(
                "ai_api_mode must be responses or chat-completions.");
        }
        var aiWebSearchEnabled = request.AiWebSearchEnabled
            ?? current?.AiWebSearchEnabled
            ?? true;
        if (aiWebSearchEnabled && aiApiMode != AiApiMode.Responses)
        {
            throw new ArgumentException(
                "ai_web_search_enabled requires ai_api_mode=responses.");
        }
        var aiUseBangumiPubDateFirst = request.AiUseBangumiPubDateFirst
            ?? current?.AiUseBangumiPubDateFirst
            ?? true;
        if (!IsOptionalReasoningEffort(request.AiReasoningEffort))
        {
            throw new ArgumentException(
                "ai_reasoning_effort must be none, low, medium or high.");
        }
        var aiReasoningEffortSpecified = HasValue(request.AiReasoningEffort);
        var aiReasoningEffort = aiReasoningEffortSpecified
            ? ParseReasoningEffort(request.AiReasoningEffort, inherited: null)
            : current?.AiReasoningEffort;
        var aiPromptTemplate = string.IsNullOrWhiteSpace(request.AiPromptTemplate)
            ? current?.AiPromptTemplate
            : request.AiPromptTemplate.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (aiPromptTemplate is not null)
        {
            try
            {
                AiMetadataPromptRenderer.ValidateTemplate(aiPromptTemplate);
            }
            catch (AiMetadataMatcherException exception)
            {
                throw new ArgumentException(
                    $"ai_prompt_template is invalid ({exception.SafeCode}).");
            }
        }
        var aiTmdbMcpUrl = NormalizeRequiredUrl(
            request.AiTmdbMcpUrl
                ?? current?.AiTmdbMcpUrl
                ?? new AiMatchingOptions().TmdbMcpUrl.AbsoluteUri,
            "ai_tmdb_mcp_url");
        var aiBangumiMcpUrl = NormalizeRequiredUrl(
            request.AiBangumiMcpUrl
                ?? current?.AiBangumiMcpUrl
                ?? new AiMatchingOptions().BangumiMcpUrl.AbsoluteUri,
            "ai_bangumi_mcp_url");

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

        var mikanTrustedOffsetRequiredEpisodes =
            request.MikanTrustedOffsetRequiredEpisodes
            ?? current?.MikanTrustedOffsetRequiredEpisodes
            ?? 3;
        if (mikanTrustedOffsetRequiredEpisodes is < 1 or > 100)
        {
            throw new ArgumentException(
                "mikan_trusted_offset_required_episodes must be between 1 and 100.");
        }

        var apiKey = NormalizeSecret(request.TmdbApiKey, "tmdb_api_key");
        var readToken = NormalizeSecret(
            request.TmdbReadAccessToken,
            "tmdb_read_access_token");
        var aiApiKey = NormalizeSecret(request.AiApiKey, "ai_api_key");
        var apiKeyOverridden = request.ClearTmdbApiKey
            || apiKey is not null
            || current?.TmdbApiKeyOverridden == true;
        var readTokenOverridden = request.ClearTmdbReadAccessToken
            || readToken is not null
            || current?.TmdbReadAccessTokenOverridden == true;
        var aiApiKeyOverridden = request.ClearAiApiKey
            || aiApiKey is not null
            || current?.AiApiKeyOverridden == true;
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
            BangumiBaseUrl: bangumiBaseUrl,
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
            TmdbCacheHours: tmdbCacheHours,
            MikanBaseUrl: mikanBaseUrl,
            TmdbImageBaseUrl: tmdbImageBaseUrl,
            OutboundProxyUrlOverridden: true,
            OutboundProxyUrl: outboundProxyUrl,
            OutboundProxyHosts: outboundProxyHosts,
            AiBaseUrlOverridden: true,
            AiBaseUrl: aiBaseUrl,
            AiApiKeyOverridden: aiApiKeyOverridden,
            AiApiKey: request.ClearAiApiKey ? null : aiApiKey ?? current?.AiApiKey,
            AiModelOverridden: true,
            AiModel: aiModel,
            AiReasoningEffortOverridden:
                aiReasoningEffortSpecified
                || current?.AiReasoningEffortOverridden == true,
            AiReasoningEffort: aiReasoningEffort,
            AiTmdbMcpUrl: aiTmdbMcpUrl,
            AiBangumiMcpUrl: aiBangumiMcpUrl,
            WriteBangumiIdWhenTmdbMatched:
                request.WriteBangumiIdWhenTmdbMatched,
            AiPromptTemplate: aiPromptTemplate,
            MikanEpisodeIdentityCacheHours: mikanEpisodeIdentityCacheHours,
            MikanBangumiIdentityCacheHours: mikanBangumiIdentityCacheHours,
            AiDebugMode: request.AiDebugMode ?? current?.AiDebugMode ?? false,
            MikanTrustedOffsetRequiredEpisodes: mikanTrustedOffsetRequiredEpisodes,
            DownloadPath: downloadPath,
            SavePath: savePath,
            MovieSavePath: movieSavePath,
            AiApiMode: aiApiMode,
            AiWebSearchEnabled: aiWebSearchEnabled,
            AiUseBangumiPubDateFirst: aiUseBangumiPubDateFirst);
    }

    private static string PromptSummary(string template) =>
        $"{AiMetadataPromptRenderer.PromptVersion} · {template.Length} chars · sha256:{StableHash.Sha256LowerHex(template)[..12]}";

    private static bool RequiresRestart(AnimeGoOptions current, AnimeGoOptions candidate)
    {
        var candidateWithoutHotAppliedOrProxy = candidate with
        {
            DataUpdate = current.DataUpdate,
            OutboundProxy = current.OutboundProxy,
        };
        return current != candidateWithoutHotAppliedOrProxy
            || current.OutboundProxy.Url != candidate.OutboundProxy.Url
            || !current.OutboundProxy.HostPatterns.SequenceEqual(
                candidate.OutboundProxy.HostPatterns,
                StringComparer.Ordinal);
    }

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

    private static string NormalizeRequiredUrl(string? value, string name) =>
        NormalizeOptionalUrl(value, name)
        ?? throw new ArgumentException($"{name} is required.");

    private static string NormalizeAbsolutePath(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value.Trim()))
        {
            throw new ArgumentException($"{name} must be an absolute path.");
        }

        try
        {
            return Path.GetFullPath(value.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ArgumentException($"{name} must be a valid absolute path.", exception);
        }
    }

    private static string[] NormalizeHostPatterns(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!AnimeGoOptionsValidator.IsValidTorrentHostPattern(normalized))
            {
                throw new ArgumentException(
                    "outbound_proxy_hosts must contain only exact hosts or '*.example.com'.");
            }
            if (seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }
        return result.ToArray();
    }

    private static async Task<IResult> Downloads(
        [FromQuery] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? state,
        [FromQuery(Name = "business_status")] string? businessStatus,
        [FromQuery(Name = "downloader_id")] string? downloaderId,
        [FromQuery] string? source,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        [FromQuery(Name = "summary_bucket")] string? summaryBucket,
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
                    source,
                    sort,
                    direction,
                    summaryBucket),
                cancellationToken).ConfigureAwait(false);
            var normalizedSort = string.IsNullOrWhiteSpace(sort)
                ? "created"
                : sort.Trim().ToLowerInvariant();
            var normalizedDirection = string.IsNullOrWhiteSpace(direction)
                ? "desc"
                : direction.Trim().ToLowerInvariant();
            return TypedResults.Ok(new DownloadListResponse(
                records.Page,
                records.PageSize,
                records.TotalItems,
                NormalizeEcho(search),
                NormalizeEcho(state),
                NormalizeEcho(businessStatus),
                NormalizeEcho(downloaderId),
                NormalizeEcho(source),
                normalizedSort,
                normalizedDirection,
                NormalizeEcho(summaryBucket),
                new DownloadDashboardSummary(
                    records.Summary.TotalJobs,
                    records.Summary.ActiveJobs,
                    records.Summary.PausedJobs,
                    records.Summary.DeadJobs,
                    records.Summary.FailedJobs,
                    records.Summary.StaleJobs,
                    records.Summary.WaitingOrganizationJobs,
                    records.Summary.CompletedJobs,
                    records.Summary.SkippedDuplicateJobs,
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
            detail.Summary.State is "paused" or "dead",
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
        if (value.State == "skipped_duplicate"
            && value.BusinessStatus == "download_skipped_duplicate")
        {
            var result = await jobs.RetrySkippedDuplicateAsync(
                value,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return await ControlResultAsync(
                value.JobId,
                "retry_duplicate",
                result,
                jobs,
                cancellationToken).ConfigureAwait(false);
        }

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
            DownloadJobControlUpdateResult.DuplicateStillOccupied => TypedResults.Conflict(Error(
                "download_duplicate_still_occupied",
                "The TMDB Episode is still completed or claimed by another task.")),
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
            if (!PathBoundary.IsAbsolute(downloadPath))
            {
                throw new ArgumentException("download_path must be an absolute path visible to AnimeGoNet and qBittorrent.");
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
                plugins,
                request.MediaType,
                request.PreferAniDbTmdbMapping,
                request.AniDbTmdbMappingUrlTemplate);
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
                plugins,
                request.MediaType,
                request.PreferAniDbTmdbMapping,
                request.AniDbTmdbMappingUrlTemplate);
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
                request.AniDbId, request.ImdbId,
                MediaType: profile.MediaType));
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
            profile.MediaType == MediaTypes.Movie
                ? options.Paths.EffectiveMovieSavePath
                : options.Paths.SavePath,
            profile.FileStrategy,
            profile.Category,
            profile.Tags,
            profile.DynamicTagTemplate,
            profile.SeedingTimeMinutes,
            profile.RssFilterEnabled,
            profile.RssPriorityEnabled,
            profile.DuplicateNotificationEnabled,
            ruleRevision,
            profile.MediaType));
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
                preview.MediaFiles.Select(ToResponse).ToArray(),
                preview.TaskRecords.Select(ToResponse).ToArray(),
                preview.TaskRecordDeletionAllowed,
                preview.TaskRecordDeletionDenialReason));
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
                    request.DeleteMediaFiles,
                    request.DeleteTaskRecord),
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
            : TypedResults.Ok(ToResponse(execution));
    }

    private static async Task<IResult> ExecuteDeleteAndWait(
        string taskId,
        CreateDeleteExecutionRequest request,
        DeletePlanStore plans,
        DeleteExecutionStore executions,
        DeleteExecutionProcessor processor,
        IHostApplicationLifetime applicationLifetime,
        CancellationToken cancellationToken)
    {
        DeleteExecutionStatus? execution = null;
        var reusedExistingExecution = false;
        try
        {
            var plan = await plans.CreateAsync(
                taskId,
                request.Fingerprint ?? string.Empty,
                new DeleteSelection(
                    request.DeleteBusinessRecord,
                    request.DeleteDownloaderTask,
                    request.DeleteSourceFiles,
                    request.DeleteMediaFiles,
                    request.DeleteTaskRecord),
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            execution = await executions.GetAsync(plan.ExecutionId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            reusedExistingExecution = true;
            execution = await executions.GetActiveForTaskAsync(taskId, cancellationToken)
                .ConfigureAwait(false);
            if (execution is null)
            {
                return TypedResults.Conflict(Error(
                    "delete_execution_active_missing",
                    "The active delete execution could not be loaded."));
            }
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound(Error("delete_task_not_found", "Delete task was not found."));
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Conflict(Error("delete_preview_stale", exception.Message));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("delete_request_invalid", exception.Message));
        }

        if (execution is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "delete_execution_missing");
        }

        var executionCancellationToken = applicationLifetime.ApplicationStopping;
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (execution.State == "completed")
            {
                return TypedResults.Ok(ToResponse(execution, reusedExistingExecution));
            }

            if (execution.State == "pending")
            {
                var result = await processor.RunExecutionOnceAsync(
                    execution.ExecutionId, executionCancellationToken).ConfigureAwait(false);
                execution = await executions.GetAsync(
                    execution.ExecutionId, executionCancellationToken)
                    .ConfigureAwait(false) ?? execution;
                if (result == DeleteExecutionResult.RetryScheduled
                    || (result == DeleteExecutionResult.NoWork
                        && execution.State == "pending"
                        && execution.FailureReason is not null))
                {
                    return TypedResults.Ok(ToResponse(execution, reusedExistingExecution));
                }

                continue;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), executionCancellationToken)
                .ConfigureAwait(false);
            execution = await executions.GetAsync(
                execution.ExecutionId, executionCancellationToken)
                .ConfigureAwait(false) ?? execution;
        }

        return TypedResults.Ok(ToResponse(execution, reusedExistingExecution));
    }

    private static DeleteExecutionStatusResponse ToResponse(
        DeleteExecutionStatus execution,
        bool reusedExistingExecution = false) =>
        new(
            execution.ExecutionId, execution.TaskId, execution.State, execution.FailureReason,
            execution.AttemptCount, execution.CreatedAtUtc, execution.CompletedAtUtc,
            execution.Items.Select(item => new DeleteTargetResponse(
                item.ItemKind, item.TargetKey, item.RootPath, item.DownloaderId,
                item.DisplayValue, item.State)).ToArray(),
            reusedExistingExecution);

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
        AnimeGoOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var requiredEpisodes = options.Metadata.MikanTrustedOffsetRequiredEpisodes;
            var values = await offsets.ListAsync(
                mikanId,
                groupId,
                requiredEpisodes,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new MikanTrustedOffsetListResponse(
                values.Select(value => new MikanTrustedOffsetItemResponse(
                    value.MikanId,
                    value.GroupId,
                    value.TmdbSeriesId,
                    value.TmdbSeasonNumber,
                    value.EpisodeOffset,
                    value.DistinctEpisodeCount,
                    requiredEpisodes,
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

    private static async Task<IResult> ListMikanTrustedOffsetBlacklist(
        MikanTrustedOffsetStore offsets,
        CancellationToken cancellationToken)
    {
        var values = await offsets.ListBlacklistAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new MikanTrustedOffsetBlacklistListResponse(
            values.Select(ToBlacklistResponse).ToArray()));
    }

    private static async Task<IResult> AddMikanTrustedOffsetBlacklist(
        MikanTrustedOffsetBlacklistWriteRequest request,
        MikanTrustedOffsetStore offsets,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await offsets.AddBlacklistAsync(
                request.Scope,
                request.MikanId,
                request.GroupId,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ToBlacklistResponse(value));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("mikan_offset_blacklist_key_invalid", exception.Message));
        }
    }

    private static async Task<IResult> RemoveMikanTrustedOffsetBlacklist(
        [FromQuery(Name = "scope")] string scope,
        [FromQuery(Name = "mikanid")] int? mikanId,
        [FromQuery(Name = "groupid")] int? groupId,
        MikanTrustedOffsetStore offsets,
        CancellationToken cancellationToken)
    {
        try
        {
            return await offsets.RemoveBlacklistAsync(
                    scope,
                    mikanId,
                    groupId,
                    cancellationToken).ConfigureAwait(false)
                ? TypedResults.NoContent()
                : TypedResults.NotFound(Error(
                    "mikan_offset_blacklist_not_found",
                    "Mikan trusted offset blacklist entry was not found."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("mikan_offset_blacklist_key_invalid", exception.Message));
        }
    }

    private static MikanTrustedOffsetBlacklistItemResponse ToBlacklistResponse(
        MikanTrustedOffsetBlacklistEntry value) =>
        new(value.Scope, value.MikanId, value.GroupId, value.CreatedAtUtc);

    private static async Task<IResult> ListNotificationChannels(
        NotificationStore store,
        CancellationToken cancellationToken)
    {
        var values = await store.ListChannelsAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new NotificationChannelListResponse(
            values.Select(ToNotificationChannelResponse).ToArray()));
    }

    private static Task<IResult> CreateNotificationChannel(
        NotificationChannelWriteRequest request,
        NotificationStore store,
        CancellationToken cancellationToken) =>
        SaveNotificationChannel(null, request, store, cancellationToken);

    private static Task<IResult> UpdateNotificationChannel(
        string channelId,
        NotificationChannelWriteRequest request,
        NotificationStore store,
        CancellationToken cancellationToken) =>
        SaveNotificationChannel(channelId, request, store, cancellationToken);

    private static async Task<IResult> SaveNotificationChannel(
        string? channelId,
        NotificationChannelWriteRequest request,
        NotificationStore store,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await store.SaveChannelAsync(
                channelId,
                new NotificationChannelWrite(
                    request.Name,
                    request.Provider,
                    request.Enabled,
                    request.EndpointUrl,
                    request.Secret,
                    request.Target,
                    request.Options.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                        ? "{}"
                        : request.Options.GetRawText(),
                    request.Events),
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ToNotificationChannelResponse(value));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("notification_channel_invalid", exception.Message));
        }
    }

    private static async Task<IResult> DeleteNotificationChannel(
        string channelId,
        NotificationStore store,
        CancellationToken cancellationToken) =>
        await store.DeleteChannelAsync(channelId, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : TypedResults.NotFound(Error("notification_channel_not_found", "Notification channel was not found."));

    private static async Task<IResult> TestNotificationChannel(
        string channelId,
        NotificationStore store,
        WebhookNotificationSender sender,
        CancellationToken cancellationToken)
    {
        var channel = await store.GetChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (channel is null)
            return TypedResults.NotFound(Error("notification_channel_not_found", "Notification channel was not found."));

        var now = DateTimeOffset.UtcNow;
        var value = await store.CreateTestEventAsync(
            "AnimeGoNet 测试通知",
            $"渠道“{channel.Name}”连接测试成功触发。时间：{now:O}",
            now,
            cancellationToken).ConfigureAwait(false);
        var result = await sender.SendAsync(channel, value, cancellationToken).ConfigureAwait(false);
        await store.RecordDeliveryAsync(value, channel, result, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        await store.CompleteEventAsync(value.Id, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new NotificationTestResponse(
            result.Succeeded,
            result.HttpStatus,
            result.FailureCode,
            result.ResponseExcerpt,
            result.DurationMilliseconds));
    }

    private static async Task<IResult> ListNotificationDeliveries(
        [FromQuery] int? limit,
        NotificationStore store,
        CancellationToken cancellationToken)
    {
        var values = await store.ListDeliveriesAsync(limit ?? 100, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new NotificationDeliveryListResponse(
            values.Select(value => new NotificationDeliveryResponse(
                value.Id, value.ChannelName, value.Provider, value.EventType,
                value.TaskId, value.Title, value.State, value.HttpStatus,
                value.FailureCode, value.ResponseExcerpt,
                value.DurationMilliseconds, value.CreatedAtUtc)).ToArray()));
    }

    private static NotificationChannelResponse ToNotificationChannelResponse(
        NotificationChannel value)
    {
        using var document = JsonDocument.Parse(value.OptionsJson);
        return new NotificationChannelResponse(
            value.Id, value.Name, value.Provider, value.Enabled,
            value.EndpointUrl, value.Secret, value.Target,
            document.RootElement.Clone(), value.Events,
            value.CreatedAtUtc, value.UpdatedAtUtc);
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

    private static async Task<IResult> PreviewOtherFileReadaptation(
        string taskId,
        OtherFileReadaptationStore store,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return TypedResults.BadRequest(Error(
                "metadata_task_id_invalid",
                "Metadata task ID is required."));
        }

        var preview = await store.PreviewAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (preview is null)
        {
            return TypedResults.NotFound(Error(
                "metadata_task_not_found",
                "Metadata task was not found."));
        }

        var files = preview.Files.Select(file => new OtherFileReadaptationFileResponse(
            file.TaskFileId,
            file.SourceName,
            file.SizeBytes,
            file.OtherReason,
            file.TmdbSeriesId,
            file.TmdbSeasonNumber,
            File.Exists(file.SourceMediaPath)
                && new FileInfo(file.SourceMediaPath).Length == file.SizeBytes,
            file.SharedPathReferenceCount)).ToArray();
        var reason = ReadaptationDenialReason(preview, files);
        return TypedResults.Ok(new OtherFileReadaptationPreviewResponse(
            preview.TaskId,
            preview.Title,
            reason is null,
            reason,
            files));
    }

    private static async Task<IResult> StartOtherFileReadaptation(
        string taskId,
        OtherFileReadaptationStore store,
        MikanFeedIdentityResolver mikanIdentity,
        MikanBangumiSubjectResolver mikanBangumi,
        CancellationToken cancellationToken)
    {
        var preview = await store.PreviewAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (preview is null)
        {
            return TypedResults.NotFound(Error(
                "metadata_task_not_found",
                "Metadata task was not found."));
        }

        var files = preview.Files.Select(file => new OtherFileReadaptationFileResponse(
            file.TaskFileId,
            file.SourceName,
            file.SizeBytes,
            file.OtherReason,
            file.TmdbSeriesId,
            file.TmdbSeasonNumber,
            File.Exists(file.SourceMediaPath)
                && new FileInfo(file.SourceMediaPath).Length == file.SizeBytes,
            file.SharedPathReferenceCount)).ToArray();
        var denial = ReadaptationDenialReason(preview, files);
        if (denial is not null)
        {
            return TypedResults.Conflict(Error("other_readaptation_not_eligible", denial));
        }

        OtherFileReadaptationSourceIdentity? freshIdentity = null;
        if (string.Equals(preview.SourceAdapter, "mikan", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(preview.SourcePageUrl, UriKind.Absolute, out var sourcePage)
                || sourcePage.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(sourcePage.UserInfo))
            {
                return TypedResults.Conflict(Error(
                    "other_readaptation_source_url_missing",
                    "任务没有可安全重新访问的 Mikan Episode 来源页；不能从来源重新解析。"));
            }

            var identity = await mikanIdentity.ResolveFreshAsync(
                sourcePage,
                preview.SourceProfileId,
                cancellationToken).ConfigureAwait(false);
            if (identity.Identity is null)
            {
                return TypedResults.Conflict(Error(
                    identity.FailureCode ?? "mikan_identity_request_failed",
                    "Mikan Episode 来源页重新解析失败；未修改任务。"));
            }

            var discovery = await mikanBangumi.ResolveFreshAsync(
                identity.Identity.MikanId,
                sourcePage,
                preview.SourceProfileId,
                cancellationToken).ConfigureAwait(false);
            if (discovery.State == MikanBangumiDiscoveryStates.Failed)
            {
                return TypedResults.Conflict(Error(
                    discovery.FailureCode ?? "mikan_bgmid_discovery_failed",
                    "Mikan 对应 Bangumi 作品重新解析失败；未修改任务。"));
            }

            freshIdentity = new OtherFileReadaptationSourceIdentity(
                identity.Identity.MikanId,
                identity.Identity.SubGroupId,
                discovery.BangumiSubjectId);
        }

        var result = await store.StartAsync(
            taskId,
            DateTimeOffset.UtcNow,
            freshIdentity,
            cancellationToken).ConfigureAwait(false);
        return result switch
        {
            OtherFileReadaptationStartResult.Started => TypedResults.Ok(
                new OtherFileReadaptationStartResponse(
                    taskId,
                    "download_preparing",
                    files.Length)),
            OtherFileReadaptationStartResult.NotFound => TypedResults.NotFound(Error(
                "metadata_task_not_found",
                "Metadata task was not found.")),
            OtherFileReadaptationStartResult.ActiveLease => TypedResults.Conflict(Error(
                "metadata_task_active",
                "Metadata task has an active resolution lease.")),
            _ => TypedResults.Conflict(Error(
                "other_readaptation_not_eligible",
                "The task changed and can no longer re-adapt Other files.")),
        };
    }

    private static async Task<IResult> IgnoreOtherAttention(
        string taskId,
        OtherFileReadaptationStore readaptation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return TypedResults.BadRequest(Error("task_id_required", "taskId is required."));
        }

        var outcome = await readaptation.IgnoreAsync(
            taskId,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return outcome.Result switch
        {
            OtherAttentionIgnoreResult.Ignored => TypedResults.Ok(
                new OtherAttentionIgnoreResponse(taskId, "ignored", outcome.FileCount)),
            OtherAttentionIgnoreResult.NotFound => TypedResults.NotFound(Error(
                "metadata_task_not_found", "Metadata task was not found.")),
            OtherAttentionIgnoreResult.NothingToIgnore => TypedResults.Conflict(Error(
                "other_attention_empty", "The task has no Other files to ignore.")),
            _ => TypedResults.Conflict(Error(
                "other_attention_not_eligible",
                "Only organized tasks without an active readaptation can ignore Other handling.")),
        };
    }

    private static async Task<IResult> PreviewMixedMediaPostprocess(
        string taskId,
        MixedMediaPostprocessStore postprocess,
        CancellationToken cancellationToken)
    {
        var preview = await postprocess.PreviewAsync(taskId, cancellationToken)
            .ConfigureAwait(false);
        if (preview is null)
        {
            return TypedResults.NotFound(Error(
                "metadata_task_not_found", "Metadata task was not found."));
        }

        var eligible = preview.TaskStatus == "organized"
            && preview.MediaType == "tv"
            && !preview.HasActivePostprocess
            && preview.Files.Count > 0;
        var reason = eligible
            ? null
            : preview.TaskStatus != "organized"
                ? "任务尚未整理完成。"
                : preview.MediaType != "tv"
                    ? "只有按 TV 处理的任务需要 TV+Movie 后处理。"
                    : preview.HasActivePostprocess
                        ? "任务已有正在执行的后处理。"
                        : "没有可迁移的已整理视频文件。";
        return TypedResults.Ok(new MixedMediaPostprocessPreviewResponse(
            preview.TaskId,
            preview.Title,
            eligible,
            reason,
            preview.Files.Select(file => new MixedMediaPostprocessFileResponse(
                file.TaskFileId,
                file.SourceName,
                file.SizeBytes,
                file.Disposition,
                file.OtherReason,
                file.TmdbSeriesId,
                file.TmdbSeasonNumber,
                file.TmdbEpisodeNumber,
                file.MovieHint,
                File.Exists(file.SourceMediaPath)
                    && new FileInfo(file.SourceMediaPath).Length == file.SizeBytes)).ToArray()));
    }

    private static async Task<IResult> SearchTmdbMovies(
        [FromQuery] string? query,
        ITmdbMovieClient tmdb,
        CancellationToken cancellationToken)
    {
        var normalized = query?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 256)
        {
            return TypedResults.BadRequest(Error(
                "tmdb_movie_query_invalid", "Movie search query must contain 1 to 256 characters."));
        }

        try
        {
            var matches = await tmdb.SearchMoviesAsync(normalized, cancellationToken)
                .ConfigureAwait(false);
            return TypedResults.Ok(new TmdbMovieSearchResponse(
                normalized,
                matches.Take(20).Select(movie => new TmdbMovieSearchItemResponse(
                    movie.Id,
                    movie.Title,
                    movie.OriginalTitle,
                    movie.ReleaseDate,
                    movie.PosterPath)).ToArray()));
        }
        catch (TmdbClientException exception)
        {
            return TypedResults.Problem(
                statusCode: exception.Kind is MetadataFailureKind.Network
                    or MetadataFailureKind.RemoteService ? 503 : 422,
                title: "TMDB Movie search failed",
                detail: exception.SafeCode);
        }
    }

    private static async Task<IResult> StartMixedMediaPostprocess(
        string taskId,
        MixedMediaPostprocessRequest request,
        MixedMediaPostprocessStore postprocess,
        ITmdbMovieClient tmdb,
        CancellationToken cancellationToken)
    {
        var taskFileIds = request.SelectedTaskFileIds;
        if (taskFileIds.Count == 0 || request.TmdbMovieId <= 0)
        {
            return TypedResults.BadRequest(Error(
                "mixed_media_request_invalid",
                "task_file_ids and a positive tmdb_movie_id are required."));
        }

        TmdbMovie? movie;
        try
        {
            movie = await tmdb.GetMovieAsync(request.TmdbMovieId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TmdbClientException exception)
        {
            return TypedResults.Problem(
                statusCode: exception.Kind is MetadataFailureKind.Network
                    or MetadataFailureKind.RemoteService ? 503 : 422,
                title: "TMDB Movie validation failed",
                detail: exception.SafeCode);
        }
        if (movie is null || movie.Id != request.TmdbMovieId)
        {
            return TypedResults.UnprocessableEntity(Error(
                "tmdb_movie_not_found", "TMDB Movie could not be validated."));
        }

        var result = await postprocess.StartAsync(
            taskId,
            taskFileIds,
            movie,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return result switch
        {
            MixedMediaPostprocessResult.Started => TypedResults.Ok(
                new MixedMediaPostprocessStartResponse(
                    taskId, taskFileIds, request.TmdbMovieId, "downloaded")),
            MixedMediaPostprocessResult.NotFound => TypedResults.NotFound(Error(
                "metadata_task_not_found", "Metadata task was not found.")),
            MixedMediaPostprocessResult.FileNotEligible => TypedResults.Conflict(Error(
                "mixed_media_file_not_eligible",
                "The selected file is missing, changed, or not an eligible organized video.")),
            MixedMediaPostprocessResult.MovieAlreadyCompleted => TypedResults.Conflict(Error(
                "movie_already_completed", "This TMDB Movie is already completed.")),
            MixedMediaPostprocessResult.MovieClaimed => TypedResults.Conflict(Error(
                "movie_claimed_by_another_task", "This TMDB Movie is being handled by another task.")),
            _ => TypedResults.Conflict(Error(
                "mixed_media_postprocess_not_eligible",
                "The TV task is not currently eligible for mixed-media postprocessing.")),
        };
    }

    private static async Task<IResult> ApproveOtherFileReadaptationReview(
        string taskId,
        OtherFileReadaptationStore store,
        AiSeriesChangeReviewStore seriesChangeReviews,
        CancellationToken cancellationToken)
    {
        if (await seriesChangeReviews.GetPendingAsync(taskId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return TypedResults.Conflict(Error(
                "ai_tmdb_series_change_decision_required",
                "请先明确同意或拒绝 AI 提议的 TMDB Series 变更。"));
        }
        var result = await store.ApproveReviewAsync(
            taskId,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return result switch
        {
            OtherFileReadaptationReviewResult.Approved => TypedResults.Ok(
                new OtherFileReadaptationReviewResponse(taskId, "approved")),
            OtherFileReadaptationReviewResult.NotFound => TypedResults.NotFound(Error(
                "metadata_task_not_found", "Metadata task was not found.")),
            OtherFileReadaptationReviewResult.NotPending => TypedResults.Conflict(Error(
                "other_readaptation_review_not_pending", "任务当前不需要人工审核。")),
            _ => TypedResults.Conflict(Error(
                "other_readaptation_review_not_completed",
                "重新解析和整理尚未完成，不能确认人工审核。")),
        };
    }

    private static async Task<IResult> PreviewOtherFileReadaptationReview(
        string taskId,
        OtherFileReadaptationStore store,
        CancellationToken cancellationToken)
    {
        var preview = await store.GetReviewPreviewAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (preview is null)
        {
            return TypedResults.NotFound(Error(
                "metadata_task_not_found", "Metadata task was not found."));
        }

        if (preview.Files.Count == 0)
        {
            return TypedResults.Conflict(Error(
                "other_readaptation_review_snapshot_missing",
                "该任务没有可供人工审核的适配前后快照。"));
        }

        return TypedResults.Ok(new OtherFileReadaptationReviewPreviewResponse(
            preview.TaskId,
            preview.Title,
            preview.TaskStatus,
            preview.ReviewState,
            preview.ReviewState == "approved"
                ? "review_completed"
                : preview.TaskStatus == "organized" && preview.CompletedAtUtc is not null
                    ? "awaiting_review"
                    : "processing",
            preview.RequestedAtUtc,
            preview.CompletedAtUtc,
            preview.ReviewedAtUtc,
            preview.ReviewKind,
            preview.ReviewDecision,
            preview.Files.Select(file => new OtherFileReadaptationReviewFileResponse(
                file.TaskFileId,
                file.SourceName,
                file.BeforeDisposition,
                file.BeforeOtherReason,
                file.BeforeTmdbSeriesId,
                file.BeforeSeriesName,
                file.BeforeTmdbSeasonNumber,
                file.BeforeSeasonName,
                file.BeforeTmdbEpisodeNumber,
                file.BeforeEpisodeName,
                file.AfterDisposition,
                file.AfterOtherReason,
                file.AfterTmdbSeriesId,
                file.AfterSeriesName,
                file.AfterTmdbSeasonNumber,
                file.AfterSeasonName,
                file.AfterTmdbEpisodeNumber,
                file.AfterEpisodeName,
                file.AfterEpisodeStrategy,
                file.PreservedSharedSource,
                file.BeforeMediaPath,
                file.AfterMediaPath)).ToArray()));
    }

    private static async Task<IResult> AcceptAiSeriesChangeReview(
        string taskId,
        AiSeriesChangeReviewStore reviews,
        MikanManualSeriesMappingStore manualSeriesMappings,
        TmdbAuthority authority,
        OtherFileReadaptationStore readaptation,
        CancellationToken cancellationToken)
    {
        var proposal = await reviews.GetPendingAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (proposal is null)
        {
            return TypedResults.Conflict(Error(
                "ai_tmdb_series_change_review_not_pending",
                "任务没有待决定的 AI TMDB Series 变更。"));
        }

        var validation = await authority.ValidateEpisodeAsync(
            proposal.Proposed.Series.Id,
            proposal.Proposed.Season.SeasonNumber,
            proposal.Proposed.Episode.EpisodeNumber,
            cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            return TypedResults.UnprocessableEntity(Error(
                validation.Failure!.Code,
                "AI 提议的 TMDB Series / Season / Episode 重新验证失败，未修改任务。"));
        }

        var apply = await readaptation.ApplyManualOverrideAsync(
            taskId,
            proposal.TaskFileId,
            validation.Value!,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        if (apply is OtherFileReadaptationManualOverrideResult.NotFound
            or OtherFileReadaptationManualOverrideResult.NotEligible)
        {
            return TypedResults.Conflict(Error(
                "ai_tmdb_series_change_review_not_eligible",
                "任务尚未整理完成，或候选文件已不处于可审核的 Other 状态。"));
        }

        if (proposal.MikanId is > 0 && proposal.GroupId is > 0)
        {
            await manualSeriesMappings.UpsertAsync(
                proposal.MikanId.Value,
                proposal.GroupId.Value,
                proposal.ExpectedTmdbSeriesId,
                validation.Value!.Series.Id,
                validation.Value.Season.SeasonNumber,
                taskId,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
        }

        await reviews.AcceptAsync(taskId, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new AiSeriesChangeReviewDecisionResponse(
            taskId,
            "accepted",
            apply == OtherFileReadaptationManualOverrideResult.OrganizationQueued
                ? "organization_queued"
                : "duplicate_kept_in_other"));
    }

    private static async Task<Ok<MikanManualSeriesMappingListResponse>> ListMikanManualSeriesMappings(
        MikanManualSeriesMappingStore mappings,
        CancellationToken cancellationToken)
    {
        var values = await mappings.ListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new MikanManualSeriesMappingListResponse(
            values.Select(value => new MikanManualSeriesMappingItemResponse(
                value.MikanId,
                value.GroupId,
                value.ExpectedTmdbSeriesId,
                value.TmdbSeriesId,
                value.TmdbSeasonNumber,
                value.AcceptedFromTaskId,
                value.AcceptedAtUtc,
                value.UpdatedAtUtc)).ToArray()));
    }

    private static async Task<IResult> ListMikanPluginCallLogs(
        int? page,
        int? page_size,
        string? mode,
        string? result,
        MikanPluginCallLogStore logs,
        CancellationToken cancellationToken)
    {
        var effectivePage = page ?? 1;
        var effectivePageSize = page_size ?? 50;
        if (effectivePage < 1
            || effectivePageSize is < 1 or > 200
            || (!string.IsNullOrWhiteSpace(mode)
                && mode.Trim().ToLowerInvariant() is not ("single" or "all" or "selected" or "batch"))
            || (!string.IsNullOrWhiteSpace(result)
                && result.Trim().ToLowerInvariant() is not ("success" or "partial" or "failed")))
        {
            return TypedResults.BadRequest(Error(
                "mikan_plugin_call_log_filter_invalid",
                "Mikan plugin call log filter is invalid."));
        }

        var pageResult = await logs.ListAsync(
            effectivePage, effectivePageSize, mode, result, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new MikanPluginCallLogListResponse(
            pageResult.Page,
            pageResult.PageSize,
            pageResult.TotalCount,
            pageResult.Items.Select(entry => new MikanPluginCallLogResponse(
                entry.Id,
                entry.Endpoint,
                entry.Mode,
                entry.MediaType,
                entry.Result,
                entry.RequestedCount,
                entry.AcceptedCount,
                entry.RejectedCount,
                entry.FailureCode,
                entry.DurationMilliseconds,
                entry.StartedAtUtc,
                entry.CompletedAtUtc,
                entry.Items.Select(item => new MikanPluginCallLogItemResponse(
                    item.Index,
                    item.Title,
                    item.TaskId,
                    item.MikanId,
                    item.GroupId,
                    item.Status,
                    item.FailureCode)).ToArray())).ToArray()));
    }

    private static async Task<IResult> ListU2PluginCallLogs(
        int? page,
        int? page_size,
        string? result,
        U2PluginCallLogStore logs,
        CancellationToken cancellationToken)
    {
        var effectivePage = page ?? 1;
        var effectivePageSize = page_size ?? 50;
        if (effectivePage < 1
            || effectivePageSize is < 1 or > 200
            || (!string.IsNullOrWhiteSpace(result)
                && result.Trim().ToLowerInvariant() is not ("success" or "partial" or "failed")))
        {
            return TypedResults.BadRequest(Error(
                "u2_plugin_call_log_filter_invalid",
                "U2 plugin call log filter is invalid."));
        }

        var pageResult = await logs.ListAsync(
            effectivePage, effectivePageSize, result, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new U2PluginCallLogListResponse(
            pageResult.Page,
            pageResult.PageSize,
            pageResult.TotalCount,
            pageResult.Items.Select(entry => new U2PluginCallLogResponse(
                entry.Id,
                entry.Endpoint,
                entry.SourceProfileId,
                entry.Result,
                entry.RequestedCount,
                entry.AcceptedCount,
                entry.RejectedCount,
                entry.FailureCode,
                entry.DurationMilliseconds,
                entry.StartedAtUtc,
                entry.CompletedAtUtc,
                entry.Items.Select(item => new U2PluginCallLogItemResponse(
                    item.Index,
                    item.U2Id,
                    item.Title,
                    item.DetailsUrl,
                    item.AniDbId,
                    item.CategoryId,
                    item.CategoryName,
                    item.MediaType,
                    item.TaskId,
                    item.Status,
                    item.FailureCode)).ToArray())).ToArray()));
    }

    private static async Task<IResult> DeleteMikanManualSeriesMapping(
        int mikanId,
        int groupId,
        MikanManualSeriesMappingStore mappings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mappings.DeleteAsync(mikanId, groupId, cancellationToken).ConfigureAwait(false)
                ? TypedResults.NoContent()
                : TypedResults.NotFound(Error(
                    "mikan_manual_series_mapping_not_found",
                    "Mikan manual TMDB Series mapping was not found."));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return TypedResults.BadRequest(Error(
                "mikan_manual_series_mapping_key_invalid",
                exception.Message));
        }
    }

    private static async Task<Ok<MikanPublishGroupListResponse>> ListMikanPublishGroups(
        MikanPublishGroupStore groups,
        CancellationToken cancellationToken)
    {
        var items = await groups.ListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new MikanPublishGroupListResponse(items.Select(item =>
            new MikanPublishGroupResponse(
                item.GroupId, item.GroupName, item.NameSource, item.SourceProfileId,
                item.State, item.FailureCode, item.FetchedAtUtc, item.NextAttemptAtUtc,
                item.UpdatedAtUtc, item.Revision)).ToArray()));
    }

    private static async Task<IResult> UpdateMikanPublishGroup(
        int groupId,
        MikanPublishGroupWriteRequest request,
        MikanPublishGroupStore groups,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await groups.UpdateManualAsync(
                groupId, request.GroupName, request.ExpectedRevision,
                DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            return result switch
            {
                MikanPublishGroupUpdateResult.Updated => TypedResults.NoContent(),
                MikanPublishGroupUpdateResult.NotFound => TypedResults.NotFound(Error(
                    "mikan_publish_group_not_found", "Mikan publish group was not found.")),
                _ => TypedResults.Conflict(Error(
                    "mikan_publish_group_revision_conflict", "Mikan publish group changed; reload and retry.")),
            };
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            return TypedResults.BadRequest(Error("mikan_publish_group_name_invalid", exception.Message));
        }
    }

    private static async Task<IResult> RefreshMikanPublishGroup(
        int groupId,
        MikanPublishGroupRefreshRequest request,
        MikanPublishGroupStore groups,
        MikanPublishGroupResolver resolver,
        CancellationToken cancellationToken)
    {
        var result = await groups.RequestRefreshAsync(
            groupId, request.ExpectedRevision, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        if (result == MikanPublishGroupUpdateResult.NotFound)
            return TypedResults.NotFound(Error("mikan_publish_group_not_found", "Mikan publish group was not found."));
        if (result == MikanPublishGroupUpdateResult.RevisionConflict)
            return TypedResults.Conflict(Error("mikan_publish_group_revision_conflict", "Mikan publish group changed; reload and retry."));
        await resolver.RunOnceAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RejectAiSeriesChangeReview(
        string taskId,
        AiSeriesChangeReviewStore reviews,
        CancellationToken cancellationToken)
    {
        var result = await reviews.RejectAsync(
            taskId,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return result switch
        {
            AiSeriesChangeReviewDecisionResult.Updated => TypedResults.Ok(
                new AiSeriesChangeReviewDecisionResponse(taskId, "rejected", "kept_in_other")),
            AiSeriesChangeReviewDecisionResult.NotFound => TypedResults.NotFound(Error(
                "metadata_task_not_found", "Metadata task was not found.")),
            _ => TypedResults.Conflict(Error(
                "ai_tmdb_series_change_review_not_pending",
                "任务没有待决定的 AI TMDB Series 变更。")),
        };
    }

    private static async Task<IResult> ApplyOtherFileReadaptationManualOverride(
        string taskId,
        string taskFileId,
        OtherFileReadaptationManualOverrideRequest request,
        TmdbAuthority authority,
        OtherFileReadaptationStore store,
        CancellationToken cancellationToken)
    {
        var validation = await authority.ValidateEpisodeAsync(
            request.TmdbSeriesId,
            request.TmdbSeasonNumber,
            request.TmdbEpisodeNumber,
            cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            var failure = validation.Failure!;
            var status = failure.Kind switch
            {
                MetadataFailureKind.InvalidInput => StatusCodes.Status400BadRequest,
                MetadataFailureKind.SemanticNoMatch => StatusCodes.Status422UnprocessableEntity,
                MetadataFailureKind.Network
                    or MetadataFailureKind.RemoteService
                    or MetadataFailureKind.Authentication
                    or MetadataFailureKind.Configuration => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status502BadGateway,
            };
            return TypedResults.Json(
                Error(failure.Code, "TMDB Series / Season / Episode 验证失败；未修改任务和文件。"),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: status);
        }

        var canonical = validation.Value!;
        var result = await store.ApplyManualOverrideAsync(
            taskId,
            taskFileId,
            canonical,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        if (result == OtherFileReadaptationManualOverrideResult.NotFound)
        {
            return TypedResults.NotFound(Error(
                "metadata_task_not_found", "Metadata task was not found."));
        }
        if (result == OtherFileReadaptationManualOverrideResult.NotEligible)
        {
            return TypedResults.Conflict(Error(
                "other_readaptation_manual_override_not_eligible",
                "文件已不处于可人工修正的待审核 Other 状态。"));
        }

        var queued = result == OtherFileReadaptationManualOverrideResult.OrganizationQueued;
        return TypedResults.Ok(new OtherFileReadaptationManualOverrideResponse(
            taskId,
            taskFileId,
            queued ? "organization_queued" : "duplicate_kept_in_other",
            canonical.Series.Id,
            canonical.CanonicalSeriesName,
            canonical.Season.SeasonNumber,
            canonical.Season.Name,
            canonical.Episode.EpisodeNumber,
            canonical.Episode.Name,
            queued ? "move_or_copy_from_other" : "kept_in_other_no_auto_delete"));
    }

    private static string? ReadaptationDenialReason(
        OtherFileReadaptationPreview preview,
        OtherFileReadaptationFileResponse[] files)
    {
        if (preview.HasActiveResolutionLease)
        {
            return "当前任务正在匹配，不能重复提交 Other 重新适配。";
        }

        if (preview.TaskStatus != "organized")
        {
            return "仅已整理完成的任务可重新适配 Other 文件。";
        }

        if (preview.FileStrategy is not ("move" or "wait_move"))
        {
            return "首版 Other 重新适配仅支持 move / wait_move 文件策略。";
        }

        if (files.Length == 0)
        {
            return "任务没有可重新适配的 Other 文件。";
        }

        if (files.Any(file => !file.SourceAvailable))
        {
            return "至少一个 Other 文件不存在或大小已变化；未修改任务。";
        }

        return null;
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
        [FromQuery(Name = "file_state")] string? fileState,
        [FromQuery(Name = "review_state")] string? reviewState,
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
        var resolvedFileState = string.IsNullOrWhiteSpace(fileState)
            ? "all"
            : fileState.Trim().ToLowerInvariant();
        var resolvedReviewState = string.IsNullOrWhiteSpace(reviewState)
            ? "all"
            : reviewState.Trim().ToLowerInvariant();
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
                or "manual" or "skipped" or "fallback" or "active" or "resolved" or "other")
            || resolvedFileState is not ("all" or "has_other")
            || resolvedReviewState is not ("all" or "pending" or "approved" or "not_required"))
        {
            return TypedResults.BadRequest(Error(
                "metadata_task_filter_invalid",
                "Metadata task filters, sorting or pagination are invalid."));
        }

        IEnumerable<MetadataTaskListProjection> filtered =
            await resolutions.ListTasksAsync(500, cancellationToken).ConfigureAwait(false);
        var attention = await resolutions.GetTaskAttentionSummaryAsync(cancellationToken)
            .ConfigureAwait(false);
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

        if (resolvedFileState == "has_other")
        {
            filtered = filtered.Where(item => item.OtherFileCount > 0);
        }

        if (resolvedReviewState != "all")
        {
            filtered = filtered.Where(item => string.Equals(
                item.ReadaptationReviewState,
                resolvedReviewState,
                StringComparison.Ordinal));
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
            new MetadataTaskAttentionSummaryResponse(
                attention.OtherTaskCount,
                attention.FailedTaskCount,
                attention.ReviewPendingTaskCount),
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
                attempt.RunCompletedAtUtc,
                attempt.AiUsage?.Model,
                attempt.AiUsage?.PromptTokens,
                attempt.AiUsage?.CompletionTokens,
                attempt.AiUsage?.TotalTokens,
                attempt.AiUsage?.RequestCount,
                attempt.AiUsage?.ToolCallCount)).ToArray()));
    }

    private static async Task<IResult> AiInvocationLogs(
        [FromQuery] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? stage,
        [FromQuery] string? result,
        [FromQuery] string? model,
        [FromQuery(Name = "error_category")] string? errorCategory,
        [FromQuery(Name = "from_utc")] DateTimeOffset? fromUtc,
        [FromQuery(Name = "to_utc")] DateTimeOffset? toUtc,
        MetadataResolutionStore resolutions,
        AiMetadataDebugTraceStore debugTraces,
        CancellationToken cancellationToken)
    {
        var resolvedPage = page ?? 1;
        var resolvedPageSize = pageSize ?? 25;
        if (resolvedPage < 1 || resolvedPageSize is < 1 or > 100)
        {
            return TypedResults.BadRequest(Error(
                "ai_log_paging_invalid",
                "AI log page must be positive and page_size must be between 1 and 100."));
        }

        if (search?.Trim().Length > 200 || model?.Trim().Length > 256)
        {
            return TypedResults.BadRequest(Error(
                "ai_log_filter_too_long",
                "AI log search must be at most 200 characters and model at most 256 characters."));
        }

        var normalizedStage = string.IsNullOrWhiteSpace(stage)
            ? null
            : stage.Trim().ToLowerInvariant();
        if (normalizedStage is not null && normalizedStage is not ("series" or "season" or "episode"))
        {
            return TypedResults.BadRequest(Error(
                "ai_log_stage_invalid",
                "AI log stage must be series, season, or episode."));
        }

        var normalizedResult = string.IsNullOrWhiteSpace(result)
            ? null
            : result.Trim().ToLowerInvariant();
        if (normalizedResult is not null
            && normalizedResult is not (
                "matched" or "not_matched" or "error" or "failed" or "skipped" or "not_applicable"))
        {
            return TypedResults.BadRequest(Error(
                "ai_log_result_invalid",
                "AI log result filter is invalid."));
        }

        var normalizedErrorCategory = string.IsNullOrWhiteSpace(errorCategory)
            ? null
            : errorCategory.Trim().ToLowerInvariant();
        if (normalizedErrorCategory is not null
            && normalizedErrorCategory is not ("output_format" or "other"))
        {
            return TypedResults.BadRequest(Error(
                "ai_log_error_category_invalid",
                "AI log error_category must be output_format or other."));
        }

        if (fromUtc is not null && toUtc is not null && fromUtc > toUtc)
        {
            return TypedResults.BadRequest(Error(
                "ai_log_time_range_invalid",
                "AI log from_utc must not be after to_utc."));
        }

        var log = await resolutions.ListAiInvocationLogsAsync(
            new MetadataAiInvocationLogFilter(
                resolvedPage,
                resolvedPageSize,
                search,
                normalizedStage,
                normalizedResult,
                model,
                normalizedErrorCategory,
                fromUtc,
                toUtc),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new AiInvocationLogListResponse(
            log.Filter.Page,
            log.Filter.PageSize,
            log.Summary.TotalItems,
            new AiInvocationLogSummaryResponse(
                log.Summary.MatchedItems,
                log.Summary.FailedItems,
                log.Summary.OutputFormatFailedItems,
                log.Summary.PromptTokens,
                log.Summary.CompletionTokens,
                log.Summary.TotalTokens,
                log.Summary.RequestCount,
                log.Summary.ToolCallCount),
            log.Items.Select(item => new AiInvocationLogItemResponse(
                item.AttemptId,
                item.RunId,
                item.TaskId,
                item.Title,
                item.SourceId,
                item.MikanId,
                item.BangumiSubjectId,
                item.TmdbSeriesId,
                item.TmdbSeasonNumber,
                item.RunStatus,
                item.Stage,
                item.Strategy,
                item.Result,
                item.ErrorCode,
                item.ErrorCategory,
                item.AiTriggerReason,
                item.Reason,
                item.Retryable,
                item.DurationMilliseconds,
                item.CreatedAtUtc,
                item.Usage.Model,
                item.Usage.PromptTokens,
                item.Usage.CompletionTokens,
                item.Usage.TotalTokens,
                item.Usage.RequestCount,
                item.Usage.ToolCallCount,
                item.ValidatedEpisodes.Select(episode =>
                    new AiInvocationValidatedEpisodeResponse(
                        episode.TmdbSeriesId,
                        episode.TmdbSeasonNumber,
                        episode.TmdbEpisodeNumber,
                        episode.EpisodeName)).ToArray(),
                debugTraces.Exists(item.RunId))).ToArray()));
    }

    private static async Task<IResult> AiInvocationDebug(
        string runId,
        AiMetadataDebugTraceStore debugTraces,
        CancellationToken cancellationToken)
    {
        var json = await debugTraces.ReadAsync(runId, cancellationToken)
            .ConfigureAwait(false);
        return json is null
            ? TypedResults.NotFound()
            : Results.Text(json, "application/json", Encoding.UTF8);
    }

    private static IResult DeleteAiInvocationDebug(
        string runId,
        AiMetadataDebugTraceStore debugTraces) =>
        debugTraces.Delete(runId)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

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
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null)
                : new MetadataTaskAiItem(
                    ai.Result,
                    ai.Stage,
                    ai.ErrorCode,
                    ai.Reason,
                    ai.Result == "matched" ? "tmdb_verified" : "not_established",
                    ai.DurationMilliseconds,
                    ai.AttemptedAtUtc,
                    ai.Usage?.Model,
                    ai.Usage?.PromptTokens,
                    ai.Usage?.CompletionTokens,
                    ai.Usage?.TotalTokens,
                    ai.Usage?.RequestCount,
                    ai.Usage?.ToolCallCount),
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
        [FromQuery] string? search,
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

        if (search?.Any(char.IsControl) == true)
        {
            return TypedResults.BadRequest(Error(
                "library_search_invalid",
                "Library search must be at most 200 characters without control characters."));
        }

        var resolvedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (resolvedSearch is { Length: > 200 })
        {
            return TypedResults.BadRequest(Error(
                "library_search_invalid",
                "Library search must be at most 200 characters without control characters."));
        }

        if (!TryParseLibrarySort(sort, allowEpisodeChangedAt: true, out var resolvedSort))
        {
            return TypedResults.BadRequest(Error(
                "library_sort_invalid",
                "Library sort must be last_updated, episode_changed_at, name, air_date or added_at."));
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
                resolvedDirection,
                resolvedSearch),
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
                    item.LastEpisodeChangedAt,
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

    private static async Task<IResult> LibraryMovies(
        [FromQuery] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        AnimeLibraryStore library,
        CancellationToken cancellationToken)
    {
        var resolvedPage = page ?? 1;
        var resolvedPageSize = pageSize ?? 24;
        if (resolvedPage < 1)
        {
            return TypedResults.BadRequest(Error("library_page_invalid", "Library page must be a positive integer."));
        }

        if (resolvedPageSize is < 1 or > 100)
        {
            return TypedResults.BadRequest(Error("library_page_size_invalid", "Library page size must be between 1 and 100."));
        }

        var resolvedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (resolvedSearch is { Length: > 200 } || resolvedSearch?.Any(char.IsControl) == true)
        {
            return TypedResults.BadRequest(Error("library_search_invalid", "Library search must be at most 200 characters without control characters."));
        }

        if (!TryParseLibrarySort(sort, allowEpisodeChangedAt: false, out var resolvedSort))
        {
            return TypedResults.BadRequest(Error("library_sort_invalid", "Library sort must be last_updated, name, air_date or added_at."));
        }

        if (!TryParseLibraryDirection(direction, out var resolvedDirection))
        {
            return TypedResults.BadRequest(Error("library_direction_invalid", "Library direction must be asc or desc."));
        }

        var result = await library.ListMoviesAsync(
            new AnimeSeasonListQuery(resolvedPage, resolvedPageSize, resolvedSort, resolvedDirection, resolvedSearch),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new AnimeMovieListResponse(
            result.Page,
            result.PageSize,
            result.TotalItems,
            LibrarySortName(resolvedSort),
            resolvedDirection == AnimeLibrarySortDirection.Ascending ? "asc" : "desc",
            result.Items.Select(item => new AnimeMovieListItemResponse(
                $"tmdb:movie:{item.TmdbMovieId}",
                item.TmdbMovieId,
                item.Title,
                item.OriginalTitle,
                item.PosterPath,
                $"/api/v1/library/movie-covers/{item.TmdbMovieId}",
                item.ReleaseDate,
                item.AddedAt,
                item.LastUpdatedAt,
                item.Completed,
                item.DownloadSourceId,
                item.CompletedAtUtc,
                item.MediaPathKnown)).ToArray()));
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

    private static IResult RestartRuntime(IHostApplicationLifetime lifetime)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            lifetime.StopApplication();
        });
        return Results.Accepted(
            value: new RuntimeRestartResponse(true, "AnimeGoNet 将在当前请求返回后停止；请由服务管理器重新启动."));
    }

    private static async Task<IResult> ImportExternalMedia(
        AnimeGoOptions options,
        ExternalMediaImportStore importer,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await importer.ScanAllAsync(
                options.Paths.SavePath,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ExternalMediaResponse(result));
        }
        catch (IOException)
        {
            return TypedResults.Conflict(Error(
                "external_media_scan_failed",
                "The configured media library could not be scanned safely."));
        }
        catch (UnauthorizedAccessException)
        {
            return TypedResults.Conflict(Error(
                "external_media_scan_denied",
                "The configured media library could not be read."));
        }
    }

    private static async Task<IResult> ImportSubtitleArchive(
        HttpContext context,
        [FromQuery] int tmdbSeriesId,
        [FromQuery] int seasonNumber,
        AnimeLibraryStore library,
        SubtitleArchiveImportService importer,
        AnimeGoOptions options,
        CancellationToken cancellationToken)
    {
        if (tmdbSeriesId <= 0 || seasonNumber <= 0)
        {
            return TypedResults.BadRequest(Error("subtitle_import_identity_invalid",
                "TMDB Series ID and season number must be positive."));
        }
        var mediaType = context.Request.ContentType?.Split(';', 2)[0].Trim();
        if (!SubtitleArchiveContentTypes.IsSupported(mediaType))
        {
            return TypedResults.BadRequest(Error("subtitle_import_content_type_invalid",
                "字幕压缩包必须以 ZIP、RAR、7z、TAR、GZ、BZ2 或 XZ 请求体上传。"));
        }
        if (context.Request.ContentLength is > 512L * 1024 * 1024)
        {
            return Results.Json(Error("subtitle_import_archive_too_large", "字幕压缩包不能超过 512 MiB。"),
                ApiJsonContext.Default.ApiErrorResponse, statusCode: StatusCodes.Status413PayloadTooLarge);
        }
        var detail = await library.GetSeasonAsync(tmdbSeriesId, seasonNumber, cancellationToken)
            .ConfigureAwait(false);
        if (detail is null)
        {
            return TypedResults.NotFound(Error("library_season_not_found", "指定季度不存在。"));
        }
        try
        {
            var result = await importer.ImportAsync(
                context.Request.Body,
                SubtitleArchiveNameCodec.Decode(
                    context.Request.Headers["X-AnimeGo-Archive-Name-Encoded"].ToString(),
                    context.Request.Headers["X-AnimeGo-Archive-Name"].ToString()),
                tmdbSeriesId,
                seasonNumber,
                detail.Season.DisplayName,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new SubtitleArchiveImportResponse(
                result.SessionId, result.ArchiveName, result.TmdbSeriesId, result.SeasonNumber,
                result.SeriesName, result.Candidates));
        }
        catch (InvalidDataException exception)
        {
            return TypedResults.BadRequest(Error("subtitle_import_archive_invalid", exception.Message));
        }
    }

    private static async Task<IResult> ConfirmSubtitleArchive(
        string sessionId,
        SubtitleArchiveConfirmRequest request,
        SubtitleArchiveImportService importer,
        AnimeGoOptions options,
        CancellationToken cancellationToken)
    {
        if (request.Assignments is null)
        {
            return TypedResults.BadRequest(Error("subtitle_import_assignments_missing", "请至少提交一个字幕匹配项。"));
        }
        var result = await importer.ConfirmAsync(
            sessionId,
            request.Assignments,
            options.Paths.SavePath,
            cancellationToken).ConfigureAwait(false);
        return result is null
            ? TypedResults.NotFound(Error("subtitle_import_session_not_found", "字幕导入会话已过期或不存在。"))
            : TypedResults.Ok(new SubtitleArchiveConfirmResponse(
                result.SessionId, result.ImportedCount, result.ExtrasCount, result.ImportedPaths));
    }

    private static async Task<IResult> AiMatchSubtitleArchive(
        string sessionId,
        SubtitleArchiveImportService importer,
        SubtitleArchiveAiMatchService aiMatcher,
        CancellationToken cancellationToken)
    {
        var session = await importer.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return TypedResults.NotFound(Error("subtitle_import_session_not_found", "字幕导入会话已过期或不存在。"));
        }
        try
        {
            var response = await aiMatcher.MatchAsync(session, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new SubtitleArchiveAiMatchResponse(
                SubtitleAiPrompt.Version, response.Assignments, response.Reason, response.Usage));
        }
        catch (AiMetadataMatcherException exception)
        {
            return TypedResults.BadRequest(Error(exception.SafeCode, "字幕 AI 匹配失败。"));
        }
    }

    private static async Task<IResult> ImportExternalMediaSeason(
        int tmdbSeriesId,
        int seasonNumber,
        AnimeGoOptions options,
        ExternalMediaImportStore importer,
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

        try
        {
            var result = await importer.ScanSeasonAsync(
                options.Paths.SavePath,
                tmdbSeriesId,
                seasonNumber,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return result is null
                ? TypedResults.NotFound(Error(
                    "library_season_not_found",
                    "The requested TMDB season was not found in the local library."))
                : TypedResults.Ok(ExternalMediaResponse(result));
        }
        catch (IOException)
        {
            return TypedResults.Conflict(Error(
                "external_media_scan_failed",
                "The configured media library could not be scanned safely."));
        }
        catch (UnauthorizedAccessException)
        {
            return TypedResults.Conflict(Error(
                "external_media_scan_denied",
                "The configured media library could not be read."));
        }
    }

    private static ExternalMediaImportResponse ExternalMediaResponse(
        ExternalMediaImportResult result) =>
        new(
            result.ScannedSeasonCount,
            result.CandidateFileCount,
            result.ImportedCount,
            result.AlreadyRecordedCount,
            result.SkippedCount,
            result.Items.Select(item => new ExternalMediaImportItemResponse(
                item.TmdbSeriesId,
                item.TmdbSeasonNumber,
                item.TmdbEpisodeNumber,
                item.RelativePath,
                item.Status,
                item.ReasonCode)).ToArray());

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
            season.LastEpisodeChangedAt,
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
                episode.MediaPathKnown,
                episode.GroupId,
                episode.GroupName)).ToArray(),
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
            detail.Audit.MikanBindings.Select(value =>
                new AnimeSeasonMikanBindingResponse(
                    value.SourceProfileId,
                    value.MikanId,
                    value.GroupId,
                    value.LastUsedAtUtc)).ToArray(),
            detail.Audit.RelatedTaskTotal,
            detail.Audit.RelatedTasksTruncated,
            detail.Audit.RelatedTasks.Select(value =>
                new AnimeSeasonRelatedTaskResponse(
                    value.TaskId,
                    value.Title,
                    value.SourceId,
                    value.Status,
                    value.MikanId,
                    value.GroupId,
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

    private static async Task<IResult> PreviewMikanSeasonCompletion(
        int tmdbSeriesId,
        int seasonNumber,
        MikanSeasonCompletionPreviewRequest request,
        MikanSeasonCompletionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var preview = await service.PreviewAsync(
                tmdbSeriesId,
                seasonNumber,
                request.SourceProfileId ?? string.Empty,
                request.MikanId,
                request.GroupId,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new MikanSeasonCompletionPreviewResponse(
                preview.TmdbSeriesId,
                preview.TmdbSeasonNumber,
                preview.ResourceRevision,
                preview.SourceProfileId,
                preview.MikanId,
                preview.GroupId,
                preview.OffsetSource,
                preview.EpisodeOffset,
                preview.Items.Select(item => new MikanSeasonCompletionCandidateResponse(
                    item.CandidateId,
                    item.Title,
                    item.Length,
                    item.PublishedDate,
                    item.SourceEpisodeKind,
                    item.SourceEpisode,
                    item.TargetEpisode,
                    item.Status,
                    item.DefaultSelected)).ToArray()));
        }
        catch (MikanSeasonCompletionException exception)
        {
            return TypedResults.BadRequest(Error(exception.Code, exception.Message));
        }
        catch (RssFeedException exception)
        {
            return TypedResults.BadRequest(Error(exception.Code, "Mikan RSS preview failed."));
        }
    }

    private static async Task<IResult> DiscoverMikanSeasonCompletionGroups(
        int tmdbSeriesId,
        int seasonNumber,
        MikanSeasonCompletionGroupDiscoveryRequest request,
        MikanSeasonCompletionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var discovery = await service.DiscoverGroupsAsync(
                tmdbSeriesId,
                seasonNumber,
                request.SourceProfileId ?? string.Empty,
                request.MikanId,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new MikanSeasonCompletionGroupDiscoveryResponse(
                discovery.SourceProfileId,
                discovery.MikanId,
                discovery.Groups.Select(group => new MikanSeasonCompletionGroupResponse(
                    group.GroupId,
                    group.Name,
                    group.PreviouslyUsed)).ToArray()));
        }
        catch (MikanSeasonCompletionException exception)
        {
            return TypedResults.BadRequest(Error(exception.Code, exception.Message));
        }
    }

    private static async Task<IResult> ConfirmMikanSeasonCompletion(
        int tmdbSeriesId,
        int seasonNumber,
        MikanSeasonCompletionConfirmRequest request,
        MikanSeasonCompletionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ConfirmAsync(
                tmdbSeriesId,
                seasonNumber,
                request.SourceProfileId ?? string.Empty,
                request.MikanId,
                request.GroupId,
                request.ExpectedResourceRevision ?? string.Empty,
                request.SelectedCandidateIds ?? [],
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(result);
        }
        catch (MikanSeasonCompletionException exception)
        {
            var status = exception.Code is "mikan_completion_library_changed"
                or "mikan_completion_feed_changed"
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
            return Results.Json(
                Error(exception.Code, exception.Message),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: status);
        }
        catch (RssFeedException exception)
        {
            return TypedResults.BadRequest(Error(exception.Code, "Mikan RSS import failed."));
        }
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

    private static async Task<IResult> LibraryMovieCover(
        int tmdbMovieId,
        AnimeCoverService covers,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (tmdbMovieId <= 0)
        {
            return TypedResults.BadRequest(Error("library_movie_id_invalid", "TMDB Movie ID must be a positive integer."));
        }

        var cover = await covers.GetMovieAsync(tmdbMovieId, cancellationToken).ConfigureAwait(false);
        if (cover is null)
        {
            return TypedResults.NotFound(Error("library_movie_not_found", "The requested TMDB movie was not found in the local library."));
        }

        context.Response.Headers["X-AnimeGoNet-Cover-Source"] = cover.Source;
        context.Response.Headers["X-AnimeGoNet-Cover-Cache"] = cover.CacheHit ? "hit" : "miss";
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

    private static bool TryParseLibrarySort(
        string? value,
        bool allowEpisodeChangedAt,
        out AnimeLibrarySort sort)
    {
        sort = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "last_updated" => AnimeLibrarySort.LastUpdated,
            "name" => AnimeLibrarySort.Name,
            "air_date" => AnimeLibrarySort.AirDate,
            "added_at" => AnimeLibrarySort.AddedAt,
            "episode_changed_at" when allowEpisodeChangedAt => AnimeLibrarySort.EpisodeChangedAt,
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
            AnimeLibrarySort.EpisodeChangedAt => "episode_changed_at",
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

    private static async Task<Ok<U2PluginIngestResponse>> IngestU2Plugin(
        U2PluginIngestRequest request,
        UnifiedIngestProcessor processor,
        SourceProfileStore profiles,
        U2PluginCallLogStore audit,
        CancellationToken cancellationToken)
    {
        const string endpoint = "/api/v1/plugins/inner_plugin_u2/ingest";
        var startedAt = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        var sourceProfileId = string.IsNullOrWhiteSpace(request.SourceProfileId)
            ? "u2"
            : request.SourceProfileId.Trim().ToLowerInvariant();
        var requestedItems = request.Items ?? [];
        var responses = new List<U2PluginIngestItemResponse>();
        var auditItems = new List<U2PluginCallLogItem>();

        var requestFailure = request.SchemaVersion != 1
            ? "u2_schema_version_unsupported"
            : sourceProfileId.Length > 128
                ? "u2_source_profile_invalid"
                : requestedItems.Count is < 1 or > 100
                    ? "u2_item_count_invalid"
                    : null;
        var profile = requestFailure is null
            ? await profiles.GetEnabledAsync(sourceProfileId, cancellationToken).ConfigureAwait(false)
            : null;
        if (requestFailure is null && profile is null)
        {
            requestFailure = "u2_source_profile_missing";
        }
        else if (requestFailure is null
            && !string.Equals(profile!.Adapter, "u2", StringComparison.OrdinalIgnoreCase))
        {
            requestFailure = "u2_source_profile_adapter_invalid";
        }

        for (var index = 0; index < requestedItems.Count; index++)
        {
            var item = requestedItems[index];
            IReadOnlyList<string> validation = requestFailure is null
                ? ValidateU2PluginItem(item)
                : [requestFailure];
            UnifiedIngestItemResult? result = null;
            if (validation.Count == 0 && item is not null)
            {
                var command = new IngestItemCommand(
                    item.TorrentUrl,
                    new IngestItemInfo(
                        item.Title,
                        null,
                        item.U2Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        item.AniDbId is > 0 ? $"anidb:{item.AniDbId.Value}" : null,
                        null,
                        null,
                        null,
                        null,
                        item.AniDbId,
                        null,
                        null,
                        item.MediaType));
                result = await processor.ProcessAsync(
                    sourceProfileId,
                    command,
                    requireModernMetadata: true,
                    cancellationToken).ConfigureAwait(false);
                validation = result.Errors;
            }

            var status = result?.Status ?? "rejected";
            var failureCode = validation.Count == 0 ? null : U2FailureCode(validation[0]);
            responses.Add(new U2PluginIngestItemResponse(
                index,
                item is { U2Id: > 0 } ? item.U2Id : null,
                status,
                result?.IngestId,
                result?.SourceProfileId,
                result?.SourceProfileRevision,
                result?.DownloaderId,
                validation));
            auditItems.Add(new U2PluginCallLogItem(
                index,
                item is { U2Id: > 0 } ? item.U2Id : null,
                NormalizeU2AuditText(item?.Title, 1000) ?? "(missing title)",
                NormalizeU2AuditDetailsUrl(item?.DetailsUrl, item?.U2Id),
                item?.AniDbId is > 0 ? item.AniDbId : null,
                item?.Category?.Id is > 0 ? item.Category.Id : null,
                NormalizeU2AuditText(item?.Category?.Name, 100),
                AnimeGoNet.Core.Media.MediaTypes.TryNormalize(item?.MediaType, out var mediaType)
                    ? mediaType
                    : "unknown",
                result?.IngestId,
                status,
                failureCode));
        }

        var accepted = responses.Count(item => item.IngestId is not null);
        var rejected = responses.Count - accepted;
        var callResult = accepted == responses.Count && responses.Count > 0
            ? "success"
            : accepted > 0 ? "partial" : "failed";
        var callFailure = auditItems.FirstOrDefault(item => item.FailureCode is not null)?.FailureCode
            ?? requestFailure;
        var completedAt = DateTimeOffset.UtcNow;
        await audit.RecordAsync(new U2PluginCallLog(
            Guid.NewGuid().ToString("N"),
            endpoint,
            sourceProfileId,
            callResult,
            requestedItems.Count,
            accepted,
            rejected,
            callFailure,
            timer.ElapsedMilliseconds,
            startedAt,
            completedAt,
            auditItems), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new U2PluginIngestResponse(
            "inner_plugin_u2",
            1,
            sourceProfileId,
            accepted,
            rejected,
            responses));
    }

    private static List<string> ValidateU2PluginItem(U2PluginIngestItemRequest? item)
    {
        var errors = new List<string>();
        if (item is null) return ["u2_item_required"];
        if (item.U2Id <= 0) errors.Add("u2id_invalid");
        if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 1000)
            errors.Add("u2_title_invalid");
        if (item.AniDbId is <= 0) errors.Add("u2_anidbid_invalid");
        if (item.Category?.Id is <= 0) errors.Add("u2_category_id_invalid");
        if (item.Category?.Name is { Length: > 100 }) errors.Add("u2_category_name_invalid");
        if (!AnimeGoNet.Core.Media.MediaTypes.TryNormalize(item.MediaType, out _))
            errors.Add("u2_media_type_invalid");

        if (!TryU2Url(item.DetailsUrl, "/details.php", item.U2Id, out var details))
            errors.Add("u2_details_url_invalid");
        if (!TryU2Url(item.TorrentUrl, "/download.php", item.U2Id, out var torrent))
        {
            errors.Add("u2_torrent_url_invalid");
        }
        else if (string.IsNullOrWhiteSpace(torrent!.Query.TrimStart('?'))
            || string.IsNullOrWhiteSpace(ParseQueryValue(torrent.Query, "passkey")))
        {
            errors.Add("u2_torrent_passkey_missing");
        }
        if (details is not null && torrent is not null
            && !string.Equals(details.IdnHost, torrent.IdnHost, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("u2_url_host_mismatch");
        }
        return errors;
    }

    private static bool TryU2Url(string? value, string expectedPath, int u2Id, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.Equals(parsed.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(ParseQueryValue(parsed.Query, "id"), out var parsedId)
            || parsedId <= 0
            || parsedId != u2Id)
        {
            return false;
        }
        uri = parsed;
        return true;
    }

    private static string? ParseQueryValue(string query, string key)
    {
        foreach (var entry in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('=', 2);
            if (string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.OrdinalIgnoreCase))
                return parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }
        return null;
    }

    private static string U2FailureCode(string value)
    {
        var separator = value.IndexOf(':');
        var candidate = (separator >= 0 ? value[..separator] : value).Trim();
        return candidate.Length is > 0 and <= 100 && candidate.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            ? candidate.ToLowerInvariant()
            : "u2_ingest_rejected";
    }

    private static string NormalizeU2AuditDetailsUrl(string? value, int? u2Id)
    {
        if (u2Id is not > 0
            || !TryU2Url(value, "/details.php", u2Id.Value, out var uri)) return "about:blank";
        var authority = uri!.IsDefaultPort
            ? $"{uri.Scheme}://{uri.IdnHost}"
            : $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}";
        return $"{authority}/details.php?id={u2Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static string? NormalizeU2AuditText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static async Task<Results<
        Ok<MikanEpisodeResolveResponse>,
        BadRequest<ApiErrorResponse>>> ResolveMikanEpisode(
        MikanEpisodeResolveRequest request,
        MikanAiTestImportService resolver,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EpisodeUrl)
            || request.EpisodeUrl.Length > 2048)
        {
            return TypedResults.BadRequest(Error(
                "mikan_episode_url_invalid",
                "Mikan Episode URL is required."));
        }
        if (string.IsNullOrWhiteSpace(request.SourceProfileId)
            || request.SourceProfileId.Length > 128)
        {
            return TypedResults.BadRequest(Error(
                "mikan_episode_source_profile_required",
                "source_profile_id is required."));
        }

        try
        {
            var result = await resolver.ResolveAsync(
                request.EpisodeUrl,
                request.SourceProfileId,
                cancellationToken).ConfigureAwait(false);
            var sourceItemId = result.EpisodeUrl.AbsolutePath
                .TrimEnd('/')
                .Split('/')[^1];
            return TypedResults.Ok(new MikanEpisodeResolveResponse(
                result.Title,
                result.TorrentUrl.AbsoluteUri,
                sourceItemId,
                result.MikanId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                result.EpisodeUrl.AbsoluteUri,
                result.MikanId,
                result.GroupId,
                result.BangumiSubjectId,
                result.PublishedAt));
        }
        catch (MikanAiTestImportException exception)
        {
            return TypedResults.BadRequest(Error(exception.Code, exception.Message));
        }
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
                .ProcessAsync(
                    feed,
                    profile.Id,
                    request.MediaType ?? "tv",
                    cancellationToken)
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

    private static async Task<Results<
        Ok<MikanRssIngestResult>,
        BadRequest<ApiErrorResponse>,
        Conflict<ApiErrorResponse>>> RunSourceRssNow(
        string sourceProfileId,
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

        string id;
        try
        {
            id = RequireCanonicalStableId(sourceProfileId, "source profile id");
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("source_profile_invalid", exception.Message));
        }

        var profile = await profiles.GetEnabledAsync(id, cancellationToken).ConfigureAwait(false);
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

        if (string.IsNullOrWhiteSpace(profile.RssFeedUrl))
        {
            return TypedResults.BadRequest(Error(
                "rss_feed_url_missing",
                "The source profile does not have a saved RSS URL."));
        }

        if (!await profiles.TryStartManualRssRunAsync(
                profile.Id,
                profile.Revision,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.Conflict(Error(
                "rss_run_in_progress",
                "This source already has an RSS run in progress or was modified."));
        }

        try
        {
            var feed = await FetchMikanFeedAsync(
                profile.RssFeedUrl,
                profile.Id,
                plugins,
                cancellationToken).ConfigureAwait(false);
            var result = await processor
                .ProcessAsync(feed, profile.Id, profile.MediaType, cancellationToken)
                .ConfigureAwait(false);
            await profiles.CompleteScheduledRunAsync(
                profile.Id,
                profile.Revision,
                result.BatchId,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await profiles.FailScheduledRunAsync(
                profile.Id,
                profile.Revision,
                "rss_manual_run_cancelled",
                DateTimeOffset.UtcNow,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (RssFeedException exception)
        {
            await profiles.FailScheduledRunAsync(
                profile.Id,
                profile.Revision,
                exception.Code,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.BadRequest(Error(
                exception.Code,
                "RSS processing failed."));
        }
        catch
        {
            const string code = "rss_manual_run_failed";
            await profiles.FailScheduledRunAsync(
                profile.Id,
                profile.Revision,
                code,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.BadRequest(Error(code, "RSS processing failed."));
        }
    }

    private static async Task<Ok<LegacyApiResponse<MikanRssIngestResult?>>> LegacyRss(
        LegacyRssRequest request,
        AnimeGo.Plugin.Abstractions.PluginCatalog plugins,
        MikanRssIngestProcessor processor,
        LegacyDownloaderMigrationState legacyMigration,
        MikanPluginCallLogStore audit,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        var mode = request.IsSelectEp ? "selected" : "all";
        var mediaType = NormalizePluginMediaType(request.MediaType);
        if (legacyMigration.BlockingDiagnostic is { } diagnostic)
        {
            await RecordMikanPluginCallAsync(
                audit, "/api/rss", mode, mediaType, 0, 0, 0,
                "failed", diagnostic.Code, startedAt, timer, [], cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new LegacyApiResponse<MikanRssIngestResult?>(
                300,
                $"{diagnostic.Code}: {diagnostic.Message}",
                null));
        }

        if (!string.Equals(request.Source?.Trim(), "mikan", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(request.Rss?.Url))
        {
            await RecordMikanPluginCallAsync(
                audit, "/api/rss", mode, mediaType, 0, 0, 0,
                "failed", "plugin_request_invalid", startedAt, timer, [], cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new LegacyApiResponse<MikanRssIngestResult?>(
                300, "source and rss.url are required", null));
        }

        RssFeedDocument? feed = null;
        try
        {
            feed = await FetchMikanFeedAsync(
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

            var result = await processor.ProcessAsync(
                feed,
                "mikan",
                request.MediaType ?? "tv",
                cancellationToken).ConfigureAwait(false);
            var auditItems = result.Items.Select((item, index) => new MikanPluginCallLogItem(
                index,
                index < feed.Items.Count ? NormalizePluginAuditTitle(feed.Items[index].Title) : null,
                item.IngestTaskId,
                item.IdentityMikanId ?? result.MikanId,
                item.IdentityGroupId,
                item.Status,
                item.Errors.Count == 0 ? null : "ingest_item_rejected")).ToArray();
            var accepted = auditItems.Count(item => item.TaskId is not null);
            await RecordMikanPluginCallAsync(
                audit, "/api/rss", mode, mediaType, feed.Items.Count, accepted,
                auditItems.Length - accepted, "success", null, startedAt, timer,
                auditItems, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new LegacyApiResponse<MikanRssIngestResult?>(
                200, $"开始处理{feed.Items.Count}个下载项", result));
        }
        catch (RssFeedException exception)
        {
            var failedItems = CreatePluginFeedAuditItems(
                feed, "failed", exception.Code);
            await RecordMikanPluginCallAsync(
                audit, "/api/rss", mode, mediaType,
                feed?.Items.Count ?? request.EpLinks?.Count ?? 0, 0, failedItems.Length,
                "failed", exception.Code, startedAt, timer, failedItems, cancellationToken).ConfigureAwait(false);
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
        MikanAiTestImportService mikanResolver,
        MikanPluginCallLogStore audit,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        var requested = request.Data?.Count ?? 0;
        var mode = requested == 1 ? "single" : "batch";
        var mediaType = NormalizePluginMediaType(request.Data?
            .Select(item => item?.Info?.MediaType)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        var legacyData = new List<IngestItemRequest?>();
        foreach (var item in request.Data ?? [])
        {
            if (item?.Info is null)
            {
                legacyData.Add(item);
                continue;
            }

            var info = item.Info;
            if (string.IsNullOrWhiteSpace(info.Title)
                && string.IsNullOrWhiteSpace(info.Name))
            {
                info = info with { Title = info.MikanUrl ?? info.Url };
            }

            var legacyMikanUrl = info.MikanUrl ?? info.Url;
            if (ShouldResolveLegacyMikanEpisode(request.Source, info, legacyMikanUrl))
            {
                try
                {
                    var sourceProfileId = string.IsNullOrWhiteSpace(request.Source)
                        ? "mikan"
                        : request.Source.Trim().ToLowerInvariant();
                    var resolved = await mikanResolver.ResolveAsync(
                        legacyMikanUrl!,
                        sourceProfileId,
                        cancellationToken).ConfigureAwait(false);
                    var sourceItemId = resolved.EpisodeUrl.AbsolutePath
                        .TrimEnd('/')
                        .Split('/')[^1];
                    info = info with
                    {
                        SourceItemId = sourceItemId,
                        SourceWorkId = resolved.MikanId.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        MikanUrl = resolved.EpisodeUrl.AbsoluteUri,
                        MikanId = resolved.MikanId,
                        GroupId = resolved.GroupId,
                        BangumiId = resolved.BangumiSubjectId,
                        PublishedAtRaw = resolved.PublishedAt?.ToString(
                            "O",
                            System.Globalization.CultureInfo.InvariantCulture),
                        PublishedAt = resolved.PublishedAt,
                    };
                }
                catch (MikanAiTestImportException exception)
                {
                    var failedItems = CreatePluginRequestAuditItems(
                        request.Data, "failed", exception.Code);
                    await RecordMikanPluginCallAsync(
                        audit, "/api/download/manager", mode, mediaType, requested, 0, requested,
                        "failed", exception.Code, startedAt, timer, failedItems, cancellationToken).ConfigureAwait(false);
                    return TypedResults.Ok(new LegacyApiResponse<IngestBatchResponse?>(
                        300,
                        $"{exception.Code}: {exception.Message}",
                        null));
                }
            }

            legacyData.Add(item with { Info = info });
        }
        var response = await ProcessIngestAsync(
            request with { Data = legacyData.ToArray() },
            processor,
            requireModernMetadata: false,
            cancellationToken).ConfigureAwait(false);
        var success = response.RejectedCount == 0;
        var auditItems = response.Items.Select((item, index) =>
        {
            var info = index < legacyData.Count ? legacyData[index]?.Info : null;
            return new MikanPluginCallLogItem(
                index,
                NormalizePluginAuditTitle(info?.Title ?? info?.Name),
                item.IngestId,
                info?.MikanId,
                info?.GroupId,
                item.Status,
                item.Errors.Count == 0 ? null : "ingest_item_rejected");
        }).ToArray();
        await RecordMikanPluginCallAsync(
            audit, "/api/download/manager", mode, mediaType, requested,
            response.AcceptedCount, response.RejectedCount,
            success ? "success" : response.AcceptedCount > 0 ? "partial" : "failed",
            success ? null : "ingest_items_rejected", startedAt, timer,
            auditItems, cancellationToken).ConfigureAwait(false);
        var message = success
            ? $"开始处理{response.AcceptedCount}个下载项"
            : string.Join("; ", response.Items.SelectMany(item => item.Errors));
        return TypedResults.Ok(new LegacyApiResponse<IngestBatchResponse?>(
            success ? 200 : 300,
            message,
            response));
    }

    private static string NormalizePluginMediaType(string? value) =>
        string.Equals(value?.Trim(), "movie", StringComparison.OrdinalIgnoreCase) ? "movie" : "tv";

    private static string? NormalizePluginAuditTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var title = value.Trim();
        return title.Length <= 1000 ? title : title[..1000];
    }

    private static MikanPluginCallLogItem[] CreatePluginFeedAuditItems(
        RssFeedDocument? feed,
        string status,
        string failureCode) =>
        feed?.Items.Select((item, index) => new MikanPluginCallLogItem(
            index,
            NormalizePluginAuditTitle(item.Title),
            null,
            feed.MikanId,
            null,
            status,
            failureCode)).ToArray() ?? [];

    private static MikanPluginCallLogItem[] CreatePluginRequestAuditItems(
        IReadOnlyList<IngestItemRequest?>? items,
        string status,
        string failureCode) =>
        items?.Select((item, index) => new MikanPluginCallLogItem(
            index,
            NormalizePluginAuditTitle(item?.Info?.Title ?? item?.Info?.Name),
            null,
            item?.Info?.MikanId,
            item?.Info?.GroupId,
            status,
            failureCode)).ToArray() ?? [];

    private static async Task RecordMikanPluginCallAsync(
        MikanPluginCallLogStore audit,
        string endpoint,
        string mode,
        string mediaType,
        int requestedCount,
        int acceptedCount,
        int rejectedCount,
        string result,
        string? failureCode,
        DateTimeOffset startedAt,
        Stopwatch timer,
        IReadOnlyList<MikanPluginCallLogItem> items,
        CancellationToken cancellationToken)
    {
        timer.Stop();
        await audit.RecordAsync(new MikanPluginCallLog(
            Guid.NewGuid().ToString("N"), endpoint, mode, mediaType, result,
            requestedCount, acceptedCount, rejectedCount, failureCode,
            timer.ElapsedMilliseconds, startedAt, DateTimeOffset.UtcNow, items),
            cancellationToken).ConfigureAwait(false);
    }

    private static bool ShouldResolveLegacyMikanEpisode(
        string? source,
        IngestItemInfoRequest info,
        string? mikanUrl)
    {
        if (!string.Equals(source?.Trim(), "mikan", StringComparison.OrdinalIgnoreCase)
            || info.MikanId is > 0
            || !string.IsNullOrWhiteSpace(info.SourceWorkId)
            || !Uri.TryCreate(mikanUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.AbsolutePath.StartsWith("/Home/Episode/", StringComparison.OrdinalIgnoreCase);
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
                request.Info.ImdbId,
                request.Info.GroupId,
                request.Info.MediaType),
            request.Info.PublishedAtRaw is null && request.Info.PublishedAt is null
                ? null
                : new IngestSourceEvidence(
                    request.Info.PublishedAtRaw,
                    request.Info.PublishedAt));

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
            profile.MikanIdentityCookie,
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
            profile.RssFeedUrl,
            profile.RssScheduleEnabled,
            profile.RssScheduleCron,
            schedule is not null,
            schedule?.NextTime,
            profile.RssLastRunState,
            profile.RssLastStartedAtUtc,
            profile.RssLastCompletedAtUtc,
            profile.RssLastFailureCode,
            profile.RssLastBatchId,
            profile.MediaType,
            profile.PreferAniDbTmdbMapping,
            profile.AniDbTmdbMappingUrlTemplate);
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
            downloader.Username,
            downloader.Password,
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
        AnimeGo.Plugin.Abstractions.PluginCatalog plugins,
        string? mediaType,
        bool? preferAniDbTmdbMapping,
        string? aniDbTmdbMappingUrlTemplate)
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
        if (!MediaTypes.TryNormalize(mediaType ?? current?.MediaType, out var normalizedMediaType))
        {
            throw new ArgumentException("media_type must be tv or movie.");
        }
        if (normalizedMediaType == MediaTypes.Movie && normalizedAdapter != "mikan")
        {
            throw new ArgumentException(
                "media_type movie can only be configured for a Mikan adapter.");
        }
        var preferMapping = normalizedAdapter == "u2"
            && (preferAniDbTmdbMapping ?? current?.PreferAniDbTmdbMapping ?? true);
        var normalizedAniDbMappingUrlTemplate = NormalizeAniDbTmdbMappingUrlTemplate(
            aniDbTmdbMappingUrlTemplate ?? current?.AniDbTmdbMappingUrlTemplate);
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
                ?? true,
            normalizedMediaType,
            preferMapping,
            normalizedAniDbMappingUrlTemplate);
    }

    private static string NormalizeAniDbTmdbMappingUrlTemplate(string? value)
    {
        var template = string.IsNullOrWhiteSpace(value)
            ? AiMatchingOptions.FixedAniDbMappingUrlTemplate
            : value.Trim();
        if (template.Length > 2048
            || !template.Contains("{anidbid}", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(
                template.Replace("{anidbid}", "1", StringComparison.OrdinalIgnoreCase),
                UriKind.Absolute,
                out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "anidb_tmdb_mapping_url_template must be an absolute HTTP(S) URL containing {anidbid}.");
        }
        return template;
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
            item.EpisodeNumbers,
            item.EpisodeFileCount,
            item.MovieFileCount,
            item.OtherFileCount,
            item.DuplicateFileCount,
            item.PendingFileCount,
            item.UpdatedAtUtc,
            item.ReadaptationReviewState,
            item.ReviewKind);

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
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.DownloaderConnected,
            record.DownloaderFailureCode,
            record.DownloaderLastSuccessAtUtc,
            record.TmdbMetadata.Select(metadata => new DownloadTmdbMetadata(
                metadata.SeriesId,
                metadata.SeriesName,
                metadata.SeasonNumber,
                metadata.SeasonName,
                metadata.EpisodeNumbers)).ToArray(),
            record.TmdbMovieMetadata is null
                ? null
                : new DownloadTmdbMovieMetadata(
                    record.TmdbMovieMetadata.MovieId,
                    record.TmdbMovieMetadata.Title,
                    record.TmdbMovieMetadata.OriginalTitle,
                    record.TmdbMovieMetadata.ReleaseDate));

    private static bool CanRetry(DownloadJobDetailRecord detail) =>
        detail.Summary.State == "error"
        || (detail.Summary.State == "skipped_duplicate"
            && detail.Summary.BusinessStatus == "download_skipped_duplicate")
        || (detail.PreparationState == "pending" && detail.PreparationFailureCode is not null)
        || (detail.OrganizationState is "pending" or "cleanup"
            && detail.OrganizationFailureCode is not null);

    private static bool ControlStateAllowed(string kind, string state) =>
        kind switch
        {
            "pause" => state is "waiting" or "downloading" or "moving" or "seeding",
            "resume" => state is "paused" or "dead",
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
