using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.App.DataUpdate;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.DataUpdate;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.App.Tests.DataUpdate;

internal sealed class DataUpdateServiceFixture : IAsyncDisposable
{
    private readonly HttpClient _httpClient;

    private DataUpdateServiceFixture(
        string rootPath,
        AnimeGoOptions options,
        DirectoryLayout layout,
        AnimeGoSqliteDatabase database,
        RoutingHandler handler,
        HttpClient httpClient,
        DataPackageStore packages,
        DataUpdateTransferStore transfers,
        DataUpdateService service)
    {
        RootPath = rootPath;
        Options = options;
        Layout = layout;
        Database = database;
        Handler = handler;
        _httpClient = httpClient;
        Packages = packages;
        Transfers = transfers;
        Service = service;
    }

    public string RootPath { get; }

    public AnimeGoOptions Options { get; }

    public DirectoryLayout Layout { get; }

    public AnimeGoSqliteDatabase Database { get; }

    public RoutingHandler Handler { get; }

    public DataPackageStore Packages { get; }

    public DataUpdateTransferStore Transfers { get; }

    public DataUpdateService Service { get; }

    public static async Task<DataUpdateServiceFixture> CreateAsync(TimeSpan? httpTimeout = null)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-data-update-tests",
            Guid.NewGuid().ToString("N"));
        var defaults = AnimeGoDefaults.CreateNative(root);
        var options = defaults with
        {
            DataUpdate = defaults.DataUpdate with
            {
                ManifestUrl = new Uri("https://updates.test/manifest.json"),
                HttpTimeout = httpTimeout ?? TimeSpan.FromSeconds(10),
                KeepVersions = 2,
            },
        };
        var layout = DirectoryLayout.From(options.Paths);
        layout.CreateDataDirectories();
        var database = new AnimeGoSqliteDatabase(layout.DatabaseFile);
        await database.InitializeAsync();
        var handler = new RoutingHandler();
        var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var packages = new DataPackageStore(database);
        var transfers = new DataUpdateTransferStore(database);
        var service = new DataUpdateService(
            httpClient,
            options,
            layout,
            packages,
            transfers,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 29, 14, 0, 0, TimeSpan.Zero)),
            new Version(1, 0, 0));
        return new DataUpdateServiceFixture(
            root,
            options,
            layout,
            database,
            handler,
            httpClient,
            packages,
            transfers,
            service);
    }

    public ReleasePayload AddRelease(
        string version,
        bool corruptEpisodeResponse = false,
        HttpStatusCode manifestStatus = HttpStatusCode.OK)
    {
        var subjects = Gzip(
            """
            {"id":51,"name":"CLANNAD","name_cn":"CLANNAD","air_date":"2007-10-05","episode_count":1}

            """);
        var episodes = Gzip(
            """
            {"id":1423,"subject_id":51,"sort":1,"episode":"1","air_date":"2007-10-05"}

            """);
        var subjectName = $"subjects-{version}.jsonl.gz";
        var episodeName = $"episodes-{version}.jsonl.gz";
        var subjectUrl = new Uri($"https://updates.test/{subjectName}");
        var episodeUrl = new Uri($"https://updates.test/{episodeName}");
        var manifest = Encoding.UTF8.GetBytes(
            $$"""
            {
              "schema_version":1,
              "data_version":"{{version}}",
              "generated_at_utc":"2026-07-29T12:00:00.0000000+00:00",
              "minimum_client_version":"0.1.0",
              "upstream":{
                "repository":"https://github.com/bangumi/Archive",
                "release":"archive-2026-07-29",
                "asset":"bangumi-json.zip",
                "sha256":"{{new string('a', 64)}}"
              },
              "assets":[
                {
                  "kind":"subjects",
                  "file_name":"{{subjectName}}",
                  "url":"{{subjectUrl}}",
                  "size_bytes":{{subjects.LongLength}},
                  "sha256":"{{Sha256(subjects)}}",
                  "record_count":1,
                  "subject_id_min":1,
                  "subject_id_max":100
                },
                {
                  "kind":"episodes",
                  "file_name":"{{episodeName}}",
                  "url":"{{episodeUrl}}",
                  "size_bytes":{{episodes.LongLength}},
                  "sha256":"{{Sha256(episodes)}}",
                  "record_count":1,
                  "subject_id_min":1,
                  "subject_id_max":100
                }
              ],
              "totals":{"subjects":1,"episodes":1}
            }
            """);
        Handler.Set(
            Options.DataUpdate.ManifestUrl!,
            () => Response(manifestStatus, manifest));
        Handler.Set(subjectUrl, () => Response(HttpStatusCode.OK, subjects));
        Handler.Set(
            episodeUrl,
            () => Response(
                HttpStatusCode.OK,
                corruptEpisodeResponse ? [.. episodes, (byte)0] : episodes));
        return new ReleasePayload(
            version,
            manifest,
            subjects,
            episodes,
            subjectUrl,
            episodeUrl);
    }

    public async ValueTask DisposeAsync()
    {
        Service.Dispose();
        Packages.Dispose();
        _httpClient.Dispose();
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
        await ValueTask.CompletedTask;
    }

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] content) =>
        new(status)
        {
            Content = new ByteArrayContent(content),
        };

    private static byte[] Gzip(string value)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(value));
        }
        return output.ToArray();
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));
}

internal sealed record ReleasePayload(
    string Version,
    byte[] Manifest,
    byte[] Subjects,
    byte[] Episodes,
    Uri SubjectUrl,
    Uri EpisodeUrl);

internal sealed class RoutingHandler : HttpMessageHandler
{
    private readonly Dictionary<Uri, Func<HttpResponseMessage>> _routes = [];

    public List<Uri> Requests { get; } = [];

    public void Set(Uri uri, Func<HttpResponseMessage> response) =>
        _routes[uri] = response;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = request.RequestUri ?? throw new InvalidOperationException("Request URL is missing.");
        Requests.Add(uri);
        return Task.FromResult(
            _routes.TryGetValue(uri, out var response)
                ? response()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
