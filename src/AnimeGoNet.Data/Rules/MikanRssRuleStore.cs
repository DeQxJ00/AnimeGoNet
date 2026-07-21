using System.Globalization;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Rules;

public sealed record MikanRssRuleSnapshot(
    string SourceProfileId,
    long Revision,
    MikanRssRuleSet Rules,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed class MikanRssRuleRevisionException : InvalidOperationException;

public sealed class MikanRssRuleStore(AnimeGoSqliteDatabase database)
{
    public async Task EnsureDefaultAsync(
        string sourceProfileId,
        MikanRssRuleSet defaults,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var normalizedProfile = NormalizeProfileId(sourceProfileId);
        var normalized = MikanRssRuleSetNormalizer.Normalize(defaults);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var exists = connection.CreateCommand();
        exists.Transaction = transaction;
        exists.CommandText = "SELECT COUNT(*) FROM mikan_rss_rule_sets WHERE source_profile_id = $profile;";
        exists.Parameters.AddWithValue("$profile", normalizedProfile);
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) == 0)
        {
            await InsertRuleSetAsync(
                connection, transaction, normalizedProfile, 1, normalized, utcNow, utcNow, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MikanRssRuleSnapshot?> GetAsync(
        string sourceProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = NormalizeProfileId(sourceProfileId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MikanRssRuleSnapshot> SaveAsync(
        string sourceProfileId,
        MikanRssRuleSet rules,
        long expectedRevision,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        var profile = NormalizeProfileId(sourceProfileId);
        var normalized = MikanRssRuleSetNormalizer.Normalize(rules);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var current = await ReadAsync(connection, transaction, profile, cancellationToken).ConfigureAwait(false);
        if ((current?.Revision ?? 0) != expectedRevision)
        {
            throw new MikanRssRuleRevisionException();
        }

        var revision = expectedRevision + 1;
        if (current is null)
        {
            await InsertRuleSetAsync(
                connection, transaction, profile, revision, normalized, utcNow, utcNow, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE mikan_rss_rule_sets SET revision = $revision, updated_at_utc = $now
                    WHERE source_profile_id = $profile AND revision = $expected;
                    """;
                update.Parameters.AddWithValue("$profile", profile);
                update.Parameters.AddWithValue("$revision", revision);
                update.Parameters.AddWithValue("$expected", expectedRevision);
                update.Parameters.AddWithValue("$now", Format(utcNow));
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                {
                    throw new MikanRssRuleRevisionException();
                }
            }

            await using (var clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = """
                    DELETE FROM mikan_rss_match_arrays WHERE source_profile_id = $profile;
                    DELETE FROM mikan_rss_priority_groups WHERE source_profile_id = $profile;
                    """;
                clear.Parameters.AddWithValue("$profile", profile);
                await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await InsertChildrenAsync(
                connection, transaction, profile, normalized, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await GetAsync(profile, cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task InsertRuleSetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string profile,
        long revision,
        MikanRssRuleSet rules,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO mikan_rss_rule_sets (
                source_profile_id, revision, created_at_utc, updated_at_utc)
            VALUES ($profile, $revision, $created, $updated);
            """;
        insert.Parameters.AddWithValue("$profile", profile);
        insert.Parameters.AddWithValue("$revision", revision);
        insert.Parameters.AddWithValue("$created", Format(createdAt));
        insert.Parameters.AddWithValue("$updated", Format(updatedAt));
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await InsertChildrenAsync(connection, transaction, profile, rules, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertChildrenAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string profile,
        MikanRssRuleSet rules,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < rules.PriorityGroups.Count; index++)
        {
            var group = rules.PriorityGroups[index];
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO mikan_rss_priority_groups (source_profile_id, id, name, position)
                VALUES ($profile, $id, $name, $position);
                """;
            insert.Parameters.AddWithValue("$profile", profile);
            insert.Parameters.AddWithValue("$id", group.Id);
            insert.Parameters.AddWithValue("$name", group.Name);
            insert.Parameters.AddWithValue("$position", index);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await InsertArraysAsync(connection, transaction, profile, "whitelist", null, rules.Whitelist, cancellationToken)
            .ConfigureAwait(false);
        await InsertArraysAsync(connection, transaction, profile, "blacklist", null, rules.Blacklist, cancellationToken)
            .ConfigureAwait(false);
        foreach (var group in rules.PriorityGroups)
        {
            await InsertArraysAsync(connection, transaction, profile, "priority", group.Id, group.Arrays, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task InsertArraysAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string profile,
        string scope,
        string? groupId,
        IReadOnlyList<NamedMatchArray> arrays,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < arrays.Count; index++)
        {
            var array = arrays[index];
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO mikan_rss_match_arrays (
                        source_profile_id, id, scope, group_id, name, enabled, position)
                    VALUES ($profile, $id, $scope, $group, $name, $enabled, $position);
                    """;
                insert.Parameters.AddWithValue("$profile", profile);
                insert.Parameters.AddWithValue("$id", array.Id);
                insert.Parameters.AddWithValue("$scope", scope);
                insert.Parameters.AddWithValue("$group", (object?)groupId ?? DBNull.Value);
                insert.Parameters.AddWithValue("$name", array.Name);
                insert.Parameters.AddWithValue("$enabled", array.Enabled);
                insert.Parameters.AddWithValue("$position", index);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            for (var valueIndex = 0; valueIndex < array.Values.Count; valueIndex++)
            {
                await using var value = connection.CreateCommand();
                value.Transaction = transaction;
                value.CommandText = """
                    INSERT INTO mikan_rss_match_values (
                        source_profile_id, array_id, position, value_lower)
                    VALUES ($profile, $array, $position, $value);
                    """;
                value.Parameters.AddWithValue("$profile", profile);
                value.Parameters.AddWithValue("$array", array.Id);
                value.Parameters.AddWithValue("$position", valueIndex);
                value.Parameters.AddWithValue("$value", array.Values[valueIndex]);
                await value.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<MikanRssRuleSnapshot?> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string profile,
        CancellationToken cancellationToken)
    {
        long revision;
        DateTimeOffset created;
        DateTimeOffset updated;
        await using (var root = connection.CreateCommand())
        {
            root.Transaction = transaction;
            root.CommandText = """
                SELECT revision, created_at_utc, updated_at_utc
                FROM mikan_rss_rule_sets WHERE source_profile_id = $profile;
                """;
            root.Parameters.AddWithValue("$profile", profile);
            await using var reader = await root.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            revision = reader.GetInt64(0);
            created = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
            updated = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
        }

        var arrays = new Dictionary<string, NamedMatchArray>(StringComparer.Ordinal);
        var arrayRows = new List<(string Id, string Scope, string? GroupId, string Name, bool Enabled, int Position)>();
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT id, scope, group_id, name, enabled, position
                FROM mikan_rss_match_arrays WHERE source_profile_id = $profile
                ORDER BY scope, COALESCE(group_id, ''), position;
                """;
            query.Parameters.AddWithValue("$profile", profile);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                arrayRows.Add((
                    reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3), reader.GetInt64(4) != 0, reader.GetInt32(5)));
            }
        }

        foreach (var row in arrayRows)
        {
            var values = new List<string>();
            await using var query = connection.CreateCommand();
            query.Transaction = transaction;
            query.CommandText = """
                SELECT value_lower FROM mikan_rss_match_values
                WHERE source_profile_id = $profile AND array_id = $array ORDER BY position;
                """;
            query.Parameters.AddWithValue("$profile", profile);
            query.Parameters.AddWithValue("$array", row.Id);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                values.Add(reader.GetString(0));
            }

            arrays.Add(row.Id, new NamedMatchArray(row.Id, row.Name, row.Enabled, values));
        }

        var whitelist = arrayRows.Where(row => row.Scope == "whitelist").OrderBy(row => row.Position)
            .Select(row => arrays[row.Id]).ToArray();
        var blacklist = arrayRows.Where(row => row.Scope == "blacklist").OrderBy(row => row.Position)
            .Select(row => arrays[row.Id]).ToArray();
        var groups = new List<PriorityGroup>();
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT id, name FROM mikan_rss_priority_groups
                WHERE source_profile_id = $profile ORDER BY position;
                """;
            query.Parameters.AddWithValue("$profile", profile);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var groupId = reader.GetString(0);
                groups.Add(new PriorityGroup(
                    groupId, reader.GetString(1),
                    arrayRows.Where(row => row.Scope == "priority" && row.GroupId == groupId)
                        .OrderBy(row => row.Position).Select(row => arrays[row.Id]).ToArray()));
            }
        }

        return new MikanRssRuleSnapshot(
            profile, revision, new MikanRssRuleSet(whitelist, blacklist, groups), created, updated);
    }

    private static string NormalizeProfileId(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Source profile id is required.", nameof(value));
        }

        return normalized;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
