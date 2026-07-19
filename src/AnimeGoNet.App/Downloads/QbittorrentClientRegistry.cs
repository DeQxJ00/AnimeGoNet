using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;

namespace AnimeGoNet.App.Downloads;

public sealed class QbittorrentClientRegistry : IDownloadClientRegistry, IDisposable
{
    private readonly Dictionary<string, (IDownloadClient Client, HttpClient HttpClient)> _clients;

    public QbittorrentClientRegistry(AnimeGoOptions options)
    {
        _clients = new Dictionary<string, (IDownloadClient, HttpClient)>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in options.Downloaders.Where(pair => pair.Value.Enabled))
        {
            if (!string.Equals(pair.Value.Type, DownloaderTypes.Qbittorrent, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported downloader type for '{pair.Key}'.");
            }

            var httpClient = new HttpClient(new HttpClientHandler { UseCookies = true })
            {
                BaseAddress = pair.Value.BaseUrl,
                Timeout = TimeSpan.FromSeconds(30),
            };
            _clients.Add(pair.Key, (new QbittorrentClient(httpClient, pair.Value), httpClient));
        }
    }

    public IReadOnlyCollection<string> InstanceIds => _clients.Keys;

    public IDownloadClient GetRequired(string instanceId)
    {
        if (!_clients.TryGetValue(instanceId, out var client))
        {
            throw new KeyNotFoundException($"Downloader instance '{instanceId}' is not enabled or configured.");
        }

        return client.Client;
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.HttpClient.Dispose();
        }
    }
}
