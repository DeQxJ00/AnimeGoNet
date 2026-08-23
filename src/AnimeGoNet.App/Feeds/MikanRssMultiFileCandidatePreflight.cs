using System.Globalization;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Ingest;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.App.Feeds;

public sealed class MikanRssCandidatePreflightResult(
    MikanRssBatchPlan plan,
    Dictionary<string, StagedTorrent>? stagedTorrents = null) : IAsyncDisposable
{
    private readonly Dictionary<string, StagedTorrent> _stagedTorrents =
        stagedTorrents ?? new Dictionary<string, StagedTorrent>(StringComparer.Ordinal);

    public MikanRssBatchPlan Plan { get; } = plan;

    public StagedTorrent? TakeStagedTorrent(string candidateId)
    {
        if (!_stagedTorrents.Remove(candidateId, out var staged))
        {
            return null;
        }

        return staged;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var staged in _stagedTorrents.Values)
        {
            await staged.DisposeAsync().ConfigureAwait(false);
        }
        _stagedTorrents.Clear();
    }
}

public sealed class MikanRssMultiFileCandidatePreflight(
    ITorrentStagingService staging,
    AnimeGoOptions options,
    IBangumiSubjectClient bangumiSubjects,
    IBangumiEpisodeClient? bangumiEpisodes,
    TmdbSeriesSeasonResolver seriesSeasonResolver,
    ITmdbClient tmdb)
{
    public static bool ShouldRun(MikanRssBatchPlan plan, bool priorityEnabled)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return priorityEnabled
            && plan.MikanId is > 0
            && plan.Items.Count(item => item.LegacyFilterAudit.Eligible
                && item.Decision.Kind == MikanRssDecisionKind.Winner
                && string.Equals(
                    item.Decision.Reason,
                    "UngroupedBypass",
                    StringComparison.Ordinal)) > 1;
    }

    public async Task<MikanRssCandidatePreflightResult> RefineAsync(
        RssFeedDocument feed,
        MikanRssBatchPlan plan,
        MikanRssRuleSet rules,
        SourceProfileRecord profile,
        int bangumiSubjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfLessThan(bangumiSubjectId, 1);
        if (!ShouldRun(plan, profile.RssPriorityEnabled) || bangumiEpisodes is null)
        {
            return new MikanRssCandidatePreflightResult(plan);
        }

        var stagedByCandidate = new Dictionary<string, StagedTorrent>(StringComparer.Ordinal);
        try
        {
            var sourceEpisodesByCandidate = new Dictionary<string, int[]>(StringComparer.Ordinal);
            foreach (var item in plan.Items.Where(item =>
                         item.LegacyFilterAudit.Eligible
                         && item.Decision.Kind == MikanRssDecisionKind.Winner
                         && string.Equals(
                             item.Decision.Reason,
                             "UngroupedBypass",
                             StringComparison.Ordinal)))
            {
                var staged = await TryStageAsync(
                    item.FeedItem.TorrentUrl,
                    profile,
                    cancellationToken).ConfigureAwait(false);
                if (staged is null)
                {
                    continue;
                }

                var videoFiles = staged.Metadata.Files
                    .Where(file => !file.IsPadding
                        && SubtitleAssociationResolver.IsVideo(file.RelativePath))
                    .ToArray();
                var sourceEpisodes = videoFiles
                    .Select(file => FileEpisodeCandidateResolver.Resolve(
                        profile.Adapter,
                        file.RelativePath).Episode)
                    .ToArray();
                if (videoFiles.Length <= 1
                    || sourceEpisodes.Any(value => value is null or <= 0)
                    || sourceEpisodes.Select(value => value!.Value).Distinct().Count() != videoFiles.Length)
                {
                    await staged.DisposeAsync().ConfigureAwait(false);
                    continue;
                }

                stagedByCandidate.Add(item.Candidate.Id, staged);
                sourceEpisodesByCandidate.Add(
                    item.Candidate.Id,
                    sourceEpisodes.Select(value => value!.Value).Order().ToArray());
            }

            if (sourceEpisodesByCandidate.Count == 0)
            {
                return new MikanRssCandidatePreflightResult(plan, stagedByCandidate);
            }

            var subject = await bangumiSubjects.GetSubjectAsync(
                bangumiSubjectId,
                cancellationToken).ConfigureAwait(false);
            if (subject is null)
            {
                return await UnchangedAsync(plan, stagedByCandidate).ConfigureAwait(false);
            }

            var titles = TmdbSeriesSeasonResolver.BangumiTitles(subject);
            var seriesSeason = await seriesSeasonResolver.ResolveAsync(
                titles.Count == 0 ? [feed.Items[0].Title] : titles,
                subject.AirDate,
                cancellationToken).ConfigureAwait(false);
            if (!seriesSeason.IsSuccess)
            {
                return await UnchangedAsync(plan, stagedByCandidate).ConfigureAwait(false);
            }

            var sourceEpisodeCandidates = sourceEpisodesByCandidate.Values
                .SelectMany(value => value)
                .Concat(plan.Items
                    .Where(IsRuleEligible)
                    .Select(item => ParseNormalEpisode(item.Candidate))
                    .Where(value => value is > 0)
                    .Select(value => value!.Value))
                .Distinct()
                .ToArray();
            var bangumiValues = await LoadBangumiEpisodesAsync(
                bangumiSubjectId,
                sourceEpisodeCandidates,
                cancellationToken).ConfigureAwait(false);
            var tmdbSeason = seriesSeason.Season!;
            var tmdbEpisodes = tmdbSeason.Episodes ?? [];
            if (tmdbEpisodes.Count == 0)
            {
                return await UnchangedAsync(plan, stagedByCandidate).ConfigureAwait(false);
            }

            var verifiedBySourceEpisode = new Dictionary<int, TmdbEpisode>();
            foreach (var sourceEpisode in sourceEpisodeCandidates)
            {
                var match = BangumiTmdbEpisodeDateResolver.Resolve(
                    bangumiValues,
                    tmdbEpisodes,
                    sourceEpisode,
                    allowFilenameNearestFallback: false);
                if (!match.IsSuccess)
                {
                    continue;
                }

                var episode = await tmdb.GetEpisodeAsync(
                    seriesSeason.Details!.Series.Id,
                    tmdbSeason.SeasonNumber,
                    match.Episode!.EpisodeNumber,
                    cancellationToken).ConfigureAwait(false);
                if (episode is not null
                    && episode.SeriesId == seriesSeason.Details.Series.Id
                    && episode.SeasonNumber == tmdbSeason.SeasonNumber
                    && episode.EpisodeNumber == match.Episode.EpisodeNumber)
                {
                    verifiedBySourceEpisode[sourceEpisode] = episode;
                }
            }

            var coverages = new List<MikanRssVerifiedCoverage>();
            foreach (var item in plan.Items.Where(IsRuleEligible))
            {
                int[] sourceEpisodes;
                if (sourceEpisodesByCandidate.TryGetValue(item.Candidate.Id, out var multiEpisodes))
                {
                    sourceEpisodes = multiEpisodes;
                }
                else if (ParseNormalEpisode(item.Candidate) is { } normalEpisode)
                {
                    sourceEpisodes = [normalEpisode];
                }
                else
                {
                    continue;
                }

                if (sourceEpisodes.Any(value => !verifiedBySourceEpisode.ContainsKey(value)))
                {
                    if (sourceEpisodes.Length > 1
                        && stagedByCandidate.Remove(item.Candidate.Id, out var invalidStaged))
                    {
                        await invalidStaged.DisposeAsync().ConfigureAwait(false);
                    }
                    continue;
                }

                coverages.Add(new MikanRssVerifiedCoverage(
                    item.Candidate.Id,
                    seriesSeason.Details!.Series.Id,
                    tmdbSeason.SeasonNumber,
                    sourceEpisodes.Select(value => verifiedBySourceEpisode[value].EpisodeNumber)
                        .Distinct()
                        .Order()
                        .ToArray()));
            }

            if (!coverages.Any(value => value.EpisodeNumbers.Count > 1))
            {
                return await UnchangedAsync(plan, stagedByCandidate).ConfigureAwait(false);
            }

            var candidates = plan.Items
                .Where(item => coverages.Any(value => value.CandidateId == item.Candidate.Id))
                .Select(item => item.Candidate)
                .ToArray();
            var refined = MikanRssVerifiedCoverageSelector.Evaluate(candidates, coverages, rules)
                .ToDictionary(value => value.CandidateId, StringComparer.Ordinal);
            var refinedItems = plan.Items.Select(item =>
                refined.TryGetValue(item.Candidate.Id, out var decision)
                    ? item with { Decision = decision }
                    : item).ToArray();
            foreach (var candidateId in stagedByCandidate.Keys.ToArray())
            {
                if (!refined.TryGetValue(candidateId, out var decision)
                    || decision.Kind != MikanRssDecisionKind.Winner)
                {
                    var loser = stagedByCandidate[candidateId];
                    stagedByCandidate.Remove(candidateId);
                    await loser.DisposeAsync().ConfigureAwait(false);
                }
            }

            return new MikanRssCandidatePreflightResult(
                plan with { Items = refinedItems },
                stagedByCandidate);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeAllAsync(stagedByCandidate).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is
            BangumiClientException
            or TmdbClientException
            or TorrentStagingException
            or HttpRequestException
            or IOException)
        {
            await DisposeAllAsync(stagedByCandidate).ConfigureAwait(false);
            return new MikanRssCandidatePreflightResult(plan);
        }
    }

    private async Task<StagedTorrent?> TryStageAsync(
        string torrentUrl,
        SourceProfileRecord profile,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(torrentUrl, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        var requestUrl = MikanEndpointRewriter.Rewrite(parsed, options.Metadata.Mikan);
        var allowedHosts = profile.AllowedTorrentHosts.Append(requestUrl.IdnHost)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var trustedPrivateHosts = string.Equals(
                requestUrl.IdnHost,
                options.Metadata.Mikan.BaseUrl.IdnHost,
                StringComparison.OrdinalIgnoreCase)
            ? new[] { requestUrl.IdnHost }
            : Array.Empty<string>();
        try
        {
            return await staging.StageAsync(
                requestUrl,
                new TorrentSourcePolicy(
                    profile.Id,
                    allowedHosts,
                    profile.MikanIdentityCookie,
                    trustedPrivateHosts),
                cancellationToken).ConfigureAwait(false);
        }
        catch (TorrentStagingException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<BangumiEpisode>> LoadBangumiEpisodesAsync(
        int subjectId,
        IReadOnlyCollection<int> sourceCandidates,
        CancellationToken cancellationToken)
    {
        var values = await bangumiEpisodes!.GetEpisodesAsync(subjectId, cancellationToken)
            .ConfigureAwait(false);
        if (bangumiEpisodes is IBangumiEpisodeRefreshClient refreshClient
            && sourceCandidates.Any(candidate => HasIdentityWithoutAirDate(values, candidate)))
        {
            values = await refreshClient.RefreshEpisodesAsync(subjectId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (sourceCandidates.All(candidate => HasIdentity(values, candidate)))
        {
            return values;
        }

        var relations = await bangumiSubjects.GetRelatedSubjectsAsync(subjectId, cancellationToken)
            .ConfigureAwait(false);
        foreach (var sequel in relations
                     .Where(value => value.Type == 2
                         && string.Equals(value.Relation, "续集", StringComparison.Ordinal))
                     .OrderBy(value => value.Id)
                     .Take(8))
        {
            var sequelEpisodes = await bangumiEpisodes.GetEpisodesAsync(
                sequel.Id,
                cancellationToken).ConfigureAwait(false);
            values = values.Concat(sequelEpisodes)
                .GroupBy(value => value.Id)
                .Select(group => group.First())
                .ToArray();
            if (sourceCandidates.All(candidate => HasIdentity(values, candidate)))
            {
                break;
            }
        }

        return values;
    }

    private static bool IsRuleEligible(MikanRssPlannedItem item) =>
        item.LegacyFilterAudit.Eligible
        && item.Decision.Kind is not MikanRssDecisionKind.RejectedByBlacklist
            and not MikanRssDecisionKind.RejectedByWhitelist
            and not MikanRssDecisionKind.RejectedByLegacyFilter
            and not MikanRssDecisionKind.FilterEvaluationFailed;

    private static int? ParseNormalEpisode(MikanRssCandidate candidate) =>
        string.Equals(candidate.SourceEpisodeKind, "normal", StringComparison.Ordinal)
        && int.TryParse(
            candidate.SourceEpisode,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value)
        && value > 0
            ? value
            : null;

    private static bool HasIdentity(IReadOnlyList<BangumiEpisode> episodes, int sourceEpisode) =>
        episodes.Any(value => value.Type == 0
            && (value.EpisodeNumber == sourceEpisode || value.SortNumber == sourceEpisode));

    private static bool HasIdentityWithoutAirDate(
        IReadOnlyList<BangumiEpisode> episodes,
        int sourceEpisode)
    {
        var matches = episodes.Where(value => value.Type == 0
            && (value.EpisodeNumber == sourceEpisode || value.SortNumber == sourceEpisode)).ToArray();
        return matches.Length > 0 && matches.All(value => value.AirDate is null);
    }

    private static async Task<MikanRssCandidatePreflightResult> UnchangedAsync(
        MikanRssBatchPlan plan,
        Dictionary<string, StagedTorrent> staged)
    {
        await DisposeAllAsync(staged).ConfigureAwait(false);
        return new MikanRssCandidatePreflightResult(plan);
    }

    private static async Task DisposeAllAsync(Dictionary<string, StagedTorrent> staged)
    {
        foreach (var value in staged.Values)
        {
            await value.DisposeAsync().ConfigureAwait(false);
        }
        staged.Clear();
    }
}
