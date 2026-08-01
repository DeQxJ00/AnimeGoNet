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

    [Fact]
    public async Task SourceEpisodeAliasIsNormalizedQueryableAndCascadesWithCompletion()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new CompletionRecordStore(fixture.Database);
        var completion = CreateRecord(new TmdbEpisodeIdentity(42, 2, 3)) with
        {
            Id = "completion-3",
            CompletedAtUtc = new DateTimeOffset(2026, 7, 1, 2, 3, 4, TimeSpan.Zero),
        };
        Assert.True(await store.TryAddAsync(completion));
        var alias = new CompletionAlias
        {
            Id = "alias-3",
            CompletionId = completion.Id,
            SourceId = " MIKAN ",
            SourceWorkId = " 3951 ",
            SourceEpisode = " 3 ",
            InfoHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            CreatedAtUtc = new DateTimeOffset(2026, 7, 1, 2, 4, 5, TimeSpan.Zero),
        };

        Assert.True(await store.TryAddAliasAsync(alias));
        Assert.False(await store.TryAddAliasAsync(alias with { Id = "alias-duplicate" }));

        var match = Assert.IsType<CompletionAliasMatch>(
            await store.FindBySourceEpisodeAsync("Mikan", "3951", "3"));
        Assert.Equal("alias-3", match.Alias.Id);
        Assert.Equal("mikan", match.Alias.SourceId);
        Assert.Equal("3951", match.Alias.SourceWorkId);
        Assert.Equal("3", match.Alias.SourceEpisode);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", match.Alias.InfoHash);
        Assert.Equal(new TmdbEpisodeIdentity(42, 2, 3), match.Episode);
        Assert.Equal(completion.CompletedAtUtc, match.CompletedAtUtc);

        await using (var connection = await fixture.Database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM completion_records WHERE id = 'completion-3';";
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        Assert.Null(await store.FindBySourceEpisodeAsync("mikan", "3951", "3"));
    }

    [Fact]
    public async Task AliasCannotReferenceMissingCompletion()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new CompletionRecordStore(fixture.Database);

        Assert.False(await store.TryAddAliasAsync(new CompletionAlias
        {
            Id = "orphan",
            CompletionId = "missing",
            SourceId = "mikan",
            SourceWorkId = "3951",
            SourceEpisode = "3",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        }));
    }

    private static CompletionRecord CreateRecord(TmdbEpisodeIdentity episode) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Episode = episode,
        SourceId = "mikan",
        CompletedAtUtc = DateTimeOffset.UtcNow,
    };
}
