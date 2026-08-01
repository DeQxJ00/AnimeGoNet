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
                    priority_enabled, entry_count, created_at_utc,
                    legacy_filter_revision, legacy_filter_enabled)
                VALUES ($id, $profile, $revision, $fingerprint, $mikanid, $enabled, $count, $created,
                    $legacy_revision, $legacy_enabled);
                """;
            insert.Parameters.AddWithValue("$id", batchId);
            insert.Parameters.AddWithValue("$profile", profile);
            insert.Parameters.AddWithValue("$revision", ruleRevision);
            insert.Parameters.AddWithValue("$fingerprint", fingerprint);
            insert.Parameters.AddWithValue("$mikanid", (object?)plan.MikanId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$enabled", priorityEnabled);
            insert.Parameters.AddWithValue("$count", plan.Items.Count);
            insert.Parameters.AddWithValue("$created", Format(utcNow));
            insert.Parameters.AddWithValue("$legacy_revision", plan.LegacyFilterRevision);
            insert.Parameters.AddWithValue("$legacy_enabled", plan.LegacyFilterEnabled);
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
        CancellationToken cancellationToken = default) =>
        (await TryClaimWinnerCoreAsync(
            batchId,
            candidateId,
            utcNow,
            leaseDuration,
            null,
            null,
            null,
            cancellationToken).ConfigureAwait(false)).Lease;

    public Task<MikanRssWinnerClaimResult> TryClaimWinnerWithCompletionCheckAsync(
        string batchId,
        string candidateId,
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        string sourceId,
        string sourceWorkId,
        string sourceEpisode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWorkId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEpisode);
        return TryClaimWinnerCoreAsync(
            batchId,
            candidateId,
            utcNow,
            leaseDuration,
            sourceId.Trim().ToLowerInvariant(),
            sourceWorkId.Trim(),
            sourceEpisode.Trim(),
            cancellationToken);
    }

    public async Task<bool> TryRecordCompletedWinnerAsync(
        string batchId,
        string candidateId,
        DateTimeOffset utcNow,
        string sourceId,
        string sourceWorkId,
        string sourceEpisode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWorkId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEpisode);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var match = await TryAuditCompletionAsync(
            connection,
            transaction,
            batchId,
            candidateId,
            utcNow,
            sourceId.Trim().ToLowerInvariant(),
            sourceWorkId.Trim(),
            sourceEpisode.Trim(),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return match is not null;
    }

    private async Task<MikanRssWinnerClaimResult> TryClaimWinnerCoreAsync(
        string batchId,
        string candidateId,
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        string? sourceId,
        string? sourceWorkId,
        string? sourceEpisode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        var token = Guid.NewGuid().ToString("N");
        var expires = utcNow.Add(leaseDuration);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);

        if (sourceId is not null && sourceWorkId is not null && sourceEpisode is not null)
        {
            var completion = await TryAuditCompletionAsync(
                connection,
                transaction,
                batchId,
                candidateId,
                utcNow,
                sourceId,
                sourceWorkId,
                sourceEpisode,
                cancellationToken).ConfigureAwait(false);
            if (completion is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new MikanRssWinnerClaimResult(
                    MikanRssWinnerClaimState.AlreadyCompleted,
                    null,
                    completion.Value.CompletionId,
                    completion.Value.AliasId);
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE mikan_rss_batch_entries
            SET effect_state = 'claimed', claim_token = $token, claim_expires_at_utc = $expires,
                early_completion_id = NULL, early_completion_alias_id = NULL,
                early_completion_checked_at_utc = NULL
            WHERE batch_id = $batch AND candidate_id = $candidate AND decision_kind = 'Winner'
              AND (effect_state = 'ready'
                   OR (effect_state = 'claimed' AND claim_expires_at_utc <= $now));
            """;
        command.Parameters.AddWithValue("$batch", batchId);
        command.Parameters.AddWithValue("$candidate", candidateId);
        command.Parameters.AddWithValue("$token", token);
        command.Parameters.AddWithValue("$expires", Format(expires));
        command.Parameters.AddWithValue("$now", Format(utcNow));
        var claimed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return claimed
            ? new MikanRssWinnerClaimResult(
                MikanRssWinnerClaimState.Claimed,
                new MikanRssWinnerLease(batchId, candidateId, token, expires),
                null,
                null)
            : new MikanRssWinnerClaimResult(
                MikanRssWinnerClaimState.Unavailable, null, null, null);
    }

    private static async Task<(string CompletionId, string AliasId)?> TryAuditCompletionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string batchId,
        string candidateId,
        DateTimeOffset utcNow,
        string sourceId,
        string sourceWorkId,
        string sourceEpisode,
        CancellationToken cancellationToken)
    {
        string? completionId = null;
        string? aliasId = null;
        await using (var duplicate = connection.CreateCommand())
        {
            duplicate.Transaction = transaction;
            duplicate.CommandText = """
                SELECT alias.completion_id, alias.id
                FROM completion_aliases AS alias
                JOIN completion_records AS completion ON completion.id = alias.completion_id
                WHERE alias.source_id = $source_id
                  AND alias.source_work_id = $source_work_id
                  AND alias.source_episode = $source_episode
                ORDER BY completion.completed_at_utc, alias.created_at_utc, alias.id
                LIMIT 1;
                """;
            duplicate.Parameters.AddWithValue("$source_id", sourceId);
            duplicate.Parameters.AddWithValue("$source_work_id", sourceWorkId);
            duplicate.Parameters.AddWithValue("$source_episode", sourceEpisode);
            await using var reader = await duplicate.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                completionId = reader.GetString(0);
                aliasId = reader.GetString(1);
            }
        }

        if (completionId is null || aliasId is null)
        {
            return null;
        }

        await using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = """
            UPDATE mikan_rss_batch_entries
            SET early_completion_id = $completion_id,
                early_completion_alias_id = $alias_id,
                early_completion_checked_at_utc = $checked_at
            WHERE batch_id = $batch AND candidate_id = $candidate
              AND decision_kind = 'Winner'
              AND (effect_state = 'ready'
                   OR (effect_state = 'claimed' AND claim_expires_at_utc <= $now));
            """;
        audit.Parameters.AddWithValue("$completion_id", completionId);
        audit.Parameters.AddWithValue("$alias_id", aliasId);
        audit.Parameters.AddWithValue("$checked_at", Format(utcNow));
        audit.Parameters.AddWithValue("$batch", batchId);
        audit.Parameters.AddWithValue("$candidate", candidateId);
        audit.Parameters.AddWithValue("$now", Format(utcNow));
        return await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1
            ? (completionId, aliasId)
            : null;
    }

    public async Task<MikanRssBatchRecord> SetBangumiDiscoveryAsync(
        string batchId,
        MikanBangumiDiscovery discovery,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentNullException.ThrowIfNull(discovery);
        ValidateDiscovery(discovery);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE mikan_rss_batches
            SET bangumi_subject_id = $bgmid,
                bangumi_discovery_state = $state,
                bangumi_discovery_failure_code = $failure
            WHERE id = $id AND bangumi_discovery_state <> 'resolved';
            """;
        command.Parameters.AddWithValue("$id", batchId);
        command.Parameters.AddWithValue("$bgmid", PositiveOrNull(discovery.BangumiSubjectId));
        command.Parameters.AddWithValue("$state", discovery.State);
        command.Parameters.AddWithValue("$failure", (object?)discovery.FailureCode ?? DBNull.Value);
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(batchId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Mikan RSS batch was not found.");
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
                    decision_kind, decision_reason, winner_candidate_id,
                    legacy_filter_state, legacy_filter_reason, legacy_filter_scope, legacy_filter_key,
                    identity_mikanid, identity_groupid, effect_state)
                VALUES ($batch, $candidate, $ordinal, $title, $mikan_url, $torrent, $content_type,
                    $length, $published, $kind, $episode, $decision, $reason, $winner,
                    $filter_state, $filter_reason, $filter_scope, $filter_key,
                    $identity_mikanid, $identity_groupid, $state);
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
            insert.Parameters.AddWithValue("$filter_state", item.LegacyFilterAudit.State.ToString());
            insert.Parameters.AddWithValue("$filter_reason", item.LegacyFilterAudit.Reason);
            insert.Parameters.AddWithValue("$filter_scope", (object?)item.LegacyFilterAudit.MatchedScope ?? DBNull.Value);
            insert.Parameters.AddWithValue("$filter_key", (object?)item.LegacyFilterAudit.MatchedKey ?? DBNull.Value);
            insert.Parameters.AddWithValue("$identity_mikanid", PositiveOrNull(item.LegacyFilterAudit.IdentityMikanId));
            insert.Parameters.AddWithValue("$identity_groupid", PositiveOrNull(item.LegacyFilterAudit.IdentityGroupId));
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
        long legacyRevision;
        bool legacyEnabled;
        int? bangumiSubjectId;
        string bangumiDiscoveryState;
        string? bangumiDiscoveryFailureCode;
        DateTimeOffset created;
        await using (var root = connection.CreateCommand())
        {
            root.CommandText = $"""
                SELECT id, source_profile_id, rule_revision, fingerprint, mikanid,
                       priority_enabled, created_at_utc, legacy_filter_revision, legacy_filter_enabled,
                       bangumi_subject_id, bangumi_discovery_state, bangumi_discovery_failure_code
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
            legacyRevision = reader.GetInt64(7);
            legacyEnabled = reader.GetBoolean(8);
            bangumiSubjectId = reader.IsDBNull(9) ? null : reader.GetInt32(9);
            bangumiDiscoveryState = reader.GetString(10);
            bangumiDiscoveryFailureCode = reader.IsDBNull(11) ? null : reader.GetString(11);
        }

        var storedRows = new List<StoredEntryRow>();
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT candidate_id, title, mikan_url, torrent_url_fingerprint, content_type,
                       length_bytes, published_date, source_episode_kind, source_episode,
                       decision_kind, decision_reason, winner_candidate_id,
                       legacy_filter_state, legacy_filter_reason, legacy_filter_scope, legacy_filter_key,
                       identity_mikanid, identity_groupid, effect_state, ingest_task_id,
                       early_completion_id, early_completion_alias_id, early_completion_checked_at_utc
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
                    rows.GetString(12), rows.GetString(13), rows.IsDBNull(14) ? null : rows.GetString(14),
                    rows.IsDBNull(15) ? null : rows.GetString(15), rows.IsDBNull(16) ? null : rows.GetInt32(16),
                    rows.IsDBNull(17) ? null : rows.GetInt32(17), rows.GetString(18),
                    rows.IsDBNull(19) ? null : rows.GetString(19),
                    rows.IsDBNull(20) ? null : rows.GetString(20),
                    rows.IsDBNull(21) ? null : rows.GetString(21),
                    rows.IsDBNull(22)
                        ? null
                        : DateTimeOffset.Parse(rows.GetString(22), CultureInfo.InvariantCulture)));
            }
        }

        var entries = new List<MikanRssBatchEntryRecord>(storedRows.Count);
        foreach (var row in storedRows)
        {
            var groups = await ReadGroupsAsync(connection, id, row.CandidateId, cancellationToken).ConfigureAwait(false);
            var decision = new MikanRssDecision(row.CandidateId,
                Enum.Parse<MikanRssDecisionKind>(row.DecisionKind), row.DecisionReason,
                row.WinnerCandidateId, groups);
            var filterAudit = new MikanLegacyFilterAudit(
                Enum.Parse<MikanLegacyFilterState>(row.LegacyFilterState),
                row.LegacyFilterReason,
                row.LegacyFilterScope,
                row.LegacyFilterKey,
                row.IdentityMikanId,
                row.IdentityGroupId);
            entries.Add(new MikanRssBatchEntryRecord(
                row.CandidateId, row.Title, row.MikanUrl, row.TorrentUrlFingerprint,
                row.ContentType, row.LengthBytes, row.PublishedDate, row.SourceEpisodeKind,
                row.SourceEpisode, decision, filterAudit, row.EffectState, row.IngestTaskId,
                row.EarlyCompletionId, row.EarlyCompletionAliasId, row.EarlyCompletionCheckedAtUtc));
        }

        return new MikanRssBatchRecord(
            id, profile, revision, fingerprint, mikanId, enabled,
            legacyRevision, legacyEnabled,
            new MikanBangumiDiscovery(
                bangumiSubjectId, bangumiDiscoveryState, bangumiDiscoveryFailureCode),
            created, entries);
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
        var value = new StringBuilder("v2|")
            .Append(profile).Append('|').Append(revision).Append('|').Append(enabled)
            .Append('|').Append(plan.MikanId)
            .Append('|').Append(plan.LegacyFilterRevision).Append('|').Append(plan.LegacyFilterEnabled).Append('|');
        foreach (var item in plan.Items)
        {
            value.Append(item.Candidate.Id).Append('|').Append(item.Decision.Kind).Append('|')
                .Append(item.Decision.Reason).Append('|').Append(item.Decision.WinnerId).Append('|')
                .Append(item.LegacyFilterAudit.State).Append('|').Append(item.LegacyFilterAudit.Reason).Append('|')
                .Append(item.LegacyFilterAudit.MatchedScope).Append('|').Append(item.LegacyFilterAudit.MatchedKey).Append('|')
                .Append(item.LegacyFilterAudit.IdentityMikanId).Append('|').Append(item.LegacyFilterAudit.IdentityGroupId)
                .Append(';');
        }
        return Sha256(value.ToString());
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static object PositiveOrNull(int? value) => value is > 0 ? value.Value : DBNull.Value;

    private static void ValidateDiscovery(MikanBangumiDiscovery discovery)
    {
        var valid = discovery.State switch
        {
            MikanBangumiDiscoveryStates.Resolved =>
                discovery.BangumiSubjectId is > 0 && discovery.FailureCode is null,
            MikanBangumiDiscoveryStates.NotAttempted =>
                discovery.BangumiSubjectId is null && discovery.FailureCode is null,
            MikanBangumiDiscoveryStates.NotFound
                or MikanBangumiDiscoveryStates.Failed
                or MikanBangumiDiscoveryStates.NotApplicable =>
                discovery.BangumiSubjectId is null
                && !string.IsNullOrWhiteSpace(discovery.FailureCode)
                && discovery.FailureCode.Length <= 128,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException("Mikan Bangumi discovery result is invalid.", nameof(discovery));
        }
    }

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
        string LegacyFilterState,
        string LegacyFilterReason,
        string? LegacyFilterScope,
        string? LegacyFilterKey,
        int? IdentityMikanId,
        int? IdentityGroupId,
        string EffectState,
        string? IngestTaskId,
        string? EarlyCompletionId,
        string? EarlyCompletionAliasId,
        DateTimeOffset? EarlyCompletionCheckedAtUtc);
}
