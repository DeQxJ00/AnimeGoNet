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
        return cached?.Subject
            ?? await subjects
                .GetSubjectAsync(subjectId, cancellationToken)
                .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<BangumiSubjectRelation>>
        GetRelatedSubjectsAsync(
            int subjectId,
            CancellationToken cancellationToken = default) =>
        subjects.GetRelatedSubjectsAsync(subjectId, cancellationToken);

    public async Task<IReadOnlyList<BangumiEpisode>> GetEpisodesAsync(
        int subjectId,
        CancellationToken cancellationToken = default)
    {
        var cached = await archive
            .GetAsync(subjectId, cancellationToken)
            .ConfigureAwait(false);
        if (cached is { HasCompleteEpisodeSet: true })
        {
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
