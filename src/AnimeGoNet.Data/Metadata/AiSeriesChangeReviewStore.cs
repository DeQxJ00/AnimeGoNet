using System.Globalization;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Metadata;

public sealed record AiSeriesChangeReviewProposal(
    string TaskId,
    string TaskFileId,
    string State,
    int ExpectedTmdbSeriesId,
    int ExpectedTmdbSeasonNumber,
    TmdbCanonicalEpisode Proposed,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ReviewedAtUtc);

public enum AiSeriesChangeReviewDecisionResult
{
    Updated,
    NotFound,
    NotPending,
}

public sealed class AiSeriesChangeReviewStore(AnimeGoSqliteDatabase database)
{
    public async Task<bool> RecordAsync(
        MetadataEpisodeTaskClaim claim,
        ValidatedAiMetadataMatch proposal,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(proposal);
        var proposedFiles = proposal.Files.Where(file => file.Episode is not null).ToArray();
        if (proposal.Files.Count != 1 || proposedFiles.Length != 1)
        {
            return false;
        }

        var proposedFile = proposedFiles[0];
        var taskFile = claim.Files.SingleOrDefault(file => string.Equals(
            file.RelativePath,
            proposedFile.Input.Name,
            StringComparison.Ordinal));
        if (taskFile is null)
        {
            return false;
        }

        var canonical = new TmdbCanonicalEpisode(
            proposal.Series,
            proposedFile.Season,
            proposedFile.Episode!,
            proposal.Series.Name);
        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO ai_series_change_reviews (
                    id, task_id, task_file_id, state,
                    expected_tmdb_series_id, expected_tmdb_season_number,
                    proposed_tmdb_series_id, proposed_series_name, proposed_original_name,
                    proposed_series_first_air_date, proposed_series_poster_path,
                    proposed_tmdb_season_id, proposed_tmdb_season_number,
                    proposed_season_name, proposed_season_air_date,
                    proposed_season_episode_count, proposed_season_poster_path,
                    proposed_tmdb_episode_id, proposed_tmdb_episode_number,
                    proposed_episode_name, proposed_episode_air_date,
                    requested_at_utc, reviewed_at_utc)
                VALUES (
                    $id, $task_id, $file_id, 'pending',
                    $expected_series, $expected_season,
                    $series_id, $series_name, $original_name,
                    $series_air_date, $series_poster,
                    $season_id, $season_number, $season_name, $season_air_date,
                    $episode_count, $season_poster,
                    $episode_id, $episode_number, $episode_name, $episode_air_date,
                    $now, NULL)
                ON CONFLICT(task_id, task_file_id) DO UPDATE SET
                    state = 'pending',
                    expected_tmdb_series_id = excluded.expected_tmdb_series_id,
                    expected_tmdb_season_number = excluded.expected_tmdb_season_number,
                    proposed_tmdb_series_id = excluded.proposed_tmdb_series_id,
                    proposed_series_name = excluded.proposed_series_name,
                    proposed_original_name = excluded.proposed_original_name,
                    proposed_series_first_air_date = excluded.proposed_series_first_air_date,
                    proposed_series_poster_path = excluded.proposed_series_poster_path,
                    proposed_tmdb_season_id = excluded.proposed_tmdb_season_id,
                    proposed_tmdb_season_number = excluded.proposed_tmdb_season_number,
                    proposed_season_name = excluded.proposed_season_name,
                    proposed_season_air_date = excluded.proposed_season_air_date,
                    proposed_season_episode_count = excluded.proposed_season_episode_count,
                    proposed_season_poster_path = excluded.proposed_season_poster_path,
                    proposed_tmdb_episode_id = excluded.proposed_tmdb_episode_id,
                    proposed_tmdb_episode_number = excluded.proposed_tmdb_episode_number,
                    proposed_episode_name = excluded.proposed_episode_name,
                    proposed_episode_air_date = excluded.proposed_episode_air_date,
                    requested_at_utc = excluded.requested_at_utc,
                    reviewed_at_utc = NULL;
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$task_id", claim.Resolution.TaskId);
            insert.Parameters.AddWithValue("$file_id", taskFile.FileId);
            insert.Parameters.AddWithValue("$expected_series", claim.TmdbSeriesId);
            insert.Parameters.AddWithValue("$expected_season", claim.TmdbSeasonNumber);
            AddCanonicalParameters(insert, canonical, now);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var task = connection.CreateCommand())
        {
            task.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            task.CommandText = """
                UPDATE ingest_tasks
                SET readaptation_review_state = 'pending',
                    readaptation_review_requested_at_utc = $now,
                    readaptation_reviewed_at_utc = NULL,
                    updated_at_utc = $now
                WHERE id = $task_id;
                """;
            task.Parameters.AddWithValue("$task_id", claim.Resolution.TaskId);
            task.Parameters.AddWithValue("$now", now);
            if (await task.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("AI Series change review task disappeared.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<AiSeriesChangeReviewProposal?> GetPendingAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task_file_id, state, expected_tmdb_series_id, expected_tmdb_season_number,
                   proposed_tmdb_series_id, proposed_series_name, proposed_original_name,
                   proposed_series_first_air_date, proposed_series_poster_path,
                   proposed_tmdb_season_id, proposed_tmdb_season_number,
                   proposed_season_name, proposed_season_air_date,
                   proposed_season_episode_count, proposed_season_poster_path,
                   proposed_tmdb_episode_id, proposed_tmdb_episode_number,
                   proposed_episode_name, proposed_episode_air_date,
                   requested_at_utc, reviewed_at_utc
            FROM ai_series_change_reviews
            WHERE task_id = $task_id AND state = 'pending'
            ORDER BY requested_at_utc DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var series = new TmdbSeries(
            reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
            ReadDate(reader, 7), reader.IsDBNull(8) ? null : reader.GetString(8));
        var season = new TmdbSeason(
            reader.GetInt32(9), series.Id, reader.GetInt32(10), reader.GetString(11),
            ReadDate(reader, 12), reader.GetInt32(13),
            reader.IsDBNull(14) ? null : reader.GetString(14));
        var episode = new TmdbEpisode(
            reader.GetInt32(15), series.Id, season.SeasonNumber, reader.GetInt32(16),
            reader.GetString(17), ReadDate(reader, 18));
        return new AiSeriesChangeReviewProposal(
            taskId, reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3),
            new TmdbCanonicalEpisode(series, season, episode, series.Name),
            DateTimeOffset.Parse(reader.GetString(19), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(20) ? null : DateTimeOffset.Parse(
                reader.GetString(20), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    public Task<AiSeriesChangeReviewDecisionResult> AcceptAsync(
        string taskId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        SetDecisionAsync(taskId, "accepted", utcNow, approveTask: false, cancellationToken);

    public Task<AiSeriesChangeReviewDecisionResult> RejectAsync(
        string taskId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        SetDecisionAsync(taskId, "rejected", utcNow, approveTask: true, cancellationToken);

    private async Task<AiSeriesChangeReviewDecisionResult> SetDecisionAsync(
        string taskId,
        string state,
        DateTimeOffset utcNow,
        bool approveTask,
        CancellationToken cancellationToken)
    {
        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var update = connection.CreateCommand();
        update.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        update.CommandText = """
            UPDATE ai_series_change_reviews
            SET state = $state, reviewed_at_utc = $now
            WHERE task_id = $task_id AND state = 'pending';
            """;
        update.Parameters.AddWithValue("$state", state);
        update.Parameters.AddWithValue("$now", now);
        update.Parameters.AddWithValue("$task_id", taskId);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            await using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT EXISTS(SELECT 1 FROM ingest_tasks WHERE id = $task_id);";
            exists.Parameters.AddWithValue("$task_id", taskId);
            return Convert.ToInt64(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) == 0
                ? AiSeriesChangeReviewDecisionResult.NotFound
                : AiSeriesChangeReviewDecisionResult.NotPending;
        }

        if (approveTask)
        {
            await using var task = connection.CreateCommand();
            task.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            task.CommandText = """
                UPDATE ingest_tasks
                SET readaptation_review_state = 'approved',
                    readaptation_reviewed_at_utc = $now,
                    updated_at_utc = $now
                WHERE id = $task_id AND readaptation_review_state = 'pending';
                """;
            task.Parameters.AddWithValue("$task_id", taskId);
            task.Parameters.AddWithValue("$now", now);
            await task.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return AiSeriesChangeReviewDecisionResult.Updated;
    }

    private static DateOnly? ReadDate(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateOnly.ParseExact(reader.GetString(ordinal), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void AddCanonicalParameters(
        Microsoft.Data.Sqlite.SqliteCommand command,
        TmdbCanonicalEpisode canonical,
        string now)
    {
        command.Parameters.AddWithValue("$series_id", canonical.Series.Id);
        command.Parameters.AddWithValue("$series_name", canonical.CanonicalSeriesName);
        command.Parameters.AddWithValue("$original_name", canonical.Series.OriginalName);
        command.Parameters.AddWithValue("$series_air_date", DateValue(canonical.Series.FirstAirDate));
        command.Parameters.AddWithValue("$series_poster", (object?)canonical.Series.PosterPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$season_id", canonical.Season.Id);
        command.Parameters.AddWithValue("$season_number", canonical.Season.SeasonNumber);
        command.Parameters.AddWithValue("$season_name", canonical.Season.Name);
        command.Parameters.AddWithValue("$season_air_date", DateValue(canonical.Season.AirDate));
        command.Parameters.AddWithValue("$episode_count", canonical.Season.EpisodeCount);
        command.Parameters.AddWithValue("$season_poster", (object?)canonical.Season.PosterPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$episode_id", canonical.Episode.Id);
        command.Parameters.AddWithValue("$episode_number", canonical.Episode.EpisodeNumber);
        command.Parameters.AddWithValue("$episode_name", canonical.Episode.Name);
        command.Parameters.AddWithValue("$episode_air_date", DateValue(canonical.Episode.AirDate));
        command.Parameters.AddWithValue("$now", now);
    }

    private static object DateValue(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (object)DBNull.Value;
}
