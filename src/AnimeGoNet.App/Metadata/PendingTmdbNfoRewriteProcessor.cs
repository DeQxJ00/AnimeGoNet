using AnimeGoNet.App.Library;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Metadata;

public enum PendingTmdbNfoRewriteResult
{
    NoWork,
    Completed,
    RetryScheduled,
}

public sealed class PendingTmdbNfoRewriteProcessor(
    PendingTmdbNfoRewriteStore store,
    TvShowNfoWriter writer)
{
    public async Task<PendingTmdbNfoRewriteResult> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var claim = await store.TryClaimNextAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5),
            cancellationToken).ConfigureAwait(false);
        if (claim is null)
        {
            return PendingTmdbNfoRewriteResult.NoWork;
        }

        try
        {
            await writer.WriteAsync(
                claim.SaveRootPath,
                claim.SeriesDirectoryName,
                claim.CanonicalSeriesName,
                claim.TmdbSeriesId,
                claim.BangumiSubjectId,
                cancellationToken).ConfigureAwait(false);
            await store.CompleteAsync(claim, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            return PendingTmdbNfoRewriteResult.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await store.FailAsync(
                claim,
                "nfo_rewrite_failed",
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(30),
                cancellationToken).ConfigureAwait(false);
            return PendingTmdbNfoRewriteResult.RetryScheduled;
        }
    }
}
