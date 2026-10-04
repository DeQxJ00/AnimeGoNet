using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class DownloaderOverrideStoreTests
{
    [Fact]
    public async Task WritesSelectedYamlPreservingOtherSectionsAndIgnoresLegacyJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-yaml", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var yaml = Path.Combine(root, "custom.yaml");
            await File.WriteAllTextAsync(yaml, """
                version: "1.7.1"
                web:
                  port: 6180
                downloaders:
                  bt:
                    type: qbittorrent
                    base_url: http://localhost:8080/
                    download_path: /downloads
                    custom_field: keep-me
                """);
            var legacy = Path.Combine(root, "downloaders.private.json");
            await File.WriteAllTextAsync(legacy, "not-read-or-migrated");
            using var store = new DownloaderOverrideStore(root, yaml);
            await store.UpsertAsync("bt", Entry("http://localhost:9090/", "admin", "a: #b\nquoted", "/downloads"), 0);
            using var reloaded = new DownloaderOverrideStore(root, yaml);
            Assert.Equal("a: #b\nquoted", (await reloaded.LoadAsync()).Downloaders["bt"].Password);
            var content = await File.ReadAllTextAsync(yaml);
            Assert.Contains("port: 6180", content);
            Assert.Contains("custom_field: keep-me", content);
            Assert.Equal("not-read-or-migrated", await File.ReadAllTextAsync(legacy));
            Assert.False(File.Exists(Path.Combine(root, "animego.yaml")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task UpsertReloadAndDeleteUseAtomicVersionedPrivateFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-downloader-overrides", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new DownloaderOverrideStore(root);
            var initial = await store.LoadAsync();
            var saved = await store.UpsertAsync(
                "pt-main",
                Entry("http://127.0.0.1:8080", "admin", "private-password", "C:\\downloads\\pt"),
                expectedRevision: 0);
            using var reloadStore = new DownloaderOverrideStore(root);
            var reloaded = await reloadStore.LoadAsync();
            var entry = reloaded.Downloaders["PT-MAIN"];

            Assert.Equal(0, initial.Revision);
            Assert.Equal(1, saved.Revision);
            Assert.Equal(1, entry.Revision);
            Assert.Equal("private-password", entry.Password);
            Assert.Single(Directory.GetFiles(root, "animego.yaml"));
            Assert.Empty(Directory.GetFiles(root, "*.json"));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
            await Assert.ThrowsAsync<DownloaderOverrideRevisionException>(() =>
                store.UpsertAsync("pt-main", entry, expectedRevision: 0));

            var deleted = await store.DeleteAsync("PT-MAIN", expectedRevision: 1);
            Assert.Equal(2, deleted.Revision);
            Assert.Empty(deleted.Downloaders);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateIncrementsEntryRevisionAndReplacesCredential()
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-downloader-overrides", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new DownloaderOverrideStore(root);
            _ = await store.UpsertAsync(
                "bt", Entry("http://localhost:8080", "first", "old", "C:\\downloads\\bt"), 0);
            var saved = await store.UpsertAsync(
                "bt", Entry("http://localhost:8081", "second", "new", "C:\\downloads\\bt"), 1);

            var entry = saved.Downloaders["bt"];
            Assert.Equal(2, entry.Revision);
            Assert.Equal("second", entry.Username);
            Assert.Equal("new", entry.Password);
            Assert.Equal("http://localhost:8081", entry.BaseUrl);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplicationStartupAppliesPrivateOverridesBeforeValidationAndRegistryCreation()
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-downloader-overrides", Guid.NewGuid().ToString("N"));
        var options = AnimeGoDefaults.CreateNative(root);
        var layout = DirectoryLayout.From(options.Paths);
        layout.CreateDataDirectories();
        try
        {
            using (var store = new DownloaderOverrideStore(layout.ConfigurationPath, Path.Combine(options.Paths.DataPath, "animego.yaml")))
            {
                _ = await store.UpsertAsync(
                    "archive",
                    Entry(
                        "http://127.0.0.1:9090/",
                        "archive-user",
                        "archive-secret",
                        Path.Combine(options.Paths.DownloadPath, "archive")),
                    0);
            }
            await using var app = await AnimeGoApplication.BuildAsync(
                [], options, startBackgroundWorkers: false);
            var effective = app.Services.GetRequiredService<AnimeGoOptions>();
            var archive = effective.Downloaders["archive"];
            var runtime = app.Services.GetRequiredService<DownloaderConfigurationRuntimeState>();

            Assert.Equal(new Uri("http://127.0.0.1:9090/"), archive.BaseUrl);
            Assert.Equal("archive-user", archive.Username);
            Assert.Equal("archive-secret", archive.Password);
            Assert.Equal(1, runtime.AppliedRevision);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static DownloaderOverrideEntry Entry(
        string url,
        string? username,
        string? password,
        string path) =>
        new(
            url, username, password, path, true, 0,
            DateTimeOffset.Parse(
                "2026-07-26T10:00:00Z",
                System.Globalization.CultureInfo.InvariantCulture));
}
