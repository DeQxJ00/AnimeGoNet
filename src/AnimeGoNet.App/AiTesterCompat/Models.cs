using System.Text.Json.Serialization;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.AiTesterCompat;

[JsonSerializable(typeof(ResponsesRequest))]
[JsonSerializable(typeof(ChatCompletionsRequest))]
[JsonSerializable(typeof(ErrorEnvelope))]
[JsonSerializable(typeof(TmdbAiMatchResult))]
[JsonSerializable(typeof(UiRunRequest))]
[JsonSerializable(typeof(UiRunResponse))]
[JsonSerializable(typeof(TorrentImportRequest))]
[JsonSerializable(typeof(TorrentImportResponse))]
[JsonSerializable(typeof(MikanEpisodeImportRequest))]
[JsonSerializable(typeof(MikanEpisodeImportResponse))]
[JsonSerializable(typeof(LocalEpisodeOffsetResult))]
[JsonSerializable(typeof(FileEpisodeCandidateEntry))]
[JsonSerializable(typeof(ToolTimelineEntry))]
[JsonSerializable(typeof(AiApiRequestEntry))]
[JsonSerializable(typeof(UiStreamEnvelope))]
[JsonSerializable(typeof(UiStopRequest))]
[JsonSerializable(typeof(UiStopResponse))]
[JsonSerializable(typeof(TesterBootstrapResponse))]
[JsonSerializable(typeof(TesterConfig))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
public sealed partial class AiTesterJsonContext : JsonSerializerContext;

public enum ApiMode
{
    Responses,
    ChatCompletions
}

public sealed record TesterConfig(
    string BaseUrl,
    string ApiKey,
    string Model,
    ApiMode Mode,
    string? ReasoningEffort,
    bool WebSearchEnabled,
    int TimeoutSeconds,
    string? ProxyUrl,
    string BgmMcpUrl = "http://bgm.mcp.local/mcp",
    string TmdbMcpUrl = "http://tmdb.mcp.local/mcp",
    bool EnableBgmMcp = true,
    bool EnableTmdbMcp = true,
    bool EnableAniDbLookup = true,
    string AniDbMappingUrlTemplate = "https://raw.githubusercontent.com/DeQxJ00/Anime-Lists-Json/refs/heads/main/api/anidb/{anidbid}.json",
    bool IsMikanRssSource = false);

public sealed record CliOptions(
    MatchRequestInput Input,
    bool Integration,
    bool Ui,
    int UiPort);

public sealed record MatchRequestInput(
    string Title,
    IReadOnlyList<MatchFileInput> Files,
    long? Bgmid = null,
    long? Anidbid = null,
    string? MikanPubDate = null,
    int? TorrentFileCount = null,
    bool EnableBangumiPubDateFirst = true,
    int? BgmEpisodeCandidate = null,
    bool IsMikanRssSource = false);

public sealed record PromptFeatures(
    bool TmdbMcp,
    bool BgmMcp,
    bool AniDbLookup,
    bool BangumiPubDateFirst)
{
    public static PromptFeatures From(TesterConfig config, MatchRequestInput input)
    {
        PubDatePriorityGate gate = PubDatePriority.Evaluate(config, input);
        return new(
            config.EnableTmdbMcp,
            config.EnableBgmMcp && input.Bgmid is not null,
            config.EnableAniDbLookup && input.Anidbid is not null,
            gate.UseBangumiPubDateFirst);
    }
}

public sealed record PubDatePriorityGate(
    bool UseBangumiPubDateFirst,
    int? TorrentFileCount,
    int? BgmEpisodeCandidate,
    string? NormalizedPubDate,
    string Reason);

public sealed record MatchFileInput(
    string Name,
    long SizeBytes,
    int? FileEpisodeCandidate = null);

public sealed record FileEpisodeCandidateEntry(
    string Name,
    int? FileEpisodeCandidate);

public sealed record ResponsesRequest(
    string Model,
    string Input,
    ReasoningOptions? Reasoning,
    IReadOnlyList<ResponseTool>? Tools);

public sealed record ReasoningOptions(string Effort);

public sealed record ResponseTool(string Type);

public sealed record ChatCompletionsRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    ReasoningOptions? Reasoning,
    ResponseFormat? ResponseFormat);

public sealed record ChatMessage(string Role, string Content);

public sealed record ResponseFormat(string Type);

public sealed record ErrorEnvelope(ErrorPayload? Error);

public sealed record ErrorPayload(string? Message, string? Type, string? Code);

public sealed record ApiUsage(
    int? InputTokens,
    int? OutputTokens,
    int? ReasoningTokens,
    int? TotalTokens);

public sealed record ApiCallResult(
    bool Success,
    int StatusCode,
    string RawResponse,
    string? ModelJson,
    ApiUsage Usage,
    TimeSpan Elapsed,
    string? ErrorMessage,
    IReadOnlyList<ToolTimelineEntry>? ToolTimeline = null);

public sealed record TmdbAiMatchResult(
    bool? Matched,
    int? TmdbId,
    IReadOnlyList<TmdbAiFileResult>? Files,
    string? Reason);

