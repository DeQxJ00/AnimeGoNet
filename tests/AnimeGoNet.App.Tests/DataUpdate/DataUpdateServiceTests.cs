using System.Net;
using AnimeGoNet.App.DataUpdate;
using AnimeGoNet.Data.DataUpdate;

namespace AnimeGoNet.App.Tests.DataUpdate;

public sealed class DataUpdateServiceTests
{
    [Fact]
    public async Task CheckOnlyReportsAvailableWithoutDownloadingAssets()
    {
        await using var fixture = await DataUpdateServiceFixture.CreateAsync();
        var release = fixture.AddRelease("2026.07.29.1");

        var result = await fixture.Service.ExecuteAsync(
            DataUpdateTriggerKinds.Manual,
            DataUpdateActions.Check);
        var run = await fixture.Transfers.GetLastRunAsync();

        Assert.Equal(DataUpdateTransferStatuses.UpdateAvailable, result.Status);
        Assert.False(result.Downloaded);
        Assert.False(result.Imported);
        Assert.Equal([fixture.Options.DataUpdate.ManifestUrl!], fixture.Handler.Requests);
        Assert.Equal(DataUpdateTransferStatuses.UpdateAvailable, run!.Status);
        Assert.Equal(release.Version, run.DataVersion);
        Assert.Null((await fixture.Packages.GetStatusAsync()).ActiveVersion);
    }

    [Fact]
    public async Task DownloadOnlyPersistsVerifiedPackageWithoutActivatingIt()
    {
        await using var fixture = await DataUpdateServiceFixture.CreateAsync();
        var release = fixture.AddRelease("2026.07.29.1");

        var result = await fixture.Service.ExecuteAsync(
            DataUpdateTriggerKinds.Manual,
            DataUpdateActions.Download);
        var download = Assert.Single(await fixture.Transfers.ListDownloadsAsync());

        Assert.Equal(DataUpdateTransferStatuses.Downloaded, result.Status);
        Assert.True(result.Downloaded);
        Assert.False(result.Imported);
        Assert.Equal("verified", download.State);
        Assert.True(Directory.Exists(Path.Combine(
            fixture.Layout.DataUpdatePath,
            download.RelativeDirectory)));
        Assert.Equal(
            [
                fixture.Options.DataUpdate.ManifestUrl!,
                release.SubjectUrl,
                release.EpisodeUrl,
            ],
            fixture.Handler.Requests);
        Assert.Null((await fixture.Packages.GetStatusAsync()).ActiveVersion);
    }

    [Fact]
    public async Task DownloadAndImportActivatesPackage()
    {
        await using var fixture = await DataUpdateServiceFixture.CreateAsync();
        fixture.AddRelease("2026.07.29.1");

        var result = await fixture.Service.ExecuteAsync(
            DataUpdateTriggerKinds.Scheduled,
            DataUpdateActions.DownloadImport);
        var package = await fixture.Packages.GetStatusAsync();
        var download = Assert.Single(await fixture.Transfers.ListDownloadsAsync());

        Assert.Equal(DataUpdateTransferStatuses.Completed, result.Status);
        Assert.True(result.Downloaded);
        Assert.True(result.Imported);
        Assert.Equal("2026.07.29.1", package.ActiveVersion);
        Assert.Equal("imported", download.State);
        Assert.NotNull(download.ImportedAtUtc);
        Assert.Equal(DataUpdateTriggerKinds.Scheduled, (await fixture.Transfers.GetLastRunAsync())!.TriggerKind);
    }

    [Fact]
    public async Task ActiveManifestReturnsUpToDateWithoutAssetRequests()
    {
        await using var fixture = await DataUpdateServiceFixture.CreateAsync();
        fixture.AddRelease("2026.07.29.1");
        await fixture.Service.ExecuteAsync(
            DataUpdateTriggerKinds.Manual,
            DataUpdateActions.DownloadImport);
        fixture.Handler.Requests.Clear();

        var result = await fixture.Service.ExecuteAsync(
            DataUpdateTriggerKinds.Manual,
            DataUpdateActions.DownloadImport);

        Assert.Equal(DataUpdateTransferStatuses.UpToDate, result.Status);
        Assert.False(result.Downloaded);
        Assert.Equal([fixture.Options.DataUpdate.ManifestUrl!], fixture.Handler.Requests);
    }

