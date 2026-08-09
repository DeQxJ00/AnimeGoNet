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
    bool UseBangumiPubDateFirst);

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
    int ToolCallCount);

public sealed record AiMetadataMatchResponse(
    AiMetadataMatchCandidate Candidate,
    AiMetadataProviderUsage? Usage)
{
    public bool? Matched => Candidate.Matched;

    public int? TmdbId => Candidate.TmdbId;

    public IReadOnlyList<AiMetadataFileCandidate>? Files => Candidate.Files;

    public string? Reason => Candidate.Reason;

    public static implicit operator AiMetadataMatchCandidate(AiMetadataMatchResponse response) =>
        response.Candidate;
}

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
