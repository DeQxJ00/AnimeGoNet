using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Feeds;

public sealed class MikanRssBatchStore(AnimeGoSqliteDatabase database)
{
    public async Task<MikanRssBatchRecord> SaveAsync(
        string sourceProfileId,
        long ruleRevision,
        bool priorityEnabled,
        MikanRssBatchPlan plan,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        ArgumentOutOfRangeException.ThrowIfLessThan(ruleRevision, 1);
        ArgumentNullException.ThrowIfNull(plan);
        var profile = sourceProfileId.Trim().ToLowerInvariant();
        var fingerprint = Fingerprint(profile, ruleRevision, priorityEnabled, plan);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var batchId = Guid.NewGuid().ToString("N");
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT OR IGNORE INTO mikan_rss_batches (
                    id, source_profile_id, rule_revision, fingerprint, mikanid,
                    priority_enabled, entry_count, created_at_utc)
                VALUES ($id, $profile, $revision, $fingerprint, $mikanid, $enabled, $count, $created);
                """;
            insert.Parameters.AddWithValue("$id", batchId);
            insert.Parameters.AddWithValue("$profile", profile);
            insert.Parameters.AddWithValue("$revision", ruleRevision);
            insert.Parameters.AddWithValue("$fingerprint", fingerprint);
            insert.Parameters.AddWithValue("$mikanid", (object?)plan.MikanId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$enabled", priorityEnabled);
            insert.Parameters.AddWithValue("$count", plan.Items.Count);
            insert.Parameters.AddWithValue("$created", Format(utcNow));
            if (await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return (await GetByFingerprintAsync(fingerprint, cancellationToken).ConfigureAwait(false))!;
            }
        }

        for (var index = 0; index < plan.Items.Count; index++)
        {
            await InsertEntryAsync(connection, transaction, batchId, index, plan.Items[index], cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await GetAsync(batchId, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<MikanRssWinnerLease?> TryClaimWinnerAsync(
        string batchId,
        string candidateId,
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        var token = Guid.NewGuid().ToString("N");
        var expires = utcNow.Add(leaseDuration);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE mikan_rss_batch_entries
            SET effect_state = 'claimed', claim_token = $token, claim_expires_at_utc = $expires
            WHERE batch_id = $batch AND candidate_id = $candidate AND decision_kind = 'Winner'
              AND (effect_state = 'ready'
                   OR (effect_state = 'claimed' AND claim_expires_at_utc <= $now));
            """;
        command.Parameters.AddWithValue("$batch", batchId);
        command.Parameters.AddWithValue("$candidate", candidateId);
        command.Parameters.AddWithValue("$token", token);
        command.Parameters.AddWithValue("$expires", Format(expires));
        command.Parameters.AddWithValue("$now", Format(utcNow));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1
            ? new MikanRssWinnerLease(batchId, candidateId, token, expires)
            : null;
    }

    public Task<bool> CompleteWinnerAsync(
        MikanRssWinnerLease lease,
        string ingestTaskId,
        CancellationToken cancellationToken = default) =>
        FinishLeaseAsync(lease, "ingested", ingestTaskId, cancellationToken);

    public Task<bool> ReleaseWinnerAsync(
        MikanRssWinnerLease lease,
        CancellationToken cancellationToken = default) =>
        FinishLeaseAsync(lease, "ready", null, cancellationToken);

