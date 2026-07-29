using System.Text;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Rules;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Rules;

public sealed class LegacyMikanFilterStoreTests
{
    [Fact]
    public async Task FullReplacementPreservesOrderEmptyAndDuplicateValues()
    {
        await using var fixture = await FilterFixture.CreateAsync();
        var initial = Assert.IsType<LegacyMikanFilterSnapshot>(await fixture.Store.GetAsync("mikan"));
        var config = LegacyMikanFilterCodec.Parse(Encoding.UTF8.GetBytes("""
            {"Filiter0":{
              "first":{"is_enable_whitelist":true,"whitelist":["","CHS","CHS"],"is_enable_blacklist":false,"blacklist":[]},
              "second":{"is_enable_whitelist":false,"whitelist":[],"is_enable_blacklist":true,"blacklist":["720P","720p"]}},
             "Filiter1":{},"Filiter2":{},"Filiter3":{},"Filiter4":{}}
            """));

        var saved = await fixture.Store.SaveAsync(
            "MIKAN", config, initial.Revision, "web", DateTimeOffset.UtcNow);

        Assert.Equal(2, saved.Revision);
        Assert.Equal("web", saved.UpdatedSource);
        Assert.Equal(["first", "second"], saved.Config.Filiter0.Select(pair => pair.Key));
        Assert.Equal(["", "CHS", "CHS"], saved.Config.Filiter0[0].Value.Whitelist);
        await using var connection = await fixture.Database.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM legacy_mikan_filter_values WHERE value = 'CHS';";
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ExpectedRevisionProtectsWebAndLegacyUploadCreatesSnapshot()
    {
        await using var fixture = await FilterFixture.CreateAsync();
        var saved = await fixture.Store.SaveLegacyAsync(
            "mikan", Config("one"), DateTimeOffset.UtcNow);
        Assert.Equal(2, saved.Revision);
        Assert.Equal("legacy_api", saved.UpdatedSource);
        await Assert.ThrowsAsync<LegacyMikanFilterRevisionException>(() => fixture.Store.SaveAsync(
            "mikan", Config("stale"), 1, "web", DateTimeOffset.UtcNow));
        await using var connection = await fixture.Database.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM legacy_mikan_filter_snapshots WHERE source_profile_id = 'mikan';";
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task RollbackCreatesNewAuditedRevisionWithoutDeletingHistory()
    {
        await using var fixture = await FilterFixture.CreateAsync();
        var second = await fixture.Store.SaveLegacyAsync("mikan", Config("second"), DateTimeOffset.UtcNow);
        var third = await fixture.Store.SaveAsync(
            "mikan", Config("third"), second.Revision, "web", DateTimeOffset.UtcNow.AddMinutes(1));
        var rolled = await fixture.Store.RollbackAsync(
            "mikan", 2, third.Revision, DateTimeOffset.UtcNow.AddMinutes(2));
        Assert.Equal(4, rolled.Revision);
        Assert.Equal("rollback", rolled.UpdatedSource);
        Assert.Equal("second", Assert.Single(rolled.Config.Filiter0).Key);
        await using var connection = await fixture.Database.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM legacy_mikan_filter_snapshots WHERE source_profile_id = 'mikan';";
        Assert.Equal(4L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task SnapshotListIsNewestFirstAndBounded()
    {
        await using var fixture = await FilterFixture.CreateAsync();
        var second = await fixture.Store.SaveLegacyAsync(
            "mikan", Config("second"), DateTimeOffset.UtcNow);
        await fixture.Store.SaveAsync(
            "mikan", Config("third"), second.Revision, "web", DateTimeOffset.UtcNow.AddMinutes(1));

        var snapshots = await fixture.Store.ListSnapshotsAsync("MIKAN", 2);

        Assert.Equal([3L, 2L], snapshots.Select(item => item.Revision));
        Assert.Equal(["web", "legacy_api"], snapshots.Select(item => item.UpdatedSource));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Store.ListSnapshotsAsync("mikan", 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Store.ListSnapshotsAsync("mikan", 201));
    }

    private static LegacyMikanFilterConfig Config(string key) => new(
        [new KeyValuePair<string, LegacyMikanFilterRule>(key, new(false, false, [], []))],
        Empty(), Empty(), Empty(), Empty());

    private static Dictionary<string, LegacyMikanFilterRule> Empty() => new(StringComparer.Ordinal);

    private sealed class FilterFixture : IAsyncDisposable
    {
        private FilterFixture(SqliteDatabaseFixture database)
        {
            Database = database;
            Store = new LegacyMikanFilterStore(database.Database);
        }
        public SqliteDatabaseFixture Database { get; }
        public LegacyMikanFilterStore Store { get; }
        public static async Task<FilterFixture> CreateAsync()
        {
            var database = await SqliteDatabaseFixture.CreateAsync();
            await new SourceProfileStore(database.Database)
                .EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
            var fixture = new FilterFixture(database);
            await fixture.Store.EnsureDefaultAsync("mikan", DateTimeOffset.UtcNow);
            return fixture;
        }
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}
