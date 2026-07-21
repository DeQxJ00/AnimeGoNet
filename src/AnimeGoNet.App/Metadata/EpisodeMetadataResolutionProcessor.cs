using System.Globalization;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Mikan;

namespace AnimeGoNet.App.Metadata;

public sealed class EpisodeMetadataResolutionProcessor(
    MetadataResolutionStore resolutions,
    MikanWorkMetadataRuleStore rules,
    ITmdbClient tmdb,
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
        var results = new List<MetadataEpisodeFileResolution>(claim.Files.Count);
        foreach (var file in claim.Files)
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

        await resolutions.CompleteEpisodesAsync(
            claim,
            results,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        return true;
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
