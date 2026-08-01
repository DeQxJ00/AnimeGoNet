using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Plugins;

namespace AnimeGoNet.App.Tests.Plugins;

public sealed class ExternalPluginAdaptersTests
{
    [Fact]
    public async Task SixTypedAdaptersUseStableOperationsCamelCasePayloadAndPersistedConfig()
    {
        await using var fixture = await AdapterFixture.CreateAsync(
            "source", "feed", "parser", "filter", "rename", "schedule");
        fixture.Factory.Handler = static (operation, _, _) => Task.FromResult(operation switch
        {
            "source.normalize" => Json($$"""
                {
                  "item": {
                    "source": "com.example.source",
                    "torrentUrl": "https://tracker.example/item.torrent?passkey=dummy",
                    "torrentUrlFingerprint": "{{Fingerprint("https://tracker.example/item.torrent?passkey=dummy")}}",
                    "title": "Source title",
                    "sourceItemId": "item-1",
                    "sourceWorkId": "work-1",
                    "mikanId": null,
                    "bangumiId": 42,
                    "aniDbId": null,
                    "imdbId": null,
                    "publishedAtRaw": null,
                    "publishedAt": null
                  },
                  "errors": []
                }
                """),
            "feed.fetch" => Json("""
                {
                  "items": [{
                    "title": "Feed title",
                    "torrentUrl": "https://tracker.example/feed.torrent",
                    "sourceUrl": "https://source.example/item/1",
                    "sourceItemId": "1",
                    "sourceWorkId": "2",
                    "contentType": "application/x-bittorrent",
                    "length": 123,
                    "publishedAtRaw": null,
                    "publishedAt": null
                  }],
                  "errors": [],
                  "metadata": { "cursor": "next" }
                }
                """),
            "parser.parse" => Json("""
                {
                  "matched": true,
                  "animeTitle": "Parsed title",
                  "season": 2,
                  "episode": 2.5,
                  "episodeKind": "fractional",
                  "episodeText": "2.5",
                  "releaseGroup": "group",
                  "resolution": "1080p",
                  "errors": []
                }
                """),
            "filter.all" => Json("""
                {
                  "decisions": [{
                    "index": 7,
                    "outcome": "Accepted",
                    "accepted": true,
                    "reason": "fixture",
                    "priority": 10,
                    "metadata": { "rule": "fixture" }
                  }],
                  "errors": [],
                  "metadata": { "revision": "1" }
                }
                """),
            "rename.plan" => Json("""
                {
                  "matched": true,
                  "relativeTargetPath": "Series/Season 02/Series S02E03.mkv",
                  "errors": []
                }
                """),
            "schedule.execute" => Json("""
                {
                  "succeeded": true,
                  "message": "done",
                  "errors": [],
                  "nextDelay": "00:00:05"
                }
                """),
            _ => throw new InvalidOperationException(operation),
        });
        var catalog = fixture.CreateCatalog();

        var source = await catalog.Require<IInputSourceAdapter>("com.example.source")
            .NormalizeAsync(new SourceIngestContext(
                "com.example.source",
                "https://tracker.example/original.torrent",
                "Original title",
                null,
                null,
                null,
                null,
                null,
                null,
                42,
                null,
                null,
                null,
                null,
                true), CancellationToken.None);
        var feed = await catalog.Require<IFeedPlugin>("com.example.feed")
            .FetchAsync(new FeedContext(
                "profile",
                "https://source.example/feed.xml",
                EmptyArguments), CancellationToken.None);
        var parsed = await catalog.Require<ITitleParserPlugin>("com.example.parser")
            .ParseAsync(new TitleParseContext(
                "Title 2.5",
                "file.mkv",
                "profile",
                EmptyArguments), CancellationToken.None);
        var filtered = await catalog.Require<IFeedFilterPlugin>("com.example.filter")
            .FilterAsync(new FilterContext(
                "profile",
                [new FilterItem(
                    7,
                    "Title",
                    "https://tracker.example/item.torrent",
                    null,
                    null,
                    null,
                    null,
                    123,
                    null)],
                EmptyArguments), CancellationToken.None);
        var renamed = await catalog.Require<IRenamePlugin>("com.example.rename")
            .RenameAsync(new RenameContext(
                "download/file.mkv",
                "Series",
                2,
                "episode",
                3,
                null,
                null,
                EmptyArguments), CancellationToken.None);
        var scheduled = await catalog.Require<IScheduledPlugin>("com.example.schedule")
            .ExecuteAsync(new ScheduledContext(
                "fixture",
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                EmptyArguments), CancellationToken.None);

        Assert.True(source.Succeeded);
        Assert.Equal("Source title", source.Item!.Title);
        Assert.Single(feed.Items);
        Assert.Equal("next", feed.Metadata["cursor"]);
        Assert.True(parsed.Matched);
        Assert.Equal(2.5m, parsed.Episode);
        Assert.True(Assert.Single(filtered.Decisions).Accepted);
        Assert.True(renamed.Matched);
        Assert.Equal("Series/Season 02/Series S02E03.mkv", renamed.RelativeTargetPath);
        Assert.True(scheduled.Succeeded);
        Assert.Equal(TimeSpan.FromSeconds(5), scheduled.NextDelay);

        Assert.Equal(
            [
                "feed.fetch",
                "filter.all",
                "parser.parse",
                "rename.plan",
                "schedule.execute",
                "source.normalize",
            ],
            fixture.Factory.Calls.Select(call => call.Operation).Order(StringComparer.Ordinal));
        Assert.All(fixture.Factory.Calls, call =>
        {
            Assert.Equal("configured-default", call.Payload.GetProperty("adapterDefault").GetString());
            Assert.Equal("configured-secret", call.Config.GetProperty("credential").GetString());
            Assert.DoesNotContain(
                call.Payload.EnumerateObject(),
                property => property.Name.Length > 0 && char.IsUpper(property.Name[0]));
        });
        Assert.True(fixture.Factory.Calls
            .Single(call => call.Operation == "source.normalize")
            .Payload.GetProperty("requireModernMetadata").GetBoolean());
        Assert.All(catalog.All, plugin => Assert.False(plugin.Descriptor.IsBuiltIn));
    }

