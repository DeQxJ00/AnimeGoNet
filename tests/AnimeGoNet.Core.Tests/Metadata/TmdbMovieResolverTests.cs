using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class TmdbMovieResolverTests
{
    [Fact]
    public async Task TriesBangumiNamesAndVerifiesSelectedMovieDetails()
    {
        var client = new FakeMovieClient
        {
            Searches =
            {
                ["千と千尋の神隠し"] = [],
                ["千与千寻"] = [new TmdbMovie(129, "千与千寻", "千と千尋の神隠し", null)],
            },
        };

        var result = await new TmdbMovieResolver(client).ResolveAsync(
            ["千と千尋の神隠し", "千与千寻"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(129, result.Value!.Id);
        Assert.Contains("千と千尋の神隠し", result.AttemptedTitles);
        Assert.Contains("千与千寻", result.AttemptedTitles);
        Assert.Equal([129], client.DetailRequests);
    }

    [Fact]
    public async Task DoesNotAcceptUnverifiedSearchIdentity()
    {
        var client = new FakeMovieClient
        {
            Searches = { ["Movie"] = [new TmdbMovie(9, "Movie", "Movie", null)] },
            ReturnDetails = false,
        };

        var result = await new TmdbMovieResolver(client).ResolveAsync(["Movie"]);

        Assert.False(result.IsSuccess);
        Assert.Equal("tmdb_movie_not_found", result.Failure!.Code);
        Assert.True(result.Failure.TmdbAccessConfirmed);
    }

    private sealed class FakeMovieClient : ITmdbMovieClient
    {
        public Dictionary<string, IReadOnlyList<TmdbMovie>> Searches { get; } =
            new(StringComparer.Ordinal);

        public List<int> DetailRequests { get; } = [];

        public bool ReturnDetails { get; init; } = true;

        public Task<IReadOnlyList<TmdbMovie>> SearchMoviesAsync(
            string title,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                Searches.TryGetValue(title, out var values)
                    ? values
                    : (IReadOnlyList<TmdbMovie>)[]);
        }

        public Task<TmdbMovie?> GetMovieAsync(
            int movieId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DetailRequests.Add(movieId);
            return Task.FromResult<TmdbMovie?>(ReturnDetails
                ? Searches.Values.SelectMany(value => value).Single(movie => movie.Id == movieId)
                : null);
        }
    }
}
