using AnimeGoNet.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class WebBindingConfigurationTests
{
    [Fact]
    public void NativeAndDockerDefaultsAreStronglyTypedAndEnvironmentCompatible()
    {
        var nativeConfiguration = new ConfigurationManager();
        nativeConfiguration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ANIMEGO_WEB_HOST"] = "localhost",
            ["ANIMEGO_WEB_PORT"] = "8123",
        });

        var native = AnimeGoApplication.LoadOptions(nativeConfiguration, inContainer: false);
        var docker = AnimeGoApplication.LoadOptions(new ConfigurationManager(), inContainer: true);

        Assert.Equal("localhost", native.Web.Host);
        Assert.Equal(8123, native.Web.Port);
        Assert.Equal("0.0.0.0", docker.Web.Host);
        Assert.Equal(7991, docker.Web.Port);
    }

    [Fact]
    public async Task LegacyWebHostAndPortDriveTheActualKestrelListener()
    {
        var root = CreateRoot();
        WebApplication? app = null;
        try
        {
            app = await AnimeGoApplication.BuildAsync(
                BaseArguments(root)
                    .Concat([
                        "--ANIMEGO_WEB_HOST", "127.0.0.1",
                        "--ANIMEGO_WEB_PORT", "0",
                    ])
                    .ToArray(),
                startBackgroundWorkers: false);

            await app.StartAsync();

            var options = app.Services.GetRequiredService<AnimeGoOptions>();
            Assert.Equal("127.0.0.1", options.Web.Host);
            Assert.Equal(0, options.Web.Port);
            var address = Assert.Single(app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses);
            var uri = new Uri(address);
            Assert.Equal("127.0.0.1", uri.Host);
            Assert.InRange(uri.Port, 1, 65535);
        }
        finally
        {
            await DisposeAsync(app);
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task StandardUrlsSettingOverridesLegacyHostAndPort()
    {
        var root = CreateRoot();
        WebApplication? app = null;
        try
        {
            app = await AnimeGoApplication.BuildAsync(
                BaseArguments(root)
                    .Concat([
                        "--ANIMEGO_WEB_HOST", "0.0.0.0",
                        "--ANIMEGO_WEB_PORT", "65000",
                        "--urls", "http://127.0.0.1:0",
                    ])
                    .ToArray(),
                startBackgroundWorkers: false);

            await app.StartAsync();

            var options = app.Services.GetRequiredService<AnimeGoOptions>();
            Assert.Equal("0.0.0.0", options.Web.Host);
            Assert.Equal(65000, options.Web.Port);
            var address = Assert.Single(app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses);
            var uri = new Uri(address);
            Assert.Equal("127.0.0.1", uri.Host);
            Assert.NotEqual(65000, uri.Port);
        }
        finally
        {
            await DisposeAsync(app);
            DeleteRoot(root);
        }
    }

    private static string[] BaseArguments(string root) =>
    [
        "--data_path", Path.Combine(root, "data"),
        "--download_path", Path.Combine(root, "download"),
        "--save_path", Path.Combine(root, "library"),
        "--background_workers_enabled", "false",
    ];

    private static async Task DisposeAsync(WebApplication? app)
    {
        if (app is null)
        {
            return;
        }

        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-web-binding",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
