using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using AnimeGoNet.App.Api;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.AiTesterCompat;

public sealed class AiTesterCoordinator(
    AnimeGoOptions applicationOptions,
    MikanAiTestImportService mikanImporter,
    AiMetadataResultValidator productionValidator)
{
    private const int MaxTorrentImports = 256;
    private static readonly TimeSpan TorrentImportTtl = TimeSpan.FromHours(4);
    private readonly ConcurrentDictionary<string, TorrentImportSnapshot> _torrentImports = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeRuns = new();
    private readonly McpMetadataCache _mcpCache = new();

    public TesterConfig Defaults => BuildDefaults(applicationOptions);

    public TorrentImportResponse ImportTorrent(TorrentImportRequest? request)
    {
        if (string.IsNullOrWhiteSpace(request?.DataBase64))
        {
            return new(false, null, "Torrent data is required.");
        }

        try
        {
            var bytes = Convert.FromBase64String(request.DataBase64);
            TorrentImportResult imported = TorrentFileImporter.ImportDetailed(bytes);
            var importId = RegisterTorrentImport(imported.TorrentFileCount);
            MatchFileInput[] files = imported.VideoFiles
                .Select(file => new MatchFileInput(file.Name, file.SizeBytes))
                .ToArray();
            FileEpisodeCandidateEntry[] candidates = imported.VideoFiles
                .Select(file => new FileEpisodeCandidateEntry(file.Name, file.FileEpisodeCandidate))
                .ToArray();
            return new(true, files, null, importId, imported.TorrentFileCount, candidates);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or IOException)
        {
            return new(false, null, exception.Message);
        }
    }

    public async Task<MikanEpisodeImportResponse> ImportMikanAsync(
        MikanEpisodeImportRequest? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.EpisodeUrl))
        {
            return FailedMikan("Mikan Episode URL is required.");
        }

        try
        {
            MikanAiTestImportResult imported = await mikanImporter
                .ImportAsync(request.EpisodeUrl, cancellationToken)
                .ConfigureAwait(false);
            var importId = RegisterTorrentImport(imported.TorrentFileCount);
            MatchFileInput[] files = imported.VideoFiles
                .Select(file => new MatchFileInput(file.Name, file.SizeBytes))
                .ToArray();
            FileEpisodeCandidateEntry[] candidates = files
                .Select(file => new FileEpisodeCandidateEntry(
                    file.Name,
                    FileEpisodeCandidateResolver.Resolve(file.Name)))
                .ToArray();
            return new(
                true,
                imported.Title,
                imported.MikanId,
                imported.GroupId,
                imported.BangumiSubjectId,
                imported.PublishedAt?.ToString("O", CultureInfo.InvariantCulture),
                null,
                files,
                importId,
                imported.TorrentFileCount,
                candidates,
                null);
        }
        catch (Exception exception) when (
            exception is MikanAiTestImportException
                or HttpRequestException
                or IOException
                or TaskCanceledException)
        {
            return FailedMikan(exception.Message);
        }
    }

    public string? ValidateRequest(UiRunRequest? request)
    {
        if (request is null) return "Request body is required.";
        if (string.IsNullOrWhiteSpace(request.BaseUrl)) return "Base URL is required.";
        if (string.IsNullOrWhiteSpace(request.ApiKey)) return "API key is required.";
        if (string.IsNullOrWhiteSpace(request.Model)) return "Model is required.";
        if (request.TimeoutSeconds is <= 0) return "timeout_seconds must be a positive integer.";
        if (string.IsNullOrWhiteSpace(request.Title)) return "Title is required.";
        if (!string.IsNullOrWhiteSpace(request.RunId) && !Guid.TryParse(request.RunId, out _))
            return "run_id must be a valid UUID.";
        try
        {
            Configuration.ParseOptionalPositiveLong(request.Bgmid, "bgmid");
            Configuration.ParseOptionalPositiveLong(request.Anidbid, "anidbid");
            Configuration.ParseOptionalPositiveInt(request.BgmEpisodeCandidate, "bgm_episode_candidate");
            if (request.EnableBgmMcp != false && request.BgmMcpUrl is not null)
                Configuration.ValidateHttpUrl(request.BgmMcpUrl, "bgmMcpUrl");
            if (request.EnableTmdbMcp != false && request.TmdbMcpUrl is not null)
                Configuration.ValidateHttpUrl(request.TmdbMcpUrl, "tmdbMcpUrl");
            if (request.EnableAniDbLookup != false && request.AniDbMappingUrlTemplate is not null)
                Configuration.ValidateAniDbTemplate(request.AniDbMappingUrlTemplate);
            if (string.IsNullOrWhiteSpace(request.FilesJson)) return "Files JSON is required.";
            Configuration.ParseFilesJson(request.FilesJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return exception.Message;
        }

        return null;
    }

    public (Guid RunId, CancellationTokenSource Cancellation) RegisterRun(string? value)
    {
        var runId = Guid.TryParse(value, out var parsed) ? parsed : Guid.NewGuid();
        var cancellation = new CancellationTokenSource();
        if (!_activeRuns.TryAdd(runId, cancellation))
        {
            cancellation.Dispose();
            throw new ArgumentException("run_id is already active.");
        }
        return (runId, cancellation);
    }

    public void CompleteRun(Guid runId, CancellationTokenSource cancellation)
    {
        _activeRuns.TryRemove(runId, out _);
        cancellation.Dispose();
    }

    public UiStopResponse Stop(UiStopRequest? request)
    {
        if (!Guid.TryParse(request?.RunId, out var runId))
            return new(false, "A valid run_id is required.");
        if (!_activeRuns.TryGetValue(runId, out var cancellation))
            return new(false, "Run is no longer active.");
        cancellation.Cancel();
        return new(true, "Stop requested.");
    }

    public async Task<UiRunResponse> ExecuteAsync(
        UiRunRequest request,
        Func<ExecutionProgress, CancellationToken, ValueTask>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var aiApiRequests = new List<AiApiRequestEntry>();
        async ValueTask CaptureProgressAsync(ExecutionProgress item, CancellationToken token)
        {
            if (item.Type == "model-start" && item.Content is not null)
            {
                var display = ToolRegistry.RedactContentForDisplay(item.Content);
                aiApiRequests.Add(new(item.Step, item.Endpoint ?? "unavailable", display));
                item = item with { Content = display };
            }
            if (progress is not null) await progress(item, token).ConfigureAwait(false);
        }

        TesterConfig config = BuildConfig(Defaults, request);
        int? torrentFileCount = ResolveTorrentFileCount(request.TorrentImportId);
        MatchRequestInput input = InputNormalizer.Normalize(new MatchRequestInput(
            request.Title!,
            Configuration.ParseFilesJson(request.FilesJson!),
            Configuration.ParseOptionalPositiveLong(request.Bgmid, "bgmid"),
            Configuration.ParseOptionalPositiveLong(request.Anidbid, "anidbid"),
            request.MikanPubDate,
            torrentFileCount,
            request.UseBangumiPubDateFirst ?? true,
            Configuration.ParseOptionalPositiveInt(request.BgmEpisodeCandidate, "bgm_episode_candidate"),
            request.IsMikanRssSource ?? config.IsMikanRssSource));
        PubDatePriorityGate gate = PubDatePriority.Evaluate(config, input);
        await CaptureProgressAsync(new ExecutionProgress(
            "gate",
            0,
            $"Bangumi pubDate 优先门禁: {gate.UseBangumiPubDateFirst}; {gate.Reason}; " +
            $"torrent_file_count={gate.TorrentFileCount?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}; " +
            $"bgm_episode_candidate={gate.BgmEpisodeCandidate?.ToString(CultureInfo.InvariantCulture) ?? "null"}"), cancellationToken).ConfigureAwait(false);
        if (gate.UseBangumiPubDateFirst)
        {
            await CaptureProgressAsync(new ExecutionProgress(
                "branch", 0, "已进入 BGM pubDate 日期优先分支；失败时继续通用匹配"), cancellationToken).ConfigureAwait(false);
        }

        string template = string.IsNullOrWhiteSpace(request.PromptTemplate)
            ? PromptTemplate.LoadFromMarkdown(PromptTemplate.FindDefaultMarkdownPath())
            : request.PromptTemplate;
        RenderedPrompt prompt = PromptTemplate.Render(template, input, PromptFeatures.From(config, input));
        await CaptureProgressAsync(new ExecutionProgress(
            "prompt", 0, "最终 Prompt 已生成", Content: prompt.Text), cancellationToken).ConfigureAwait(false);

        using HttpClient httpClient = HttpClientFactory.Create(config);
        var registry = new ToolRegistry(config, input, httpClient, _mcpCache);
        var client = new OpenAiCompatibleClient(httpClient, config);
        ApiCallResult result = await client
            .SendAsync(prompt.Text, registry, CaptureProgressAsync, cancellationToken)
            .ConfigureAwait(false);
        (bool valid, string? validationError, TmdbAiMatchResult? parsed) =
            ResultValidator.Validate(result.ModelJson, input);
        LocalEpisodeOffsetResult? localOffset = valid && parsed is not null
            ? EpisodeOffsetCalculator.Calculate(input, parsed)
            : null;
        FileEpisodeCandidateEntry[] candidates = input.Files
            .Select(file => new FileEpisodeCandidateEntry(file.Name, file.FileEpisodeCandidate))
            .ToArray();
        if (localOffset is not null)
        {
            await CaptureProgressAsync(new ExecutionProgress(
                "local-offset",
                0,
                $"本地 EP 偏移: calculated={localOffset.Calculated}; offset={localOffset.EpisodeOffset?.ToString(CultureInfo.InvariantCulture) ?? "null"}; {localOffset.Reason}"),
                cancellationToken).ConfigureAwait(false);
        }

        AiMetadataTestValidationResponse? productionValidation = null;
        if (valid && parsed is not null)
        {
            productionValidation = await ValidateProductionAsync(input, parsed, cancellationToken)
                .ConfigureAwait(false);
            await CaptureProgressAsync(new ExecutionProgress(
                "production-validation",
                0,
                productionValidation.Success
                    ? "主程序 TMDB 二次验证通过"
                    : $"主程序 TMDB 二次验证失败: {productionValidation.FailureCode ?? "unknown"}"),
                cancellationToken).ConfigureAwait(false);
        }

        return new UiRunResponse(
            result.Success,
            result.StatusCode,
            result.RawResponse,
            result.ModelJson,
            result.Usage,
            (long)Math.Round(result.Elapsed.TotalMilliseconds),
            result.ErrorMessage,
            valid,
            validationError,
            prompt.RequestIdentity,
            result.ToolTimeline,
            gate,
            prompt.Text,
            aiApiRequests,
            localOffset,
            candidates,
            productionValidation);
    }

    private async Task<AiMetadataTestValidationResponse> ValidateProductionAsync(
        MatchRequestInput testerInput,
        TmdbAiMatchResult parsed,
        CancellationToken cancellationToken)
    {
        var input = new AiMetadataMatchInput(
            testerInput.Title,
            testerInput.Files.Select(file => new AiMetadataFileInput(file.Name, file.SizeBytes)).ToArray(),
            testerInput.Bgmid is > 0 and <= int.MaxValue ? (int)testerInput.Bgmid.Value : null,
            testerInput.Anidbid is > 0 and <= int.MaxValue ? (int)testerInput.Anidbid.Value : null,
            null,
            testerInput.TorrentFileCount ?? testerInput.Files.Count,
            ParseDate(testerInput.MikanPubDate),
            testerInput.BgmEpisodeCandidate,
            testerInput.EnableBangumiPubDateFirst);
        var candidate = new AiMetadataMatchCandidate(
            parsed.Matched,
            parsed.TmdbId,
            parsed.Files?.Select(file => new AiMetadataFileCandidate(
                file.Name,
                file.Matched,
                file.Season,
                file.Episode,
                file.Reason)).ToArray(),
            parsed.Reason);
        AiMetadataValidationResult validation = await productionValidator
            .ValidateAsync(input, candidate, null, null, cancellationToken)
            .ConfigureAwait(false);
        return ApiEndpoints.ToAiTestValidationResponse(validation);
    }

    private string RegisterTorrentImport(int count)
    {
        var id = Guid.NewGuid().ToString("N");
        _torrentImports[id] = new(count, DateTimeOffset.UtcNow);
        if (_torrentImports.Count > MaxTorrentImports)
        {
            foreach (var key in _torrentImports
                .OrderBy(item => item.Value.CreatedAt)
                .Take(_torrentImports.Count - MaxTorrentImports)
                .Select(item => item.Key))
            {
                _torrentImports.TryRemove(key, out _);
            }
        }
        return id;
    }

    private int? ResolveTorrentFileCount(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !_torrentImports.TryGetValue(id, out var snapshot)) return null;
        if (DateTimeOffset.UtcNow - snapshot.CreatedAt <= TorrentImportTtl) return snapshot.TorrentFileCount;
        _torrentImports.TryRemove(id, out _);
        return null;
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static TesterConfig BuildDefaults(AnimeGoOptions options)
    {
        AiMatchingOptions ai = options.Metadata.Ai;
        return new(
            ai.BaseUrl?.AbsoluteUri ?? string.Empty,
            ai.ApiKey ?? string.Empty,
            ai.Model ?? string.Empty,
            ai.ApiMode == AiApiMode.Responses ? ApiMode.Responses : ApiMode.ChatCompletions,
            ai.ReasoningEffort,
            ai.WebSearchEnabled,
            Math.Max(1, (int)Math.Round(ai.HttpTimeout.TotalSeconds)),
            options.OutboundProxy.Url?.AbsoluteUri,
            ai.BangumiMcpUrl.AbsoluteUri,
            ai.TmdbMcpUrl.AbsoluteUri,
            true,
            true,
            true,
            ai.AniDbMappingUrlTemplate,
            false);
    }

    private static TesterConfig BuildConfig(TesterConfig defaults, UiRunRequest request)
    {
        string reasoning = string.IsNullOrWhiteSpace(request.ReasoningEffort)
            ? defaults.ReasoningEffort ?? "medium"
            : request.ReasoningEffort.Trim();
        string? normalizedReasoning = string.Equals(reasoning, "none", StringComparison.OrdinalIgnoreCase)
            ? null
            : reasoning.ToLowerInvariant();
        bool enableBgm = request.EnableBgmMcp ?? defaults.EnableBgmMcp;
        bool enableTmdb = request.EnableTmdbMcp ?? defaults.EnableTmdbMcp;
        bool enableAniDb = request.EnableAniDbLookup ?? defaults.EnableAniDbLookup;
        return new(
            First(request.BaseUrl, defaults.BaseUrl)!,
            First(request.ApiKey, defaults.ApiKey)!,
            First(request.Model, defaults.Model)!,
            ParseMode(First(request.Mode, defaults.Mode == ApiMode.Responses ? "responses" : "chat-completions")!),
            normalizedReasoning,
            request.WebSearchEnabled ?? defaults.WebSearchEnabled,
            request.TimeoutSeconds ?? defaults.TimeoutSeconds,
            NormalizeOptional(request.ProxyUrl),
            enableBgm ? Configuration.ValidateHttpUrl(First(request.BgmMcpUrl, defaults.BgmMcpUrl), "bgmMcpUrl") : First(request.BgmMcpUrl, defaults.BgmMcpUrl)!,
            enableTmdb ? Configuration.ValidateHttpUrl(First(request.TmdbMcpUrl, defaults.TmdbMcpUrl), "tmdbMcpUrl") : First(request.TmdbMcpUrl, defaults.TmdbMcpUrl)!,
            enableBgm,
            enableTmdb,
            enableAniDb,
            enableAniDb ? Configuration.ValidateAniDbTemplate(First(request.AniDbMappingUrlTemplate, defaults.AniDbMappingUrlTemplate)) : First(request.AniDbMappingUrlTemplate, defaults.AniDbMappingUrlTemplate)!,
            request.IsMikanRssSource ?? defaults.IsMikanRssSource);
    }

    private static ApiMode ParseMode(string value) => value.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
    {
        "responses" or "response" => ApiMode.Responses,
        "chatcompletions" or "chatcompletion" or "chat" => ApiMode.ChatCompletions,
        _ => throw new ArgumentException("mode must be responses or chat-completions."),
    };

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MikanEpisodeImportResponse FailedMikan(string error) =>
        new(false, null, null, null, null, null, null, null, null, null, null, error);

    private sealed record TorrentImportSnapshot(int TorrentFileCount, DateTimeOffset CreatedAt);
}
