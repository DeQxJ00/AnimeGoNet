using System.Reflection;
using System.Runtime.CompilerServices;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AnimeGoNet.App.Api;

public static class ApiEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/ping", Ping);
        app.MapGet("/sha256", Sha256);
        app.MapGet("/api/v1/status", Status);
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
                Tmdb: false,
                Organizer: false)));
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
}
