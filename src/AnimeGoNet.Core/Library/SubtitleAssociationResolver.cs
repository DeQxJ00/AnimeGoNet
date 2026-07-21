namespace AnimeGoNet.Core.Library;

public sealed record TorrentMediaFile(
    string FileId,
    string RelativePath,
    int? SourceEpisode);

public sealed record SubtitleAssociation(
    string SubtitleFileId,
    string? VideoFileId,
    string RenameSuffix,
    string? UnmatchedReason);

public static class SubtitleAssociationResolver
{
    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ass", ".ssa", ".srt", ".vtt", ".sup", ".idx", ".sub",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".m2ts", ".ts", ".webm",
    };

    public static bool IsSubtitle(string relativePath) =>
        SubtitleExtensions.Contains(Path.GetExtension(Normalize(relativePath)));

    public static bool IsVideo(string relativePath) =>
        VideoExtensions.Contains(Path.GetExtension(Normalize(relativePath)));

    public static IReadOnlyList<SubtitleAssociation> Resolve(IReadOnlyList<TorrentMediaFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Select(file => file.FileId).Distinct(StringComparer.Ordinal).Count() != files.Count)
        {
            throw new ArgumentException("Torrent file IDs must be unique.", nameof(files));
        }

        var videos = files.Where(file => IsVideo(file.RelativePath)).ToArray();
        var results = new List<SubtitleAssociation>();
        foreach (var subtitle in files.Where(file => IsSubtitle(file.RelativePath)))
        {
            var sameStem = videos
                .Where(video => SameDirectory(subtitle.RelativePath, video.RelativePath))
                .Select(video => (Video: video, Suffix: StemSuffix(subtitle.RelativePath, video.RelativePath)))
                .Where(item => item.Suffix is not null)
                .ToArray();
            if (sameStem.Length == 1)
            {
                results.Add(Bound(subtitle, sameStem[0].Video, sameStem[0].Suffix!));
                continue;
            }

            if (sameStem.Length > 1)
            {
                results.Add(Unmatched(subtitle, "subtitle_stem_ambiguous"));
                continue;
            }

            var sameEpisode = subtitle.SourceEpisode is > 0
                ? videos.Where(video => video.SourceEpisode == subtitle.SourceEpisode).ToArray()
                : [];
            if (sameEpisode.Length == 1)
            {
                results.Add(Bound(subtitle, sameEpisode[0], string.Empty));
            }
            else
            {
                results.Add(Unmatched(
                    subtitle,
                    sameEpisode.Length > 1 ? "subtitle_episode_ambiguous" : "subtitle_unmatched"));
            }
        }

        return results;
    }

    private static SubtitleAssociation Bound(TorrentMediaFile subtitle, TorrentMediaFile video, string stemSuffix) =>
        new(subtitle.FileId, video.FileId, stemSuffix + Path.GetExtension(Normalize(subtitle.RelativePath)), null);

    private static SubtitleAssociation Unmatched(TorrentMediaFile subtitle, string reason) =>
        new(subtitle.FileId, null, Path.GetExtension(Normalize(subtitle.RelativePath)), reason);

    private static string? StemSuffix(string subtitlePath, string videoPath)
    {
        var subtitleStem = Path.GetFileNameWithoutExtension(Normalize(subtitlePath));
        var videoStem = Path.GetFileNameWithoutExtension(Normalize(videoPath));
        if (string.Equals(subtitleStem, videoStem, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (subtitleStem.Length > videoStem.Length
            && subtitleStem.StartsWith(videoStem, StringComparison.OrdinalIgnoreCase)
            && subtitleStem[videoStem.Length] == '.')
        {
            return subtitleStem[videoStem.Length..];
        }

        return null;
    }

    private static bool SameDirectory(string left, string right) =>
        string.Equals(
            Path.GetDirectoryName(Normalize(left)) ?? string.Empty,
            Path.GetDirectoryName(Normalize(right)) ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Replace('\\', '/');
}
