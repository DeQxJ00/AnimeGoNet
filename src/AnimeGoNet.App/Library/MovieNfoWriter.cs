using System.Globalization;
using System.Security;
using System.Text;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Library;

public sealed class MovieNfoWriter(AnimeGoOptions? options = null)
{
    private readonly bool _writeBangumiIdWhenTmdbMatched =
        options?.Metadata.WriteBangumiIdWhenTmdbMatched ?? false;

    public async Task WriteAsync(
        string saveRoot,
        TmdbMovie movie,
        int? bangumiSubjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        ArgumentNullException.ThrowIfNull(movie);
        ArgumentOutOfRangeException.ThrowIfLessThan(movie.Id, 1);

        var directoryName = MoviePathPlanner.DirectoryName(movie.Title, movie.ReleaseDate);
        var movieDirectory = PathBoundary.Combine(saveRoot, directoryName);
        var target = Path.Combine(movieDirectory, "movie.nfo");
        if (!PathBoundary.IsWithin(saveRoot, target))
        {
            throw new SafeFileMoveException(
                "nfo_path_outside_root",
                "Movie NFO target is outside the captured save root.");
        }

        Directory.CreateDirectory(movieDirectory);
        foreach (var directory in new[] { saveRoot, movieDirectory })
        {
            var info = new DirectoryInfo(directory);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null)
            {
                throw new SafeFileMoveException(
                    "symbolic_path_not_allowed",
                    "Symbolic links are not allowed in NFO paths.");
            }
        }

        var title = SecurityElement.Escape(movie.Title) ?? string.Empty;
        var originalTitle = SecurityElement.Escape(movie.OriginalTitle) ?? string.Empty;
        var released = movie.ReleaseDate is null
            ? string.Empty
            : $"  <premiered>{movie.ReleaseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}</premiered>\n"
              + $"  <year>{movie.ReleaseDate.Value.Year.ToString(CultureInfo.InvariantCulture)}</year>\n";
        var bangumi = _writeBangumiIdWhenTmdbMatched && bangumiSubjectId is > 0
            ? $"  <bangumiid>{bangumiSubjectId.Value}</bangumiid>\n"
            : string.Empty;
        var content = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <movie>
              <title>{title}</title>
              <originaltitle>{originalTitle}</originaltitle>
              <tmdbid>{movie.Id}</tmdbid>
              <uniqueid type="tmdb" default="true">{movie.Id}</uniqueid>
            {released}{bangumi}</movie>
            """;
        var temporary = target + $".animegonet-{Guid.NewGuid():N}.partial";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                content,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
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
