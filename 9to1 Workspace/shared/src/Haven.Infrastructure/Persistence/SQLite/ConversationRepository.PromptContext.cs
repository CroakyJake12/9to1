using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class ConversationRepository : IConversationPromptContextRepository
{
    public Task<ConversationPromptContext> GetPromptContextAsync(Guid conversationId, CancellationToken cancellationToken) =>
        ReadPromptContextAsync(conversationId, null, cancellationToken);

    private async Task<ConversationPromptContext> ReadPromptContextAsync(Guid conversationId, Guid? boundBranchId,
        CancellationToken cancellationToken)
    {
        if (conversationId == Guid.Empty) throw new ArgumentException("A conversation identity is required.", nameof(conversationId));
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var branchId = boundBranchId ?? await GetCurrentBranchIdAsync(connection, transaction, conversationId, cancellationToken).ConfigureAwait(false);
        var context = await ReadPromptContextWithinTransactionAsync(connection, transaction, conversationId, branchId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return context;
    }

    private static async Task<ConversationPromptContext> ReadPromptContextWithinTransactionAsync(SqliteConnection connection,
        SqliteTransaction transaction, Guid conversationId, Guid? branchId, CancellationToken cancellationToken)
    {
        // A bound invocation may read its admitted branch after selection changes. It may
        // never read another conversation, nor fall back to all branches if its branch vanished.
        await using (var branchCheck = connection.CreateCommand())
        {
            branchCheck.Transaction = transaction;
            branchCheck.CommandText = branchId is null
                ? "SELECT COUNT(*) FROM conversation_branches WHERE conversation_id=$conversation;"
                : "SELECT COUNT(*) FROM conversation_branches WHERE conversation_id=$conversation AND id=$branch;";
            branchCheck.Parameters.AddWithValue("$conversation", conversationId.ToString());
            if (branchId is { } exactBranch) branchCheck.Parameters.AddWithValue("$branch", exactBranch.ToString());
            var count = Convert.ToInt32(await branchCheck.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (branchId is null ? count != 0 : count != 1)
                throw new InvalidOperationException("The original conversation branch is unavailable for context projection.");
        }
        var history = await ReadBranchMessagesAsync(connection, transaction, conversationId, branchId,
            includeCompacted: true, cancellationToken).ConfigureAwait(false);
        if (history.Any(message => message.ConversationId != conversationId))
            throw new InvalidOperationException("The original branch contains another conversation's source.");
        var versions = await ReadEffectiveSourceVersionsAsync(connection, transaction, branchId, history, cancellationToken).ConfigureAwait(false);
        var byId = history.ToDictionary(message => message.Id);
        var versionById = versions.ToDictionary(version => version.MessageId, version => version.VersionId);
        var candidates = new Dictionary<Guid, StoredCompaction>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT e.id,e.conversation_id,e.kind,e.title,e.content,e.evidence,e.created_at,
                       c.source_branch_id,c.source_count,c.summary_sha256,
                       s.message_id,s.source_order,s.message_sha256,s.effective_version_id
                  FROM conversation_context e
                  JOIN conversation_compactions c ON c.summary_id=e.id AND c.conversation_id=e.conversation_id
                  LEFT JOIN conversation_compaction_sources s ON s.summary_id=c.summary_id
                 WHERE e.conversation_id=$conversation AND e.kind=$kind
                 ORDER BY e.created_at DESC,e.id,s.source_order;
                """;
            command.Parameters.AddWithValue("$conversation", conversationId.ToString());
            command.Parameters.AddWithValue("$kind", (int)ContextEntryKind.CompactSummary);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.Guid("id");
                if (!candidates.TryGetValue(id, out var candidate))
                {
                    var entry = new ConversationContextEntry(id, reader.Guid("conversation_id"), (ContextEntryKind)reader.Int32("kind"),
                        reader.String("title"), reader.String("content"), reader.String("evidence"), reader.DateTimeOffset("created_at"));
                    candidates.Add(id, candidate = new(entry, reader.NullableGuid("source_branch_id"),
                        reader.Int32("source_count"), reader.String("summary_sha256"), []));
                }
                if (!reader.IsDBNull(reader.GetOrdinal("message_id")))
                    candidate.Sources.Add(new(reader.Guid("message_id"), reader.Int32("source_order"),
                        reader.String("message_sha256"), reader.NullableGuid("effective_version_id")));
            }
        }

        var applicable = new List<ConversationCompaction>();
        var covered = new HashSet<Guid>();
        // Prefer a newer complete replacement if independently compacted branches overlap.
        // Never partially apply a summary: it might contain the excluded branch-only content.
        foreach (var candidate in candidates.Values.OrderByDescending(item => item.Summary.CreatedAt).ThenBy(item => item.Summary.Id))
        {
            if (string.IsNullOrWhiteSpace(candidate.Summary.Content) || candidate.SourceCount <= 0 ||
                candidate.Sources.Count != candidate.SourceCount ||
                candidate.Sources.Select(source => source.MessageId).Distinct().Count() != candidate.SourceCount ||
                !candidate.Sources.Select(source => source.Order).SequenceEqual(Enumerable.Range(0, candidate.SourceCount)) ||
                candidate.SummarySha256 != SummaryFingerprint(candidate.Summary) ||
                candidate.Sources.Any(source => covered.Contains(source.MessageId) ||
                    !byId.TryGetValue(source.MessageId, out var actual) ||
                    !actual.IsCompacted || actual.Role is not (MessageRole.User or MessageRole.Assistant) ||
                    source.MessageSha256 != MessageFingerprint(actual) ||
                    !versionById.TryGetValue(source.MessageId, out var versionId) || versionId != source.VersionId))
                continue; // Unscoped, incomplete or changed replacements retain every raw source.
            var originals = candidate.Sources.Select(source => byId[source.MessageId] with { IsCompacted = false }).ToArray();
            applicable.Add(new(candidate.Summary, originals)
            {
                SourceBranchId = candidate.SourceBranchId,
                SourceVersions = candidate.Sources.Select(source => new ConversationSourceVersion(source.MessageId, source.VersionId)).ToArray()
            });
            covered.UnionWith(originals.Select(message => message.Id));
        }
        return new(conversationId, branchId, history, versions,
            applicable.OrderBy(compaction => compaction.Summary.CreatedAt).ThenBy(compaction => compaction.Summary.Id).ToArray());
    }

    private static async Task<IReadOnlyList<ConversationSourceVersion>> ReadEffectiveSourceVersionsAsync(SqliteConnection connection,
        SqliteTransaction transaction, Guid? branchId, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
    {
        if (branchId is null) return history.Select(message => new ConversationSourceVersion(message.Id, null)).ToArray();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH RECURSIVE ancestry(id,depth) AS (
                SELECT $branch,0
                UNION ALL
                SELECT b.parent_branch_id,ancestry.depth+1 FROM conversation_branches b
                  JOIN ancestry ON b.id=ancestry.id WHERE b.parent_branch_id IS NOT NULL
            )
            SELECT bm.message_id,
                   (SELECT v.id FROM message_versions v JOIN ancestry a ON a.id=v.branch_id
                     WHERE v.message_id=bm.message_id AND v.is_current=1 ORDER BY a.depth LIMIT 1) AS version_id
              FROM conversation_branch_messages bm WHERE bm.branch_id=$branch ORDER BY bm.sequence;
            """;
        command.Parameters.AddWithValue("$branch", branchId.Value.ToString());
        var versions = new List<ConversationSourceVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            versions.Add(new(reader.Guid("message_id"), reader.NullableGuid("version_id")));
        return versions;
    }

    private static async Task InsertCompactionCoverageAsync(SqliteConnection connection, SqliteTransaction transaction,
        ConversationCompaction compaction, CancellationToken cancellationToken)
    {
        var versions = compaction.SourceVersions?.ToDictionary(version => version.MessageId, version => version.VersionId)
            ?? throw new InvalidOperationException("Persisted compaction requires the original effective source versions.");
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO conversation_compactions(summary_id,conversation_id,source_branch_id,source_count,summary_sha256)
                VALUES($summary,$conversation,$branch,$count,$hash);
                """;
            command.Parameters.AddWithValue("$summary", compaction.Summary.Id.ToString());
            command.Parameters.AddWithValue("$conversation", compaction.Summary.ConversationId.ToString());
            command.Parameters.AddWithValue("$branch", (object?)compaction.SourceBranchId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$count", compaction.OriginalMessages.Count);
            command.Parameters.AddWithValue("$hash", SummaryFingerprint(compaction.Summary));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        for (var index = 0; index < compaction.OriginalMessages.Count; index++)
        {
            var source = compaction.OriginalMessages[index];
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO conversation_compaction_sources(summary_id,message_id,source_order,message_sha256,effective_version_id)
                VALUES($summary,$message,$order,$hash,$version);
                """;
            command.Parameters.AddWithValue("$summary", compaction.Summary.Id.ToString());
            command.Parameters.AddWithValue("$message", source.Id.ToString());
            command.Parameters.AddWithValue("$order", index);
            command.Parameters.AddWithValue("$hash", MessageFingerprint(source));
            command.Parameters.AddWithValue("$version", (object?)versions[source.Id]?.ToString() ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string MessageFingerprint(ChatMessage message) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        Schema = "9to1.compaction-source/v1", message.Id, message.ConversationId, message.Role, message.Content,
        message.AgentName, message.ModelName, message.MetadataJson, CreatedAt = message.CreatedAt.ToUniversalTime()
    })));

    private static string SummaryFingerprint(ConversationContextEntry entry) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        Schema = "9to1.compaction-summary/v1", entry.Id, entry.ConversationId, entry.Kind, entry.Title, entry.Content,
        entry.Evidence, CreatedAt = entry.CreatedAt.ToUniversalTime()
    })));

    private sealed record StoredCompaction(ConversationContextEntry Summary, Guid? SourceBranchId, int SourceCount,
        string SummarySha256, List<StoredCompactionSource> Sources);
    private sealed record StoredCompactionSource(Guid MessageId, int Order, string MessageSha256, Guid? VersionId);
}
