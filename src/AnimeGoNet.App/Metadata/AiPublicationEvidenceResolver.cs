using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed record AiPublicationEvidenceResult(
    DateTimeOffset? PublishedAt,
    int? BangumiEpisodeCandidate,
    bool UseBangumiPubDateFirst,
    bool ShouldAudit,
    string Result,
    string? ErrorCode,
    bool Retryable);

public sealed class AiPublicationEvidenceResolver(
    IBangumiEpisodeClient? bangumi,
    AiMatchingOptions options)
{
    public async Task<AiPublicationEvidenceResult> ResolveAsync(
        MetadataTaskClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var publishedAt = string.Equals(
                claim.SourceAdapter,
                "mikan",
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(claim.SourcePublishedAtRaw)
                ? claim.SourcePublishedAt
                : null;
        if (!options.UseBangumiPubDateFirst)
        {
            return Disabled(publishedAt, shouldAudit: false, "ai_pubdate_disabled");
        }

        if (!string.Equals(claim.SourceAdapter, "mikan", StringComparison.OrdinalIgnoreCase))
        {
            return Disabled(null, shouldAudit: true, "ai_pubdate_source_not_mikan");
        }

        if (claim.TorrentFileCount != 1)
        {
            return Disabled(publishedAt, shouldAudit: true, "ai_pubdate_torrent_file_count_not_one");
        }

        if (claim.BangumiSubjectId is null)
        {
            return Disabled(publishedAt, shouldAudit: true, "ai_pubdate_bgmid_missing");
        }

        if (string.IsNullOrWhiteSpace(claim.SourcePublishedAtRaw)
            || claim.SourcePublishedAt is null)
        {
            return Disabled(null, shouldAudit: true, "ai_pubdate_published_at_missing");
        }

        if (bangumi is null)
        {
            return Disabled(publishedAt, shouldAudit: true, "ai_pubdate_client_unavailable");
        }

        IReadOnlyList<BangumiEpisode> episodes;
        try
        {
            episodes = await bangumi.GetEpisodesAsync(
                claim.BangumiSubjectId.Value,
                cancellationToken).ConfigureAwait(false);
        }
        catch (BangumiClientException exception)
        {
            return new AiPublicationEvidenceResult(
                publishedAt,
                null,
                false,
                true,
                "error",
                exception.SafeCode,
                IsRetryable(exception.Kind));
        }

        var candidate = BangumiPublicationEpisodeResolver.SelectClosest(
            episodes,
            claim.SourcePublishedAt.Value);
        if (candidate is null)
        {
            return new AiPublicationEvidenceResult(
                publishedAt,
                null,
                false,
                true,
                "not_matched",
                "ai_pubdate_episode_not_found",
                false);
        }

        return new AiPublicationEvidenceResult(
            claim.SourcePublishedAt,
            candidate,
            true,
            true,
            "matched",
            null,
            false);
    }

    private static AiPublicationEvidenceResult Disabled(
        DateTimeOffset? publishedAt,
        bool shouldAudit,
        string code) =>
        new(
            publishedAt,
            null,
            false,
            shouldAudit,
            "not_applicable",
            code,
            false);

    private static bool IsRetryable(MetadataFailureKind kind) =>
        kind is MetadataFailureKind.Network or MetadataFailureKind.RemoteService;
}
