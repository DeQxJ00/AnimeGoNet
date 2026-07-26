using System.Globalization;
using System.Net.Http.Json;
using AnimeGoNet.App.Serialization;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;

namespace AnimeGoNet.App.Downloads;

public sealed class QbittorrentClient(HttpClient httpClient, QbittorrentInstanceOptions options)
    : IDownloadClient, IDownloadClientDiagnostics
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
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(body.Trim(), "Ok.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("qBittorrent authentication failed.");
        }
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/v2/app/version", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
    }

    public async Task<string> GetDefaultSavePathAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            "api/v2/app/defaultSavePath", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
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
            item.Eta < 0 || item.Eta >= 8_640_000 ? null : item.Eta,
            item.Seeds,
            item.Peers)).ToArray();
    }

    public async Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(command.SeedingTimeMinutes, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            command.SeedingTimeMinutes,
            SourceDownloadPolicy.MaximumSeedingTimeMinutes);
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(command.Torrent), "torrents", command.FileName);
        content.Add(new StringContent(command.SavePath), "savepath");
        AddIfPresent(content, "rename", command.Rename);
        AddIfPresent(content, "category", command.Category);
        if (command.Tags.Count > 0)
        {
            content.Add(new StringContent(string.Join(',', command.Tags)), "tags");
        }
        content.Add(
            new StringContent(command.SeedingTimeMinutes.ToString(CultureInfo.InvariantCulture)),
            "seedingTimeLimit");

        var paused = command.StartPaused.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        content.Add(new StringContent(paused), "stopped");
        content.Add(new StringContent(paused), "paused");
        using var response = await _httpClient.PostAsync("api/v2/torrents/add", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<DownloadFileSnapshot>> ListFilesAsync(
        string hash,
        CancellationToken cancellationToken = default)
    {
        ValidateHash(hash);
        using var response = await _httpClient.GetAsync(
            $"api/v2/torrents/files?hash={Uri.EscapeDataString(hash)}",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var files = await response.Content.ReadFromJsonAsync(
            ApiJsonContext.Default.QbittorrentTorrentFileArray,
            cancellationToken).ConfigureAwait(false) ?? [];
        return files.Select(file => new DownloadFileSnapshot(
            file.Index,
            file.Name.Replace('\\', '/'),
            file.Size,
            Math.Clamp(file.Progress, 0, 1),
            file.Priority)).ToArray();
    }

    public async Task SetFilePriorityAsync(
        string hash,
        IReadOnlyList<int> fileIndexes,
        int priority,
        CancellationToken cancellationToken = default)
    {
        ValidateHash(hash);
        ArgumentNullException.ThrowIfNull(fileIndexes);
        if (fileIndexes.Count == 0)
        {
            throw new ArgumentException("At least one file index is required.", nameof(fileIndexes));
        }

        if (fileIndexes.Any(index => index < 0) || fileIndexes.Distinct().Count() != fileIndexes.Count)
        {
            throw new ArgumentException("File indexes must be unique non-negative integers.", nameof(fileIndexes));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(priority, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(priority, 7);
        using var response = await _httpClient.PostAsync(
            "api/v2/torrents/filePrio",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["hash"] = hash,
                ["id"] = string.Join('|', fileIndexes),
                ["priority"] = priority.ToString(CultureInfo.InvariantCulture),
            }),
            cancellationToken).ConfigureAwait(false);
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

    private static void ValidateHash(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        if (hash.Length is not (40 or 64) || hash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Torrent hash must be a hexadecimal v1 or v2 info hash.", nameof(hash));
        }
    }

    private static HttpClient Configure(HttpClient client, QbittorrentInstanceOptions configuredOptions)
    {
        client.BaseAddress ??= configuredOptions.BaseUrl;
        return client;
    }
}
