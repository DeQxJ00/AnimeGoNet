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
using AnimeGoNet.App.Networking;
using AnimeGoNet.App.Notifications;
using AnimeGoNet.App.Library;
using AnimeGoNet.App.Logging;
using AnimeGoNet.App.Plugins;
using AnimeGoNet.App.Scheduling;
using AnimeGoNet.App.Serialization;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Compatibility;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Media;
using AnimeGoNet.Core.Plugins;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Core.Sources;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Cache;
using AnimeGoNet.App.Ingest;
using AnimeGoNet.App.Hosting;
using AnimeGoNet.Data.Feeds;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Deletion;
using AnimeGoNet.Data.DataUpdate;
using AnimeGoNet.Data.Library;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Mikan;
using AnimeGoNet.Data.Notifications;
using AnimeGoNet.Data.Rules;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;
using AnimeGoNet.Data.U2;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AnimeGoNet.App;

public static class AnimeGoApplication
{
    public static async Task<WebApplication> BuildAsync(
        string[] args,
        AnimeGoOptions? options = null,
        string? accessKey = null,
        string? webUiAccessKey = null,
        string? u2AccessKey = null,
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
        LegacyDownloaderMigrationState? legacyDownloaderMigrationState = null,
        CancellationToken cancellationToken = default)
    {
        args = AnimeGoHostCommandLine.Normalize(args);
        var webRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = webRootPath,
        });
        var debugEnabled = ParseOptionalBool(
            FirstConfigurationValue(
                builder.Configuration,
                "ANIMEGO_DEBUG",
                "debug"),
            false,
            "debug");
        var webEnabled = ParseOptionalBool(
            FirstConfigurationValue(
                builder.Configuration,
                "ANIMEGO_WEB",
                "web"),
            true,
            "web");
        builder.Logging.SetMinimumLevel(
            debugEnabled ? LogLevel.Debug : LogLevel.Information);
        if (!webEnabled)
        {
            builder.Services.Replace(
                ServiceDescriptor.Singleton<IServer, HeadlessServer>());
        }
        builder.Services.Configure<HostOptions>(host =>
            host.ShutdownTimeout = TimeSpan.FromSeconds(5));
        var webSocketLogHub = new WebSocketLogHub();
        builder.Services.AddSingleton(webSocketLogHub);
        builder.Services.AddSingleton<ILoggerProvider>(
            static services =>
                services.GetRequiredService<WebSocketLogHub>());

        runningInContainer ??= string.Equals(
            builder.Configuration["DOTNET_RUNNING_IN_CONTAINER"],
            "true",
            StringComparison.OrdinalIgnoreCase);
        var optionsWereSupplied = options is not null;
        DeploymentYamlSnapshot? deploymentYaml = null;
        if (!optionsWereSupplied)
        {
            var yamlDefaults = runningInContainer.Value
                ? AnimeGoDefaults.CreateDocker()
                : AnimeGoDefaults.CreateNative(AppContext.BaseDirectory);
            yamlDefaults = ApplyBootstrapPaths(
                yamlDefaults,
                builder.Configuration);
            var yamlPath = DeploymentYamlConfiguration.ResolvePath(
                builder.Configuration,
                yamlDefaults);
            var backupLegacy = ParseOptionalBool(
                FirstConfigurationValue(
                    builder.Configuration,
                    "ANIMEGO_CONFIG_BACKUP",
                    "backup"),
                true,
                "backup");
            deploymentYaml = await DeploymentYamlConfiguration
                .LoadOrCreateAsync(
                    yamlPath,
                    yamlDefaults,
                    backupLegacy,
                    cancellationToken)
                .ConfigureAwait(false);
            builder.Configuration.AddInMemoryCollection(deploymentYaml.Values);
            builder.Configuration.AddEnvironmentVariables();
            if (args.Length > 0)
            {
                builder.Configuration.AddCommandLine(args);
            }

            options = LoadOptions(builder.Configuration, runningInContainer.Value);
        }
        if (options is null)
        {
            throw new InvalidOperationException(
                "AnimeGoNet options were not initialized.");
        }

        startBackgroundWorkers ??= ParseOptionalBool(
            FirstConfigurationValue(
                builder.Configuration,
                "background_workers_enabled",
                "web:background_workers_enabled"),
            true,
            "background_workers_enabled");
        legacyDownloaderMigrationState ??= optionsWereSupplied
            ? LegacyDownloaderMigrationState.None
            : LegacyDownloaderMigrationDetector.Detect(
                builder.Configuration["ANIMEGO_CLIENT"],
                options.Paths.DataPath,
                builder.Configuration["ANIMEGO_CONFIG"]
                    ?? builder.Configuration["config"]
                    ?? deploymentYaml?.FilePath);
        if (legacyDownloaderMigrationState.BlocksDownloads)
        {
            startBackgroundWorkers = false;
        }
        var deploymentOptions = options;
        var configurationLocks = deploymentEnvironmentVariables is not null
            ? DeploymentConfigurationLocks.FromSources(
                deploymentEnvironmentVariables,
                args)
            : optionsWereSupplied
                ? DeploymentConfigurationLocks.Empty
                : DeploymentConfigurationLocks.FromCurrentProcess(args);
        var downloaderLocks = deploymentEnvironmentVariables is not null
            ? DownloaderDeploymentLocks.FromSources(
                deploymentEnvironmentVariables,
                args)
            : optionsWereSupplied
                ? DownloaderDeploymentLocks.Empty
                : DownloaderDeploymentLocks.FromCurrentProcess(args);
        var sourceProfileLocks = deploymentEnvironmentVariables is not null
            ? SourceProfileDeploymentLocks.FromSources(
                deploymentEnvironmentVariables,
                args)
            : optionsWereSupplied
                ? SourceProfileDeploymentLocks.Empty
                : SourceProfileDeploymentLocks.FromCurrentProcess(args);
        var layout = DirectoryLayout.From(options.Paths);
        builder.Services.AddSingleton(layout);
        layout.CreateDataDirectories();
        var externalPluginLoader = new ExternalPluginManifestLoader(layout.PluginsPath);
        var externalPluginDiscovery = await externalPluginLoader
            .DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var externalPluginConfigurations = new ExternalPluginConfigurationStore(
            layout.ConfigurationPath);
        await externalPluginConfigurations.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
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
        options = downloaderLocks.Reapply(deploymentOptions, options);
        accessKey ??= FirstConfigurationValue(
            builder.Configuration,
            "ANIMEGO_INNER_PLUGIN_MIKAN_ACCESS_KEY",
            "ANIMEGO_PLUGIN_ACCESS_KEY",
            "ANIMEGO_WEB_ACCESS_KEY",
            "inner_plugin_mikan_access_key",
            "inner_plugin_mikan:access_key",
            "access_key",
            "web:access_key");
        u2AccessKey ??= FirstConfigurationValue(
            builder.Configuration,
            "ANIMEGO_INNER_PLUGIN_U2_ACCESS_KEY",
            "inner_plugin_u2_access_key",
            "inner_plugin_u2:access_key");
        webUiAccessKey ??= FirstConfigurationValue(
            builder.Configuration,
            "ANIMEGO_WEBUI_ACCESS_KEY",
            "webui_access_key",
            "web:webui_access_key");
        if (runningInContainer.Value && string.IsNullOrWhiteSpace(accessKey))
        {
            throw new InvalidOperationException("Docker mode requires a non-empty access_key.");
        }
        var errors = AnimeGoOptionsValidator.Validate(options).ToList();
        if (options.Metadata.Ai.PromptTemplate is not null)
        {
            try
            {
                AiMetadataPromptRenderer.ValidateTemplate(options.Metadata.Ai.PromptTemplate);
            }
            catch (AiMetadataMatcherException exception)
            {
                errors.Add($"AI Prompt template is invalid ({exception.SafeCode}).");
            }
        }
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid AnimeGoNet configuration: " + string.Join("; ", errors));
        }
        var rollingFileLoggerProvider = new RollingFileLoggerProvider(
            new RollingFileLogOptions
            {
                FilePath = Path.Combine(
                    layout.LogsPath,
                    "animego.log"),
                MinimumLevel = debugEnabled
                    ? LogLevel.Debug
                    : LogLevel.Information,
            });
        builder.Services.AddSingleton(rollingFileLoggerProvider);
        builder.Services.AddSingleton<ILoggerProvider>(
            static services =>
                services.GetRequiredService<RollingFileLoggerProvider>());
        var outboundHttpLogs = new OutboundHttpLogSink(
            webSocketLogHub.CreateLogger("AnimeGoNet.App.Http.Outbound"),
            rollingFileLoggerProvider.CreateLogger("AnimeGoNet.App.Http.Outbound"));
        if (!optionsWereSupplied && webEnabled)
        {
            ConfigureWebBinding(builder, options.Web);
        }
        var dataUpdateRuntime = new DataUpdateRuntimeState(options.DataUpdate);
        var database = new AnimeGoSqliteDatabase(layout.DatabaseFile);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var dataPackages = new DataPackageStore(database);
        var bangumiArchive = new BangumiArchiveStore(database);
        var anidbTitleCache = new AnidbTitleCacheStore(database);
        var dataUpdateTransfers = new DataUpdateTransferStore(database);
        var ownsDataUpdateHttpClient = dataUpdateHttpClient is null;
        dataUpdateHttpClient ??= OutboundHttpClientFactory.Create(
            options.OutboundProxy,
            outboundHttpLogs,
            "AnimeGoNetData");
        var dataUpdates = new DataUpdateService(
            dataUpdateHttpClient,
            dataUpdateRuntime,
            layout,
            dataPackages,
            dataUpdateTransfers,
            ownsHttpClient: ownsDataUpdateHttpClient);
        var anidbHttpClient = OutboundHttpClientFactory.Create(
            options.OutboundProxy,
            outboundHttpLogs,
            "AniDB titles");
        var anidbTitleCacheService = new AnidbTitleCacheService(
            anidbHttpClient,
            layout,
            anidbTitleCache,
            ownsHttpClient: true);
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
        foreach (var seed in options.InitialSourceProfiles)
        {
            if (sourceProfileLocks.CreateOverride(seed) is { } deploymentOverride)
            {
                await sourceProfiles.ApplyDeploymentOverrideAsync(
                    deploymentOverride,
                    DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        await sourceProfiles.RecoverInterruptedScheduledRunsAsync(
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
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
            new PinnedTorrentHttpTransport(
                options.OutboundProxy,
                outboundHttpLogs,
                "Torrent"));
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
        downloadClientRegistry = legacyDownloaderMigrationState.BlocksDownloads
            ? new BlockedDownloadClientRegistry()
            : downloadClientRegistry ?? new QbittorrentClientRegistry(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new DeploymentConfigurationOptions(deploymentOptions));
        builder.Services.AddSingleton(new LegacyDeploymentConfigurationFile(
            deploymentYaml?.FilePath
                ?? Path.Combine(options.Paths.DataPath, "animego.yaml"),
            deploymentOptions,
            runningInContainer.Value
                ? AnimeGoDefaults.CreateDocker()
                : AnimeGoDefaults.CreateNative(AppContext.BaseDirectory),
            runningInContainer.Value));
        builder.Services.AddSingleton(configurationLocks);
        builder.Services.AddSingleton(downloaderLocks);
        builder.Services.AddSingleton(sourceProfileLocks);
        builder.Services.AddSingleton(dataUpdateRuntime);
        builder.Services.AddSingleton(layout);
        builder.Services.AddSingleton(new RuntimeConfigurationState(
            runningInContainer.Value,
            startBackgroundWorkers.Value,
            !string.IsNullOrWhiteSpace(accessKey),
            !string.IsNullOrWhiteSpace(u2AccessKey),
            !string.IsNullOrWhiteSpace(webUiAccessKey)));
        builder.Services.AddSingleton<RuntimeResourceMetricsService>();
        builder.Services.AddSingleton(legacyDownloaderMigrationState);
        builder.Services.AddSingleton(externalPluginLoader);
        builder.Services.AddSingleton(externalPluginDiscovery);
        builder.Services.AddSingleton(externalPluginConfigurations);
        builder.Services.AddSingleton<ExternalPluginConfigurationValidator>();
        builder.Services.AddSingleton<ExternalPluginHostManager>(services =>
            new ExternalPluginHostManager(
                externalPluginLoader,
                externalPluginDiscovery,
                layout.PluginDataPath,
                configurations: externalPluginConfigurations,
                loggerFactory: services.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddSingleton<ExternalPluginConfigurationService>();
        builder.Services.AddSingleton(applicationOverrides);
        builder.Services.AddSingleton(
            new ApplicationConfigurationRuntimeState(applicationOverrideSnapshot.Revision));
        builder.Services.AddSingleton(downloaderOverrides);
        builder.Services.AddSingleton(
            new DownloaderConfigurationRuntimeState(downloaderOverrideSnapshot.Revision));
        builder.Services.AddSingleton<ConfigurationBackupAutomationStore>();
        builder.Services.AddSingleton<ConfigurationArchiveService>();
        builder.Services.AddSingleton<ConfigurationBackupAutomationRunner>();
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton(dataPackages);
        builder.Services.AddSingleton(anidbTitleCache);
        builder.Services.AddSingleton<IAnidbTitleCacheService>(anidbTitleCacheService);
        builder.Services.AddSingleton(dataUpdateTransfers);
        builder.Services.AddSingleton<IDataUpdateService>(dataUpdates);
        builder.Services.AddSingleton<MikanRssFeedPlugin>();
        builder.Services.AddSingleton<MikanToolFilterPlugin>();
        builder.Services.AddSingleton<StagedTorrentDispatchSchedulePlugin>();
        builder.Services.AddSingleton<DirectoryDatabaseRefreshSchedulePlugin>();
        builder.Services.AddSingleton<DataUpdateSchedulePlugin>();
        builder.Services.AddSingleton<MikanRssIngestSchedulePlugin>();
        builder.Services.AddSingleton<PluginCatalog>(services =>
        {
            IAnimeGoPlugin[] applicationPlugins =
            [
                services.GetRequiredService<MikanRssFeedPlugin>(),
                services.GetRequiredService<MikanToolFilterPlugin>(),
                services.GetRequiredService<StagedTorrentDispatchSchedulePlugin>(),
                services.GetRequiredService<DirectoryDatabaseRefreshSchedulePlugin>(),
                services.GetRequiredService<DataUpdateSchedulePlugin>(),
                services.GetRequiredService<MikanRssIngestSchedulePlugin>(),
            ];
            var externalPlugins = ExternalPluginAdapterFactory.Create(
                externalPluginDiscovery,
                services.GetRequiredService<ExternalPluginHostManager>());
            return BuiltInPluginCatalog.Create(applicationPlugins.Concat(externalPlugins));
        });
        builder.Services.AddSingleton<TitleParserManager>();
        builder.Services.AddSingleton<OrderedFeedFilterManager>();
        var jsonCache = new SqliteJsonCacheStore(database);
        builder.Services.AddSingleton(jsonCache);
        builder.Services.AddSingleton(new MikanEpisodeIdentityCache(
            jsonCache,
            options.Metadata.Mikan.EpisodeIdentityCacheTtl));
        builder.Services.AddSingleton(new MikanBangumiIdentityCache(
            jsonCache,
            options.Metadata.Mikan.BangumiIdentityCacheTtl));
        builder.Services.AddSingleton(directoryDatabaseScanner);
        builder.Services.AddSingleton(directoryDatabaseIndex);
        builder.Services.AddSingleton<DirectoryDatabaseWriter>();
        builder.Services.AddSingleton(sourceProfiles);
        builder.Services.AddSingleton(rssRules);
        builder.Services.AddSingleton(legacyMikanFilters);
        builder.Services.AddSingleton(ingestTasks);
        builder.Services.AddSingleton<UnifiedIngestProcessor>();
        builder.Services.AddSingleton<MikanRssBatchStore>();
        builder.Services.AddSingleton<MikanRssTaskEvidenceStore>();
        builder.Services.AddSingleton<MikanFeedIdentityResolver>();
        builder.Services.AddSingleton<MikanBangumiSubjectResolver>();
        builder.Services.AddSingleton<MikanRssIngestProcessor>();
        rssDnsResolver ??= new SystemTorrentDnsResolver();
        rssHttpTransport ??= new PinnedTorrentHttpTransport(
            options.OutboundProxy,
            outboundHttpLogs,
            "Mikan");
        var profileRssClient = new ProfileBoundRssFeedHttpClient(
            sourceProfiles, rssDnsResolver, rssHttpTransport, options);
        builder.Services.AddSingleton<IRssFeedHttpClient>(profileRssClient);
        builder.Services.AddSingleton<ISourceProfileRssFeedHttpClient>(profileRssClient);
        builder.Services.AddSingleton<RssFeedReader>();
        builder.Services.AddSingleton<MikanSeasonCompletionService>();
        builder.Services.AddSingleton<MikanLegacyFilterProcessor>();
        builder.Services.AddSingleton<PluginScheduleCoordinator>();
        builder.Services.AddSingleton<DataUpdateScheduleManager>();
        builder.Services.AddSingleton<SourceRssScheduleManager>();
        builder.Services.AddSingleton(downloadJobs);
        builder.Services.AddSingleton<DownloaderAdminStore>();
        builder.Services.AddSingleton<DownloadPreparationStore>();
        builder.Services.AddSingleton<MediaOrganizationStore>();
        builder.Services.AddSingleton<DeletePlanStore>();
        builder.Services.AddSingleton<DeleteExecutionStore>();
        builder.Services.AddSingleton<MikanWorkMetadataRuleStore>();
        builder.Services.AddSingleton<MikanTrustedOffsetStore>();
        builder.Services.AddSingleton<MikanManualSeriesMappingStore>();
        builder.Services.AddSingleton<MikanPluginCallLogStore>();
        builder.Services.AddSingleton<U2PluginCallLogStore>();
        builder.Services.AddSingleton<MikanPublishGroupStore>();
        builder.Services.AddSingleton<NotificationStore>();
        builder.Services.AddSingleton(new WebhookNotificationSender(
            OutboundHttpClientFactory.Create(
                options.OutboundProxy,
                outboundHttpLogs,
                "Notification")));
        builder.Services.AddSingleton<NotificationProcessor>();
        builder.Services.AddSingleton<MetadataResolutionStore>();
        builder.Services.AddSingleton<OtherFileReadaptationStore>();
        builder.Services.AddSingleton<MixedMediaPostprocessStore>();
        builder.Services.AddSingleton<AiSeriesChangeReviewStore>();
        var metadataRefreshScope = new MetadataRefreshScope();
        builder.Services.AddSingleton(metadataRefreshScope);
        builder.Services.AddSingleton<PendingTmdbStore>();
        builder.Services.AddSingleton<PendingTmdbRecoveryStore>();
        builder.Services.AddSingleton<PendingTmdbNfoRewriteStore>();
        builder.Services.AddSingleton<CompletionRecordStore>();
        builder.Services.AddSingleton<AnimeLibraryStore>();
        builder.Services.AddSingleton<AnimeLibraryAdminStore>();
        builder.Services.AddSingleton<ExternalMediaImportStore>();
        if (tmdbPosterTransport is null)
        {
            builder.Services.AddSingleton<ITmdbPosterTransport>(_ =>
                new HttpTmdbPosterTransport(
                    MetadataHttpClientFactory.Create(
                        options.OutboundProxy,
                        outboundHttpLogs,
                        "TMDB image"),
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
        builder.Services.AddSingleton<MovieNfoWriter>();
        builder.Services.AddSingleton<PendingTmdbNfoRewriteProcessor>();
        builder.Services.AddSingleton<MediaOrganizationProcessor>();
        builder.Services.AddSingleton<SafeFileDeleter>();
        builder.Services.AddSingleton<DeleteExecutionProcessor>();
        tmdbClient ??= new TmdbCachingClient(
            new TmdbClient(
                MetadataHttpClientFactory.Create(
                    options.OutboundProxy,
                    outboundHttpLogs,
                    "TMDB"),
                options.Metadata.Tmdb,
                ownsHttpClient: true),
            jsonCache,
            options.Metadata.Tmdb,
            ownsInner: true,
            refreshScope: metadataRefreshScope);
        var registeredTmdbClient = tmdbClient;
        builder.Services.AddSingleton<ITmdbClient>(_ => registeredTmdbClient);
        builder.Services.AddSingleton<ITmdbMovieClient>(_ =>
            registeredTmdbClient as ITmdbMovieClient ?? UnavailableTmdbMovieClient.Instance);
        builder.Services.AddSingleton<TmdbAuthority>();
        builder.Services.AddSingleton<TmdbSeriesResolver>();
        builder.Services.AddSingleton<TmdbSeriesSeasonResolver>();
        builder.Services.AddSingleton<TmdbMovieResolver>();
        builder.Services.AddSingleton<SubtitleArchiveImportService>();
        builder.Services.AddSingleton<SubtitleAiPromptStore>();
        builder.Services.AddSingleton<U2AniDbMetadataResolver>(services =>
            new U2AniDbMetadataResolver(
                MetadataHttpClientFactory.Create(
                    options.OutboundProxy,
                    outboundHttpLogs,
                    "AniDB TMDB mapping"),
                anidbTitleCache,
                services.GetRequiredService<TmdbSeriesResolver>(),
                services.GetRequiredService<ITmdbClient>()));
        if (bangumiSubjectClient is null)
        {
            var upstream = new BangumiSubjectClient(
                MetadataHttpClientFactory.Create(
                    options.OutboundProxy,
                    outboundHttpLogs,
                    "Bangumi"),
                options.Metadata.Bangumi,
                ownsHttpClient: true);
            var cached = new BangumiArchiveCachingClient(
                bangumiArchive,
                upstream,
                upstream,
                ownsClients: true,
                refreshScope: metadataRefreshScope);
            bangumiSubjectClient = cached;
            bangumiEpisodeClient ??= cached;
        }
        else
        {
            bangumiEpisodeClient ??= bangumiSubjectClient as IBangumiEpisodeClient;
        }

        builder.Services.AddSingleton(bangumiSubjectClient);
        builder.Services.AddSingleton(bangumiArchive);
        if (bangumiEpisodeClient is not null)
        {
            builder.Services.AddSingleton(bangumiEpisodeClient);
        }
        builder.Services.AddSingleton<MikanRssMultiFileCandidatePreflight>();
        builder.Services.AddSingleton<BangumiSeasonBacktraceResolver>();
        builder.Services.AddSingleton(new AiPublicationEvidenceResolver(
            bangumiEpisodeClient,
            options.Metadata.Ai));
        builder.Services.AddSingleton(new AiMetadataDebugTraceStore(layout));
        aiMetadataMatcher ??= new OpenAiCompatibleMetadataMatcher(
            OutboundHttpClientFactory.Create(
                options.OutboundProxy,
                outboundHttpLogs,
                "AI"),
            options.Metadata.Ai,
            ownsHttpClient: true,
            referenceHttpClient: CreateAiReferenceHttpClient(),
            ownsReferenceHttpClient: true);
        builder.Services.AddSingleton(aiMetadataMatcher);
        builder.Services.AddSingleton<AiMetadataResultValidator>();
        builder.Services.AddSingleton<MikanAiTestImportService>();
        builder.Services.AddSingleton<MikanPublishGroupResolver>();
        builder.Services.AddSingleton<AnimeGoNet.App.AiTesterCompat.AiTesterCoordinator>();
        builder.Services.AddSingleton<AiMetadataTaskResolver>();
        builder.Services.AddSingleton<DuplicateHitNotifier>();
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
            builder.Services.AddHostedService<NotificationWorker>();
            builder.Services.AddHostedService<ConfigurationBackupAutomationWorker>();
            builder.Services.AddHostedService<MikanPublishGroupWorker>();
            builder.Services.AddHostedService<AnidbTitleCacheWorker>();
        }
        builder.Services.Configure<JsonOptions>(json =>
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));
        builder.Services.AddAnimeGoOpenApi();

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var isU2PluginApi = IsU2PluginApiPath(path);
            var isPluginApi = isU2PluginApi || IsPluginApiPath(path);
            var isWebUiApi = !isPluginApi
                && (path.StartsWithSegments("/api")
                    || path.StartsWithSegments("/websocket"));
            if (isU2PluginApi
                && (string.IsNullOrWhiteSpace(u2AccessKey)
                    || !HasValidPluginAccessKey(context.Request, u2AccessKey)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (!isU2PluginApi
                && isPluginApi
                && !string.IsNullOrWhiteSpace(accessKey)
                && !HasValidPluginAccessKey(context.Request, accessKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (isWebUiApi
                && !string.IsNullOrWhiteSpace(webUiAccessKey)
                && !HasValidWebUiAccessKey(context.Request, webUiAccessKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        ApiEndpoints.Map(app);
        WebSocketLogEndpoint.Map(app);
        app.MapOpenApi().ExcludeFromDescription();
        app.MapFallbackToFile("index.html");
        return app;
    }

    internal static AnimeGoOptions LoadOptions(ConfigurationManager configuration, bool inContainer)
    {
        var defaults = inContainer
            ? AnimeGoDefaults.CreateDocker()
            : AnimeGoDefaults.CreateNative(AppContext.BaseDirectory);

        var dataPath = ResolveConfiguredPath(
            FirstConfigurationValue(
                configuration,
                "ANIMEGO_DATA_PATH",
                "data_path",
                "paths:data_path")
            ?? defaults.Paths.DataPath);
        var downloadPath = ResolveConfiguredPath(
            FirstConfigurationValue(
                configuration,
                "ANIMEGO_DOWNLOAD_PATH",
                "download_path",
                "paths:download_path")
            ?? defaults.Paths.DownloadPath);
        var savePath = ResolveConfiguredPath(
            FirstConfigurationValue(
                configuration,
                "ANIMEGO_SAVE_PATH",
                "save_path",
                "paths:save_path")
            ?? defaults.Paths.SavePath);
        var movieSavePath = ResolveConfiguredPath(
            FirstConfigurationValue(
                configuration,
                "ANIMEGO_MOVIE_SAVE_PATH",
                "movie_save_path",
                "paths:movie_save_path")
            ?? defaults.Paths.MovieSavePath);
        var paths = new PathOptions
        {
            DataPath = dataPath,
            DownloadPath = downloadPath,
            SavePath = savePath,
            MovieSavePath = movieSavePath,
        };
        var downloaders = LoadDownloaders(configuration, defaults, downloadPath);
        var sources = LoadSourceProfiles(configuration, defaults);
        var web = new WebBindingOptions
        {
            Host = NormalizeOptional(FirstConfigurationValue(
                    configuration,
                    "ANIMEGO_WEB_HOST",
                    "web_host",
                    "web:host"))
                ?? defaults.Web.Host,
            Port = ParseOptionalInt(
                FirstConfigurationValue(
                    configuration,
                    "ANIMEGO_WEB_PORT",
                    "web_port",
                    "web:port"),
                defaults.Web.Port,
                "web_port"),
        };

        return defaults with
        {
            Paths = paths,
            Web = web,
            OutboundProxy = new OutboundProxyOptions
            {
                Url = ParseOptionalAbsoluteUri(
                    FirstPresentConfigurationValue(
                        configuration,
                        "outbound_proxy_url",
                        "ANIMEGO_OUTBOUND_PROXY_URL",
                        "outbound_proxy:url"),
                    "outbound_proxy_url"),
                HostPatterns = LoadOutboundProxyHosts(configuration),
            },
            Metadata = defaults.Metadata with
            {
                Mikan = defaults.Metadata.Mikan with
                {
                    BaseUrl = ParseOptionalAbsoluteUri(
                        FirstConfigurationValue(
                            configuration,
                            "mikan_base_url",
                            "metadata:mikan:base_url"),
                        "mikan_base_url") ?? defaults.Metadata.Mikan.BaseUrl,
                    EpisodeIdentityCacheTtl = TimeSpan.FromHours(ParseOptionalDouble(
                        FirstConfigurationValue(
                            configuration,
                            "mikan_episode_identity_cache_hours",
                            "metadata:mikan:episode_identity_cache_hours"),
                        defaults.Metadata.Mikan.EpisodeIdentityCacheTtl.TotalHours,
                        "mikan_episode_identity_cache_hours")),
                    BangumiIdentityCacheTtl = TimeSpan.FromHours(ParseOptionalDouble(
                        FirstConfigurationValue(
                            configuration,
                            "mikan_bangumi_identity_cache_hours",
                            "metadata:mikan:bangumi_identity_cache_hours"),
                        defaults.Metadata.Mikan.BangumiIdentityCacheTtl.TotalHours,
                        "mikan_bangumi_identity_cache_hours")),
                },
                Tmdb = defaults.Metadata.Tmdb with
                {
                    BaseUrl = ParseOptionalAbsoluteUri(
                        FirstConfigurationValue(
                            configuration,
                            "tmdb_base_url",
                            "metadata:tmdb:base_url"),
                        "tmdb_base_url") ?? defaults.Metadata.Tmdb.BaseUrl,
                    ImageBaseUrl = ParseOptionalAbsoluteUri(
                        FirstConfigurationValue(
                            configuration,
                            "tmdb_image_base_url",
                            "metadata:tmdb:image_base_url"),
                        "tmdb_image_base_url") ?? defaults.Metadata.Tmdb.ImageBaseUrl,
                    ApiKey = NormalizeOptional(FirstConfigurationValue(
                        configuration,
                        "ANIMEGO_THEMOVIEDB_KEY",
                        "tmdb_api_key",
                        "metadata:tmdb:api_key")),
                    ReadAccessToken = NormalizeOptional(FirstConfigurationValue(
                        configuration,
                        "tmdb_read_access_token",
                        "metadata:tmdb:read_access_token")),
                    Language = NormalizeOptional(FirstConfigurationValue(
                            configuration,
                            "tmdb_language",
                            "metadata:tmdb:language"))
                        ?? defaults.Metadata.Tmdb.Language,
                    HttpTimeout = TimeSpan.FromSeconds(ParseOptionalDouble(
                        FirstConfigurationValue(
                            configuration,
                            "tmdb_timeout_second",
                            "metadata:tmdb:timeout_seconds"),
                        defaults.Metadata.Tmdb.HttpTimeout.TotalSeconds,
                        "tmdb_timeout_second")),
                    RetryCount = ParseOptionalInt(
                        FirstConfigurationValue(
                            configuration,
                            "tmdb_retry_count",
                            "metadata:tmdb:retry_count"),
                        defaults.Metadata.Tmdb.RetryCount,
                        "tmdb_retry_count"),
                    RetryDelay = TimeSpan.FromSeconds(ParseOptionalDouble(
                        FirstConfigurationValue(
                            configuration,
                            "tmdb_retry_wait_second",
                            "metadata:tmdb:retry_wait_seconds"),
                        defaults.Metadata.Tmdb.RetryDelay.TotalSeconds,
                        "tmdb_retry_wait_second")),
                    CacheTtl = TimeSpan.FromHours(ParseOptionalDouble(
                        FirstConfigurationValue(
                            configuration,
                            "tmdb_cache_hour",
                            "advanced:cache:themoviedb_cache_hour",
                            "metadata:tmdb:cache_hours"),
                        defaults.Metadata.Tmdb.CacheTtl.TotalHours,
                        "tmdb_cache_hour")),
                },
                Bangumi = defaults.Metadata.Bangumi with
                {
                    BaseUrl = ParseOptionalAbsoluteUri(
                        FirstConfigurationValue(
                            configuration,
                            "bangumi_base_url",
                            "metadata:bangumi:base_url"),
                        "bangumi_base_url") ?? defaults.Metadata.Bangumi.BaseUrl,
                    HttpTimeout = TimeSpan.FromSeconds(ParseOptionalDouble(
                        FirstConfigurationValue(
                            configuration,
                            "bangumi_timeout_second",
                            "metadata:bangumi:timeout_seconds"),
                        defaults.Metadata.Bangumi.HttpTimeout.TotalSeconds,
                        "bangumi_timeout_second")),
                    RetryCount = ParseOptionalInt(
                        FirstConfigurationValue(
                            configuration,
                            "bangumi_retry_count",
                            "metadata:bangumi:retry_count"),
                        defaults.Metadata.Bangumi.RetryCount,
                        "bangumi_retry_count"),
                    RetryDelay = TimeSpan.FromSeconds(ParseOptionalDouble(
                        FirstConfigurationValue(
                            configuration,
                            "bangumi_retry_wait_second",
                            "metadata:bangumi:retry_wait_seconds"),
                        defaults.Metadata.Bangumi.RetryDelay.TotalSeconds,
                        "bangumi_retry_wait_second")),
                },
                SeasonFailure = defaults.Metadata.SeasonFailure with
                {
                    Skip = ParseOptionalBool(
                        FirstConfigurationValue(
                            configuration,
                            "tmdb_fail_skip",
                            "metadata:season_failure:skip"),
                        defaults.Metadata.SeasonFailure.Skip,
                        "tmdb_fail_skip"),
                    Backtrace = ParseOptionalBool(
                        FirstConfigurationValue(
                            configuration,
                            "tmdb_fail_backtrace",
                            "metadata:season_failure:backtrace"),
                        defaults.Metadata.SeasonFailure.Backtrace,
                        "tmdb_fail_backtrace"),
                    UseTitleSeason = ParseOptionalBool(
                        FirstConfigurationValue(
                            configuration,
                            "tmdb_fail_use_title_season",
                            "metadata:season_failure:use_title_season"),
                        defaults.Metadata.SeasonFailure.UseTitleSeason,
                        "tmdb_fail_use_title_season"),
                    UseFirstSeason = ParseOptionalBool(
                        FirstConfigurationValue(
                            configuration,
                            "tmdb_fail_use_first_season",
                            "metadata:season_failure:use_first_season"),
                        defaults.Metadata.SeasonFailure.UseFirstSeason,
                        "tmdb_fail_use_first_season"),
                },
                Ai = defaults.Metadata.Ai with
                {
                    Provider = NormalizeOptional(FirstConfigurationValue(
                            configuration,
                            "ai_provider",
                            "metadata:ai:provider"))
                        ?? defaults.Metadata.Ai.Provider,
                    BaseUrl = ParseOptionalAbsoluteUri(
                        FirstConfigurationValue(
                            configuration,
                            "ai_base_url",
                            "metadata:ai:base_url"),
                        "ai_base_url"),
                    ApiKey = NormalizeOptional(FirstConfigurationValue(
                        configuration,
                        "ai_api_key",
                        "metadata:ai:api_key")),
                    Model = NormalizeOptional(FirstConfigurationValue(
                        configuration,
                        "ai_model",
                        "metadata:ai:model")),
                    ReasoningEffort = ParseOptionalReasoningEffort(
                        FirstConfigurationValue(
                            configuration,
                            "ai_reasoning_effort",
                            "metadata:ai:reasoning_effort")),
                    PromptTemplate = NormalizeOptional(FirstConfigurationValue(
                        configuration,
                        "ai_prompt_template",
                        "metadata:ai:prompt_template")),
                    UseMetadataMatch = ParseAiMetadataMatch(
                        configuration,
                        ParseOptionalBool(
                            FirstConfigurationValue(
                                configuration,
                                "metadata:ai:use_metadata_match"),
                            defaults.Metadata.Ai.UseMetadataMatch,
                            "metadata:ai:use_metadata_match")),
                    DebugMode = ParseOptionalBool(
                        FirstConfigurationValue(
                            configuration,
                            "ai_debug_mode",
                            "metadata:ai:debug_mode"),
                        defaults.Metadata.Ai.DebugMode,
                        "ai_debug_mode"),
                    HttpTimeout = TimeSpan.FromSeconds(ParseOptionalDouble(
                        FirstConfigurationValue(
                            configuration,
                            "ai_timeout_second",
                            "metadata:ai:timeout_seconds"),
                        defaults.Metadata.Ai.HttpTimeout.TotalSeconds,
                        "ai_timeout_second")),
                    RetryCount = ParseOptionalInt(
                        FirstConfigurationValue(
                            configuration,
                            "ai_retry_count",
                            "metadata:ai:retry_count"),
                        defaults.Metadata.Ai.RetryCount,
                        "ai_retry_count"),
                    UseBangumiPubDateFirst = ParseOptionalBool(
                        FirstConfigurationValue(
                            configuration,
                            "ai_use_bangumi_pubdate_first",
                            "metadata:ai:use_bangumi_pubdate_first"),
                        defaults.Metadata.Ai.UseBangumiPubDateFirst,
                        "ai_use_bangumi_pubdate_first"),
                    TmdbMcpUrl = ParseOptionalAbsoluteUri(
                        FirstConfigurationValue(
                            configuration,
                            "ai_tmdb_mcp_url",
                            "metadata:ai:tmdb_mcp_url"),
                        "ai_tmdb_mcp_url") ?? defaults.Metadata.Ai.TmdbMcpUrl,
                    BangumiMcpUrl = ParseOptionalAbsoluteUri(
                        FirstConfigurationValue(
                            configuration,
                            "ai_bangumi_mcp_url",
                            "metadata:ai:bangumi_mcp_url"),
                        "ai_bangumi_mcp_url") ?? defaults.Metadata.Ai.BangumiMcpUrl,
                    AniDbMappingUrlTemplate = NormalizeOptional(
                        FirstConfigurationValue(
                            configuration,
                            "ai_anidb_mapping_url_template",
                            "metadata:ai:anidb_mapping_url_template"))
                        ?? defaults.Metadata.Ai.AniDbMappingUrlTemplate,
                },
                TmdbFailureUseBangumi = ParseOptionalBool(
                    FirstConfigurationValue(
                        configuration,
                        "tmdb_fail_use_bangumi",
                        "metadata:tmdb_failure_use_bangumi"),
                    defaults.Metadata.TmdbFailureUseBangumi,
                    "tmdb_fail_use_bangumi"),
                WriteBangumiIdWhenTmdbMatched = ParseOptionalBool(
                    FirstConfigurationValue(
                        configuration,
                        "write_bangumi_id_when_tmdb_matched",
                        "metadata:write_bangumi_id_when_tmdb_matched"),
                    defaults.Metadata.WriteBangumiIdWhenTmdbMatched,
                    "write_bangumi_id_when_tmdb_matched"),
                MikanTrustedOffsetCacheEnabled = ParseOptionalBool(
                    FirstConfigurationValue(
                        configuration,
                        "mikan_trusted_offset_cache_enabled",
                        "metadata:mikan_trusted_offset_cache_enabled"),
                    defaults.Metadata.MikanTrustedOffsetCacheEnabled,
                    "mikan_trusted_offset_cache_enabled"),
                MikanTrustedOffsetRequiredEpisodes = ParseOptionalIntInRange(
                    FirstConfigurationValue(
                        configuration,
                        "mikan_trusted_offset_required_episodes",
                        "metadata:mikan_trusted_offset_required_episodes"),
                    defaults.Metadata.MikanTrustedOffsetRequiredEpisodes,
                    "mikan_trusted_offset_required_episodes",
                    1,
                    100),
            },
            Schedule = defaults.Schedule with
            {
                RefreshDatabaseCron = NormalizeOptional(
                    FirstConfigurationValue(
                        configuration,
                        "refresh_database_cron",
                        "schedule:refresh_database_cron"))
                    ?? defaults.Schedule.RefreshDatabaseCron,
            },
            DataUpdate = defaults.DataUpdate with
            {
                Enabled = ParseOptionalBool(
                    FirstConfigurationValue(
                        configuration,
                        "data_update_enabled",
                        "data_update:enabled"),
                    defaults.DataUpdate.Enabled,
                    "data_update_enabled"),
                Cron = NormalizeOptional(FirstConfigurationValue(
                        configuration,
                        "data_update_cron",
                        "data_update:cron"))
                    ?? defaults.DataUpdate.Cron,
                ManifestUrl = ParseOptionalAbsoluteUri(
                    FirstConfigurationValue(
                        configuration,
                        "data_update_manifest_url",
                        "data_update:manifest_url"),
                    "data_update_manifest_url"),
                AutoDownload = ParseOptionalBool(
                    FirstConfigurationValue(
                        configuration,
                        "data_update_auto_download",
                        "data_update:auto_download"),
                    defaults.DataUpdate.AutoDownload,
                    "data_update_auto_download"),
                AutoImport = ParseOptionalBool(
                    FirstConfigurationValue(
                        configuration,
                        "data_update_auto_import",
                        "data_update:auto_import"),
                    defaults.DataUpdate.AutoImport,
                    "data_update_auto_import"),
                KeepVersions = ParseOptionalInt(
                    FirstConfigurationValue(
                        configuration,
                        "data_update_keep_versions",
                        "data_update:keep_versions"),
                    defaults.DataUpdate.KeepVersions,
                    "data_update_keep_versions"),
                HttpTimeout = TimeSpan.FromSeconds(ParseOptionalDouble(
                    FirstConfigurationValue(
                        configuration,
                        "data_update_timeout_second",
                        "data_update:timeout_seconds"),
                    defaults.DataUpdate.HttpTimeout.TotalSeconds,
                    "data_update_timeout_second")),
            },
            TorrentFetch = defaults.TorrentFetch with
            {
                Timeout = TimeSpan.FromSeconds(ParseOptionalDouble(
                    FirstConfigurationValue(
                        configuration,
                        "torrent_http_timeout_seconds",
                        "torrent_fetch:timeout_seconds"),
                    defaults.TorrentFetch.Timeout.TotalSeconds,
                    "torrent_http_timeout_seconds")),
                MaxResponseBytes = ParseOptionalLong(
                    FirstConfigurationValue(
                        configuration,
                        "torrent_max_response_bytes",
                        "torrent_fetch:max_response_bytes"),
                    defaults.TorrentFetch.MaxResponseBytes,
                    "torrent_max_response_bytes"),
                MaxRedirects = ParseOptionalInt(
                    FirstConfigurationValue(
                        configuration,
                        "torrent_max_redirects",
                        "torrent_fetch:max_redirects"),
                    defaults.TorrentFetch.MaxRedirects,
                    "torrent_max_redirects"),
                StagingTtl = TimeSpan.FromSeconds(ParseOptionalDouble(
                    FirstConfigurationValue(
                        configuration,
                        "torrent_staging_ttl_seconds",
                        "torrent_fetch:staging_ttl_seconds"),
                    defaults.TorrentFetch.StagingTtl.TotalSeconds,
                    "torrent_staging_ttl_seconds")),
            },
            Downloaders = downloaders,
            InitialSourceProfiles = sources,
        };
    }

    private static void ConfigureWebBinding(
        WebApplicationBuilder builder,
        WebBindingOptions web)
    {
        if (!string.IsNullOrWhiteSpace(FirstConfigurationValue(
                builder.Configuration,
                "urls",
                "ASPNETCORE_URLS")))
        {
            return;
        }

        var host = Uri.CheckHostName(web.Host) == UriHostNameType.IPv6
            ? $"[{web.Host}]"
            : web.Host;
        builder.WebHost.UseUrls($"http://{host}:{web.Port}");
    }

    private static AnimeGoOptions ApplyBootstrapPaths(
        AnimeGoOptions defaults,
        ConfigurationManager configuration)
    {
        var dataPath = ResolveConfiguredPath(
            FirstConfigurationValue(
                configuration,
                "ANIMEGO_DATA_PATH",
                "data_path")
            ?? defaults.Paths.DataPath);
        var downloadPath = ResolveConfiguredPath(
            FirstConfigurationValue(
                configuration,
                "ANIMEGO_DOWNLOAD_PATH",
                "download_path")
            ?? defaults.Paths.DownloadPath);
        var savePath = ResolveConfiguredPath(
            FirstConfigurationValue(
                configuration,
                "ANIMEGO_SAVE_PATH",
                "save_path")
            ?? defaults.Paths.SavePath);
        var movieSavePath = ResolveConfiguredPath(
            FirstConfigurationValue(
                configuration,
                "ANIMEGO_MOVIE_SAVE_PATH",
                "movie_save_path")
            ?? defaults.Paths.MovieSavePath);
        return defaults with
        {
            Paths = new PathOptions
            {
                DataPath = dataPath,
                DownloadPath = downloadPath,
                SavePath = savePath,
                MovieSavePath = movieSavePath,
            },
            Downloaders = defaults.Downloaders.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with
                {
                    DownloadPath = PathBoundary.Combine(downloadPath, pair.Key),
                },
                StringComparer.OrdinalIgnoreCase),
        };
    }

    private static Dictionary<string, QbittorrentInstanceOptions> LoadDownloaders(
        ConfigurationManager configuration,
        AnimeGoOptions defaults,
        string globalDownloadPath)
    {
        var children = configuration.GetSection("downloaders").GetChildren().ToArray();
        if (children.Length == 0)
        {
            return defaults.Downloaders.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with
                {
                    DownloadPath = PathBoundary.Combine(globalDownloadPath, pair.Key),
                },
                StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, QbittorrentInstanceOptions>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var child in children.OrderBy(section => section.Key, StringComparer.Ordinal))
        {
            var id = child.Key.Trim().ToLowerInvariant();
            if (id.Length == 0)
            {
                throw new InvalidOperationException("Downloader id must not be empty.");
            }

            var baseUrlText = id == "bt"
                ? FirstConfigurationValue(
                    configuration,
                    "ANIMEGO_CLIENT_URL",
                    $"downloaders:{id}:base_url")
                : FirstConfigurationValue(
                    configuration,
                    $"downloaders:{id}:base_url");
            var baseUrl = ParseOptionalAbsoluteUri(
                baseUrlText,
                $"downloaders:{id}:base_url")
                ?? throw new InvalidOperationException(
                    $"downloaders:{id}:base_url is required.");
            var configuredDownloadPath = id == "bt"
                ? FirstConfigurationValue(
                    configuration,
                    "ANIMEGO_CLIENT_DOWNLOAD_PATH",
                    $"downloaders:{id}:download_path")
                : FirstConfigurationValue(
                    configuration,
                    $"downloaders:{id}:download_path");
            result.Add(id, new QbittorrentInstanceOptions
            {
                Type = NormalizeOptional(FirstConfigurationValue(
                        configuration,
                        $"downloaders:{id}:type"))
                    ?? DownloaderTypes.Qbittorrent,
                BaseUrl = baseUrl,
                Username = NormalizeOptional(id == "bt"
                    ? FirstConfigurationValue(
                        configuration,
                        "ANIMEGO_CLIENT_USERNAME",
                        $"downloaders:{id}:username")
                    : FirstConfigurationValue(
                        configuration,
                        $"downloaders:{id}:username")),
                Password = NormalizeOptional(id == "bt"
                    ? FirstConfigurationValue(
                        configuration,
                        "ANIMEGO_CLIENT_PASSWORD",
                        $"downloaders:{id}:password")
                    : FirstConfigurationValue(
                        configuration,
                        $"downloaders:{id}:password")),
                DownloadPath = ResolveConfiguredPath(
                    configuredDownloadPath
                    ?? PathBoundary.Combine(globalDownloadPath, id)),
                Enabled = ParseOptionalBool(
                    FirstConfigurationValue(
                        configuration,
                        $"downloaders:{id}:enabled"),
                    true,
                    $"downloaders:{id}:enabled"),
            });
        }

        return result;
    }

    private static List<SourceProfileSeed> LoadSourceProfiles(
        ConfigurationManager configuration,
        AnimeGoOptions defaults)
    {
        var children = configuration.GetSection("sources").GetChildren().ToArray();
        if (children.Length == 0)
        {
            return defaults.InitialSourceProfiles.ToList();
        }

        var result = new List<SourceProfileSeed>(children.Length);
        foreach (var child in children.OrderBy(section => section.Key, StringComparer.Ordinal))
        {
            var id = child.Key.Trim().ToLowerInvariant();
            var adapter = NormalizeOptional(
                FirstConfigurationValue(child, "adapter")) ?? id;
            var strategyText = NormalizeOptional(
                FirstConfigurationValue(child, "file_strategy")) ?? "move";
            result.Add(new SourceProfileSeed
            {
                Id = id,
                DisplayName = NormalizeOptional(
                    FirstConfigurationValue(child, "display_name")) ?? id,
                Adapter = adapter,
                MediaType = NormalizeOptional(
                    FirstConfigurationValue(child, "media_type"))
                    ?? MediaTypes.Tv,
                DownloaderId = NormalizeOptional(
                    FirstConfigurationValue(child, "downloader_id"))
                    ?? throw new InvalidOperationException(
                        $"sources:{id}:downloader_id is required."),
                FileStrategy = strategyText.ToLowerInvariant() switch
                {
                    "link" => FileStrategy.Link,
                    "link_delete" => FileStrategy.LinkDelete,
                    "move" => FileStrategy.Move,
                    "wait_move" => FileStrategy.WaitMove,
                    _ => throw new InvalidOperationException(
                        $"sources:{id}:file_strategy is unsupported."),
                },
                AllowedTorrentHosts = ReadScalarList(
                    child.GetSection("allowed_torrent_hosts")),
                Category = NormalizeOptional(id == "mikan"
                    ? FirstConfigurationValue(
                        configuration,
                        "ANIMEGO_CATEGORY",
                        $"sources:{id}:category")
                    : FirstConfigurationValue(
                        configuration,
                        $"sources:{id}:category"))
                    ?? "animegonet",
                Tags = ReadScalarList(child.GetSection("tags")),
                DynamicTagTemplate = DownloadDynamicTagTemplate.Normalize(
                    id == "mikan"
                        ? FirstPresentConfigurationValue(
                            configuration,
                            "ANIMEGO_TAG",
                            "sources:mikan:dynamic_tag_template")
                        : FirstConfigurationValue(child, "dynamic_tag_template")),
                SeedingTimeMinutes = ParseOptionalInt(
                    FirstConfigurationValue(child, "seeding_time_minutes"),
                    0,
                    $"sources:{id}:seeding_time_minutes"),
                RssFilterEnabled = ParseOptionalBool(
                    FirstConfigurationValue(child, "rss_filter_enabled"),
                    id == "mikan",
                    $"sources:{id}:rss_filter_enabled"),
                RssPriorityEnabled = ParseOptionalBool(
                    FirstConfigurationValue(child, "rss_priority_enabled"),
                    id == "mikan",
                    $"sources:{id}:rss_priority_enabled"),
                DuplicateNotificationEnabled = ParseOptionalBool(
                    FirstConfigurationValue(child, "duplicate_notification_enabled"),
                    true,
                    $"sources:{id}:duplicate_notification_enabled"),
                RssFeedUrl = SourceRssSchedulePolicy.NormalizeFeedUrl(
                    adapter,
                    FirstConfigurationValue(child, "rss_feed_url")),
                RssScheduleEnabled = ParseOptionalBool(
                    FirstConfigurationValue(child, "rss_schedule_enabled"),
                    false,
                    $"sources:{id}:rss_schedule_enabled"),
                RssScheduleCron = SourceRssSchedulePolicy.NormalizeCron(
                    FirstConfigurationValue(child, "rss_schedule_cron")),
                MikanIdentityCookie = string.Equals(
                    adapter,
                    "mikan",
                    StringComparison.OrdinalIgnoreCase)
                    ? MikanIdentityCookie.NormalizeOptional(
                        id == "mikan"
                            ? FirstPresentConfigurationValue(
                                configuration,
                                "ANIMEGO_MIKAN_COOKIE",
                                "mikan_cookie",
                                "sources:mikan:mikan_identity_cookie")
                            : FirstConfigurationValue(
                                child,
                                "mikan_identity_cookie"))
                    : null,
            });
        }

        return result;
    }

    private static string[] ReadScalarList(IConfigurationSection section) =>
        section.GetChildren()
            .OrderBy(child =>
                int.TryParse(
                    child.Key,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var index)
                    ? index
                    : int.MaxValue)
            .Select(child => NormalizeOptional(child.Value))
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

    private static string[] LoadOutboundProxyHosts(ConfigurationManager configuration)
    {
        var scalar = NormalizeOptional(FirstConfigurationValue(
            configuration,
            "outbound_proxy_hosts",
            "ANIMEGO_OUTBOUND_PROXY_HOSTS"));
        var values = scalar is null
            ? ReadScalarList(configuration.GetSection("outbound_proxy:hosts"))
            : scalar.Split(
                [',', ';', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values
            .Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? FirstConfigurationValue(
        IConfiguration configuration,
        params string[] keys) =>
        ConfigurationAliasResolver.FirstNonEmpty(configuration, keys);

    private static string? FirstPresentConfigurationValue(
        ConfigurationManager configuration,
        params string[] keys) =>
        ConfigurationAliasResolver.FirstPresent(configuration, keys);

    private static string ResolveConfiguredPath(string value) =>
        Path.GetFullPath(
            Path.IsPathRooted(value)
                ? value
                : Path.Combine(AppContext.BaseDirectory, value));

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

    private static int ParseOptionalIntInRange(
        string? value,
        int defaultValue,
        string name,
        int minimum,
        int maximum)
    {
        var parsed = ParseOptionalInt(value, defaultValue, name);
        return parsed >= minimum && parsed <= maximum
            ? parsed
            : throw new InvalidOperationException(
                $"{name} must be between {minimum} and {maximum}.");
    }

    private static long ParseOptionalLong(string? value, long defaultValue, string name)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return defaultValue;
        }

        if (!long.TryParse(
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

    private static string? ParseOptionalReasoningEffort(string? value)
    {
        var normalized = NormalizeOptional(value)?.ToLowerInvariant();
        return normalized switch
        {
            null or "none" => null,
            "low" or "medium" or "high" => normalized,
            _ => throw new InvalidOperationException(
                "ai_reasoning_effort must be none, low, medium or high."),
        };
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
        var values = ConfigurationAliasResolver.HighestPriorityValues(
            configuration,
            "ai_use_metadata_match",
            "metadata:ai:use_metadata_match",
            "ai_use_season_match",
            "ai_use_episode_match");
        if (values.TryGetValue("ai_use_metadata_match", out var canonical)
            || values.TryGetValue("metadata:ai:use_metadata_match", out canonical))
        {
            return ParseOptionalBool(
                canonical,
                defaultValue,
                "ai_use_metadata_match");
        }

        var legacySeason = ParseOptionalBool(
            values.GetValueOrDefault("ai_use_season_match"),
            false,
            "ai_use_season_match");
        var legacyEpisode = ParseOptionalBool(
            values.GetValueOrDefault("ai_use_episode_match"),
            false,
            "ai_use_episode_match");
        return values.Count == 0
            ? defaultValue
            : legacySeason || legacyEpisode;
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

    private static bool IsPluginApiPath(PathString path) =>
        path.StartsWithSegments("/api/plugin")
        || path.StartsWithSegments("/api/rss")
        || path.StartsWithSegments("/api/download/manager")
        || path.Equals("/api/v1/ingest");

    private static bool IsU2PluginApiPath(PathString path) =>
        path.Equals("/api/v1/plugins/inner_plugin_u2/ingest");

    private static bool HasValidPluginAccessKey(
        HttpRequest request,
        string configuredKey) =>
        HasValidAccessKey(
            request,
            configuredKey,
            "X-AnimeGo-Access-Key",
            "Access-Key",
            "access_key");

    private static bool HasValidWebUiAccessKey(
        HttpRequest request,
        string configuredKey) =>
        HasValidAccessKey(
            request,
            configuredKey,
            "X-AnimeGo-WebUI-Access-Key",
            "WebUI-Access-Key",
            "webui_access_key");

    private static bool HasValidAccessKey(
        HttpRequest request,
        string configuredKey,
        string directHeaderName,
        string hashedHeaderName,
        string queryName)
    {
        if (request.Headers.TryGetValue(directHeaderName, out var directKey)
            && FixedTimeEquals(directKey.ToString(), configuredKey))
        {
            return true;
        }

        var suppliedHash = request.Query[queryName].ToString();
        if (string.IsNullOrWhiteSpace(suppliedHash))
        {
            suppliedHash = request.Headers[hashedHeaderName].ToString();
        }

        var expectedHash = StableHash.Sha256LowerHex(configuredKey);
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
