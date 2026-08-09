using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Torrents;

namespace AnimeGoNet.App.Tests.Delivery;

public sealed class ContainerE2EFixtureProcessTests
{
    [Fact]
    public async Task ServesDeterministicLegalTorrentPayloadAndMetadataGraph()
    {
        var assembly = Path.Combine(
            AppContext.BaseDirectory,
            "container-e2e-fixture",
            "AnimeGoNet.ContainerE2EFixture.dll");
        Assert.True(File.Exists(assembly), $"Fixture assembly is missing: {assembly}");
        var port = FreeLoopbackPort();
        var baseUrl = $"http://127.0.0.1:{port}/";
        using var process = StartFixture(assembly, baseUrl);
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };

        try
        {
            using var ready = await WaitForReadyAsync(client, process);
            var infoHash = ready.RootElement.GetProperty("info_hash").GetString();
            var fileName = ready.RootElement.GetProperty("file_name").GetString();
            var payloadSha256 = ready.RootElement.GetProperty("payload_sha256").GetString();
            Assert.Equal("AnimeGoNet.Container.E2E.S01E01.mkv", fileName);
            Assert.Equal(128 * 1024, ready.RootElement.GetProperty("size_bytes").GetInt32());

            var torrent = await client.GetByteArrayAsync("animegonet-container-e2e.torrent");
            var metadata = TorrentMetainfoParser.Parse(torrent);
            Assert.Equal(infoHash, metadata.InfoHash);
            Assert.Equal(fileName, Assert.Single(metadata.Files).RelativePath);
            Assert.Contains(
                $"url-list{baseUrl.Length + "payload/".Length + fileName!.Length}:" +
                $"{baseUrl}payload/{fileName}",
                Encoding.ASCII.GetString(torrent),
                StringComparison.Ordinal);

            var payload = await client.GetByteArrayAsync($"payload/{fileName}");
            Assert.Equal(payloadSha256, Convert.ToHexStringLower(SHA256.HashData(payload)));
            Assert.Equal(128 * 1024, payload.Length);

            var search = await client.GetStringAsync(
                "tmdb/3/discover/tv?api_key=container-e2e-tmdb-key");
            var series = await client.GetStringAsync(
                "tmdb/3/tv/990001?api_key=container-e2e-tmdb-key");
            var season = await client.GetStringAsync(
                "tmdb/3/tv/990001/season/1?api_key=container-e2e-tmdb-key");
            var episode = await client.GetStringAsync(
                "tmdb/3/tv/990001/season/1/episode/1?api_key=container-e2e-tmdb-key");
            var bangumi = await client.GetStringAsync("bangumi/v0/subjects/990001");
            var bangumiEpisodes = await client.GetStringAsync(
                "bangumi/v0/episodes?subject_id=990001&type=0&limit=200&offset=0");
            Assert.Contains("\"id\":990001", search + series + bangumi, StringComparison.Ordinal);
            Assert.Contains("\"season_number\":1", season, StringComparison.Ordinal);
            Assert.Contains("\"episode_number\":1", episode, StringComparison.Ordinal);
            Assert.Contains("\"ep\":1", bangumiEpisodes, StringComparison.Ordinal);

            using var state = JsonDocument.Parse(await client.GetStringAsync("__state"));
            Assert.Equal(1, state.RootElement.GetProperty("torrent_requests").GetInt32());
            Assert.Equal(1, state.RootElement.GetProperty("payload_requests").GetInt32());
            Assert.Equal(0, state.RootElement.GetProperty("tmdb_credential_failures").GetInt32());
            Assert.Equal(1, state.RootElement.GetProperty("bangumi_subject_requests").GetInt32());
            Assert.Equal(1, state.RootElement.GetProperty("bangumi_episode_requests").GetInt32());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static Process StartFixture(string assembly, string baseUrl)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(assembly);
        start.Environment["ANIMEGONET_FIXTURE_LISTEN_URL"] = baseUrl;
        start.Environment["ANIMEGONET_FIXTURE_PUBLIC_BASE_URL"] = baseUrl;
        return Process.Start(start) ?? throw new InvalidOperationException("Fixture process did not start.");
    }

    private static async Task<JsonDocument> WaitForReadyAsync(HttpClient client, Process process)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (process.HasExited)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException(
                    $"Fixture exited with {process.ExitCode}: {error}");
            }
            try
            {
                return JsonDocument.Parse(await client.GetStringAsync("ready"));
            }
            catch (HttpRequestException) when (attempt < 79)
            {
                await Task.Delay(100);
            }
        }
        throw new TimeoutException("Container E2E fixture did not become ready.");
    }

    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
