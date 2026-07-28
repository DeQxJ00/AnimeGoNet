using System.Globalization;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Mikan;

namespace AnimeGoNet.App.Metadata;

public sealed class AutomaticMetadataResolutionProcessor(
    MetadataResolutionStore resolutions,
    IBangumiSubjectClient bangumi,
    BangumiSeasonBacktraceResolver backtrace,
    TmdbSeriesResolver seriesResolver,
    ITmdbClient tmdb,
    IAiMetadataMatcher aiMatcher,
    AiMetadataResultValidator aiValidator,
    AiPublicationEvidenceResolver publicationEvidence,
    MikanWorkMetadataRuleStore rules,
    MikanTrustedOffsetStore trustedOffsets,
    AnimeGoOptions options,
    TimeProvider? timeProvider = null,
    IBangumiEpisodeClient? bangumiEpisodes = null)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var claim = await resolutions.TryClaimNextDownloadedAsync(
            _timeProvider.GetUtcNow(),
            LeaseDuration,
            cancellationToken).ConfigureAwait(false);
        if (claim is null)
        {
            return false;
        }

        var rule = claim.MikanId is null
            ? null
            : await rules.GetEnabledAsync(claim.MikanId.Value, cancellationToken).ConfigureAwait(false);
        if (rule?.BangumiSubjectId is not null)
        {
            claim = claim with { BangumiSubjectId = rule.BangumiSubjectId };
        }

        if (await TryCompleteTrustedOffsetAsync(claim, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        BangumiSubject? subject = null;
        var title = claim.Title;
        if (claim.BangumiSubjectId is not null)
        {
            var started = _timeProvider.GetTimestamp();
            try
            {
                subject = await bangumi.GetSubjectAsync(
                    claim.BangumiSubjectId.Value,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (BangumiClientException exception)
            {
                var failure = new MetadataFailure(exception.Kind, exception.SafeCode, false);
                await RecordAsync(claim, "bangumi", "bangumi_subject", null, "failed", failure.Code,
                    IsRetryable(failure.Kind), started, cancellationToken).ConfigureAwait(false);
                await FailAsync(claim, failure, "tmdb_access_not_attempted", cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (subject is null)
            {
                var failure = new MetadataFailure(MetadataFailureKind.SemanticNoMatch, "bangumi_subject_not_found", false);
                await RecordAsync(claim, "bangumi", "bangumi_subject", null, "failed", failure.Code,
                    false, started, cancellationToken).ConfigureAwait(false);
                await FailAsync(claim, failure, "tmdb_access_not_attempted", cancellationToken).ConfigureAwait(false);
                return true;
            }

            title = string.IsNullOrWhiteSpace(subject.ChineseName) ? subject.Name : subject.ChineseName;
            await RecordAsync(claim, "bangumi", "bangumi_subject", null, "matched", null,
                false, started, cancellationToken).ConfigureAwait(false);
        }

        var seriesStarted = _timeProvider.GetTimestamp();
        var seriesResult = await seriesResolver.ResolveAsync(title, cancellationToken).ConfigureAwait(false);
        if (!seriesResult.IsSuccess)
        {
            var failure = seriesResult.Failure!;
            await RecordAsync(claim, "series", "tmdb_title", null, "failed", failure.Code,
                IsRetryable(failure.Kind), seriesStarted, cancellationToken).ConfigureAwait(false);
            if (options.Metadata.Ai.UseSeasonMatch
                && await TryCompleteAiSeasonAsync(claim, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (await TryCompleteBangumiFallbackAsync(
                    claim,
                    subject,
                    failure,
                    cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            await FailAsync(claim, failure, SeriesFailureDenialReason(claim, failure), cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        TmdbSeriesDetails? details;
        try
        {
            details = await tmdb.GetSeriesDetailsAsync(seriesResult.Value!.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (TmdbClientException exception)
        {
            var failure = ToFailure(exception);
            await RecordAsync(claim, "series", "tmdb_title", null, "failed", failure.Code,
                IsRetryable(failure.Kind), seriesStarted, cancellationToken).ConfigureAwait(false);
            await FailAsync(claim, failure, SeriesFailureDenialReason(claim, failure), cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        if (details is null || details.Series.Id != seriesResult.Value.Id)
        {
            var failure = new MetadataFailure(MetadataFailureKind.SemanticNoMatch, "tmdb_series_details_not_found", true);
            await RecordAsync(claim, "series", "tmdb_title", null, "failed", failure.Code,
                false, seriesStarted, cancellationToken).ConfigureAwait(false);
            await FailAsync(claim, failure, SeriesFailureDenialReason(claim, failure), cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        await RecordAsync(claim, "series", "tmdb_title", null, "matched", null,
            false, seriesStarted, cancellationToken).ConfigureAwait(false);

        var seasonStarted = _timeProvider.GetTimestamp();
        var direct = TmdbSeasonSelector.SelectByAirDate(details.Seasons, subject?.AirDate);
        if (direct.IsSuccess)
        {
            await CompleteSeasonAsync(claim, details.Series, direct.Value!, "tmdb_air_date", null,
                seasonStarted, cancellationToken).ConfigureAwait(false);
            return true;
        }

        await RecordAsync(claim, "season", "tmdb_air_date", null, "not_matched", direct.Failure!.Code,
            false, seasonStarted, cancellationToken).ConfigureAwait(false);
        var policy = options.Metadata.SeasonFailure;
        if (policy.Skip)
        {
            var failure = new MetadataFailure(MetadataFailureKind.SemanticNoMatch, "tmdb_season_skipped", true);
            await RecordAsync(claim, "season", "skip", 4, "skipped", failure.Code,
                false, _timeProvider.GetTimestamp(), cancellationToken).ConfigureAwait(false);
            await FailAsync(claim, failure, "tmdb_series_resolved", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (policy.Backtrace && claim.BangumiSubjectId is not null)
        {
            var started = _timeProvider.GetTimestamp();
            try
            {
                var result = await backtrace.ResolveAsync(
                    claim.BangumiSubjectId.Value,
                    details.Seasons,
                    cancellationToken).ConfigureAwait(false);
                await RecordAsync(claim, "season", "backtrace", 3,
                    result.IsSuccess ? "matched" : "not_matched",
                    result.Failure?.Code,
                    false, started, cancellationToken).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    await resolutions.CompleteSeasonAsync(
                        claim, details.Series, result.Season!, _timeProvider.GetUtcNow(), cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }
            }
            catch (BangumiClientException exception)
            {
                await RecordAsync(claim, "season", "backtrace", 3, "error", exception.SafeCode,
                    IsRetryable(exception.Kind), started, cancellationToken).ConfigureAwait(false);
            }
        }

        if (options.Metadata.Ai.UseSeasonMatch)
        {
            if (await TryCompleteAiSeasonAsync(claim, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        if (policy.UseTitleSeason)
        {
            var started = _timeProvider.GetTimestamp();
            var seasonNumber = TmdbSeasonFallbackSelector.ParseSeasonNumber(claim.Title);
            var matched = seasonNumber is > 0;
            await RecordAsync(claim, "season", "title_season", 2,
                matched ? "matched" : "not_matched",
                matched ? null : "title_season_not_found",
                false, started, cancellationToken).ConfigureAwait(false);
            if (matched)
            {
                await resolutions.CompleteLocalSeasonAsync(
                    claim,
                    details.Series,
                    seasonNumber!.Value,
                    _timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        if (policy.UseFirstSeason)
        {
            var started = _timeProvider.GetTimestamp();
            await RecordAsync(claim, "season", "first_season", 1,
                "matched",
                null,
                false, started, cancellationToken).ConfigureAwait(false);
            await resolutions.CompleteLocalSeasonAsync(
                claim,
                details.Series,
                1,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        await FailAsync(claim, direct.Failure!, "tmdb_series_resolved", cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TryCompleteBangumiFallbackAsync(
        MetadataTaskClaim claim,
        BangumiSubject? subject,
        MetadataFailure failure,
        CancellationToken cancellationToken)
    {
        if (!options.Metadata.TmdbFailureUseBangumi
            || failure.Kind != MetadataFailureKind.SemanticNoMatch
            || !failure.TmdbAccessConfirmed
            || subject is null
            || claim.BangumiSubjectId != subject.Id)
        {
            return false;
        }

        var policy = options.Metadata.SeasonFailure;
        int? seasonNumber = policy.UseTitleSeason
            ? TmdbSeasonFallbackSelector.ParseSeasonNumber(claim.Title)
                ?? TmdbSeasonFallbackSelector.ParseSeasonNumber(subject.ChineseName)
                ?? TmdbSeasonFallbackSelector.ParseSeasonNumber(subject.Name)
            : null;
        if (seasonNumber is null && policy.UseFirstSeason)
        {
            seasonNumber = 1;
        }

        var started = _timeProvider.GetTimestamp();
        if (seasonNumber is null or <= 0)
        {
            await RecordAsync(
                claim, "season", "bangumi_fallback", null, "not_matched",
                "bangumi_fallback_season_missing", false, started, cancellationToken).ConfigureAwait(false);
            return false;
        }

        await RecordAsync(
            claim, "season", "bangumi_fallback", null, "matched",
            null, false, started, cancellationToken).ConfigureAwait(false);
        var bangumiEpisodeIds = await ResolveBangumiEpisodeIdsAsync(
            claim,
            bangumiEpisodes,
            cancellationToken).ConfigureAwait(false);
        await resolutions.CompleteBangumiFallbackAsync(
            claim,
            subject,
            seasonNumber.Value,
            failure,
            bangumiEpisodeIds,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<IReadOnlyDictionary<string, int>?> ResolveBangumiEpisodeIdsAsync(
        MetadataTaskClaim claim,
        IBangumiEpisodeClient? client,
        CancellationToken cancellationToken)
    {
        if (client is null
            || claim.BangumiSubjectId is null or <= 0
            || claim.Files is null
            || claim.Files.All(file => string.IsNullOrWhiteSpace(file.SourceEpisode)))
        {
            return null;
        }

        var started = _timeProvider.GetTimestamp();
        try
        {
            var episodes = await client.GetEpisodesAsync(
                claim.BangumiSubjectId.Value,
                cancellationToken).ConfigureAwait(false);
            var resolved = claim.Files
                .Select(file => (
                    file.FileId,
                    EpisodeId: BangumiEpisodeIdentityResolver.Resolve(episodes, file.SourceEpisode)))
                .Where(value => value.EpisodeId is > 0)
                .ToDictionary(value => value.FileId, value => value.EpisodeId!.Value, StringComparer.Ordinal);
            await RecordAsync(
                claim,
                "episode",
                "bangumi_fallback_episode_identity",
                null,
                resolved.Count > 0 ? "matched" : "not_matched",
                resolved.Count > 0 ? null : "bangumi_episode_identity_not_found",
                false,
                started,
                cancellationToken).ConfigureAwait(false);
            return resolved;
        }
        catch (BangumiClientException exception)
        {
            await RecordAsync(
                claim,
                "episode",
                "bangumi_fallback_episode_identity",
                null,
                "failed",
                exception.SafeCode,
                IsRetryable(exception.Kind),
                started,
                cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    private async Task<bool> TryCompleteTrustedOffsetAsync(
        MetadataTaskClaim claim,
        CancellationToken cancellationToken)
    {
        if (!options.Metadata.MikanTrustedOffsetCacheEnabled
            || !string.Equals(claim.SourceAdapter, "mikan", StringComparison.OrdinalIgnoreCase)
            || claim.MikanId is null or <= 0
            || claim.GroupId is null or <= 0)
        {
            return false;
        }

        var started = _timeProvider.GetTimestamp();
        var trusted = await trustedOffsets.GetTrustedAsync(
            claim.MikanId.Value,
            claim.GroupId.Value,
            cancellationToken).ConfigureAwait(false);
        if (trusted is null)
        {
            await RecordAsync(
                claim, "season", "trusted_mikan_offset", null, "not_matched",
                "trusted_offset_not_found", false, started, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var canonical = await resolutions.GetCanonicalSeasonAsync(
            trusted.TmdbSeriesId,
            trusted.TmdbSeasonNumber,
            cancellationToken).ConfigureAwait(false);
        if (canonical is null)
        {
            await RecordAsync(
                claim, "season", "trusted_mikan_offset", null, "not_matched",
                "trusted_offset_projection_missing", false, started, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var videos = (claim.Files ?? [])
            .Where(file => SubtitleAssociationResolver.IsVideo(file.RelativePath))
            .ToArray();
        var seeds = new List<MetadataSeasonFileSeed>(videos.Length);
        foreach (var file in videos)
        {
            if (int.TryParse(
                    file.FileEpisodeCandidate,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var sourceEpisode)
                && sourceEpisode > 0)
            {
                int targetEpisode;
                try
                {
                    targetEpisode = checked(sourceEpisode + trusted.EpisodeOffset);
                }
                catch (OverflowException)
                {
                    targetEpisode = 0;
                }

                if (targetEpisode <= 0)
                {
                    await RecordAsync(
                        claim, "season", "trusted_mikan_offset", null, "not_matched",
                        "trusted_offset_episode_invalid", false, started, cancellationToken).ConfigureAwait(false);
                    return false;
                }

                seeds.Add(new MetadataSeasonFileSeed(file.RelativePath, targetEpisode, null));
                continue;
            }

            var parsed = TorrentEpisodeCandidateParser.Parse(file.RelativePath);
            if (parsed.Kind is TorrentEpisodeCandidateKind.Special or TorrentEpisodeCandidateKind.Fractional)
            {
                seeds.Add(new MetadataSeasonFileSeed(file.RelativePath, null, parsed.Reason));
                continue;
            }

            await RecordAsync(
                claim, "season", "trusted_mikan_offset", null, "not_matched",
                "trusted_offset_file_ineligible", false, started, cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (seeds.Count == 0)
        {
            await RecordAsync(
                claim, "season", "trusted_mikan_offset", null, "not_applicable",
                "trusted_offset_video_files_missing", false, started, cancellationToken).ConfigureAwait(false);
            return false;
        }

        await RecordAsync(
            claim, "season", "trusted_mikan_offset", null, "matched",
            null, false, started, cancellationToken).ConfigureAwait(false);
        await resolutions.CompleteAiSeasonAsync(
            claim,
            canonical.Series,
            canonical.Season,
            seeds,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TryCompleteAiSeasonAsync(
        MetadataTaskClaim claim,
        CancellationToken cancellationToken)
    {
        var videos = (claim.Files ?? [])
            .Where(file => SubtitleAssociationResolver.IsVideo(file.RelativePath))
            .ToArray();
        var started = _timeProvider.GetTimestamp();
        if (videos.Length == 0)
        {
            await RecordAsync(
                claim,
                "season",
                "ai_season",
                null,
                "not_applicable",
                "ai_video_files_missing",
                false,
                started,
                cancellationToken).ConfigureAwait(false);
            return false;
        }

        var publicationStarted = _timeProvider.GetTimestamp();
        var publication = await publicationEvidence.ResolveAsync(
            claim,
            cancellationToken).ConfigureAwait(false);
        if (publication.ShouldAudit)
        {
            await RecordAsync(
                claim,
                "season",
                "ai_pubdate",
                null,
                publication.Result,
                publication.ErrorCode,
                publication.Retryable,
                publicationStarted,
                cancellationToken).ConfigureAwait(false);
        }

        var input = new AiMetadataMatchInput(
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
            publication.UseBangumiPubDateFirst);
        AiMetadataMatchCandidate candidate;
        try
        {
            candidate = await aiMatcher.MatchAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (AiMetadataMatcherException exception)
        {
            await RecordAsync(
                claim,
                "season",
                "ai_season",
                null,
                "error",
                exception.SafeCode,
                IsRetryable(exception.Kind),
                started,
                cancellationToken).ConfigureAwait(false);
            return false;
        }

        var validated = await aiValidator.ValidateAsync(
            input,
            candidate,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!validated.IsSuccess)
        {
            var failure = validated.Failure!;
            await RecordAsync(
                claim,
                "season",
                "ai_season",
                null,
                failure.Kind == MetadataFailureKind.SemanticNoMatch ? "not_matched" : "error",
                failure.Code,
                IsRetryable(failure.Kind),
                started,
                cancellationToken).ConfigureAwait(false);
            return false;
        }

        var validatedFiles = validated.Value!.Files;
        var seasons = validatedFiles
            .GroupBy(file => file.Season.SeasonNumber)
            .Select(group => group.First().Season)
            .OrderBy(season => season.SeasonNumber)
            .ToArray();
        MetadataSeasonFileSeed[] seeds;
        if (seasons.Length == 1)
        {
            seeds = validatedFiles
                .Select(file => new MetadataSeasonFileSeed(
                    file.Input.Name,
                    file.Episode?.EpisodeNumber,
                    file.Episode is null ? "ai_episode_unmatched" : null,
                    file.Season.SeasonNumber))
                .ToArray();
        }
        else
        {
            var files = claim.Files ?? [];
            var validatedByPath = validatedFiles.ToDictionary(
                file => file.Input.Name,
                StringComparer.Ordinal);
            var seedsByFileId = new Dictionary<string, MetadataSeasonFileSeed>(StringComparer.Ordinal);
            foreach (var file in files.Where(file => SubtitleAssociationResolver.IsVideo(file.RelativePath)))
            {
                var aiFile = validatedByPath[file.RelativePath];
                seedsByFileId.Add(
                    file.FileId,
                    new MetadataSeasonFileSeed(
                        file.RelativePath,
                        aiFile.Episode?.EpisodeNumber,
                        aiFile.Episode is null ? "ai_episode_unmatched" : null,
                        aiFile.Season.SeasonNumber));
            }

            var associations = SubtitleAssociationResolver.Resolve(files.Select(file => new TorrentMediaFile(
                file.FileId,
                file.RelativePath,
                int.TryParse(
                    file.FileEpisodeCandidate,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var episode)
                    ? episode
                    : null)).ToArray());
            foreach (var association in associations)
            {
                if (association.VideoFileId is null
                    || !seedsByFileId.TryGetValue(association.VideoFileId, out var videoSeed))
                {
                    continue;
                }

                var subtitle = files.Single(file => file.FileId == association.SubtitleFileId);
                seedsByFileId.Add(
                    subtitle.FileId,
                    new MetadataSeasonFileSeed(
                        subtitle.RelativePath,
                        videoSeed.EpisodeNumber,
                        videoSeed.OtherReason,
                        videoSeed.SeasonNumber));
            }

            if (seedsByFileId.Count != files.Count)
            {
                await RecordAsync(
                    claim,
                    "season",
                    "ai_season",
                    null,
                    "error",
                    "ai_cross_season_file_unassigned",
                    false,
                    started,
                    cancellationToken).ConfigureAwait(false);
                return false;
            }

            seeds = files.Select(file => seedsByFileId[file.FileId]).ToArray();
        }

        await RecordAsync(
            claim,
            "season",
            "ai_season",
            null,
            "matched",
            null,
            false,
            started,
            cancellationToken).ConfigureAwait(false);
        await resolutions.CompleteAiSeasonsAsync(
            claim,
            validated.Value.Series,
            seasons,
            seeds,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task CompleteSeasonAsync(
        MetadataTaskClaim claim,
        TmdbSeries series,
        TmdbSeason season,
        string strategy,
        int? priority,
        long started,
        CancellationToken cancellationToken)
    {
        await RecordAsync(claim, "season", strategy, priority, "matched", null,
            false, started, cancellationToken).ConfigureAwait(false);
        await resolutions.CompleteSeasonAsync(
            claim, series, season, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordAsync(
        MetadataTaskClaim claim,
        string stage,
        string strategy,
        int? priority,
        string result,
        string? errorCode,
        bool retryable,
        long started,
        CancellationToken cancellationToken) =>
        await resolutions.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                stage,
                strategy,
                priority,
                result,
                errorCode,
                retryable,
                claim.AttemptNumber,
                ElapsedMilliseconds(started)),
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);

    private Task FailAsync(
        MetadataTaskClaim claim,
        MetadataFailure failure,
        string denialReason,
        CancellationToken cancellationToken) =>
        resolutions.FailAsync(
            claim,
            failure,
            fallbackEligible: false,
            denialReason,
            _timeProvider.GetUtcNow(),
            cancellationToken);

    private string SeriesFailureDenialReason(MetadataTaskClaim claim, MetadataFailure failure)
    {
        if (failure.Kind != MetadataFailureKind.SemanticNoMatch || !failure.TmdbAccessConfirmed)
        {
            return "tmdb_access_not_confirmed";
        }

        if (claim.BangumiSubjectId is null)
        {
            return "bangumi_subject_missing";
        }

        return options.Metadata.TmdbFailureUseBangumi
            ? "bangumi_fallback_pending"
            : "bangumi_fallback_disabled";
    }

    private long ElapsedMilliseconds(long startedTimestamp) =>
        Math.Max(0, (long)_timeProvider.GetElapsedTime(startedTimestamp).TotalMilliseconds);

    private static MetadataFailure ToFailure(TmdbClientException exception) =>
        new(exception.Kind, exception.SafeCode, exception.TmdbAccessConfirmed);

    private static bool IsRetryable(MetadataFailureKind kind) =>
        kind is MetadataFailureKind.Network or MetadataFailureKind.RemoteService;
}
