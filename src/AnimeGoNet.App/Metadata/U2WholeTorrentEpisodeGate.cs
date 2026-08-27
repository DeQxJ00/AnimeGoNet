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
    IReadOnlySet<string> BlockingFileIds);

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
            return RequiresAi("u2_regular_season_not_verified", extras, videos.Select(file => file.FileId));
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
        if (candidates
            .Where(value => value.Episode is > 0)
            .GroupBy(value => value.Episode!.Value)
            .Any(group => group.Count() > 1))
        {
            var duplicateFileIds = candidates
                .Where(value => value.Episode is > 0)
                .GroupBy(value => value.Episode!.Value)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group.Select(value => value.File.FileId));
            return RequiresAi("u2_duplicate_episode_candidate", extras, duplicateFileIds);
        }

        var mainVideos = candidates
            .Where(value => !extras.Contains(value.File.FileId))
            .ToArray();
        if (mainVideos.Length <= 1)
        {
            return RequiresAi(
                "u2_single_or_non_season_torrent",
                extras,
                mainVideos.Select(value => value.File.FileId));
        }

        if (mainVideos.Any(value => value.Episode is null))
        {
            return RequiresAi(
                "u2_main_video_episode_not_parsed",
                extras,
                mainVideos
                    .Where(value => value.Episode is null)
                    .Select(value => value.File.FileId));
        }

        var torrentEpisodes = mainVideos.Select(value => value.Episode!.Value).ToArray();
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
                mainVideos.Select(value => value.File.FileId));
        }

        var exact = torrentEpisodes.Length == tmdbEpisodes.Length
            && torrentEpisodes.Distinct().Count() == torrentEpisodes.Length
            && torrentEpisodes.OrderBy(value => value).SequenceEqual(tmdbEpisodes);
        return exact
            ? new U2WholeTorrentEpisodeDecision(true, false, null, extras, EmptyFileIds())
            : RequiresAi(
                "u2_torrent_not_complete_tmdb_season",
                extras,
                mainVideos.Select(value => value.File.FileId));
    }

    private static U2WholeTorrentEpisodeDecision RequiresAi(
        string reason,
        IReadOnlySet<string> extras,
        IEnumerable<string> blockingFileIds) =>
        new(
            true,
            true,
            reason,
            extras,
            blockingFileIds.ToHashSet(StringComparer.Ordinal));

    private static HashSet<string> EmptyFileIds() =>
        new HashSet<string>(StringComparer.Ordinal);
}
