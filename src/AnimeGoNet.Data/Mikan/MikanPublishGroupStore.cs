using System.Globalization;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Mikan;

public sealed record MikanPublishGroup(
    int GroupId,
    string? GroupName,
    string NameSource,
    string? SourceProfileId,
    string State,
    string? FailureCode,
    DateTimeOffset? FetchedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Revision);

public sealed record MikanPublishGroupCandidate(int MikanId, int GroupId, string SourceProfileId);

public enum MikanPublishGroupUpdateResult
{
    Updated,
    NotFound,
    RevisionConflict,
}

public sealed class MikanPublishGroupStore(AnimeGoSqliteDatabase database)
{
    public async Task<IReadOnlyList<MikanPublishGroup>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT groupid, group_name, name_source, source_profile_id, state,
                   failure_code, fetched_at_utc, next_attempt_at_utc, updated_at_utc, revision
            FROM mikan_publish_groups ORDER BY groupid;
            """;
        var values = new List<MikanPublishGroup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) values.Add(Read(reader));
        return values;
    }

    public async Task<MikanPublishGroupCandidate?> FindNextCandidateAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task.mikanid, task.groupid, task.source_profile_id
            FROM ingest_tasks AS task
            JOIN source_profiles AS profile ON profile.id = task.source_profile_id
            LEFT JOIN mikan_publish_groups AS map ON map.groupid = task.groupid
            WHERE profile.adapter = 'mikan'
              AND task.mikanid IS NOT NULL AND task.mikanid > 0
              AND task.groupid IS NOT NULL AND task.groupid > 0
              AND (map.groupid IS NULL
                   OR (map.name_source = 'automatic'
                       AND map.state IN ('pending', 'failed')
                       AND (map.next_attempt_at_utc IS NULL
                            OR map.next_attempt_at_utc <= $now
                            OR map.failure_code IN (
                                'mikan_publish_group_name_missing',
                                'mikan_publish_group_page_invalid',
                                'mikan_publish_group_encoding_invalid'))))
            ORDER BY task.updated_at_utc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$now", Format(utcNow));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new MikanPublishGroupCandidate(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2))
            : null;
    }

    public Task SaveAutomaticAsync(
        int groupId,
        string groupName,
        string sourceProfileId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        UpsertAutomaticAsync(groupId, groupName, sourceProfileId, "resolved", null, null, utcNow, cancellationToken);

    public Task SaveFailureAsync(
        int groupId,
        string sourceProfileId,
        string failureCode,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        UpsertAutomaticAsync(groupId, null, sourceProfileId, "failed", failureCode, utcNow.AddHours(6), utcNow, cancellationToken);

    public async Task<MikanPublishGroupUpdateResult> UpdateManualAsync(
        int groupId,
        string groupName,
        long expectedRevision,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        Validate(groupId, groupName);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE mikan_publish_groups
            SET group_name = $name, name_source = 'manual', state = 'resolved',
                failure_code = NULL, next_attempt_at_utc = NULL,
                updated_at_utc = $now, revision = revision + 1
            WHERE groupid = $groupid AND revision = $revision;
            """;
        command.Parameters.AddWithValue("$name", groupName.Trim());
        command.Parameters.AddWithValue("$now", Format(utcNow));
        command.Parameters.AddWithValue("$groupid", groupId);
        command.Parameters.AddWithValue("$revision", expectedRevision);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
            return MikanPublishGroupUpdateResult.Updated;
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM mikan_publish_groups WHERE groupid = $groupid;";
        exists.Parameters.AddWithValue("$groupid", groupId);
        return Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) == 0
            ? MikanPublishGroupUpdateResult.NotFound
            : MikanPublishGroupUpdateResult.RevisionConflict;
    }

    public async Task<MikanPublishGroupUpdateResult> RequestRefreshAsync(
        int groupId,
        long expectedRevision,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(groupId, 1);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE mikan_publish_groups
            SET name_source = 'automatic', state = 'pending', failure_code = NULL,
                next_attempt_at_utc = NULL, updated_at_utc = $now, revision = revision + 1
            WHERE groupid = $groupid AND revision = $revision;
            """;
        command.Parameters.AddWithValue("$groupid", groupId);
        command.Parameters.AddWithValue("$revision", expectedRevision);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
            return MikanPublishGroupUpdateResult.Updated;
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM mikan_publish_groups WHERE groupid = $groupid;";
        exists.Parameters.AddWithValue("$groupid", groupId);
        return Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) == 0
            ? MikanPublishGroupUpdateResult.NotFound
            : MikanPublishGroupUpdateResult.RevisionConflict;
    }

    private async Task UpsertAutomaticAsync(
        int groupId,
        string? groupName,
        string sourceProfileId,
        string state,
        string? failureCode,
        DateTimeOffset? nextAttemptAtUtc,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(groupId, 1);
        if (groupName is not null) Validate(groupId, groupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mikan_publish_groups (
                groupid, group_name, name_source, source_profile_id, state,
                failure_code, fetched_at_utc, next_attempt_at_utc, updated_at_utc, revision)
            VALUES ($groupid, $name, 'automatic', $profile, $state, $failure,
                    CASE WHEN $state = 'resolved' THEN $now ELSE NULL END,
                    $next, $now, 1)
            ON CONFLICT(groupid) DO UPDATE SET
                group_name = CASE WHEN mikan_publish_groups.name_source = 'manual'
                                  THEN mikan_publish_groups.group_name ELSE excluded.group_name END,
                name_source = CASE WHEN mikan_publish_groups.name_source = 'manual'
                                   THEN 'manual' ELSE 'automatic' END,
                source_profile_id = excluded.source_profile_id,
                state = CASE WHEN mikan_publish_groups.name_source = 'manual'
                             THEN 'resolved' ELSE excluded.state END,
                failure_code = CASE WHEN mikan_publish_groups.name_source = 'manual'
                                    THEN NULL ELSE excluded.failure_code END,
                fetched_at_utc = CASE WHEN mikan_publish_groups.name_source = 'manual'
                                      THEN mikan_publish_groups.fetched_at_utc ELSE excluded.fetched_at_utc END,
                next_attempt_at_utc = CASE WHEN mikan_publish_groups.name_source = 'manual'
                                           THEN NULL ELSE excluded.next_attempt_at_utc END,
                updated_at_utc = excluded.updated_at_utc,
                revision = mikan_publish_groups.revision + 1;
            """;
        command.Parameters.AddWithValue("$groupid", groupId);
        command.Parameters.AddWithValue("$name", (object?)groupName?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$profile", sourceProfileId.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$failure", (object?)failureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$next", nextAttemptAtUtc is null ? DBNull.Value : Format(nextAttemptAtUtc.Value));
        command.Parameters.AddWithValue("$now", Format(utcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static MikanPublishGroup Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5), ReadTimestamp(reader, 6),
        ReadTimestamp(reader, 7), Parse(reader.GetString(8)), reader.GetInt64(9));

    private static void Validate(int groupId, string groupName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(groupId, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        if (groupName.Trim().Length > 200) throw new ArgumentOutOfRangeException(nameof(groupName));
    }

    private static DateTimeOffset? ReadTimestamp(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Parse(reader.GetString(ordinal));
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
