using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Library;

public sealed record SubtitleArchiveAiMatchResult(
    IReadOnlyList<SubtitleArchiveAssignment> Assignments,
    string? Reason,
    AiMetadataProviderUsage? Usage);

public sealed class SubtitleArchiveAiMatchService(
    IAiMetadataMatcher matcher,
    ITmdbClient tmdb,
    SubtitleAiPromptStore prompts)
{
    public async Task<SubtitleArchiveAiMatchResult> MatchAsync(
        SubtitleArchiveImportSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var files = session.Candidates
            .Select(value => new AiMetadataFileInput(value.RelativePath, value.SizeBytes))
            .ToArray();
        var input = new AiMetadataMatchInput(
            $"{session.SeriesName}; confirmed_tmdb_series_id={session.TmdbSeriesId}; confirmed_tmdb_season={session.SeasonNumber}",
            files,
            null,
            null,
            null,
            files.Length,
            null,
            null,
            false)
        {
            PromptTemplateOverride = await prompts.GetTemplateAsync(cancellationToken).ConfigureAwait(false),
            PromptFeaturesOverride = new AiMetadataPromptFeatures(true, false, false, false),
            DebugIdentity = new AiMetadataDebugIdentity(
                Guid.NewGuid().ToString("N"),
                $"subtitle:{session.SessionId}"),
        };

        var response = await matcher.MatchAsync(input, cancellationToken).ConfigureAwait(false);
        ValidateStructure(session, input, response);
        await ValidateTmdbAsync(session, response, cancellationToken).ConfigureAwait(false);

        var assignments = response.Files!
            .Select(value => value.Matched == true
                && TryParseFileIndex(value.FileId, session.Candidates.Count, out var index)
                ? new SubtitleArchiveAssignment(session.Candidates[index].Id, value.Episode)
                : null)
            .OfType<SubtitleArchiveAssignment>()
            .ToArray();
        return new SubtitleArchiveAiMatchResult(assignments, response.Reason, response.Usage);
    }

    private static void ValidateStructure(
        SubtitleArchiveImportSession session,
        AiMetadataMatchInput input,
        AiMetadataMatchResponse response)
    {
        var genericFailure = AiMetadataResultValidator.ValidateStructure(input, response.Candidate);
        if (genericFailure is not null)
        {
            throw Failure(genericFailure.Kind, genericFailure.Code, response.Usage);
        }

        if (response.TmdbId != session.TmdbSeriesId)
        {
            throw Failure(MetadataFailureKind.Ambiguous, "subtitle_ai_tmdb_series_changed", response.Usage);
        }

        var anyMatched = false;
        for (var index = 0; index < input.Files.Count; index++)
        {
            var file = response.Files![index];
            if (file.Season != session.SeasonNumber)
            {
                throw Failure(MetadataFailureKind.Ambiguous, "subtitle_ai_tmdb_season_changed", response.Usage);
            }
            anyMatched |= file.Matched == true;
        }

        if (response.Matched != anyMatched)
        {
            throw Failure(MetadataFailureKind.Protocol, "subtitle_ai_match_summary_mismatch", response.Usage);
        }
        if (!anyMatched && string.IsNullOrWhiteSpace(response.Reason))
        {
            throw Failure(MetadataFailureKind.Protocol, "subtitle_ai_no_match_reason_missing", response.Usage);
        }
    }

    private async Task ValidateTmdbAsync(
        SubtitleArchiveImportSession session,
        AiMetadataMatchResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            var details = await tmdb.GetSeriesDetailsAsync(session.TmdbSeriesId, cancellationToken)
                .ConfigureAwait(false);
            if (details is null && tmdb is ITmdbRefreshClient refreshClient)
            {
                details = await refreshClient.RefreshSeriesDetailsAsync(session.TmdbSeriesId, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (details is null || details.Series.Id != session.TmdbSeriesId)
            {
                throw Failure(MetadataFailureKind.SemanticNoMatch, "subtitle_ai_tmdb_series_not_found", response.Usage);
            }

            var season = await tmdb.GetSeasonAsync(
                session.TmdbSeriesId,
                session.SeasonNumber,
                cancellationToken).ConfigureAwait(false);
            if (season is null && tmdb is ITmdbRefreshClient seasonRefreshClient)
            {
                season = await seasonRefreshClient.RefreshSeasonAsync(
                    session.TmdbSeriesId,
                    session.SeasonNumber,
                    cancellationToken).ConfigureAwait(false);
            }
            if (season is null
                || season.SeriesId != session.TmdbSeriesId
                || season.SeasonNumber != session.SeasonNumber)
            {
                throw Failure(MetadataFailureKind.SemanticNoMatch, "subtitle_ai_tmdb_season_not_found", response.Usage);
            }

            foreach (var episodeNumber in response.Files!
                         .Where(value => value.Matched == true)
                         .Select(value => value.Episode!.Value)
                         .Distinct())
            {
                var episode = await tmdb.GetEpisodeAsync(
                    session.TmdbSeriesId,
                    session.SeasonNumber,
                    episodeNumber,
                    cancellationToken).ConfigureAwait(false);
                if (episode is null && tmdb is ITmdbRefreshClient episodeRefreshClient)
                {
                    episode = await episodeRefreshClient.RefreshEpisodeAsync(
                        session.TmdbSeriesId,
                        session.SeasonNumber,
                        episodeNumber,
                        cancellationToken).ConfigureAwait(false);
                }
                if (episode is null)
                {
                    throw Failure(MetadataFailureKind.SemanticNoMatch, "subtitle_ai_tmdb_episode_not_found", response.Usage);
                }
                if (episode.SeriesId != session.TmdbSeriesId
                    || episode.SeasonNumber != session.SeasonNumber
                    || episode.EpisodeNumber != episodeNumber)
                {
                    throw Failure(MetadataFailureKind.Protocol, "subtitle_ai_tmdb_episode_identity_mismatch", response.Usage);
                }
            }
        }
        catch (TmdbClientException exception)
        {
            throw new AiMetadataMatcherException(
                exception.Kind,
                exception.SafeCode,
                exception,
                response.Usage);
        }
    }

    private static AiMetadataMatcherException Failure(
        MetadataFailureKind kind,
        string code,
        AiMetadataProviderUsage? usage) =>
        new(kind, code, usage: usage);

    private static bool TryParseFileIndex(string? fileId, int count, out int index)
    {
        for (index = 0; index < count; index++)
        {
            if (string.Equals(
                    fileId,
                    AiMetadataFileIdentity.FromIndex(index),
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        index = -1;
        return false;
    }
}
