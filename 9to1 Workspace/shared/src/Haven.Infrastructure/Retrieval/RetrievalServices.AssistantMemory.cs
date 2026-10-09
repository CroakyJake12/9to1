using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class RetrievalIndexService
{
    // These are preparation/transaction seams on the SAME configured canonical index.
    // Neither a prepared value nor a physical SQLite pin grants a product WRITE.
    internal sealed class AssistantMemoryIndexPreparation(
        RetrievalIndexService owner, CanonicalSqliteOriginalStoreOwner store,
        RetrievalDocument document, IReadOnlyList<RetrievalChunk> chunks)
    {
        internal RetrievalIndexService Owner => owner;
        internal CanonicalSqliteOriginalStoreOwner Store => store;
        internal RetrievalDocument Document => document;
        internal IReadOnlyList<RetrievalChunk> Chunks => chunks;
        internal bool Consumed;
    }
    private readonly ConditionalWeakTable<AssistantMemoryIndexPreparation, object> _assistantIndexPreparations = new();

    internal bool HasOriginalLocalAssistantMemoryComposition(CanonicalSqliteOriginalStoreOwner store) =>
        store.HasOriginalDatabase(factory) && embeddings is LocalHashEmbeddingService;

    internal async Task<AssistantMemoryIndexPreparation> PrepareOriginalAssistantMemoryIndexAsync(
        CanonicalSqliteOriginalStoreOwner store, KnowledgeRecord record,
        CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        // WRITE approval does not authorize remote disclosure. Only the actual configured
        // sealed local embedding owner is supported until that separate admission exists.
        if (!HasOriginalLocalAssistantMemoryComposition(store))
            throw new NotSupportedException("This Assistant memory writer requires its configured local embedding owner.");
        var scope = new RetrievalScope(RetrievalScopeKind.Collection, record.Id);
        source.Run(() => ValidateSource(scope, "knowledge", record.Id.ToString(), record.Title));
        var normalized = source.Invoke(() => NormalizeText(record.Summary));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        var document = new RetrievalDocument(Guid.NewGuid(), scope.Kind, scope.Id, "knowledge",
            record.Id.ToString(), record.Title.Trim(), hash, record.CreatedAt, record.UpdatedAt);
        var chunks = new List<RetrievalChunk>();
        foreach (var segment in Split(normalized))
        {
            token.ThrowIfCancellationRequested();
            var actualVector = await source.Read(() => embeddings.EmbedAsync(segment.Text, token)).ConfigureAwait(false);
            source.Run(() => chunks.Add(new RetrievalChunk(Guid.NewGuid(), document.Id, segment.Ordinal,
                segment.Text, segment.Start, segment.Text.Length, Array.AsReadOnly(actualVector.ToArray()), CountTerms(segment.Text))));
        }
        var preparation = new AssistantMemoryIndexPreparation(this, store, document, chunks.AsReadOnly());
        _assistantIndexPreparations.Add(preparation, new());
        return preparation;
    }

    internal async Task InsertOriginalAssistantMemoryIndexWithinTransactionAsync(
        CanonicalSqliteOriginalStoreLease actualWriter, AssistantMemoryIndexPreparation preparation,
        Action demandOriginalWrite, CancellationToken token)
    {
        if (preparation is null || !_assistantIndexPreparations.TryGetValue(preparation, out _) ||
            !ReferenceEquals(preparation.Owner, this) || !ReferenceEquals(preparation.Store, actualWriter.OriginalOwner) ||
            !actualWriter.OriginalOwner.IsIssuedOriginalLease(actualWriter) ||
            !HasOriginalLocalAssistantMemoryComposition(actualWriter.OriginalOwner))
            throw new UnauthorizedAccessException("The SAME prepared canonical index and original protected writer are required.");
        lock (preparation)
        {
            if (preparation.Consumed) throw new InvalidOperationException("The actual index preparation cannot be replayed.");
            preparation.Consumed = true;
        }
        // No nested connection, schema setup, source upsert or chunk deletion. A collision
        // aborts the caller's SAME transaction containing both Knowledge and index records.
        await ExecuteOriginalAssistantIndexInsertAsync(actualWriter, command =>
        {
            demandOriginalWrite();
            command.CommandText = """
                INSERT INTO retrieval_documents(id,scope_kind,scope_id,source_type,source_id,title,content_hash,created_at,updated_at)
                VALUES($id,$scopeKind,$scopeId,$sourceType,$sourceId,$title,$hash,$createdAt,$updatedAt);
                """;
            AddDocumentParameters(command, preparation.Document);
        }, token).ConfigureAwait(false);
        foreach (var chunk in preparation.Chunks)
        {
            await ExecuteOriginalAssistantIndexInsertAsync(actualWriter, command =>
            {
                demandOriginalWrite();
                command.CommandText = """
                    INSERT INTO retrieval_chunks(id,document_id,ordinal,text,start_character,length,embedding_json,terms_json)
                    VALUES($id,$documentId,$ordinal,$text,$start,$length,$embedding,$terms);
                    """;
                AddCanonicalChunkParameters(command, chunk);
            }, token).ConfigureAwait(false);
        }
    }
    private static async Task ExecuteOriginalAssistantIndexInsertAsync(CanonicalSqliteOriginalStoreLease lease,
        Action<SqliteCommand> configure, CancellationToken token)
    {
        SqliteCommand? command = null; var errors = new List<Exception>();
        try
        {
            lease.InvokeOriginalSource(() => { command = lease.Connection.CreateCommand(); return command; });
            lease.InvokeOriginalSource(() => { command!.Transaction = lease.Transaction; configure(command); });
            var changed = await lease.ReadOriginalSourceAsync(() => command!.ExecuteNonQueryAsync(token)).ConfigureAwait(false);
            if (changed != 1) throw new InvalidOperationException("The original canonical index insert did not create exactly one row.");
        }
        catch (Exception failure) { errors.Add(failure); }
        if (command is not null)
            try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); }
            catch (Exception failure) { errors.Add(failure); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }
}
