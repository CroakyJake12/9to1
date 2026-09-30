using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Evidence comes from the actual SQLite UUID and one consistent canonical Planner snapshot.
/// Even built-in collection/calendar rows count as existing data: setup must bind a new empty store
/// before populating defaults, otherwise the explicit Home import flow remains required.</summary>
public sealed class PlannerLocalStoreEvidenceProvider(IResourceStoreIdentitySource identities,
    ISqliteConnectionFactory connections) : IHomeLocalStoreEvidenceProvider
{
    public string ResourceKind => "planner";

    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken cancellationToken)
    {
        var identity = await identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty || storeId != identity.StoreId.ToString("D")) return null;
        await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        using (var actualIdentity = connection.CreateCommand())
        {
            actualIdentity.Transaction = transaction;
            actualIdentity.CommandText = "SELECT store_id FROM resource_store_identity WHERE singleton=1 AND schema_version=1";
            if (await actualIdentity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not string observedId || observedId != storeId)
                return null;
        }
        var tables = new List<string>();
        using (var catalogue = connection.CreateCommand())
        {
            catalogue.Transaction = transaction;
            catalogue.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND (name GLOB 'planner_*' OR name GLOB 'calendar_*') ORDER BY name";
            await using var reader = await catalogue.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) tables.Add(reader.GetString(0));
        }
        if (tables.Count == 0) return null;
        var snapshot = new List<object>();
        var isEmpty = true;
        foreach (var table in tables)
        {
            if (!table.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
                throw new InvalidDataException("Unsupported canonical Planner table identifier.");
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT * FROM \"" + table + "\"";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            var rows = new List<string>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var values = Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray();
                rows.Add(JsonSerializer.Serialize(values));
            }
            isEmpty &= rows.Count == 0;
            rows.Sort(StringComparer.Ordinal);
            snapshot.Add(new { Table = table, Columns = columns, Rows = rows });
        }
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));
        return new(ResourceKind, storeId, revision, identity.NewlyCreated, isEmpty, true);
    }
}
