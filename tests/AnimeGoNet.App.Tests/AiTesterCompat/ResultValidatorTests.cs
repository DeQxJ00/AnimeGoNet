using AnimeGoNet.App.AiTesterCompat;

namespace AnimeGoNet.App.Tests.AiTesterCompat;

public sealed class ResultValidatorTests
{
    [Fact]
    public void AcceptsMinimalSuccessResultWithoutTitle()
    {
        string json = """
            {
              "matched": true,
              "tmdb_id": 12345,
              "files": [
                { "name": "01.mkv", "matched": true, "season": 1, "episode": 1, "reason": null }
              ],
              "reason": null
            }
            """;
        var input = new MatchRequestInput("任务", [new MatchFileInput("01.mkv", 1)]);

        (bool valid, string? error, _) = ResultValidator.Validate(json, input);

        Assert.True(valid, error);
    }

    [Fact]
    public void AcceptsBdTaskWithEpisodesAndOtherFilesAsTopLevelMatched()
    {
        var inputFiles = new List<MatchFileInput>();
        var responseFiles = new List<string>();
        int[] episodeOrder = [4, 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12];
        foreach (int episode in episodeOrder)
        {
            string name = $"{episode:00}.mkv";
            inputFiles.Add(new MatchFileInput(name, episode));
            responseFiles.Add($$"""    { "name": "{{name}}", "matched": true, "season": 1, "episode": {{episode}}, "reason": null }""");
        }

        string[] otherNames = ["Menu.mkv", "SP.mkv", "PV.mkv", "NCOP.mkv", "NCED.mkv", "Logo.mkv"];
        foreach (string name in otherNames)
        {
            inputFiles.Add(new MatchFileInput(name, 100));
            responseFiles.Add($$"""    { "name": "{{name}}", "matched": false, "season": 1, "episode": null, "reason": "{{name}} 是非正片文件，可放入 Season 1 Other。" }""");
        }

        string json = string.Join('\n', [
            "{",
            "  \"matched\": true,",
            "  \"tmdb_id\": 12345,",
            "  \"files\": [",
            string.Join(",\n", responseFiles),
            "  ],",
            "  \"reason\": null",
            "}"
        ]);
        var input = new MatchRequestInput("BD Box", inputFiles);

        (bool valid, string? error, _) = ResultValidator.Validate(json, input);

        Assert.True(valid, error);
    }

    [Fact]
    public void AcceptsTmdbEpisodeThatDiffersFromSourceEpisodeNumber()
    {
        string json = """
            {
              "matched": true,
              "tmdb_id": 12345,
              "files": [
                { "name": "第01话.mkv", "matched": true, "season": 2, "episode": 67, "reason": null }
              ],
              "reason": null
            }
            """;
        var input = new MatchRequestInput("跨站标题不同的续作", [new MatchFileInput("第01话.mkv", 1)], 100, 200, IsMikanRssSource: true);

        (bool valid, string? error, _) = ResultValidator.Validate(json, input);

        Assert.True(valid, error);
    }

    [Fact]
    public void AcceptsValidFailureResultWithPerFileReason()
    {
        string json = """
            {
              "matched": false,
              "tmdb_id": 12345,
              "files": [
                { "name": "01.mkv", "matched": true, "season": 1, "episode": 1, "reason": null },
                { "name": "SP.mkv", "matched": false, "season": null, "episode": null, "reason": "无法对应正篇Episode。" }
              ],
              "reason": "部分文件无法可靠确认。"
            }
            """;
        var input = new MatchRequestInput("任务", [new MatchFileInput("01.mkv", 1), new MatchFileInput("SP.mkv", 2)]);

        (bool valid, string? error, _) = ResultValidator.Validate(json, input);

        Assert.True(valid, error);
    }

    [Fact]
    public void IgnoresEchoedFileNameBecauseOriginalInputIdentityIsAuthoritative()
    {
        string json = """
            {
              "matched": true,
              "tmdb_id": 12345,
              "files": [
                { "name": "02.mkv", "matched": true, "season": 1, "episode": 2, "reason": null }
              ],
              "reason": null
            }
            """;
        var input = new MatchRequestInput("任务", [new MatchFileInput("01.mkv", 1)]);

        (bool valid, string? error, _) = ResultValidator.Validate(json, input);

        Assert.True(valid, error);
    }

