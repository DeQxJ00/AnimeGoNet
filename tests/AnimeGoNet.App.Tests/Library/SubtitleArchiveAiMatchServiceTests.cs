using AnimeGoNet.App.Library;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Library;

public sealed class SubtitleArchiveAiMatchServiceTests
{
    [Fact]
    public async Task MatchAcceptsLanguageVariantsForTheSameVerifiedEpisode()
    {
        await WithServiceAsync(async (service, matcher, tmdb) =>
        {
            matcher.Response = Response(
                72517,
                [
                    new("Subs/01.zh-Hans.ass", true, 2, 1, null),
                    new("Subs/01.zh-Hant.ass", true, 2, 1, null),
                    new("Extras/NCOP.ass", false, 2, null, "non_episode_extra"),
                ]);
            var session = Session();

            var result = await service.MatchAsync(session);

            Assert.Equal(2, result.Assignments.Count);
            Assert.All(result.Assignments, value => Assert.Equal(1, value.EpisodeNumber));
            Assert.Equal(
                session.Candidates.Take(2).Select(value => value.Id),
                result.Assignments.Select(value => value.CandidateId));
            Assert.Equal(
                session.Candidates.Select(value => value.RelativePath),
                matcher.Input!.Files.Select(value => value.Name));
            Assert.Contains("confirmed_tmdb_series_id=72517", matcher.Input.Title, StringComparison.Ordinal);
            Assert.Contains("confirmed_tmdb_season=2", matcher.Input.Title, StringComparison.Ordinal);
            Assert.True(matcher.Input.PromptFeaturesOverride!.TmdbMcp);
            Assert.False(matcher.Input.PromptFeaturesOverride.BangumiMcp);
            Assert.False(matcher.Input.PromptFeaturesOverride.AniDbLookup);
            Assert.Equal([1], tmdb.RequestedEpisodes);
        });
    }

    [Theory]
    [InlineData(90001, 2, "subtitle_ai_tmdb_series_changed")]
    [InlineData(72517, 3, "subtitle_ai_tmdb_season_changed")]
    public async Task MatchRejectsChangesToConfirmedTmdbOwnership(
        int tmdbId,
        int seasonNumber,
        string expectedCode)
    {
        await WithServiceAsync(async (service, matcher, _) =>
        {
            matcher.Response = Response(
                tmdbId,
                [
                    new("Subs/01.zh-Hans.ass", true, seasonNumber, 1, null),
                    new("Subs/01.zh-Hant.ass", true, seasonNumber, 1, null),
                    new("Extras/NCOP.ass", false, seasonNumber, null, "non_episode_extra"),
                ]);

            var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(
                () => service.MatchAsync(Session()));

            Assert.Equal(expectedCode, exception.SafeCode);
        });
    }

    [Fact]
    public async Task MatchRejectsAChangedOrReorderedRelativePath()
    {
        await WithServiceAsync(async (service, matcher, _) =>
        {
            matcher.Response = Response(
                72517,
                [
                    new("01.zh-Hans.ass", true, 2, 1, null),
                    new("Subs/01.zh-Hant.ass", true, 2, 1, null),
                    new("Extras/NCOP.ass", false, 2, null, "non_episode_extra"),
                ]);

            var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(
                () => service.MatchAsync(Session()));

            Assert.Equal("subtitle_ai_file_identity_mismatch", exception.SafeCode);
        });
    }

    private static SubtitleArchiveImportSession Session() =>
        new(
            "session-1",
            "subtitles.zip",
            72517,
            2,
            "来自深渊",
            [
                new("candidate-1", "01.zh-Hans.ass", "Subs/01.zh-Hans.ass", 100, null, null, null),
                new("candidate-2", "01.zh-Hant.ass", "Subs/01.zh-Hant.ass", 110, null, null, null),
                new("candidate-3", "NCOP.ass", "Extras/NCOP.ass", 120, null, null, null),
            ]);

    private static AiMetadataMatchResponse Response(
        int tmdbId,
        IReadOnlyList<AiMetadataFileCandidate> files) =>
        new(
            new AiMetadataMatchCandidate(true, tmdbId, files, null),
            new AiMetadataProviderUsage("fake-model", 10, 5, 15, 1, 1));

    private static async Task WithServiceAsync(
        Func<SubtitleArchiveAiMatchService, FakeMatcher, FakeTmdbClient, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-subtitle-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var matcher = new FakeMatcher();
            var tmdb = new FakeTmdbClient();
            using var prompts = new SubtitleAiPromptStore(
                DirectoryLayout.From(AnimeGoDefaults.CreateNative(root).Paths));
            var service = new SubtitleArchiveAiMatchService(matcher, tmdb, prompts);
            await test(service, matcher, tmdb);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeMatcher : IAiMetadataMatcher
    {
        public AiMetadataMatchInput? Input { get; private set; }

        public AiMetadataMatchResponse Response { get; set; } = null!;

        public Task<AiMetadataMatchResponse> MatchAsync(
            AiMetadataMatchInput input,
            CancellationToken cancellationToken = default)
        {
            Input = input;
            return Task.FromResult(Response);
        }
    }

    private sealed class FakeTmdbClient : ITmdbClient
    {
        private static readonly TmdbSeries Series =
            new(72517, "来自深渊", "メイドインアビス", null);
        private static readonly TmdbSeason Season =
            new(204984, 72517, 2, "Season 2", null, 12);

        public List<int> RequestedEpisodes { get; } = [];

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
            CancellationToken cancellationToken = default)
        {
            RequestedEpisodes.Add(episodeNumber);
            return Task.FromResult<TmdbEpisode?>(
                seriesId == Series.Id && seasonNumber == Season.SeasonNumber && episodeNumber == 1
                    ? new TmdbEpisode(300001, seriesId, seasonNumber, episodeNumber, "Episode 1", null)
                    : null);
        }
    }
}
