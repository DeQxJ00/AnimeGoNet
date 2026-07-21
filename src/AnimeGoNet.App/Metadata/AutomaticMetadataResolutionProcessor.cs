using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed class AutomaticMetadataResolutionProcessor(
    MetadataResolutionStore resolutions,
    IBangumiSubjectClient bangumi,
    BangumiSeasonBacktraceResolver backtrace,
    TmdbSeriesResolver seriesResolver,
    ITmdbClient tmdb,
    AnimeGoOptions options,
    TimeProvider? timeProvider = null)
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

        if (policy.UseTitleSeason)
        {
            var started = _timeProvider.GetTimestamp();
            var season = TmdbSeasonFallbackSelector.SelectTitleSeason(claim.Title, details.Seasons);
            await RecordAsync(claim, "season", "title_season", 2,
                season is null ? "not_matched" : "matched",
                season is null ? "title_season_not_found" : null,
                false, started, cancellationToken).ConfigureAwait(false);
            if (season is not null)
            {
                await resolutions.CompleteSeasonAsync(
                    claim, details.Series, season, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        if (policy.UseFirstSeason)
        {
            var started = _timeProvider.GetTimestamp();
            var season = TmdbSeasonFallbackSelector.SelectFirstSeason(details.Seasons);
            await RecordAsync(claim, "season", "first_season", 1,
                season is null ? "not_matched" : "matched",
                season is null ? "first_season_not_found" : null,
                false, started, cancellationToken).ConfigureAwait(false);
            if (season is not null)
            {
                await resolutions.CompleteSeasonAsync(
                    claim, details.Series, season, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        await FailAsync(claim, direct.Failure!, "tmdb_series_resolved", cancellationToken).ConfigureAwait(false);
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
