using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class KnowledgeLibraryService
{
    internal sealed record AssistantMemoryPagePosition(string CreatedAt, string Id);
    internal sealed record AssistantMemoryRecordPage(IReadOnlyList<KnowledgeRecord> Records, AssistantMemoryPagePosition? Next);

    // Search and continuation only narrow the SAME private original read scope.
    // Cursor values are taken from actual SQL columns, without timestamp reformatting.
    internal async Task<AssistantMemoryRecordPage> ReadAssistantMemoryPageWithinSourceAsync(
        CanonicalSqliteOriginalStoreLease lease, AssistantOriginalReadScope scope, int maximum,
        string search, AssistantMemoryPagePosition? after, CancellationToken token)
    {
        DemandOriginalAssistantMemoryReadScope(lease, scope);
        if (maximum is < 1 or > 64 || search.Length > 256) throw new ArgumentOutOfRangeException(nameof(maximum));
        return await ReadAssistantMemoryRowsAsync(lease, command =>
        {
            ConfigureOriginalAssistantMemorySelect(command, scope, DateTimeOffset.UtcNow.ToString("O"));
            command.CommandText += """
                 AND ($search='' OR instr(lower(r.title),lower($search))>0 OR instr(lower(r.summary),lower($search))>0)
                 AND ($after=0 OR r.created_at<$created OR (r.created_at=$created AND r.id>$id))
                 ORDER BY r.created_at DESC,r.id ASC LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$search", search);
            command.Parameters.AddWithValue("$after", after is null ? 0 : 1);
            command.Parameters.AddWithValue("$created", after?.CreatedAt ?? "");
            command.Parameters.AddWithValue("$id", after?.Id ?? "");
            command.Parameters.AddWithValue("$limit", maximum + 1);
        }, async reader =>
        {
            var records = new List<KnowledgeRecord>(); AssistantMemoryPagePosition? last = null; var more = false;
            while (await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
            {
                if (records.Count == maximum) { more = true; break; }
                lease.InvokeOriginalSource(() =>
                {
                    records.Add(ReadRecord(reader)); last = new(reader.GetString(8), reader.GetString(0)); return true;
                });
            }
            return new AssistantMemoryRecordPage(records.AsReadOnly(), more ? last : null);
        }, token).ConfigureAwait(false);
    }

    private async Task<KnowledgeRecord?> ReadSelectedAssistantMemoryWithinSourceAsync(
        CanonicalSqliteOriginalStoreLease lease, AssistantOriginalReadScope scope, Guid selectedId, CancellationToken token)
    {
        DemandOriginalAssistantMemoryReadScope(lease, scope);
        return await ReadAssistantMemoryRowsAsync<KnowledgeRecord?>(lease, command =>
        {
            ConfigureOriginalAssistantMemorySelect(command, scope, DateTimeOffset.UtcNow.ToString("O"));
            command.CommandText += " AND r.id=$id LIMIT 1;";
            command.Parameters.AddWithValue("$id", selectedId.ToString());
        }, async reader => await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false)
            ? lease.InvokeOriginalSource(() => ReadRecord(reader)) : null, token).ConfigureAwait(false);
    }
}
