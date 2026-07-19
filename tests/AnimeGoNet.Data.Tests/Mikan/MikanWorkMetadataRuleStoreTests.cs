using AnimeGoNet.Data.Mikan;

namespace AnimeGoNet.Data.Tests.Mikan;

public sealed class MikanWorkMetadataRuleStoreTests
{
    [Fact]
    public async Task SavesOneManualOverrideForTheWholeMikanWork()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanWorkMetadataRuleStore(fixture.Database);
        var now = new DateTimeOffset(2026, 7, 19, 9, 0, 0, TimeSpan.Zero);

        var saved = await store.SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, 72517, 2, -12),
            expectedRevision: 0,
            now);

        Assert.Equal(3951, saved.MikanId);
        Assert.Equal(547888, saved.BangumiSubjectId);
        Assert.Equal(72517, saved.TmdbSeriesId);
        Assert.Equal(2, saved.TmdbSeasonNumber);
        Assert.Equal(-12, saved.EpisodeOffset);
        Assert.Equal(1, saved.Revision);
        Assert.Equal(now, saved.CreatedAtUtc);
        Assert.Equal(now, saved.UpdatedAtUtc);
        Assert.Equal(saved, await store.GetEnabledAsync(3951));
    }

    [Fact]
    public async Task StaleRevisionCannotOverwriteManualOverride()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanWorkMetadataRuleStore(fixture.Database);
        var first = await store.SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, 72517, 2, null),
            0,
            DateTimeOffset.UtcNow);
        var second = await store.SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, 72517, 3, null),
            first.Revision,
            DateTimeOffset.UtcNow.AddMinutes(1));

        var exception = await Assert.ThrowsAsync<MikanWorkMetadataRuleRevisionException>(() => store.SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, 72517, 4, null),
            first.Revision,
            DateTimeOffset.UtcNow.AddMinutes(2)));

        Assert.Equal(3951, exception.MikanId);
        Assert.Equal(first.Revision, exception.ExpectedRevision);
        Assert.Equal(second, await store.GetAsync(3951));
    }

    [Fact]
    public async Task DisabledRuleIsRetainedButNotApplied()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanWorkMetadataRuleStore(fixture.Database);
        var saved = await store.SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, null, null, null),
            0,
            DateTimeOffset.UtcNow);

        var disabled = await store.SetEnabledAsync(
            3951,
            enabled: false,
            saved.Revision,
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.False(disabled.Enabled);
        Assert.Equal(2, disabled.Revision);
        Assert.Null(await store.GetEnabledAsync(3951));
        Assert.Equal(disabled, await store.GetAsync(3951));
    }

    [Fact]
    public async Task DeleteRequiresCurrentRevision()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanWorkMetadataRuleStore(fixture.Database);
        var saved = await store.SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, null, null, null),
            0,
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<MikanWorkMetadataRuleRevisionException>(() =>
            store.DeleteAsync(3951, saved.Revision + 1));
        await store.DeleteAsync(3951, saved.Revision);

        Assert.Null(await store.GetAsync(3951));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(72517, null, 1)]
    [InlineData(null, 2, null)]
    public async Task RejectsIncompleteTmdbOverrides(
        int? tmdbSeriesId,
        int? tmdbSeasonNumber,
        int? episodeOffset)
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanWorkMetadataRuleStore(fixture.Database);
        var update = new MikanWorkMetadataRuleUpdate(
            3951,
            null,
            tmdbSeriesId,
            tmdbSeasonNumber,
            episodeOffset);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync(update, 0, DateTimeOffset.UtcNow));
    }
}
