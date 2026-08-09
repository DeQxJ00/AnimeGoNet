using System.Security;
using System.Text;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;

namespace AnimeGoNet.App.Library;

public sealed class TvShowNfoWriter(AnimeGoOptions? options = null)
{
    private readonly bool _writeBangumiIdWhenTmdbMatched =
        options?.Metadata.WriteBangumiIdWhenTmdbMatched ?? false;

    public async Task WriteAsync(
        string saveRoot,
        string canonicalSeriesName,
        int tmdbSeriesId,
        int? bangumiSubjectId,
        CancellationToken cancellationToken = default) =>
        await WriteAsync(
            saveRoot,
            canonicalSeriesName,
            canonicalSeriesName,
            tmdbSeriesId,
            bangumiSubjectId,
            cancellationToken).ConfigureAwait(false);

    public async Task WriteAsync(
        string saveRoot,
        string seriesDirectoryName,
        string canonicalSeriesName,
        int tmdbSeriesId,
        int? bangumiSubjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tmdbSeriesId);
        if (tmdbSeriesId == 0 && bangumiSubjectId is null or <= 0)
        {
            throw new ArgumentException(
                "TMDB fallback NFO requires a positive Bangumi Subject ID.",
                nameof(bangumiSubjectId));
        }
        var seriesDirectory = PathBoundary.Combine(
            saveRoot,
            MediaPathPlanner.SanitizeSegment(seriesDirectoryName));
        var target = Path.Combine(seriesDirectory, "tvshow.nfo");
        if (!PathBoundary.IsWithin(saveRoot, target))
        {
            throw new SafeFileMoveException("nfo_path_outside_root", "NFO target is outside the captured save root.");
        }

        Directory.CreateDirectory(seriesDirectory);
        foreach (var directory in new[] { saveRoot, seriesDirectory })
        {
            var info = new DirectoryInfo(directory);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null)
            {
                throw new SafeFileMoveException("symbolic_path_not_allowed", "Symbolic links are not allowed in NFO paths.");
            }
        }

        var title = SecurityElement.Escape(canonicalSeriesName) ?? string.Empty;
        var bangumi = bangumiSubjectId is > 0
            && (tmdbSeriesId == 0 || _writeBangumiIdWhenTmdbMatched)
            ? $"  <bangumiid>{bangumiSubjectId.Value}</bangumiid>\n"
            : string.Empty;
        var content = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <tvshow>
              <title>{title}</title>
              <tmdbid>{tmdbSeriesId}</tmdbid>
              <uniqueid type="tmdb" default="true">{tmdbSeriesId}</uniqueid>
            {bangumi}</tvshow>
            """;
        var temporary = target + $".animegonet-{Guid.NewGuid():N}.partial";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
