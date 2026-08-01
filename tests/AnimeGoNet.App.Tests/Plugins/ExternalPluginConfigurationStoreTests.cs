using System.Text.Json;
using AnimeGoNet.App.Plugins;

namespace AnimeGoNet.App.Tests.Plugins;

public sealed class ExternalPluginConfigurationStoreTests
{
    [Fact]
    public async Task MissingFileStartsAtRevisionZeroAndPluginsAreDisabled()
    {
        using var fixture = new StoreFixture();

        var snapshot = await fixture.Store.LoadAsync();
        var configuration = fixture.Store.GetOrDefault("com.example.missing");

        Assert.Equal(0, snapshot.Revision);
        Assert.Empty(snapshot.Plugins);
        Assert.False(configuration.Enabled);
        Assert.Equal(0, configuration.Revision);
        Assert.Equal(JsonValueKind.Object, configuration.Args.ValueKind);
        Assert.Equal(JsonValueKind.Object, configuration.Vars.ValueKind);
        Assert.False(File.Exists(fixture.Store.FilePath));
    }

    [Fact]
    public async Task UpsertPersistsCanonicalObjectsAndMonotonicRevisions()
    {
        using var fixture = new StoreFixture();
        await fixture.Store.LoadAsync();

        var first = await fixture.Store.UpsertAsync(
            "com.example.filter",
            true,
            Json("{\"fallback\":true}"),
            Json("{\"quality\":\"1080p\"}"),
            0,
            new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero));
        var second = await fixture.Store.UpsertAsync(
            "com.example.filter",
            false,
            Json("{}"),
            Json("{\"quality\":\"720p\"}"),
            1,
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));

        using var reloaded = new ExternalPluginConfigurationStore(fixture.ConfigurationPath);
        var persisted = await reloaded.LoadAsync();
        var entry = Assert.Single(persisted.Plugins).Value;
        Assert.Equal(1, first.Revision);
        Assert.Equal(2, second.Revision);
        Assert.Equal(2, persisted.Revision);
        Assert.Equal(2, entry.Revision);
        Assert.False(entry.Enabled);
        Assert.Equal("720p", entry.Vars.GetProperty("quality").GetString());
        Assert.Equal(
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
            entry.UpdatedAtUtc);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(fixture.Store.FilePath));
        }
    }

    [Fact]
    public async Task RevisionConflictDoesNotChangePersistedBytes()
    {
        using var fixture = new StoreFixture();
        await fixture.Store.LoadAsync();
        await fixture.Store.UpsertAsync(
            "com.example.filter",
            true,
            Json("{}"),
            Json("{}"),
            0,
            DateTimeOffset.UtcNow);
        var before = await File.ReadAllBytesAsync(fixture.Store.FilePath);

        await Assert.ThrowsAsync<ExternalPluginConfigurationRevisionException>(() =>
            fixture.Store.UpsertAsync(
                "com.example.filter",
                false,
                Json("{}"),
                Json("{}"),
                0,
                DateTimeOffset.UtcNow));

        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Store.FilePath));
        Assert.True(fixture.Store.GetOrDefault("com.example.filter").Enabled);
    }

    [Fact]
    public async Task InvalidObjectsAndIdsAreRejectedBeforeCreatingFile()
    {
        using var fixture = new StoreFixture();
        await fixture.Store.LoadAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.UpsertAsync(
            "Com.Example.Filter",
            true,
            Json("{}"),
            Json("{}"),
            0,
            DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.UpsertAsync(
            "com.example.filter",
            true,
            Json("[]"),
            Json("{}"),
            0,
            DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.UpsertAsync(
            "com.example.filter",
            true,
            Json("{\"duplicate\":1,\"duplicate\":2}"),
            Json("{}"),
            0,
            DateTimeOffset.UtcNow));

        Assert.False(File.Exists(fixture.Store.FilePath));
    }

    [Fact]
    public async Task DuplicatePropertiesInPrivateFileFailClosed()
    {
        using var fixture = new StoreFixture();
        Directory.CreateDirectory(fixture.ConfigurationPath);
        await File.WriteAllTextAsync(
            fixture.Store.FilePath,
            "{\"format_version\":1,\"revision\":0,\"revision\":1,\"plugins\":{}}");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Store.LoadAsync());

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteRestoresSafeDisabledDefault()
    {
        using var fixture = new StoreFixture();
        await fixture.Store.LoadAsync();
        await fixture.Store.UpsertAsync(
            "com.example.filter",
            true,
            Json("{}"),
            Json("{}"),
            0,
            DateTimeOffset.UtcNow);

        var deleted = await fixture.Store.DeleteAsync("com.example.filter", 1);

        Assert.Equal(2, deleted.Revision);
        Assert.Empty(deleted.Plugins);
        Assert.False(fixture.Store.GetOrDefault("com.example.filter").Enabled);
    }

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private sealed class StoreFixture : IDisposable
    {
        public StoreFixture()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                $"animegonet-plugin-configuration-{Guid.NewGuid():N}");
            ConfigurationPath = Path.Combine(RootPath, "config");
            Store = new ExternalPluginConfigurationStore(ConfigurationPath);
        }

        public string RootPath { get; }

        public string ConfigurationPath { get; }

        public ExternalPluginConfigurationStore Store { get; }

        public void Dispose()
        {
            Store.Dispose();
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
