using System.Text.Json.Serialization;
using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.Core.Metadata;

public sealed record AiMetadataMatchInput(
    string Title,
    IReadOnlyList<AiMetadataFileInput> Files,
    int? BangumiSubjectId,
    int? AniDbAnimeId,
    string? ImdbTitleId,
    int TorrentFileCount,
    DateTimeOffset? PublishedAt,
    int? BangumiEpisodeCandidate,
    bool UseBangumiPubDateFirst)
{
    public string? PromptTemplateOverride { get; init; }

    public AiMetadataPromptFeatures? PromptFeaturesOverride { get; init; }

    public AiMetadataDebugIdentity? DebugIdentity { get; init; }

    public AiMetadataDebugPreAiContext? DebugPreAiContext { get; init; }
}

public sealed record AiMetadataDebugIdentity(string RunId, string TaskId);

public sealed record AiMetadataDebugTaskInput(
    string Title,
    int? MikanId,
    int? GroupId,
    int? BangumiSubjectId,
    int? AniDbAnimeId,
    string? ImdbTitleId,
    string? SourceAdapter,
    string? SourceProfileId,
    string? SourceId,
    int TorrentFileCount,
    IReadOnlyList<AiMetadataDebugTaskFileInput> Files);

public sealed record AiMetadataDebugTaskFileInput(
    string Name,
    long SizeBytes,
    string? SourceEpisode,
    string? FileEpisodeCandidate,
    int? PreResolvedEpisodeNumber,
    string? PreResolvedOtherReason,
    int? TmdbSeasonNumber);

public sealed record AiMetadataDebugPreAiAttempt(
    string AttemptId,
    string Stage,
    string Strategy,
    int? Priority,
    string Result,
    string? ErrorCode,
    string? Reason,
    bool Retryable,
    long DurationMilliseconds,
    DateTimeOffset CreatedAtUtc);

public sealed record AiMetadataDebugPreAiContext(
    string TriggerStage,
    AiMetadataDebugTaskInput Input,
    int? ExpectedTmdbSeriesId,
    int? ExpectedSeasonNumber,
    IReadOnlyList<string> AttemptedTmdbSearchTitles,
    DateTimeOffset? TorrentPublishedAt,
    int? BangumiEpisodeCandidate,
    bool UseBangumiPubDateFirst,
    string PublicationResult,
    string? PublicationErrorCode,
    IReadOnlyList<AiMetadataDebugPreAiAttempt> Attempts);

public sealed record AiMetadataDebugExchange(
    int Sequence,
    string Channel,
    string Operation,
    string Endpoint,
    string? RequestBody,
    int? StatusCode,
    string? ResponseBody,
    long DurationMilliseconds,
    string? Error);

public sealed record AiMetadataDebugChain(
    string TraceId,
    string? RunId,
    string? TaskId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string PromptVersion,
    string ApiMode,
    string Model,
    AiMetadataDebugPreAiContext? PreAiContext,
    string PromptTemplate,
    string RenderedPrompt,
    IReadOnlyList<AiMetadataDebugExchange> Exchanges,
    string? RawOutput,
    AiMetadataMatchCandidate? Candidate,
    AiMetadataProviderUsage? Usage,
    string? FailureCode);

