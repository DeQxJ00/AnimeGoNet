using AnimeGoNet.Data.Mikan;

namespace AnimeGoNet.Data.Tests.Mikan;

public sealed class MikanPluginCallLogStoreTests
{
    [Fact]
    public async Task PersistsSafeCallAndItemAuditAndFiltersByMode()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanPluginCallLogStore(fixture.Database);
        var now = new DateTimeOffset(2026, 8, 24, 2, 0, 0, TimeSpan.Zero);
        await store.RecordAsync(new MikanPluginCallLog(
            "call-one", "/api/download/manager", "single", "tv", "success",
            1, 1, 0, null, 25, now, now.AddMilliseconds(25),
            [new MikanPluginCallLogItem(0, "task-one", 3981, 392, "staged", null)]));
        await store.RecordAsync(new MikanPluginCallLog(
            "call-two", "/api/rss", "all", "movie", "failed",
            0, 0, 0, "rss_fetch_failed", 30, now.AddMinutes(1), now.AddMinutes(1), []));

        var single = await store.ListAsync(1, 20, mode: "single");
        var entry = Assert.Single(single.Items);
        Assert.Equal(2, (await store.ListAsync(1, 20)).TotalCount);
        Assert.Equal("task-one", Assert.Single(entry.Items).TaskId);
        Assert.Equal(392, entry.Items[0].GroupId);
        Assert.Empty((await store.ListAsync(1, 20, result: "failed")).Items[0].Items);
    }
}
