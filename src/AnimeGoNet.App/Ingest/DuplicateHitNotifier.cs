using Microsoft.Extensions.Logging;

namespace AnimeGoNet.App.Ingest;

public sealed partial class DuplicateHitNotifier(ILogger<DuplicateHitNotifier> logger)
{
    public void Notify(
        bool enabled,
        string sourceProfileId,
        string sourceId,
        string scope,
        string reason)
    {
        if (!enabled)
        {
            return;
        }

        LogDuplicateHit(logger, sourceProfileId, sourceId, scope, reason);
    }

    [LoggerMessage(
        EventId = 4301,
        Level = LogLevel.Information,
        Message = "Duplicate media hit for source profile {SourceProfileId} ({SourceId}), scope {Scope}, reason {Reason}; download was skipped.")]
    private static partial void LogDuplicateHit(
        ILogger logger,
        string sourceProfileId,
        string sourceId,
        string scope,
        string reason);
}
