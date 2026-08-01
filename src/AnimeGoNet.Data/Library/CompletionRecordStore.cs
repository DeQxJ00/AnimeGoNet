using System.Globalization;
using AnimeGoNet.Core.Library;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Library;

public sealed class CompletionRecordStore(Sqlite.AnimeGoSqliteDatabase database)
{
    public async Task<bool> TryAddAsync(
        CompletionRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var inserted = false;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO completion_records(
                    id,
                    tmdb_series_id,
                    tmdb_season_number,
                    tmdb_episode_number,
                    source_id,
                    source_item_id,
                    completed_at_utc)
                VALUES ($id, $seriesId, $seasonNumber, $episodeNumber, $sourceId, $sourceItemId, $completedAtUtc);
                """;
            command.Parameters.AddWithValue("$id", record.Id);
            command.Parameters.AddWithValue("$seriesId", record.Episode.SeriesId);
            command.Parameters.AddWithValue("$seasonNumber", record.Episode.SeasonNumber);
            command.Parameters.AddWithValue("$episodeNumber", record.Episode.EpisodeNumber);
            command.Parameters.AddWithValue("$sourceId", record.SourceId.ToLowerInvariant());
            command.Parameters.AddWithValue("$sourceItemId", (object?)record.SourceItemId ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$completedAtUtc",
                record.CompletedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }

        if (inserted)
        {
            await using var completeClaim = connection.CreateCommand();
            completeClaim.Transaction = transaction;
            completeClaim.CommandText = """
                UPDATE episode_claims
                SET state = 'completed', expires_at_utc = NULL
                WHERE tmdb_series_id = $seriesId
                  AND tmdb_season_number = $seasonNumber
                  AND tmdb_episode_number = $episodeNumber
                  AND state = 'active';
                """;
            completeClaim.Parameters.AddWithValue("$seriesId", record.Episode.SeriesId);
            completeClaim.Parameters.AddWithValue("$seasonNumber", record.Episode.SeasonNumber);
            completeClaim.Parameters.AddWithValue("$episodeNumber", record.Episode.EpisodeNumber);
            await completeClaim.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    public async Task<bool> TryAddAliasAsync(
        CompletionAlias alias,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias.CompletionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias.SourceId);
        var sourceId = alias.SourceId.Trim().ToLowerInvariant();
        var sourceWorkId = NullIfWhiteSpace(alias.SourceWorkId);
        var sourceEpisode = NullIfWhiteSpace(alias.SourceEpisode);
        var infoHash = NullIfWhiteSpace(alias.InfoHash)?.ToLowerInvariant();
        var createdAt = alias.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO completion_aliases (
                id, completion_id, source_id, source_work_id, source_episode,
                info_hash, created_at_utc)
            SELECT $id, $completion_id, $source_id, $source_work_id, $source_episode,
                   $info_hash, $created_at_utc
            WHERE EXISTS (
                SELECT 1 FROM completion_records WHERE id = $completion_id)
              AND NOT EXISTS (
                SELECT 1 FROM completion_aliases
                WHERE completion_id = $completion_id
                  AND source_id = $source_id
                  AND ifnull(source_work_id, '') = ifnull($source_work_id, '')
                  AND ifnull(source_episode, '') = ifnull($source_episode, '')
                  AND ifnull(info_hash, '') = ifnull($info_hash, ''));
            """;
        command.Parameters.AddWithValue("$id", alias.Id.Trim());
        command.Parameters.AddWithValue("$completion_id", alias.CompletionId.Trim());
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$source_work_id", (object?)sourceWorkId ?? DBNull.Value);
        command.Parameters.AddWithValue("$source_episode", (object?)sourceEpisode ?? DBNull.Value);
        command.Parameters.AddWithValue("$info_hash", (object?)infoHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at_utc", createdAt);
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    public async Task<CompletionAliasMatch?> FindBySourceEpisodeAsync(
        string sourceId,
        string sourceWorkId,
        string sourceEpisode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWorkId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEpisode);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT alias.id, alias.completion_id, alias.source_id, alias.source_work_id,
                   alias.source_episode, alias.info_hash, alias.created_at_utc,
                   completion.tmdb_series_id, completion.tmdb_season_number,
                   completion.tmdb_episode_number, completion.completed_at_utc
            FROM completion_aliases AS alias
            JOIN completion_records AS completion ON completion.id = alias.completion_id
            WHERE alias.source_id = $source_id
              AND alias.source_work_id = $source_work_id
              AND alias.source_episode = $source_episode
            ORDER BY completion.completed_at_utc, alias.created_at_utc, alias.id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$source_work_id", sourceWorkId.Trim());
        command.Parameters.AddWithValue("$source_episode", sourceEpisode.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var alias = new CompletionAlias
        {
            Id = reader.GetString(0),
            CompletionId = reader.GetString(1),
            SourceId = reader.GetString(2),
            SourceWorkId = reader.IsDBNull(3) ? null : reader.GetString(3),
            SourceEpisode = reader.IsDBNull(4) ? null : reader.GetString(4),
            InfoHash = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
        };
        return new CompletionAliasMatch(
            alias,
            new TmdbEpisodeIdentity(reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9)),
            DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture));
    }

    public async Task<bool> ExistsAsync(
        TmdbEpisodeIdentity episode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM completion_records
                WHERE tmdb_series_id = $seriesId
                  AND tmdb_season_number = $seasonNumber
                  AND tmdb_episode_number = $episodeNumber);
            """;
        command.Parameters.AddWithValue("$seriesId", episode.SeriesId);
        command.Parameters.AddWithValue("$seasonNumber", episode.SeasonNumber);
        command.Parameters.AddWithValue("$episodeNumber", episode.EpisodeNumber);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture) == 1;
    }

    public async Task<bool> ReleaseClaimAsync(
        TmdbEpisodeIdentity episode,
        string taskFileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskFileId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE episode_claims
            SET state = 'released', expires_at_utc = NULL
            WHERE tmdb_series_id = $seriesId
              AND tmdb_season_number = $seasonNumber
              AND tmdb_episode_number = $episodeNumber
              AND task_file_id = $taskFileId
              AND state = 'active';
            """;
        command.Parameters.AddWithValue("$seriesId", episode.SeriesId);
        command.Parameters.AddWithValue("$seasonNumber", episode.SeasonNumber);
        command.Parameters.AddWithValue("$episodeNumber", episode.EpisodeNumber);
        command.Parameters.AddWithValue("$taskFileId", taskFileId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> ReleaseFallbackClaimAsync(
        FallbackDedupScope scope,
        string taskFileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskFileId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE fallback_claims
            SET state = 'released', expires_at_utc = NULL
            WHERE scope_kind = $scope_kind
              AND scope_key = $scope_key
              AND task_file_id = $task_file_id
              AND state = 'active';
            """;
        command.Parameters.AddWithValue("$scope_kind", scope.Kind);
        command.Parameters.AddWithValue("$scope_key", scope.Key);
        command.Parameters.AddWithValue("$task_file_id", taskFileId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
