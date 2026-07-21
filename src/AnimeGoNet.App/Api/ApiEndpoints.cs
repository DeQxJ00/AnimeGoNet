using System.Reflection;
using System.Runtime.CompilerServices;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Deletion;
using AnimeGoNet.Data.Mikan;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Rules;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.App.Api;

public static class ApiEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/ping", Ping);
        app.MapGet("/sha256", Sha256);
        app.MapGet("/api/v1/status", Status);
        app.MapGet("/api/v1/downloads", Downloads);
        app.MapGet("/api/v1/rss-rules/{sourceProfileId}", GetRssRules);
        app.MapPut("/api/v1/rss-rules/{sourceProfileId}", PutRssRules);
        app.MapPost("/api/v1/rss-rules/{sourceProfileId}/preview", PreviewRssRules);
        app.MapGet("/api/v1/delete/tasks/{taskId}/preview", DeletePreview);
        app.MapPost("/api/v1/delete/tasks/{taskId}", CreateDeleteExecution);
        app.MapGet("/api/v1/delete/executions/{executionId}", DeleteExecutionStatus);
        app.MapGet("/api/v1/mikan/work-rules/{mikanId:int}", GetMikanWorkRule);
        app.MapPut("/api/v1/mikan/work-rules/{mikanId:int}", PutMikanWorkRule);
        app.MapDelete("/api/v1/mikan/work-rules/{mikanId:int}", DeleteMikanWorkRule);
        app.MapPost("/api/v1/metadata/tasks/{taskId}/retry", RetryMetadataTask);
        app.MapGet("/api/v1/metadata/tasks", MetadataTasks);
        app.MapPost("/api/v1/ingest", Ingest);
        app.MapPost("/api/download/manager", LegacyDownloadManager);
    }

    private static Ok<LegacyApiResponse<PingData>> Ping()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        return TypedResults.Ok(new LegacyApiResponse<PingData>(
            200,
            "pong",
            new PingData(version, DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
    }

    private static Ok<LegacyApiResponse<string>> Sha256(string accessKey)
    {
        var hash = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(accessKey)));
        return TypedResults.Ok(new LegacyApiResponse<string>(200, "Access-Key", hash));
    }

    private static Ok<RuntimeStatus> Status(AnimeGoOptions options)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        return TypedResults.Ok(new RuntimeStatus(
            version,
            DatabaseSchema.CurrentVersion,
            !RuntimeFeature.IsDynamicCodeSupported,
            System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            new RuntimePaths(
                options.Paths.DataPath,
                options.Paths.DownloadPath,
                options.Paths.SavePath),
            new RuntimeCapabilities(
                Configuration: true,
                Sqlite: true,
                UnifiedIngest: true,
                RssRules: true,
                Qbittorrent: true,
                Tmdb: !string.IsNullOrWhiteSpace(options.Metadata.Tmdb.ApiKey)
                    || !string.IsNullOrWhiteSpace(options.Metadata.Tmdb.ReadAccessToken),
                Organizer: true,
                Deletion: true)));
    }

    private static async Task<Ok<DownloadListResponse>> Downloads(
        DownloadJobStore jobs,
        CancellationToken cancellationToken)
    {
        var records = await jobs.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new DownloadListResponse(records.Select(record => new DownloadListItem(
            record.JobId,
            record.TaskId,
            record.Title,
            record.SourceId,
            record.DownloaderId,
            record.InfoHash,
            record.State,
            record.BusinessStatus,
            record.Progress,
            record.DownloadedBytes,
            record.TotalBytes,
            record.SpeedBytesPerSecond,
            record.EtaSeconds,
            record.Seeds,
            record.Peers,
            record.IsStale,
            record.Revision,
            record.SnapshotAtUtc,
            record.UpdatedAtUtc,
            record.DownloaderConnected,
            record.DownloaderFailureCode,
            record.DownloaderLastSuccessAtUtc)).ToArray()));
    }

    private static async Task<IResult> GetRssRules(
        string sourceProfileId,
        SourceProfileStore profiles,
        MikanRssRuleStore rules,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.GetEnabledAsync(sourceProfileId.Trim().ToLowerInvariant(), cancellationToken)
            .ConfigureAwait(false);
        var snapshot = await rules.GetAsync(sourceProfileId, cancellationToken).ConfigureAwait(false);
        return profile is null || snapshot is null
            ? TypedResults.NotFound(Error("rss_rule_set_not_found", "RSS rule set was not found."))
            : TypedResults.Ok(ToResponse(profile, snapshot));
    }

    private static async Task<IResult> PutRssRules(
        string sourceProfileId,
        RssRuleSetRequest request,
        SourceProfileStore profiles,
        MikanRssRuleStore rules,
        CancellationToken cancellationToken)
    {
        try
        {
            var profileId = sourceProfileId.Trim().ToLowerInvariant();
            var profile = await profiles.GetEnabledAsync(profileId, cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                return TypedResults.NotFound(Error("rss_rule_set_not_found", "RSS source profile was not found."));
            }

            var saved = await rules.SaveAsync(
                profileId, ToRuleSet(request), request.ExpectedRevision,
                DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ToResponse(profile, saved));
        }
        catch (MikanRssRuleRevisionException)
        {
            return TypedResults.Conflict(Error(
                "rss_rule_revision_conflict", "RSS rules changed; reload before saving."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("rss_rule_set_invalid", exception.Message));
        }
    }

    private static async Task<IResult> PreviewRssRules(
        string sourceProfileId,
        RssRulePreviewRequest request,
        SourceProfileStore profiles,
        MikanRssRuleStore rules,
        CancellationToken cancellationToken)
    {
        try
        {
            var profileId = sourceProfileId.Trim().ToLowerInvariant();
            var profile = await profiles.GetEnabledAsync(profileId, cancellationToken).ConfigureAwait(false);
            var snapshot = await rules.GetAsync(profileId, cancellationToken).ConfigureAwait(false);
            if (profile is null || snapshot is null)
            {
                return TypedResults.NotFound(Error("rss_rule_set_not_found", "RSS rule set was not found."));
            }

            var candidates = (request.Candidates ?? []).Select((candidate, index) =>
            {
                if (candidate is null
                    || string.IsNullOrWhiteSpace(candidate.Id)
                    || string.IsNullOrWhiteSpace(candidate.Title))
                {
                    throw new ArgumentException($"Candidate {index} requires id and title.");
                }

                return new MikanRssCandidate(
                    candidate.Id.Trim(), candidate.Title, candidate.MikanId,
                    candidate.SourceEpisodeKind, candidate.SourceEpisode);
            }).ToArray();
            if (candidates.Select(candidate => candidate.Id).Distinct(StringComparer.Ordinal).Count() != candidates.Length)
            {
                throw new ArgumentException("Candidate ids must be unique.");
            }

            var decisions = profile.RssPriorityEnabled
                ? MikanRssRuleEngine.Evaluate(candidates, snapshot.Rules)
                : candidates.Select(candidate => new MikanRssDecision(
                    candidate.Id, MikanRssDecisionKind.Winner, "SkippedByConfiguration",
                    candidate.Id, [])).ToArray();
            return TypedResults.Ok(new RssRulePreviewResponse(
                profile.Id, snapshot.Revision, profile.RssPriorityEnabled,
                decisions.Select(decision => new RssRuleDecisionResponse(
                    decision.CandidateId, ToApiValue(decision.Kind), decision.Reason,
                    decision.WinnerId, decision.EvaluatedPriorityGroups)).ToArray()));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("rss_rule_preview_invalid", exception.Message));
        }
    }

    private static async Task<IResult> DeletePreview(
        string taskId,
        DeletePlanStore plans,
        CancellationToken cancellationToken)
    {
        var preview = await plans.GetPreviewAsync(taskId, cancellationToken).ConfigureAwait(false);
        return preview is null
            ? TypedResults.NotFound(Error("delete_task_not_found", "Delete task was not found."))
            : TypedResults.Ok(new DeletePreviewResponse(
                preview.TaskId, preview.TaskTitle, preview.TaskStatus, preview.Fingerprint,
                preview.BusinessRecords.Select(ToResponse).ToArray(),
                preview.DownloaderTasks.Select(ToResponse).ToArray(),
                preview.SourceFiles.Select(ToResponse).ToArray(),
                preview.MediaFiles.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> CreateDeleteExecution(
        string taskId,
        CreateDeleteExecutionRequest request,
        DeletePlanStore plans,
        CancellationToken cancellationToken)
    {
        try
        {
            var plan = await plans.CreateAsync(
                taskId,
                request.Fingerprint ?? string.Empty,
                new DeleteSelection(
                    request.DeleteBusinessRecord,
                    request.DeleteDownloaderTask,
                    request.DeleteSourceFiles,
                    request.DeleteMediaFiles),
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Accepted(
                $"/api/v1/delete/executions/{plan.ExecutionId}",
                new CreateDeleteExecutionResponse(
                    plan.ExecutionId, plan.TaskId, plan.State, plan.Targets.Count));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound(Error("delete_task_not_found", "Delete task was not found."));
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Conflict(Error("delete_preview_stale", exception.Message));
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            return TypedResults.Conflict(Error(
                "delete_execution_active", "This task already has an active delete execution."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("delete_request_invalid", exception.Message));
        }
    }

    private static async Task<IResult> DeleteExecutionStatus(
        string executionId,
        DeleteExecutionStore executions,
        CancellationToken cancellationToken)
    {
        var execution = await executions.GetAsync(executionId, cancellationToken).ConfigureAwait(false);
        return execution is null
            ? TypedResults.NotFound(Error("delete_execution_not_found", "Delete execution was not found."))
            : TypedResults.Ok(new DeleteExecutionStatusResponse(
                execution.ExecutionId, execution.TaskId, execution.State, execution.FailureReason,
                execution.AttemptCount, execution.CreatedAtUtc, execution.CompletedAtUtc,
                execution.Items.Select(item => new DeleteTargetResponse(
                    item.ItemKind, item.TargetKey, item.RootPath, item.DownloaderId,
                    item.DisplayValue, item.State)).ToArray()));
    }

    private static async Task<IResult> GetMikanWorkRule(
        int mikanId,
        MikanWorkMetadataRuleStore rules,
        CancellationToken cancellationToken)
    {
        if (mikanId <= 0)
        {
            return TypedResults.BadRequest(Error("mikanid_invalid", "mikanid must be a positive integer."));
        }

        var rule = await rules.GetAsync(mikanId, cancellationToken).ConfigureAwait(false);
        return rule is null
            ? TypedResults.NotFound(Error("mikan_rule_not_found", "Mikan work metadata rule was not found."))
            : TypedResults.Ok(ToResponse(rule));
    }

    private static async Task<IResult> PutMikanWorkRule(
        int mikanId,
        MikanWorkRuleRequest request,
        MikanWorkMetadataRuleStore rules,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await rules.SaveAsync(
                new MikanWorkMetadataRuleUpdate(
                    mikanId,
                    request.BangumiSubjectId,
                    request.TmdbSeriesId,
                    request.TmdbSeasonNumber,
                    request.EpisodeOffset,
                    request.Enabled),
                request.ExpectedRevision,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ToResponse(saved));
        }
        catch (MikanWorkMetadataRuleRevisionException)
        {
            return TypedResults.Conflict(Error(
                "mikan_rule_revision_conflict",
                "Mikan work metadata rule changed; reload it before saving."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("mikan_rule_invalid", exception.Message));
        }
    }

    private static async Task<IResult> DeleteMikanWorkRule(
        int mikanId,
        [FromQuery(Name = "expected_revision")] long expectedRevision,
        MikanWorkMetadataRuleStore rules,
        CancellationToken cancellationToken)
    {
        try
        {
            await rules.DeleteAsync(mikanId, expectedRevision, cancellationToken).ConfigureAwait(false);
            return TypedResults.NoContent();
        }
        catch (MikanWorkMetadataRuleRevisionException)
        {
            return TypedResults.Conflict(Error(
                "mikan_rule_revision_conflict",
                "Mikan work metadata rule changed; reload it before deleting."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(Error("mikan_rule_invalid", exception.Message));
        }
    }

    private static async Task<IResult> RetryMetadataTask(
        string taskId,
        MetadataResolutionStore resolutions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return TypedResults.BadRequest(Error("metadata_task_id_invalid", "Metadata task ID is required."));
        }

        var result = await resolutions.RetryFailedAsync(
            taskId,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        if (result == MetadataRetryResult.Retried)
        {
            var status = await resolutions.GetTaskStatusAsync(taskId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Retried metadata task disappeared.");
            return TypedResults.Ok(new MetadataRetryResponse(taskId, status));
        }

        return result switch
        {
            MetadataRetryResult.NotFound => TypedResults.NotFound(Error(
                "metadata_task_not_found",
                "Metadata task was not found.")),
            MetadataRetryResult.ActiveLease => TypedResults.Conflict(Error(
                "metadata_task_active",
                "Metadata task has an active resolution lease.")),
            _ => TypedResults.Conflict(Error(
                "metadata_task_not_failed",
                "Only failed metadata tasks can be retried.")),
        };
    }

    private static async Task<Ok<MetadataTaskListResponse>> MetadataTasks(
        MetadataResolutionStore resolutions,
        CancellationToken cancellationToken)
    {
        var items = await resolutions.ListTasksAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new MetadataTaskListResponse(items.Select(item => new MetadataTaskListItem(
            item.TaskId,
            item.Title,
            item.SourceId,
            item.Status,
            item.MikanId,
            item.BangumiSubjectId,
            item.TmdbSeriesId,
            item.TmdbSeasonNumber,
            item.SeriesStrategy,
            item.SeasonStrategy,
            item.EpisodeStrategy,
            item.FailureKind,
            item.FailureReason,
            item.EpisodeFileCount,
            item.OtherFileCount,
            item.DuplicateFileCount,
            item.PendingFileCount,
            item.UpdatedAtUtc)).ToArray()));
    }

    private static async Task<Ok<IngestBatchResponse>> Ingest(
        IngestBatchRequest request,
        SourceProfileStore profiles,
        IngestTaskStore tasks,
        ITorrentStagingService staging,
        AnimeGoOptions options,
        CancellationToken cancellationToken)
    {
        var response = await ProcessIngestAsync(
            request,
            profiles,
            tasks,
            staging,
            options.TorrentFetch.StagingTtl,
            requireModernMetadata: true,
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(response);
    }

    private static async Task<Ok<LegacyApiResponse<IngestBatchResponse?>>> LegacyDownloadManager(
        IngestBatchRequest request,
        SourceProfileStore profiles,
        IngestTaskStore tasks,
        ITorrentStagingService staging,
        AnimeGoOptions options,
        CancellationToken cancellationToken)
    {
        var legacyData = (request.Data ?? []).Select(item =>
        {
            if (item?.Info is null
                || !string.IsNullOrWhiteSpace(item.Info.Title)
                || !string.IsNullOrWhiteSpace(item.Info.Name))
            {
                return item;
            }

            return item with
            {
                Info = item.Info with { Title = item.Info.MikanUrl ?? item.Info.Url },
            };
        }).ToArray();
        var response = await ProcessIngestAsync(
            request with { Data = legacyData },
            profiles,
            tasks,
            staging,
            options.TorrentFetch.StagingTtl,
            requireModernMetadata: false,
            cancellationToken).ConfigureAwait(false);
        var success = response.RejectedCount == 0;
        var message = success
            ? $"开始处理{response.AcceptedCount}个下载项"
            : string.Join("; ", response.Items.SelectMany(item => item.Errors));
        return TypedResults.Ok(new LegacyApiResponse<IngestBatchResponse?>(
            success ? 200 : 300,
            message,
            response));
    }

    private static async Task<IngestBatchResponse> ProcessIngestAsync(
        IngestBatchRequest request,
        SourceProfileStore profiles,
        IngestTaskStore tasks,
        ITorrentStagingService staging,
        TimeSpan stagingTtl,
        bool requireModernMetadata,
        CancellationToken cancellationToken)
    {
        var data = request.Data ?? [];
        var responses = new List<IngestItemResponse>(data.Count);
        for (var index = 0; index < data.Count; index++)
        {
            if (data[index]?.Info is null)
            {
                responses.Add(Rejected(index, ["info is required"]));
                continue;
            }

            var command = ToCommand(data[index]!);
            var validation = IngestCommandNormalizer.Normalize(request.Source ?? string.Empty, command, requireModernMetadata);
            if (!validation.IsValid)
            {
                responses.Add(Rejected(index, validation.Errors));
                continue;
            }

            var normalized = validation.Item!;
            var profile = await profiles.GetEnabledAsync(normalized.Source, cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                responses.Add(Rejected(index, ["no enabled source profile is configured"]));
                continue;
            }

            StagedTorrent? staged = null;
            var ownershipTransferred = false;
            try
            {
                staged = await staging.StageAsync(
                    normalized.TorrentUrl,
                    new TorrentSourcePolicy(profile.Id, profile.AllowedTorrentHosts),
                    cancellationToken).ConfigureAwait(false);
                var task = await tasks.AddStagedAsync(
                    normalized,
                    profile,
                    staged.Metadata,
                    staged.StagingFileName,
                    DateTimeOffset.UtcNow + stagingTtl,
                    cancellationToken).ConfigureAwait(false);
                ownershipTransferred = true;
                responses.Add(new IngestItemResponse(
                    index,
                    task.Status,
                    task.Id,
                    task.SourceProfileId,
                    task.SourceProfileRevision,
                    task.DownloaderId,
                    normalized.TorrentUrlFingerprint,
                    task.InfoHash,
                    task.FileCount,
                    []));
            }
            catch (TorrentStagingException exception)
            {
                responses.Add(Rejected(index, [$"torrent staging failed: {exception.Code}"]));
            }
            finally
            {
                if (!ownershipTransferred && staged is not null)
                {
                    await staged.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        var accepted = responses.Count(item => item.IngestId is not null);
        return new IngestBatchResponse(
            (request.Source ?? string.Empty).Trim().ToLowerInvariant(),
            accepted,
            responses.Count - accepted,
            responses);
    }

    private static IngestItemCommand ToCommand(IngestItemRequest request) =>
        new(
            request.Torrent,
            new IngestItemInfo(
                request.Info!.Title,
                request.Info.Name,
                request.Info.SourceItemId,
                request.Info.SourceWorkId,
                request.Info.MikanUrl,
                request.Info.Url,
                request.Info.MikanId,
                request.Info.BangumiId,
                request.Info.AniDbId,
                request.Info.ImdbId));

    private static IngestItemResponse Rejected(int index, IReadOnlyList<string> errors) =>
        new(index, "rejected", null, null, null, null, null, null, null, errors);

    private static MikanWorkRuleResponse ToResponse(MikanWorkMetadataRule rule) =>
        new(
            rule.MikanId,
            rule.BangumiSubjectId,
            rule.TmdbSeriesId,
            rule.TmdbSeasonNumber,
            rule.EpisodeOffset,
            rule.Enabled,
            rule.Revision,
            rule.CreatedAtUtc,
            rule.UpdatedAtUtc);

    private static ApiErrorResponse Error(string code, string message) => new(code, message);

    private static DeleteTargetResponse ToResponse(DeletePlanTarget target) =>
        new(target.ItemKind, target.TargetKey, target.RootPath, target.DownloaderId, target.DisplayValue);

    private static MikanRssRuleSet ToRuleSet(RssRuleSetRequest request) => new(
        (request.Whitelist ?? []).Select((item, index) => ToArray(item, $"whitelist[{index}]")).ToArray(),
        (request.Blacklist ?? []).Select((item, index) => ToArray(item, $"blacklist[{index}]")).ToArray(),
        (request.PriorityGroups ?? []).Select((group, index) =>
        {
            if (group is null)
            {
                throw new ArgumentException($"priority_groups[{index}] is required.");
            }

            return new PriorityGroup(
                group.Id ?? string.Empty,
                group.Name ?? string.Empty,
                (group.Arrays ?? []).Select((item, arrayIndex) =>
                    ToArray(item, $"priority_groups[{index}].arrays[{arrayIndex}]")).ToArray());
        }).ToArray());

    private static NamedMatchArray ToArray(RssNamedArrayRequest? request, string path)
    {
        if (request is null)
        {
            throw new ArgumentException($"{path} is required.");
        }

        return new NamedMatchArray(
            request.Id ?? string.Empty,
            request.Name ?? string.Empty,
            request.Enabled,
            (request.Values ?? []).Select(value => value ?? string.Empty).ToArray());
    }

    private static RssRuleSetResponse ToResponse(
        SourceProfileRecord profile,
        MikanRssRuleSnapshot snapshot) =>
        new(
            snapshot.SourceProfileId, profile.RssFilterEnabled, profile.RssPriorityEnabled,
            snapshot.Revision,
            snapshot.Rules.Whitelist.Select(ToResponse).ToArray(),
            snapshot.Rules.Blacklist.Select(ToResponse).ToArray(),
            snapshot.Rules.PriorityGroups.Select(group => new RssPriorityGroupResponse(
                group.Id, group.Name, group.Arrays.Select(ToResponse).ToArray())).ToArray(),
            snapshot.CreatedAtUtc, snapshot.UpdatedAtUtc);

    private static RssNamedArrayResponse ToResponse(NamedMatchArray array) =>
        new(array.Id, array.Name, array.Enabled, array.Values);

    private static string ToApiValue(MikanRssDecisionKind value) => value switch
    {
        MikanRssDecisionKind.Winner => "winner",
        MikanRssDecisionKind.RejectedByBlacklist => "rejected_by_blacklist",
        MikanRssDecisionKind.RejectedByWhitelist => "rejected_by_whitelist",
        MikanRssDecisionKind.SuppressedByHigherPriority => "suppressed_by_higher_priority",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
