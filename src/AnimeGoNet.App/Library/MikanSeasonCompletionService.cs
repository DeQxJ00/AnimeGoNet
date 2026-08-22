using System.Globalization;
using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Library;
using AnimeGoNet.Data.Mikan;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.App.Library;

public sealed record MikanSeasonCompletionCandidate(
    string CandidateId,
    string Title,
    long Length,
    string? PublishedDate,
    string? SourceEpisodeKind,
    int? SourceEpisode,
    int? TargetEpisode,
    string Status,
    bool DefaultSelected);

public sealed record MikanSeasonCompletionPreview(
    int TmdbSeriesId,
    int TmdbSeasonNumber,
    string ResourceRevision,
    string SourceProfileId,
    int MikanId,
    int GroupId,
    string? OffsetSource,
    int? EpisodeOffset,
    IReadOnlyList<MikanSeasonCompletionCandidate> Items);

public sealed class MikanSeasonCompletionException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class MikanSeasonCompletionService(
    AnimeLibraryStore library,
    SourceProfileStore profiles,
    MikanWorkMetadataRuleStore workRules,
    MikanTrustedOffsetStore trustedOffsets,
    RssFeedReader feeds,
    TitleParserManager titleParsers,
    MikanRssIngestProcessor ingest,
    AnimeGoOptions options)
{
    public async Task<MikanSeasonCompletionPreview> PreviewAsync(
        int tmdbSeriesId,
        int seasonNumber,
        string sourceProfileId,
        int mikanId,
        int groupId,
        CancellationToken cancellationToken = default)
    {
        var context = await RequireContextAsync(
            tmdbSeriesId,
            seasonNumber,
            sourceProfileId,
            mikanId,
            groupId,
            cancellationToken).ConfigureAwait(false);
        var feedUrl = BuildFeedUrl(mikanId, groupId);
        var feed = await feeds.ParseUrlAsync(
            feedUrl,
            context.SourceProfileId,
            cancellationToken).ConfigureAwait(false);
        if (feed.MikanId != mikanId)
        {
            throw new MikanSeasonCompletionException(
                "mikan_completion_feed_identity_mismatch",
                "Mikan RSS did not identify the requested work.");
        }

        var plan = await MikanRssBatchPlanner.CreateAsync(
            feed,
            MikanRssRuleDefaults.Create(),
            titleParsers,
            "mikan-title",
            priorityEnabled: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var completedSourceEpisodes = await library.ListCompletedMikanSourceEpisodesAsync(
            context.SourceProfileId,
            mikanId,
            cancellationToken).ConfigureAwait(false);
        var completedTargetEpisodes = context.Detail.Episodes
            .Where(item => item.Downloaded)
            .Select(item => item.EpisodeNumber)
            .ToHashSet();
        var offset = await ResolveOffsetAsync(
            tmdbSeriesId,
            seasonNumber,
            mikanId,
            groupId,
            cancellationToken).ConfigureAwait(false);

        var items = plan.Items.Select(item =>
        {
            var sourceEpisode = ParseNormalEpisode(item.Candidate);
            var targetEpisode = sourceEpisode is not null && offset.Value is not null
                ? CheckedAdd(sourceEpisode.Value, offset.Value.Value)
                : null;
            var sourceCompleted = sourceEpisode is not null
                && completedSourceEpisodes.Contains(sourceEpisode.Value);
            var targetCompleted = targetEpisode is > 0
                && completedTargetEpisodes.Contains(targetEpisode.Value);
            var ordinary = sourceEpisode is > 0;
            var status = !ordinary
                ? "episode_not_ordinary"
                : sourceCompleted
                    ? "completed_source_alias"
                    : targetCompleted
                        ? "completed_target_episode"
                        : targetEpisode is null
                            ? "requires_metadata_matching"
                            : "missing_target_episode";
            return new MikanSeasonCompletionCandidate(
                item.Candidate.Id,
                item.FeedItem.Title,
                item.FeedItem.Length,
                item.FeedItem.PublishedDate,
                item.Candidate.SourceEpisodeKind,
                sourceEpisode,
                targetEpisode,
                status,
                ordinary && !sourceCompleted && !targetCompleted);
        }).ToArray();

        return new MikanSeasonCompletionPreview(
            tmdbSeriesId,
            seasonNumber,
            context.Detail.Season.ResourceRevision,
            context.SourceProfileId,
            mikanId,
            groupId,
            offset.Source,
            offset.Value,
            items);
    }

    public async Task<MikanRssIngestResult> ConfirmAsync(
        int tmdbSeriesId,
        int seasonNumber,
        string sourceProfileId,
        int mikanId,
        int groupId,
        string expectedResourceRevision,
        IReadOnlyList<string> selectedCandidateIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedCandidateIds);
        if (selectedCandidateIds.Count is < 1 or > 500
            || selectedCandidateIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 80))
        {
            throw new MikanSeasonCompletionException(
                "mikan_completion_selection_invalid",
                "Select between 1 and 500 valid RSS candidates.");
        }

        var context = await RequireContextAsync(
            tmdbSeriesId,
            seasonNumber,
            sourceProfileId,
            mikanId,
            groupId,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                context.Detail.Season.ResourceRevision,
                expectedResourceRevision,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new MikanSeasonCompletionException(
                "mikan_completion_library_changed",
                "The library season changed; preview the RSS list again.");
        }

        var feedUrl = BuildFeedUrl(mikanId, groupId);
        var feed = await feeds.ParseUrlAsync(
            feedUrl,
            context.SourceProfileId,
            cancellationToken).ConfigureAwait(false);
        if (feed.MikanId != mikanId)
        {
            throw new MikanSeasonCompletionException(
                "mikan_completion_feed_identity_mismatch",
                "Mikan RSS did not identify the requested work.");
        }

        var plan = await MikanRssBatchPlanner.CreateAsync(
            feed,
            MikanRssRuleDefaults.Create(),
            titleParsers,
            "mikan-title",
            priorityEnabled: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var selected = selectedCandidateIds.ToHashSet(StringComparer.Ordinal);
        if (selected.Count != selectedCandidateIds.Count)
        {
            throw new MikanSeasonCompletionException(
                "mikan_completion_selection_invalid",
                "Selected RSS candidates must be unique.");
        }
        var known = plan.Items.Select(item => item.Candidate.Id).ToHashSet(StringComparer.Ordinal);
        if (!selected.IsSubsetOf(known))
        {
            throw new MikanSeasonCompletionException(
                "mikan_completion_feed_changed",
                "The Mikan RSS list changed; preview it again before confirming.");
        }

        var selectedFeed = feed with
        {
            Items = plan.Items
                .Where(item => selected.Contains(item.Candidate.Id))
                .Select(item => item.FeedItem)
                .ToArray(),
        };
        return await ingest.ProcessAsync(
            selectedFeed,
            context.SourceProfileId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CompletionContext> RequireContextAsync(
        int tmdbSeriesId,
        int seasonNumber,
        string sourceProfileId,
        int mikanId,
        int groupId,
        CancellationToken cancellationToken)
    {
        if (tmdbSeriesId <= 0 || seasonNumber <= 0 || mikanId <= 0 || groupId <= 0)
        {
            throw new MikanSeasonCompletionException(
                "mikan_completion_identity_invalid",
                "TMDB Series, Season, mikanid and groupid must be positive integers.");
        }
        var profileId = sourceProfileId?.Trim().ToLowerInvariant() ?? string.Empty;
        if (profileId.Length is < 1 or > 128)
        {
            throw new MikanSeasonCompletionException(
                "mikan_completion_source_invalid",
                "A valid source profile is required.");
        }
        var detail = await library.GetSeasonAsync(
            tmdbSeriesId,
            seasonNumber,
            cancellationToken).ConfigureAwait(false)
            ?? throw new MikanSeasonCompletionException(
                "library_season_not_found",
                "The requested TMDB season was not found in the local library.");
        if (!detail.Audit.MikanBindings.Any(binding =>
                binding.MikanId == mikanId
                && binding.GroupId == groupId
                && string.Equals(
                    binding.SourceProfileId,
                    profileId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new MikanSeasonCompletionException(
                "mikan_completion_binding_unknown",
                "The requested mikanid and groupid are not associated with this library season.");
        }
        var profile = await profiles.GetEnabledAsync(profileId, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null || !string.Equals(profile.Adapter, "mikan", StringComparison.OrdinalIgnoreCase))
        {
            throw new MikanSeasonCompletionException(
                "mikan_completion_source_unavailable",
                "The associated Mikan source profile is missing or disabled.");
        }
        return new CompletionContext(detail, profile.Id);
    }

    private async Task<(string? Source, int? Value)> ResolveOffsetAsync(
        int tmdbSeriesId,
        int seasonNumber,
        int mikanId,
        int groupId,
        CancellationToken cancellationToken)
    {
        var manual = await workRules.GetEnabledAsync(mikanId, cancellationToken)
            .ConfigureAwait(false);
        if (manual is
            {
                TmdbSeriesId: not null,
                TmdbSeasonNumber: not null,
                EpisodeOffset: not null,
            }
            && manual.TmdbSeriesId == tmdbSeriesId
            && manual.TmdbSeasonNumber == seasonNumber)
        {
            return ("manual_offset", manual.EpisodeOffset);
        }

        if (!options.Metadata.MikanTrustedOffsetCacheEnabled)
        {
            return (null, null);
        }
        var trusted = await trustedOffsets.GetTrustedAsync(
            mikanId,
            groupId,
            options.Metadata.MikanTrustedOffsetRequiredEpisodes,
            cancellationToken).ConfigureAwait(false);
        return trusted is not null
            && trusted.TmdbSeriesId == tmdbSeriesId
            && trusted.TmdbSeasonNumber == seasonNumber
                ? ("trusted_offset", trusted.EpisodeOffset)
                : (null, null);
    }

    private string BuildFeedUrl(int mikanId, int groupId)
    {
        var endpoint = new Uri(options.Metadata.Mikan.BaseUrl, "RSS/Bangumi");
        var builder = new UriBuilder(endpoint)
        {
            Query = $"bangumiId={mikanId.ToString(CultureInfo.InvariantCulture)}&subgroupid={groupId.ToString(CultureInfo.InvariantCulture)}",
        };
        return builder.Uri.AbsoluteUri;
    }

    private static int? ParseNormalEpisode(MikanRssCandidate candidate) =>
        string.Equals(candidate.SourceEpisodeKind, "normal", StringComparison.Ordinal)
        && int.TryParse(
            candidate.SourceEpisode,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var episode)
        && episode > 0
            ? episode
            : null;

    private static int? CheckedAdd(int episode, int offset)
    {
        try
        {
            var value = checked(episode + offset);
            return value > 0 ? value : null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private sealed record CompletionContext(
        AnimeSeasonDetailProjection Detail,
        string SourceProfileId);
}
