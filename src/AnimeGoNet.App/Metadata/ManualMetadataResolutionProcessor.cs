using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Mikan;

namespace AnimeGoNet.App.Metadata;

public sealed class ManualMetadataResolutionProcessor(
    MetadataResolutionStore resolutions,
    MikanWorkMetadataRuleStore rules,
    ITmdbClient tmdb,
    TimeProvider? timeProvider = null)
{
    public const int ManualOverridePriority = 100;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var claim = await resolutions.TryClaimNextManualOverrideAsync(
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
        if (rule?.TmdbSeriesId is null || rule.TmdbSeasonNumber is null)
        {
            await FailAsync(
                claim,
                "series",
                new MetadataFailure(MetadataFailureKind.InvalidInput, "manual_override_changed", false),
                durationMilliseconds: 0,
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        var seriesAttemptStarted = _timeProvider.GetTimestamp();
        TmdbSeries? series;
        try
        {
            series = await tmdb.GetSeriesAsync(rule.TmdbSeriesId.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (TmdbClientException exception)
        {
            await FailAsync(
                claim,
                "series",
                ToFailure(exception),
                ElapsedMilliseconds(seriesAttemptStarted),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (series is null)
        {
            await FailAsync(
                claim,
                "series",
                new MetadataFailure(MetadataFailureKind.SemanticNoMatch, "manual_tmdb_series_not_found", true),
                ElapsedMilliseconds(seriesAttemptStarted),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (series.Id != rule.TmdbSeriesId.Value)
        {
            await FailAsync(
                claim,
                "series",
                new MetadataFailure(MetadataFailureKind.Protocol, "manual_tmdb_series_identity_mismatch", false),
                ElapsedMilliseconds(seriesAttemptStarted),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        await RecordSuccessAsync(
            claim,
            "series",
            ElapsedMilliseconds(seriesAttemptStarted),
            cancellationToken).ConfigureAwait(false);

        var seasonAttemptStarted = _timeProvider.GetTimestamp();
        TmdbSeason? season;
        try
        {
            season = await tmdb.GetSeasonAsync(
                series.Id,
                rule.TmdbSeasonNumber.Value,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TmdbClientException exception)
        {
            await FailAsync(
                claim,
                "season",
                ToFailure(exception),
                ElapsedMilliseconds(seasonAttemptStarted),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (season is null)
        {
            await FailAsync(
                claim,
                "season",
                new MetadataFailure(MetadataFailureKind.SemanticNoMatch, "manual_tmdb_season_not_found", true),
                ElapsedMilliseconds(seasonAttemptStarted),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (season.SeriesId != series.Id
            || season.SeasonNumber != rule.TmdbSeasonNumber.Value
            || season.SeasonNumber <= 0)
        {
            await FailAsync(
                claim,
                "season",
                new MetadataFailure(MetadataFailureKind.Protocol, "manual_tmdb_season_identity_mismatch", false),
                ElapsedMilliseconds(seasonAttemptStarted),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        await RecordSuccessAsync(
            claim,
            "season",
            ElapsedMilliseconds(seasonAttemptStarted),
            cancellationToken).ConfigureAwait(false);
        await resolutions.CompleteSeasonAsync(
            claim,
            series,
            season,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task RecordSuccessAsync(
        MetadataTaskClaim claim,
        string stage,
        long durationMilliseconds,
        CancellationToken cancellationToken)
    {
        await resolutions.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                stage,
                "manual_mikan_override",
                ManualOverridePriority,
                "matched",
                null,
                Retryable: false,
                claim.AttemptNumber,
                durationMilliseconds),
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task FailAsync(
        MetadataTaskClaim claim,
        string stage,
        MetadataFailure failure,
        long durationMilliseconds,
        CancellationToken cancellationToken)
    {
        await resolutions.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                stage,
                "manual_mikan_override",
                ManualOverridePriority,
                "failed",
                failure.Code,
                IsRetryable(failure.Kind),
                claim.AttemptNumber,
                durationMilliseconds),
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        await resolutions.FailAsync(
            claim,
            failure,
            fallbackEligible: false,
            "manual_override_active",
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
    }

    private long ElapsedMilliseconds(long startedTimestamp) =>
        Math.Max(0, (long)_timeProvider.GetElapsedTime(startedTimestamp).TotalMilliseconds);

    private static MetadataFailure ToFailure(TmdbClientException exception) =>
        new(exception.Kind, exception.SafeCode, exception.TmdbAccessConfirmed);

    private static bool IsRetryable(MetadataFailureKind kind) =>
        kind is MetadataFailureKind.Network or MetadataFailureKind.RemoteService;
}
