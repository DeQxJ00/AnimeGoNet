using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;
using System.Globalization;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class U2WholeTorrentEpisodeGateTests
{
    [Fact]
    public void CompleteSeasonSetPassesWithoutAiAndKeepsUnnumberedExtra()
    {
        var claim = Claim("u2", [Video("e1", "Show 01.mkv", 1), Video("e2", "Show 02.mkv", 2),
            Video("e3", "Show 03.mkv", 3), Video("ncop", "Show NCOP.mkv", null)]);

        var result = U2WholeTorrentEpisodeGate.Evaluate(claim, Season(1, 2, 3));

        Assert.True(result.IsApplicable);
        Assert.False(result.RequiresAi);
        Assert.Contains("ncop", result.ExplicitExtraFileIds);
    }

    [Theory]
    [MemberData(nameof(AiCases))]
    public void NonExactOrConflictingTorrentRequiresAi(
        IReadOnlyList<MetadataTaskFileProjection> files,
        string expectedReason)
    {
        var result = U2WholeTorrentEpisodeGate.Evaluate(Claim("u2", files), Season(1, 2, 3));

        Assert.True(result.RequiresAi);
        Assert.Equal(expectedReason, result.Reason);
    }

    public static TheoryData<IReadOnlyList<MetadataTaskFileProjection>, string> AiCases() => new()
    {
        { [Video("e1", "Show 01.mkv", 1)], "u2_single_or_non_season_torrent" },
        { [Video("e1", "Show 01.mkv", 1), Video("e2", "Show 02.mkv", 2)], "u2_torrent_not_complete_tmdb_season" },
        { [Video("e1", "Show 01.mkv", 1), Video("e2", "Show 02.mkv", 2), Video("e4", "Show 04.mkv", 4)], "u2_torrent_not_complete_tmdb_season" },
        { [Video("e1", "Show 01.mkv", 1), Video("e1v2", "Show 01v2.mkv", 1), Video("e2", "Show 02.mkv", 2)], "u2_duplicate_episode_candidate" },
        { [Video("s1e1", "S1/Show 01.mkv", 1, 1), Video("s2e1", "S2/Show 01.mkv", 1, 2)], "u2_duplicate_episode_candidate" },
        { [Video("e1", "Show 01.mkv", 1), Video("ncop", "Show NCOP 01.mkv", 1), Video("e2", "Show 02.mkv", 2)], "u2_duplicate_episode_candidate" },
        { [Video("e1", "Show 01.mkv", 1), Video("unknown", "Show unknown.mkv", null)], "u2_main_video_episode_not_parsed" },
    };

    [Fact]
    public void SeasonZeroNeverParticipatesInRegularSeasonValidation()
    {
        var claim = Claim("u2", [Video("e1", "Show 01.mkv", 1)]) with
        {
            TmdbSeasonNumber = 0,
        };

        var result = U2WholeTorrentEpisodeGate.Evaluate(claim, Season(1, 2, 3) with { SeasonNumber = 0 });

        Assert.True(result.RequiresAi);
        Assert.Equal("u2_regular_season_not_verified", result.Reason);
    }

    [Fact]
    public void MikanIsNotAffected()
    {
        var result = U2WholeTorrentEpisodeGate.Evaluate(
            Claim("mikan", [Video("e1", "Show 01.mkv", 1)]),
            Season(1, 2, 3));

        Assert.False(result.IsApplicable);
        Assert.False(result.RequiresAi);
    }

    [Fact]
    public void U2AiMayLeaveOnlyExplicitExtrasUnmatched()
    {
        var claim = Claim("u2", []).Resolution;
        var season = Season(1, 2, 3);
        var files = new[]
        {
            new ValidatedAiMetadataFile(
                new AiMetadataFileInput("Show 01.mkv", 1), season,
                new TmdbEpisode(1, 100, 1, 1, "E1", null), null),
            new ValidatedAiMetadataFile(
                new AiMetadataFileInput("Show NCOP.mkv", 1), season, null, "creditless opening"),
        };

        Assert.Null(U2AiFilePolicy.Validate(claim, files));

        ValidatedAiMetadataFile[] bad =
        [
            files[0],
            new ValidatedAiMetadataFile(
                new AiMetadataFileInput("Show unknown.mkv", 1), season, null, "unknown"),
        ];
        Assert.Equal("ai_u2_main_video_unmatched", U2AiFilePolicy.Validate(claim, bad)?.Code);
    }

    private static MetadataEpisodeTaskClaim Claim(
        string adapter,
        IReadOnlyList<MetadataTaskFileProjection> files) =>
        new(
            new MetadataTaskClaim(
                "run", "task", "title", null, null, null, 1, "lease",
                Files: files, SourceAdapter: adapter, TorrentFileCount: files.Count),
            100,
            1,
            files);

    private static MetadataTaskFileProjection Video(
        string id,
        string path,
        int? episode,
        int? season = null) =>
        new(
            id,
            path,
            1,
            episode?.ToString(CultureInfo.InvariantCulture),
            episode?.ToString(CultureInfo.InvariantCulture),
            TmdbSeasonNumber: season);

    private static TmdbSeason Season(params int[] episodes) =>
        new(
            10,
            100,
            1,
            "Season 1",
            null,
            episodes.Length,
            Episodes: episodes.Select(number =>
                new TmdbEpisode(number, 100, 1, number, $"E{number}", null)).ToArray());
}
