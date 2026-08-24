using AnimeGoNet.Data.Mikan;

namespace AnimeGoNet.Data.Tests.Mikan;

public sealed class MikanPublishGroupStoreTests
{
    [Fact]
    public async Task ManualNameWinsUntilExplicitAutomaticRefresh()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanPublishGroupStore(fixture.Database);
        var now = new DateTimeOffset(2026, 8, 24, 3, 0, 0, TimeSpan.Zero);
        await store.SaveAutomaticAsync(392, "Kirara Fantasia", "mikan", now);
        var automatic = Assert.Single(await store.ListAsync());
        Assert.Equal("automatic", automatic.NameSource);

        Assert.Equal(
            MikanPublishGroupUpdateResult.Updated,
            await store.UpdateManualAsync(392, "人工字幕组", automatic.Revision, now.AddMinutes(1)));
        await store.SaveAutomaticAsync(392, "不应覆盖", "mikan", now.AddMinutes(2));
        var manual = Assert.Single(await store.ListAsync());
        Assert.Equal("人工字幕组", manual.GroupName);
        Assert.Equal("manual", manual.NameSource);

        Assert.Equal(
            MikanPublishGroupUpdateResult.Updated,
            await store.RequestRefreshAsync(392, manual.Revision, now.AddMinutes(3)));
        await store.SaveAutomaticAsync(392, "重新获取", "mikan", now.AddMinutes(4));
        var refreshed = Assert.Single(await store.ListAsync());
        Assert.Equal("重新获取", refreshed.GroupName);
        Assert.Equal("automatic", refreshed.NameSource);
    }
}
