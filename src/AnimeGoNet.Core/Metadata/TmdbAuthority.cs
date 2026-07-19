namespace AnimeGoNet.Core.Metadata;

public sealed class TmdbAuthority(ITmdbClient client)
{
    public async Task<TmdbValidationResult> ValidateEpisodeAsync(
        int seriesId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken = default)
    {
        if (seriesId <= 0 || seasonNumber <= 0 || episodeNumber <= 0)
        {
            return Failed(MetadataFailureKind.InvalidInput, "tmdb_identity_invalid", accessConfirmed: false);
        }

        try
        {
            var series = await client.GetSeriesAsync(seriesId, cancellationToken).ConfigureAwait(false);
            if (series is null)
            {
                return Failed(MetadataFailureKind.SemanticNoMatch, "tmdb_series_not_found", accessConfirmed: true);
            }

            if (series.Id != seriesId)
            {
                return Failed(MetadataFailureKind.Protocol, "tmdb_series_identity_mismatch", accessConfirmed: false);
            }

            var canonicalName = !string.IsNullOrWhiteSpace(series.Name)
                ? series.Name
                : series.OriginalName;
            if (string.IsNullOrWhiteSpace(canonicalName))
            {
                return Failed(MetadataFailureKind.Protocol, "tmdb_series_name_missing", accessConfirmed: false);
            }

            var season = await client.GetSeasonAsync(seriesId, seasonNumber, cancellationToken).ConfigureAwait(false);
            if (season is null)
            {
                return Failed(MetadataFailureKind.SemanticNoMatch, "tmdb_season_not_found", accessConfirmed: true);
            }

            if (season.Id <= 0 || season.SeriesId != seriesId || season.SeasonNumber != seasonNumber)
            {
                return Failed(MetadataFailureKind.Protocol, "tmdb_season_identity_mismatch", accessConfirmed: false);
            }

            var episode = await client.GetEpisodeAsync(
                seriesId,
                seasonNumber,
                episodeNumber,
                cancellationToken).ConfigureAwait(false);
            if (episode is null)
            {
                return Failed(MetadataFailureKind.SemanticNoMatch, "tmdb_episode_not_found", accessConfirmed: true);
            }

            if (episode.Id <= 0
                || episode.SeriesId != seriesId
                || episode.SeasonNumber != seasonNumber
                || episode.EpisodeNumber != episodeNumber)
            {
                return Failed(MetadataFailureKind.Protocol, "tmdb_episode_identity_mismatch", accessConfirmed: false);
            }

            return new TmdbValidationResult(
                new TmdbCanonicalEpisode(series, season, episode, canonicalName.Trim()),
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TmdbClientException exception)
        {
            return Failed(exception.Kind, exception.SafeCode, exception.TmdbAccessConfirmed);
        }
    }

    private static TmdbValidationResult Failed(
        MetadataFailureKind kind,
        string code,
        bool accessConfirmed) =>
        new(null, new MetadataFailure(kind, code, accessConfirmed));
}
