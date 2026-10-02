using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure.Tests;

public sealed class AutomationOrdinaryGuidFenceTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task Actual_SQL_ordinary_writer_preserves_legacy_UUID_spelling_and_denies_protected_or_ambiguous_aliases(bool reusable, bool protect, bool ambiguous)
    {
        using var owningLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = owningLifetime.Token;
        var root = Path.Combine(Path.GetTempPath(), "astra-automation-guid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = new SqliteDatabase(new Paths(root));
            await database.InitializeAsync(token); // Requires the coherent real global migration28, never a test-only owner schema.
            var automations = new AutomationRepository(database);
            var tasks = new WorkspaceStateRepository(database);
            var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var definition = new AutomationDefinition(id, "Original", default, "Retain original instruction", AutomationScheduleKind.Daily,
                "{\"opaqueSchedule\":true}", null, null, false, now, now);
            var task = new ReusableTaskDefinition(id, "Original", "Description", "Retain original instruction", null, false, now, now,
                "{\"unknownLegacyGraph\":null}");
            async Task Save(string name)
            {
                if (reusable) await tasks.UpsertReusableTaskAsync(task with { Name = name }, token);
                else await automations.UpsertAsync(definition with { Name = name }, token);
            }
            await Save("Original");
            var table = reusable ? "reusable_tasks" : "automations";
            var storedID = id.ToString("N").ToUpperInvariant();
            await using (var connection = await database.OpenAsync(token))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"UPDATE {table} SET id=$stored,owner_binding_json=$owner WHERE id=$id;";
                command.Parameters.AddWithValue("$stored", storedID); command.Parameters.AddWithValue("$id", id.ToString());
                command.Parameters.AddWithValue("$owner", protect ? "{\"unknownOwnerBinding\":true}" : DBNull.Value);
                Assert.Equal(1, await command.ExecuteNonQueryAsync(token));
                if (ambiguous)
                {
                    command.Parameters.Clear();
                    command.CommandText = reusable
                        ? "INSERT INTO reusable_tasks(id,name,description,instruction,container_id,is_enabled,created_at,updated_at,graph_json) SELECT $alias,name,description,instruction,container_id,is_enabled,created_at,updated_at,graph_json FROM reusable_tasks WHERE id=$stored;"
                        : "INSERT INTO automations(id,name,mode,instruction,schedule_kind,schedule_json,next_run_at,container_id,is_enabled,created_at,updated_at) SELECT $alias,name,mode,instruction,schedule_kind,schedule_json,next_run_at,container_id,is_enabled,created_at,updated_at FROM automations WHERE id=$stored;";
                    command.Parameters.AddWithValue("$alias", "{" + id.ToString("D") + "}"); command.Parameters.AddWithValue("$stored", storedID);
                    Assert.Equal(1, await command.ExecuteNonQueryAsync(token));
                }
            }
            async Task<string> RawRows()
            {
                await using var connection = await database.OpenAsync(token);
                await using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY id;";
                await using var reader = await command.ExecuteReaderAsync(token);
                var rows = new List<Dictionary<string, string?>>();
                while (await reader.ReadAsync(token))
                {
                    var row = new Dictionary<string, string?>();
                    for (var index = 0; index < reader.FieldCount; index++) row.Add(reader.GetName(index), reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture));
                    rows.Add(row);
                }
                return JsonSerializer.Serialize(rows);
            }
            var before = await RawRows();
            if (protect || ambiguous)
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => Save("Attempted overwrite"));
                Assert.Equal(before, await RawRows()); // Unknown descriptors, original graph/schedule and all payload identities survive refusal.
            }
            else
            {
                await Save("Allowed unbound update");
                var rows = JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(await RawRows())!;
                var row = Assert.Single(rows); Assert.Equal(storedID, row["id"]); Assert.Equal("Allowed unbound update", row["name"]);
                Assert.Null(row["owner_binding_json"]); Assert.Null(row["owner_commit_receipt_json"]);
                Assert.Equal(reusable ? task.GraphJson : definition.ScheduleJson, row[reusable ? "graph_json" : "schedule_json"]);
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "app.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
