using System.Globalization;
using System.Text;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Rules;

public sealed record LegacyMikanFilterSnapshot(
    string SourceProfileId,
    long Revision,
    LegacyMikanFilterConfig Config,
    string UpdatedSource,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed class LegacyMikanFilterRevisionException : InvalidOperationException;

public sealed class LegacyMikanFilterStore(AnimeGoSqliteDatabase database)
{
    public async Task EnsureDefaultAsync(
        string sourceProfileId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var profile = NormalizeProfile(sourceProfileId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR IGNORE INTO legacy_mikan_filter_sets (
                source_profile_id, revision, updated_source, created_at_utc, updated_at_utc)
            VALUES ($profile, 1, 'migration', $now, $now);
            """;
        insert.Parameters.AddWithValue("$profile", profile);
        insert.Parameters.AddWithValue("$now", Format(utcNow));
        if (await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
        {
            await InsertSnapshotAsync(
                connection, transaction, profile, 1, LegacyMikanFilterCodec.Empty,
                "migration", utcNow, cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LegacyMikanFilterSnapshot?> GetAsync(
        string sourceProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = NormalizeProfile(sourceProfileId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LegacyMikanFilterSnapshot> SaveAsync(
        string sourceProfileId,
        LegacyMikanFilterConfig config,
        long expectedRevision,
        string updatedSource,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedRevision, 1);
        ValidateSource(updatedSource);
        var profile = NormalizeProfile(sourceProfileId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var current = await ReadAsync(connection, transaction, profile, cancellationToken).ConfigureAwait(false);
        if (current is null || current.Revision != expectedRevision) throw new LegacyMikanFilterRevisionException();
        var revision = expectedRevision + 1;
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE legacy_mikan_filter_sets
                SET revision = $revision, updated_source = $source, updated_at_utc = $now
                WHERE source_profile_id = $profile AND revision = $expected;
                """;
            update.Parameters.AddWithValue("$revision", revision);
            update.Parameters.AddWithValue("$source", updatedSource);
            update.Parameters.AddWithValue("$now", Format(utcNow));
            update.Parameters.AddWithValue("$profile", profile);
            update.Parameters.AddWithValue("$expected", expectedRevision);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new LegacyMikanFilterRevisionException();
        }
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM legacy_mikan_filter_rules WHERE source_profile_id = $profile;";
            clear.Parameters.AddWithValue("$profile", profile);
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await InsertConfigAsync(connection, transaction, profile, config, cancellationToken).ConfigureAwait(false);
        await InsertSnapshotAsync(
            connection, transaction, profile, revision, config, updatedSource, utcNow, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await GetAsync(profile, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<LegacyMikanFilterSnapshot> SaveLegacyAsync(
        string sourceProfileId,
        LegacyMikanFilterConfig config,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(sourceProfileId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Legacy Mikan filter was not initialized.");
        return await SaveAsync(
            sourceProfileId, config, current.Revision, "legacy_api", utcNow, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LegacyMikanFilterSnapshot> RollbackAsync(
        string sourceProfileId,
        long targetRevision,
        long expectedRevision,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var profile = NormalizeProfile(sourceProfileId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT config_json FROM legacy_mikan_filter_snapshots
            WHERE source_profile_id = $profile AND revision = $revision;
            """;
        command.Parameters.AddWithValue("$profile", profile);
        command.Parameters.AddWithValue("$revision", targetRevision);
        var json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string
            ?? throw new KeyNotFoundException("Legacy Mikan filter snapshot was not found.");
        return await SaveAsync(
            profile, LegacyMikanFilterCodec.Parse(Encoding.UTF8.GetBytes(json)),
            expectedRevision, "rollback", utcNow, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertConfigAsync(
        SqliteConnection connection, SqliteTransaction transaction, string profile,
        LegacyMikanFilterConfig config, CancellationToken cancellationToken)
    {
        await InsertTierAsync(connection, transaction, profile, 0, config.Filiter0, cancellationToken).ConfigureAwait(false);
        await InsertTierAsync(connection, transaction, profile, 1, config.Filiter1, cancellationToken).ConfigureAwait(false);
        await InsertTierAsync(connection, transaction, profile, 2, config.Filiter2, cancellationToken).ConfigureAwait(false);
        await InsertTierAsync(connection, transaction, profile, 3, config.Filiter3, cancellationToken).ConfigureAwait(false);
        await InsertTierAsync(connection, transaction, profile, 4, config.Filiter4, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertTierAsync(
        SqliteConnection connection, SqliteTransaction transaction, string profile, int tier,
        IEnumerable<KeyValuePair<string, LegacyMikanFilterRule>> rules, CancellationToken cancellationToken)
    {
        var position = 0;
        foreach (var pair in rules)
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO legacy_mikan_filter_rules (
                        source_profile_id, tier, legacy_key, position, whitelist_enabled, blacklist_enabled)
                    VALUES ($profile, $tier, $key, $position, $white, $black);
                    """;
                insert.Parameters.AddWithValue("$profile", profile);
                insert.Parameters.AddWithValue("$tier", tier);
                insert.Parameters.AddWithValue("$key", pair.Key);
                insert.Parameters.AddWithValue("$position", position++);
                insert.Parameters.AddWithValue("$white", pair.Value.IsEnableWhitelist);
                insert.Parameters.AddWithValue("$black", pair.Value.IsEnableBlacklist);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await InsertValuesAsync(connection, transaction, profile, tier, pair.Key, "whitelist", pair.Value.Whitelist, cancellationToken).ConfigureAwait(false);
            await InsertValuesAsync(connection, transaction, profile, tier, pair.Key, "blacklist", pair.Value.Blacklist, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertValuesAsync(
        SqliteConnection connection, SqliteTransaction transaction, string profile, int tier,
        string key, string kind, IReadOnlyList<string> values, CancellationToken cancellationToken)
    {
        for (var position = 0; position < values.Count; position++)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO legacy_mikan_filter_values (
                    source_profile_id, tier, legacy_key, list_kind, position, value)
                VALUES ($profile, $tier, $key, $kind, $position, $value);
                """;
            insert.Parameters.AddWithValue("$profile", profile);
            insert.Parameters.AddWithValue("$tier", tier);
            insert.Parameters.AddWithValue("$key", key);
            insert.Parameters.AddWithValue("$kind", kind);
            insert.Parameters.AddWithValue("$position", position);
            insert.Parameters.AddWithValue("$value", values[position]);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertSnapshotAsync(
        SqliteConnection connection, SqliteTransaction transaction, string profile, long revision,
        LegacyMikanFilterConfig config, string source, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO legacy_mikan_filter_snapshots (
                source_profile_id, revision, config_json, updated_source, created_at_utc)
            VALUES ($profile, $revision, $json, $source, $now);
            """;
        insert.Parameters.AddWithValue("$profile", profile);
        insert.Parameters.AddWithValue("$revision", revision);
        insert.Parameters.AddWithValue("$json", Encoding.UTF8.GetString(LegacyMikanFilterCodec.Encode(config)));
        insert.Parameters.AddWithValue("$source", source);
        insert.Parameters.AddWithValue("$now", Format(now));
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<LegacyMikanFilterSnapshot?> ReadAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string profile, CancellationToken cancellationToken)
    {
        long revision;
        string source;
        DateTimeOffset created;
        DateTimeOffset updated;
        await using (var root = connection.CreateCommand())
        {
            root.Transaction = transaction;
            root.CommandText = """
                SELECT revision, updated_source, created_at_utc, updated_at_utc
                FROM legacy_mikan_filter_sets WHERE source_profile_id = $profile;
                """;
            root.Parameters.AddWithValue("$profile", profile);
            await using var reader = await root.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            revision = reader.GetInt64(0);
            source = reader.GetString(1);
            created = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
            updated = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
        }
        await using var snapshot = connection.CreateCommand();
        snapshot.Transaction = transaction;
        snapshot.CommandText = """
            SELECT config_json FROM legacy_mikan_filter_snapshots
            WHERE source_profile_id = $profile AND revision = $revision;
            """;
        snapshot.Parameters.AddWithValue("$profile", profile);
        snapshot.Parameters.AddWithValue("$revision", revision);
        var json = (string)(await snapshot.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        return new LegacyMikanFilterSnapshot(
            profile, revision, LegacyMikanFilterCodec.Parse(Encoding.UTF8.GetBytes(json)), source, created, updated);
    }

    private static string NormalizeProfile(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant();
    }

    private static void ValidateSource(string value)
    {
        if (value is not ("migration" or "legacy_api" or "web" or "rollback"))
            throw new ArgumentException("Unknown legacy filter update source.", nameof(value));
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
