using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

[assembly: InternalsVisibleTo("HavenOS.Apps.Assistants.Memory")]

namespace Haven.Infrastructure;

public sealed partial class KnowledgeLibraryService
{
    public const string AssistantMemoryApplicationId = "9to1.assistants";

    /// <summary>Pure original-store pairing only; neither identity nor this result grants reads.</summary>
    public bool HasOriginalAssistantMemoryStoreOwner(CanonicalSqliteOriginalStoreOwner owner) =>
        owner is not null && owner.HasOriginalDatabase(factory);

    // Only the trusted product adapter may request this private selection, after actual
    // issuer/member/Home authorization. A public record ID or scope string cannot call the reader.
    internal sealed class AssistantOriginalReadScope
    {
        internal AssistantOriginalReadScope(KnowledgeLibraryService library, CanonicalSqliteOriginalStoreOwner store,
            string definitionId, string storageScope, bool allowVerifiedLegacy)
        { Library = library; Store = store; DefinitionId = definitionId; StorageScope = storageScope; AllowVerifiedLegacy = allowVerifiedLegacy; }
        internal KnowledgeLibraryService Library { get; }
        internal CanonicalSqliteOriginalStoreOwner Store { get; }
        internal string DefinitionId { get; }
        internal string StorageScope { get; }
        internal bool AllowVerifiedLegacy { get; }
    }
    private readonly ConditionalWeakTable<AssistantOriginalReadScope, object> _assistantMemoryScopes = new();
    internal AssistantOriginalReadScope CreateOriginalAssistantMemoryReadScope(CanonicalSqliteOriginalStoreOwner store,
        string actualDefinitionId, string fullStorageScope, bool allowVerifiedLegacy)
    {
        if (!HasOriginalAssistantMemoryStoreOwner(store) || string.IsNullOrWhiteSpace(actualDefinitionId) || string.IsNullOrWhiteSpace(fullStorageScope))
            throw new UnauthorizedAccessException("The actual canonical Assistant memory scope is unavailable.");
        var scope = new AssistantOriginalReadScope(this, store, actualDefinitionId, fullStorageScope, allowVerifiedLegacy);
        _assistantMemoryScopes.Add(scope, new()); return scope;
    }

    /// <summary>Canonical bounded Learn Me query over an already protected actual lease.
    /// The product owner must separately verify its live membership and Home assistant.memory
    /// receipt before calling. The identifier narrows that admitted read; it grants nothing.
    /// This method never opens another connection, initializes schema or queries global memory.</summary>
    internal async Task<IReadOnlyList<KnowledgeRecord>> GetActiveAssistantLearnMeWithinSourceAsync(
        CanonicalSqliteOriginalStoreLease originalLease, AssistantOriginalReadScope scope, int limit,
        CancellationToken token)
    {
        DemandOriginalAssistantMemoryReadScope(originalLease, scope);
        if (limit < 1 || limit > MemoryInjection.MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(limit), "An admitted Assistant identity and bounded memory limit are required.");
        return await ReadAssistantMemoryRowsAsync<IReadOnlyList<KnowledgeRecord>>(originalLease, command =>
        {
            ConfigureOriginalAssistantMemorySelect(command, scope, DateTimeOffset.UtcNow.ToString("O"));
            command.CommandText += " ORDER BY r.is_pinned DESC,r.confidence DESC,COALESCE(d.freshness,0) DESC,r.updated_at DESC,r.id LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", limit);
        }, async reader =>
        {
            var records = new List<KnowledgeRecord>();
            while (await originalLease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                records.Add(originalLease.InvokeOriginalSource(() => ReadRecord(reader)));
            return records.AsReadOnly();
        }, token).ConfigureAwait(false);
    }

