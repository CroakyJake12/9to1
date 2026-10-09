using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Haven.Application.Automations;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class CanonicalAutomationLibraryOriginalReadOwner
{
    private sealed record Row(AutomationOwnerRead<AutomationDefinition> Definition, string UpdatedAt, string Id);
    private sealed record Page(IReadOnlyList<AutomationOwnerRead<AutomationDefinition>> Definitions, bool HasMore, Row? Last);

    private static async Task<bool> HasExistingTable(CanonicalSqliteOriginalStoreLease lease, CancellationToken token)
    {
        var types = await QueryRows(lease, command =>
        {
            command.CommandText = "SELECT type FROM sqlite_master WHERE name=$name;";
            command.Parameters.AddWithValue("$name", "automations");
        }, reader => reader.GetString(0), 2, token).ConfigureAwait(false);
        if (types.Count > 1 || types.Any(type => type != "table"))
            throw new InvalidDataException("The actual automation schema name is not one existing table.");
        return types.Count == 1;
    }
    private static async Task<Page> ReadPage(CanonicalSqliteOriginalStoreLease lease, Query query,
        Continuation? window, CancellationToken token)
    {
        var columns = await QueryRows(lease, command => command.CommandText = "PRAGMA table_info(automations);",
            reader => reader.GetString(1), 256, token).ConfigureAwait(false);
        string[] required = ["id", "name", "mode", "instruction", "schedule_kind", "schedule_json", "next_run_at",
            "container_id", "is_enabled", "created_at", "updated_at"];
        if (required.Any(name => !columns.Contains(name, StringComparer.Ordinal)))
            throw new InvalidDataException("The maintained automation schema is incomplete; READ will not repair or seed it.");
        var hasArchive = columns.Contains("archived_at", StringComparer.Ordinal);
        var pageBytes = 0;
        var rows = await QueryRows(lease, command =>
        {
            var predicates = new List<string>();
            if (!query.IncludeDisabled) predicates.Add("is_enabled=1");
            if (!query.IncludeArchived && hasArchive) predicates.Add("archived_at IS NULL");
            if (query.Search.Length != 0)
            {
                predicates.Add("name LIKE $search ESCAPE '\\'");
                command.Parameters.AddWithValue("$search", "%" + query.Search.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%");
            }
            if (window is not null)
            {
                predicates.Add("(updated_at < $updated OR (updated_at = $updated AND id > $id))");
                command.Parameters.AddWithValue("$updated", window.UpdatedAt);
                command.Parameters.AddWithValue("$id", window.Id);
            }
            command.CommandText = "SELECT * FROM automations" + (predicates.Count == 0 ? "" : " WHERE " + string.Join(" AND ", predicates))
                + " ORDER BY updated_at DESC,id ASC LIMIT $maximum;";
            command.Parameters.AddWithValue("$maximum", query.Limit + 1);
        }, reader =>
        {
            var raw = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                // Preserve every actual column, including unknown future descriptors,
                // and its SQLite storage class. NULL stays distinct from an absent key.
                var value = reader.GetValue(index);
                raw.Add(reader.GetName(index), value is DBNull ? null : value switch
                {
                    string text => JsonSerializer.Serialize(new { storageClass = "text", value = text }),
                    long integer => JsonSerializer.Serialize(new { storageClass = "integer", value = integer }),
                    double real => JsonSerializer.Serialize(new { storageClass = "real", value = real }),
                    byte[] blob => JsonSerializer.Serialize(new { storageClass = "blob", value = Convert.ToBase64String(blob) }),
                    _ => throw new InvalidDataException("The actual SQLite row contains an unsupported storage class.")
                });
            }
            var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(raw));
            if (bytes > 256 * 1024 || checked(pageBytes += bytes) > 4 * 1024 * 1024)
                throw new InvalidDataException("The actual automation row/page exceeds its bounded READ size.");
            var definition = new AutomationDefinition(reader.Guid("id"), reader.String("name"),
                (HavenMode)reader.Int32("mode"), reader.String("instruction"),
                (AutomationScheduleKind)reader.Int32("schedule_kind"), reader.String("schedule_json"),
                reader.NullableDateTimeOffset("next_run_at"), reader.NullableGuid("container_id"),
                reader.Boolean("is_enabled"), reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at"))
            {
                ArchivedAt = hasArchive ? reader.NullableDateTimeOffset("archived_at") : null
            };
            // Current maintained schemas have no source-issued protected owner/CAS
            // writer. Stored IDs/configuration do not manufacture that missing owner.
            var observed = new AutomationOwnerRead<AutomationDefinition>(definition, true,
                "ORIGINAL_AUTOMATION_OWNER_REVIEW_REQUIRED", new ReadOnlyDictionary<string, string?>(raw));
            return new Row(observed, reader.String("updated_at"), reader.String("id"));
        }, query.Limit + 1, token).ConfigureAwait(false);
        var selected = rows.Take(query.Limit).ToArray();
        return new(Array.AsReadOnly(selected.Select(row => row.Definition).ToArray()), rows.Count > query.Limit, selected.LastOrDefault());
    }
    private static bool SameRows(IReadOnlyList<AutomationOwnerRead<AutomationDefinition>> first,
        IReadOnlyList<AutomationOwnerRead<AutomationDefinition>> second) => first.Count == second.Count &&
        first.Zip(second).All(pair => pair.First.Value == pair.Second.Value &&
            pair.First.RequiresRecovery == pair.Second.RequiresRecovery && pair.First.RecoveryCode == pair.Second.RecoveryCode &&
            pair.First.RetainedProtectedDescriptors.Count == pair.Second.RetainedProtectedDescriptors.Count &&
            pair.First.RetainedProtectedDescriptors.All(field => pair.Second.RetainedProtectedDescriptors.TryGetValue(field.Key, out var value) && field.Value == value));

    private static async Task<List<T>> QueryRows<T>(CanonicalSqliteOriginalStoreLease lease,
        Action<SqliteCommand> configure, Func<SqliteDataReader, T> read, int maximum, CancellationToken token)
    {
        SqliteCommand? command = null; SqliteDataReader? reader = null; Task<SqliteDataReader>? actualReader = null;
        var failures = new List<Exception>(); var rows = new List<T>();
        try
        {
            // Return the SAME command from the physical callback so the lease owns
            // it even when an outer publication guard rejects the successful birth.
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
                lease.InvokeOriginalSource(() => rows.Add(read(reader!)));
                if (rows.Count > maximum) throw new InvalidDataException("The actual query exceeded its bounded row count.");
            }
        }
        catch (Exception cause) { failures.Add(cause); }
        // Independently join the actual reader close before disposing its command.
        // Both cached close failures remain in custody; a reader failure cannot skip
        // the command close, and its statement cannot be freed during reader cleanup.
        foreach (var resource in new IAsyncDisposable?[] { reader, command })
        {
            if (resource is null) continue;
            Task? close = null;
            try { close = lease.CloseOriginalResourceAsync(resource); await close.ConfigureAwait(false); }
            catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(failures, close, cause); }
        }
        CanonicalSqliteOriginalStoreOwner.Throw(failures); return rows;
    }
}