    public async Task<MikanRssBatchRecord?> GetAsync(string batchId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, "id", batchId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MikanRssBatchRecord?> GetByFingerprintAsync(string fingerprint, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, "fingerprint", fingerprint, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> FinishLeaseAsync(
        MikanRssWinnerLease lease,
        string targetState,
        string? ingestTaskId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (targetState == "ingested") ArgumentException.ThrowIfNullOrWhiteSpace(ingestTaskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE mikan_rss_batch_entries
            SET effect_state = $state, claim_token = NULL, claim_expires_at_utc = NULL,
                ingest_task_id = $task
            WHERE batch_id = $batch AND candidate_id = $candidate
              AND effect_state = 'claimed' AND claim_token = $token;
            """;
        command.Parameters.AddWithValue("$state", targetState);
        command.Parameters.AddWithValue("$task", (object?)ingestTaskId ?? DBNull.Value);
        command.Parameters.AddWithValue("$batch", lease.BatchId);
        command.Parameters.AddWithValue("$candidate", lease.CandidateId);
        command.Parameters.AddWithValue("$token", lease.LeaseToken);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static async Task InsertEntryAsync(
        SqliteConnection connection, SqliteTransaction transaction, string batchId, int ordinal,
        MikanRssPlannedItem item, CancellationToken cancellationToken)
    {
        var torrentFingerprint = Sha256(item.FeedItem.TorrentUrl);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO mikan_rss_batch_entries (
                    batch_id, candidate_id, ordinal, title, mikan_url, torrent_url_fingerprint,
                    content_type, length_bytes, published_date, source_episode_kind, source_episode,
                    decision_kind, decision_reason, winner_candidate_id, effect_state)
                VALUES ($batch, $candidate, $ordinal, $title, $mikan_url, $torrent, $content_type,
                    $length, $published, $kind, $episode, $decision, $reason, $winner, $state);
                """;
            insert.Parameters.AddWithValue("$batch", batchId);
            insert.Parameters.AddWithValue("$candidate", item.Candidate.Id);
            insert.Parameters.AddWithValue("$ordinal", ordinal);
            insert.Parameters.AddWithValue("$title", item.FeedItem.Title);
            insert.Parameters.AddWithValue("$mikan_url", item.FeedItem.MikanUrl);
            insert.Parameters.AddWithValue("$torrent", torrentFingerprint);
            insert.Parameters.AddWithValue("$content_type", item.FeedItem.ContentType);
            insert.Parameters.AddWithValue("$length", item.FeedItem.Length);
            insert.Parameters.AddWithValue("$published", (object?)item.FeedItem.PublishedDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("$kind", (object?)item.Candidate.SourceEpisodeKind ?? DBNull.Value);
            insert.Parameters.AddWithValue("$episode", (object?)item.Candidate.SourceEpisode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$decision", item.Decision.Kind.ToString());
            insert.Parameters.AddWithValue("$reason", item.Decision.Reason);
            insert.Parameters.AddWithValue("$winner", (object?)item.Decision.WinnerId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$state", item.Decision.Kind == MikanRssDecisionKind.Winner ? "ready" : "blocked");
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var index = 0; index < item.Decision.EvaluatedPriorityGroups.Count; index++)
        {
            await using var group = connection.CreateCommand();
            group.Transaction = transaction;
            group.CommandText = """
                INSERT INTO mikan_rss_decision_groups (batch_id, candidate_id, position, group_id)
                VALUES ($batch, $candidate, $position, $group);
                """;
            group.Parameters.AddWithValue("$batch", batchId);
            group.Parameters.AddWithValue("$candidate", item.Candidate.Id);
            group.Parameters.AddWithValue("$position", index);
            group.Parameters.AddWithValue("$group", item.Decision.EvaluatedPriorityGroups[index]);
            await group.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<MikanRssBatchRecord?> ReadAsync(
        SqliteConnection connection, string keyColumn, string key, CancellationToken cancellationToken)
    {
        string id;
        string profile;
        long revision;
        string fingerprint;
        int? mikanId;
        bool enabled;
        DateTimeOffset created;
        await using (var root = connection.CreateCommand())
        {
            root.CommandText = $"""
                SELECT id, source_profile_id, rule_revision, fingerprint, mikanid,
                       priority_enabled, created_at_utc
                FROM mikan_rss_batches WHERE {keyColumn} = $key;
                """;
            root.Parameters.AddWithValue("$key", key);
            await using var reader = await root.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            id = reader.GetString(0);
            profile = reader.GetString(1);
            revision = reader.GetInt64(2);
            fingerprint = reader.GetString(3);
            mikanId = reader.IsDBNull(4) ? null : reader.GetInt32(4);
            enabled = reader.GetBoolean(5);
            created = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture);
        }

        var storedRows = new List<StoredEntryRow>();
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT candidate_id, title, mikan_url, torrent_url_fingerprint, content_type,
                       length_bytes, published_date, source_episode_kind, source_episode,
                   decision_kind, decision_reason, winner_candidate_id, effect_state, ingest_task_id
                FROM mikan_rss_batch_entries WHERE batch_id = $batch ORDER BY ordinal;
                """;
            query.Parameters.AddWithValue("$batch", id);
            await using var rows = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await rows.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                storedRows.Add(new StoredEntryRow(
                    rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3), rows.GetString(4),
                    rows.GetInt64(5), rows.IsDBNull(6) ? null : rows.GetString(6),
                    rows.IsDBNull(7) ? null : rows.GetString(7), rows.IsDBNull(8) ? null : rows.GetString(8),
                    rows.GetString(9), rows.GetString(10), rows.IsDBNull(11) ? null : rows.GetString(11),
                    rows.GetString(12), rows.IsDBNull(13) ? null : rows.GetString(13)));
            }
        }

        var entries = new List<MikanRssBatchEntryRecord>(storedRows.Count);
        foreach (var row in storedRows)
        {
            var groups = await ReadGroupsAsync(connection, id, row.CandidateId, cancellationToken).ConfigureAwait(false);
            var decision = new MikanRssDecision(row.CandidateId,
                Enum.Parse<MikanRssDecisionKind>(row.DecisionKind), row.DecisionReason,
                row.WinnerCandidateId, groups);
            entries.Add(new MikanRssBatchEntryRecord(
                row.CandidateId, row.Title, row.MikanUrl, row.TorrentUrlFingerprint,
                row.ContentType, row.LengthBytes, row.PublishedDate, row.SourceEpisodeKind,
                row.SourceEpisode, decision, row.EffectState, row.IngestTaskId));
        }

        return new MikanRssBatchRecord(id, profile, revision, fingerprint, mikanId, enabled, created, entries);
    }

    private static async Task<IReadOnlyList<string>> ReadGroupsAsync(
        SqliteConnection connection, string batchId, string candidateId, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT group_id FROM mikan_rss_decision_groups
            WHERE batch_id = $batch AND candidate_id = $candidate ORDER BY position;
            """;
        command.Parameters.AddWithValue("$batch", batchId);
        command.Parameters.AddWithValue("$candidate", candidateId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(reader.GetString(0));
        return result;
    }

    private static string Fingerprint(string profile, long revision, bool enabled, MikanRssBatchPlan plan)
    {
        var value = new StringBuilder().Append(profile).Append('|').Append(revision).Append('|').Append(enabled).Append('|');
        foreach (var item in plan.Items)
        {
            value.Append(item.Candidate.Id).Append('|').Append(item.Decision.Kind).Append('|')
                .Append(item.Decision.Reason).Append('|').Append(item.Decision.WinnerId).Append(';');
        }
        return Sha256(value.ToString());
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private sealed record StoredEntryRow(
        string CandidateId,
        string Title,
        string MikanUrl,
        string TorrentUrlFingerprint,
        string ContentType,
        long LengthBytes,
        string? PublishedDate,
        string? SourceEpisodeKind,
        string? SourceEpisode,
        string DecisionKind,
        string DecisionReason,
        string? WinnerCandidateId,
        string EffectState,
        string? IngestTaskId);
}
