using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class TmdbSeriesResolverTests
{
    [Fact]
    public async Task RepeatsUpstreamSuffixStepsUntilSearchFindsOneCandidate()
    {
        var found = new TmdbSeries(64196, "OVERLORD", "オーバーロード", null);
        var client = new SearchClient(title => title == "オーバーロード" ? [found] : []);

        var result = await new TmdbSeriesResolver(client).ResolveAsync("オーバーロードIV");

        Assert.True(result.IsSuccess);
        Assert.Same(found, result.Value);
        Assert.Equal(["オーバーロードIV", "オーバーロード"], result.AttemptedTitles);
    }

    [Fact]
    public async Task MultipleCandidatesPreferExactOriginalName()
    {
        var exact = new TmdbSeries(2, "Exact", "ONE PIECE", null);
        var client = new SearchClient(_ =>
        [
            new TmdbSeries(1, "Other", "One Piece Live", null),
            exact,
        ]);

        var result = await new TmdbSeriesResolver(client).ResolveAsync("ONE PIECE");

        Assert.Same(exact, result.Value);
        Assert.Single(result.AttemptedTitles);
    }

    [Fact]
    public async Task MultipleCandidatesUseUpstreamSimilarityThreshold()
    {
        const string title = "ダンジョンに出会いを求めるのは間違っているだろうか IV";
        var similar = new TmdbSeries(2, "DanMachi", "ダンジョンに出会いを求めるのは間違っているだろうか", null);
        var client = new SearchClient(_ =>
        [
            new TmdbSeries(1, "Other", "unrelated", null),
            similar,
        ]);

        var result = await new TmdbSeriesResolver(client).ResolveAsync(title);

        Assert.True(result.IsSuccess);
        Assert.Same(similar, result.Value);
    }

    [Fact]
    public async Task CandidateValidatorChecksEveryEligibleSeriesInRankedOrder()
    {
        var first = new TmdbSeries(1, "同名作品", "同名作品", null);
        var second = new TmdbSeries(2, "同名作品", "同名作品", null);
        var client = new SearchClient(_ => [first, second]);
        var inspected = new List<int>();

        var result = await new TmdbSeriesResolver(client).ResolveAsync(
            "同名作品",
            (candidate, _) =>
            {
                inspected.Add(candidate.Id);
                return ValueTask.FromResult(candidate.Id == second.Id);
            });

        Assert.True(result.IsSuccess);
        Assert.Same(second, result.Value);
        Assert.Equal([1, 2], inspected);
        Assert.Single(result.AttemptedTitles);
    }

    [Fact]
    public async Task MultipleCandidatesCanMatchLocalizedName()
    {
        const string title = "Re：从零开始的异世界生活 第四季 丧失篇";
        var localized = new TmdbSeries(
            65942,
            "Re：从零开始的异世界生活",
            "Re:ゼロから始める異世界生活",
            null);
        var client = new SearchClient(_ =>
        [
            new TmdbSeries(1, "其他动画", "別のアニメ", null),
            localized,
        ]);

        var result = await new TmdbSeriesResolver(client).ResolveAsync(title);

        Assert.True(result.IsSuccess);
        Assert.Same(localized, result.Value);
    }

    [Fact]
    public async Task UnrelatedCandidatesDoNotPreventChangedSuffixRetry()
    {
        var found = new TmdbSeries(
            65942,
            "Re：从零开始的异世界生活",
            "Re:ゼロから始める異世界生活",
            null);
        var client = new SearchClient(title => title switch
        {
            "Re:ゼロから始める異世界生活" => [found],
            _ =>
            [
                new TmdbSeries(1, "A", "zzzzz", null),
                new TmdbSeries(2, "B", "yyyyy", null),
            ],
        });

        var result = await new TmdbSeriesResolver(client).ResolveAsync(
            "Re:ゼロから始める異世界生活 4th season 喪失編");

        Assert.True(result.IsSuccess);
        Assert.Same(found, result.Value);
        Assert.Equal(
            ["Re:ゼロから始める異世界生活 4th season 喪失編", "Re:ゼロから始める異世界生活"],
            result.AttemptedTitles);
    }

    [Fact]
    public async Task MultipleUnrelatedCandidatesStopAsAuthoritativeNoMatch()
    {
        var client = new SearchClient(_ =>
        [
            new TmdbSeries(1, "A", "zzzzz", null),
            new TmdbSeries(2, "B", "yyyyy", null),
        ]);

        var result = await new TmdbSeriesResolver(client).ResolveAsync("abcdefghijk");

        Assert.False(result.IsSuccess);
        Assert.Equal("tmdb_series_not_similar", result.Failure!.Code);
        Assert.True(result.Failure.TmdbAccessConfirmed);
        Assert.Single(result.AttemptedTitles);
    }

    [Fact]
    public async Task ClientFailureKeepsFallbackIneligible()
    {
        var client = new SearchClient(_ => throw new TmdbClientException(
            MetadataFailureKind.Authentication,
            "tmdb_authentication_failed",
            tmdbAccessConfirmed: false));

        var result = await new TmdbSeriesResolver(client).ResolveAsync("Title");

        Assert.Equal(MetadataFailureKind.Authentication, result.Failure!.Kind);
        Assert.False(result.Failure.TmdbAccessConfirmed);
    }

    private sealed class SearchClient(Func<string, IReadOnlyList<TmdbSeries>> search) : ITmdbClient
    {
        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(search(title));

        public Task<TmdbSeries?> GetSeriesAsync(int seriesId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
