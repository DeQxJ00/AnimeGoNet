using System.Text.Json;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class AiMetadataTaskResolverTests
{
    [Fact]
    public async Task SendsOneTaskPromptAndValidatesSeriesSeasonEpisodeTogether()
    {
        var matcher = new FakeMatcher();
        var tmdb = new FakeTmdbClient();
        var resolver = new AiMetadataTaskResolver(
            matcher,
            new AiMetadataResultValidator(tmdb),
            new AiPublicationEvidenceResolver(
                null,
                new AiMatchingOptions { UseBangumiPubDateFirst = false }));
        var claim = new MetadataTaskClaim(
            "route-secret-run",
            "route-secret-task",
            "Task title",
            3951,
            7,
            547888,
            1,
            "route-secret-lease",
            AniDbAnimeId: 999,
            ImdbTitleId: "tt1234567",
            SourceAdapter: "route-secret-adapter",
            SourcePublishedAtRaw: "route-secret-raw-date",
            TorrentFileCount: 2);
        MetadataTaskFileProjection[] files =
        [
            new("video", "episode-04.mkv", 1234, "4", "4"),
            new("subtitle", "episode-04.zh-Hans.ass", 45, "4", "4"),
        ];

        var result = await resolver.ResolveAsync(claim, files, 72517, 2);

        Assert.True(result.IsSuccess);
        Assert.Single(matcher.Requests);
        var input = Assert.Single(matcher.Requests);
        Assert.Equal("Task title", input.Title);
        var file = Assert.Single(input.Files);
        Assert.Equal("episode-04.mkv", file.Name);
        Assert.Equal(1234, file.SizeBytes);
        var serializedInput = JsonSerializer.Serialize(input);
        Assert.DoesNotContain("route-secret", serializedInput, StringComparison.Ordinal);
        Assert.Equal(72517, result.Value!.Series.Id);
        Assert.Equal(2, Assert.Single(result.Value.Files).Season.SeasonNumber);
        Assert.Equal(4, Assert.Single(result.Value.Files).Episode!.EpisodeNumber);
        Assert.Equal("fake-model", result.Usage!.Model);
        Assert.Equal(42, result.Usage.TotalTokens);
    }

    [Fact]
    public void AiInputTypesExposeOnlyApprovedEvidenceFields()
    {
        Assert.Equal(
            [
                "Title",
                "Files",
                "BangumiSubjectId",
                "AniDbAnimeId",
                "ImdbTitleId",
                "TorrentFileCount",
                "PublishedAt",
                "BangumiEpisodeCandidate",
                "UseBangumiPubDateFirst",
                "PromptTemplateOverride",
                "PromptFeaturesOverride",
                "DebugIdentity",
                "DebugPreAiContext",
            ],
            typeof(AiMetadataMatchInput).GetProperties().Select(property => property.Name));
        Assert.Equal(
            ["Name", "SizeBytes"],
            typeof(AiMetadataFileInput).GetProperties().Select(property => property.Name));
        Assert.Equal(
            ["TmdbMcp", "BangumiMcp", "AniDbLookup", "BangumiPubDateFirst", "ImdbLookup"],
            typeof(AiMetadataPromptFeatures).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public async Task NoVideoIsNotApplicableAndDoesNotCallMatcher()
    {
        var matcher = new FakeMatcher();
        var resolver = new AiMetadataTaskResolver(
            matcher,
            new AiMetadataResultValidator(new FakeTmdbClient()),
            new AiPublicationEvidenceResolver(
                null,
                new AiMatchingOptions { UseBangumiPubDateFirst = false }));
        var claim = new MetadataTaskClaim(
            "run",
            "task",
            "Task title",
            null,
            null,
            null,
            1,
            "lease");

        var result = await resolver.ResolveAsync(
            claim,
            [new MetadataTaskFileProjection("subtitle", "only.ass", 45, null, null)]);

        Assert.False(result.IsApplicable);
        Assert.Equal("ai_video_files_missing", result.Failure!.Code);
        Assert.Empty(matcher.Requests);
    }

    [Fact]
    public async Task DifferentSeriesIsFullyValidatedAndReturnedAsManualReviewProposal()
    {
        var matcher = new FakeMatcher { TmdbId = 90001 };
        var resolver = new AiMetadataTaskResolver(
            matcher,
            new AiMetadataResultValidator(new FakeTmdbClient()),
            new AiPublicationEvidenceResolver(
                null,
                new AiMatchingOptions { UseBangumiPubDateFirst = false }));
        var claim = new MetadataTaskClaim(
            "run", "task", "同一作品的另一语言标题", 1, 2, 3, 1, "lease",
            TorrentFileCount: 1);

        var result = await resolver.ResolveAsync(
            claim,
            [new MetadataTaskFileProjection("video", "episode-04.mkv", 1234, "4", "4")],
            expectedSeriesId: 72517,
            expectedSeasonNumber: 2);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "ai_tmdb_multilingual_series_conflict_review_required",
            result.Failure!.Code);
        Assert.True(result.Failure.TmdbAccessConfirmed);
        var proposal = Assert.IsType<ValidatedAiMetadataMatch>(result.SeriesChangeProposal);
        Assert.Equal(90001, proposal.Series.Id);
        Assert.Equal(4, Assert.Single(proposal.Files).Episode!.EpisodeNumber);
    }

    private sealed class FakeMatcher : IAiMetadataMatcher
    {
        public List<AiMetadataMatchInput> Requests { get; } = [];
        public int TmdbId { get; init; } = 72517;

        public Task<AiMetadataMatchResponse> MatchAsync(
            AiMetadataMatchInput input,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(input);
            return Task.FromResult(new AiMetadataMatchResponse(
                new AiMetadataMatchCandidate(
                    true,
                    TmdbId,
                    input.Files.Select(file => new AiMetadataFileCandidate(
                        file.Name,
                        true,
                        2,
                        4,
                        null)).ToArray(),
                    null),
                new AiMetadataProviderUsage("fake-model", 30, 12, 42, 1, 0)));
        }
    }

    private sealed class FakeTmdbClient : ITmdbClient
    {
        private static readonly TmdbSeries Series =
            new(72517, "来自深渊", "メイドインアビス", null);
        private static readonly TmdbSeason Season =
            new(204984, 72517, 2, "Season 2", new DateOnly(2022, 7, 6), 12);
        private static readonly TmdbSeries AlternateSeries =
            new(90001, "Alternative title", "別名", null);
        private static readonly TmdbSeason AlternateSeason =
            new(900012, 90001, 2, "Season 2", new DateOnly(2022, 7, 6), 12);

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(seriesId switch
            {
                72517 => Series,
                90001 => AlternateSeries,
                _ => null,
            });

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(seriesId switch
            {
                72517 => new TmdbSeriesDetails(Series, [Season]),
                90001 => new TmdbSeriesDetails(AlternateSeries, [AlternateSeason]),
                _ => null,
            });

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(seriesId switch
            {
                72517 when seasonNumber == 2 => Season,
                90001 when seasonNumber == 2 => AlternateSeason,
                _ => null,
            });

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbEpisode?>(
                (seriesId == Series.Id || seriesId == AlternateSeries.Id)
                    && seasonNumber == 2 && episodeNumber == 4
                    ? new TmdbEpisode(300004, seriesId, seasonNumber, episodeNumber, "Episode 4", null)
                    : null);
    }
}
