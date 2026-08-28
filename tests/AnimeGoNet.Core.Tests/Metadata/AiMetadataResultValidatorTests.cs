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
                new("f0001", true, 2, 3, null),
                new("f0002", false, 2, null, "NCOP belongs in Other."),
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
    public async Task ValidatesMatchedExtrasWithoutRequestingTmdbEpisode()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(new AiMetadataFileInput("Show/NCOP.mkv", 10));
        var candidate = new AiMetadataMatchCandidate(
            true,
            42,
            [new(
                "f0001",
                true,
                2,
                AiMetadataFileCandidate.ExtrasEpisodeSentinel,
                null)],
            null);

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.True(result.IsSuccess);
        var file = Assert.Single(result.Value!.Files);
        Assert.True(file.IsExtra);
        Assert.False(file.IsEpisode);
        Assert.Equal(1, tmdb.SeriesCalls);
        Assert.Equal(1, tmdb.SeasonCalls);
        Assert.Equal(0, tmdb.EpisodeCalls);
    }

    [Fact]
    public async Task ValidatesUnmatchedExtrasWithoutRequestingTmdbEpisode()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(new AiMetadataFileInput("Show/Summary.mkv", 10));
        var candidate = new AiMetadataMatchCandidate(
            true,
            42,
            [new(
                "f0001",
                false,
                2,
                AiMetadataFileCandidate.ExtrasEpisodeSentinel,
                "Summary belongs in Extras.")],
            null);

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.True(result.IsSuccess);
        var file = Assert.Single(result.Value!.Files);
        Assert.True(file.IsExtra);
        Assert.Equal("Summary belongs in Extras.", file.OtherReason);
        Assert.Equal(0, tmdb.EpisodeCalls);
    }

    [Fact]
    public async Task ReZeroCopiedTitleFileNameIsIgnoredAndOriginalInputIdentityIsPreserved()
    {
        var tmdb = new FakeTmdbClient();
        const string originalName = "[ANi] Re：從零開始的異世界生活 第四季 - 12 [1080P][Baha][WEB-DL][AAC AVC][CHT].mp4";
        var input = Input(new AiMetadataFileInput(originalName, 289_517_816));
        var candidate = Success(1, 78);

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
                "f0001",
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
    public async Task AcceptsReorderedFileIdsAndRestoresOriginalInputOrder()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(
            new AiMetadataFileInput("01.mkv", 100),
            new AiMetadataFileInput("02.mkv", 100));
        var candidate = new AiMetadataMatchCandidate(
            true,
            42,
            [
                new("f0002", true, 1, 2, null),
                new("f0001", true, 1, 1, null),
            ],
            null);

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.True(result.IsSuccess);
        Assert.Equal("01.mkv", result.Value!.Files[0].Input.Name);
        Assert.Equal(1, result.Value.Files[0].Episode!.EpisodeNumber);
        Assert.Equal("02.mkv", result.Value.Files[1].Input.Name);
        Assert.Equal(2, result.Value.Files[1].Episode!.EpisodeNumber);
    }

    [Fact]
    public void RejectsUnknownFileId()
    {
        var input = Input(
            new AiMetadataFileInput("[AI-Raws] Nadesico 01 [1080p].mkv", 100),
            new AiMetadataFileInput("[AI-Raws] 劇場版 機動戦艦ナデシコ 特別先行編『それから』 [1080p].mkv", 100),
            new AiMetadataFileInput("[AI-Raws] Nadesico 03 [1080p].mkv", 100));
        var candidate = Candidate("f0001", "unknown", "f0003");

        var failure = AiMetadataResultValidator.ValidateStructure(input, candidate);

        Assert.Equal("ai_file_id_unknown", failure!.Code);
    }

    [Fact]
    public void AcceptsEveryFileIdExactlyOnceRegardlessOfOutputOrder()
    {
        var input = Input(
            new AiMetadataFileInput("[AI-Raws] Nadesico 01 『Part A』 [1080p].mkv", 100),
            new AiMetadataFileInput("[AI-Raws] Nadesico 02 『Part B』 [1080p].mkv", 100),
            new AiMetadataFileInput("[AI-Raws] Nadesico 03 [1080p].mkv", 100));
        var candidate = Candidate("f0003", "f0001", "f0002");

        var failure = AiMetadataResultValidator.ValidateStructure(
            input,
            candidate);

        Assert.Null(failure);
    }

    [Fact]
    public void RejectsDuplicateFileId()
    {
        var input = Input(
            new AiMetadataFileInput("Show 01 『A』 [1080p].mkv", 100),
            new AiMetadataFileInput("Show 02 『B』 [1080p].mkv", 100),
            new AiMetadataFileInput("Show 03 『C』 [1080p].mkv", 100),
            new AiMetadataFileInput("Show 04 [1080p].mkv", 100));
        var candidate = Candidate("f0001", "f0002", "f0002", "f0004");

        var failure = AiMetadataResultValidator.ValidateStructure(input, candidate);

        Assert.Equal("ai_file_id_duplicate", failure!.Code);
    }

    [Fact]
    public void RejectsMissingFileId()
    {
        var input = Input(
            new AiMetadataFileInput("Show 01 『A』 [1080p].mkv", 100),
            new AiMetadataFileInput("Show 02 『B』 [1080p].mkv", 100),
            new AiMetadataFileInput("Show 03 [1080p].mkv", 100));
        var candidate = Candidate("f0001", "", "f0003");

        var failure = AiMetadataResultValidator.ValidateStructure(input, candidate);

        Assert.Equal("ai_file_id_missing", failure!.Code);
    }

    [Fact]
    public void LegacyFuzzyToleranceDoesNotPermitUnknownFileId()
    {
        var input = Input(
            new AiMetadataFileInput("Show 01 『A』 [1080p].mkv", 100),
            new AiMetadataFileInput("Show 02 [1080p].mkv", 100));
        var candidate = Candidate("filename.mkv", "f0002");

        var failure = AiMetadataResultValidator.ValidateStructure(
            input,
            candidate,
            fileIdentityFuzzyMatchLimit: 0);

        Assert.Equal("ai_file_id_unknown", failure!.Code);
    }

    [Fact]
    public void RejectsFileCountMismatch()
    {
        var input = Input(
            new AiMetadataFileInput("Show Episode 01 [1080p].mkv", 100),
            new AiMetadataFileInput("Show Episode 02 [1080p].mkv", 100));
        var candidate = Candidate("f0001");

        var failure = AiMetadataResultValidator.ValidateStructure(input, candidate);

        Assert.Equal("ai_file_count_mismatch", failure!.Code);
    }

    [Fact]
    public void RejectsGeneratedFileIdOutsideInputSet()
    {
        var input = Input(
            new AiMetadataFileInput("Show 01 『A』 [1080p].mkv", 100),
            new AiMetadataFileInput("Show 02 『B』 [1080p].mkv", 100));
        var candidate = Candidate("f0001", "f0003");

        var failure = AiMetadataResultValidator.ValidateStructure(input, candidate);

        Assert.Equal("ai_file_id_unknown", failure!.Code);
    }

    [Fact]
    public async Task RejectsSeasonZeroBeforeTmdbAccess()
    {
        var tmdb = new FakeTmdbClient();
        var input = Input(new AiMetadataFileInput("OVA.mkv", 100));
        var candidate = new AiMetadataMatchCandidate(
            true,
            42,
            [new("f0001", false, 0, null, "Special")],
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
            Success(1, 1));

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
            Success(1, 1));

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
                new("f0001", true, 1, 1, null),
                new("f0002", true, 1, 1, null),
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
            Success(2, 1),
            expectedSeriesId: 99,
            expectedSeasonNumber: 2);
        var changedSeason = await validator.ValidateAsync(
            input,
            Success(2, 1),
            expectedSeriesId: 42,
            expectedSeasonNumber: 1);

        Assert.Equal(MetadataFailureKind.Ambiguous, changedSeries.Failure!.Kind);
        Assert.Equal("ai_tmdb_series_candidate_conflict", changedSeries.Failure.Code);
        Assert.Equal("ai_tmdb_season_changed", changedSeason.Failure!.Code);
    }

    [Fact]
    public async Task RejectsTmdbIdentityMismatch()
    {
        var tmdb = new FakeTmdbClient { EpisodeIdentityMismatch = true };
        var input = Input(new AiMetadataFileInput("01.mkv", 100));

        var result = await new AiMetadataResultValidator(tmdb).ValidateAsync(
            input,
            Success(1, 1));

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
            Success(1, 1));

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
            Success(1, 1));

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
            [new("f0001", false, 1, null, "Episode is ambiguous.")],
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
            [new("f0001", false, null, null, "Season is unknown.")],
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
            [new("f0001", false, null, null, "Season is unknown.")],
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

    private static AiMetadataMatchCandidate Success(int season, int episode) =>
        new(
            true,
            42,
            [new("f0001", true, season, episode, null)],
            null);

    private static AiMetadataMatchCandidate Candidate(params string[] fileIds) =>
        new(
            true,
            42,
            fileIds.Select((fileId, index) =>
                new AiMetadataFileCandidate(fileId, true, 1, index + 1, null)).ToArray(),
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