    [Fact]
    public void RejectsMismatchedEchoedNamesForMultiFileMapping()
    {
        string json = """
            {
              "matched": true,
              "tmdb_id": 12345,
              "files": [
                { "name": "02.mkv", "matched": true, "season": 1, "episode": 2, "reason": null },
                { "name": "01.mkv", "matched": true, "season": 1, "episode": 1, "reason": null }
              ],
              "reason": null
            }
            """;
        var input = new MatchRequestInput(
            "任务",
            [new MatchFileInput("01.mkv", 1), new MatchFileInput("02.mkv", 2)]);

        (bool valid, string? error, _) = ResultValidator.Validate(json, input);

        Assert.False(valid);
        Assert.Contains("multi-file mapping", error);
    }

    [Fact]
    public void RejectsMatchedFileWithoutSeasonEpisode()
    {
        string json = """
            {
              "matched": true,
              "tmdb_id": 12345,
              "files": [
                { "name": "01.mkv", "matched": true, "season": null, "episode": 1, "reason": null }
              ],
              "reason": null
            }
            """;

        (bool valid, string? error, _) = ResultValidator.Validate(json);

        Assert.False(valid);
        Assert.Contains("season", error);
    }

    [Fact]
    public void RejectsSeasonZeroEvenWhenFileIsUnmatched()
    {
        string json = """
            {
              "matched": false,
              "tmdb_id": 12345,
              "files": [
                { "name": "OVA.mkv", "matched": false, "season": 0, "episode": null, "reason": "特别篇不匹配正篇Episode。" }
              ],
              "reason": "部分文件无法可靠确认。"
            }
            """;

        (bool valid, string? error, _) = ResultValidator.Validate(json);

        Assert.False(valid);
        Assert.Contains("must not be 0", error);
    }

    [Fact]
    public void AcceptsUnmatchedSpecialFileWithKnownRegularSeasonAndNullEpisode()
    {
        string json = """
            {
              "matched": false,
              "tmdb_id": 12345,
              "files": [
                { "name": "NCOP.mkv", "matched": false, "season": 1, "episode": null, "reason": "NCOP无法可靠匹配到正篇Episode，保留到普通季度其他文件。" }
              ],
              "reason": "部分文件无法可靠确认。"
            }
            """;
        var input = new MatchRequestInput("任务", [new MatchFileInput("NCOP.mkv", 10)]);

        (bool valid, string? error, _) = ResultValidator.Validate(json, input);

        Assert.True(valid, error);
    }

    [Fact]
    public void AcceptsUnmatchedFileWithUnknownSeasonAndNullEpisode()
    {
        string json = """
            {
              "matched": false,
              "tmdb_id": 12345,
              "files": [
                { "name": "OVA.mkv", "matched": false, "season": null, "episode": null, "reason": "OVA无法可靠匹配到普通季度和Episode。" }
              ],
              "reason": "部分文件无法可靠确认。"
            }
            """;

        (bool valid, string? error, _) = ResultValidator.Validate(json);

        Assert.True(valid, error);
    }

    [Fact]
    public void RejectsLegacyResponseTitleField()
    {
        string json = """
            {
              "matched": true,
              "tmdb_id": 12345,
              "title": "TMDB名称",
              "files": [
                { "name": "01.mkv", "matched": true, "season": 1, "episode": 1, "reason": null }
              ],
              "reason": null
            }
            """;

        (bool valid, string? error, _) = ResultValidator.Validate(json);

        Assert.False(valid);
        Assert.Contains("Unexpected legacy response field 'title'", error);
    }

    [Fact]
    public void MikanResultDoesNotRequireEpisodeOffsetField()
    {
        string json = """
            {
              "matched": true,
              "tmdb_id": 12345,
              "files": [
                { "name": "[Group] Show [04].mkv", "matched": true, "season": 1, "episode": 8, "reason": null }
              ],
              "reason": null
            }
            """;

        var input = new MatchRequestInput("任务", [new MatchFileInput("[Group] Show [04].mkv", 1)], IsMikanRssSource: true);
        (bool valid, string? error, _) = ResultValidator.Validate(json, input);

        Assert.True(valid, error);
    }

