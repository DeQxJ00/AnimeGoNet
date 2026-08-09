using System.Globalization;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.App.Ingest;
using AnimeGoNet.App.Library;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.LocalIntegration.Tests;

public sealed class MikanLiveChainAuditTests
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task RealMikanCasesMatchExpectedMetadataAndProduceAnAuditReport()
    {
        Assert.Equal("1", Required("ANIMEGONET_MIKAN_LIVE_AUDIT"));
        var sourceCases = ReadCases(Path.GetFullPath(Required("ANIMEGONET_MIKAN_AUDIT_CSV")));
        Assert.Equal(29, sourceCases.Length);
        var startRow = int.Parse(
            Required("ANIMEGONET_MIKAN_AUDIT_START_ROW"),
            CultureInfo.InvariantCulture);
        var maximumCases = int.Parse(
            Required("ANIMEGONET_MIKAN_AUDIT_MAX_CASES"),
            CultureInfo.InvariantCulture);
        var cases = sourceCases
            .Where(value => value.RowNumber >= startRow)
            .Take(maximumCases)
            .ToArray();
        Assert.NotEmpty(cases);

        var auditRoot = Path.GetFullPath(Required("ANIMEGONET_MIKAN_AUDIT_OUTPUT"));
        var realDownload = string.Equals(
            Required("ANIMEGONET_MIKAN_REAL_DOWNLOAD"),
            "1",
            StringComparison.Ordinal);
        var downloadTimeout = TimeSpan.FromMinutes(int.Parse(
            Required("ANIMEGONET_MIKAN_DOWNLOAD_TIMEOUT_MINUTES"),
            CultureInfo.InvariantCulture));
        Directory.CreateDirectory(auditRoot);
        var runId = Guid.NewGuid().ToString("N");
        var runRoot = Path.Combine(auditRoot, $"run-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{runId[..8]}");
        var dataPath = Path.Combine(runRoot, "data");
        Directory.CreateDirectory(dataPath);
        var reportPath = Path.Combine(runRoot, "mikan-live-audit.json");
        var category = $"animegonet-mikan-audit-{runId}";
        var tag = $"animegonet-mikan-audit-{runId}";
        var options = CreateOptions(dataPath, category, tag);
        var qbit = options.Downloaders["bt"];
        using var adminHttp = CreateQbittorrentHttpClient(qbit);
        using var registry = new QbittorrentClientRegistry(options);
        var admin = new QbittorrentClient(adminHttp, qbit);
        var outcomes = new List<AuditCaseOutcome>(cases.Length);
        var createdHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        WebApplication? app = null;

        await admin.ConnectAsync();
        await PostFormAsync(adminHttp, "api/v2/torrents/createCategory", new Dictionary<string, string>
        {
            ["category"] = category,
            ["savePath"] = string.Empty,
        });
        await PostFormAsync(adminHttp, "api/v2/torrents/createTags", new Dictionary<string, string>
        {
            ["tags"] = tag,
        });

        try
        {
            app = await AnimeGoApplication.BuildAsync(
                [],
                options,
                runningInContainer: false,
                downloadClientRegistry: registry,
                startBackgroundWorkers: false);
            var ingest = app.Services.GetRequiredService<UnifiedIngestProcessor>();
            var dispatch = app.Services.GetRequiredService<StagedTorrentDispatcher>();
            var season = app.Services.GetRequiredService<AutomaticMetadataResolutionProcessor>();
            var episode = app.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>();
            var resolutions = app.Services.GetRequiredService<MetadataResolutionStore>();
            var preparations = app.Services.GetRequiredService<DownloadPreparationProcessor>();
            var snapshots = app.Services.GetRequiredService<DownloadSnapshotSynchronizer>();
            var organizer = app.Services.GetRequiredService<MediaOrganizationProcessor>();
            var database = app.Services.GetRequiredService<AnimeGoSqliteDatabase>();

            foreach (var testCase in cases)
            {
                string? taskId = null;
                string? infoHash = null;
                try
                {
                    var result = await ingest.ProcessAsync(
                        "mikan",
                        new IngestItemCommand(
                            testCase.TorrentUrl.AbsoluteUri,
                            new IngestItemInfo(
                                testCase.Title,
                                null,
                                $"mikan-live-{testCase.RowNumber}",
                                testCase.BangumiId.ToString(CultureInfo.InvariantCulture),
                                null,
                                null,
                                null,
                                testCase.BangumiId,
                                null,
                                null),
                            new IngestSourceEvidence(
                                testCase.PublishedAtRaw,
                                testCase.PublishedAt)),
                        requireModernMetadata: true);
                    if (!result.Accepted)
                    {
                        outcomes.Add(AuditCaseOutcome.Failed(
                            testCase,
                            "ingest_rejected",
                            string.Join("; ", result.Errors)));
                        await SaveReportAsync(reportPath, runId, outcomes);
                        continue;
                    }

                    taskId = result.IngestId;
                    infoHash = result.InfoHash;
                    createdHashes.Add(infoHash!);
                    var dispatchResult = await dispatch.DispatchNextAsync();
                    if (dispatchResult != StagedDispatchResult.Completed)
                    {
                        outcomes.Add(AuditCaseOutcome.Failed(
                            testCase,
                            "qbittorrent_dispatch_failed",
                            dispatchResult.ToString(),
                            result.TorrentUrlFingerprint,
                            taskId,
                            infoHash));
                        await SaveReportAsync(reportPath, runId, outcomes);
                        continue;
                    }

                    var paused = await WaitForPausedTaskAsync(admin, infoHash!, TimeSpan.FromSeconds(20));
                    Assert.Equal(AnimeGoNet.Core.Downloads.DownloadTaskState.Paused, paused.State);
                    Assert.True(await season.RunOnceAsync());
                    var seasonRun = await resolutions.GetLatestAsync(taskId!);
                    if (string.Equals(seasonRun?.Status, "season_resolved", StringComparison.Ordinal))
                    {
                        Assert.True(await episode.RunOnceAsync());
                    }

                    var detail = Assert.IsType<MetadataTaskDetailProjection>(
                        await resolutions.GetTaskDetailAsync(taskId!));
                    var attempts = await resolutions.ListAttemptsAsync(taskId!, 500);
                    var expectedEpisodes = ParseEpisodeSet(testCase.ExpectedEpisode);
                    var actualEpisodes = detail.Files
                        .Select(file => file.TmdbEpisodeNumber)
                        .Where(value => value is not null)
                        .Select(value => value!.Value)
                        .Distinct()
                        .Order()
                        .ToArray();
                    var passed = detail.Summary.TmdbSeriesId == testCase.ExpectedTmdbSeriesId
                        && detail.Summary.TmdbSeasonNumber == testCase.ExpectedSeason
                        && expectedEpisodes.SequenceEqual(actualEpisodes);
                    var downloadResult = "metadata_only";
                    IReadOnlyList<string> mediaRelativePaths = [];
                    if (passed && realDownload)
                    {
                        var preparation = await preparations.RunOnceAsync();
                        downloadResult = preparation.ToString();
                        if (preparation == DownloadPreparationResult.Completed)
                        {
                            await WaitForDownloadAsync(
                                admin,
                                snapshots,
                                infoHash!,
                                downloadTimeout);
                            downloadResult = "preparation=Completed;organization=" + await CompleteOrganizationAsync(
                                organizer,
                                resolutions,
                                taskId!);
                        }

                        mediaRelativePaths = await ReadMediaRelativePathsAsync(
                            database,
                            taskId!,
                            options.Paths.SavePath);
                    }

                    outcomes.Add(new AuditCaseOutcome(
                        testCase.RowNumber,
                        testCase.Title,
                        testCase.BangumiId,
                        result.TorrentUrlFingerprint,
                        taskId,
                        infoHash,
                        "completed",
                        null,
                        detail.Summary.Status,
                        testCase.ExpectedTmdbSeriesId,
                        testCase.ExpectedSeason,
                        expectedEpisodes,
                        detail.Summary.TmdbSeriesId,
                        detail.Summary.TmdbSeasonNumber,
                        actualEpisodes,
                        detail.Files.Select(file => new AuditFileOutcome(
                            file.RelativePath,
                            file.SizeBytes,
                            file.SourceEpisode,
                            file.FileEpisodeCandidate,
                            file.Disposition,
                            file.OtherReason,
                            file.TmdbSeriesId,
                            file.TmdbSeasonNumber,
                            file.TmdbEpisodeNumber)).ToArray(),
                        attempts.OrderBy(value => value.CreatedAtUtc).Select(ToAuditAttempt).ToArray(),
                        attempts.Any(value => value.Strategy == "ai_metadata"),
                        SumUsage(attempts),
                        realDownload,
                        downloadResult,
                        mediaRelativePaths,
                        passed,
                        testCase.Note));
                }
                catch (Exception exception)
                {
                    outcomes.Add(AuditCaseOutcome.Failed(
                        testCase,
                        Classify(exception),
                        exception.GetType().Name,
                        null,
                        taskId,
                        infoHash));
                }
                finally
                {
                    if (infoHash is not null)
                    {
                        await BestEffortDeleteAsync(admin, infoHash);
                        createdHashes.Remove(infoHash);
                    }

                    await SaveReportAsync(reportPath, runId, outcomes);
                }
            }
        }
        finally
        {
            if (app is not null)
            {
                await app.DisposeAsync();
            }

            foreach (var infoHash in createdHashes)
            {
                await BestEffortDeleteAsync(admin, infoHash);
            }

            await BestEffortPostFormAsync(adminHttp, "api/v2/torrents/removeCategories", new Dictionary<string, string>
            {
                ["categories"] = category,
            });
            await BestEffortPostFormAsync(adminHttp, "api/v2/torrents/deleteTags", new Dictionary<string, string>
            {
                ["tags"] = tag,
            });
            await SaveReportAsync(reportPath, runId, outcomes);
        }

        var failures = outcomes.Where(outcome => !outcome.Passed).ToArray();
        Assert.True(
            failures.Length == 0,
            $"{failures.Length}/{cases.Length} Mikan live cases failed. Audit report: {reportPath}. "
            + string.Join(" | ", failures.Select(value => $"row {value.RowNumber}: {value.FailureCode}")));
    }

    private static AnimeGoOptions CreateOptions(string dataPath, string category, string tag)
    {
        var defaults = AnimeGoDefaults.CreateNative(dataPath);
        var downloadPath = Path.GetFullPath(Required("ANIMEGONET_QBIT_DOWNLOAD_PATH"));
        var savePath = Path.GetFullPath(Required("ANIMEGONET_QBIT_SAVE_PATH"));
        var mikanBase = AbsoluteBaseUrl("ANIMEGONET_MIKAN_BASE_URL");
        var tmdbBase = AbsoluteBaseUrl("ANIMEGONET_TMDB_BASE_URL");
        var bangumiBase = AbsoluteBaseUrl("ANIMEGONET_BANGUMI_BASE_URL");
        var aiBase = AbsoluteBaseUrl("ANIMEGONET_AI_BASE_URL");
        var qbitBase = AbsoluteBaseUrl("ANIMEGONET_QBIT_BASE_URL");
        var proxyValue = Environment.GetEnvironmentVariable("ANIMEGONET_OUTBOUND_PROXY_URL");
        var proxy = string.IsNullOrWhiteSpace(proxyValue) ? null : new Uri(proxyValue);
        var options = defaults with
        {
            Paths = new PathOptions
            {
                DataPath = dataPath,
                DownloadPath = downloadPath,
                SavePath = savePath,
            },
            OutboundProxy = new OutboundProxyOptions
            {
                Url = proxy,
                HostPatterns = proxy is null ? [] : ["*"],
            },
            Downloaders = new Dictionary<string, QbittorrentInstanceOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["bt"] = new QbittorrentInstanceOptions
                {
                    BaseUrl = qbitBase,
                    Username = Required("ANIMEGONET_QBIT_USERNAME"),
                    Password = Required("ANIMEGONET_QBIT_PASSWORD"),
                    DownloadPath = downloadPath,
                },
            },
            Metadata = defaults.Metadata with
            {
                Mikan = defaults.Metadata.Mikan with { BaseUrl = mikanBase },
                Tmdb = defaults.Metadata.Tmdb with
                {
                    BaseUrl = tmdbBase,
                    ApiKey = Required("ANIMEGONET_TMDB_API_KEY"),
                    HttpTimeout = TimeSpan.FromSeconds(45),
                },
                Bangumi = defaults.Metadata.Bangumi with
                {
                    BaseUrl = bangumiBase,
                    HttpTimeout = TimeSpan.FromSeconds(45),
                },
                SeasonFailure = new SeasonFailureOptions
                {
                    Backtrace = true,
                    UseTitleSeason = true,
                    UseFirstSeason = true,
                },
                Ai = defaults.Metadata.Ai with
                {
                    BaseUrl = aiBase,
                    ApiKey = Required("ANIMEGONET_AI_API_KEY"),
                    Model = Required("ANIMEGONET_AI_MODEL"),
                    UseMetadataMatch = true,
                    HttpTimeout = TimeSpan.FromSeconds(600),
                    TmdbMcpUrl = AbsoluteUrl("ANIMEGONET_TMDB_MCP_URL"),
                    BangumiMcpUrl = AbsoluteUrl("ANIMEGONET_BANGUMI_MCP_URL"),
                },
                TmdbFailureUseBangumi = false,
                MikanTrustedOffsetCacheEnabled = false,
            },
            InitialSourceProfiles =
            [
                new SourceProfileSeed
                {
                    Id = "mikan",
                    Adapter = "mikan",
                    DownloaderId = "bt",
                    FileStrategy = FileStrategy.Move,
                    AllowedTorrentHosts = [mikanBase.IdnHost, "mikanani.me", "mikanime.tv"],
                    Category = category,
                    Tags = [tag],
                    SeedingTimeMinutes = 0,
                    RssFilterEnabled = true,
                    RssPriorityEnabled = true,
                },
            ],
        };
        Assert.Empty(AnimeGoOptionsValidator.Validate(options));
        return options;
    }

    private static HttpClient CreateQbittorrentHttpClient(QbittorrentInstanceOptions options)
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true })
        {
            BaseAddress = options.BaseUrl,
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.Referrer = options.BaseUrl;
        return client;
    }

    private static async Task<AnimeGoNet.Core.Downloads.DownloadTaskSnapshot> WaitForPausedTaskAsync(
        QbittorrentClient client,
        string infoHash,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await client.ConnectAsync();
            var task = (await client.ListAsync()).SingleOrDefault(value =>
                string.Equals(value.Hash, infoHash, StringComparison.OrdinalIgnoreCase));
            if (task?.State == AnimeGoNet.Core.Downloads.DownloadTaskState.Paused)
            {
                return task;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("qBittorrent did not expose the audit task in paused state.");
    }

    private static async Task BestEffortDeleteAsync(QbittorrentClient client, string infoHash)
    {
        try
        {
            await client.ConnectAsync();
            await client.DeleteAsync([infoHash], deleteFiles: false);
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }

    private static async Task WaitForDownloadAsync(
        QbittorrentClient client,
        DownloadSnapshotSynchronizer snapshots,
        string infoHash,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await client.ConnectAsync();
            var task = (await client.ListAsync()).SingleOrDefault(value =>
                string.Equals(value.Hash, infoHash, StringComparison.OrdinalIgnoreCase));
            await snapshots.SyncOnceAsync();
            if (task is not null && task.Progress >= 1)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new TimeoutException("The real Mikan payload did not finish before the configured audit timeout.");
    }

    private static async Task<string> CompleteOrganizationAsync(
        MediaOrganizationProcessor organizer,
        MetadataResolutionStore resolutions,
        string taskId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var result = await organizer.RunOnceAsync();
            var status = await resolutions.GetTaskStatusAsync(taskId);
            if (string.Equals(status, "organized", StringComparison.Ordinal))
            {
                return result.ToString();
            }

            if (result == MediaOrganizationResult.RetryScheduled)
            {
                await Task.Delay(TimeSpan.FromSeconds(31));
            }
            else if (result == MediaOrganizationResult.NoWork)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("Media organization did not reach the organized state.");
    }

    private static async Task<IReadOnlyList<string>> ReadMediaRelativePathsAsync(
        AnimeGoSqliteDatabase database,
        string taskId,
        string savePath)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT completion.media_path
            FROM completion_records AS completion
            WHERE completion.source_id = (
                    SELECT source_profile_id FROM ingest_tasks WHERE id = $task_id)
              AND completion.source_item_id = (
                    SELECT source_item_id FROM ingest_tasks WHERE id = $task_id)
            ORDER BY completion.media_path;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var relative = Path.GetRelativePath(savePath, reader.GetString(0));
            Assert.False(
                Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal),
                "Organizer returned a media path outside the configured save root.");
            values.Add(relative);
        }

        return values;
    }

    private static async Task PostFormAsync(
        HttpClient client,
        string path,
        IReadOnlyDictionary<string, string> values)
    {
        using var response = await client.PostAsync(path, new FormUrlEncodedContent(values));
        response.EnsureSuccessStatusCode();
    }

    private static async Task BestEffortPostFormAsync(
        HttpClient client,
        string path,
        IReadOnlyDictionary<string, string> values)
    {
        try
        {
            await PostFormAsync(client, path, values);
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }

    private static AuditAttempt ToAuditAttempt(MetadataAttemptProjection value) => new(
        value.Stage,
        value.Strategy,
        value.Priority,
        value.Result,
        value.ErrorCode,
        value.Reason,
        value.Retryable,
        value.AttemptNumber,
        value.DurationMilliseconds,
        value.AiUsage);

    private static AiMetadataProviderUsage? SumUsage(IReadOnlyList<MetadataAttemptProjection> attempts)
    {
        var values = attempts.Where(value => value.AiUsage is not null).Select(value => value.AiUsage!).ToArray();
        if (values.Length == 0)
        {
            return null;
        }

        return new AiMetadataProviderUsage(
            string.Join(",", values.Select(value => value.Model).Distinct(StringComparer.Ordinal)),
            SumNullable(values.Select(value => value.PromptTokens)),
            SumNullable(values.Select(value => value.CompletionTokens)),
            SumNullable(values.Select(value => value.TotalTokens)),
            values.Sum(value => value.RequestCount),
            values.Sum(value => value.ToolCallCount));
    }

    private static long? SumNullable(IEnumerable<long?> values)
    {
        var materialized = values.ToArray();
        return materialized.Any(value => value is not null)
            ? materialized.Sum(value => value ?? 0)
            : null;
    }

    private static Task SaveReportAsync(
        string path,
        string runId,
        IReadOnlyList<AuditCaseOutcome> outcomes)
    {
        var report = new AuditReport(
            runId,
            DateTimeOffset.UtcNow,
            "real Mikan metadata, isolated qB dispatch, Bangumi/TMDB/optional AI, and per-case real-download audit",
            outcomes.Count,
            outcomes.Count(value => value.Passed),
            outcomes.Count(value => !value.Passed),
            outcomes.Count(value => value.AiUsed),
            new AiUsageSummary(
                outcomes.Sum(value => value.AiUsage?.PromptTokens ?? 0),
                outcomes.Sum(value => value.AiUsage?.CompletionTokens ?? 0),
                outcomes.Sum(value => value.AiUsage?.TotalTokens ?? 0),
                outcomes.Sum(value => value.AiUsage?.RequestCount ?? 0),
                outcomes.Sum(value => value.AiUsage?.ToolCallCount ?? 0)),
            outcomes);
        var json = JsonSerializer.Serialize(report, ReportJsonOptions);
        return WriteReportAtomicallyAsync(path, json);
    }

    private static async Task WriteReportAtomicallyAsync(string path, string json)
    {
        var temporary = path + ".partial";
        await File.WriteAllTextAsync(
            temporary,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }

    private static MikanAuditCase[] ReadCases(string path)
    {
        var rows = ParseCsv(File.ReadAllText(path, Encoding.UTF8));
        Assert.NotEmpty(rows);
        Assert.Equal(
            ["link", "title", "torrent发布日期", "bgmid", "tmdb预期id", "season预期序号", "ep预期序号", "说明"],
            rows[0]);
        return rows.Skip(1).Select((row, index) =>
        {
            Assert.Equal(8, row.Length);
            var local = DateTime.Parse(row[2], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces);
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            return new MikanAuditCase(
                index + 2,
                new Uri(row[0]),
                row[1],
                row[2],
                new DateTimeOffset(local, TimeSpan.FromHours(8)),
                int.Parse(row[3], CultureInfo.InvariantCulture),
                int.Parse(row[4], CultureInfo.InvariantCulture),
                int.Parse(row[5], CultureInfo.InvariantCulture),
                row[6],
                string.IsNullOrWhiteSpace(row[7]) ? null : row[7]);
        }).ToArray();
    }

    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var value = text[index];
            if (quoted)
            {
                if (value == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(value);
                }

                continue;
            }

            switch (value)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    if (row.Any(item => item.Length > 0))
                    {
                        rows.Add(row.ToArray());
                    }

                    row.Clear();
                    break;
                default:
                    field.Append(value);
                    break;
            }
        }

        Assert.False(quoted, "CSV ended inside a quoted field.");
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }

    private static int[] ParseEpisodeSet(string value)
    {
        var parts = value.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return [int.Parse(parts[0], CultureInfo.InvariantCulture)];
        }

        Assert.Equal(2, parts.Length);
        var first = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var last = int.Parse(parts[1], CultureInfo.InvariantCulture);
        Assert.True(last >= first);
        return Enumerable.Range(first, last - first + 1).ToArray();
    }

    private static Uri AbsoluteBaseUrl(string name)
    {
        var value = AbsoluteUrl(name);
        Assert.EndsWith("/", value.AbsolutePath, StringComparison.Ordinal);
        return value;
    }

    private static Uri AbsoluteUrl(string name)
    {
        var value = new Uri(Required(name));
        Assert.True(value.IsAbsoluteUri);
        Assert.True(value.Scheme is "http" or "https");
        Assert.True(string.IsNullOrEmpty(value.UserInfo));
        return value;
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Set {name} before running the explicit Mikan live audit.");

    private static string Classify(Exception exception) => exception switch
    {
        TimeoutException => "audit_timeout",
        HttpRequestException => "audit_http_error",
        TaskCanceledException => "audit_cancelled",
        _ => "audit_exception",
    };

    private sealed record MikanAuditCase(
        int RowNumber,
        Uri TorrentUrl,
        string Title,
        string PublishedAtRaw,
        DateTimeOffset PublishedAt,
        int BangumiId,
        int ExpectedTmdbSeriesId,
        int ExpectedSeason,
        string ExpectedEpisode,
        string? Note);

    private sealed record AuditReport(
        string RunId,
        DateTimeOffset UpdatedAtUtc,
        string Scope,
        int Processed,
        int Passed,
        int Failed,
        int AiCases,
        AiUsageSummary AiUsage,
        IReadOnlyList<AuditCaseOutcome> Cases);

    private sealed record AiUsageSummary(
        long PromptTokens,
        long CompletionTokens,
        long TotalTokens,
        int RequestCount,
        int ToolCallCount);

    private sealed record AuditCaseOutcome(
        int RowNumber,
        string Title,
        int BangumiId,
        string? TorrentUrlFingerprint,
        string? TaskId,
        string? InfoHash,
        string Stage,
        string? FailureCode,
        string? TaskStatus,
        int ExpectedTmdbSeriesId,
        int ExpectedSeason,
        IReadOnlyList<int> ExpectedEpisodes,
        int? ActualTmdbSeriesId,
        int? ActualSeason,
        IReadOnlyList<int> ActualEpisodes,
        IReadOnlyList<AuditFileOutcome> Files,
        IReadOnlyList<AuditAttempt> Attempts,
        bool AiUsed,
        AiMetadataProviderUsage? AiUsage,
        bool RealDownloadEnabled,
        string DownloadResult,
        IReadOnlyList<string> MediaRelativePaths,
        bool Passed,
        string? Note)
    {
        public static AuditCaseOutcome Failed(
            MikanAuditCase value,
            string code,
            string reason,
            string? fingerprint = null,
            string? taskId = null,
            string? infoHash = null) => new(
                value.RowNumber,
                value.Title,
                value.BangumiId,
                fingerprint,
                taskId,
                infoHash,
                "failed",
                code,
                reason,
                value.ExpectedTmdbSeriesId,
                value.ExpectedSeason,
                ParseEpisodeSet(value.ExpectedEpisode),
                null,
                null,
                [],
                [],
                [],
                false,
                null,
                false,
                "not_started",
                [],
                false,
                value.Note);
    }

    private sealed record AuditFileOutcome(
        string RelativePath,
        long SizeBytes,
        string? SourceEpisode,
        string? FileEpisodeCandidate,
        string Disposition,
        string? OtherReason,
        int? TmdbSeriesId,
        int? TmdbSeasonNumber,
        int? TmdbEpisodeNumber);

    private sealed record AuditAttempt(
        string Stage,
        string Strategy,
        int? Priority,
        string Result,
        string? ErrorCode,
        string? Reason,
        bool Retryable,
        int AttemptNumber,
        long DurationMilliseconds,
        AiMetadataProviderUsage? AiUsage);
}
