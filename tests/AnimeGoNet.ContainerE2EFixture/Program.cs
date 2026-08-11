using System.Globalization;
using System.Security.Cryptography;
using System.Text;

const string FileName = "AnimeGoNet.Container.E2E.S01E01.mkv";
const string RouteFileName = "animegonet-mikan-route.bin";
const int PayloadLength = 128 * 1024;
const int PieceLength = 16 * 1024;
const string TmdbApiKey = "container-e2e-tmdb-key";
var publicBaseUrl = Environment.GetEnvironmentVariable("ANIMEGONET_FIXTURE_PUBLIC_BASE_URL")
    ?? "http://container-e2e-fixture.invalid:8089/";
if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicBase)
    || publicBase.Scheme != Uri.UriSchemeHttp
    || !string.IsNullOrEmpty(publicBase.UserInfo)
    || !string.IsNullOrEmpty(publicBase.Fragment))
{
    throw new InvalidOperationException("Container fixture public base URL is invalid.");
}

var payload = CreatePayload();
var payloadSha256 = Convert.ToHexStringLower(SHA256.HashData(payload));
var torrent = BuildTorrent(
    FileName,
    payload,
    new Uri(publicBase, $"payload/{FileName}"),
    out var infoHash);
var routePayload = new byte[] { 17, 34, 51, 68, 85 };
var routeTorrent = BuildTorrent(
    RouteFileName,
    routePayload,
    new Uri(publicBase, $"payload/{RouteFileName}"),
    out var routeInfoHash);
var state = new FixtureState();
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls(
    Environment.GetEnvironmentVariable("ANIMEGONET_FIXTURE_LISTEN_URL")
    ?? "http://0.0.0.0:8089");
var app = builder.Build();

app.MapGet("/ready", () => Results.Text(
    $$"""{"info_hash":"{{infoHash}}","file_name":"{{FileName}}","size_bytes":{{PayloadLength}},"payload_sha256":"{{payloadSha256}}"}""",
    "application/json"));
app.MapGet("/route-ready", () => Results.Text(
    $$"""{"info_hash":"{{routeInfoHash}}","file_name":"{{RouteFileName}}","size_bytes":{{routePayload.Length}}}""",
    "application/json"));
app.MapGet("/animegonet-container-e2e.torrent", () =>
{
    state.RecordTorrent();
    return Results.Bytes(torrent, "application/x-bittorrent");
});
app.MapGet("/animegonet-route-smoke.torrent", () =>
{
    state.RecordTorrent();
    return Results.Bytes(routeTorrent, "application/x-bittorrent");
});
app.MapGet($"/payload/{FileName}", () =>
{
    state.RecordPayload();
    return Results.Bytes(payload, "application/octet-stream");
});
app.MapGet($"/payload/{RouteFileName}", () => Results.Bytes(
    routePayload,
    "application/octet-stream"));
app.MapGet("/tmdb/3/discover/tv", (HttpContext context) =>
{
    state.RecordTmdbSearch(HasTmdbCredential(context));
    return Json("""
        {"total_results":1,"results":[{"id":990001,"name":"AnimeGoNet Container E2E","original_name":"AnimeGoNet Container E2E","first_air_date":"2026-01-01","poster_path":null}]}
        """);
});
app.MapGet("/tmdb/3/tv/990001", (HttpContext context) =>
{
    state.RecordTmdbSeries(HasTmdbCredential(context));
    return Json("""
        {"id":990001,"name":"AnimeGoNet Container E2E","original_name":"AnimeGoNet Container E2E","first_air_date":"2026-01-01","poster_path":null,"seasons":[{"id":990011,"name":"Season 1","season_number":1,"air_date":"2026-01-01","episode_count":1,"poster_path":null}]}
        """);
});
app.MapGet("/tmdb/3/tv/990001/season/1", (HttpContext context) =>
{
    state.RecordTmdbSeason(HasTmdbCredential(context));
    return Json("""
        {"id":990011,"name":"Season 1","season_number":1,"air_date":"2026-01-01","episode_count":1,"poster_path":null,"episodes":[{"id":990111,"name":"Container Episode 1","air_date":"2026-01-01","season_number":1,"episode_number":1}]}
        """);
});
app.MapGet("/tmdb/3/tv/990001/season/1/episode/1", (HttpContext context) =>
{
    state.RecordTmdbEpisode(HasTmdbCredential(context));
    return Json("""
        {"id":990111,"name":"Container Episode 1","air_date":"2026-01-01","season_number":1,"episode_number":1}
        """);
});
app.MapGet("/bangumi/v0/subjects/990001", () =>
{
    state.RecordBangumiSubject();
    return Json("""
        {"id":990001,"name":"AnimeGoNet Container E2E","name_cn":"AnimeGoNet Container E2E","date":"2026-01-01","eps":1,"total_episodes":1}
        """);
});
app.MapGet("/bangumi/v0/episodes", () =>
{
    state.RecordBangumiEpisodes();
    return Json("""
        {"total":1,"limit":200,"offset":0,"data":[{"id":990111,"type":0,"ep":1,"airdate":"2026-01-01"}]}
        """);
});
app.MapGet("/__state", () => Results.Text(state.ToJson(), "application/json"));

