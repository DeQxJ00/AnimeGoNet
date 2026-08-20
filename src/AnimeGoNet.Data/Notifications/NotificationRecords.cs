namespace AnimeGoNet.Data.Notifications;

public static class NotificationProviders
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "bark", "generic", "discord", "slack", "telegram", "serverchan", "pushplus",
    };
}

public static class NotificationEventTypes
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "metadata_failed", "metadata_other", "download_failed", "download_completed",
        "organization_completed", "review_required", "test",
    };
}

public sealed record NotificationChannel(
    string Id,
    string Name,
    string Provider,
    bool Enabled,
    string EndpointUrl,
    string? Secret,
    string? Target,
    string OptionsJson,
    IReadOnlyList<string> Events,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record NotificationChannelWrite(
    string Name,
    string Provider,
    bool Enabled,
    string EndpointUrl,
    string? Secret,
    string? Target,
    string OptionsJson,
    IReadOnlyList<string> Events);

public sealed record NotificationEvent(
    string Id,
    string EventType,
    string? TaskId,
    string Title,
    string Body,
    string PayloadJson,
    DateTimeOffset CreatedAtUtc);

public sealed record NotificationDelivery(
    string Id,
    string? EventId,
    string? ChannelId,
    string ChannelName,
    string Provider,
    string EventType,
    string? TaskId,
    string Title,
    string State,
    int? HttpStatus,
    string? FailureCode,
    string? ResponseExcerpt,
    long DurationMilliseconds,
    DateTimeOffset CreatedAtUtc);

public sealed record NotificationSendResult(
    bool Succeeded,
    int? HttpStatus,
    string? FailureCode,
    string? ResponseExcerpt,
    long DurationMilliseconds);
