using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed record AiMetadataTaskResolution(
    ValidatedAiMetadataMatch? Value,
    MetadataFailure? Failure,
    AiPublicationEvidenceResult? Publication,
    AiMetadataProviderUsage? Usage,
    bool IsApplicable,
    ValidatedAiMetadataMatch? SeriesChangeProposal = null)
{
    public bool IsSuccess => IsApplicable && Value is not null && Failure is null;
}

public sealed class AiMetadataTaskResolver(
    IAiMetadataMatcher matcher,
    AiMetadataResultValidator validator,
    AiPublicationEvidenceResolver publicationEvidence,
    AiMetadataDebugTraceStore? debugStore = null,
    ILogger<AiMetadataTaskResolver>? logger = null,
    MetadataResolutionStore? resolutions = null,
    AnimeGoOptions? options = null)
{
    private static readonly Action<ILogger, string, Exception?> DebugWriteFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(7201, "AiDebugWriteFailed"),
            "AI debug chain {TraceId} could not be persisted.");

    public async Task<AiMetadataTaskResolution> ResolveAsync(
        MetadataTaskClaim claim,
        IReadOnlyList<MetadataTaskFileProjection> files,
        int? expectedSeriesId = null,
        int? expectedSeasonNumber = null,
        IReadOnlyList<string>? preAiSearchTitles = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(files);
        var videos = files
            .Where(file => SubtitleAssociationResolver.IsVideo(file.RelativePath))
            .ToArray();
        if (videos.Length == 0)
        {
            return new AiMetadataTaskResolution(
                null,
                new MetadataFailure(
                    MetadataFailureKind.InvalidInput,
                    "ai_video_files_missing",
                    TmdbAccessConfirmed: false),
                null,
                null,
                IsApplicable: false);
        }

        var publication = await publicationEvidence.ResolveAsync(
            claim,
            cancellationToken).ConfigureAwait(false);
        var debugContext = options?.Metadata.Ai.DebugMode == true
            ? await CreateDebugContextAsync(
                claim,
                videos,
                publication,
                expectedSeriesId,
                expectedSeasonNumber,
                preAiSearchTitles,
                resolutions,
                cancellationToken).ConfigureAwait(false)
            : null;
        var input = AiMetadataInputBoundary.Create(claim, videos, publication, debugContext);

        AiMetadataMatchResponse response;
        try
        {
            response = await matcher.MatchAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (AiMetadataMatcherException exception)
        {
            await SaveDebugAsync(
                exception.DebugChain,
                null,
                expectedSeriesId,
                expectedSeasonNumber,
                cancellationToken).ConfigureAwait(false);
            return new AiMetadataTaskResolution(
                null,
                new MetadataFailure(exception.Kind, exception.SafeCode, TmdbAccessConfirmed: false),
                publication,
                exception.Usage,
                IsApplicable: true);
        }

        var validated = await validator.ValidateAsync(
            input,
            response.Candidate,
            expectedSeriesId,
            expectedSeasonNumber,
            cancellationToken).ConfigureAwait(false);
        ValidatedAiMetadataMatch? seriesChangeProposal = null;
        if (string.Equals(
                validated.Failure?.Code,
                "ai_tmdb_series_candidate_conflict",
                StringComparison.Ordinal))
        {
            var proposalValidation = await validator.ValidateAsync(
                input,
                response.Candidate,
                expectedSeriesId: null,
                expectedSeasonNumber,
                cancellationToken).ConfigureAwait(false);
            if (proposalValidation.IsSuccess)
            {
                seriesChangeProposal = proposalValidation.Value;
                validated = new AiMetadataValidationResult(
                    null,
                    new MetadataFailure(
                        MetadataFailureKind.Ambiguous,
                        "ai_tmdb_multilingual_series_conflict_review_required",
                        TmdbAccessConfirmed: true));
            }
        }
        await SaveDebugAsync(
            response.DebugChain,
            validated,
            expectedSeriesId,
            expectedSeasonNumber,
            cancellationToken).ConfigureAwait(false);
        return new AiMetadataTaskResolution(
            validated.Value,
            validated.Failure,
            publication,
            response.Usage,
            IsApplicable: true,
            seriesChangeProposal);
    }

    private static async Task<AiMetadataDebugPreAiContext> CreateDebugContextAsync(
        MetadataTaskClaim claim,
        IReadOnlyList<MetadataTaskFileProjection> videos,
        AiPublicationEvidenceResult publication,
        int? expectedSeriesId,
        int? expectedSeasonNumber,
        IReadOnlyList<string>? preAiSearchTitles,
        MetadataResolutionStore? resolutions,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AiMetadataDebugPreAiAttempt> attempts = resolutions is null
            ? []
            : (await resolutions.ListAttemptsAsync(
                    claim.TaskId,
                    500,
                    cancellationToken).ConfigureAwait(false))
                .Where(attempt => string.Equals(attempt.RunId, claim.RunId, StringComparison.Ordinal))
                .OrderBy(attempt => attempt.CreatedAtUtc)
                .ThenBy(attempt => attempt.AttemptId, StringComparer.Ordinal)
                .Select(attempt => new AiMetadataDebugPreAiAttempt(
                    attempt.AttemptId,
                    attempt.Stage,
                    attempt.Strategy,
                    attempt.Priority,
                    attempt.Result,
                    attempt.ErrorCode,
                    attempt.Reason,
                    attempt.Retryable,
                    attempt.DurationMilliseconds,
                    attempt.CreatedAtUtc))
                .ToArray();
        return new AiMetadataDebugPreAiContext(
            expectedSeriesId is not null && expectedSeasonNumber is not null
                ? "episode"
                : "series_season",
            new AiMetadataDebugTaskInput(
                claim.Title,
                claim.MikanId,
                claim.GroupId,
                claim.BangumiSubjectId,
                claim.AniDbAnimeId,
                claim.ImdbTitleId,
                claim.SourceAdapter,
                claim.SourceProfileId,
                claim.SourceId,
                claim.TorrentFileCount,
                videos.Select(file => new AiMetadataDebugTaskFileInput(
                    file.RelativePath,
                    file.SizeBytes,
                    file.SourceEpisode,
                    file.FileEpisodeCandidate,
                    file.PreResolvedEpisodeNumber,
                    file.PreResolvedOtherReason,
                    file.TmdbSeasonNumber)).ToArray()),
            expectedSeriesId,
            expectedSeasonNumber,
            preAiSearchTitles?.Distinct(StringComparer.Ordinal).ToArray() ?? [],
            publication.PublishedAt,
            publication.BangumiEpisodeCandidate,
            publication.UseBangumiPubDateFirst,
            publication.Result,
            publication.ErrorCode,
            attempts);
    }

    private async Task SaveDebugAsync(
        AiMetadataDebugChain? chain,
        AiMetadataValidationResult? validation,
        int? expectedSeriesId,
        int? expectedSeasonNumber,
        CancellationToken cancellationToken)
    {
        if (chain is null || debugStore is null)
        {
            return;
        }

        try
        {
            await debugStore.WriteAsync(
                chain,
                validation,
                expectedSeriesId,
                expectedSeasonNumber,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            if (logger is not null)
            {
                DebugWriteFailed(logger, chain.TraceId, exception);
            }
        }
    }
}
