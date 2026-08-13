using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Metadata;

public sealed partial class AiMetadataResultValidator(ITmdbClient tmdb)
{
    public async Task<AiMetadataValidationResult> ValidateAsync(
        AiMetadataMatchInput input,
        AiMetadataMatchCandidate candidate,
        int? expectedSeriesId = null,
        int? expectedSeasonNumber = null,
        CancellationToken cancellationToken = default)
    {
        var structuralFailure = ValidateStructure(input, candidate);
        if (structuralFailure is not null)
        {
            return Failed(structuralFailure);
        }

        if (candidate.Matched != true)
        {
            return Failed(MetadataFailureKind.SemanticNoMatch, "ai_metadata_not_matched", false);
        }

        var tmdbId = candidate.TmdbId!.Value;
        if (expectedSeriesId is not null && tmdbId != expectedSeriesId.Value)
        {
            return Failed(MetadataFailureKind.Protocol, "ai_tmdb_series_changed", false);
        }

        TmdbSeriesDetails? details;
        try
        {
            details = await tmdb.GetSeriesDetailsAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        }
        catch (TmdbClientException exception)
        {
            return Failed(exception.Kind, exception.SafeCode, exception.TmdbAccessConfirmed);
        }

        if (details is null)
        {
            return Failed(MetadataFailureKind.SemanticNoMatch, "ai_tmdb_series_not_found", true);
        }

        if (details.Series.Id != tmdbId)
        {
            return Failed(MetadataFailureKind.Protocol, "ai_tmdb_series_identity_mismatch", false);
        }

        var seasons = new Dictionary<int, TmdbSeason>();
        var targets = new HashSet<(int Season, int Episode)>();
        var validated = new List<ValidatedAiMetadataFile>(input.Files.Count);
        for (var index = 0; index < input.Files.Count; index++)
        {
            var file = candidate.Files![index];
            var seasonNumber = file.Season!.Value;
            if (expectedSeasonNumber is not null && seasonNumber != expectedSeasonNumber.Value)
            {
                return Failed(MetadataFailureKind.Protocol, "ai_tmdb_season_changed", false);
            }

            if (!seasons.TryGetValue(seasonNumber, out var season))
            {
                try
                {
                    season = await tmdb.GetSeasonAsync(
                        tmdbId,
                        seasonNumber,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (TmdbClientException exception)
                {
                    return Failed(exception.Kind, exception.SafeCode, exception.TmdbAccessConfirmed);
                }

                if (season is null)
                {
                    return Failed(MetadataFailureKind.SemanticNoMatch, "ai_tmdb_season_not_found", true);
                }

                if (season.SeriesId != tmdbId || season.SeasonNumber != seasonNumber)
                {
                    return Failed(MetadataFailureKind.Protocol, "ai_tmdb_season_identity_mismatch", false);
                }

                seasons.Add(seasonNumber, season);
            }

            if (file.Matched != true)
            {
                validated.Add(new ValidatedAiMetadataFile(
                    input.Files[index],
                    season,
                    null,
                    file.Reason));
                continue;
            }

            var episodeNumber = file.Episode!.Value;
            if (!targets.Add((seasonNumber, episodeNumber)))
            {
                return Failed(MetadataFailureKind.Ambiguous, "ai_duplicate_episode_target", true);
            }

            TmdbEpisode? episode;
            try
            {
                episode = await tmdb.GetEpisodeAsync(
                    tmdbId,
                    seasonNumber,
                    episodeNumber,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TmdbClientException exception)
            {
                return Failed(exception.Kind, exception.SafeCode, exception.TmdbAccessConfirmed);
            }

            if (episode is null)
            {
                return Failed(MetadataFailureKind.SemanticNoMatch, "ai_tmdb_episode_not_found", true);
            }

            if (episode.SeriesId != tmdbId
                || episode.SeasonNumber != seasonNumber
                || episode.EpisodeNumber != episodeNumber)
            {
                return Failed(MetadataFailureKind.Protocol, "ai_tmdb_episode_identity_mismatch", false);
            }

            validated.Add(new ValidatedAiMetadataFile(
                input.Files[index],
                season,
                episode,
                null));
        }

        return new AiMetadataValidationResult(
            new ValidatedAiMetadataMatch(details.Series, validated),
            null);
    }

    public static MetadataFailure? ValidateStructure(
        AiMetadataMatchInput input,
        AiMetadataMatchCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(input.Title)
            || input.Files.Count == 0
            || input.TorrentFileCount <= 0
            || input.Files.Any(file => !IsSafeRelativeName(file.Name) || file.SizeBytes < 0)
            || input.BangumiSubjectId is <= 0
            || input.AniDbAnimeId is <= 0
            || input.BangumiEpisodeCandidate is <= 0
            || (input.UseBangumiPubDateFirst
                && (input.TorrentFileCount != 1
                    || input.BangumiSubjectId is null
                    || input.PublishedAt is null
                    || input.BangumiEpisodeCandidate is null))
            || (input.ImdbTitleId is not null && !ImdbTitleIdPattern().IsMatch(input.ImdbTitleId)))
        {
            return new MetadataFailure(MetadataFailureKind.InvalidInput, "ai_metadata_input_invalid", false);
        }

        if (candidate.Matched is null || candidate.Files is null)
        {
            return new MetadataFailure(MetadataFailureKind.Protocol, "ai_metadata_response_incomplete", false);
        }

        if (candidate.Files.Count != input.Files.Count)
        {
            return new MetadataFailure(MetadataFailureKind.Protocol, "ai_file_count_mismatch", false);
        }

        if (candidate.Matched == true)
        {
            if (candidate.TmdbId is null or <= 0)
            {
                return new MetadataFailure(MetadataFailureKind.Protocol, "ai_metadata_match_invalid", false);
            }
        }
        else if (string.IsNullOrWhiteSpace(candidate.Reason))
        {
            return new MetadataFailure(MetadataFailureKind.Protocol, "ai_metadata_no_match_reason_missing", false);
        }

        for (var index = 0; index < input.Files.Count; index++)
        {
            var file = candidate.Files[index];
            if (input.Files.Count > 1
                && !string.Equals(file.Name, input.Files[index].Name, StringComparison.Ordinal))
            {
                return new MetadataFailure(MetadataFailureKind.Protocol, "ai_file_identity_mismatch", false);
            }

            if (file.Matched is null || file.Season is <= 0)
            {
                return new MetadataFailure(MetadataFailureKind.Protocol, "ai_file_resolution_incomplete", false);
            }

            if (file.Matched == true)
            {
                if (file.Season is null || file.Episode is null or <= 0)
                {
                    return new MetadataFailure(MetadataFailureKind.Protocol, "ai_episode_match_invalid", false);
                }
            }
            else if (file.Episode is not null || string.IsNullOrWhiteSpace(file.Reason))
            {
                return new MetadataFailure(MetadataFailureKind.Protocol, "ai_other_resolution_invalid", false);
            }
            else if (candidate.Matched == true && file.Season is null)
            {
                return new MetadataFailure(MetadataFailureKind.Protocol, "ai_other_season_missing", false);
            }
        }

        return null;
    }

    private static bool IsSafeRelativeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Contains('\0', StringComparison.Ordinal)
            || name.StartsWith('/')
            || name.StartsWith('\\')
            || WindowsRootedPathPattern().IsMatch(name))
        {
            return false;
        }

        return !name
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }

    private static AiMetadataValidationResult Failed(MetadataFailure failure) =>
        new(null, failure);

    private static AiMetadataValidationResult Failed(
        MetadataFailureKind kind,
        string code,
        bool tmdbAccessConfirmed) =>
        Failed(new MetadataFailure(kind, code, tmdbAccessConfirmed));

    [GeneratedRegex("^tt[0-9]{7,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImdbTitleIdPattern();

    [GeneratedRegex("^[A-Za-z]:[\\\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsRootedPathPattern();
}
