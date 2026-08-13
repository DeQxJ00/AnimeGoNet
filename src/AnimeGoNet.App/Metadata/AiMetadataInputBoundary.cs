using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Metadata;

internal static class AiMetadataInputBoundary
{
    public static AiMetadataMatchInput Create(
        MetadataTaskClaim claim,
        IReadOnlyList<MetadataTaskFileProjection> videos,
        AiPublicationEvidenceResult publication,
        AiMetadataDebugPreAiContext? debugPreAiContext = null)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(videos);
        ArgumentNullException.ThrowIfNull(publication);

        // Deliberate data diode: this projection has no Torrent URL/fingerprint,
        // announce data, staged bytes, route snapshot, Cookie or downloader secret.
        return new AiMetadataMatchInput(
            claim.Title,
            videos.Select(file => new AiMetadataFileInput(
                file.RelativePath,
                file.SizeBytes)).ToArray(),
            claim.BangumiSubjectId,
            claim.AniDbAnimeId,
            claim.ImdbTitleId,
            claim.TorrentFileCount,
            publication.PublishedAt,
            publication.BangumiEpisodeCandidate,
            publication.UseBangumiPubDateFirst)
        {
            DebugIdentity = debugPreAiContext is null
                ? null
                : new AiMetadataDebugIdentity(claim.RunId, claim.TaskId),
            DebugPreAiContext = debugPreAiContext,
        };
    }
}
