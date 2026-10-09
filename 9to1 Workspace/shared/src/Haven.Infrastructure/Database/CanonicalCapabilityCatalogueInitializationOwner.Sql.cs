using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class CanonicalCapabilityCatalogueInitializationOwner
{
    private sealed record RowSnapshot(IReadOnlyList<CapabilityDefinition> Rows, string Sha256, IReadOnlyDictionary<Guid, string> RowDigests);
    private sealed record Snapshot(IReadOnlyList<CapabilityDefinition> Rows, string Sha256, Receipt? Receipt);
    private async Task<Snapshot> ReadSnapshotAsync(AuthenticatedResourceActor actor, ResourceStoreIdentity identity,
        Guid operationId, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        CanonicalSqliteOriginalStoreLease? lease = null; RowSnapshot? rows = null; Receipt? receipt = null;
        var errors = new List<Exception>();
        try
        {
            await source.ReadCapture(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(actor, false,
                source.Run, source.Retain, token), value => lease = value).ConfigureAwait(false);
            if (!_store.IsIssuedOriginalLease(lease!) || lease!.OriginalIdentity != identity)
                throw new UnauthorizedAccessException("The actual initialized store READ lease changed before setup review.");
            rows = await ReadRowsWithinLeaseAsync(lease, token).ConfigureAwait(false);
            receipt = await ReadReceiptWithinLeaseAsync(lease, operationId, token).ConfigureAwait(false);
            await lease.RevalidateWithinSourceAsync(source.Run, source.Retain, token).ConfigureAwait(false);
        }
        catch (Exception cause) { errors.Add(cause); }
        if (lease is not null) try { await lease.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors); return new(rows!.Rows, rows.Sha256, receipt);
    }
    private static async Task<RowSnapshot> ReadRowsWithinLeaseAsync(
        CanonicalSqliteOriginalStoreLease lease, CancellationToken token)
    {
        SqliteCommand? command = null; SqliteDataReader? reader = null; Task<SqliteDataReader>? rawReader = null;
        var errors = new List<Exception>(); var rows = new List<CapabilityDefinition>();
        var rawRows = new List<object?[]>(); var rowDigests = new Dictionary<Guid, string>(); string[] columns = [];
        try
        {
            lease.InvokeOriginalSource(() =>
            {
                command = lease.Connection.CreateCommand(); command.Transaction = lease.Transaction;
                command.CommandText = "SELECT type FROM sqlite_master WHERE name='capabilities';";
            });
            if (await lease.ReadOriginalSourceAsync(() => command!.ExecuteScalarAsync(token)).ConfigureAwait(false) is not "table")
                throw new InvalidDataException("The actual owning SQLite initialization must prepare the capabilities table; setup never creates or repairs schema.");
            await lease.CloseOriginalResourceAsync(command!).ConfigureAwait(false); command = null;
            lease.InvokeOriginalSource(() =>
            {
                command = lease.Connection.CreateCommand(); command.Transaction = lease.Transaction;
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND tbl_name IN ('capabilities','settings');";
            });
            if (Convert.ToInt64(await lease.ReadOriginalSourceAsync(() => command!.ExecuteScalarAsync(token)).ConfigureAwait(false)) != 0)
                throw new InvalidDataException("Preserve custom capability/settings triggers for explicit recovery; setup cannot authorize their unknown side effects.");
            await lease.CloseOriginalResourceAsync(command!).ConfigureAwait(false); command = null;
            lease.InvokeOriginalSource(() =>
            {
                command = lease.Connection.CreateCommand(); command.Transaction = lease.Transaction;
                command.CommandText = "SELECT * FROM capabilities ORDER BY id COLLATE BINARY LIMIT 1025;";
            });
            try { reader = await lease.ReadOriginalSourceAsync(() => rawReader = command!.ExecuteReaderAsync(token)).ConfigureAwait(false); }
            finally
            {
                // Recover the SAME successful reader before propagating caller/source
                // publication failure. No productive factory/disposal is replayed.
                if (rawReader is not null) try { reader = await rawReader.ConfigureAwait(false); }
                catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, rawReader, cause); }
            }
            lease.InvokeOriginalSource(() =>
            {
                if (reader!.FieldCount is < 1 or > 64) throw new InvalidDataException("The capability column snapshot exceeds its bound.");
                columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            });
            while (await lease.ReadOriginalSourceAsync(() => reader!.ReadAsync(token)).ConfigureAwait(false))
                lease.InvokeOriginalSource(() =>
                {
                    var row = CapabilityRepository.MapOriginalDatabaseRow(reader!);
                    var values = Enumerable.Range(0, reader!.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray();
                    rows.Add(row); rawRows.Add(values); rowDigests.Add(row.Id, Hash(new { columns, values }));
                });
            if (rows.Count > 1024) throw new InvalidDataException("The actual setup catalogue exceeds its bounded snapshot size.");
            _ = Hash(new { columns, rawRows });
        }
        catch (Exception cause) { errors.Add(cause); }
        if (reader is not null) try { await lease.CloseOriginalResourceAsync(reader).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (command is not null) try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors); return new(Array.AsReadOnly(rows.ToArray()), Hash(new { columns, rawRows }),
            new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, string>(rowDigests));
    }
    private static async Task<Receipt?> ReadReceiptWithinLeaseAsync(CanonicalSqliteOriginalStoreLease lease,
        Guid operationId, CancellationToken token)
    {
        SqliteCommand? command = null; var errors = new List<Exception>(); Receipt? result = null;
        try
        {
            lease.InvokeOriginalSource(() =>
            {
                command = lease.Connection.CreateCommand(); command.Transaction = lease.Transaction;
                command.CommandText = "SELECT value FROM settings WHERE key=$key;";
                command.Parameters.AddWithValue("$key", ReceiptPrefix + operationId.ToString("N"));
            });
            var value = await lease.ReadOriginalSourceAsync(() => command!.ExecuteScalarAsync(token)).ConfigureAwait(false);
            if (value is not null and not DBNull)
            {
                if (value is not string text || text.Length > 64 * 1024) throw new InvalidDataException("Preserve the invalid/oversized capability setup receipt.");
                try { result = JsonSerializer.Deserialize<Receipt>(text) ?? throw new JsonException(); }
                catch (JsonException cause) { throw new InvalidDataException("Preserve the corrupt capability setup receipt.", cause); }
            }
        }
        catch (Exception cause) { errors.Add(cause); }
        if (command is not null) try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors); return result;
    }
    private static async Task InsertReceiptWithinLeaseAsync(CanonicalSqliteOriginalStoreLease lease,
        Receipt receipt, Action demand, CancellationToken token)
    {
        SqliteCommand? command = null; var errors = new List<Exception>();
        try
        {
            lease.InvokeOriginalSource(() =>
            {
                demand(); command = lease.Connection.CreateCommand(); command.Transaction = lease.Transaction;
                command.CommandText = "INSERT INTO settings(key,value,updated_at) VALUES($key,$value,$updated);";
                command.Parameters.AddWithValue("$key", ReceiptPrefix + receipt.OperationId.ToString("N"));
                command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(receipt));
                command.Parameters.AddWithValue("$updated", receipt.CompletedAtUtc.ToString("O"));
            });
            if (await lease.ReadOriginalSourceAsync(() => command!.ExecuteNonQueryAsync(token)).ConfigureAwait(false) != 1)
                throw new InvalidDataException("No exact create-only capability setup receipt was inserted.");
        }
        catch (Exception cause) { errors.Add(cause); }
        if (command is not null) try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }
}
