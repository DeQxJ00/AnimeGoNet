using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Rules;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Rules;

public sealed class MikanRssRuleStoreTests
{
    [Fact]
    public async Task DefaultInitializationIsIdempotentAndDoesNotOverwriteEdits()
    {
        await using var fixture = await RuleFixture.CreateAsync();
        var original = Assert.IsType<MikanRssRuleSnapshot>(await fixture.Store.GetAsync("mikan"));
        Assert.Equal(1, original.Revision);
        Assert.Equal("resolution-720p", Assert.Single(original.Rules.Blacklist).Id);

        var edited = await fixture.Store.SaveAsync(
            "mikan",
            new MikanRssRuleSet(
                [new NamedMatchArray("wanted", "Wanted", true, [" CHS "])],
                [new NamedMatchArray("blocked", "Blocked", true, [" 720P "])],
                [new PriorityGroup(
                    "codec", "Codec",
                    [
                        new NamedMatchArray("hevc", "HEVC", true, [" HEVC "]),
                        new NamedMatchArray("avc", "AVC", false, [" H264 "]),
                    ])]),
            original.Revision,
            DateTimeOffset.UtcNow);
        Assert.Equal(2, edited.Revision);
        Assert.Equal("chs", Assert.Single(edited.Rules.Whitelist).Values[0]);
        Assert.Equal(["hevc", "avc"], Assert.Single(edited.Rules.PriorityGroups).Arrays.Select(item => item.Id));

        await fixture.Store.EnsureDefaultAsync(
            "mikan", MikanRssRuleDefaults.Create(), DateTimeOffset.UtcNow.AddMinutes(1));
        var afterRestart = Assert.IsType<MikanRssRuleSnapshot>(await fixture.Store.GetAsync("MIKAN"));
        Assert.Equal(2, afterRestart.Revision);
        Assert.Equal("blocked", Assert.Single(afterRestart.Rules.Blacklist).Id);
    }

    [Fact]
    public async Task SaveRequiresExpectedRevisionAndRejectsDuplicateIds()
    {
        await using var fixture = await RuleFixture.CreateAsync();
        var current = Assert.IsType<MikanRssRuleSnapshot>(await fixture.Store.GetAsync("mikan"));
        await Assert.ThrowsAsync<MikanRssRuleRevisionException>(() => fixture.Store.SaveAsync(
            "mikan", current.Rules, current.Revision + 1, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.SaveAsync(
            "mikan",
            new MikanRssRuleSet(
                [new NamedMatchArray("duplicate", "one", true, ["a"])],
                [new NamedMatchArray("duplicate", "two", true, ["b"])], []),
            current.Revision,
            DateTimeOffset.UtcNow));
        var unchanged = Assert.IsType<MikanRssRuleSnapshot>(await fixture.Store.GetAsync("mikan"));
        Assert.Equal(current.Revision, unchanged.Revision);
        Assert.Equal(
            current.Rules.PriorityGroups.Select(group => group.Id),
            unchanged.Rules.PriorityGroups.Select(group => group.Id));
    }

    [Fact]
    public async Task DatabaseRejectsNonLowercaseStoredValues()
    {
        await using var fixture = await RuleFixture.CreateAsync();
        await using var connection = await fixture.Database.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mikan_rss_match_values (
                source_profile_id, array_id, position, value_lower)
            VALUES ('mikan', 'resolution-720p', 99, 'UPPER');
            """;
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task SnapshotsAreNewestFirstAndRollbackCreatesNewRevision()
    {
        await using var fixture = await RuleFixture.CreateAsync();
        var initial = Assert.IsType<MikanRssRuleSnapshot>(await fixture.Store.GetAsync("mikan"));
        var second = await fixture.Store.SaveAsync(
            "mikan",
            new MikanRssRuleSet(
                [new NamedMatchArray("second", "Second", true, ["chs"])],
                [],
                []),
            initial.Revision,
            DateTimeOffset.UtcNow);
        var third = await fixture.Store.SaveAsync(
            "mikan",
            new MikanRssRuleSet(
                [new NamedMatchArray("third", "Third", true, ["cht"])],
                [],
                []),
            second.Revision,
            DateTimeOffset.UtcNow.AddMinutes(1));

        var newest = await fixture.Store.ListSnapshotsAsync("MIKAN", 2);
        Assert.Equal([3L, 2L], newest.Select(item => item.Revision));

        var rolled = await fixture.Store.RollbackAsync(
            "mikan", second.Revision, third.Revision, DateTimeOffset.UtcNow.AddMinutes(2));
        Assert.Equal(4, rolled.Revision);
        Assert.Equal("second", Assert.Single(rolled.Rules.Whitelist).Id);
        Assert.Equal(
            [4L, 3L, 2L, 1L],
            (await fixture.Store.ListSnapshotsAsync("mikan")).Select(item => item.Revision));
        await Assert.ThrowsAsync<MikanRssRuleRevisionException>(() =>
            fixture.Store.RollbackAsync(
                "mikan", 1, third.Revision, DateTimeOffset.UtcNow.AddMinutes(3)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            fixture.Store.RollbackAsync(
                "mikan", 999, rolled.Revision, DateTimeOffset.UtcNow.AddMinutes(3)));
    }

    private sealed class RuleFixture : IAsyncDisposable
    {
        private RuleFixture(SqliteDatabaseFixture database, MikanRssRuleStore store)
        {
            Database = database;
            Store = store;
        }

        public SqliteDatabaseFixture Database { get; }
        public MikanRssRuleStore Store { get; }

        public static async Task<RuleFixture> CreateAsync()
        {
            var database = await SqliteDatabaseFixture.CreateAsync();
            var profiles = new SourceProfileStore(database.Database);
            await profiles.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
            var store = new MikanRssRuleStore(database.Database);
            await store.EnsureDefaultAsync("mikan", MikanRssRuleDefaults.Create(), DateTimeOffset.UtcNow);
            return new RuleFixture(database, store);
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}
