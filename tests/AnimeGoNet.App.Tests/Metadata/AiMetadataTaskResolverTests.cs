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
            "run",
            "task",
            "Task title",
            3951,
            7,
            547888,
            1,
            "lease",
            AniDbAnimeId: 999,
            ImdbTitleId: "tt1234567",
            SourceAdapter: "mikan",
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
        Assert.Equal(72517, result.Value!.Series.Id);
        Assert.Equal(2, Assert.Single(result.Value.Files).Season.SeasonNumber);
        Assert.Equal(4, Assert.Single(result.Value.Files).Episode!.EpisodeNumber);
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

    private sealed class FakeMatcher : IAiMetadataMatcher
    {
        public List<AiMetadataMatchInput> Requests { get; } = [];

        public Task<AiMetadataMatchCandidate> MatchAsync(
            AiMetadataMatchInput input,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(input);
            return Task.FromResult(new AiMetadataMatchCandidate(
                true,
                72517,
                input.Files.Select(file => new AiMetadataFileCandidate(
                    file.Name,
                    true,
                    2,
                    4,
                    null)).ToArray(),
                null));
        }
    }

    private sealed class FakeTmdbClient : ITmdbClient
    {
        private static readonly TmdbSeries Series =
            new(72517, "来自深渊", "メイドインアビス", null);
        private static readonly TmdbSeason Season =
            new(204984, 72517, 2, "Season 2", new DateOnly(2022, 7, 6), 12);

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(seriesId == Series.Id ? Series : null);

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(
                seriesId == Series.Id ? new TmdbSeriesDetails(Series, [Season]) : null);

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(
                seriesId == Series.Id && seasonNumber == Season.SeasonNumber ? Season : null);

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbEpisode?>(
                seriesId == Series.Id && seasonNumber == Season.SeasonNumber && episodeNumber == 4
                    ? new TmdbEpisode(300004, seriesId, seasonNumber, episodeNumber, "Episode 4", null)
                    : null);
    }
}
