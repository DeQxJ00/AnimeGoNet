using AnimeGoNet.App.Library;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Library;

public sealed class MovieNfoWriterTests
{
    [Fact]
    public async Task WritesMovieIdentityWithoutTvSeasonOrEpisodeFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-movie-nfo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var movie = new TmdbMovie(
                129,
                "千与千寻",
                "千と千尋の神隠し",
                new DateOnly(2001, 7, 20));

            await new MovieNfoWriter().WriteAsync(root, movie, 311);

            var path = Path.Combine(root, "千与千寻 (2001)", "movie.nfo");
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("<movie>", content, StringComparison.Ordinal);
            Assert.Contains("<tmdbid>129</tmdbid>", content, StringComparison.Ordinal);
            Assert.Contains("<premiered>2001-07-20</premiered>", content, StringComparison.Ordinal);
            Assert.DoesNotContain("<season>", content, StringComparison.Ordinal);
            Assert.DoesNotContain("<episode>", content, StringComparison.Ordinal);
            Assert.DoesNotContain("<bangumiid>", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
