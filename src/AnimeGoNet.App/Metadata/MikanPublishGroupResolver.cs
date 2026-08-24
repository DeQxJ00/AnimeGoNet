using System.Globalization;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Data.Mikan;

namespace AnimeGoNet.App.Metadata;

public sealed class MikanPublishGroupResolver(
    IRssFeedHttpClient httpClient,
    MikanPublishGroupStore store,
    AnimeGoOptions options,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var candidate = await store.FindNextCandidateAsync(now, cancellationToken).ConfigureAwait(false);
        if (candidate is null) return false;
        var uri = new Uri(
            options.Metadata.Mikan.BaseUrl,
            $"Home/Bangumi/{candidate.MikanId.ToString(CultureInfo.InvariantCulture)}");
        try
        {
            var html = httpClient is ISourceProfileRssFeedHttpClient profileClient
                ? await profileClient.GetAsync(uri, candidate.SourceProfileId, cancellationToken).ConfigureAwait(false)
                : await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            var name = MikanSubgroupListParser.Parse(html)
                .FirstOrDefault(group => group.GroupId == candidate.GroupId)?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                await store.SaveFailureAsync(
                    candidate.GroupId,
                    candidate.SourceProfileId,
                    "mikan_publish_group_not_listed",
                    now,
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
            await store.SaveAutomaticAsync(
                candidate.GroupId, name, candidate.SourceProfileId, now, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MikanSubgroupListException exception)
        {
            await store.SaveFailureAsync(
                candidate.GroupId, candidate.SourceProfileId, exception.Code, now, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is RssFeedException or HttpRequestException)
        {
            await store.SaveFailureAsync(
                candidate.GroupId, candidate.SourceProfileId, "mikan_publish_group_fetch_failed", now, cancellationToken).ConfigureAwait(false);
        }
        return true;
    }
}

public sealed class MikanPublishGroupWorker(MikanPublishGroupResolver resolver) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await resolver.RunOnceAsync(stoppingToken).ConfigureAwait(false))
                {
                    await Task.Yield();
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // A failed row is retried from persisted state; keep the worker alive.
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }
}
