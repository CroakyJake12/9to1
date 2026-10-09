using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class CanonicalAutomationDefinitionOriginalWriteOwner
{
    private sealed record Snapshot(IReadOnlyDictionary<string, string?> Row, string RowSha256,
        Descriptor? Descriptor, string? DescriptorJson, string? PreviousDescriptorJson,
        DurableOperation? Operation, string? OperationJson);
    private static string DescriptorKey(Guid id, long revision) => DescriptorPrefix + id.ToString("N") + "." + revision.ToString("D20", System.Globalization.CultureInfo.InvariantCulture);
    private async Task<Snapshot> ReadSnapshotAsync(AuthenticatedResourceActor actor, ResourceStoreIdentity identity,
        Guid entity, string rawId, Guid operation, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        CanonicalSqliteOriginalStoreLease? lease = null; Snapshot? snapshot = null; var errors = new List<Exception>();
        try
        {
            await source.ReadCapture(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(actor, false,
                source.Run, source.Retain, token), actual => lease = actual).ConfigureAwait(false);
            if (!_store.IsIssuedOriginalLease(lease!) || lease!.OriginalIdentity != identity)
                throw new UnauthorizedAccessException("The exact protected store changed before Automation review.");
            await source.JoinAllAsync().ConfigureAwait(false);
            snapshot = await ReadSnapshotWithinLeaseAsync(lease, entity, rawId, operation, token).ConfigureAwait(false);
            await source.Read(() => lease.RevalidateWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
        }
        catch (Exception cause) { errors.Add(cause); }
        Task? close = null;
        if (lease is not null)
            try { close = lease.CloseAndDrainAsync(); source.Retain(close); await close.ConfigureAwait(false); }
            catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, close, cause); }
        try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
        return snapshot ?? throw new InvalidOperationException("No healthy actual Automation snapshot was captured.");
    }
    private static async Task<Snapshot> ReadSnapshotWithinLeaseAsync(CanonicalSqliteOriginalStoreLease lease,
        Guid entity, string rawId, Guid operation, CancellationToken token)
    {
        var schema = await Query(lease, command => command.CommandText =
            "SELECT name,type FROM sqlite_master WHERE name IN ('automations','settings');",
            reader => (reader.GetString(0), reader.GetString(1)), 2, token).ConfigureAwait(false);
        if (schema.Count != 2 || schema.Any(row => row.Item2 != "table"))
            throw new InvalidDataException("The owning existing automation/settings schema is required; review does not initialize or repair it.");
        var triggers = await Query(lease, command => command.CommandText =
            "SELECT name FROM sqlite_master WHERE type='trigger' AND tbl_name IN ('automations','settings') LIMIT 1;",
            reader => reader.GetString(0), 1, token).ConfigureAwait(false);
        if (triggers.Count != 0)
            throw new InvalidDataException("Unknown automation/settings triggers require separate recovery; this WRITE cannot authorize their effects.");
        var row = await ReadRowWithinLeaseAsync(lease, rawId, token).ConfigureAwait(false);
        if (!Guid.TryParse(RawText(row, "id"), out var actualId) || actualId != entity)
            throw new UnauthorizedAccessException("The exact actual raw automation identity changed.");
        var descriptors = await Query(lease, command =>
        {
            command.CommandText = "SELECT key,value FROM settings WHERE key LIKE $prefix ORDER BY key DESC LIMIT 2;";
            command.Parameters.AddWithValue("$prefix", DescriptorPrefix + entity.ToString("N") + ".%");
        }, reader => (reader.GetString(0), reader.GetString(1)), 2, token).ConfigureAwait(false);
        Descriptor? descriptor = null; string? current = null, previous = null;
        if (descriptors.Count != 0)
        {
            current = descriptors[0].Item2; descriptor = Parse<Descriptor>(current);
            if (descriptors[0].Item1 != DescriptorKey(entity, descriptor.Revision))
                throw new InvalidDataException("Preserve the mismatched immutable Automation descriptor key.");
            if (descriptors.Count == 2)
            {
                previous = descriptors[1].Item2; var prior = Parse<Descriptor>(previous);
                if (descriptors[1].Item1 != DescriptorKey(entity, prior.Revision) || prior.Revision != descriptor.Revision - 1 ||
                    descriptor.PreviousDescriptorSha256 != RawJsonDigest(previous))
                    throw new InvalidDataException("The actual Automation descriptor predecessor is not confirmed; no hidden adoption or replay.");
            }
            else if (descriptor.Revision != 1 || descriptor.PreviousDescriptorSha256 is not null)
                throw new InvalidDataException("The first preserved Automation descriptor is not an original revision.");
        }
        var receipts = await Query(lease, command =>
        {
            command.CommandText = "SELECT value FROM settings WHERE key=$key;";
            command.Parameters.AddWithValue("$key", ReceiptPrefix + operation.ToString("N"));
        }, reader => reader.GetString(0), 1, token).ConfigureAwait(false);
        var operationJson = receipts.Count == 0 ? null : receipts[0];
        return new(row, AutomationDefinitionChange.ComputeRawRowSHA256(row), descriptor, current, previous,
            operationJson is null ? null : Parse<DurableOperation>(operationJson), operationJson);
    }
    private static T Parse<T>(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > 256 * 1024) throw new InvalidDataException("Preserve the oversized Automation owner descriptor/receipt.");
        try { return JsonSerializer.Deserialize<T>(text) ?? throw new JsonException(); }
        catch (JsonException cause) { throw new InvalidDataException("Preserve the invalid Automation owner descriptor/receipt.", cause); }
    }
    private static string RawJsonDigest(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string RawText(IReadOnlyDictionary<string, string?> raw, string field)
    {
        if (!raw.TryGetValue(field, out var encoded) || encoded is null)
            throw new InvalidDataException("The actual automation row lacks required original text: " + field);
        using var json = JsonDocument.Parse(encoded); var value = json.RootElement;
        if (value.GetProperty("storageClass").GetString() != "text" || value.GetProperty("value").GetString() is not { } text)
            throw new InvalidDataException("The actual automation text storage class changed: " + field);
        return text;
    }
    private static bool HasLegacyLease(IReadOnlyDictionary<string, string?> raw) =>
        raw.TryGetValue("lease_token", out var token) && raw.TryGetValue("lease_until", out var until) && (token is not null || until is not null);
    private static void DemandNoLegacyLease(IReadOnlyDictionary<string, string?> raw)
    {
        // Even an expired non-null lease is retained as an unresolved legacy owner.
        // Review never cancels, clears, resumes or replays its run.
        if (!raw.TryGetValue("lease_token", out var token) || !raw.TryGetValue("lease_until", out var until))
            throw new InvalidDataException("The actual legacy run lease schema is unavailable.");
        if (token is not null || until is not null)
            throw new InvalidOperationException("An actual legacy run lease remains stored; recovery/disable cannot interrupt or adopt it.");
    }
    // AuthenticationRevision in an old descriptor is historical evidence. Fresh
    // current READ + a separately accepted exact current-actor WRITE permits the
    // SAME stable principal to append a new revision; it never lends old authority.
    private static void DemandDescriptor(Snapshot snapshot, AuthenticatedResourceActor actor,
        ResourceStoreIdentity identity, Guid entity)
    {
        if (snapshot.Descriptor is not { } descriptor) return;
        var owner = new AutomationOwnerBinding(identity.StoreId, actor.ProfileId, actor.ActorId, actor.AuthenticationRevision, actor.AccountId, actor.OrganisationId);
        var receipt = descriptor.Receipt;
        if (descriptor.SchemaVersion != 1 || descriptor.StoreId != identity.StoreId || descriptor.EntityId != entity || descriptor.Revision < 1 ||
            descriptor.Owner.StoreId != owner.StoreId || descriptor.Owner.ProfileId != owner.ProfileId ||
            descriptor.Owner.ActorId != owner.ActorId || descriptor.Owner.AccountId != owner.AccountId || descriptor.Owner.OrganisationId != owner.OrganisationId ||
            string.IsNullOrWhiteSpace(descriptor.Owner.AuthenticationRevision) || descriptor.OperationId == Guid.Empty || descriptor.AfterRowSha256 != snapshot.RowSha256 ||
            descriptor.State is not (AutomationOperationalState.Disabled or AutomationOperationalState.NeedsAttention) ||
            receipt.SchemaVersion != 1 || receipt.StoreId != identity.StoreId || receipt.EntityId != entity ||
            receipt.EntityKind != AutomationDefinitionEntityKind.Automation || receipt.OperationId != descriptor.OperationId ||
            receipt.CommittedRevision != descriptor.Revision || receipt.ExpectedRevision != descriptor.Revision - 1 ||
            receipt.ObservedOwner != descriptor.Owner || receipt.CommittedAt == default || receipt.PayloadSha256.Length != 64)
            throw new InvalidDataException("The current row/actor and durable immutable owner descriptor do not match; preserve them for recovery.");
    }
    private static void DemandDurableOperation(DurableOperation actual, Snapshot snapshot,
        AuthenticatedResourceActor actor, ResourceStoreIdentity identity, Guid entity, Guid operation,
        CanonicalAutomationOriginalChangeKind kind, string intentDigest)
    {
        DemandDescriptor(snapshot, actor, identity, entity);
        if (actual.SchemaVersion != 1 || actual.StoreId != identity.StoreId || actual.EntityId != entity ||
            actual.OperationId != operation || actual.Kind != kind || actual.Descriptor is not { } descriptor ||
            snapshot.Descriptor is null || Hash(descriptor) != Hash(snapshot.Descriptor) ||
            descriptor.StoreId != actual.StoreId || descriptor.EntityId != actual.EntityId ||
            descriptor.OperationId != actual.OperationId || descriptor.Receipt.OperationId != actual.OperationId ||
            actual.BeforeRowSha256 != descriptor.BeforeRowSha256 || actual.AfterRowSha256 != descriptor.AfterRowSha256 ||
            actual.AfterRowSha256 != snapshot.RowSha256 || actual.IntentSha256 != descriptor.Receipt.PayloadSha256 ||
            actual.IntentSha256 != intentDigest || actual.BeforeRowSha256.Length != 64 || actual.AfterRowSha256.Length != 64 ||
            !actual.BeforeRowSha256.All(Uri.IsHexDigit) || !actual.AfterRowSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The complete durable Automation operation/descriptor/receipt tuple is not the SAME original; no replay or acknowledgment is admitted.");
    }
    private static async Task<IReadOnlyDictionary<string, string?>> ReadRowWithinLeaseAsync(
        CanonicalSqliteOriginalStoreLease lease, string actualId, CancellationToken token)
    {
        var rows = await Query(lease, command =>
        {
            command.CommandText = "SELECT * FROM automations WHERE id=$id COLLATE BINARY;";
            command.Parameters.AddWithValue("$id", actualId);
        }, reader =>
        {
            if (reader.FieldCount is < 1 or > 256) throw new InvalidDataException("The automation column snapshot exceeds its bound.");
            var raw = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = reader.GetValue(index);
                raw.Add(reader.GetName(index), value is DBNull ? null : value switch
                {
                    string text => JsonSerializer.Serialize(new { storageClass = "text", value = text }),
                    long integer => JsonSerializer.Serialize(new { storageClass = "integer", value = integer }),
                    double real => JsonSerializer.Serialize(new { storageClass = "real", value = real }),
                    byte[] blob => JsonSerializer.Serialize(new { storageClass = "blob", value = Convert.ToBase64String(blob) }),
                    _ => throw new InvalidDataException("Unknown actual SQLite storage class.")
                });
            }
            if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(raw)) > 256 * 1024)
                throw new InvalidDataException("The actual selected row exceeds its returned metadata bound.");
            return (IReadOnlyDictionary<string, string?>)new ReadOnlyDictionary<string, string?>(raw);
        }, 1, token).ConfigureAwait(false);
        return rows.Count == 1 ? rows[0] : throw new UnauthorizedAccessException("The one exact selected automation row is unavailable.");
    }
    private static bool SameExceptDisabled(IReadOnlyDictionary<string, string?> before, IReadOnlyDictionary<string, string?> after) =>
        before.Count == after.Count && after.TryGetValue("is_enabled", out var enabled) &&
        enabled == JsonSerializer.Serialize(new { storageClass = "integer", value = 0L }) &&
        before.All(pair => pair.Key == "is_enabled" || after.TryGetValue(pair.Key, out var value) && value == pair.Value);
    private static Task DisableRowWithinLeaseAsync(CanonicalSqliteOriginalStoreLease lease, string rawId,
        Action demand, CancellationToken token) => Execute(lease, command =>
        {
            demand(); command.CommandText = "UPDATE automations SET is_enabled=0 WHERE id=$id COLLATE BINARY;";
            command.Parameters.AddWithValue("$id", rawId);
        }, token);
    private static Task InsertSettingWithinLeaseAsync<T>(CanonicalSqliteOriginalStoreLease lease,
        string key, T value, DateTimeOffset at, Action demand, CancellationToken token) => Execute(lease, command =>
        {
            demand(); command.CommandText = "INSERT INTO settings(key,value,updated_at) VALUES($key,$value,$at);";
            command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value));
            command.Parameters.AddWithValue("$at", at.ToString("O"));
        }, token);
    private static async Task Execute(CanonicalSqliteOriginalStoreLease lease, Action<SqliteCommand> configure, CancellationToken token)
    {
        SqliteCommand? command = null; var errors = new List<Exception>();
        try
        {
            lease.InvokeOriginalSource(() => command = lease.Connection.CreateCommand());
            lease.InvokeOriginalSource(() => { command!.Transaction = lease.Transaction; configure(command); });
            if (await lease.ReadOriginalSourceAsync(() => command!.ExecuteNonQueryAsync(token)).ConfigureAwait(false) != 1)
                throw new InvalidDataException("The exact atomic Automation statement did not affect one row.");
        }
        catch (Exception cause) { errors.Add(cause); }
        if (command is not null) try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }
    private static async Task<List<T>> Query<T>(CanonicalSqliteOriginalStoreLease lease,
        Action<SqliteCommand> configure, Func<SqliteDataReader, T> map, int maximum, CancellationToken token)
    {
        SqliteCommand? command = null; SqliteDataReader? reader = null; Task<SqliteDataReader>? raw = null;
        var errors = new List<Exception>(); var rows = new List<T>();
        try
        {
            lease.InvokeOriginalSource(() => command = lease.Connection.CreateCommand());
            lease.InvokeOriginalSource(() => { command!.Transaction = lease.Transaction; configure(command); });
            try { reader = await lease.ReadOriginalSourceAsync(() => raw = command!.ExecuteReaderAsync(token)).ConfigureAwait(false); }
            finally
            {
                if (raw is not null) try { reader = await raw.ConfigureAwait(false); }
                catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, raw, cause); }
            }
            while (await lease.ReadOriginalSourceAsync(() => reader!.ReadAsync(token)).ConfigureAwait(false))
            {
                lease.InvokeOriginalSource(() => rows.Add(map(reader!)));
                if (rows.Count > maximum) throw new InvalidDataException("The actual Automation query exceeded its row bound.");
            }
        }
        catch (Exception cause) { errors.Add(cause); }
        // The reader owns statement state used during its actual close. Independently
        // join it before disposing the command, even when the reader close fails.
        foreach (var resource in new IAsyncDisposable?[] { reader, command })
        {
            if (resource is null) continue;
            Task? close = null;
            try { close = lease.CloseOriginalResourceAsync(resource); await close.ConfigureAwait(false); }
            catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, close, cause); }
        }
        CanonicalSqliteOriginalStoreOwner.Throw(errors); return rows;
    }
}
