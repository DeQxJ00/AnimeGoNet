using System.Text.RegularExpressions;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.Core.Metadata;

public sealed partial class AiMetadataResultValidator(
    ITmdbClient tmdb,
    AnimeGoOptions? options = null)
{
    public async Task<AiMetadataValidationResult> ValidateAsync(
        AiMetadataMatchInput input,
        AiMetadataMatchCandidate candidate,
        int? expectedSeriesId = null,
        int? expectedSeasonNumber = null,
        CancellationToken cancellationToken = default)
    {
        var structuralFailure = ValidateStructure(
            input,
            candidate,
            options?.Metadata.Ai.FileIdentityFuzzyMatchLimit ?? 1);
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
            return Failed(
                MetadataFailureKind.Ambiguous,
                "ai_tmdb_series_candidate_conflict",
                false);
        }

        TmdbSeriesDetails? details;
        try
        {
            details = await tmdb.GetSeriesDetailsAsync(tmdbId, cancellationToken).ConfigureAwait(false);
            if (details is null && tmdb is ITmdbRefreshClient detailsRefreshClient)
            {
                details = await detailsRefreshClient.RefreshSeriesDetailsAsync(
                    tmdbId,
                    cancellationToken).ConfigureAwait(false);
            }
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
                    if (season is null && tmdb is ITmdbRefreshClient seasonRefreshClient)
                    {
                        season = await seasonRefreshClient.RefreshSeasonAsync(
                            tmdbId,
                            seasonNumber,
                            cancellationToken).ConfigureAwait(false);
                    }
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

            if (file.IsExtras)
            {
                validated.Add(new ValidatedAiMetadataFile(
                    input.Files[index],
                    season,
                    null,
                    file.Reason,
                    IsExtra: true));
                continue;
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
                if (episode is null && tmdb is ITmdbRefreshClient episodeRefreshClient)
                {
                    episode = await episodeRefreshClient.RefreshEpisodeAsync(
                        tmdbId,
                        seasonNumber,
                        episodeNumber,
                        cancellationToken).ConfigureAwait(false);
                }
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
        AiMetadataMatchCandidate candidate,
        int fileIdentityFuzzyMatchLimit = 1)
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

        if (input.Files.Count > 1
            && !HasCompatibleOrderedFileIdentities(
                input.Files,
                candidate.Files,
                Math.Clamp(
                    fileIdentityFuzzyMatchLimit,
                    0,
                    AiMatchingOptions.MaximumFileIdentityFuzzyMatchLimit)))
        {
            return new MetadataFailure(MetadataFailureKind.Protocol, "ai_file_identity_mismatch", false);
        }

        for (var index = 0; index < input.Files.Count; index++)
        {
            var file = candidate.Files[index];
            if (file.Matched is null || file.Season is <= 0)
            {
                return new MetadataFailure(MetadataFailureKind.Protocol, "ai_file_resolution_incomplete", false);
            }

            if (file.Matched == true)
            {
                if (file.Episode is null || (file.Episode <= 0 && !file.IsExtras))
                {
                    return new MetadataFailure(MetadataFailureKind.Protocol, "ai_episode_match_invalid", false);
                }
            }
            else if ((file.Episode is not null && !file.IsExtras)
                || string.IsNullOrWhiteSpace(file.Reason))
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

    private static bool HasCompatibleOrderedFileIdentities(
        IReadOnlyList<AiMetadataFileInput> inputFiles,
        IReadOnlyList<AiMetadataFileCandidate> candidateFiles,
        int fuzzyMatchLimit)
    {
        var mismatchedIndexes = new List<int>(capacity: 2);
        for (var index = 0; index < inputFiles.Count; index++)
        {
            if (string.Equals(inputFiles[index].Name, candidateFiles[index].Name, StringComparison.Ordinal))
            {
                continue;
            }

            mismatchedIndexes.Add(index);
            if (mismatchedIndexes.Count > fuzzyMatchLimit)
            {
                return false;
            }
        }

        if (mismatchedIndexes.Count == 0)
        {
            return true;
        }

        // Fuzzy matching is only a narrow fallback. At least one unchanged item must
        // remain at the same index to anchor the list order.
        if (mismatchedIndexes.Count == inputFiles.Count)
        {
            return false;
        }

        foreach (var index in mismatchedIndexes)
        {
            var expected = inputFiles[index].Name;
            var actual = candidateFiles[index].Name;
            if (actual is null)
            {
                return false;
            }

            // An exact name from another position means the model reordered the list.
            if (inputFiles.Where((_, inputIndex) => inputIndex != index)
                .Any(file => string.Equals(file.Name, actual, StringComparison.Ordinal)))
            {
                return false;
            }

            if (!HasCompatibleFileIdentity(expected, actual))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasCompatibleFileIdentity(string expected, string actual)
    {
        if (!string.Equals(
                Path.GetExtension(expected),
                Path.GetExtension(actual),
                StringComparison.OrdinalIgnoreCase)
            || !HaveSameIdentityTokens(NumberTokenPattern(), expected, actual)
            || !HaveSameIdentityTokens(LongHexTokenPattern(), expected, actual))
        {
            return false;
        }

        return CalculateSimilarity(expected, actual) >= 0.90;
    }

    private static bool HaveSameIdentityTokens(Regex pattern, string expected, string actual)
    {
        var expectedMatches = pattern.Matches(expected);
        var actualMatches = pattern.Matches(actual);
        if (expectedMatches.Count != actualMatches.Count)
        {
            return false;
        }

        for (var index = 0; index < expectedMatches.Count; index++)
        {
            if (!string.Equals(
                    expectedMatches[index].Value,
                    actualMatches[index].Value,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static double CalculateSimilarity(string expected, string actual)
    {
        if (expected.Length == 0 || actual.Length == 0)
        {
            return expected.Length == actual.Length ? 1 : 0;
        }

        var previous = new int[actual.Length + 1];
        var current = new int[actual.Length + 1];
        for (var column = 0; column <= actual.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= expected.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= actual.Length; column++)
            {
                var substitutionCost = expected[row - 1] == actual[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        var distance = previous[actual.Length];
        return 1 - ((double)distance / Math.Max(expected.Length, actual.Length));
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

    [GeneratedRegex("[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NumberTokenPattern();

    [GeneratedRegex("[A-Fa-f0-9]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex LongHexTokenPattern();
}