    [Fact]
    public async Task DisabledAdapterReturnsStableDomainErrorWithoutStartingProcess()
    {
        await using var fixture = await AdapterFixture.CreateAsync(["source"], enabled: false);
        var adapter = fixture.CreateCatalog().Require<IInputSourceAdapter>("com.example.source");

        var result = await adapter.NormalizeAsync(new SourceIngestContext(
            "com.example.source",
            "https://tracker.example/item.torrent",
            "Title",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false), CancellationToken.None);

        Assert.Null(result.Item);
        Assert.Equal("plugin_disabled", Assert.Single(result.Errors).Code);
        Assert.Equal(0, fixture.Factory.CreateCount);
    }

    [Fact]
    public async Task RemoteBusinessErrorIsRedactedAndDoesNotPenalizeHealthySession()
    {
        await using var fixture = await AdapterFixture.CreateAsync("feed");
        fixture.Factory.Handler = static (_, _, _) =>
            throw new ExternalPluginRemoteException(
                "remote_rejected",
                "password=fixture-secret https://service.example/private?token=value");
        var adapter = fixture.CreateCatalog().Require<IFeedPlugin>("com.example.feed");

        var result = await adapter.FetchAsync(new FeedContext(
            "profile",
            "https://source.example/feed.xml",
            EmptyArguments), CancellationToken.None);

        var error = Assert.Single(result.Errors);
        Assert.Equal("remote_rejected", error.Code);
        Assert.Contains("password=<redacted>", error.Message, StringComparison.Ordinal);
        Assert.Contains("https://service.example/<redacted>", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-secret", error.Message, StringComparison.Ordinal);
        var runtime = fixture.Manager.GetSnapshot("com.example.feed")!;
        Assert.Equal(ExternalPluginRuntimeState.Ready, runtime.State);
        Assert.Equal(0, runtime.ConsecutiveFailures);
    }

    [Theory]
    [InlineData("unknown-field")]
    [InlineData("duplicate-field")]
    [InlineData("missing-index")]
    public async Task InvalidFilterResultFaultsSessionAndEntersBackoff(string fault)
    {
        await using var fixture = await AdapterFixture.CreateAsync("filter");
        fixture.Factory.Handler = (operation, _, _) => Task.FromResult(fault switch
        {
            "unknown-field" => Json("""
                {"decisions":[],"errors":[],"metadata":{},"unexpected":true}
                """),
            "duplicate-field" => Json("""
                {"decisions":[],"decisions":[],"errors":[],"metadata":{}}
                """),
            "missing-index" => Json("""
                {"decisions":[],"errors":[],"metadata":{}}
                """),
            _ => throw new InvalidOperationException(fault),
        });
        var adapter = fixture.CreateCatalog().Require<IFeedFilterPlugin>("com.example.filter");

        var result = await adapter.FilterAsync(new FilterContext(
            "profile",
            [new FilterItem(
                7,
                "Title",
                "https://tracker.example/item.torrent",
                null,
                null,
                null,
                null,
                1,
                null)],
            EmptyArguments), CancellationToken.None);

        Assert.Equal("filter_result_invalid", Assert.Single(result.Errors).Code);
        var runtime = fixture.Manager.GetSnapshot("com.example.filter")!;
        Assert.Equal(ExternalPluginRuntimeState.Backoff, runtime.State);
        Assert.Equal(1, runtime.ConsecutiveFailures);
        Assert.True(fixture.Factory.LastSessionDisposed);
    }

    [Theory]
    [InlineData("../escape.mkv")]
    [InlineData("folder/../../escape.mkv")]
    [InlineData("/absolute/escape.mkv")]
    public async Task UnsafeRenameResultIsRejectedBeforePathConsumer(string path)
    {
        await using var fixture = await AdapterFixture.CreateAsync("rename");
        fixture.Factory.Handler = (_, _, _) => Task.FromResult(Json($$"""
            {"matched":true,"relativeTargetPath":{{JsonSerializer.Serialize(path)}},"errors":[]}
            """));
        var adapter = fixture.CreateCatalog().Require<IRenamePlugin>("com.example.rename");

        var result = await adapter.RenameAsync(new RenameContext(
            "source.mkv",
            "Series",
            1,
            "episode",
            1,
            null,
            null,
            EmptyArguments), CancellationToken.None);

        Assert.False(result.Matched);
        Assert.Equal("rename_result_invalid", Assert.Single(result.Errors).Code);
        Assert.Equal(
            ExternalPluginRuntimeState.Backoff,
            fixture.Manager.GetSnapshot("com.example.rename")!.State);
    }

    [Fact]
    public async Task SourceFingerprintMustMatchTheExactReturnedTorrentUrl()
    {
        await using var fixture = await AdapterFixture.CreateAsync("source");
        fixture.Factory.Handler = (_, _, _) => Task.FromResult(Json($$"""
            {
              "item": {
                "source": "com.example.source",
                "torrentUrl": "https://tracker.example/item.torrent?passkey=one",
                "torrentUrlFingerprint": "{{Fingerprint("https://tracker.example/item.torrent?passkey=two")}}",
                "title": "Title",
                "sourceItemId": null,
                "sourceWorkId": null,
                "mikanId": null,
                "bangumiId": null,
                "aniDbId": null,
                "imdbId": null,
                "publishedAtRaw": null,
                "publishedAt": null
              },
              "errors": []
            }
            """));
        var adapter = fixture.CreateCatalog().Require<IInputSourceAdapter>("com.example.source");

        var result = await adapter.NormalizeAsync(new SourceIngestContext(
            "com.example.source",
            "https://tracker.example/item.torrent?passkey=one",
            "Title",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false), CancellationToken.None);

        Assert.Equal("source_result_invalid", Assert.Single(result.Errors).Code);
        Assert.Equal(
            ExternalPluginRuntimeState.Backoff,
            fixture.Manager.GetSnapshot("com.example.source")!.State);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyArguments =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static string Fingerprint(string url) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)));

    private sealed class AdapterFixture : IAsyncDisposable
    {
        private readonly string _root;

        private AdapterFixture(
            string root,
            ExternalPluginDiscoveryResult discovery,
            ExternalPluginConfigurationStore configurations,
            FakeSessionFactory factory,
            ExternalPluginHostManager manager)
        {
            _root = root;
            Discovery = discovery;
            Configurations = configurations;
            Factory = factory;
            Manager = manager;
        }

        public ExternalPluginDiscoveryResult Discovery { get; }

        public ExternalPluginConfigurationStore Configurations { get; }

        public FakeSessionFactory Factory { get; }

        public ExternalPluginHostManager Manager { get; }

        public static Task<AdapterFixture> CreateAsync(params string[] types) =>
            CreateAsync(types, enabled: true);

        public static async Task<AdapterFixture> CreateAsync(
            IReadOnlyList<string> types,
            bool enabled)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "animegonet-external-adapter-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var packages = types.Select(type => Package(root, type)).ToArray();
            var discovery = new ExternalPluginDiscoveryResult(packages, []);
            var configurations = new ExternalPluginConfigurationStore(
                Path.Combine(root, "config", "external-plugins.private.json"));
            foreach (var package in packages)
            {
                await configurations.UpsertAsync(
                    package.Manifest.Id,
                    enabled,
                    Json("{\"adapterDefault\":\"configured-default\"}"),
                    Json("{\"credential\":\"configured-secret\"}"),
                    configurations.Current.Revision,
                    new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
            }
            var factory = new FakeSessionFactory();
            var manager = new ExternalPluginHostManager(
                discovery,
                Path.Combine(root, "plugin-data"),
                "1.0.0-test",
                new ExternalPluginHostOptions
                {
                    InitialBackoff = TimeSpan.FromSeconds(1),
                    MaximumBackoff = TimeSpan.FromSeconds(4),
                },
                factory,
                TimeProvider.System,
                configurations);
            return new AdapterFixture(
                root,
                discovery,
                configurations,
                factory,
                manager);
        }

        public PluginCatalog CreateCatalog() =>
            new(ExternalPluginAdapterFactory.Create(Discovery, Manager));

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            Configurations.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static ExternalPluginPackage Package(string root, string type)
        {
            var id = $"com.example.{type}";
            var directory = Path.Combine(root, "plugins", type);
            var entry = OperatingSystem.IsWindows() ? "plugin.exe" : "plugin";
            return new ExternalPluginPackage(
                new ExternalPluginManifest(
                    id,
                    $"External {type}",
                    "1.0.0",
                    1,
                    type,
                    CurrentRid(),
                    entry,
                    "config.schema.json",
                    []),
                directory,
                Path.Combine(directory, "plugin.json"),
                Path.Combine(directory, entry),
                Path.Combine(directory, "config.schema.json"));
        }

        private static string CurrentRid()
        {
            var architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException(),
            };
            if (OperatingSystem.IsWindows()) return $"win-{architecture}";
            if (OperatingSystem.IsLinux()) return $"linux-{architecture}";
            if (OperatingSystem.IsMacOS() && architecture == "arm64") return "osx-arm64";
            throw new PlatformNotSupportedException();
        }
    }

    private sealed record SessionCall(
        string Operation,
        JsonElement Payload,
        JsonElement Config);

    private sealed class FakeSessionFactory : IExternalPluginSessionFactory
    {
        public Func<string, JsonElement, JsonElement, Task<JsonElement>> Handler { get; set; } =
            static (_, _, _) => throw new InvalidOperationException("Fixture handler was not set.");

        public List<SessionCall> Calls { get; } = [];

        public int CreateCount { get; private set; }

        private FakeSession? LastSession { get; set; }

        public bool LastSessionDisposed => LastSession?.Disposed ?? false;

        public IExternalPluginSession Create(
            ExternalPluginPackage package,
            string pluginDataPath,
            ExternalPluginSessionOptions options)
        {
            CreateCount++;
            LastSession = new FakeSession(this);
            return LastSession;
        }

        private sealed class FakeSession(FakeSessionFactory owner) : IExternalPluginSession
        {
            public ExternalPluginSessionState State { get; private set; } =
                ExternalPluginSessionState.Created;

            public bool Disposed { get; private set; }

            public Task StartAsync(
                string hostVersion,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                State = ExternalPluginSessionState.Ready;
                return Task.CompletedTask;
            }

            public async Task<JsonElement> ExecuteAsync(
                string operation,
                JsonElement payload,
                JsonElement config,
                TimeSpan? timeout = null,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.Calls.Add(new SessionCall(operation, payload.Clone(), config.Clone()));
                return (await owner.Handler(operation, payload, config)).Clone();
            }

            public Task<bool> HealthAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(true);

            public Task ShutdownAsync(
                string reason = "host_shutdown",
                CancellationToken cancellationToken = default)
            {
                State = ExternalPluginSessionState.Stopped;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                State = ExternalPluginSessionState.Stopped;
                return ValueTask.CompletedTask;
            }
        }
    }
}
