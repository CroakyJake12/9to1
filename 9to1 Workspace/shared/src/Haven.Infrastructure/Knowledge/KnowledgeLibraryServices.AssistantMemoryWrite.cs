using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class KnowledgeLibraryService
{
    internal sealed class AssistantMemoryCreatePreparation(KnowledgeLibraryService library,
        AssistantOriginalReadScope selection, KnowledgeRecord record, string sourcesJson, int estimatedBytes,
        RetrievalIndexService index, RetrievalIndexService.AssistantMemoryIndexPreparation indexPreparation)
    {
        internal KnowledgeLibraryService Library => library;
        internal AssistantOriginalReadScope Selection => selection;
        internal KnowledgeRecord Record => record;
        internal string SourcesJson => sourcesJson;
        internal int EstimatedBytes => estimatedBytes;
        internal RetrievalIndexService Index => index;
        internal RetrievalIndexService.AssistantMemoryIndexPreparation IndexPreparation => indexPreparation;
        internal bool Consumed;
    }
    private readonly ConditionalWeakTable<AssistantMemoryCreatePreparation, object> _assistantCreates = new();

    internal bool HasOriginalLocalAssistantMemoryWriter(CanonicalSqliteOriginalStoreOwner store) =>
        HasOriginalAssistantMemoryStoreOwner(store) && retrieval is RetrievalIndexService actual &&
        actual.HasOriginalLocalAssistantMemoryComposition(store);

    internal Task<AssistantMemoryCreatePreparation> PrepareOriginalAssistantMemoryCreateAsync(
        AssistantOriginalReadScope selection, KnowledgeRecord candidate,
        CanonicalSqliteOriginalSourceScope source, CancellationToken token) =>
        PrepareOriginalAssistantMemoryCandidateAsync(selection, candidate, source, false, token);

    internal async Task<AssistantMemoryCreatePreparation> PrepareOriginalAssistantMemoryCandidateAsync(
        AssistantOriginalReadScope selection, KnowledgeRecord candidate,
        CanonicalSqliteOriginalSourceScope source, bool correction, CancellationToken token)
    {
        if (!_assistantMemoryScopes.TryGetValue(selection, out _) || !ReferenceEquals(selection.Library, this) ||
            !HasOriginalLocalAssistantMemoryWriter(selection.Store))
            throw new UnauthorizedAccessException("The original scoped Knowledge selection and configured local index are required.");
        source.Run(() =>
        {
            if (candidate.Id == Guid.Empty || candidate.Category != KnowledgeCategory.LearnMe ||
                candidate.Origin != KnowledgeOrigin.Explicit || candidate.Status != (correction ? KnowledgeRecordStatus.Corrected : KnowledgeRecordStatus.Active) ||
                candidate.PrivacyClass != KnowledgePrivacyClass.Private || candidate.Confidence != 1 ||
                candidate.Scope != selection.StorageScope || candidate.AgentId != selection.DefinitionId ||
                candidate.AppId != AssistantMemoryApplicationId || candidate.ProjectId is not null || candidate.KnowledgeBankId is not null ||
                (correction ? candidate.UserCorrection is null || candidate.SupersedesId is null : candidate.UserCorrection is not null || candidate.SupersedesId is not null) ||
                (!correction && candidate.ExpiresAt is not null) || candidate.Title.Length is < 1 or > 160 ||
                candidate.Summary.Length is < 1 or > 4000 || candidate.Sources.Count < (correction ? 0 : 1) || candidate.Sources.Count > (correction ? 64 : 1))
                throw new ArgumentException("Only the exact bounded explicit private Assistant memory candidate may be created.");
            KnowledgeContentSafety.ThrowIfContainsSecret(candidate.Topic, candidate.Title, candidate.Summary,
                candidate.LearnedBecause, candidate.Scope);
            foreach (var originalSource in candidate.Sources)
                KnowledgeContentSafety.ThrowIfContainsSecret(originalSource.Title, originalSource.SourceId);
        });
        var record = candidate with { Sources = Array.AsReadOnly(candidate.Sources.ToArray()) };
        var sourcesJson = JsonSerializer.Serialize(record.Sources, JsonOptions);
        var estimated = KnowledgeContentSafety.Utf8Bytes(record.Topic, record.Title, record.Summary,
            record.LearnedBecause, sourcesJson, record.Summary, record.Scope, record.UserCorrection) + 256;
        var index = (RetrievalIndexService)retrieval;
        // Uses the SAME configured normalizer, chunker and local embedding owner before
        // holding the SQLite writer transaction. This stage performs no persistence.
        var preparedIndex = await source.Read(() => index.PrepareOriginalAssistantMemoryIndexAsync(
            selection.Store, record, source, token)).ConfigureAwait(false);
        var prepared = new AssistantMemoryCreatePreparation(this, selection, record, sourcesJson, estimated, index, preparedIndex);
        _assistantCreates.Add(prepared, new()); return prepared;
    }

    internal async Task InsertOriginalAssistantMemoryWithinTransactionAsync(CanonicalSqliteOriginalStoreLease lease,
        AssistantMemoryCreatePreparation preparation, Action demandOriginalWrite, CancellationToken token)
    {
        if (!_assistantCreates.TryGetValue(preparation, out _) || !ReferenceEquals(preparation.Library, this) ||
            !ReferenceEquals(preparation.Selection.Store, lease.OriginalOwner) || !lease.OriginalOwner.IsIssuedOriginalLease(lease))
            throw new UnauthorizedAccessException("The SAME canonical Knowledge preparation and protected writer are required.");
        lock (preparation)
        {
            if (preparation.Consumed) throw new InvalidOperationException("The actual memory preparation cannot be replayed.");
            preparation.Consumed = true;
        }
        var record = preparation.Record;
        var existingBytes = await ExecuteAssistantMemoryScalarAsync(lease, command =>
        {
            demandOriginalWrite();
            ConfigureCanonicalKnowledgeStorageSizeRead(command, record.Id);
        }, token).ConfigureAwait(false);
        if (Convert.ToInt64(existingBytes, System.Globalization.CultureInfo.InvariantCulture) + preparation.EstimatedBytes > KnowledgeStorageLimits.BackgroundLearningBytes)
            throw new InvalidOperationException("The existing Knowledge storage limit does not permit another memory record.");
        await ExecuteAssistantMemoryInsertAsync(lease, command =>
        {
            demandOriginalWrite(); ConfigureCanonicalRecordUpsert(command, record, preparation.SourcesJson);
            RequireCreateOnlyKnowledgeCommand(command);
        }, token).ConfigureAwait(false);
        await ExecuteAssistantMemoryInsertAsync(lease, command =>
        {
            demandOriginalWrite(); ConfigureCanonicalRecordDetailsUpsert(command, record);
            RequireCreateOnlyKnowledgeCommand(command);
        }, token).ConfigureAwait(false);
        await preparation.Index.InsertOriginalAssistantMemoryIndexWithinTransactionAsync(lease,
            preparation.IndexPreparation, demandOriginalWrite, token).ConfigureAwait(false);
    }
    private static void RequireCreateOnlyKnowledgeCommand(SqliteCommand command)
    {
        // Reuse the maintained canonical INSERT and every parameter binder. Removing
        // its fixed conflict-update suffix makes collisions fail inside the SAME txn.
        var conflict = command.CommandText.IndexOf("ON CONFLICT", StringComparison.Ordinal);
        if (conflict < 0) throw new InvalidOperationException("The canonical Knowledge command shape changed.");
        command.CommandText = command.CommandText[..conflict].TrimEnd() + ";";
    }
    private static void ConfigureCanonicalKnowledgeStorageSizeRead(SqliteCommand command, Guid id)
    {
        command.CommandText = """
            SELECT COALESCE(SUM(
                length(r.topic)+length(r.title)+length(r.summary)+length(r.learned_because)+length(r.sources_json)+
                length(COALESCE(d.scope,''))+length(COALESCE(d.user_correction,''))),0)
            FROM knowledge_records r
            LEFT JOIN knowledge_record_details d ON d.id=r.id
            WHERE r.id<>$id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
    }
    internal static async Task<object?> ExecuteAssistantMemoryScalarAsync(CanonicalSqliteOriginalStoreLease lease,
        Action<SqliteCommand> configure, CancellationToken token)
    {
        SqliteCommand? command = null; object? result = null; var errors = new List<Exception>();
        try
        {
            lease.InvokeOriginalSource(() => { command = lease.Connection.CreateCommand(); return command; });
            lease.InvokeOriginalSource(() => { command!.Transaction = lease.Transaction; configure(command); });
            result = await lease.ReadOriginalSourceAsync(() => command!.ExecuteScalarAsync(token)).ConfigureAwait(false);
        }
        catch (Exception failure) { errors.Add(failure); }
        if (command is not null) await CloseAssistantMemoryResourceAsync(lease, command, errors).ConfigureAwait(false);
        ThrowAssistantMemoryErrors(errors); return result;
    }
    internal static async Task ExecuteAssistantMemoryInsertAsync(CanonicalSqliteOriginalStoreLease lease,
        Action<SqliteCommand> configure, CancellationToken token)
    {
        SqliteCommand? command = null; var errors = new List<Exception>();
        try
        {
            lease.InvokeOriginalSource(() => { command = lease.Connection.CreateCommand(); return command; });
            lease.InvokeOriginalSource(() => { command!.Transaction = lease.Transaction; configure(command); });
            if (await lease.ReadOriginalSourceAsync(() => command!.ExecuteNonQueryAsync(token)).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The original create-only memory insert did not create exactly one row.");
        }
        catch (Exception failure) { errors.Add(failure); }
        if (command is not null) await CloseAssistantMemoryResourceAsync(lease, command, errors).ConfigureAwait(false);
        ThrowAssistantMemoryErrors(errors);
    }
}
