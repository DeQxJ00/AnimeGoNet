using AnimeGoNet.Core.Library;
using AnimeGoNet.Data.Library;

namespace AnimeGoNet.Data.Tests.Library;

public sealed class CompletionRecordStoreTests
{
    [Fact]
    public async Task ConcurrentSameEpisodeWritesCreateOneCompletion()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new CompletionRecordStore(fixture.Database);
        var episode = new TmdbEpisodeIdentity(42, 1, 3);
        var writes = Enumerable.Range(0, 8).Select(index => store.TryAddAsync(new CompletionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Episode = episode,
            SourceId = index % 2 == 0 ? "Mikan" : "U2",
            SourceItemId = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CompletedAtUtc = DateTimeOffset.UtcNow,
        }));

        var results = await Task.WhenAll(writes);

        Assert.Single(results, result => result);
        Assert.True(await store.ExistsAsync(episode));
    }

    [Fact]
    public async Task AnotherEpisodeIsNotSuppressed()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new CompletionRecordStore(fixture.Database);

        Assert.True(await store.TryAddAsync(CreateRecord(new TmdbEpisodeIdentity(42, 1, 3))));
        Assert.True(await store.TryAddAsync(CreateRecord(new TmdbEpisodeIdentity(42, 1, 4))));
    }

    private static CompletionRecord CreateRecord(TmdbEpisodeIdentity episode) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Episode = episode,
        SourceId = "mikan",
        CompletedAtUtc = DateTimeOffset.UtcNow,
    };
}