    [Fact]
    public void RejectsModelProvidedEpisodeOffset()
    {
        string json = """
            {
              "matched": true,
              "tmdb_id": 12345,
              "episode_offset": 1,
              "files": [
                { "name": "[Group] Show [04].mkv", "matched": true, "season": 1, "episode": 8, "reason": null }
              ],
              "reason": null
            }
            """;
        var input = new MatchRequestInput("任务", [new MatchFileInput("[Group] Show [04].mkv", 1)], IsMikanRssSource: true);

        (bool valid, string? error, _) = ResultValidator.Validate(json, input);

        Assert.False(valid);
        Assert.Contains("Unexpected legacy response field 'episode_offset'", error);
    }

    [Fact]
    public void AcceptsSingleUniformOffsetAcrossMatchedFiles()
    {
        string json = """
            {
              "matched": true,
              "tmdb_id": 12345,
              "files": [
                { "name": "[Group] Show [01].mkv", "matched": true, "season": 2, "episode": 13, "reason": null },
                { "name": "[Group] Show [02].mkv", "matched": true, "season": 2, "episode": 14, "reason": null }
              ],
              "reason": null
            }
            """;
        var input = new MatchRequestInput("任务", [
            new MatchFileInput("[Group] Show [01].mkv", 1),
            new MatchFileInput("[Group] Show [02].mkv", 2)
        ], IsMikanRssSource: true);

        (bool valid, string? error, _) = ResultValidator.Validate(json, input);

        Assert.True(valid, error);
    }

    [Fact]
    public void AcceptsExtrasEpisodeValue()
    {
        const string json = """
            {
              "matched": true,
              "tmdb_id": 12345,
              "files": [
                { "name": "NCOP.mkv", "matched": true, "season": 1, "episode": "Extras", "reason": null }
              ],
              "reason": null
            }
            """;
        var input = new MatchRequestInput(
            "任务",
            [new MatchFileInput("NCOP.mkv", 1)],
            IsMikanRssSource: false);

        (bool valid, string? error, TmdbAiMatchResult? result) = ResultValidator.Validate(json, input);

        Assert.True(valid, error);
        Assert.True(Assert.Single(result!.Files!).IsExtras);
    }

    [Fact]
    public void AcceptsUnmatchedExtrasAndNumericStringEpisodeValues()
    {
        const string json = """
            {
              "matched": true,
              "tmdb_id": 12345,
              "files": [
                { "name": "01.mkv", "matched": true, "season": 1, "episode": "1", "reason": null },
                { "name": "Summary.mkv", "matched": false, "season": 1, "episode": "Extras", "reason": "Summary" }
              ],
              "reason": null
            }
            """;
        var input = new MatchRequestInput(
            "任务",
            [new MatchFileInput("01.mkv", 1), new MatchFileInput("Summary.mkv", 2)],
            IsMikanRssSource: false);

        (bool valid, string? error, TmdbAiMatchResult? result) = ResultValidator.Validate(json, input);

        Assert.True(valid, error);
        Assert.Equal(1, result!.Files![0].Episode);
        Assert.True(result.Files[1].IsExtras);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(-2, 2)]
    public void AcceptsZeroAndNegativeMikanOffsets(int offset, int tmdbEpisode)
    {
        string json = $$"""
            {
              "matched": true,
              "tmdb_id": 12345,
              "files": [
                { "name": "[Group] Show [04].mkv", "matched": true, "season": 1, "episode": {{tmdbEpisode}}, "reason": null }
              ],
              "reason": null
            }
            """;
        var input = new MatchRequestInput("任务", [new MatchFileInput("[Group] Show [04].mkv", 1)], IsMikanRssSource: true);

        (bool valid, string? error, TmdbAiMatchResult? result) = ResultValidator.Validate(json, input);

        Assert.True(valid, error);
        LocalEpisodeOffsetResult local = EpisodeOffsetCalculator.Calculate(input, result!);
        Assert.True(local.Calculated);
        Assert.Equal(offset, local.EpisodeOffset);
    }

    [Fact]
    public void RejectsModelProvidedEpisodeOffsetEvenWhenNull()
    {
        const string json = """
            {
              "matched": false,
              "tmdb_id": null,
              "episode_offset": null,
              "files": [],
              "reason": "未匹配"
            }
            """;

        (bool valid, string? error, _) = ResultValidator.Validate(json);

        Assert.False(valid);
        Assert.Contains("Unexpected legacy response field 'episode_offset'", error);
    }
}