    [Fact]
    public async Task DownloadedPackageCanBeImportedLaterWithoutNetwork()
    {
        await using var fixture = await DataUpdateServiceFixture.CreateAsync();
        fixture.AddRelease("2026.07.29.1");
        await fixture.Service.ExecuteAsync(
            DataUpdateTriggerKinds.Manual,
            DataUpdateActions.Download);
        fixture.Handler.Requests.Clear();

        var result = await fixture.Service.ImportDownloadedAsync("2026.07.29.1");

        Assert.True(result.Imported);
        Assert.Empty(fixture.Handler.Requests);
        Assert.Equal("2026.07.29.1", (await fixture.Packages.GetStatusAsync()).ActiveVersion);
    }

    [Fact]
    public async Task CorruptAssetFailsAndPreservesPreviousActiveVersion()
    {
        await using var fixture = await DataUpdateServiceFixture.CreateAsync();
        fixture.AddRelease("2026.07.29.1");
        await fixture.Service.ExecuteAsync(
            DataUpdateTriggerKinds.Manual,
            DataUpdateActions.DownloadImport);
        fixture.AddRelease("2026.07.30.1", corruptEpisodeResponse: true);

        var exception = await Assert.ThrowsAsync<DataUpdateServiceException>(() =>
            fixture.Service.ExecuteAsync(
                DataUpdateTriggerKinds.Scheduled,
                DataUpdateActions.DownloadImport));
        var package = await fixture.Packages.GetStatusAsync();
        var run = await fixture.Transfers.GetLastRunAsync();

        Assert.Equal("data_asset_size_mismatch", exception.Code);
        Assert.Equal("2026.07.29.1", package.ActiveVersion);
        Assert.Equal(DataUpdateTransferStatuses.Failed, run!.Status);
        Assert.Equal(exception.Code, run.FailureCode);
        Assert.Empty(Directory.EnumerateDirectories(
            fixture.Layout.DataUpdatePath,
            ".partial-*",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ManifestHttpFailureUsesStableFailureCode()
    {
        await using var fixture = await DataUpdateServiceFixture.CreateAsync();
        fixture.AddRelease(
            "2026.07.29.1",
            manifestStatus: HttpStatusCode.InternalServerError);

        var exception = await Assert.ThrowsAsync<DataUpdateServiceException>(() =>
            fixture.Service.ExecuteAsync(
                DataUpdateTriggerKinds.Manual,
                DataUpdateActions.Check));

        Assert.Equal("data_manifest_http_failed", exception.Code);
        Assert.Equal(exception.Code, (await fixture.Transfers.GetLastRunAsync())!.FailureCode);
    }

    [Fact]
    public async Task MissingManifestConfigurationIsAudited()
    {
        await using var fixture = await DataUpdateServiceFixture.CreateAsync();
        var service = new DataUpdateService(
            new HttpClient(fixture.Handler),
            fixture.Options with
            {
                DataUpdate = fixture.Options.DataUpdate with { ManifestUrl = null },
            },
            fixture.Layout,
            fixture.Packages,
            fixture.Transfers,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 29, 14, 0, 0, TimeSpan.Zero)),
            new Version(1, 0, 0),
            ownsHttpClient: true);
        using (service)
        {
            var exception = await Assert.ThrowsAsync<DataUpdateServiceException>(() =>
                service.ExecuteAsync(
                    DataUpdateTriggerKinds.Manual,
                    DataUpdateActions.Check));
            Assert.Equal("data_manifest_url_missing", exception.Code);
        }

        Assert.Equal(
            "data_manifest_url_missing",
            (await fixture.Transfers.GetLastRunAsync())!.FailureCode);
    }
}
