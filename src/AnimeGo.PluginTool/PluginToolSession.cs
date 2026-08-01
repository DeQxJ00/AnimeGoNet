using System.Text.Json;
using AnimeGoNet.App.Plugins;

namespace AnimeGo.PluginTool;

internal interface IPluginToolSession : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    Task<JsonElement> ExecuteAsync(
        string operation,
        JsonElement payload,
        JsonElement config,
        CancellationToken cancellationToken);

    Task<bool> HealthAsync(CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);
}

internal interface IPluginToolSessionFactory
{
    IPluginToolSession Create(
        ExternalPluginManifestLoader loader,
        ExternalPluginPackage package,
        string dataPath,
        TimeSpan executeTimeout);
}

internal sealed class PluginToolSessionFactory : IPluginToolSessionFactory
{
    public IPluginToolSession Create(
        ExternalPluginManifestLoader loader,
        ExternalPluginPackage package,
        string dataPath,
        TimeSpan executeTimeout) =>
        new PluginToolSession(new ExternalPluginProcessSession(
            loader,
            package,
            dataPath,
            new ExternalPluginSessionOptions { ExecuteTimeout = executeTimeout }));
}

internal sealed class PluginToolSession(ExternalPluginProcessSession session) : IPluginToolSession
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        session.StartAsync("AnimeGo.PluginTool/1.0.0", cancellationToken);

    public Task<JsonElement> ExecuteAsync(
        string operation,
        JsonElement payload,
        JsonElement config,
        CancellationToken cancellationToken) =>
        session.ExecuteAsync(operation, payload, config, cancellationToken: cancellationToken);

    public Task<bool> HealthAsync(CancellationToken cancellationToken) =>
        session.HealthAsync(cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken) =>
        session.ShutdownAsync("fixture_complete", cancellationToken);

    public ValueTask DisposeAsync() => session.DisposeAsync();
}
