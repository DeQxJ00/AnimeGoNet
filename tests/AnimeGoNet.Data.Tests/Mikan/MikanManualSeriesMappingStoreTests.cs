using AnimeGoNet.Data.Mikan;

namespace AnimeGoNet.Data.Tests.Mikan;

public sealed class MikanManualSeriesMappingStoreTests
{
    [Fact]
    public async Task UpsertIsScopedToExactMikanAndGroupPairAndCanBeDeleted()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanManualSeriesMappingStore(fixture.Database);
        var now = new DateTimeOffset(2026, 8, 24, 1, 2, 3, TimeSpan.Zero);

        var created = await store.UpsertAsync(
            3981, 392, 100, 200, 2, "task-one", now);

        Assert.Equal(3981, created.MikanId);
        Assert.Equal(392, created.GroupId);
        Assert.Equal(100, created.ExpectedTmdbSeriesId);
        Assert.Equal(200, created.TmdbSeriesId);
        Assert.Equal(2, created.TmdbSeasonNumber);
        Assert.Null(await store.GetAsync(3981, 393));

        var updated = await store.UpsertAsync(
            3981, 392, 100, 201, 3, "task-two", now.AddMinutes(1));

        Assert.Equal(201, updated.TmdbSeriesId);
        Assert.Equal(3, updated.TmdbSeasonNumber);
        Assert.Equal("task-two", updated.AcceptedFromTaskId);
        Assert.Single(await store.ListAsync());
        Assert.True(await store.DeleteAsync(3981, 392));
        Assert.Null(await store.GetAsync(3981, 392));
        Assert.False(await store.DeleteAsync(3981, 392));
    }
}
