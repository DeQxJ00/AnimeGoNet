using AnimeGoNet.App.AiTesterCompat;

namespace AnimeGoNet.App.Tests.AiTesterCompat;

public sealed class EpisodeOffsetCalculatorTests
{
    [Fact]
    public void CalculatesUniformOffsetAfterAiFileMapping()
    {
        MatchRequestInput input = MikanInput("[Group] Show [01].mkv", "[Group] Show [02].mkv");
        TmdbAiMatchResult result = Result(
            new("[Group] Show [01].mkv", true, 2, 13, null),
            new("[Group] Show [02].mkv", true, 2, 14, null));

        LocalEpisodeOffsetResult offset = EpisodeOffsetCalculator.Calculate(input, result);

        Assert.True(offset.Applicable);
        Assert.True(offset.Calculated);
        Assert.Equal(12, offset.EpisodeOffset);
        Assert.Equal(2, offset.Season);
        Assert.Equal(2, offset.MatchedCandidateCount);
    }

    [Fact]
    public void DifferentOffsetsDoNotInvalidateAiMappingButCreateNoEvidence()
    {
        MatchRequestInput input = MikanInput("[Group] Show [01].mkv", "[Group] Show [02].mkv");
        TmdbAiMatchResult result = Result(
            new("[Group] Show [01].mkv", true, 2, 13, null),
            new("[Group] Show [02].mkv", true, 2, 15, null));

        LocalEpisodeOffsetResult offset = EpisodeOffsetCalculator.Calculate(input, result);

        Assert.False(offset.Calculated);
        Assert.Null(offset.EpisodeOffset);
        Assert.Contains("different", offset.Reason);
    }

    [Fact]
    public void NonMikanInputIsNotApplicable()
    {
        MatchRequestInput input = new("Show", [new MatchFileInput("[01].mkv", 1)]);

        LocalEpisodeOffsetResult offset = EpisodeOffsetCalculator.Calculate(
            input,
            Result(new TmdbAiFileResult("[01].mkv", true, 1, 1, null)));

        Assert.False(offset.Applicable);
        Assert.False(offset.Calculated);
    }

    [Fact]
    public void MultipleTmdbSeasonsCreateNoCacheEvidence()
    {
        MatchRequestInput input = MikanInput("[Group] Show [01].mkv", "[Group] Show [02].mkv");
        TmdbAiMatchResult result = Result(
            new("[Group] Show [01].mkv", true, 1, 13, null),
            new("[Group] Show [02].mkv", true, 2, 14, null));

        LocalEpisodeOffsetResult offset = EpisodeOffsetCalculator.Calculate(input, result);

        Assert.False(offset.Calculated);
        Assert.Contains("multiple TMDB seasons", offset.Reason);
    }

    [Fact]
    public void CalculatesZeroOffsetForSeasonEpisodeFileNames()
    {
        string[] names = Enumerable.Range(51, 16)
            .Select(episode => $"ReZero kara Hajimeru Isekai Seikatsu 2016 S01E{episode}-[1080p][BDRIP][x265.OPUS].mkv")
            .ToArray();
        MatchRequestInput input = MikanInput(names);
        TmdbAiMatchResult result = Result(input.Files
            .Select((file, index) => new TmdbAiFileResult(file.Name, true, 1, 51 + index, null))
            .ToArray());

        LocalEpisodeOffsetResult offset = EpisodeOffsetCalculator.Calculate(input, result);

        Assert.True(offset.Calculated);
        Assert.Equal(0, offset.EpisodeOffset);
        Assert.Equal(16, offset.MatchedCandidateCount);
    }

    private static MatchRequestInput MikanInput(params string[] names) =>
        new("Show", names.Select(name => new MatchFileInput(name, 1)).ToArray(), IsMikanRssSource: true);

    private static TmdbAiMatchResult Result(params TmdbAiFileResult[] files) =>
        new(true, 12345, files, null);
}