    private void DemandOriginalAssistantMemoryReadScope(CanonicalSqliteOriginalStoreLease originalLease, AssistantOriginalReadScope scope)
    {
        ArgumentNullException.ThrowIfNull(originalLease);
        if (scope is null || !_assistantMemoryScopes.TryGetValue(scope, out _) || !ReferenceEquals(scope.Library, this) ||
            !ReferenceEquals(scope.Store, originalLease.OriginalOwner) || !HasOriginalAssistantMemoryStoreOwner(originalLease.OriginalOwner) ||
            !originalLease.OriginalOwner.IsIssuedOriginalLease(originalLease))
            throw new UnauthorizedAccessException("The Knowledge Library requires its SAME protected canonical SQLite lease.");
    }
    private static void ConfigureOriginalAssistantMemorySelect(SqliteCommand command, AssistantOriginalReadScope scope, string now)
    {
        command.CommandText = """
                SELECT r.id,r.category,r.topic,r.title,r.summary,r.privacy_class,r.confidence,r.is_pinned,
                       r.created_at,r.updated_at,r.expires_at,r.learned_because,r.sources_json,
                       COALESCE(d.freshness,0),d.last_confirmed_at,d.scope,
                       COALESCE(d.status,0),COALESCE(d.origin,0),d.user_correction,d.supersedes_id,
                       d.knowledge_bank_id,d.last_reinforced_at,d.last_used_at,COALESCE(d.is_user_locked,0),d.app_id,d.project_id,d.agent_id
                FROM knowledge_records r
                JOIN knowledge_record_details d ON d.id=r.id
                WHERE r.category=$category AND d.agent_id=$agent
                  AND (d.scope=$scope OR ($legacy=1 AND d.scope='agent'))
                  AND (d.app_id IS NULL OR d.app_id=$app)
                  AND d.project_id IS NULL AND d.knowledge_bank_id IS NULL
                  AND r.privacy_class IN ($normal,$private)
                  AND COALESCE(d.status,0) IN ($active,$corrected)
                  AND (r.expires_at IS NULL OR r.expires_at>$now)
            """;
            command.Parameters.AddWithValue("$category", (int)KnowledgeCategory.LearnMe);
            command.Parameters.AddWithValue("$agent", scope.DefinitionId);
            command.Parameters.AddWithValue("$scope", scope.StorageScope);
            command.Parameters.AddWithValue("$legacy", scope.AllowVerifiedLegacy ? 1 : 0);
            command.Parameters.AddWithValue("$app", AssistantMemoryApplicationId);
            command.Parameters.AddWithValue("$normal", (int)KnowledgePrivacyClass.Normal);
            command.Parameters.AddWithValue("$private", (int)KnowledgePrivacyClass.Private);
            command.Parameters.AddWithValue("$active", (int)KnowledgeRecordStatus.Active);
            command.Parameters.AddWithValue("$corrected", (int)KnowledgeRecordStatus.Corrected);
            command.Parameters.AddWithValue("$now", now);
    }

    private static async Task<T> ReadAssistantMemoryRowsAsync<T>(CanonicalSqliteOriginalStoreLease lease,
        Action<SqliteCommand> configure, Func<SqliteDataReader, Task<T>> consume, CancellationToken token)
    {
        SqliteCommand? command = null; SqliteDataReader? reader = null;
        Task<SqliteDataReader>? rawReader = null; T result = default!; var errors = new List<Exception>();
        try
        {
            // Capture handles INSIDE the callback, before a caller postguard can fail.
            lease.InvokeOriginalSource(() => { command = lease.Connection.CreateCommand(); return command; });
            lease.InvokeOriginalSource(() => { command!.Transaction = lease.Transaction; configure(command); });
            try { reader = await lease.ReadOriginalSourceAsync(() => rawReader = command!.ExecuteReaderAsync(token)).ConfigureAwait(false); }
            catch (Exception)
            {
                if (rawReader is { IsCompletedSuccessfully: true }) reader = rawReader.Result;
                throw;
            }
            result = await consume(reader!).ConfigureAwait(false);
        }
        catch (Exception failure) { errors.Add(failure); }
        if (reader is not null) await CloseAssistantMemoryResourceAsync(lease, reader, errors).ConfigureAwait(false);
        if (command is not null) await CloseAssistantMemoryResourceAsync(lease, command, errors).ConfigureAwait(false);
        ThrowAssistantMemoryErrors(errors); return result;
    }
    private static async Task CloseAssistantMemoryResourceAsync(CanonicalSqliteOriginalStoreLease lease,
        IAsyncDisposable resource, List<Exception> errors)
    {
        try { await lease.CloseOriginalResourceAsync(resource).ConfigureAwait(false); }
        catch (Exception failure) { errors.Add(failure); }
    }
    private static void ThrowAssistantMemoryErrors(List<Exception> errors)
    {
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("The original scoped Knowledge Library read or cleanup failed.", errors);
    }
}