public sealed record LocalEpisodeOffsetResult(
    bool Applicable,
    bool Calculated,
    int? EpisodeOffset,
    int? TmdbId,
    int? Season,
    int MatchedCandidateCount,
    string Reason);

public sealed record TmdbAiFileResult(
    [property: JsonPropertyName("file_id")] string? FileId,
    bool? Matched,
    int? Season,
    [property: JsonConverter(typeof(AiMetadataEpisodeJsonConverter))] int? Episode,
    string? Reason)
{
    public bool IsExtras => Episode == AiMetadataFileCandidate.ExtrasEpisodeSentinel;
}

public sealed record UiRunRequest(
    string? BaseUrl,
    string? ApiKey,
    string? Model,
    string? Mode,
    string? ReasoningEffort,
    bool? WebSearchEnabled,
    int? TimeoutSeconds,
    string? ProxyUrl,
    string? PromptTemplate,
    string? Title,
    string? FilesJson,
    string? Bgmid,
    string? Anidbid,
    [property: JsonPropertyName("mikan_pub_date")] string? MikanPubDate,
    [property: JsonPropertyName("bgm_episode_candidate")] string? BgmEpisodeCandidate,
    [property: JsonPropertyName("use_bangumi_pubdate_first")] bool? UseBangumiPubDateFirst,
    [property: JsonPropertyName("torrent_import_id")] string? TorrentImportId,
    [property: JsonPropertyName("is_mikan_rss_source")] bool? IsMikanRssSource,
    [property: JsonPropertyName("bgm_mcp_url")] string? BgmMcpUrl,
    [property: JsonPropertyName("tmdb_mcp_url")] string? TmdbMcpUrl,
    [property: JsonPropertyName("enable_bgm_mcp")] bool? EnableBgmMcp,
    [property: JsonPropertyName("enable_tmdb_mcp")] bool? EnableTmdbMcp,
    [property: JsonPropertyName("enable_anidb_lookup")] bool? EnableAniDbLookup,
    [property: JsonPropertyName("anidb_mapping_url_template")] string? AniDbMappingUrlTemplate,
    [property: JsonPropertyName("run_id")] string? RunId);

public sealed record UiRunResponse(
    bool Success,
    int StatusCode,
    string RawResponse,
    string? ModelJson,
    ApiUsage Usage,
    long ElapsedMilliseconds,
    string? ErrorMessage,
    bool ResultJsonValid,
    string? ResultJsonError,
    string? RequestIdentity,
    IReadOnlyList<ToolTimelineEntry>? ToolTimeline = null,
    PubDatePriorityGate? PubDatePriority = null,
    string? RenderedPrompt = null,
    IReadOnlyList<AiApiRequestEntry>? AiApiRequests = null,
    LocalEpisodeOffsetResult? LocalEpisodeOffset = null,
    IReadOnlyList<FileEpisodeCandidateEntry>? FileEpisodeCandidates = null,
    AnimeGoNet.App.Api.AiMetadataTestValidationResponse? ProductionValidation = null);

public sealed record ExecutionProgress(
    string Type,
    int Step,
    string Message,
    ApiUsage? Usage = null,
    ToolTimelineEntry? Tool = null,
    string? Content = null,
    string? Endpoint = null);

public sealed record AiApiRequestEntry(int Step, string Endpoint, string Content);

public sealed record UiStreamEnvelope(
    string Type,
    ExecutionProgress? Progress = null,
    UiRunResponse? Result = null,
    string? Error = null);

public sealed record UiStopRequest([property: JsonPropertyName("run_id")] string? RunId);

public sealed record UiStopResponse(bool Stopped, string Message);

public sealed record TesterBootstrapResponse(TesterConfig Defaults, string PromptTemplate);

public sealed record TorrentImportRequest(string? DataBase64);

public sealed record TorrentImportResponse(
    bool Success,
    IReadOnlyList<MatchFileInput>? Files,
    string? ErrorMessage,
    string? ImportId = null,
    int? TorrentFileCount = null,
    IReadOnlyList<FileEpisodeCandidateEntry>? FileEpisodeCandidates = null);

public sealed record TorrentImportResult(
    IReadOnlyList<MatchFileInput> VideoFiles,
    int TorrentFileCount);

public sealed record MikanEpisodeImportRequest(string? EpisodeUrl, string? ProxyUrl);

public sealed record MikanEpisodeImportResponse(
    bool Success,
    string? Title,
    long? MikanId,
    long? GroupId,
    long? Bgmid,
    string? MikanPubDate,
    string? TorrentUrl,
    IReadOnlyList<MatchFileInput>? Files,
    string? ImportId,
    int? TorrentFileCount,
    IReadOnlyList<FileEpisodeCandidateEntry>? FileEpisodeCandidates,
    string? ErrorMessage);

public sealed record ToolTimelineEntry(
    string Source,
    string Name,
    string Phase,
    long ElapsedMilliseconds,
    bool Success,
    string? Message,
    string? RequestContent = null,
    string? ResponseContent = null);
