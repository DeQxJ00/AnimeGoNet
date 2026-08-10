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
}

public sealed record AiMetadataPromptFeatures(
    bool TmdbMcp,
    bool BangumiMcp,
    bool AniDbLookup,
    bool BangumiPubDateFirst)
{
    public bool ImdbLookup { get; init; }

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
    int? Episode,
    string? Reason);

public sealed record ValidatedAiMetadataMatch(
    TmdbSeries Series,
    IReadOnlyList<ValidatedAiMetadataFile> Files);

public sealed record ValidatedAiMetadataFile(
    AiMetadataFileInput Input,
    TmdbSeason Season,
    TmdbEpisode? Episode,
    string? OtherReason)
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
    AiMetadataProviderUsage? usage = null)
    : Exception(safeCode, innerException)
{
    public MetadataFailureKind Kind { get; } = kind;

    public string SafeCode { get; } = StableErrorCode.Require(safeCode, nameof(safeCode));

    public AiMetadataProviderUsage? Usage { get; } = usage;
}
