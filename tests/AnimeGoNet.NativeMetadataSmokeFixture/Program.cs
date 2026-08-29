using System.Globalization;
using System.Text.Json;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.NativeMetadataSmokeFixture;

public static class Program
{
    private const string FileName = "Native.AI.S02E07.mkv";
    private const string InfoHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Expected 'seed' or 'serve'.");
            return 2;
        }

        try
        {
            return args[0] switch
            {
                "seed" => await SeedAsync(args[1..]).ConfigureAwait(false),
                "serve" => await ServeAsync(args[1..]).ConfigureAwait(false),
                _ => throw new ArgumentException("Unknown fixture command."),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> SeedAsync(string[] args)
    {
        var values = ParseArguments(args);
        var databaseFile = RequiredPath(values, "database", requireExistingFile: true);
        var downloadPath = RequiredPath(values, "download-path", requireExistingFile: false);
        var savePath = RequiredPath(values, "save-path", requireExistingFile: false);
        Directory.CreateDirectory(downloadPath);
        Directory.CreateDirectory(savePath);

        var database = new AnimeGoSqliteDatabase(databaseFile);
        await database.InitializeAsync().ConfigureAwait(false);
        var profile = await new SourceProfileStore(database)
            .GetEnabledAsync("mikan")
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Default Mikan source profile is missing.");

        var now = DateTimeOffset.UtcNow;
        var item = new NormalizedIngestItem(
            "mikan",
            new Uri("https://mikanani.me/native-metadata-smoke.torrent"),
            new string('a', 64),
            "Native AI metadata smoke",
            "native-ai-smoke-item",
            "native-ai-smoke-work",
            null,
            null,
            null,
            null);
        var metadata = new TorrentMetadata(
            "Native AI metadata smoke",
            InfoHash,
            734_003_200,
            [new TorrentFile(FileName, 734_003_200, IsPadding: false)]);
        var tasks = new IngestTaskStore(database);
        var staged = await tasks.AddStagedAsync(
            item,
            profile,
            metadata,
            "native-ai-metadata-smoke.torrent",
            now.AddHours(1)).ConfigureAwait(false);
        var claim = await tasks.TryClaimNextStagedAsync(
            now,
            TimeSpan.FromMinutes(1)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Seeded task was not claimable.");
        if (!string.Equals(claim.TaskId, staged.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Fixture claimed an unexpected staged task.");
        }

        await tasks.CompleteDispatchAsync(
            claim,
            new DownloadTaskSnapshot(
                InfoHash,
                metadata.Name,
                DownloadTaskState.Waiting,
                0,
                0,
                metadata.TotalSize,
                0,
                null),
            Path.Combine(downloadPath, "bt"),
            savePath,
            now).ConfigureAwait(false);
        await new DownloadJobStore(database).ApplyInstanceSnapshotAsync(
            profile.DownloaderId,
            [new DownloadTaskSnapshot(
                InfoHash,
                metadata.Name,
                DownloadTaskState.Complete,
                1,
                metadata.TotalSize,
                metadata.TotalSize,
                0,
                0)],
            now).ConfigureAwait(false);

        Console.WriteLine($$"""{"task_id":"{{staged.Id}}","file_name":"{{FileName}}"}""");
        return 0;
    }

    private static async Task<int> ServeAsync(string[] args)
    {
        var values = ParseArguments(args);
        var url = Required(values, "urls");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var listener)
            || listener.Scheme != Uri.UriSchemeHttp
            || !listener.IsLoopback
            || listener.Port <= 0)
        {
            throw new ArgumentException("Fixture URL must be an HTTP loopback URL with an explicit port.");
        }

        var state = new FixtureState();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(listener.AbsoluteUri);
        var app = builder.Build();

        app.MapGet("/ready", () => Results.Text("ready", "text/plain"));
        app.MapGet("/__state", () => Results.Text(state.ToJson(), "application/json"));
        app.MapPost(
            "/mcp",
            (Func<HttpContext, Task<IResult>>)(context => HandleMcpAsync(context, state)));
        app.MapPost(
            "/ai/v1/chat/completions",
            (Func<HttpContext, Task<IResult>>)(context => HandleAiAsync(context, state)));
        app.MapGet("/tmdb/3/discover/tv", (HttpContext context) =>
        {
            state.RecordTmdbDiscover(HasTmdbCredential(context));
            return Json("""{"total_results":0,"results":[]}""");
        });
        app.MapGet("/tmdb/3/tv/72517", (HttpContext context) =>
        {
            state.RecordTmdbSeries(HasTmdbCredential(context));
            return Json("""
                {"id":72517,"name":"Native AI Series","original_name":"Native AI Original","first_air_date":"2022-07-06","poster_path":null,"seasons":[{"id":200,"name":"Season 2","season_number":2,"air_date":"2022-07-06","episode_count":12,"poster_path":null}]}
                """);
        });
        app.MapGet("/tmdb/3/tv/72517/season/2", (HttpContext context) =>
        {
            state.RecordTmdbSeason(HasTmdbCredential(context));
            return Json("""
                {"id":200,"name":"Season 2","season_number":2,"air_date":"2022-07-06","episode_count":12,"poster_path":null,"episodes":[{"id":207,"name":"Native Episode 7","air_date":"2022-08-17","season_number":2,"episode_number":7}]}
                """);
        });
        app.MapGet("/tmdb/3/tv/72517/season/2/episode/7", (HttpContext context) =>
        {
            state.RecordTmdbEpisode(HasTmdbCredential(context));
            return Json("""
                {"id":207,"name":"Native Episode 7","air_date":"2022-08-17","season_number":2,"episode_number":7}
                """);
        });

        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static async Task<IResult> HandleAiAsync(HttpContext context, FixtureState state)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        var authorized = string.Equals(
            context.Request.Headers.Authorization,
            "Bearer native-smoke-ai-key",
            StringComparison.Ordinal);
        var call = state.RecordAi(authorized, ContainsAbsolutePath(body));
        if (call == 1)
        {
            return Json("""
                {"choices":[{"message":{"content":null,"tool_calls":[{"id":"native-smoke-tool-call","type":"function","function":{"name":"tmdb__lookup_series","arguments":"{\"query\":\"Native AI metadata smoke\"}"}}]}}]}
                """);
        }

        var content = """
            {"matched":true,"tmdb_id":72517,"files":[{"file_id":"f0001","matched":true,"season":2,"episode":7,"reason":null}],"reason":null}
            """;
        return Json(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } },
        }));
    }

    private static async Task<IResult> HandleMcpAsync(HttpContext context, FixtureState state)
    {
        using var document = await JsonDocument.ParseAsync(
            context.Request.Body,
            cancellationToken: context.RequestAborted).ConfigureAwait(false);
        var method = document.RootElement.GetProperty("method").GetString();
        var id = document.RootElement.TryGetProperty("id", out var idElement)
            ? idElement.GetRawText()
            : "null";
        context.Response.Headers["Mcp-Session-Id"] = "native-smoke-session";
        return method switch
        {
            "initialize" => Initialize(),
            "notifications/initialized" => Notification(),
            "tools/list" => Tools(),
            "tools/call" => ToolCall(),
            _ => Json("{\"jsonrpc\":\"2.0\",\"id\":" + id
                + ",\"error\":{\"code\":-32601,\"message\":\"method not found\"}}"),
        };

        IResult Initialize()
        {
            state.RecordMcpInitialize();
            return Json("{\"jsonrpc\":\"2.0\",\"id\":" + id
                + ",\"result\":{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"native-smoke\",\"version\":\"1\"}}}");
        }

        IResult Notification()
        {
            state.RecordMcpNotification();
            return Results.Text(string.Empty, "application/json", statusCode: StatusCodes.Status202Accepted);
        }

        IResult Tools()
        {
            state.RecordMcpToolsList();
            return Json("{\"jsonrpc\":\"2.0\",\"id\":" + id
                + ",\"result\":{\"tools\":[{\"name\":\"lookup_series\",\"description\":\"Return a deterministic smoke candidate.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}},\"required\":[\"query\"],\"additionalProperties\":false}}]}}");
        }

        IResult ToolCall()
        {
            state.RecordMcpToolCall();
            return Json("{\"jsonrpc\":\"2.0\",\"id\":" + id
                + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"tmdb_tv_id\\\":72517}\"}],\"isError\":false}}");
        }
    }

    private static bool HasTmdbCredential(HttpContext context) =>
        string.Equals(
            context.Request.Query["api_key"],
            "native-smoke-tmdb-key",
            StringComparison.Ordinal);

    private static bool ContainsAbsolutePath(string value) =>
        value.Contains(":\\\\", StringComparison.Ordinal)
        || value.Contains("/tmp/", StringComparison.Ordinal)
        || value.Contains("/home/", StringComparison.Ordinal)
        || value.Contains("/Users/", StringComparison.Ordinal);

    private static IResult Json(string value) => Results.Text(value, "application/json");

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        if (args.Length == 0 || args.Length % 2 != 0)
        {
            throw new ArgumentException("Fixture options must be --name value pairs.");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)
                || args[index].Length <= 2
                || !result.TryAdd(args[index][2..], args[index + 1]))
            {
                throw new ArgumentException("Fixture option is invalid or duplicated.");
            }
        }

        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"--{name} is required.");

    private static string RequiredPath(
        IReadOnlyDictionary<string, string> values,
        string name,
        bool requireExistingFile)
    {
        var path = Required(values, name);
        if (!Path.IsPathFullyQualified(path)
            || (requireExistingFile && !File.Exists(path)))
        {
            throw new ArgumentException($"--{name} must be an absolute existing file path.");
        }

        return Path.GetFullPath(path);
    }

    private sealed class FixtureState
    {
        private int _aiCalls;
        private int _aiAuthorizationFailures;
        private int _unsafeAbsolutePaths;
        private int _mcpInitializeCalls;
        private int _mcpNotificationCalls;
        private int _mcpToolsListCalls;
        private int _mcpToolCalls;
        private int _tmdbDiscoverCalls;
        private int _tmdbSeriesCalls;
        private int _tmdbSeasonCalls;
        private int _tmdbEpisodeCalls;
        private int _tmdbCredentialFailures;

        public int RecordAi(bool authorized, bool unsafeAbsolutePath)
        {
            if (!authorized) Interlocked.Increment(ref _aiAuthorizationFailures);
            if (unsafeAbsolutePath) Interlocked.Increment(ref _unsafeAbsolutePaths);
            return Interlocked.Increment(ref _aiCalls);
        }

        public void RecordMcpInitialize() => Interlocked.Increment(ref _mcpInitializeCalls);

        public void RecordMcpNotification() => Interlocked.Increment(ref _mcpNotificationCalls);

        public void RecordMcpToolsList() => Interlocked.Increment(ref _mcpToolsListCalls);

        public void RecordMcpToolCall() => Interlocked.Increment(ref _mcpToolCalls);

        public void RecordTmdbDiscover(bool credentialValid)
        {
            RecordTmdbCredential(credentialValid);
            Interlocked.Increment(ref _tmdbDiscoverCalls);
        }

        public void RecordTmdbSeries(bool credentialValid)
        {
            RecordTmdbCredential(credentialValid);
            Interlocked.Increment(ref _tmdbSeriesCalls);
        }

        public void RecordTmdbSeason(bool credentialValid)
        {
            RecordTmdbCredential(credentialValid);
            Interlocked.Increment(ref _tmdbSeasonCalls);
        }

        public void RecordTmdbEpisode(bool credentialValid)
        {
            RecordTmdbCredential(credentialValid);
            Interlocked.Increment(ref _tmdbEpisodeCalls);
        }

        public string ToJson() => string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"ai_calls":{{Volatile.Read(ref _aiCalls)}},"ai_authorization_failures":{{Volatile.Read(ref _aiAuthorizationFailures)}},"unsafe_absolute_paths":{{Volatile.Read(ref _unsafeAbsolutePaths)}},"mcp_initialize_calls":{{Volatile.Read(ref _mcpInitializeCalls)}},"mcp_notification_calls":{{Volatile.Read(ref _mcpNotificationCalls)}},"mcp_tools_list_calls":{{Volatile.Read(ref _mcpToolsListCalls)}},"mcp_tool_calls":{{Volatile.Read(ref _mcpToolCalls)}},"tmdb_discover_calls":{{Volatile.Read(ref _tmdbDiscoverCalls)}},"tmdb_series_calls":{{Volatile.Read(ref _tmdbSeriesCalls)}},"tmdb_season_calls":{{Volatile.Read(ref _tmdbSeasonCalls)}},"tmdb_episode_calls":{{Volatile.Read(ref _tmdbEpisodeCalls)}},"tmdb_credential_failures":{{Volatile.Read(ref _tmdbCredentialFailures)}}}""");

        private void RecordTmdbCredential(bool valid)
        {
            if (!valid) Interlocked.Increment(ref _tmdbCredentialFailures);
        }
    }
}
