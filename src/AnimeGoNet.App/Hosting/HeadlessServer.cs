using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;

namespace AnimeGoNet.App.Hosting;

internal sealed class HeadlessServer : IServer
{
    public HeadlessServer()
    {
        Features.Set<IServerAddressesFeature>(new ServerAddressesFeature());
    }

    public IFeatureCollection Features { get; } = new FeatureCollection();

    public Task StartAsync<TContext>(
        IHttpApplication<TContext> application,
        CancellationToken cancellationToken)
        where TContext : notnull
    {
        ArgumentNullException.ThrowIfNull(application);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
