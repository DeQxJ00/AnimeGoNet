using AnimeGoNet.Data.Notifications;

namespace AnimeGoNet.App.Notifications;

public sealed class NotificationProcessor(
    NotificationStore store,
    WebhookNotificationSender sender,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var value = await store.ClaimNextEventAsync(
            _timeProvider.GetUtcNow(),
            TimeSpan.FromMinutes(1),
            cancellationToken).ConfigureAwait(false);
        if (value is null) return false;

        var channels = await store.ListChannelsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var channel in channels.Where(channel =>
                     channel.Enabled && channel.Events.Contains(value.EventType, StringComparer.Ordinal)))
        {
            var result = await sender.SendAsync(channel, value, cancellationToken).ConfigureAwait(false);
            await store.RecordDeliveryAsync(
                value, channel, result, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }

        await store.CompleteEventAsync(value.Id, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}

public sealed class NotificationWorker(NotificationProcessor processor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await processor.RunOnceAsync(stoppingToken).ConfigureAwait(false))
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
