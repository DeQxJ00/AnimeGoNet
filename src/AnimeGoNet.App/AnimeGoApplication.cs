using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Api;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.App.Deletion;
using AnimeGoNet.App.DataUpdate;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.App.Library;
using AnimeGoNet.App.Logging;
using AnimeGoNet.App.Plugins;
using AnimeGoNet.App.Scheduling;
using AnimeGoNet.App.Serialization;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Plugins;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Cache;
using AnimeGoNet.App.Ingest;
using AnimeGoNet.Data.Feeds;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Deletion;
using AnimeGoNet.Data.DataUpdate;
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
        IBangumiEpisodeClient? bangumiEpisodeClient = null,
        IAiMetadataMatcher? aiMetadataMatcher = null,
        ITorrentDnsResolver? rssDnsResolver = null,
        ITorrentHttpTransport? rssHttpTransport = null,
        ITmdbPosterTransport? tmdbPosterTransport = null,
        HttpClient? dataUpdateHttpClient = null,
        bool? startBackgroundWorkers = null,
        IReadOnlyCollection<string>? deploymentEnvironmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var webRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = webRootPath,
        });
        builder.Services.Configure<HostOptions>(host =>
            host.ShutdownTimeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton<WebSocketLogHub>();
        builder.Services.AddSingleton<ILoggerProvider>(
            static services =>
                services.GetRequiredService<WebSocketLogHub>());

        runningInContainer ??= string.Equals(
            builder.Configuration["DOTNET_RUNNING_IN_CONTAINER"],
            "true",
            StringComparison.OrdinalIgnoreCase);
        startBackgroundWorkers ??= !bool.TryParse(
            builder.Configuration["background_workers_enabled"],
            out var configuredWorkers) || configuredWorkers;
        var optionsWereSupplied = options is not null;
        options ??= LoadOptions(builder.Configuration, runningInContainer.Value);
        var deploymentOptions = options;
        var configurationLocks = deploymentEnvironmentVariables is not null
            ? DeploymentConfigurationLocks.FromVariableNames(deploymentEnvironmentVariables)
            : optionsWereSupplied
                ? DeploymentConfigurationLocks.Empty
                : DeploymentConfigurationLocks.FromCurrentProcess();
        var layout = DirectoryLayout.From(options.Paths);
        layout.CreateDataDirectories();
        builder.Services.AddSingleton(
            _ => new RollingFileLoggerProvider(
                new RollingFileLogOptions
                {
                    FilePath = Path.Combine(
                        layout.LogsPath,
                        "animego.log"),
                }));
        builder.Services.AddSingleton<ILoggerProvider>(
            static services =>
                services.GetRequiredService<RollingFileLoggerProvider>());
        var applicationOverrides = new ApplicationOverrideStore(
            layout.ConfigurationPath,
            layout.BackupsPath);
        var applicationOverrideSnapshot = await applicationOverrides
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        options = configurationLocks.Reapply(
            deploymentOptions,
            ApplicationOverrideStore.Apply(options, applicationOverrideSnapshot));
        var downloaderOverrides = new DownloaderOverrideStore(layout.ConfigurationPath);
        var downloaderOverrideSnapshot = await downloaderOverrides.LoadAsync(cancellationToken).ConfigureAwait(false);
        options = ApplyDownloaderOverrides(options, downloaderOverrideSnapshot);
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
        var dataUpdateRuntime = new DataUpdateRuntimeState(options.DataUpdate);
        var database = new AnimeGoSqliteDatabase(layout.DatabaseFile);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var dataPackages = new DataPackageStore(database);
        var dataUpdateTransfers = new DataUpdateTransferStore(database);
        var ownsDataUpdateHttpClient = dataUpdateHttpClient is null;
        dataUpdateHttpClient ??= new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var dataUpdates = new DataUpdateService(
            dataUpdateHttpClient,
            dataUpdateRuntime,
            layout,
            dataPackages,
            dataUpdateTransfers,
            ownsHttpClient: ownsDataUpdateHttpClient);
        var directoryDatabaseScanner = new DirectoryDatabaseScanner();
        var directoryDatabaseIndex = new DirectoryDatabaseIndexStore(
            database,
            directoryDatabaseScanner);
        await directoryDatabaseIndex.RefreshAsync(
            options.Paths.SavePath,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
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
        builder.Services.AddSingleton(new DeploymentConfigurationOptions(deploymentOptions));
        builder.Services.AddSingleton(configurationLocks);
        builder.Services.AddSingleton(dataUpdateRuntime);
        builder.Services.AddSingleton(layout);
        builder.Services.AddSingleton(new RuntimeConfigurationState(
            runningInContainer.Value,
            startBackgroundWorkers.Value,
            !string.IsNullOrWhiteSpace(accessKey)));
        builder.Services.AddSingleton(applicationOverrides);
        builder.Services.AddSingleton(
            new ApplicationConfigurationRuntimeState(applicationOverrideSnapshot.Revision));
        builder.Services.AddSingleton(downloaderOverrides);
        builder.Services.AddSingleton(
            new DownloaderConfigurationRuntimeState(downloaderOverrideSnapshot.Revision));
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton(dataPackages);
        builder.Services.AddSingleton(dataUpdateTransfers);
        builder.Services.AddSingleton<IDataUpdateService>(dataUpdates);
        builder.Services.AddSingleton<MikanRssFeedPlugin>();
        builder.Services.AddSingleton<MikanToolFilterPlugin>();
        builder.Services.AddSingleton<StagedTorrentDispatchSchedulePlugin>();
        builder.Services.AddSingleton<DirectoryDatabaseRefreshSchedulePlugin>();
        builder.Services.AddSingleton<DataUpdateSchedulePlugin>();
        builder.Services.AddSingleton<PluginCatalog>(services =>
            BuiltInPluginCatalog.Create(
            [
                services.GetRequiredService<MikanRssFeedPlugin>(),
                services.GetRequiredService<MikanToolFilterPlugin>(),
                services.GetRequiredService<StagedTorrentDispatchSchedulePlugin>(),
                services.GetRequiredService<DirectoryDatabaseRefreshSchedulePlugin>(),
                services.GetRequiredService<DataUpdateSchedulePlugin>(),
            ]));
        builder.Services.AddSingleton<TitleParserManager>();
        builder.Services.AddSingleton<OrderedFeedFilterManager>();
        builder.Services.AddSingleton<SqliteJsonCacheStore>();
        builder.Services.AddSingleton(directoryDatabaseScanner);
        builder.Services.AddSingleton(directoryDatabaseIndex);
        builder.Services.AddSingleton<DirectoryDatabaseWriter>();
        builder.Services.AddSingleton(sourceProfiles);
        builder.Services.AddSingleton(rssRules);
        builder.Services.AddSingleton(legacyMikanFilters);
        builder.Services.AddSingleton(ingestTasks);
        builder.Services.AddSingleton<UnifiedIngestProcessor>();
        builder.Services.AddSingleton<MikanRssBatchStore>();
        builder.Services.AddSingleton<MikanBangumiSubjectResolver>();
        builder.Services.AddSingleton<MikanRssIngestProcessor>();
        rssDnsResolver ??= new SystemTorrentDnsResolver();
        rssHttpTransport ??= new PinnedTorrentHttpTransport();
        builder.Services.AddSingleton<IRssFeedHttpClient>(new ProfileBoundRssFeedHttpClient(
            sourceProfiles, rssDnsResolver, rssHttpTransport));
        builder.Services.AddSingleton<RssFeedReader>();
        builder.Services.AddSingleton<MikanLegacyFilterProcessor>();
        builder.Services.AddSingleton<PluginScheduleCoordinator>();
        builder.Services.AddSingleton<DataUpdateScheduleManager>();
        builder.Services.AddSingleton(downloadJobs);
        builder.Services.AddSingleton<DownloaderAdminStore>();
        builder.Services.AddSingleton<DownloadPreparationStore>();
        builder.Services.AddSingleton<MediaOrganizationStore>();
        builder.Services.AddSingleton<DeletePlanStore>();
        builder.Services.AddSingleton<DeleteExecutionStore>();
        builder.Services.AddSingleton<MikanWorkMetadataRuleStore>();
        builder.Services.AddSingleton<MikanTrustedOffsetStore>();
        builder.Services.AddSingleton<MetadataResolutionStore>();
        builder.Services.AddSingleton<PendingTmdbStore>();
        builder.Services.AddSingleton<PendingTmdbRecoveryStore>();
        builder.Services.AddSingleton<PendingTmdbNfoRewriteStore>();
        builder.Services.AddSingleton<CompletionRecordStore>();
        builder.Services.AddSingleton<AnimeLibraryStore>();
        builder.Services.AddSingleton<AnimeLibraryAdminStore>();
        if (tmdbPosterTransport is null)
        {
            builder.Services.AddSingleton<ITmdbPosterTransport>(_ =>
                new HttpTmdbPosterTransport(
                    MetadataHttpClientFactory.Create(options.Metadata.Tmdb.ProxyUrl),
                    ownsHttpClient: true));
        }
        else
        {
            builder.Services.AddSingleton(tmdbPosterTransport);
        }
        builder.Services.AddSingleton<AnimeCoverService>();
        builder.Services.AddSingleton(downloadClientRegistry);
        builder.Services.AddSingleton<DownloadClientOperationCoordinator>();
        builder.Services.AddSingleton(torrentStagingService);
        builder.Services.AddSingleton<StagedTorrentDispatcher>();
        builder.Services.AddSingleton<DownloadSnapshotSynchronizer>();
        builder.Services.AddSingleton<DownloadPreparationProcessor>();
        builder.Services.AddSingleton<SafeFileMover>();
        builder.Services.AddSingleton<SafeFileLinker>();
        builder.Services.AddSingleton<TvShowNfoWriter>();
        builder.Services.AddSingleton<PendingTmdbNfoRewriteProcessor>();
        builder.Services.AddSingleton<MediaOrganizationProcessor>();
        builder.Services.AddSingleton<SafeFileDeleter>();
        builder.Services.AddSingleton<DeleteExecutionProcessor>();
        tmdbClient ??= new TmdbClient(
            MetadataHttpClientFactory.Create(options.Metadata.Tmdb.ProxyUrl),
            options.Metadata.Tmdb,
            ownsHttpClient: true);
        builder.Services.AddSingleton(tmdbClient);
        builder.Services.AddSingleton<TmdbAuthority>();
        builder.Services.AddSingleton<TmdbSeriesResolver>();
        builder.Services.AddSingleton<TmdbSeriesSeasonResolver>();
        if (bangumiSubjectClient is null)
        {
            var client = new BangumiSubjectClient(
                MetadataHttpClientFactory.Create(options.Metadata.Bangumi.ProxyUrl),
                options.Metadata.Bangumi,
                ownsHttpClient: true);
            bangumiSubjectClient = client;
            bangumiEpisodeClient ??= client;
        }
        else
        {
            bangumiEpisodeClient ??= bangumiSubjectClient as IBangumiEpisodeClient;
        }

        builder.Services.AddSingleton(bangumiSubjectClient);
        if (bangumiEpisodeClient is not null)
        {
            builder.Services.AddSingleton(bangumiEpisodeClient);
        }
        builder.Services.AddSingleton<BangumiSeasonBacktraceResolver>();
        builder.Services.AddSingleton(new AiPublicationEvidenceResolver(
            bangumiEpisodeClient,
            options.Metadata.Ai));
        aiMetadataMatcher ??= new OpenAiCompatibleMetadataMatcher(
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            options.Metadata.Ai,
            ownsHttpClient: true,
            referenceHttpClient: CreateAiReferenceHttpClient(),
            ownsReferenceHttpClient: true);
        builder.Services.AddSingleton(aiMetadataMatcher);
        builder.Services.AddSingleton<AiMetadataResultValidator>();
        builder.Services.AddSingleton<AiMetadataTaskResolver>();
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
            builder.Services.AddHostedService<PendingTmdbNfoRewriteWorker>();
            builder.Services.AddHostedService<DeleteExecutionWorker>();
            builder.Services.AddHostedService<PluginScheduleHostedService>();
        }
        builder.Services.Configure<JsonOptions>(json =>
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.Use(async (context, next) =>
        {
            if ((context.Request.Path.StartsWithSegments("/api")
                    || context.Request.Path.StartsWithSegments("/websocket"))
                && !string.IsNullOrWhiteSpace(accessKey)
                && !HasValidAccessKey(context.Request, accessKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        ApiEndpoints.Map(app);
        WebSocketLogEndpoint.Map(app);
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
                    BaseUrl = ParseOptionalAbsoluteUri(
                        configuration["tmdb_base_url"],
                        "tmdb_base_url") ?? defaults.Metadata.Tmdb.BaseUrl,
                    ProxyUrl = ParseOptionalAbsoluteUri(
                        configuration["tmdb_proxy_url"],
                        "tmdb_proxy_url"),
                    ApiKey = configuration["tmdb_api_key"],
                    ReadAccessToken = configuration["tmdb_read_access_token"],
                    Language = NormalizeOptional(configuration["tmdb_language"])
                        ?? defaults.Metadata.Tmdb.Language,
                    HttpTimeout = TimeSpan.FromSeconds(ParseOptionalDouble(
                        configuration["tmdb_timeout_second"],
                        defaults.Metadata.Tmdb.HttpTimeout.TotalSeconds,
                        "tmdb_timeout_second")),
                },
                Bangumi = defaults.Metadata.Bangumi with
                {
                    BaseUrl = ParseOptionalAbsoluteUri(
                        configuration["bangumi_base_url"],
                        "bangumi_base_url") ?? defaults.Metadata.Bangumi.BaseUrl,
                    ProxyUrl = ParseOptionalAbsoluteUri(
                        configuration["bangumi_proxy_url"],
                        "bangumi_proxy_url"),
                    HttpTimeout = TimeSpan.FromSeconds(ParseOptionalDouble(
                        configuration["bangumi_timeout_second"],
                        defaults.Metadata.Bangumi.HttpTimeout.TotalSeconds,
                        "bangumi_timeout_second")),
                },
                Ai = defaults.Metadata.Ai with
                {
                    Provider = NormalizeOptional(configuration["ai_provider"])
                        ?? defaults.Metadata.Ai.Provider,
                    BaseUrl = ParseOptionalAbsoluteUri(configuration["ai_base_url"], "ai_base_url"),
                    ApiKey = configuration["ai_api_key"],
                    Model = NormalizeOptional(configuration["ai_model"]),
                    UseMetadataMatch = ParseAiMetadataMatch(
                        configuration,
                        defaults.Metadata.Ai.UseMetadataMatch),
                    HttpTimeout = TimeSpan.FromSeconds(ParseOptionalDouble(
                        configuration["ai_timeout_second"],
                        defaults.Metadata.Ai.HttpTimeout.TotalSeconds,
                        "ai_timeout_second")),
                    RetryCount = ParseOptionalInt(
                        configuration["ai_retry_count"],
                        defaults.Metadata.Ai.RetryCount,
                        "ai_retry_count"),
                    UseBangumiPubDateFirst = ParseOptionalBool(
                        configuration["ai_use_bangumi_pubdate_first"],
                        defaults.Metadata.Ai.UseBangumiPubDateFirst,
                        "ai_use_bangumi_pubdate_first"),
                    TmdbMcpUrl = ParseOptionalAbsoluteUri(
                        configuration["ai_tmdb_mcp_url"],
                        "ai_tmdb_mcp_url") ?? defaults.Metadata.Ai.TmdbMcpUrl,
                    BangumiMcpUrl = ParseOptionalAbsoluteUri(
                        configuration["ai_bangumi_mcp_url"],
                        "ai_bangumi_mcp_url") ?? defaults.Metadata.Ai.BangumiMcpUrl,
                    AniDbMappingUrlTemplate = NormalizeOptional(
                        configuration["ai_anidb_mapping_url_template"])
                        ?? defaults.Metadata.Ai.AniDbMappingUrlTemplate,
                },
            },
            Schedule = defaults.Schedule with
            {
                RefreshDatabaseCron = NormalizeOptional(
                    configuration["refresh_database_cron"])
                    ?? defaults.Schedule.RefreshDatabaseCron,
            },
            DataUpdate = defaults.DataUpdate with
            {
                Enabled = ParseOptionalBool(
                    configuration["data_update_enabled"],
                    defaults.DataUpdate.Enabled,
                    "data_update_enabled"),
                Cron = NormalizeOptional(configuration["data_update_cron"])
                    ?? defaults.DataUpdate.Cron,
                ManifestUrl = ParseOptionalAbsoluteUri(
                    configuration["data_update_manifest_url"],
                    "data_update_manifest_url"),
                AutoDownload = ParseOptionalBool(
                    configuration["data_update_auto_download"],
                    defaults.DataUpdate.AutoDownload,
                    "data_update_auto_download"),
                AutoImport = ParseOptionalBool(
                    configuration["data_update_auto_import"],
                    defaults.DataUpdate.AutoImport,
                    "data_update_auto_import"),
                KeepVersions = ParseOptionalInt(
                    configuration["data_update_keep_versions"],
                    defaults.DataUpdate.KeepVersions,
                    "data_update_keep_versions"),
                HttpTimeout = TimeSpan.FromSeconds(ParseOptionalDouble(
                    configuration["data_update_timeout_second"],
                    defaults.DataUpdate.HttpTimeout.TotalSeconds,
                    "data_update_timeout_second")),
            },
            Downloaders = defaults.Downloaders.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with { DownloadPath = PathBoundary.Combine(downloadPath, pair.Key) },
                StringComparer.OrdinalIgnoreCase),
        };
    }

    private static Uri? ParseOptionalAbsoluteUri(string? value, string name)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{name} must be an absolute URL.");
        }

        return uri;
    }

    private static int ParseOptionalInt(string? value, int defaultValue, string name)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(
            normalized,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed))
        {
            throw new InvalidOperationException($"{name} must be an integer.");
        }

        return parsed;
    }

    private static double ParseOptionalDouble(string? value, double defaultValue, string name)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return defaultValue;
        }

        if (!double.TryParse(
            normalized,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            || !double.IsFinite(parsed))
        {
            throw new InvalidOperationException($"{name} must be a finite number.");
        }

        return parsed;
    }

    private static bool ParseOptionalBool(string? value, bool defaultValue, string name)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return defaultValue;
        }

        if (!bool.TryParse(normalized, out var parsed))
        {
            throw new InvalidOperationException($"{name} must be true or false.");
        }

        return parsed;
    }

    private static HttpClient CreateAiReferenceHttpClient()
    {
        var referenceUri = new Uri(
            AiMatchingOptions.FixedAniDbMappingUrlTemplate.Replace(
                "{anidbid}",
                "1",
                StringComparison.Ordinal));
        var fixedHost = referenceUri.IdnHost;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (!string.Equals(
                    context.DnsEndPoint.Host,
                    fixedHost,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new HttpRequestException(
                        "AI reference lookup connection target is not allowed.");
                }

                var addresses = await Dns.GetHostAddressesAsync(
                    fixedHost,
                    cancellationToken).ConfigureAwait(false);
                var publicAddresses = addresses
                    .Where(TorrentNetworkPolicy.IsPublicAddress)
                    .Distinct()
                    .ToArray();
                if (publicAddresses.Length == 0)
                {
                    throw new HttpRequestException(
                        "AI reference lookup host did not resolve to a public address.");
                }

                SocketException? lastError = null;
                foreach (var address in publicAddresses)
                {
                    var socket = new Socket(
                        address.AddressFamily,
                        SocketType.Stream,
                        ProtocolType.Tcp)
                    {
                        NoDelay = true,
                    };
                    try
                    {
                        await socket.ConnectAsync(
                            new IPEndPoint(
                                address,
                                context.DnsEndPoint.Port),
                            cancellationToken).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException exception)
                    {
                        lastError = exception;
                        socket.Dispose();
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }

                throw new HttpRequestException(
                    "AI reference lookup host was unreachable.",
                    lastError);
            },
        };
        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private static bool ParseAiMetadataMatch(
        ConfigurationManager configuration,
        bool defaultValue)
    {
        var canonical = NormalizeOptional(configuration["ai_use_metadata_match"]);
        if (canonical is not null)
        {
            return ParseOptionalBool(
                canonical,
                defaultValue,
                "ai_use_metadata_match");
        }

        var legacySeason = ParseOptionalBool(
            configuration["ai_use_season_match"],
            defaultValue,
            "ai_use_season_match");
        var legacyEpisode = ParseOptionalBool(
            configuration["ai_use_episode_match"],
            defaultValue,
            "ai_use_episode_match");
        return legacySeason || legacyEpisode;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AnimeGoOptions ApplyDownloaderOverrides(
        AnimeGoOptions options,
        DownloaderOverrideSnapshot snapshot)
    {
        if (snapshot.Downloaders.Count == 0) return options;
        var downloaders = new Dictionary<string, QbittorrentInstanceOptions>(
            options.Downloaders, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, entry) in snapshot.Downloaders)
        {
            if (!Uri.TryCreate(entry.BaseUrl, UriKind.Absolute, out var baseUrl))
            {
                throw new InvalidOperationException(
                    $"Downloader private configuration '{id}' has an invalid base URL.");
            }
            downloaders[id] = new QbittorrentInstanceOptions
            {
                Type = DownloaderTypes.Qbittorrent,
                BaseUrl = baseUrl,
                Username = entry.Username,
                Password = entry.Password,
                DownloadPath = entry.DownloadPath,
                Enabled = entry.Enabled,
            };
        }
        return options with { Downloaders = downloaders };
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
