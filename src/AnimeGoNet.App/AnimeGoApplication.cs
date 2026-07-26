using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.App.Api;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.App.Deletion;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.App.Library;
using AnimeGoNet.App.Serialization;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.App.Ingest;
using AnimeGoNet.Data.Feeds;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Deletion;
using AnimeGoNet.Data.Library;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Mikan;
using AnimeGoNet.Data.Rules;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;
using Microsoft.AspNetCore.Http.Json;

namespace AnimeGoNet.App;

public static class AnimeGoApplication
{
    public static async Task<WebApplication> BuildAsync(
        string[] args,
        AnimeGoOptions? options = null,
        string? accessKey = null,
        bool? runningInContainer = null,
        ITorrentStagingService? torrentStagingService = null,
        IDownloadClientRegistry? downloadClientRegistry = null,
        ITmdbClient? tmdbClient = null,
        IBangumiSubjectClient? bangumiSubjectClient = null,
        ITorrentDnsResolver? rssDnsResolver = null,
        ITorrentHttpTransport? rssHttpTransport = null,
        bool? startBackgroundWorkers = null,
        CancellationToken cancellationToken = default)
    {
        var webRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = webRootPath,
        });

        runningInContainer ??= string.Equals(
            builder.Configuration["DOTNET_RUNNING_IN_CONTAINER"],
            "true",
            StringComparison.OrdinalIgnoreCase);
        startBackgroundWorkers ??= !bool.TryParse(
            builder.Configuration["background_workers_enabled"],
            out var configuredWorkers) || configuredWorkers;
        options ??= LoadOptions(builder.Configuration, runningInContainer.Value);
        accessKey ??= builder.Configuration["access_key"];
        if (runningInContainer.Value && string.IsNullOrWhiteSpace(accessKey))
        {
            throw new InvalidOperationException("Docker mode requires a non-empty access_key.");
        }
        var errors = AnimeGoOptionsValidator.Validate(options);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid AnimeGoNet configuration: " + string.Join("; ", errors));
        }

        var layout = DirectoryLayout.From(options.Paths);
        layout.CreateDataDirectories();
        var database = new AnimeGoSqliteDatabase(layout.DatabaseFile);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var sourceProfiles = new SourceProfileStore(database);
        await sourceProfiles.EnsureSeedsAsync(options.InitialSourceProfiles, cancellationToken).ConfigureAwait(false);
        var rssRules = new MikanRssRuleStore(database);
        await rssRules.EnsureDefaultAsync(
            "mikan", MikanRssRuleDefaults.Create(), DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        var legacyMikanFilters = new LegacyMikanFilterStore(database);
        await legacyMikanFilters.EnsureDefaultAsync("mikan", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        var ingestTasks = new IngestTaskStore(database);
        var downloadJobs = new DownloadJobStore(database);
        torrentStagingService ??= new TorrentStagingService(
            layout,
            options.TorrentFetch,
            new SystemTorrentDnsResolver(),
            new PinnedTorrentHttpTransport());
        var expiredStaging = await ingestTasks
            .ExpireStagedAsync(DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        foreach (var expired in expiredStaging)
        {
            await torrentStagingService
                .DeleteAsync(expired.StagingFileName, cancellationToken)
                .ConfigureAwait(false);
        }

        await torrentStagingService.CleanupExpiredAsync(cancellationToken).ConfigureAwait(false);
        downloadClientRegistry ??= new QbittorrentClientRegistry(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(layout);
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton(sourceProfiles);
        builder.Services.AddSingleton(rssRules);
        builder.Services.AddSingleton(legacyMikanFilters);
        builder.Services.AddSingleton(ingestTasks);
        builder.Services.AddSingleton<UnifiedIngestProcessor>();
        builder.Services.AddSingleton<MikanRssBatchStore>();
        builder.Services.AddSingleton<MikanRssIngestProcessor>();
        rssDnsResolver ??= new SystemTorrentDnsResolver();
        rssHttpTransport ??= new PinnedTorrentHttpTransport();
        builder.Services.AddSingleton<IRssFeedHttpClient>(new ProfileBoundRssFeedHttpClient(
            sourceProfiles, rssDnsResolver, rssHttpTransport));
        builder.Services.AddSingleton<RssFeedReader>();
        builder.Services.AddSingleton<MikanLegacyFilterProcessor>();
        builder.Services.AddSingleton(downloadJobs);
        builder.Services.AddSingleton<DownloadPreparationStore>();
        builder.Services.AddSingleton<MediaOrganizationStore>();
        builder.Services.AddSingleton<DeletePlanStore>();
        builder.Services.AddSingleton<DeleteExecutionStore>();
        builder.Services.AddSingleton<MikanWorkMetadataRuleStore>();
        builder.Services.AddSingleton<MikanTrustedOffsetStore>();
        builder.Services.AddSingleton<MetadataResolutionStore>();
        builder.Services.AddSingleton<CompletionRecordStore>();
        builder.Services.AddSingleton(downloadClientRegistry);
        builder.Services.AddSingleton<DownloadClientOperationCoordinator>();
        builder.Services.AddSingleton(torrentStagingService);
        builder.Services.AddSingleton<StagedTorrentDispatcher>();
        builder.Services.AddSingleton<DownloadSnapshotSynchronizer>();
        builder.Services.AddSingleton<DownloadPreparationProcessor>();
        builder.Services.AddSingleton<SafeFileMover>();
        builder.Services.AddSingleton<TvShowNfoWriter>();
        builder.Services.AddSingleton<MediaOrganizationProcessor>();
        builder.Services.AddSingleton<SafeFileDeleter>();
        builder.Services.AddSingleton<DeleteExecutionProcessor>();
        tmdbClient ??= new TmdbClient(
            new HttpClient(),
            options.Metadata.Tmdb,
            ownsHttpClient: true);
        builder.Services.AddSingleton(tmdbClient);
        builder.Services.AddSingleton<TmdbAuthority>();
        builder.Services.AddSingleton<TmdbSeriesResolver>();
        bangumiSubjectClient ??= new BangumiSubjectClient(new HttpClient(), ownsHttpClient: true);
        builder.Services.AddSingleton(bangumiSubjectClient);
        builder.Services.AddSingleton<BangumiSeasonBacktraceResolver>();
        builder.Services.AddSingleton<ManualMetadataResolutionProcessor>();
        builder.Services.AddSingleton<AutomaticMetadataResolutionProcessor>();
        builder.Services.AddSingleton<EpisodeMetadataResolutionProcessor>();
        if (startBackgroundWorkers.Value)
        {
            builder.Services.AddHostedService<StagedTorrentDispatchWorker>();
            builder.Services.AddHostedService<DownloadSnapshotWorker>();
            builder.Services.AddHostedService<ManualMetadataResolutionWorker>();
            builder.Services.AddHostedService<AutomaticMetadataResolutionWorker>();
            builder.Services.AddHostedService<EpisodeMetadataResolutionWorker>();
            builder.Services.AddHostedService<DownloadPreparationWorker>();
            builder.Services.AddHostedService<MediaOrganizationWorker>();
            builder.Services.AddHostedService<DeleteExecutionWorker>();
        }
        builder.Services.Configure<JsonOptions>(json =>
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api")
                && !string.IsNullOrWhiteSpace(accessKey)
                && !HasValidAccessKey(context.Request, accessKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        ApiEndpoints.Map(app);
        app.MapFallbackToFile("index.html");
        return app;
    }

    private static AnimeGoOptions LoadOptions(ConfigurationManager configuration, bool inContainer)
    {
        var defaults = inContainer
            ? AnimeGoDefaults.CreateDocker()
            : AnimeGoDefaults.CreateNative(AppContext.BaseDirectory);

        var dataPath = configuration["data_path"] ?? defaults.Paths.DataPath;
        var downloadPath = configuration["download_path"] ?? defaults.Paths.DownloadPath;
        var savePath = configuration["save_path"] ?? defaults.Paths.SavePath;
        var paths = new PathOptions
        {
            DataPath = dataPath,
            DownloadPath = downloadPath,
            SavePath = savePath,
        };

        return defaults with
        {
            Paths = paths,
            Metadata = defaults.Metadata with
            {
                Tmdb = defaults.Metadata.Tmdb with
                {
                    ApiKey = configuration["tmdb_api_key"],
                    ReadAccessToken = configuration["tmdb_read_access_token"],
                },
            },
            Downloaders = defaults.Downloaders.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with { DownloadPath = PathBoundary.Combine(downloadPath, pair.Key) },
                StringComparer.OrdinalIgnoreCase),
        };
    }

    private static bool HasValidAccessKey(HttpRequest request, string configuredKey)
    {
        if (request.Headers.TryGetValue("X-AnimeGo-Access-Key", out var directKey)
            && FixedTimeEquals(directKey.ToString(), configuredKey))
        {
            return true;
        }

        var suppliedHash = request.Query["access_key"].ToString();
        if (string.IsNullOrWhiteSpace(suppliedHash))
        {
            suppliedHash = request.Headers["Access-Key"].ToString();
        }

        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey)));
        return FixedTimeEquals(suppliedHash.ToLowerInvariant(), expectedHash);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
