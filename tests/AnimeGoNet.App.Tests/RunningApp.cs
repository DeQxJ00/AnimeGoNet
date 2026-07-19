using AnimeGoNet.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests;

public sealed class RunningApp : IAsyncDisposable
{
    private RunningApp(WebApplication app, HttpClient client, string rootPath)
    {
        App = app;
        Client = client;
        RootPath = rootPath;
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public string RootPath { get; }

    public static async Task<RunningApp> StartAsync(string? accessKey = null)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "animegonet-app-tests", Guid.NewGuid().ToString("N"));
        var options = AnimeGoDefaults.CreateNative(rootPath);
        var app = await AnimeGoApplication.BuildAsync([], options, accessKey);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var server = app.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        return new RunningApp(app, new HttpClient { BaseAddress = new Uri(address) }, rootPath);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
