using System.Globalization;
using System.Net.Http.Json;
using AnimeGoNet.App.Serialization;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;

namespace AnimeGoNet.App.Downloads;

public sealed class QbittorrentClient(HttpClient httpClient, QbittorrentInstanceOptions options) : IDownloadClient
{
    private readonly HttpClient _httpClient = Configure(httpClient, options);

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v2/auth/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = options.Username ?? string.Empty,
                ["password"] = options.Password ?? string.Empty,
            }),
        };
        request.Headers.Referrer = options.BaseUrl;
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(body.Trim(), "Ok.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("qBittorrent authentication failed.");
        }
    }

    public async Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/v2/torrents/info", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync(
            ApiJsonContext.Default.QbittorrentTorrentInfoArray,
            cancellationToken).ConfigureAwait(false) ?? [];
        return items.Select(item => new DownloadTaskSnapshot(
            item.Hash,
            item.Name,
            QbittorrentStateMapper.Map(item.State, item.Progress),
            item.Progress,
            item.Downloaded,
            item.Size,
            item.DownloadSpeed,
            item.Eta < 0 || item.Eta >= 8_640_000 ? null : item.Eta)).ToArray();
    }

    public async Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(command.Torrent), "torrents", command.FileName);
        content.Add(new StringContent(command.SavePath), "savepath");
        AddIfPresent(content, "rename", command.Rename);
        AddIfPresent(content, "category", command.Category);
        if (command.Tags.Count > 0)
        {
            content.Add(new StringContent(string.Join(',', command.Tags)), "tags");
        }

        var paused = command.StartPaused.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        content.Add(new StringContent(paused), "stopped");
        content.Add(new StringContent(paused), "paused");
        using var response = await _httpClient.PostAsync("api/v2/torrents/add", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public Task PauseAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default) =>
        PostHashesAsync("api/v2/torrents/stop", hashes, null, cancellationToken);

    public Task ResumeAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default) =>
        PostHashesAsync("api/v2/torrents/start", hashes, null, cancellationToken);

    public Task DeleteAsync(
        IReadOnlyList<string> hashes,
        bool deleteFiles,
        CancellationToken cancellationToken = default) =>
        PostHashesAsync("api/v2/torrents/delete", hashes, deleteFiles, cancellationToken);

    private async Task PostHashesAsync(
        string path,
        IReadOnlyList<string> hashes,
        bool? deleteFiles,
        CancellationToken cancellationToken)
    {
        if (hashes.Count == 0)
        {
            throw new ArgumentException("At least one torrent hash is required.", nameof(hashes));
        }

        var values = new Dictionary<string, string>
        {
            ["hashes"] = string.Join('|', hashes),
        };
        if (deleteFiles is not null)
        {
            values["deleteFiles"] = deleteFiles.Value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        }

        using var response = await _httpClient.PostAsync(
            path,
            new FormUrlEncodedContent(values),
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static void AddIfPresent(MultipartFormDataContent content, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            content.Add(new StringContent(value), name);
        }
    }

    private static HttpClient Configure(HttpClient client, QbittorrentInstanceOptions configuredOptions)
    {
        client.BaseAddress ??= configuredOptions.BaseUrl;
        return client;
    }
}
