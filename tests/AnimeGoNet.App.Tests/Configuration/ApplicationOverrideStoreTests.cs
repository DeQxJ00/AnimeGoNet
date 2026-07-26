using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class ApplicationOverrideStoreTests
{
    [Fact]
    public async Task SaveReloadDeleteUseAtomicVersionedPrivateFile()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-application-overrides",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new ApplicationOverrideStore(root);
            var initial = await store.LoadAsync();
            var saved = await store.SaveAsync(Entry(), 0);
            using var reloader = new ApplicationOverrideStore(root);
            var reloaded = await reloader.LoadAsync();

            Assert.Equal(0, initial.Revision);
            Assert.Equal(1, saved.Revision);
            Assert.Equal("private-api-key", reloaded.Settings?.TmdbApiKey);
            Assert.Equal("private-read-token", reloaded.Settings?.TmdbReadAccessToken);
            Assert.Single(Directory.GetFiles(root, "application.private.json"));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
            await Assert.ThrowsAsync<ApplicationOverrideRevisionException>(() =>
                store.SaveAsync(Entry(), 0));

            var deleted = await store.DeleteAsync(1);
            Assert.Equal(2, deleted.Revision);
            Assert.Null(deleted.Settings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplicationStartupAppliesPrivateSettingsBeforeClientConstruction()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-application-overrides",
            Guid.NewGuid().ToString("N"));
        var options = AnimeGoDefaults.CreateNative(root);
        var layout = DirectoryLayout.From(options.Paths);
        layout.CreateDataDirectories();
        try
        {
            using (var store = new ApplicationOverrideStore(layout.ConfigurationPath))
            {
                _ = await store.SaveAsync(Entry(), 0);
            }

            await using var app = await AnimeGoApplication.BuildAsync(
                [],
                options,
                startBackgroundWorkers: false);
            var effective = app.Services.GetRequiredService<AnimeGoOptions>();
            var runtime = app.Services.GetRequiredService<ApplicationConfigurationRuntimeState>();
            var deployment = app.Services.GetRequiredService<DeploymentConfigurationOptions>();

            Assert.Equal(new Uri("https://tmdb.test.invalid/"), effective.Metadata.Tmdb.BaseUrl);
            Assert.Equal("en-US", effective.Metadata.Tmdb.Language);
            Assert.Equal("private-api-key", effective.Metadata.Tmdb.ApiKey);
            Assert.Equal("private-read-token", effective.Metadata.Tmdb.ReadAccessToken);
            Assert.True(effective.Metadata.SeasonFailure.Backtrace);
            Assert.True(effective.Metadata.Ai.UseEpisodeMatch);
            Assert.Equal(TimeSpan.FromSeconds(600), effective.Metadata.Ai.HttpTimeout);
            Assert.Equal(2, effective.TorrentFetch.MaxRedirects);
            Assert.Equal(1, runtime.AppliedRevision);
            Assert.Equal("zh-CN", deployment.Value.Metadata.Tmdb.Language);
            Assert.Null(deployment.Value.Metadata.Tmdb.ApiKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ApplicationOverrideEntry Entry() => new(
        "https://tmdb.test.invalid/",
        "en-US",
        30,
        true,
        "private-api-key",
        true,
        "private-read-token",
        false,
        true,
        true,
        false,
        false,
        true,
        600,
        false,
        true,
        30,
        16 * 1024 * 1024,
        2,
        900,
        DateTimeOffset.Parse(
            "2026-07-26T12:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture));
}