await app.RunAsync();

static IResult Json(string value) => Results.Text(value, "application/json");

static bool HasTmdbCredential(HttpContext context) =>
    string.Equals(context.Request.Query["api_key"], TmdbApiKey, StringComparison.Ordinal);

static byte[] CreatePayload()
{
    var result = new byte[PayloadLength];
    for (var index = 0; index < result.Length; index++)
    {
        result[index] = (byte)((index * 31 + 17) % 251);
    }
    return result;
}

static byte[] BuildTorrent(
    string fileName,
    byte[] content,
    Uri webSeed,
    out string infoHash)
{
    using var info = new MemoryStream();
    WriteAscii(info, "d");
    WriteString(info, "length");
    WriteInteger(info, content.LongLength);
    WriteString(info, "name");
    WriteString(info, fileName);
    WriteString(info, "piece length");
    WriteInteger(info, PieceLength);
    WriteString(info, "pieces");
    using (var hashes = new MemoryStream())
    {
        for (var offset = 0; offset < content.Length; offset += PieceLength)
        {
            var count = Math.Min(PieceLength, content.Length - offset);
#pragma warning disable CA5350 // BitTorrent v1 mandates SHA-1 piece and info hashes.
            hashes.Write(SHA1.HashData(content.AsSpan(offset, count)));
#pragma warning restore CA5350
        }
        WriteBytes(info, hashes.ToArray());
    }
    WriteAscii(info, "e");
    var infoBytes = info.ToArray();
#pragma warning disable CA5350 // BitTorrent v1 mandates SHA-1 piece and info hashes.
    infoHash = Convert.ToHexStringLower(SHA1.HashData(infoBytes));
#pragma warning restore CA5350

    using var output = new MemoryStream();
    WriteAscii(output, "d");
    WriteString(output, "announce");
    WriteString(output, "http://127.0.0.1:9/announce");
    WriteString(output, "info");
    output.Write(infoBytes);
    WriteString(output, "url-list");
    WriteString(output, webSeed.AbsoluteUri);
    WriteAscii(output, "e");
    return output.ToArray();
}

static void WriteString(Stream stream, string value) =>
    WriteBytes(stream, Encoding.UTF8.GetBytes(value));

static void WriteBytes(Stream stream, byte[] value)
{
    WriteAscii(stream, value.Length.ToString(CultureInfo.InvariantCulture));
    WriteAscii(stream, ":");
    stream.Write(value);
}

static void WriteInteger(Stream stream, long value) =>
    WriteAscii(stream, $"i{value.ToString(CultureInfo.InvariantCulture)}e");

static void WriteAscii(Stream stream, string value) =>
    stream.Write(Encoding.ASCII.GetBytes(value));

internal sealed class FixtureState
{
    private int _torrentRequests;
    private int _payloadRequests;
    private int _tmdbSearchRequests;
    private int _tmdbSeriesRequests;
    private int _tmdbSeasonRequests;
    private int _tmdbEpisodeRequests;
    private int _tmdbCredentialFailures;
    private int _bangumiSubjectRequests;
    private int _bangumiEpisodeRequests;

    public void RecordTorrent() => Interlocked.Increment(ref _torrentRequests);

    public void RecordPayload() => Interlocked.Increment(ref _payloadRequests);

    public void RecordTmdbSearch(bool authorized)
    {
        Interlocked.Increment(ref _tmdbSearchRequests);
        if (!authorized) Interlocked.Increment(ref _tmdbCredentialFailures);
    }

    public void RecordTmdbSeries(bool authorized)
    {
        Interlocked.Increment(ref _tmdbSeriesRequests);
        if (!authorized) Interlocked.Increment(ref _tmdbCredentialFailures);
    }

    public void RecordTmdbSeason(bool authorized)
    {
        Interlocked.Increment(ref _tmdbSeasonRequests);
        if (!authorized) Interlocked.Increment(ref _tmdbCredentialFailures);
    }

    public void RecordTmdbEpisode(bool authorized)
    {
        Interlocked.Increment(ref _tmdbEpisodeRequests);
        if (!authorized) Interlocked.Increment(ref _tmdbCredentialFailures);
    }

    public void RecordBangumiSubject() => Interlocked.Increment(ref _bangumiSubjectRequests);

    public void RecordBangumiEpisodes() => Interlocked.Increment(ref _bangumiEpisodeRequests);

    public string ToJson() => $$"""
        {"torrent_requests":{{Volatile.Read(ref _torrentRequests)}},"payload_requests":{{Volatile.Read(ref _payloadRequests)}},"tmdb_search_requests":{{Volatile.Read(ref _tmdbSearchRequests)}},"tmdb_series_requests":{{Volatile.Read(ref _tmdbSeriesRequests)}},"tmdb_season_requests":{{Volatile.Read(ref _tmdbSeasonRequests)}},"tmdb_episode_requests":{{Volatile.Read(ref _tmdbEpisodeRequests)}},"tmdb_credential_failures":{{Volatile.Read(ref _tmdbCredentialFailures)}},"bangumi_subject_requests":{{Volatile.Read(ref _bangumiSubjectRequests)}},"bangumi_episode_requests":{{Volatile.Read(ref _bangumiEpisodeRequests)}}}
        """;
}
