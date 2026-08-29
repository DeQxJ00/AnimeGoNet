using AnimeGoNet.App.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class U2MovieFileLayoutResolverTests
{
    [Fact]
    public void SingleVideoIsMainAndAttachmentsAreIgnored()
    {
        var result = U2MovieFileLayoutResolver.Resolve(
        [
            File("movie", "Movie.mkv", 700L * 1024 * 1024),
            File("font", "Fonts.7z", 300L * 1024 * 1024),
        ]);

        Assert.True(result.IsResolved);
        Assert.Equal("movie", result.MainFile!.FileId);
        Assert.Empty(result.ExtraVideos);
    }

    [Fact]
    public void ClearlyLargestVideoIsMainAndOtherVideosAreExtras()
    {
        var result = U2MovieFileLayoutResolver.Resolve(
        [
            File("trailer", "Trailer.mkv", 220L * 1024 * 1024),
            File("main", "Feature.mkv", 8L * 1024 * 1024 * 1024),
            File("interview", "Interview.mp4", 80L * 1024 * 1024),
        ]);

        Assert.True(result.IsResolved);
        Assert.Equal("main", result.MainFile!.FileId);
        Assert.Equal(["trailer", "interview"], result.ExtraVideos.Select(file => file.FileId));
    }

    [Fact]
    public void MultipleFeatureSizedVideosRemainAmbiguous()
    {
        var result = U2MovieFileLayoutResolver.Resolve(
        [
            File("disc1", "Disc 1.mkv", 8L * 1024 * 1024 * 1024),
            File("disc2", "Disc 2.mkv", 6L * 1024 * 1024 * 1024),
        ]);

        Assert.False(result.IsResolved);
        Assert.Equal("u2_movie_main_file_ambiguous", result.FailureCode);
    }

    [Fact]
    public void SmallVideosWithoutFeatureLengthMainRemainAmbiguous()
    {
        var result = U2MovieFileLayoutResolver.Resolve(
        [
            File("clip", "Clip.mkv", 700L * 1024 * 1024),
            File("trailer", "Trailer.mkv", 100L * 1024 * 1024),
        ]);

        Assert.False(result.IsResolved);
        Assert.Equal("u2_movie_main_file_ambiguous", result.FailureCode);
    }

    private static MetadataTaskFileProjection File(string id, string path, long size) =>
        new(id, path, size, null, null);
}
