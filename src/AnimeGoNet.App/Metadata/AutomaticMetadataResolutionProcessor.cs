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
    TmdbSeriesSeasonResolver seriesSeasonResolver,
    AiMetadataTaskResolver aiMetadata,
    MikanWorkMetadataRuleStore rules,
    MikanTrustedOffsetStore trustedOffsets,
    AnimeGoOptions options,
    TimeProvider? timeProvider = null,
    IBangumiEpisodeClient? bangumiEpisodes = null,
    MetadataRefreshScope? refreshScope = null)
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
        using var refresh = refreshScope?.Begin(claim.IsForcedReadaptation);

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
        IReadOnlyList<string> titles = [claim.Title];
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

            titles = TmdbSeriesSeasonResolver.BangumiTitles(subject);
            if (titles.Count == 0)
            {
                titles = [claim.Title];
            }
            await RecordAsync(claim, "bangumi", "bangumi_subject", null, "matched", null,
                false, started, cancellationToken).ConfigureAwait(false);
        }

        var seriesStarted = _timeProvider.GetTimestamp();
        var direct = await seriesSeasonResolver.ResolveAsync(
            titles,
            subject?.AirDate,
            cancellationToken).ConfigureAwait(false);
        if (direct.IsSuccess)
        {
            await RecordAsync(claim, "series", "tmdb_title", null, "matched", null,
                false, seriesStarted, cancellationToken).ConfigureAwait(false);
            await CompleteSeasonAsync(
                claim,
                direct.Details!.Series,
                direct.Season!,
                "tmdb_air_date",
                null,
                seriesStarted,
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        var directFailure = direct.Failure!;
        await RecordAsync(
            claim,
            "series",
            "tmdb_title",
            null,
            direct.HasValidatedSeries ? "matched" : "not_matched",
            direct.HasValidatedSeries ? null : directFailure.Code,
            IsRetryable(directFailure.Kind),
            seriesStarted,
            cancellationToken).ConfigureAwait(false);
        await RecordAsync(claim, "season", "tmdb_air_date", null, "not_matched", directFailure.Code,
            IsRetryable(directFailure.Kind), seriesStarted, cancellationToken).ConfigureAwait(false);
        if (directFailure.Kind != MetadataFailureKind.SemanticNoMatch)
        {
            await FailAsync(claim, directFailure, SeriesFailureDenialReason(claim, directFailure), cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        var details = direct.Details;
        var terminalFailure = directFailure;
        var attemptedTmdbSearchTitles = new List<string>(direct.AttemptedTitles);
        var policy = options.Metadata.SeasonFailure;
        if (policy.Skip)
        {
            var skipFailure = new MetadataFailure(MetadataFailureKind.SemanticNoMatch, "tmdb_season_skipped", true);
            await RecordAsync(claim, "season", "skip", 4, "skipped", skipFailure.Code,
                false, _timeProvider.GetTimestamp(), cancellationToken).ConfigureAwait(false);
            await FailAsync(claim, skipFailure,
                details is null ? "tmdb_series_not_resolved" : "tmdb_series_resolved", cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        if (policy.Backtrace && claim.BangumiSubjectId is not null)
        {
            var started = _timeProvider.GetTimestamp();
            try
            {
                var result = await backtrace.ResolveAsync(
                    claim.BangumiSubjectId.Value,
                    cancellationToken).ConfigureAwait(false);
                attemptedTmdbSearchTitles.AddRange(result.AttemptedTitles);
                await RecordAsync(claim, "season", "backtrace", 3,
                    result.IsSuccess
                        ? "matched"
                        : result.Failure!.Kind == MetadataFailureKind.SemanticNoMatch ? "not_matched" : "error",
                    result.Failure?.Code,
                    result.Failure is not null && IsRetryable(result.Failure.Kind),
                    started, cancellationToken).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    await RecordAsync(
                        claim,
                        "series",
                        "backtrace",
                        3,
                        "matched",
                        null,
                        false,
                        started,
                        cancellationToken).ConfigureAwait(false);
                    await resolutions.CompleteSeasonAsync(
                        claim, result.Details!.Series, result.Season!, _timeProvider.GetUtcNow(), cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }

                if (result.Failure!.Kind != MetadataFailureKind.SemanticNoMatch)
                {
                    terminalFailure = result.Failure;
                }
            }
            catch (BangumiClientException exception)
            {
                terminalFailure = new MetadataFailure(exception.Kind, exception.SafeCode, false);
                await RecordAsync(claim, "season", "backtrace", 3, "error", exception.SafeCode,
                    IsRetryable(exception.Kind), started, cancellationToken).ConfigureAwait(false);
            }
        }

        if (options.Metadata.Ai.UseMetadataMatch)
        {
            if (await TryCompleteAiMetadataAsync(
                    claim,
                    attemptedTmdbSearchTitles,
                    cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        if (policy.UseTitleSeason)
        {
            var started = _timeProvider.GetTimestamp();
            if (details is null)
            {
                await RecordAsync(claim, "season", "title_season", 2, "not_applicable",
                    "title_season_tmdb_series_required", false, started, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
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
        }

        if (policy.UseFirstSeason)
        {
            var started = _timeProvider.GetTimestamp();
            if (details is null)
            {
                await RecordAsync(claim, "season", "first_season", 1, "not_applicable",
                    "first_season_tmdb_series_required", false, started, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
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
        }

        if (details is null
            && await TryCompleteBangumiFallbackAsync(
                claim,
                subject,
                terminalFailure,
                cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        await FailAsync(
            claim,
            terminalFailure,
            details is null ? SeriesFailureDenialReason(claim, terminalFailure) : "tmdb_series_resolved",
            cancellationToken).ConfigureAwait(false);
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

        var started = _timeProvider.GetTimestamp();
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
            1,
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
        if (claim.IsForcedReadaptation
            || !options.Metadata.MikanTrustedOffsetCacheEnabled
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
            options.Metadata.MikanTrustedOffsetRequiredEpisodes,
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
            claim, "series", "trusted_mikan_offset", null, "matched",
            null, false, started, cancellationToken).ConfigureAwait(false);
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

    private async Task<bool> TryCompleteAiMetadataAsync(
        MetadataTaskClaim claim,
        IReadOnlyList<string> attemptedTmdbSearchTitles,
        CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var resolved = await aiMetadata.ResolveAsync(
            claim,
            claim.Files ?? [],
            preAiSearchTitles: attemptedTmdbSearchTitles,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (resolved.Publication?.ShouldAudit == true)
        {
            await RecordAsync(
                claim,
                "season",
                "ai_pubdate",
                null,
                resolved.Publication.Result,
                resolved.Publication.ErrorCode,
                resolved.Publication.Retryable,
                started,
                cancellationToken).ConfigureAwait(false);
        }

        if (!resolved.IsApplicable)
        {
            await RecordAsync(
                claim,
                "season",
                "ai_metadata",
                null,
                "not_applicable",
                resolved.Failure!.Code,
                false,
                started,
                cancellationToken,
                resolved.Usage).ConfigureAwait(false);
            return false;
        }

        if (!resolved.IsSuccess)
        {
            var failure = resolved.Failure!;
            await RecordAsync(
                claim,
                "season",
                "ai_metadata",
                null,
                failure.Kind == MetadataFailureKind.SemanticNoMatch ? "not_matched" : "error",
                failure.Code,
                IsRetryable(failure.Kind),
                started,
                cancellationToken,
                resolved.Usage).ConfigureAwait(false);
            return false;
        }

        var validatedFiles = resolved.Value!.Files;
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
                    "ai_metadata",
                    null,
                    "error",
                    "ai_cross_season_file_unassigned",
                    false,
                    started,
                    cancellationToken,
                    resolved.Usage).ConfigureAwait(false);
                return false;
            }

            seeds = files.Select(file => seedsByFileId[file.FileId]).ToArray();
        }

        await RecordAsync(
            claim,
            "series",
            "ai_metadata",
            null,
            "matched",
            null,
            false,
            started,
            cancellationToken,
            resolved.Usage).ConfigureAwait(false);
        await RecordAsync(
            claim,
            "season",
            "ai_metadata",
            null,
            "matched",
            null,
            false,
            started,
            cancellationToken).ConfigureAwait(false);
        await resolutions.CompleteAiSeasonsAsync(
            claim,
            resolved.Value.Series,
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
        CancellationToken cancellationToken,
        AiMetadataProviderUsage? aiUsage = null) =>
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
                ElapsedMilliseconds(started),
                AiUsage: aiUsage),
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
