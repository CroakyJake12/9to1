using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Evidence from the canonical SQL root and its existing Automation tables. Observation
/// never binds/imports ownership. Configuration and history are hashed locally, never exposed in Home metadata.</summary>
public sealed class AutomationLocalStoreEvidenceProvider(SqliteDatabase database) : IHomeLocalStoreEvidenceProvider
{
    public string ResourceKind => "automations";
    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken ct)
    {
        var identity = await database.GetStoreIdentityAsync(ct).ConfigureAwait(false);
        if (identity.StoreId.ToString("D") != storeId || identity.SchemaVersion != 1) return null;
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var current = await SqliteDatabase.ReadStoreIdentityAsync(connection, false, ct, transaction).ConfigureAwait(false);
        if (current.StoreId != identity.StoreId) return null;
        var tables = new List<string>();
        await using (var names = connection.CreateCommand())
        {
            names.Transaction = transaction;
            names.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
            await using var reader = await names.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var name = reader.GetString(0);
                if (name is "automations" or "automation_runs" or "reusable_tasks") tables.Add(name);
            }
        }
        if (tables.Count > 64) throw new InvalidDataException("Too many Automation tables for ownership observation.");
        if (!tables.Contains("automations", StringComparer.Ordinal) || !tables.Contains("automation_runs", StringComparer.Ordinal) ||
            !tables.Contains("reusable_tasks", StringComparer.Ordinal)) return null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(storeId));
        long totalBytes = 0; var rowCount = 0;
        foreach (var table in tables)
        {
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(table));
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"", StringComparison.Ordinal) + "\" ORDER BY rowid;";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (reader.FieldCount > 128) throw new InvalidDataException("Automation table exceeds the supported observation width.");
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray()));
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (++rowCount > 100000) throw new InvalidDataException("Automation ownership observation exceeds its bounded snapshot.");
                long rawRowBytes = 0;
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    if (reader.IsDBNull(i)) continue;
                    var type = reader.GetFieldType(i);
                    var length = type == typeof(string) ? reader.GetChars(i, 0, null, 0, 0) :
                        type == typeof(byte[]) ? reader.GetBytes(i, 0, null, 0, 0) : 8;
                    if (length > 2 * 1024 * 1024) throw new InvalidDataException("Automation value exceeds bounded ownership observation.");
                    rawRowBytes += type == typeof(string) ? length * 2 : length;
                    if (rawRowBytes > 8 * 1024 * 1024) throw new InvalidDataException("Automation row exceeds bounded ownership observation.");
                }
                var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray();
                var bytes = JsonSerializer.SerializeToUtf8Bytes(values);
                try
                {
                    totalBytes += bytes.Length;
                    if (totalBytes > 64 * 1024 * 1024) throw new InvalidDataException("Automation ownership observation exceeds its bounded snapshot.");
                    hash.AppendData(bytes);
                }
                finally { Array.Clear(bytes); }
            }
        }
        return new(ResourceKind, storeId, Convert.ToHexString(hash.GetHashAndReset()), identity.NewlyCreated, rowCount == 0, true);
    }
}

