using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class U2EpisodeAmbiguityTests
{
    [Fact]
    public void DuplicateFilenameEpisodesAreAmbiguousOnlyForU2()
    {
        var u2 = Claim("u2", [
            new MetadataTaskFileProjection("s1", "Season 1/Show 01.mkv", 1, "01", "1", TmdbSeasonNumber: 1),
            new MetadataTaskFileProjection("s2", "Season 2/Show 01.mkv", 1, "01", "1", TmdbSeasonNumber: 2),
            new MetadataTaskFileProjection("s3", "Season 2/Show 02.mkv", 1, "02", "2", TmdbSeasonNumber: 2),
        ]);
        var mikan = Claim("mikan", u2.Files);

        Assert.Equal(
            ["s1", "s2"],
            EpisodeMetadataResolutionProcessor.FindU2DuplicateEpisodeFileIds(u2)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
        Assert.Empty(EpisodeMetadataResolutionProcessor.FindU2DuplicateEpisodeFileIds(mikan));
    }

    private static MetadataEpisodeTaskClaim Claim(
        string adapter,
        IReadOnlyList<MetadataTaskFileProjection> files) =>
        new(
            new MetadataTaskClaim(
                "run", "task", "title", null, null, null, 1, "lease",
                Files: files,
                SourceAdapter: adapter),
            100,
            1,
            files);
}
