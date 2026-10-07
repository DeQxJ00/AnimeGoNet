using System.Security;
using System.Text;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;

namespace AnimeGoNet.App.Library;

public sealed class TvShowNfoWriter(AnimeGoOptions? options = null)
{
    private readonly bool _writeBangumiIdWhenTmdbMatched =
        options?.Metadata.WriteBangumiIdWhenTmdbMatched ?? false;
    private readonly bool _writeSeasonBangumiIdWhenTmdbMatched =
        options?.Metadata.WriteSeasonBangumiIdWhenTmdbMatched ?? false;
    private readonly bool _overwriteSeasonBangumiId = options?.Metadata.OverwriteSeasonBangumiId ?? false;

    // Callers only supply IDs carried by a Mikan source, not IDs discovered for other sources.
    public async Task WriteSeasonAsync(
        string saveRoot,
        string seriesDirectoryName,
        int tmdbSeriesId,
        int seasonNumber,
        int? bangumiSubjectId,
        CancellationToken cancellationToken = default)
    {
        if (!_writeSeasonBangumiIdWhenTmdbMatched || tmdbSeriesId <= 0 || bangumiSubjectId is null or <= 0)
            return;
        ArgumentOutOfRangeException.ThrowIfNegative(seasonNumber);
        var seriesDirectory = PathBoundary.Combine(saveRoot, MediaPathPlanner.SanitizeSegment(seriesDirectoryName));
        var seasonDirectory = Path.Combine(seriesDirectory, "S" + seasonNumber.ToString("00", CultureInfo.InvariantCulture));
        var target = Path.Combine(seasonDirectory, "season.nfo");
        if (!PathBoundary.IsWithin(saveRoot, target))
            throw new SafeFileMoveException("nfo_path_outside_root", "Season NFO is outside the save root.");
        foreach (var directory in new[] { saveRoot, seriesDirectory, seasonDirectory })
        {
            var info = new DirectoryInfo(directory);
            if (info.LinkTarget is not null || (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw new SafeFileMoveException("symbolic_path_not_allowed", "Symbolic links are not allowed in NFO paths.");
            Directory.CreateDirectory(directory);
        }
        var targetInfo = new FileInfo(target);
        if (targetInfo.LinkTarget is not null || (targetInfo.Exists && targetInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            throw new SafeFileMoveException("symbolic_path_not_allowed", "Season NFO cannot be a symbolic link.");

        XDocument document;
        if (File.Exists(target))
        {
            using var reader = XmlReader.Create(target, new XmlReaderSettings
            {
                Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 4 * 1024 * 1024,
            });
            document = await XDocument.LoadAsync(reader, LoadOptions.PreserveWhitespace, cancellationToken).ConfigureAwait(false);
            if (document.Root?.Name != "season")
                throw new InvalidDataException("Existing season NFO has an unexpected root element.");
        }
        else
        {
            document = new XDocument(new XElement("season", new XElement("seasonnumber", seasonNumber)));
        }
        if (!_overwriteSeasonBangumiId && document.Root!.Elements("bangumiid")
            .Any(element => !string.IsNullOrWhiteSpace(element.Value)))
            return;
        // Do not put the TV Series ID in the season's TMDB uniqueid: those identify different entities.
        document.Root!.Elements("bangumiid").Remove();
        document.Root.Add(new XElement("bangumiid", bangumiSubjectId.Value));
        var temporary = target + $".animegonet-{Guid.NewGuid():N}.partial";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await document.SaveAsync(stream, SaveOptions.None, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

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
