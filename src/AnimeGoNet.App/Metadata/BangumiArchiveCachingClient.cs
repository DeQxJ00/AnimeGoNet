using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed class BangumiArchiveCachingClient(
    BangumiArchiveStore archive,
    IBangumiSubjectClient subjects,
    IBangumiEpisodeClient episodes,
    bool ownsClients = false)
    : IBangumiSubjectClient, IBangumiEpisodeClient, IDisposable
{
    private int _disposed;

    public async Task<BangumiSubject?> GetSubjectAsync(
        int subjectId,
        CancellationToken cancellationToken = default)
    {
        var cached = await archive
            .GetAsync(subjectId, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null)
        {
            await archive.RecordSubjectHitAsync(
                cached.DataVersion,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return cached.Subject;
        }

        return await subjects
            .GetSubjectAsync(subjectId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BangumiSubjectRelation>>
        GetRelatedSubjectsAsync(
        int subjectId,
        CancellationToken cancellationToken = default)
    {
        var cached = await archive
            .GetRelatedSubjectsSnapshotAsync(subjectId, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null)
        {
            await archive.RecordRelationHitAsync(
                cached.DataVersion,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return cached.Relations;
        }

        return await subjects
            .GetRelatedSubjectsAsync(subjectId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BangumiEpisode>> GetEpisodesAsync(
        int subjectId,
        CancellationToken cancellationToken = default)
    {
        var cached = await archive
            .GetAsync(subjectId, cancellationToken)
            .ConfigureAwait(false);
        if (cached is { HasCompleteEpisodeSet: true })
        {
            await archive.RecordEpisodeHitAsync(
                cached.DataVersion,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return cached.Episodes;
        }

        return await episodes
            .GetEpisodesAsync(subjectId, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (!ownsClients || Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (ReferenceEquals(subjects, episodes))
        {
            (subjects as IDisposable)?.Dispose();
            return;
        }

        (subjects as IDisposable)?.Dispose();
        (episodes as IDisposable)?.Dispose();
    }
}
