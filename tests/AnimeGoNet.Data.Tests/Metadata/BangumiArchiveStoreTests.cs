using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Tests.DataUpdate;

namespace AnimeGoNet.Data.Tests.Metadata;

public sealed class BangumiArchiveStoreTests
{
    [Fact]
    public async Task NoActiveVersionOrUnknownSubjectIsCacheMiss()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var store = new BangumiArchiveStore(fixture.Database);

        Assert.Null(await store.GetAsync(51));
        await fixture.Store.ImportAsync(
            await fixture.CreateRequestAsync("2026.07.29.1"));
        Assert.Null(await store.GetAsync(999));
    }

    [Fact]
    public async Task ReadsSubjectAndEpisodesFromOneActiveVersionSnapshot()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        await fixture.Store.ImportAsync(
            await fixture.CreateRequestAsync("2026.07.29.1"));
        var store = new BangumiArchiveStore(fixture.Database);

        var snapshot = Assert.IsType<BangumiArchiveSnapshot>(
            await store.GetAsync(51));

        Assert.Equal("2026.07.29.1", snapshot.DataVersion);
        Assert.Equal("CLANNAD", snapshot.Subject.Name);
        Assert.Equal(new DateOnly(2007, 10, 5), snapshot.Subject.AirDate);
        Assert.True(snapshot.HasCompleteEpisodeSet);
        Assert.Collection(
            snapshot.Episodes,
            first =>
            {
                Assert.Equal(1423, first.Id);
                Assert.Equal(0, first.Type);
                Assert.Equal(1, first.EpisodeNumber);
                Assert.Equal(new DateOnly(2007, 10, 5), first.AirDate);
            },
            second =>
            {
                Assert.Equal(1424, second.Id);
                Assert.Equal(48.5m, second.EpisodeNumber);
                Assert.Null(second.AirDate);
            });
    }

    [Fact]
    public async Task ActiveVersionSwitchAndRollbackAreVisibleWithoutRestart()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        await fixture.Store.ImportAsync(
            await fixture.CreateRequestAsync("2026.07.29.1"));
        var store = new BangumiArchiveStore(fixture.Database);
        var first = Assert.IsType<BangumiArchiveSnapshot>(
            await store.GetAsync(51));

        await fixture.Store.ImportAsync(await fixture.CreateRequestAsync(
            "2026.07.30.1",
            subjects:
            """
            {"id":51,"name":"CLANNAD Updated","name_cn":"CLANNAD 更新","air_date":"2007-10-05","episode_count":1}

            """,
            episodes:
            """
            {"id":2423,"subject_id":51,"sort":1,"episode":"1","air_date":"2007-10-05"}

            """));
        var second = Assert.IsType<BangumiArchiveSnapshot>(
            await store.GetAsync(51));
        await fixture.Store.RollbackAsync(DateTimeOffset.UtcNow);
        var rolledBack = Assert.IsType<BangumiArchiveSnapshot>(
            await store.GetAsync(51));

        Assert.Equal("CLANNAD", first.Subject.Name);
        Assert.Equal("CLANNAD Updated", second.Subject.Name);
        Assert.Equal("CLANNAD", rolledBack.Subject.Name);
        Assert.Equal("2026.07.29.1", rolledBack.DataVersion);
    }

    [Fact]
    public async Task IncompleteEpisodeSetIsExplicitlyMarked()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        await fixture.Store.ImportAsync(await fixture.CreateRequestAsync(
            "2026.07.29.1",
            subjects:
            """
            {"id":51,"name":"CLANNAD","name_cn":"CLANNAD","air_date":"2007-10-05","episode_count":2}

            """,
            episodes:
            """
            {"id":1423,"subject_id":51,"sort":1,"episode":"1","air_date":"2007-10-05"}

            """));
        var store = new BangumiArchiveStore(fixture.Database);

        var snapshot = Assert.IsType<BangumiArchiveSnapshot>(
            await store.GetAsync(51));

        Assert.False(snapshot.HasCompleteEpisodeSet);
    }

    [Fact]
    public async Task VersionTwoRelationsAreAuthoritativeIncludingEmptyResults()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        await fixture.Store.ImportAsync(await fixture.CreateRequestAsync(
            "2026.07.29.2",
            schemaVersion: 2));
        var store = new BangumiArchiveStore(fixture.Database);

        var airRelations = Assert.IsAssignableFrom<IReadOnlyList<AnimeGoNet.Core.Metadata.BangumiSubjectRelation>>(
            await store.GetRelatedSubjectsAsync(52));
        var prequel = Assert.Single(airRelations);
        Assert.Equal(51, prequel.Id);
        Assert.Equal("CLANNAD", prequel.Name);
        Assert.Equal("前传", prequel.Relation);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<AnimeGoNet.Core.Metadata.BangumiSubjectRelation>>(
            await store.GetRelatedSubjectsAsync(51)));
    }

    [Fact]
    public async Task VersionOneRelationsAreCacheMissForOnlineFallback()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        await fixture.Store.ImportAsync(
            await fixture.CreateRequestAsync("2026.07.29.1"));

        Assert.Null(await new BangumiArchiveStore(fixture.Database)
            .GetRelatedSubjectsAsync(51));
    }

    [Fact]
    public async Task UsageAuditPersistsCountsAcrossVersions()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var store = new BangumiArchiveStore(fixture.Database);
        var firstHit = new DateTimeOffset(2026, 8, 13, 1, 2, 3, TimeSpan.Zero);
        var lastHit = firstHit.AddMinutes(2);

        await store.RecordSubjectHitAsync("2026.07.29.1", 51, firstHit);
        await store.RecordSubjectHitAsync("2026.07.29.1", 52, firstHit.AddMinutes(1));
        await store.RecordEpisodeHitAsync("2026.07.29.1", 52, 12, firstHit.AddMinutes(1));
        await store.RecordRelationHitAsync("2026.08.05.1", 53, 0, lastHit);
        await store.RecordEpisodeHitAsync("2026.07.29.1", 51, 24, firstHit);

        var usage = await store.GetUsageAsync();

        Assert.Equal(5, usage.TotalHits);
        Assert.Equal(2, usage.SubjectHits);
        Assert.Equal(2, usage.EpisodeHits);
        Assert.Equal(1, usage.RelationHits);
        Assert.Equal(lastHit, usage.LastHitAtUtc);

        var firstPage = await store.ListUsageEventsAsync(1, 2);
        Assert.Equal(5, firstPage.TotalItems);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal("relations", firstPage.Items[0].HitKind);
        Assert.Equal(53, firstPage.Items[0].SubjectId);
        Assert.Equal(0, firstPage.Items[0].ResultCount);
        Assert.Equal("episodes", firstPage.Items[1].HitKind);
        Assert.Equal(12, firstPage.Items[1].ResultCount);

        var episodes = await store.ListUsageEventsAsync(1, 100, "EPISODES");
        Assert.Equal("episodes", episodes.HitKind);
        Assert.Equal(2, episodes.TotalItems);
        Assert.All(episodes.Items, item => Assert.Equal("episodes", item.HitKind));
        Assert.Equal([12, 24], episodes.Items.Select(item => item.ResultCount));
    }
}
