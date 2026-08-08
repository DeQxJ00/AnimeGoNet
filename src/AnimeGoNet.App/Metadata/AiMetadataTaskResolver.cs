using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed record AiMetadataTaskResolution(
    ValidatedAiMetadataMatch? Value,
    MetadataFailure? Failure,
    AiPublicationEvidenceResult? Publication,
    bool IsApplicable)
{
    public bool IsSuccess => IsApplicable && Value is not null && Failure is null;
}

public sealed class AiMetadataTaskResolver(
    IAiMetadataMatcher matcher,
    AiMetadataResultValidator validator,
    AiPublicationEvidenceResolver publicationEvidence)
{
    public async Task<AiMetadataTaskResolution> ResolveAsync(
        MetadataTaskClaim claim,
        IReadOnlyList<MetadataTaskFileProjection> files,
        int? expectedSeriesId = null,
        int? expectedSeasonNumber = null,
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
                IsApplicable: false);
        }

        var publication = await publicationEvidence.ResolveAsync(
            claim,
            cancellationToken).ConfigureAwait(false);
        var input = AiMetadataInputBoundary.Create(claim, videos, publication);

        AiMetadataMatchCandidate candidate;
        try
        {
            candidate = await matcher.MatchAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (AiMetadataMatcherException exception)
        {
            return new AiMetadataTaskResolution(
                null,
                new MetadataFailure(exception.Kind, exception.SafeCode, TmdbAccessConfirmed: false),
                publication,
                IsApplicable: true);
        }

        var validated = await validator.ValidateAsync(
            input,
            candidate,
            expectedSeriesId,
            expectedSeasonNumber,
            cancellationToken).ConfigureAwait(false);
        return new AiMetadataTaskResolution(
            validated.Value,
            validated.Failure,
            publication,
            IsApplicable: true);
    }
}
