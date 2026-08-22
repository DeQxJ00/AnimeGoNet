using System.Globalization;
using AnimeGoNet.App.Ingest;
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
    AiMetadataTaskResolver aiMetadata,
    MikanTrustedOffsetStore trustedOffsets,
    DuplicateHitNotifier duplicateNotifier,
    AnimeGoOptions options,
    IBangumiSubjectClient bangumiSubjects,
    IBangumiEpisodeClient? bangumiEpisodes = null,
    TimeProvider? timeProvider = null,
    MetadataRefreshScope? refreshScope = null)
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
        using var refresh = refreshScope?.Begin(claim.IsOtherReadaptation);

        var rule = claim.Resolution.MikanId is null
            ? null
            : await rules.GetEnabledAsync(claim.Resolution.MikanId.Value, cancellationToken).ConfigureAwait(false);
        if (rule?.BangumiSubjectId is not null)
        {
            claim = claim with
            {
                Resolution = claim.Resolution with
                {
                    BangumiSubjectId = rule.BangumiSubjectId,
                },
            };
        }

        var manualOffset = !claim.HasMultipleSeasons
            && rule?.TmdbSeriesId == claim.TmdbSeriesId
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
        EpisodeDateContext? episodeDateContext = null;
        if (manualOffset is null
            && !claim.HasMultipleSeasons
            && !claim.EpisodeResolvedByTrustedOffset
            && string.Equals(
                claim.Resolution.SourceAdapter,
                "mikan",
                StringComparison.OrdinalIgnoreCase)
            && claim.Resolution.BangumiSubjectId is > 0
            && bangumiEpisodes is not null
            && claim.Files.Any(file => int.TryParse(
                file.FileEpisodeCandidate,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var candidate) && candidate > 0))
        {
            var started = _timeProvider.GetTimestamp();
            try
            {
                var sourceCandidates = claim.Files
                    .Select(file => int.TryParse(
                        file.FileEpisodeCandidate,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var candidate)
                            ? candidate
                            : 0)
                    .Where(candidate => candidate > 0)
                    .Distinct()
                    .ToArray();
                var bangumiValues = await GetBangumiEpisodesAsync(
                    bangumiEpisodes,
                    claim.Resolution.BangumiSubjectId.Value,
                    sourceCandidates,
                    refreshScope?.BypassCaches == true,
                    cancellationToken).ConfigureAwait(false);
                if (sourceCandidates.Any(candidate =>
                        !HasBangumiEpisodeIdentity(bangumiValues, candidate)))
                {
                    var relations = await bangumiSubjects.GetRelatedSubjectsAsync(
                        claim.Resolution.BangumiSubjectId.Value,
                        cancellationToken).ConfigureAwait(false);
                    foreach (var sequel in relations
                                 .Where(value => value.Type == 2
                                     && string.Equals(
                                         value.Relation,
                                         "续集",
                                         StringComparison.Ordinal))
                                 .OrderBy(value => value.Id)
                                 .Take(8))
                    {
                        var sequelEpisodes = await GetBangumiEpisodesAsync(
                            bangumiEpisodes,
                            sequel.Id,
                            sourceCandidates,
                            refreshScope?.BypassCaches == true,
                            cancellationToken).ConfigureAwait(false);
                        bangumiValues = bangumiValues
                            .Concat(sequelEpisodes)
                            .GroupBy(value => value.Id)
                            .Select(group => group.First())
                            .ToArray();
                    }
                }
                var tmdbSeason = await tmdb.GetSeasonAsync(
                    claim.TmdbSeriesId,
                    claim.TmdbSeasonNumber,
                    cancellationToken).ConfigureAwait(false);
                episodeDateContext = new EpisodeDateContext(
                    bangumiValues,
                    tmdbSeason?.Episodes ?? []);
            }
            catch (BangumiClientException exception)
            {
                await RecordFailureAndStopAsync(
                    claim,
                    "tmdb_episode_bangumi_date",
                    null,
                    new MetadataFailure(
                        exception.Kind,
                        exception.SafeCode,
                        TmdbAccessConfirmed: false),
                    ElapsedMilliseconds(started),
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (TmdbClientException exception)
            {
                await RecordFailureAndStopAsync(
                    claim,
                    "tmdb_episode_bangumi_date",
                    null,
                    new MetadataFailure(
                        exception.Kind,
                        exception.SafeCode,
                        exception.TmdbAccessConfirmed),
                    ElapsedMilliseconds(started),
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        var results = new List<MetadataEpisodeFileResolution>(claim.Files.Count);
        foreach (var file in claim.Files.Where(file => !subtitleIds.Contains(file.FileId)))
        {
            var targetSeasonNumber = file.TmdbSeasonNumber ?? claim.TmdbSeasonNumber;
            if (manualOffset is null && file.PreResolvedOtherReason is not null)
            {
                await RecordAsync(
                    claim,
                    claim.EpisodeResolvedByTrustedOffset ? "trusted_mikan_offset" : "ai_metadata",
                    null,
                    "other",
                    file.PreResolvedOtherReason,
                    retryable: false,
                    0,
                    cancellationToken).ConfigureAwait(false);
                results.Add(new MetadataEpisodeFileResolution(
                    file.FileId,
                    null,
                    "other",
                    file.PreResolvedOtherReason));
                continue;
            }

            string strategy = manualOffset is not null
                ? "manual_mikan_offset"
                : claim.EpisodeResolvedByTrustedOffset
                    ? "trusted_mikan_offset"
                : file.PreResolvedEpisodeNumber is > 0
                    ? "ai_metadata"
                    : "tmdb_episode_number";
            int? priority = manualOffset is null
                ? null
                : ManualMetadataResolutionProcessor.ManualOverridePriority;
            int? parsedSourceEpisode = int.TryParse(
                file.FileEpisodeCandidate,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var sourceEpisodeValue)
                && sourceEpisodeValue > 0
                    ? sourceEpisodeValue
                    : null;
            int targetEpisode;
            if (manualOffset is null && file.PreResolvedEpisodeNumber is > 0)
            {
                targetEpisode = file.PreResolvedEpisodeNumber.Value;
            }
            else if (parsedSourceEpisode is null)
            {
                var reason = OtherReason(file);
                await RecordAsync(
                    claim,
                    strategy,
                    priority,
                    "other", reason, retryable: false, 0, cancellationToken).ConfigureAwait(false);
                results.Add(new MetadataEpisodeFileResolution(file.FileId, null, "other", reason));
                continue;
            }
            else
            {
                targetEpisode = parsedSourceEpisode.Value + (manualOffset ?? 0);
            }

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

            if (manualOffset is null
                && file.PreResolvedEpisodeNumber is null
                && episodeDateContext is not null
                && parsedSourceEpisode is > 0)
            {
                var dateMatch = BangumiTmdbEpisodeDateResolver.Resolve(
                    episodeDateContext.BangumiEpisodes,
                    episodeDateContext.TmdbEpisodes,
                    parsedSourceEpisode.Value,
                    allowFilenameNearestFallback: claim.Resolution.TorrentFileCount == 1);
                strategy = dateMatch.EvidenceKind == TmdbEpisodeDateEvidenceKind.FilenameEpisodeNearestDate
                    ? "tmdb_episode_bangumi_nearest_date"
                    : "tmdb_episode_bangumi_date";
                if (!dateMatch.IsSuccess)
                {
                    var reason = dateMatch.FailureCode
                        ?? "tmdb_episode_bangumi_date_not_applicable";
                    await RecordAsync(
                        claim,
                        strategy,
                        null,
                        "other",
                        reason,
                        retryable: false,
                        0,
                        cancellationToken).ConfigureAwait(false);
                    results.Add(new MetadataEpisodeFileResolution(
                        file.FileId,
                        null,
                        "other",
                        reason));
                    continue;
                }

                targetEpisode = dateMatch.Episode!.EpisodeNumber;
            }

            if (claim.EpisodeResolvedByTrustedOffset)
            {
                var attemptId = await RecordAsync(
                    claim,
                    strategy,
                    priority,
                    "matched",
                    null,
                    false,
                    0,
                    cancellationToken).ConfigureAwait(false);
                results.Add(new MetadataEpisodeFileResolution(
                    file.FileId,
                    null,
                    "episode",
                    null,
                    TrustedEpisodeNumber: targetEpisode,
                    ResolutionSource:
                        TmdbResolutionSource.TrustedMikanOffset,
                    ResolutionAttemptId: attemptId));
                continue;
            }

            var started = _timeProvider.GetTimestamp();
            TmdbEpisode? episode;
            try
            {
                episode = await tmdb.GetEpisodeAsync(
                    claim.TmdbSeriesId,
                    targetSeasonNumber,
                    targetEpisode,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TmdbClientException exception)
            {
                await RecordFailureAndStopAsync(
                    claim,
                    strategy,
                    priority,
                    new MetadataFailure(exception.Kind, exception.SafeCode, exception.TmdbAccessConfirmed),
                    ElapsedMilliseconds(started),
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (episode is null)
            {
                if (manualOffset is not null || strategy == "ai_metadata")
                {
                    await RecordFailureAndStopAsync(
                        claim,
                        strategy,
                        priority,
                        new MetadataFailure(
                            MetadataFailureKind.SemanticNoMatch,
                            strategy == "ai_metadata"
                                ? "ai_tmdb_episode_not_found"
                                : "manual_tmdb_episode_not_found",
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
                || episode.SeasonNumber != targetSeasonNumber
                || episode.EpisodeNumber != targetEpisode)
            {
                await RecordFailureAndStopAsync(
                    claim,
                    strategy,
                    priority,
                    new MetadataFailure(
                        MetadataFailureKind.Protocol,
                        "tmdb_episode_identity_mismatch",
                        TmdbAccessConfirmed: false),
                    ElapsedMilliseconds(started),
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            var matchedAttemptId = await RecordAsync(
                claim,
                strategy,
                priority,
                "matched",
                null,
                false,
                ElapsedMilliseconds(started),
                cancellationToken).ConfigureAwait(false);
            results.Add(new MetadataEpisodeFileResolution(
                file.FileId,
                episode,
                "episode",
                null,
                ResolutionSource:
                    strategy.ParseTmdbResolutionSource(),
                ResolutionAttemptId: matchedAttemptId));
        }

        if (manualOffset is null
            && options.Metadata.Ai.UseMetadataMatch
            && !claim.AiMetadataAttempted
            && !claim.HasMultipleSeasons
            && !claim.EpisodeResolvedByTrustedOffset
            && results.Any(result => result.Episode is null
                && claim.Files.Any(file => file.FileId == result.FileId
                    && SubtitleAssociationResolver.IsVideo(file.RelativePath))))
        {
            var aiTriggerReason = BuildAiEpisodeTriggerReason(claim, results);
            var shouldStop = await TryApplyAiAsync(
                claim,
                results,
                aiTriggerReason,
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
            if (video?.ResolvedEpisodeNumber is > 0)
            {
                var attemptId = await RecordAsync(
                    claim, "subtitle_association", null, "matched", null,
                    false, 0, cancellationToken).ConfigureAwait(false);
                results.Add(new MetadataEpisodeFileResolution(
                    association.SubtitleFileId,
                    video.Episode,
                    "episode",
                    null,
                    association.VideoFileId,
                    association.RenameSuffix,
                    video.TrustedEpisodeNumber,
                    TmdbResolutionSource.SubtitleAssociation,
                    attemptId));
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

        var validatedSeasons = new List<TmdbSeason>();
        foreach (var seasonNumber in claim.Files
                     .Select(file => file.TmdbSeasonNumber ?? claim.TmdbSeasonNumber)
                     .Distinct()
                     .OrderBy(value => value))
        {
            var started = _timeProvider.GetTimestamp();
            TmdbSeason? season;
            try
            {
                season = await tmdb.GetSeasonAsync(
                    claim.TmdbSeriesId,
                    seasonNumber,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TmdbClientException exception)
            {
                await RecordFailureAndStopAsync(
                    claim,
                    "tmdb_episode_snapshot",
                    null,
                    new MetadataFailure(
                        exception.Kind,
                        exception.SafeCode,
                        exception.TmdbAccessConfirmed),
                    ElapsedMilliseconds(started),
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (season is null)
            {
                await RecordFailureAndStopAsync(
                    claim,
                    "tmdb_episode_snapshot",
                    null,
                    new MetadataFailure(
                        MetadataFailureKind.SemanticNoMatch,
                        "tmdb_season_not_found",
                        TmdbAccessConfirmed: true),
                    ElapsedMilliseconds(started),
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (season.SeriesId != claim.TmdbSeriesId
                || season.SeasonNumber != seasonNumber
                || season.Episodes is null
                || season.Episodes.Count != season.EpisodeCount)
            {
                await RecordFailureAndStopAsync(
                    claim,
                    "tmdb_episode_snapshot",
                    null,
                    new MetadataFailure(
                        MetadataFailureKind.Protocol,
                        "tmdb_season_episode_snapshot_invalid",
                        TmdbAccessConfirmed: false),
                    ElapsedMilliseconds(started),
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            validatedSeasons.Add(season);
        }

        var completion = await resolutions.CompleteEpisodesAsync(
            claim,
            results,
            validatedSeasons,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        foreach (var duplicate in completion.DuplicateHits)
        {
            duplicateNotifier.Notify(
                claim.Resolution.DuplicateNotificationEnabled,
                claim.Resolution.SourceProfileId ?? claim.Resolution.SourceId ?? "unknown",
                claim.Resolution.SourceId ?? "unknown",
                $"tmdb:{duplicate.TmdbSeriesId}:s{duplicate.TmdbSeasonNumber}:e{duplicate.TmdbEpisodeNumber}",
                duplicate.Reason);
        }
        await LearnTrustedOffsetAsync(claim, results, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private sealed record EpisodeDateContext(
        IReadOnlyList<BangumiEpisode> BangumiEpisodes,
        IReadOnlyList<TmdbEpisode> TmdbEpisodes);

    private static bool HasBangumiEpisodeIdentity(
        IReadOnlyList<BangumiEpisode> episodes,
        int sourceEpisode) =>
        episodes.Any(value => value.Type == 0
            && (value.EpisodeNumber == sourceEpisode
                || value.SortNumber == sourceEpisode));

    private static async Task<IReadOnlyList<BangumiEpisode>> GetBangumiEpisodesAsync(
        IBangumiEpisodeClient client,
        int subjectId,
        IReadOnlyCollection<int> sourceCandidates,
        bool cacheAlreadyBypassed,
        CancellationToken cancellationToken)
    {
        var values = await client.GetEpisodesAsync(subjectId, cancellationToken)
            .ConfigureAwait(false);
        if (!cacheAlreadyBypassed
            && client is IBangumiEpisodeRefreshClient refreshClient
            && sourceCandidates.Any(candidate =>
                HasBangumiEpisodeIdentityWithoutAirDate(values, candidate)))
        {
            return await refreshClient.RefreshEpisodesAsync(subjectId, cancellationToken)
                .ConfigureAwait(false);
        }

        return values;
    }

    private static bool HasBangumiEpisodeIdentityWithoutAirDate(
        IReadOnlyList<BangumiEpisode> episodes,
        int sourceEpisode)
    {
        var matches = episodes.Where(value => value.Type == 0
            && (value.EpisodeNumber == sourceEpisode
                || value.SortNumber == sourceEpisode)).ToArray();
        return matches.Length > 0 && matches.All(value => value.AirDate is null);
    }

    private async Task LearnTrustedOffsetAsync(
        MetadataEpisodeTaskClaim claim,
        IReadOnlyList<MetadataEpisodeFileResolution> results,
        CancellationToken cancellationToken)
    {
        if (!options.Metadata.MikanTrustedOffsetCacheEnabled
            || claim.EpisodeResolvedByTrustedOffset
            || claim.HasMultipleSeasons
            || !string.Equals(claim.Resolution.SourceAdapter, "mikan", StringComparison.OrdinalIgnoreCase)
            || claim.Resolution.MikanId is null or <= 0
            || claim.Resolution.GroupId is null or <= 0)
        {
            return;
        }

        var evidence = new List<MikanOffsetEvidenceObservation>();
        foreach (var result in results.Where(result => result.Episode is not null))
        {
            var file = claim.Files.Single(candidate => candidate.FileId == result.FileId);
            if (!SubtitleAssociationResolver.IsVideo(file.RelativePath)
                || !int.TryParse(
                    file.FileEpisodeCandidate,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var sourceEpisode)
                || sourceEpisode <= 0)
            {
                continue;
            }

            evidence.Add(new MikanOffsetEvidenceObservation(
                claim.Resolution.MikanId.Value,
                claim.Resolution.GroupId.Value,
                sourceEpisode,
                result.Episode!.SeriesId,
                result.Episode.SeasonNumber,
                result.Episode.EpisodeNumber - sourceEpisode));
        }

        if (evidence.Count == 0
            || evidence.Select(item => (
                    item.TmdbSeriesId,
                    item.TmdbSeasonNumber,
                    item.EpisodeOffset))
                .Distinct()
                .Count() != 1)
        {
            return;
        }

        foreach (var observation in evidence
                     .GroupBy(item => item.SourceEpisode)
                     .Select(group => group.First()))
        {
            await trustedOffsets.ObserveAsync(
                observation,
                _timeProvider.GetUtcNow(),
                options.Metadata.MikanTrustedOffsetRequiredEpisodes,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryApplyAiAsync(
        MetadataEpisodeTaskClaim claim,
        List<MetadataEpisodeFileResolution> results,
        string aiTriggerReason,
        CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var resolved = await aiMetadata.ResolveAsync(
            claim.Resolution,
            claim.Files,
            claim.TmdbSeriesId,
            claim.TmdbSeasonNumber,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (resolved.Publication?.ShouldAudit == true)
        {
            await RecordAsync(
                claim,
                "ai_pubdate",
                null,
                resolved.Publication.Result,
                resolved.Publication.ErrorCode,
                resolved.Publication.Retryable,
                ElapsedMilliseconds(started),
                cancellationToken).ConfigureAwait(false);
        }

        if (!resolved.IsApplicable)
        {
            return false;
        }

        if (!resolved.IsSuccess)
        {
            return await HandleAiFailureAsync(
                claim,
                results,
                resolved.Failure!,
                resolved.Usage,
                started,
                aiTriggerReason,
                cancellationToken).ConfigureAwait(false);
        }

        var validatedByPath = resolved.Value!.Files.ToDictionary(
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
                    "ai_metadata",
                    null,
                    "error",
                    failure.Code,
                    false,
                    ElapsedMilliseconds(started),
                    cancellationToken,
                    resolved.Usage,
                    aiTriggerReason).ConfigureAwait(false);
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
                    null,
                    ResolutionSource:
                        TmdbResolutionSource.AiMetadata);
        }

        var attemptId = await RecordAsync(
            claim,
            "ai_metadata",
            null,
            "matched",
            null,
            false,
            ElapsedMilliseconds(started),
            cancellationToken,
            resolved.Usage,
            aiTriggerReason).ConfigureAwait(false);
        for (var index = 0; index < results.Count; index++)
        {
            if (results[index].ResolutionSource == TmdbResolutionSource.AiMetadata
                && results[index].ResolutionAttemptId is null)
            {
                results[index] = results[index] with
                {
                    ResolutionAttemptId = attemptId,
                };
            }
        }
        return false;
    }

    private async Task<bool> HandleAiFailureAsync(
        MetadataEpisodeTaskClaim claim,
        List<MetadataEpisodeFileResolution> results,
        MetadataFailure failure,
        AiMetadataProviderUsage? aiUsage,
        long started,
        string aiTriggerReason,
        CancellationToken cancellationToken)
    {
        if (IsRetryable(failure.Kind))
        {
            await RecordFailureAndStopAsync(
                claim,
                "ai_metadata",
                null,
                failure,
                ElapsedMilliseconds(started),
                cancellationToken,
                aiUsage,
                aiTriggerReason).ConfigureAwait(false);
            return true;
        }

        AnnotateUnresolvedAiFailure(claim, results, failure.Code);
        await RecordAsync(
            claim,
            "ai_metadata",
            null,
            failure.Kind == MetadataFailureKind.SemanticNoMatch ? "not_matched" : "error",
            failure.Code,
            false,
            ElapsedMilliseconds(started),
            cancellationToken,
            aiUsage,
            aiTriggerReason).ConfigureAwait(false);
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
        CancellationToken cancellationToken,
        AiMetadataProviderUsage? aiUsage = null,
        string? aiTriggerReason = null)
    {
        await RecordAsync(
            claim,
            strategy,
            priority,
            "failed",
            failure.Code,
            IsRetryable(failure.Kind),
            durationMilliseconds,
            cancellationToken,
            aiUsage,
            aiTriggerReason).ConfigureAwait(false);
        await resolutions.FailEpisodesAsync(
            claim,
            failure,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
    }

    private Task<string> RecordAsync(
        MetadataEpisodeTaskClaim claim,
        string strategy,
        int? priority,
        string result,
        string? errorCode,
        bool retryable,
        long durationMilliseconds,
        CancellationToken cancellationToken,
        AiMetadataProviderUsage? aiUsage = null,
        string? aiTriggerReason = null) =>
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
                durationMilliseconds,
                AiUsage: aiUsage,
                AiTriggerReason: aiTriggerReason),
            _timeProvider.GetUtcNow(),
            cancellationToken);

    private static string BuildAiEpisodeTriggerReason(
        MetadataEpisodeTaskClaim claim,
        IReadOnlyList<MetadataEpisodeFileResolution> results)
    {
        var reasons = results
            .Where(result => result.Episode is null)
            .Select(result => (
                Result: result,
                File: claim.Files.Single(file => file.FileId == result.FileId)))
            .Where(value => SubtitleAssociationResolver.IsVideo(value.File.RelativePath))
            .Select(value => ResolveAiEpisodeTriggerReason(claim, value.File, value.Result))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        return $"episode_unresolved:{string.Join('+', reasons.Length == 0 ? ["unknown"] : reasons)}";
    }

    private static string ResolveAiEpisodeTriggerReason(
        MetadataEpisodeTaskClaim claim,
        MetadataTaskFileProjection file,
        MetadataEpisodeFileResolution result)
    {
        if (string.Equals(
                claim.Resolution.SourceAdapter,
                "mikan",
                StringComparison.OrdinalIgnoreCase)
            && (string.Equals(result.OtherReason, "episode_not_parsed", StringComparison.Ordinal)
                || (string.Equals(result.OtherReason, "special_episode", StringComparison.Ordinal)
                    && int.TryParse(
                        file.SourceEpisode,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out _)))
            && string.IsNullOrWhiteSpace(file.FileEpisodeCandidate))
        {
            return FileEpisodeCandidateResolver.Resolve("mikan", file.RelativePath).Reason;
        }

        return result.OtherReason ?? "episode_unresolved";
    }

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
