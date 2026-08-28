using AnimeGoNet.App.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class AnitomyTitleParserTests
{
    [Fact]
    public void ExtractsAnimeTitleAndExactHighlightRange()
    {
        const string source = "Collection/Movie/[Group] Kidou Senkan Nadesico The Movie [1080p].mkv";

        var result = AnitomyTitleParser.ParseTitle(source);

        Assert.True(result.Success);
        Assert.Equal("Kidou Senkan Nadesico The Movie", result.AnimeTitle);
        Assert.Equal(
            result.AnimeTitle,
            result.SourceText.Substring(result.MatchStart, result.MatchLength));
    }

    [Fact]
    public void EmptyInputReturnsNoCandidate()
    {
        var result = AnitomyTitleParser.ParseTitle("   ");

        Assert.False(result.Success);
        Assert.Null(result.AnimeTitle);
        Assert.Equal(-1, result.MatchStart);
        Assert.Equal(0, result.MatchLength);
    }
}