public sealed record AiMetadataPromptFeatures(
    bool TmdbMcp,
    bool BangumiMcp,
    bool AniDbLookup,
    bool BangumiPubDateFirst)
{
    public bool ImdbLookup { get; init; }

    public bool U2TvSource { get; init; }

    public bool TvSource { get; init; } = true;

    public bool MovieSource { get; init; }

    public static AiMetadataPromptFeatures Resolve(AiMetadataMatchInput input)
    {
        var requested = input.PromptFeaturesOverride
            ?? new AiMetadataPromptFeatures(
                true,
                input.BangumiSubjectId is not null,
                input.AniDbAnimeId is not null,
                input.UseBangumiPubDateFirst)
            {
                ImdbLookup = input.ImdbTitleId is not null,
            };

        var tmdb = requested.TmdbMcp;
        var bangumi = requested.BangumiMcp && input.BangumiSubjectId is not null;
        return requested with
        {
            TmdbMcp = tmdb,
            BangumiMcp = bangumi,
            AniDbLookup = requested.AniDbLookup && input.AniDbAnimeId is not null,
            BangumiPubDateFirst = requested.BangumiPubDateFirst
                && input.UseBangumiPubDateFirst,
            ImdbLookup = requested.ImdbLookup
                && input.ImdbTitleId is not null
                && tmdb,
            U2TvSource = requested.U2TvSource,
            TvSource = requested.TvSource && !requested.MovieSource,
            MovieSource = requested.MovieSource,
        };
    }
}

public sealed record AiMetadataFileInput(
    string Name,
    long SizeBytes);

public sealed record AiMetadataMatchCandidate(
    bool? Matched,
    int? TmdbId,
    IReadOnlyList<AiMetadataFileCandidate>? Files,
    string? Reason);

public sealed record AiMetadataProviderUsage(
    string Model,
    long? PromptTokens,
    long? CompletionTokens,
    long? TotalTokens,
    int RequestCount,
    int ToolCallCount,
    long? ReasoningTokens = null);

public sealed record AiMetadataMatchResponse(
    AiMetadataMatchCandidate Candidate,
    AiMetadataProviderUsage? Usage)
{
    public string? RawOutput { get; init; }

    public IReadOnlyList<AiMetadataTraceEvent> Trace { get; init; } = [];

    public AiMetadataDebugChain? DebugChain { get; init; }

    public bool? Matched => Candidate.Matched;

    public int? TmdbId => Candidate.TmdbId;

    public IReadOnlyList<AiMetadataFileCandidate>? Files => Candidate.Files;

    public string? Reason => Candidate.Reason;

    public static implicit operator AiMetadataMatchCandidate(AiMetadataMatchResponse response) =>
        response.Candidate;
}

public sealed record AiMetadataTraceEvent(
    int Sequence,
    string Stage,
    string Detail,
    long? DurationMilliseconds = null);

public sealed record AiMetadataFileCandidate(
    string? Name,
    bool? Matched,
    int? Season,
    [property: JsonConverter(typeof(AiMetadataEpisodeJsonConverter))] int? Episode,
    string? Reason)
{
    public const int ExtrasEpisodeSentinel = 0;
    public const string ExtrasEpisodeValue = "Extras";

    public bool IsExtras => Episode == ExtrasEpisodeSentinel;
}

public sealed record ValidatedAiMetadataMatch(
    TmdbSeries Series,
    IReadOnlyList<ValidatedAiMetadataFile> Files);

public sealed record ValidatedAiMetadataFile(
    AiMetadataFileInput Input,
    TmdbSeason Season,
    TmdbEpisode? Episode,
    string? OtherReason,
    bool IsExtra = false)
{
    public bool IsEpisode => Episode is not null;
}

public sealed record AiMetadataValidationResult(
    ValidatedAiMetadataMatch? Value,
    MetadataFailure? Failure)
{
    public bool IsSuccess => Value is not null && Failure is null;
}

public interface IAiMetadataMatcher
{
    Task<AiMetadataMatchResponse> MatchAsync(
        AiMetadataMatchInput input,
        CancellationToken cancellationToken = default);
}

public sealed class AiMetadataMatcherException(
    MetadataFailureKind kind,
    string safeCode,
    Exception? innerException = null,
    AiMetadataProviderUsage? usage = null,
    AiMetadataDebugChain? debugChain = null)
    : Exception(safeCode, innerException)
{
    public MetadataFailureKind Kind { get; } = kind;

    public string SafeCode { get; } = StableErrorCode.Require(safeCode, nameof(safeCode));

    public AiMetadataProviderUsage? Usage { get; } = usage;

    public AiMetadataDebugChain? DebugChain { get; } = debugChain;
}
