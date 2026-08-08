using System.Globalization;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Feeds;

public sealed record MikanRssTaskEvidenceProjection(
    string BatchId,
    int EntryOrdinal,
    string SourceProfileId,
    long RuleRevision,
    bool PriorityEnabled,
    long LegacyFilterRevision,
    bool LegacyFilterEnabled,
    int? MikanId,
    string? SourceEpisodeKind,
    string? SourceEpisode,
    string DecisionKind,
    string DecisionReason,
    IReadOnlyList<string> EvaluatedPriorityGroups,
    string LegacyFilterState,
    string LegacyFilterReason,
    string? LegacyFilterScope,
    int? IdentityMikanId,
    int? IdentityGroupId,
    string EffectState,
    DateTimeOffset BatchCreatedAtUtc);

public sealed class MikanRssTaskEvidenceStore(AnimeGoSqliteDatabase database)
{
    public async Task<IReadOnlyList<MikanRssTaskEvidenceProjection>> ListForTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<Row>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT batch.id, entry.candidate_id, entry.ordinal, batch.source_profile_id,
                       batch.rule_revision, batch.priority_enabled,
                       batch.legacy_filter_revision, batch.legacy_filter_enabled,
                       batch.mikanid, entry.source_episode_kind, entry.source_episode,
                       entry.decision_kind, entry.decision_reason,
                       entry.legacy_filter_state, entry.legacy_filter_reason,
                       entry.legacy_filter_scope, entry.identity_mikanid,
                       entry.identity_groupid, entry.effect_state, batch.created_at_utc
                FROM mikan_rss_batch_entries AS entry
                JOIN mikan_rss_batches AS batch ON batch.id = entry.batch_id
                WHERE entry.ingest_task_id = $task_id
                ORDER BY batch.created_at_utc, batch.id, entry.ordinal;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new Row(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    reader.GetBoolean(5),
                    reader.GetInt64(6),
                    reader.GetBoolean(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15),
                    reader.IsDBNull(16) ? null : reader.GetInt32(16),
                    reader.IsDBNull(17) ? null : reader.GetInt32(17),
                    reader.GetString(18),
                    DateTimeOffset.Parse(
                        reader.GetString(19),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind)));
            }
        }

        if (rows.Count == 0)
        {
            return [];
        }

        var groups = new Dictionary<(string BatchId, string CandidateId), List<string>>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT decision.batch_id, decision.candidate_id, decision.group_id
                FROM mikan_rss_decision_groups AS decision
                JOIN mikan_rss_batch_entries AS entry
                  ON entry.batch_id = decision.batch_id
                 AND entry.candidate_id = decision.candidate_id
                JOIN mikan_rss_batches AS batch ON batch.id = entry.batch_id
                WHERE entry.ingest_task_id = $task_id
                ORDER BY batch.created_at_utc, batch.id, entry.ordinal, decision.position;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = (reader.GetString(0), reader.GetString(1));
                if (!groups.TryGetValue(key, out var values))
                {
                    values = [];
                    groups.Add(key, values);
                }

                values.Add(reader.GetString(2));
            }
        }

        return rows.Select(row => new MikanRssTaskEvidenceProjection(
            row.BatchId,
            row.EntryOrdinal,
            row.SourceProfileId,
            row.RuleRevision,
            row.PriorityEnabled,
            row.LegacyFilterRevision,
            row.LegacyFilterEnabled,
            row.MikanId,
            row.SourceEpisodeKind,
            row.SourceEpisode,
            row.DecisionKind,
            row.DecisionReason,
            groups.TryGetValue((row.BatchId, row.CandidateId), out var values)
                ? values
                : [],
            row.LegacyFilterState,
            row.LegacyFilterReason,
            row.LegacyFilterScope,
            row.IdentityMikanId,
            row.IdentityGroupId,
            row.EffectState,
            row.BatchCreatedAtUtc)).ToArray();
    }

    private sealed record Row(
        string BatchId,
        string CandidateId,
        int EntryOrdinal,
        string SourceProfileId,
        long RuleRevision,
        bool PriorityEnabled,
        long LegacyFilterRevision,
        bool LegacyFilterEnabled,
        int? MikanId,
        string? SourceEpisodeKind,
        string? SourceEpisode,
        string DecisionKind,
        string DecisionReason,
        string LegacyFilterState,
        string LegacyFilterReason,
        string? LegacyFilterScope,
        int? IdentityMikanId,
        int? IdentityGroupId,
        string EffectState,
        DateTimeOffset BatchCreatedAtUtc);
}
