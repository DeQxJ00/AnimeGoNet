using System.Globalization;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Mikan;

namespace AnimeGoNet.App.Metadata;

public sealed class EpisodeMetadataResolutionProcessor(
    MetadataResolutionStore resolutions,
    MikanWorkMetadataRuleStore rules,
    ITmdbClient tmdb,
    IAiMetadataMatcher aiMatcher,
    AiMetadataResultValidator aiValidator,
    AnimeGoOptions options,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var claim = await resolutions.TryClaimNextSeasonResolvedAsync(
            _timeProvider.GetUtcNow(),
            LeaseDuration,
            cancellationToken).ConfigureAwait(false);
        if (claim is null)
        {
            return false;
        }

        var rule = claim.Resolution.MikanId is null
            ? null
            : await rules.GetEnabledAsync(claim.Resolution.MikanId.Value, cancellationToken).ConfigureAwait(false);
        var manualOffset = rule?.TmdbSeriesId == claim.TmdbSeriesId
            && rule.TmdbSeasonNumber == claim.TmdbSeasonNumber
            ? rule.EpisodeOffset
            : null;
        var associations = SubtitleAssociationResolver.Resolve(claim.Files.Select(file => new TorrentMediaFile(
            file.FileId,
            file.RelativePath,
            int.TryParse(file.FileEpisodeCandidate, NumberStyles.None, CultureInfo.InvariantCulture, out var episode)
                ? episode
                : null)).ToArray());
        var subtitleIds = associations.Select(association => association.SubtitleFileId).ToHashSet(StringComparer.Ordinal);
        var results = new List<MetadataEpisodeFileResolution>(claim.Files.Count);
        foreach (var file in claim.Files.Where(file => !subtitleIds.Contains(file.FileId)))
        {
            if (!int.TryParse(file.FileEpisodeCandidate, NumberStyles.None, CultureInfo.InvariantCulture, out var sourceEpisode)
                || sourceEpisode <= 0)
            {
                var reason = OtherReason(file);
                await RecordAsync(claim, manualOffset is null ? "tmdb_episode_number" : "manual_mikan_offset",
                    manualOffset is null ? null : ManualMetadataResolutionProcessor.ManualOverridePriority,
                    "other", reason, retryable: false, 0, cancellationToken).ConfigureAwait(false);
                results.Add(new MetadataEpisodeFileResolution(file.FileId, null, "other", reason));
                continue;
            }

            var targetEpisode = sourceEpisode + (manualOffset ?? 0);
            if (targetEpisode <= 0)
            {
                var failure = new MetadataFailure(
                    MetadataFailureKind.InvalidInput,
                    "manual_episode_offset_invalid",
                    TmdbAccessConfirmed: false);
                await RecordFailureAndStopAsync(
                    claim,
                    "manual_mikan_offset",
                    ManualMetadataResolutionProcessor.ManualOverridePriority,
                    failure,
                    0,
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            var started = _timeProvider.GetTimestamp();
            TmdbEpisode? episode;
            try
            {
                episode = await tmdb.GetEpisodeAsync(
                    claim.TmdbSeriesId,
                    claim.TmdbSeasonNumber,
                    targetEpisode,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TmdbClientException exception)
            {
                await RecordFailureAndStopAsync(
                    claim,
                    manualOffset is null ? "tmdb_episode_number" : "manual_mikan_offset",
                    manualOffset is null ? null : ManualMetadataResolutionProcessor.ManualOverridePriority,
                    new MetadataFailure(exception.Kind, exception.SafeCode, exception.TmdbAccessConfirmed),
                    ElapsedMilliseconds(started),
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (episode is null)
            {
                if (manualOffset is not null)
                {
                    await RecordFailureAndStopAsync(
                        claim,
                        "manual_mikan_offset",
                        ManualMetadataResolutionProcessor.ManualOverridePriority,
                        new MetadataFailure(
                            MetadataFailureKind.SemanticNoMatch,
                            "manual_tmdb_episode_not_found",
                            TmdbAccessConfirmed: true),
                        ElapsedMilliseconds(started),
                        cancellationToken).ConfigureAwait(false);
                    return true;
                }

                const string reason = "tmdb_episode_not_found";
                await RecordAsync(claim, "tmdb_episode_number", null, "other", reason,
                    false, ElapsedMilliseconds(started), cancellationToken).ConfigureAwait(false);
                results.Add(new MetadataEpisodeFileResolution(file.FileId, null, "other", reason));
                continue;
            }

            if (episode.SeriesId != claim.TmdbSeriesId
                || episode.SeasonNumber != claim.TmdbSeasonNumber
                || episode.EpisodeNumber != targetEpisode)
            {
                await RecordFailureAndStopAsync(
                    claim,
                    manualOffset is null ? "tmdb_episode_number" : "manual_mikan_offset",
                    manualOffset is null ? null : ManualMetadataResolutionProcessor.ManualOverridePriority,
                    new MetadataFailure(
                        MetadataFailureKind.Protocol,
                        "tmdb_episode_identity_mismatch",
                        TmdbAccessConfirmed: false),
                    ElapsedMilliseconds(started),
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            await RecordAsync(
                claim,
                manualOffset is null ? "tmdb_episode_number" : "manual_mikan_offset",
                manualOffset is null ? null : ManualMetadataResolutionProcessor.ManualOverridePriority,
                "matched",
                null,
                false,
                ElapsedMilliseconds(started),
                cancellationToken).ConfigureAwait(false);
            results.Add(new MetadataEpisodeFileResolution(file.FileId, episode, "episode", null));
        }

        if (manualOffset is null
            && options.Metadata.Ai.UseEpisodeMatch
            && results.Any(result => result.Episode is null
                && claim.Files.Any(file => file.FileId == result.FileId
                    && SubtitleAssociationResolver.IsVideo(file.RelativePath))))
        {
            var shouldStop = await TryApplyAiAsync(
                claim,
                results,
                cancellationToken).ConfigureAwait(false);
            if (shouldStop)
            {
                return true;
            }
        }

        foreach (var association in associations)
        {
            var video = association.VideoFileId is null
                ? null
                : results.SingleOrDefault(result => result.FileId == association.VideoFileId);
            if (video?.Episode is not null)
            {
                await RecordAsync(
                    claim, "subtitle_association", null, "matched", null,
                    false, 0, cancellationToken).ConfigureAwait(false);
                results.Add(new MetadataEpisodeFileResolution(
                    association.SubtitleFileId,
                    video.Episode,
                    "episode",
                    null,
                    association.VideoFileId,
                    association.RenameSuffix));
            }
            else
            {
                var reason = association.UnmatchedReason ?? "subtitle_video_unmatched";
                await RecordAsync(
                    claim, "subtitle_association", null, "other", reason,
                    false, 0, cancellationToken).ConfigureAwait(false);
                results.Add(new MetadataEpisodeFileResolution(
                    association.SubtitleFileId,
                    null,
                    "other",
                    reason,
                    association.VideoFileId,
                    association.RenameSuffix));
            }
        }

        await resolutions.CompleteEpisodesAsync(
            claim,
            results,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TryApplyAiAsync(
        MetadataEpisodeTaskClaim claim,
        List<MetadataEpisodeFileResolution> results,
        CancellationToken cancellationToken)
    {
        var videos = claim.Files
            .Where(file => SubtitleAssociationResolver.IsVideo(file.RelativePath))
            .ToArray();
        if (videos.Length == 0)
        {
            return false;
        }

        var input = new AiMetadataMatchInput(
            claim.Resolution.Title,
            videos.Select(file => new AiMetadataFileInput(
                file.RelativePath,
                file.SizeBytes)).ToArray(),
            claim.Resolution.BangumiSubjectId,
            claim.Resolution.AniDbAnimeId,
            claim.Resolution.ImdbTitleId,
            claim.Files.Count,
            PublishedAt: null,
            BangumiEpisodeCandidate: null,
            UseBangumiPubDateFirst: false);
        var started = _timeProvider.GetTimestamp();
        AiMetadataMatchCandidate candidate;
        try
        {
            candidate = await aiMatcher.MatchAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (AiMetadataMatcherException exception)
        {
            var failure = new MetadataFailure(exception.Kind, exception.SafeCode, false);
            return await HandleAiFailureAsync(
                claim,
                results,
                failure,
                started,
                cancellationToken).ConfigureAwait(false);
        }

        var validated = await aiValidator.ValidateAsync(
            input,
            candidate,
            claim.TmdbSeriesId,
            claim.TmdbSeasonNumber,
            cancellationToken).ConfigureAwait(false);
        if (!validated.IsSuccess)
        {
            return await HandleAiFailureAsync(
                claim,
                results,
                validated.Failure!,
                started,
                cancellationToken).ConfigureAwait(false);
        }

        var validatedByPath = validated.Value!.Files.ToDictionary(
            file => file.Input.Name,
            StringComparer.Ordinal);
        foreach (var existing in results.Where(result => result.Episode is not null))
        {
            var path = claim.Files.Single(file => file.FileId == existing.FileId).RelativePath;
            var aiFile = validatedByPath[path];
            if (aiFile.Episode is null
                || aiFile.Episode.EpisodeNumber != existing.Episode!.EpisodeNumber)
            {
                var failure = new MetadataFailure(
                    MetadataFailureKind.Protocol,
                    "ai_confirmed_episode_changed",
                    TmdbAccessConfirmed: true);
                await RecordAsync(
                    claim,
                    "ai_episode",
                    null,
                    "error",
                    failure.Code,
                    false,
                    ElapsedMilliseconds(started),
                    cancellationToken).ConfigureAwait(false);
                AnnotateUnresolvedAiFailure(claim, results, failure.Code);
                return false;
            }
        }

        for (var index = 0; index < results.Count; index++)
        {
            var existing = results[index];
            if (existing.Episode is not null)
            {
                continue;
            }

            var file = claim.Files.Single(candidateFile => candidateFile.FileId == existing.FileId);
            if (!SubtitleAssociationResolver.IsVideo(file.RelativePath))
            {
                continue;
            }

            var aiFile = validatedByPath[file.RelativePath];
            results[index] = aiFile.Episode is null
                ? existing with
                {
                    OtherReason = aiFile.OtherReason ?? existing.OtherReason,
                }
                : new MetadataEpisodeFileResolution(
                    existing.FileId,
                    aiFile.Episode,
                    "episode",
                    null);
        }

        await RecordAsync(
            claim,
            "ai_episode",
            null,
            "matched",
            null,
            false,
            ElapsedMilliseconds(started),
            cancellationToken).ConfigureAwait(false);
        return false;
    }

    private async Task<bool> HandleAiFailureAsync(
        MetadataEpisodeTaskClaim claim,
        List<MetadataEpisodeFileResolution> results,
        MetadataFailure failure,
        long started,
        CancellationToken cancellationToken)
    {
        if (IsRetryable(failure.Kind))
        {
            await RecordFailureAndStopAsync(
                claim,
                "ai_episode",
                null,
                failure,
                ElapsedMilliseconds(started),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        AnnotateUnresolvedAiFailure(claim, results, failure.Code);
        await RecordAsync(
            claim,
            "ai_episode",
            null,
            failure.Kind == MetadataFailureKind.SemanticNoMatch ? "not_matched" : "error",
            failure.Code,
            false,
            ElapsedMilliseconds(started),
            cancellationToken).ConfigureAwait(false);
        return false;
    }

    private static void AnnotateUnresolvedAiFailure(
        MetadataEpisodeTaskClaim claim,
        List<MetadataEpisodeFileResolution> results,
        string code)
    {
        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            if (result.Episode is not null)
            {
                continue;
            }

            var file = claim.Files.Single(candidate => candidate.FileId == result.FileId);
            if (SubtitleAssociationResolver.IsVideo(file.RelativePath))
            {
                results[index] = result with { OtherReason = code };
            }
        }
    }

    private async Task RecordFailureAndStopAsync(
        MetadataEpisodeTaskClaim claim,
        string strategy,
        int? priority,
        MetadataFailure failure,
        long durationMilliseconds,
        CancellationToken cancellationToken)
    {
        await RecordAsync(
            claim,
            strategy,
            priority,
            "failed",
            failure.Code,
            IsRetryable(failure.Kind),
            durationMilliseconds,
            cancellationToken).ConfigureAwait(false);
        await resolutions.FailEpisodesAsync(
            claim,
            failure,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
    }

    private Task RecordAsync(
        MetadataEpisodeTaskClaim claim,
        string strategy,
        int? priority,
        string result,
        string? errorCode,
        bool retryable,
        long durationMilliseconds,
        CancellationToken cancellationToken) =>
        resolutions.RecordAttemptAsync(
            claim.Resolution,
            new MetadataAttempt(
                "episode",
                strategy,
                priority,
                result,
                errorCode,
                retryable,
                claim.Resolution.AttemptNumber,
                durationMilliseconds),
            _timeProvider.GetUtcNow(),
            cancellationToken);

    private static string OtherReason(MetadataTaskFileProjection file)
    {
        if (file.SourceEpisode?.Contains('.', StringComparison.Ordinal) == true)
        {
            return "fractional_episode";
        }

        return file.SourceEpisode is null ? "episode_not_parsed" : "special_episode";
    }

    private long ElapsedMilliseconds(long startedTimestamp) =>
        Math.Max(0, (long)_timeProvider.GetElapsedTime(startedTimestamp).TotalMilliseconds);

    private static bool IsRetryable(MetadataFailureKind kind) =>
        kind is MetadataFailureKind.Network or MetadataFailureKind.RemoteService;
}
