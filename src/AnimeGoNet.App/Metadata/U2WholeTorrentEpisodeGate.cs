using System.Globalization;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Media;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Metadata;

internal sealed record U2WholeTorrentEpisodeDecision(
    bool IsApplicable,
    bool RequiresAi,
    string? Reason,
    IReadOnlySet<string> ExplicitExtraFileIds,
    IReadOnlySet<string> BlockingFileIds,
    IReadOnlySet<string> TmdbValidatedCandidateFileIds);

internal static class U2WholeTorrentEpisodeGate
{
    public static U2WholeTorrentEpisodeDecision Evaluate(
        MetadataEpisodeTaskClaim claim,
        TmdbSeason? season)
    {
        if (!string.Equals(claim.Resolution.SourceAdapter, "u2", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(claim.Resolution.MediaType, MediaTypes.Tv, StringComparison.OrdinalIgnoreCase))
        {
            return new U2WholeTorrentEpisodeDecision(
                false,
                false,
                null,
                EmptyFileIds(),
                EmptyFileIds(),
                EmptyFileIds());
        }

        var videos = claim.Files
            .Where(file => SubtitleAssociationResolver.IsVideo(file.RelativePath))
            .ToArray();
        var extras = videos
            .Where(file => U2AiFilePolicy.IsExplicitExtra(file.RelativePath))
            .Select(file => file.FileId)
            .ToHashSet(StringComparer.Ordinal);

        if (claim.TmdbSeasonNumber <= 0
            || season is null
            || season.SeasonNumber <= 0
            || season.SeriesId != claim.TmdbSeriesId
            || season.SeasonNumber != claim.TmdbSeasonNumber)
        {
            return RequiresAi(
                "u2_regular_season_not_verified",
                extras,
                videos.Select(file => file.FileId),
                EmptyFileIds());
        }

        var candidates = videos
            .Select(file => (
                File: file,
                Episode: int.TryParse(
                    file.FileEpisodeCandidate,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value) && value > 0
                        ? value
                        : (int?)null))
            .ToArray();
        var duplicateCandidates = candidates
            .Where(value => value.Episode is > 0)
            .GroupBy(value => value.Episode!.Value)
            .Where(group => group.Count() > 1)
            .ToArray();

        var mainVideos = candidates
            .Where(value => !extras.Contains(value.File.FileId))
            .ToArray();
        var tmdbEpisodes = (season.Episodes ?? [])
            .Where(episode => episode.SeriesId == claim.TmdbSeriesId
                && episode.SeasonNumber == claim.TmdbSeasonNumber
                && episode.EpisodeNumber > 0)
            .Select(episode => episode.EpisodeNumber)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        if (tmdbEpisodes.Length == 0)
        {
            return RequiresAi(
                "u2_tmdb_season_episode_snapshot_empty",
                extras,
                mainVideos.Select(value => value.File.FileId),
                EmptyFileIds());
        }

        var tmdbEpisodeSet = tmdbEpisodes.ToHashSet();
        var duplicateEpisodeNumbers = duplicateCandidates
            .Select(group => group.Key)
            .ToHashSet();
        var validatedCandidateFileIds = mainVideos
            .Where(value => value.Episode is > 0
                && !duplicateEpisodeNumbers.Contains(value.Episode.Value)
                && tmdbEpisodeSet.Contains(value.Episode.Value))
            .Select(value => value.File.FileId)
            .ToHashSet(StringComparer.Ordinal);
        if (duplicateCandidates.Length > 0)
        {
            var duplicateFileIds = duplicateCandidates
                .SelectMany(group => group.Select(value => value.File.FileId));
            return RequiresAi(
                "u2_duplicate_episode_candidate",
                extras,
                duplicateFileIds,
                validatedCandidateFileIds);
        }
        var candidatesMissingFromTmdb = mainVideos
            .Where(value => value.Episode is > 0
                && !tmdbEpisodeSet.Contains(value.Episode.Value))
            .Select(value => value.File.FileId)
            .ToArray();
        if (candidatesMissingFromTmdb.Length > 0)
        {
            return RequiresAi(
                "u2_episode_candidate_not_in_tmdb_season",
                extras,
                candidatesMissingFromTmdb,
                validatedCandidateFileIds);
        }

        var unparsedFileIds = mainVideos
            .Where(value => value.Episode is null)
            .Select(value => value.File.FileId)
            .ToArray();
        var parsedTorrentEpisodes = mainVideos
            .Where(value => value.Episode is > 0)
            .Select(value => value.Episode!.Value)
            .ToArray();
        var parsedSetExactlyMatchesTmdb = parsedTorrentEpisodes.Length == tmdbEpisodes.Length
            && parsedTorrentEpisodes.Distinct().Count() == parsedTorrentEpisodes.Length
            && parsedTorrentEpisodes.OrderBy(value => value).SequenceEqual(tmdbEpisodes);
        // An explicit AniDB identity plus a verified TMDB Series/Season makes the
        // complete numbered set authoritative. Remaining unnumbered videos cannot
        // be regular episodes in that season, so downstream classification may
        // keep them as Extras (or expose Movie hints) without invoking AI.
        if (claim.Resolution.AniDbAnimeId is > 0 && parsedSetExactlyMatchesTmdb)
        {
            return new U2WholeTorrentEpisodeDecision(
                true,
                false,
                null,
                extras.Concat(unparsedFileIds).ToHashSet(StringComparer.Ordinal),
                EmptyFileIds(),
                validatedCandidateFileIds);
        }

        if (unparsedFileIds.Length > 0)
        {
            return RequiresAi(
                "u2_main_video_episode_not_parsed",
                extras,
                unparsedFileIds,
                validatedCandidateFileIds);
        }

        if (mainVideos.Length <= 1)
        {
            return RequiresAi(
                "u2_single_or_non_season_torrent",
                extras,
                mainVideos.Select(value => value.File.FileId),
                validatedCandidateFileIds);
        }

        return parsedSetExactlyMatchesTmdb
            ? new U2WholeTorrentEpisodeDecision(
                true,
                false,
                null,
                extras,
                EmptyFileIds(),
                validatedCandidateFileIds)
            : RequiresAi(
                "u2_torrent_not_complete_tmdb_season",
                extras,
                EmptyFileIds(),
                validatedCandidateFileIds);
    }

    private static U2WholeTorrentEpisodeDecision RequiresAi(
        string reason,
        IReadOnlySet<string> extras,
        IEnumerable<string> blockingFileIds,
        IReadOnlySet<string> tmdbValidatedCandidateFileIds) =>
        new(
            true,
            true,
            reason,
            extras,
            blockingFileIds.ToHashSet(StringComparer.Ordinal),
            tmdbValidatedCandidateFileIds);

    private static HashSet<string> EmptyFileIds() =>
        new HashSet<string>(StringComparer.Ordinal);
}
