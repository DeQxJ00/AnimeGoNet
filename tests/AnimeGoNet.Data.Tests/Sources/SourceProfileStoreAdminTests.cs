using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Sources;

public sealed class SourceProfileStoreAdminTests
{
    [Fact]
    public async Task CreateUpdateAndListPreserveRevisionAndImmutableTaskRoute()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        await store.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        var created = await store.CreateAsync(
            "u2",
            Definition("U2", "u2", "pt", "link", ["u2.invalid"]),
            At(10));
        var enabled = Assert.IsType<SourceProfileRecord>(await store.GetEnabledAsync("u2"));
        var task = await new IngestTaskStore(fixture.Database).AddAsync(
            new NormalizedIngestItem(
                "u2", new Uri("https://u2.invalid/download/a.torrent"), new string('a', 64),
                "U2 episode", "u2-a", "u2-work", null, null, null, null),
            enabled);

        var updated = await store.UpdateAsync(
            "u2",
            Definition("U2 route", "u2", "bt", "move", ["u2.invalid", "*.u2.invalid"]),
            expectedRevision: 1,
            At(11));
        var listed = Assert.Single(await store.ListAsync(), item => item.Id == "u2");

        Assert.Equal(1, created.Revision);
        Assert.Equal(2, updated.Revision);
        Assert.Equal("bt", updated.DownloaderId);
        Assert.Equal("move", updated.FileStrategy);
        Assert.Equal("animegonet", updated.Category);
        Assert.Equal(["source-test"], updated.Tags);
        Assert.Equal(0, updated.SeedingTimeMinutes);
        Assert.Equal(1, listed.IngestTaskCount);
        Assert.Equal("pt", task.DownloaderId);
        Assert.Equal(1, task.SourceProfileRevision);
        await Assert.ThrowsAsync<SourceProfileRevisionException>(() => store.UpdateAsync(
            "u2", Definition("stale", "u2", "bt", "move", ["u2.invalid"]), 1, At(12)));
        await Assert.ThrowsAsync<SourceProfileConflictException>(() => store.DeleteAsync("u2", 2));
    }

    [Fact]
    public async Task UnreferencedProfileCanBeDeletedButDefaultMikanIsProtected()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        await store.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        _ = await store.CreateAsync(
            "ttg", Definition("TTG", "ttg", "pt", "link", ["ttg.invalid"]), At(10));

        await store.DeleteAsync("ttg", 1);

        Assert.Null(await store.GetAsync("ttg"));
        await Assert.ThrowsAsync<SourceProfileConflictException>(() => store.DeleteAsync("mikan", 1));
    }

    [Fact]
    public async Task DuplicateCreateAndMissingUpdateHaveStableExceptions()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        await store.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        var definition = Definition("U2", "u2", "pt", "link", ["u2.invalid"]);
        _ = await store.CreateAsync("u2", definition, At(10));

        await Assert.ThrowsAsync<SourceProfileDuplicateException>(() => store.CreateAsync("u2", definition, At(11)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.UpdateAsync("missing", definition, 1, At(11)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.DeleteAsync("missing", 1));
    }

    [Fact]
    public async Task StoreEnforcesDownloadPolicyWithoutApiLayer()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);

        var invalid = Definition("U2", "u2", "pt", "move", ["u2.invalid"]) with
        {
            SeedingTimeMinutes = 1,
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateAsync("u2", invalid, At(10)));
    }

    [Fact]
    public async Task MikanCookieIsNormalizedVersionedAndNeverFormatted()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        var seed = AnimeGoDefaults.CreateDocker().InitialSourceProfiles[0]
            with
            {
                MikanIdentityCookie =
                    ".AspNetCore.Identity.Application=first-secret",
            };

        await store.EnsureSeedsAsync([seed]);
        await store.EnsureSeedsAsync([seed]);
        var first = Assert.IsType<SourceProfileAdminRecord>(
            await store.GetAsync("mikan"));

        Assert.Equal(1, first.Revision);
        Assert.Equal("first-secret", first.MikanIdentityCookie);
        Assert.DoesNotContain(
            "first-secret",
            first.ToString(),
            StringComparison.Ordinal);

        await store.EnsureSeedsAsync(
        [
            seed with { MikanIdentityCookie = "second-secret" },
        ]);
        var second = Assert.IsType<SourceProfileRecord>(
            await store.GetEnabledAsync("mikan"));

        Assert.Equal(2, second.Revision);
        Assert.Equal("second-secret", second.MikanIdentityCookie);
        Assert.DoesNotContain(
            "second-secret",
            second.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonMikanProfileCannotPersistMikanCookie()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        var definition = Definition(
            "U2",
            "u2",
            "pt",
            "link",
            ["u2.invalid"]) with
        {
            MikanIdentityCookie = "must-not-persist",
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateAsync("u2", definition, At(10)));

        Assert.Null(await store.GetAsync("u2"));
    }

    private static SourceProfileDefinition Definition(
        string name,
        string adapter,
        string downloader,
        string strategy,
        IReadOnlyList<string> hosts) =>
        new(
            name,
            adapter,
            downloader,
            strategy,
            hosts,
            "animegonet",
            ["source-test"],
            strategy == "move" ? 0 : -1,
            false,
            false,
            true);

    private static DateTimeOffset At(int hour) =>
        DateTimeOffset.Parse(
            $"2026-07-26T{hour:00}:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
}
