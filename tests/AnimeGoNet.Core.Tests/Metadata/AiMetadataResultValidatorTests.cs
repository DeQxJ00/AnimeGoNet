using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class AiMetadataResultValidatorTests
{
    [Fact]
    public async Task ValidatesSeriesSeasonEpisodesAndKnownSeasonOther()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(
            new AiMetadataFileInput("Show/01.mkv", 100),
            new AiMetadataFileInput("Show/NCOP.mkv", 10));
        var candidate = new AiMetadataMatchCandidate(
            true,
            42,
            [
                new("Show/01.mkv", true, 2, 3, null),
                new("Show/NCOP.mkv", false, 2, null, "NCOP belongs in Other."),
            ],
            null);

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.True(result.IsSuccess);
        Assert.Equal("Canonical", result.Value!.Series.Name);
        Assert.Equal(2, result.Value.Files.Count);
        Assert.Equal(3, result.Value.Files[0].Episode!.EpisodeNumber);
        Assert.False(result.Value.Files[1].IsEpisode);
        Assert.Equal("NCOP belongs in Other.", result.Value.Files[1].OtherReason);
        Assert.Equal(1, tmdb.SeriesCalls);
        Assert.Equal(1, tmdb.SeasonCalls);
        Assert.Equal(1, tmdb.EpisodeCalls);
    }

    [Fact]
    public async Task ReZeroCopiedTitleFileNameIsIgnoredAndOriginalInputIdentityIsPreserved()
    {
        var tmdb = new FakeTmdbClient();
        const string originalName = "[ANi] Re：從零開始的異世界生活 第四季 - 12 [1080P][Baha][WEB-DL][AAC AVC][CHT].mp4";
        const string echoedName = "[ANi] Re：從零開始的異世界生活 第四季 - 12 [1080P][Baha][WEB-DL][AAC AVC][CHT][MP4]";
        var input = Input(new AiMetadataFileInput(originalName, 289_517_816));
        var candidate = Success(echoedName, 1, 78);

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalName, Assert.Single(result.Value!.Files).Input.Name);
        Assert.Equal(78, result.Value.Files[0].Episode!.EpisodeNumber);
        Assert.Equal(3, tmdb.TotalCalls);
    }

    [Fact]
    public async Task MatchedExplanatoryReasonsAreIgnoredAndTmdbIsStillFullyValidated()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(new AiMetadataFileInput("Show/78.mkv", 100));
        var candidate = new AiMetadataMatchCandidate(
            true,
            42,
            [new(
                "Show/78.mkv",
                true,
                1,
                78,
                "The model included a redundant explanation for this match.")],
            "The model included a redundant top-level explanation.");

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.True(result.IsSuccess);
        Assert.Equal(78, Assert.Single(result.Value!.Files).Episode!.EpisodeNumber);
        Assert.Equal(3, tmdb.TotalCalls);
    }

    [Fact]
    public async Task RejectsMultiFileOrderMismatchBeforeTmdbAccess()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(
            new AiMetadataFileInput("01.mkv", 100),
            new AiMetadataFileInput("02.mkv", 100));
        var candidate = new AiMetadataMatchCandidate(
            true,
            42,
            [
                new("02.mkv", true, 1, 2, null),
                new("01.mkv", true, 1, 1, null),
            ],
            null);

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.Equal("ai_file_identity_mismatch", result.Failure!.Code);
        Assert.Equal(0, tmdb.TotalCalls);
    }

    [Fact]
    public async Task RejectsSeasonZeroBeforeTmdbAccess()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(new AiMetadataFileInput("OVA.mkv", 100));
        var candidate = new AiMetadataMatchCandidate(
            true,
            42,
            [new("OVA.mkv", false, 0, null, "Special")],
            null);

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.Equal("ai_file_resolution_incomplete", result.Failure!.Code);
        Assert.Equal(0, tmdb.TotalCalls);
    }

    [Fact]
    public async Task RejectsHostAbsolutePathBeforeTmdbAccess()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(new AiMetadataFileInput(@"E:\private\01.mkv", 100));

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(
            input,
            Success(@"E:\private\01.mkv", 1, 1));

        Assert.Equal("ai_metadata_input_invalid", result.Failure!.Code);
        Assert.Equal(0, tmdb.TotalCalls);
    }

    [Fact]
    public async Task RejectsForgedBangumiPubdateGateBeforeTmdbAccess()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(new AiMetadataFileInput("01.mkv", 100)) with
        {
            UseBangumiPubDateFirst = true,
        };

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(
            input,
            Success("01.mkv", 1, 1));

        Assert.Equal("ai_metadata_input_invalid", result.Failure!.Code);
        Assert.Equal(0, tmdb.TotalCalls);
    }

    [Fact]
    public async Task RejectsDuplicateEpisodeTargets()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(
            new AiMetadataFileInput("01a.mkv", 100),
            new AiMetadataFileInput("01b.mkv", 100));
        var candidate = new AiMetadataMatchCandidate(
            true,
            42,
            [
                new("01a.mkv", true, 1, 1, null),
                new("01b.mkv", true, 1, 1, null),
            ],
            null);

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.Equal(MetadataFailureKind.Ambiguous, result.Failure!.Kind);
        Assert.Equal("ai_duplicate_episode_target", result.Failure.Code);
    }

    [Fact]
    public async Task EpisodeAiCannotChangeConfirmedSeriesOrSeason()
    {
        var input = Input(new AiMetadataFileInput("01.mkv", 100));
        var validator = new AiMetadataResultValidator(new FakeTmdbClient());

        var changedSeries = await validator.ValidateAsync(
            input,
            Success("01.mkv", 2, 1),
            expectedSeriesId: 99,
            expectedSeasonNumber: 2);
        var changedSeason = await validator.ValidateAsync(
            input,
            Success("01.mkv", 2, 1),
            expectedSeriesId: 42,
            expectedSeasonNumber: 1);

        Assert.Equal("ai_tmdb_series_changed", changedSeries.Failure!.Code);
        Assert.Equal("ai_tmdb_season_changed", changedSeason.Failure!.Code);
    }

    [Fact]
    public async Task RejectsTmdbIdentityMismatch()
    {
        var tmdb = new FakeTmdbClient { EpisodeIdentityMismatch = true };
        var input = Input(new AiMetadataFileInput("01.mkv", 100));

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(
            input,
            Success("01.mkv", 1, 1));

        Assert.Equal(MetadataFailureKind.Protocol, result.Failure!.Kind);
        Assert.Equal("ai_tmdb_episode_identity_mismatch", result.Failure.Code);
    }

    [Fact]
    public async Task PreservesTmdbNetworkFailureClassification()
    {
        var tmdb = new FakeTmdbClient
        {
            Failure = new TmdbClientException(
                MetadataFailureKind.Network,
                "tmdb_network_error",
                tmdbAccessConfirmed: false),
        };
        var input = Input(new AiMetadataFileInput("01.mkv", 100));

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(
            input,
            Success("01.mkv", 1, 1));

        Assert.Equal(MetadataFailureKind.Network, result.Failure!.Kind);
        Assert.Equal("tmdb_network_error", result.Failure.Code);
        Assert.False(result.Failure.TmdbAccessConfirmed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("imdb-123")]
    [InlineData("tt123")]
    public async Task RejectsInvalidOptionalImdbId(string? imdbId)
    {
        if (imdbId is null)
        {
            imdbId = " ";
        }

        var tmdb = new FakeTmdbClient();
        var input = Input(new AiMetadataFileInput("01.mkv", 100)) with { ImdbTitleId = imdbId };

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(
            input,
            Success("01.mkv", 1, 1));

        Assert.Equal("ai_metadata_input_invalid", result.Failure!.Code);
        Assert.Equal(0, tmdb.TotalCalls);
    }

    [Fact]
    public async Task AcceptsStructurallyCompleteNoMatchWithoutTmdbAccess()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(new AiMetadataFileInput("unknown.mkv", 100));
        var candidate = new AiMetadataMatchCandidate(
            false,
            null,
            [new("unknown.mkv", false, 1, null, "Episode is ambiguous.")],
            "Task is ambiguous.");

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.Equal(MetadataFailureKind.SemanticNoMatch, result.Failure!.Kind);
        Assert.Equal("ai_metadata_not_matched", result.Failure.Code);
        Assert.Equal(0, tmdb.TotalCalls);
    }

    [Fact]
    public async Task AcceptsNoMatchWithUnknownSeasonWithoutTmdbAccess()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(new AiMetadataFileInput("unknown.mkv", 100));
        var candidate = new AiMetadataMatchCandidate(
            false,
            42,
            [new("unknown.mkv", false, null, null, "Season is unknown.")],
            "Task cannot be completely arranged.");

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.Equal("ai_metadata_not_matched", result.Failure!.Code);
        Assert.Equal(0, tmdb.TotalCalls);
    }

    [Fact]
    public async Task TopLevelMatchRequiresKnownSeasonForEveryOtherFile()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(new AiMetadataFileInput("unknown.mkv", 100));
        var candidate = new AiMetadataMatchCandidate(
            true,
            42,
            [new("unknown.mkv", false, null, null, "Season is unknown.")],
            null);

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.Equal("ai_other_season_missing", result.Failure!.Code);
        Assert.Equal(0, tmdb.TotalCalls);
    }

    private static AiMetadataMatchInput Input(params AiMetadataFileInput[] files) =>
        new(
            "Task title",
            files,
            BangumiSubjectId: null,
            AniDbAnimeId: null,
            ImdbTitleId: null,
            TorrentFileCount: files.Length,
            PublishedAt: null,
            BangumiEpisodeCandidate: null,
            UseBangumiPubDateFirst: false);

    private static AiMetadataMatchCandidate Success(string name, int season, int episode) =>
        new(
            true,
            42,
            [new(name, true, season, episode, null)],
            null);

    private sealed class FakeTmdbClient : ITmdbClient
    {
        public int SeriesCalls { get; private set; }

        public int SeasonCalls { get; private set; }

        public int EpisodeCalls { get; private set; }

        public int TotalCalls => SeriesCalls + SeasonCalls + EpisodeCalls;

        public bool EpisodeIdentityMismatch { get; init; }

        public TmdbClientException? Failure { get; init; }

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbSeries>>([]);

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(new TmdbSeries(seriesId, "Canonical", "Original", null));

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default)
        {
            SeriesCalls++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult<TmdbSeriesDetails?>(new TmdbSeriesDetails(
                new TmdbSeries(seriesId, "Canonical", "Original", null),
                [new TmdbSeason(20, seriesId, 1, "Season 1", null, 12)]));
        }

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default)
        {
            SeasonCalls++;
            return Task.FromResult<TmdbSeason?>(
                new TmdbSeason(20 + seasonNumber, seriesId, seasonNumber, $"Season {seasonNumber}", null, 12));
        }

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default)
        {
            EpisodeCalls++;
            return Task.FromResult<TmdbEpisode?>(EpisodeIdentityMismatch
                ? new TmdbEpisode(30, seriesId + 1, seasonNumber, episodeNumber, "Wrong", null)
                : new TmdbEpisode(30, seriesId, seasonNumber, episodeNumber, "Episode", null));
        }
    }
}
