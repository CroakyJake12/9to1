using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class CanonicalGeneratedUiInteractionOriginalOwner
{
    public Task<ICanonicalGeneratedUiOriginalObservation> ReadOriginalInteractionWithinSourceAsync(
        ICanonicalGeneratedUiOriginalSelection sameSelection, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = CreateOriginalInteractionSource(scope, retain);
        return Admit<ICanonicalGeneratedUiOriginalObservation>(source, async () =>
        {
            if (!source.InvokeProductive(() => _origins.IsIssuedOriginalSelection(sameSelection)))
                throw new UnauthorizedAccessException("Use an actual source-issued current message/runtime selection.");
            var actual = await source.Read(() => _origins.ObserveOriginalSnapshotWithinSourceAsync(sameSelection,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            var origin = source.InvokeProductive(() => CaptureOrigin(actual));
            return await ReadObservation(origin, source, token).ConfigureAwait(false);
        });
    }
    private async Task<Observation> ReadObservation(CapturedOrigin origin, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        var identity = await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(origin.Actor,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        var receipt = await source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync("canonical.sqlite",
            identity.StoreId.ToString("D"), source.Run, source.Retain, token).AsTask()).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Import the existing canonical SQLite store through Home before reading interaction state.");
        await DemandCurrent(origin, identity, receipt, source, token).ConfigureAwait(false);
        var stored = await ReadStored(origin, identity, source, token).ConfigureAwait(false);
        var state = CanonicalGeneratedUiOriginalReadState.NoSavedInteraction;
        var detail = "This message has no proven saved interaction. READ does not initialize storage or adopt old generated-app rows.";
        GenUiAppDefinition? saved = null;
        if (stored.Descriptor is null && stored.Row is not null)
        { state = CanonicalGeneratedUiOriginalReadState.Unprovenance; detail = "This existing row has no original message/operation provenance and cannot be restored or overwritten."; }
        else if (stored.Descriptor is { } descriptor)
        {
            DemandStoredProof(stored, origin, identity);
            var home = OriginalHomeWriteSource;
            var audited = home is not null && await source.Read(() => home.VerifyOriginalStoredAuditWithinSourceAsync(
                descriptor.Receipt, source.Run, source.Retain, token)).ConfigureAwait(false);
            state = audited ? CanonicalGeneratedUiOriginalReadState.Restorable : CanonicalGeneratedUiOriginalReadState.AuditUnavailable;
            detail = audited ? "The exact saved interaction has current message provenance and an acknowledged Home audit." :
                "The saved SQL receipt has no current acknowledged Home audit. Interaction state remains unavailable.";
            if (audited)
            {
                var json = RawText(stored.Row!, "definition_json");
                saved = JsonSerializer.Deserialize<GenUiAppDefinition>(json) ?? throw new InvalidDataException("The acknowledged saved definition is invalid.");
                var checkedDefinition = GenUiSemanticValidator.ValidateAndRepair(saved);
                if (!checkedDefinition.IsValid || checkedDefinition.Repairs.Count != 0 ||
                    saved.Document.Origin.InstanceId != descriptor.InstanceId || saved.Document.Origin.ThreadId != origin.ConversationId)
                    throw new InvalidDataException("Saved state cannot be implicitly repaired or moved to another runtime origin.");
            }
        }
        await DemandCurrent(origin, identity, receipt, source, token).ConfigureAwait(false);
        await source.JoinAllAsync().ConfigureAwait(false);
        var observation = new Observation(this, origin, identity, receipt, stored, state, detail, saved);
        source.RunProductive(() => _observations.Add(observation, observation)); return observation;
    }
    public Task RevalidateOriginalObservationWithinSourceAsync(ICanonicalGeneratedUiOriginalObservation sameObservation,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = CreateOriginalInteractionSource(scope, retain);
        return Admit(source, async () =>
        {
            await RevalidateObserved( source.InvokeProductive(() => RequireObservation(sameObservation)), source, token).ConfigureAwait(false); return 0;
        });
    }
    private async Task RevalidateObserved(Observation original, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        var current = await ReadObservation(original.Origin, source, token).ConfigureAwait(false);
        source.RunProductive(() =>
        {
            if (current.OriginalStoreIdentity != original.OriginalStoreIdentity || current.Ownership != original.Ownership ||
                JsonSerializer.Serialize(current.Stored) != JsonSerializer.Serialize(original.Stored) ||
                current.Stored.DescriptorJson != original.Stored.DescriptorJson || current.State != original.State)
                throw new UnauthorizedAccessException("The exact original saved interaction observation changed. Read it again.");
        });
        await source.JoinAllAsync().ConfigureAwait(false);
    }
    private async Task<StoredSnapshot> ReadStored(CapturedOrigin origin, ResourceStoreIdentity identity,
        CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        CanonicalSqliteOriginalStoreLease? lease = null; StoredSnapshot? result = null;
        var failures = new List<Exception>(); Task? close = null;
        try
        {
            await source.ReadCapture(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(origin.Actor, false,
                source.Run, source.Retain, token), actual => lease = actual).ConfigureAwait(false);
            source.RunProductive(() => { if (!_store.IsIssuedOriginalLease(lease!) || lease!.OriginalIdentity != identity)
                throw new UnauthorizedAccessException("The actual protected store identity/lease changed."); });
            result = await ReadStoredUnderLease(lease!, origin, identity, token).ConfigureAwait(false);
            await source.Read(() => lease!.RevalidateWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
        }
        catch (Exception cause) { failures.Add(cause); }
        if (lease is not null)
        {
            // The lease owns its cached close; capture it before any external retainer.
            try { source.InvokeOwningCleanup(() => { close = lease.CloseAndDrainAsync(); source.Retain(close); return 0; }); }
            catch (Exception cause) { failures.Add(cause); }
            if (close is not null)
                try { await close.ConfigureAwait(false); }
                catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(failures, close, cause); }
        }
        try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(failures); return result!;
    }
    private static async Task<StoredSnapshot> ReadStoredUnderLease(CanonicalSqliteOriginalStoreLease lease,
        CapturedOrigin origin, ResourceStoreIdentity identity, CancellationToken token)
    {
        var schema = await Query(lease, c => c.CommandText = "SELECT name,type FROM sqlite_master WHERE name IN ('genui_apps','settings');",
            r => (r.GetString(0), r.GetString(1)), 2, token).ConfigureAwait(false);
        if (schema.Count != 2 || schema.Any(item => item.Item2 != "table"))
            throw new InvalidDataException("The existing generated-app/settings schema is required; READ does not seed it.");
        var descriptors = await Query(lease, c =>
        {
            c.CommandText = "SELECT key,value FROM settings WHERE key LIKE $prefix ORDER BY key DESC LIMIT 2;";
            c.Parameters.AddWithValue("$prefix", DescriptorPrefix + OriginKey(origin, identity) + ".%");
        }, r => (r.GetString(0), r.GetString(1)), 2, token).ConfigureAwait(false);
        StoredInteraction? descriptor = null; string? descriptorJson = null;
        if (descriptors.Count != 0)
        {
            descriptorJson = descriptors[0].Item2;
            descriptor = Decode<StoredInteraction>(descriptorJson);
            if (descriptor.Revision < 1 || descriptors[0].Item1 != DescriptorPrefix + OriginKey(origin, identity) + "." + descriptor.Revision.ToString("D20"))
                throw new InvalidDataException("The actual saved descriptor key/revision is incoherent.");
            DemandDescriptorOriginProof(descriptor, origin, identity);
            if (descriptor.Revision > 1)
            {
                if (descriptors.Count != 2) throw new InvalidDataException("The exact saved descriptor history is incomplete.");
                var previous = Decode<StoredInteraction>(descriptors[1].Item2);
                DemandDescriptorOriginProof(previous, origin, identity);
                if (previous.Revision != descriptor.Revision - 1 ||
                    descriptors[1].Item1 != DescriptorPrefix + OriginKey(origin, identity) + "." + previous.Revision.ToString("D20") ||
                    descriptor.PreviousDescriptorSha256 != Hash(descriptors[1].Item2) ||
                    previous.Receipt.OperationId == descriptor.Receipt.OperationId ||
                    previous.InstanceId == descriptor.InstanceId && descriptor.Receipt.BeforeRowSha256 != previous.RowSha256)
                    throw new InvalidDataException("The exact saved descriptor predecessor does not belong to this original history.");
                _ = await ReadDurableOperationProof(lease, previous, origin, identity, token).ConfigureAwait(false);
            }
            if (descriptor.Revision == 1 && (descriptors.Count != 1 || descriptor.PreviousDescriptorSha256 is not null))
                throw new InvalidDataException("The first saved descriptor cannot claim predecessor history.");
        }
        var instance = descriptor?.InstanceId ?? origin.RuntimeDocument.Origin.InstanceId;
        var rows = await Query(lease, c => { c.CommandText = "SELECT * FROM genui_apps WHERE instance_id=$instance;";
            c.Parameters.AddWithValue("$instance", instance.ToString("D")); }, CaptureRawRow, 1, token).ConfigureAwait(false);
        var row = rows.SingleOrDefault();
        var target = instance == origin.RuntimeDocument.Origin.InstanceId ? row : (await Query(lease,
            c => { c.CommandText = "SELECT * FROM genui_apps WHERE instance_id=$instance;";
                c.Parameters.AddWithValue("$instance", origin.RuntimeDocument.Origin.InstanceId.ToString("D")); },
            CaptureRawRow, 1, token).ConfigureAwait(false)).SingleOrDefault();
        DurableOperation? operation = null;
        if (descriptor is not null)
            operation = await ReadDurableOperationProof(lease, descriptor, origin, identity, token).ConfigureAwait(false);
        var sourceRows = new SortedDictionary<string, List<SortedDictionary<string, RawValue>>>(StringComparer.Ordinal);
        foreach (var (table, column, value, order) in new[]
        {
            ("messages", "id", origin.MessageId, "id"), ("conversations", "id", origin.ConversationId, "id"),
            ("message_versions", "message_id", origin.MessageId, "id"),
            ("conversation_branches", "conversation_id", origin.ConversationId, "id"),
            ("conversation_branch_messages", "message_id", origin.MessageId, "branch_id,message_id")
        })
            sourceRows.Add(table, await Query(lease, c =>
            {
                // Every identifier comes from the fixed maintained-table list above.
                c.CommandText = "SELECT * FROM " + table + " WHERE " + column + "=$id ORDER BY " + order + " LIMIT 257;";
                c.Parameters.AddWithValue("$id", value.ToString("D"));
            }, CaptureRawRow, 256, token).ConfigureAwait(false));
        if (sourceRows["messages"].Count != 1 || sourceRows["conversations"].Count != 1 ||
            RawText(sourceRows["messages"][0], "conversation_id") != origin.ConversationId.ToString("D"))
            throw new InvalidDataException("The actual canonical source message/conversation relation is missing.");
        var sourceJson = JsonSerializer.Serialize(sourceRows);
        if (Encoding.UTF8.GetByteCount(sourceJson) > 4 * 1024 * 1024)
            throw new InvalidDataException("The returned source-row proof exceeds its bounded metadata size.");
        return new(row, RowDigest(row), target, RowDigest(target), Hash(sourceJson), descriptorJson, descriptor, operation);
    }
    private static T Decode<T>(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > 768 * 1024) throw new InvalidDataException("The saved provenance metadata exceeds its bounded size.");
        return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException("The saved provenance metadata is invalid.");
    }
    private static void DemandDescriptorOriginProof(StoredInteraction d, CapturedOrigin origin, ResourceStoreIdentity identity)
    {
        var r = d.Receipt ?? throw new InvalidDataException("The exact original descriptor receipt is missing.");
        if (d.SchemaVersion != 1 || d.StoreId != identity.StoreId || d.ConversationId != origin.ConversationId ||
            d.MessageId != origin.MessageId || d.TemplateOrdinal != origin.Ordinal || d.ContentSha256 != origin.ContentDigest ||
            d.DeclarationSha256 != origin.DeclarationDigest || d.InstanceId == Guid.Empty || d.Revision < 1 ||
            !IsDigest(d.DefinitionSha256) || !IsDigest(d.RowSha256) ||
            (d.Revision == 1 ? d.PreviousDescriptorSha256 is not null : !(d.PreviousDescriptorSha256 is { } previousDigest && IsDigest(previousDigest))) ||
            r.SchemaVersion != 1 || r.StoreId != d.StoreId || r.InstanceId != d.InstanceId || r.ConversationId != d.ConversationId ||
            r.MessageId != d.MessageId || r.TemplateOrdinal != d.TemplateOrdinal || r.MessageContentSha256 != d.ContentSha256 ||
            r.DeclarationSha256 != d.DeclarationSha256 || r.AfterRowSha256 != d.RowSha256 || r.OperationId == Guid.Empty ||
            !IsDigest(r.BeforeRowSha256) || !IsDigest(r.PayloadSha256) || !IsDigest(r.HomeArgumentsSha256) ||
            string.IsNullOrWhiteSpace(r.HomeApprovalRequestId) || r.ObservedActor is null ||
            string.IsNullOrWhiteSpace(r.ObservedActor.AuthenticationRevision) ||
            r.ObservedActor.ProfileId != origin.Actor.ProfileId || r.ObservedActor.ActorId != origin.Actor.ActorId ||
            r.ObservedActor.AccountId != origin.Actor.AccountId || r.ObservedActor.OrganisationId != origin.Actor.OrganisationId)
            throw new InvalidDataException("The stored descriptor/receipt does not belong to the same original message history.");
    }
    private static void DemandDurableOperationProof(StoredInteraction d, DurableOperation o,
        CapturedOrigin origin, ResourceStoreIdentity identity)
    {
        DemandDescriptorOriginProof(d, origin, identity);
        var r = d.Receipt;
        if (o.SchemaVersion != 1 || o.StoreId != d.StoreId || o.OperationId != r.OperationId || o.IntentSha256 != r.PayloadSha256 ||
            o.BeforeRowSha256 != r.BeforeRowSha256 || o.AfterRowSha256 != r.AfterRowSha256 ||
            JsonSerializer.Serialize(o.Descriptor) != JsonSerializer.Serialize(d))
            throw new InvalidDataException("The exact durable operation/descriptor/receipt history is incoherent.");
    }
    private static async Task<DurableOperation> ReadDurableOperationProof(CanonicalSqliteOriginalStoreLease lease,
        StoredInteraction descriptor, CapturedOrigin origin, ResourceStoreIdentity identity, CancellationToken token)
    {
        DemandDescriptorOriginProof(descriptor, origin, identity);
        var operations = await Query(lease, c => { c.CommandText = "SELECT value FROM settings WHERE key=$key;";
            c.Parameters.AddWithValue("$key", OperationPrefix + descriptor.Receipt.OperationId.ToString("N")); },
            r => r.GetString(0), 1, token).ConfigureAwait(false);
        if (operations.Count != 1) throw new InvalidDataException("The original durable save operation receipt is missing.");
        var operation = Decode<DurableOperation>(operations[0]);
        DemandDurableOperationProof(descriptor, operation, origin, identity); return operation;
    }
    private static void DemandStoredProof(StoredSnapshot stored, CapturedOrigin origin, ResourceStoreIdentity identity)
    {
        var d = stored.Descriptor ?? throw new InvalidDataException("Original saved descriptor required.");
        var o = stored.Operation ?? throw new InvalidDataException("Original operation receipt required.");
        DemandDurableOperationProof(d, o, origin, identity);
        if (stored.Row is null || d.RowSha256 != stored.RowSha256 ||
            d.DefinitionSha256 != Hash(RawText(stored.Row, "definition_json")) ||
            RawText(stored.Row, "instance_id") != d.InstanceId.ToString("D") || RawText(stored.Row, "thread_id") != origin.ConversationId.ToString("D"))
            throw new InvalidDataException("The complete stored row/descriptor/operation/receipt provenance does not agree with this actual current message.");
    }
    private static SortedDictionary<string, RawValue> CaptureRawRow(SqliteDataReader reader)
    {
        var row = new SortedDictionary<string, RawValue>(StringComparer.Ordinal);
        for (var i = 0; i < reader.FieldCount; i++)
            row.Add(reader.GetName(i), reader.GetValue(i) switch
            { DBNull => new("null"), string text => new("text", Text: text), long value => new("integer", Integer: value),
                double value => new("real", Real: value), byte[] value => new("blob", Blob: Convert.ToBase64String(value)),
                _ => throw new InvalidDataException("Unknown SQLite storage class.") });
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(row)) > 768 * 1024)
            throw new InvalidDataException("The returned original row metadata exceeds its bounded size.");
        return row;
    }
    private static string RawText(SortedDictionary<string, RawValue> row, string column) =>
        row.TryGetValue(column, out var value) && value.StorageClass == "text" && value.Text is { } text ? text :
            throw new InvalidDataException("The maintained generated-app row requires its actual text column: " + column);
    private static async Task<List<T>> Query<T>(CanonicalSqliteOriginalStoreLease lease, Action<SqliteCommand> configure,
        Func<SqliteDataReader, T> read, int maximum, CancellationToken token)
    {
        SqliteCommand? command = null; SqliteDataReader? reader = null; Task<SqliteDataReader>? actualReader = null;
        var failures = new List<Exception>(); var result = new List<T>();
        try
        {
            lease.InvokeOriginalSource(() => command = lease.Connection.CreateCommand());
            lease.InvokeOriginalSource(() => { command!.Transaction = lease.Transaction; configure(command); });
            try { reader = await lease.ReadOriginalSourceAsync(() => actualReader = command!.ExecuteReaderAsync(token)).ConfigureAwait(false); }
            finally
            {
                if (actualReader is not null)
                    try { reader = await actualReader.ConfigureAwait(false); }
                    catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(failures, actualReader, cause); }
            }
            while (await lease.ReadOriginalSourceAsync(() => reader!.ReadAsync(token)).ConfigureAwait(false))
            {
                lease.InvokeOriginalSource(() => result.Add(read(reader!)));
                if (result.Count > maximum) throw new InvalidDataException("The exact original query exceeded its bounded row count.");
            }
        }
        catch (Exception cause) { failures.Add(cause); }
        // The command owns the reader's native statement. Independently settle the
        // reader close before starting command cleanup, including a failed reader close.
        foreach (var resource in new IAsyncDisposable?[] { reader, command })
        {
            if (resource is null) continue;
            Task? close = null;
            try { close = lease.CloseOriginalResourceAsync(resource); }
            catch (Exception cause) { failures.Add(cause); }
            if (close is not null)
                try { await close.ConfigureAwait(false); }
                catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(failures, close, cause); }
        }
        CanonicalSqliteOriginalStoreOwner.Throw(failures); return result;
    }
}
