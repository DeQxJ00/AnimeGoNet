using AnimeGoNet.Core.Library;

namespace AnimeGoNet.Core.Tests.Library;

public sealed class MoviePathPlannerTests
{
    [Fact]
    public void MovieAndLanguageSubtitleShareCanonicalYearDirectory()
    {
        var release = new DateOnly(2001, 7, 20);

        Assert.Equal(
            Path.Combine("千与千寻 (2001)", "千与千寻 (2001).mkv"),
            MoviePathPlanner.PlanRelativePath(new MoviePathInput(
                "千与千寻", release, "source/movie.mkv")));
        Assert.Equal(
            Path.Combine("千与千寻 (2001)", "千与千寻 (2001).zh-CN.ass"),
            MoviePathPlanner.PlanRelativePath(new MoviePathInput(
                "千与千寻", release, "source/movie.zh-CN.ass", ".zh-CN.ass")));
    }

    [Fact]
    public void MissingReleaseDateDoesNotInventYearOrSeason()
    {
        var path = MoviePathPlanner.PlanRelativePath(new MoviePathInput(
            "Movie", null, "Movie.mp4"));

        Assert.Equal(Path.Combine("Movie", "Movie.mp4"), path);
        Assert.DoesNotContain("S01", path, StringComparison.Ordinal);
    }
}
