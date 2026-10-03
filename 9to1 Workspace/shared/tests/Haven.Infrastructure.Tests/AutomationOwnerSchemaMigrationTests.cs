using System.Text.Json;
using Haven.Application;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure.Tests;

public sealed class AutomationOwnerSchemaMigrationTests
{
    [Fact]
    public async Task Real_historical_27_rows_payloads_and_disabled_identity_survive_additive_owner_migration_and_repeat_init()
    {
        using var paths = new Paths();
        await CreateHistorical27(paths);
        var before = await Snapshot(paths);
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(default);
        await database.InitializeAsync(default);
        Assert.Equal(before, await Snapshot(paths));
        await using var connection = await database.OpenAsync(default);
        foreach (var table in new[] { "automations", "reusable_tasks" })
        {
            Assert.Equal(0L, await Scalar(connection, $"SELECT revision FROM {table};"));
            Assert.Equal(0L, await Scalar(connection, $"SELECT operational_state FROM {table};"));
            Assert.Equal(1L, await Scalar(connection, $"SELECT COUNT(*) FROM {table} WHERE owner_binding_json IS NULL AND graph_binding_json IS NULL AND archived_at IS NULL AND publication_journal_json IS NULL AND definition_metadata_json IS NULL AND owner_commit_receipt_json IS NULL;"));
        }
        Assert.Equal(1L, await Scalar(connection, "SELECT COUNT(*) FROM automation_runs WHERE revision=0 AND definition_revision IS NULL AND pinned_graph_json IS NULL AND admission_snapshot_json IS NULL AND continuation_descriptor_json IS NULL AND run_details_json IS NULL;"));
        Assert.Equal(28L, await Scalar(connection, "SELECT MAX(version) FROM schema_migrations;"));
    }

    [Fact]
    public async Task Existing_incompatible_last_column_rolls_back_entire_additive_transaction_and_preserves_legacy_rows()
    {
        using var paths = new Paths();
        await CreateHistorical27(paths);
        await using (var connection = new SqliteConnection($"Data Source={paths.DatabasePath}"))
        {
            await connection.OpenAsync();
            await Execute(connection, "ALTER TABLE reusable_tasks ADD COLUMN owner_commit_receipt_json TEXT; UPDATE reusable_tasks SET owner_commit_receipt_json='unrecognised original descriptor';");
        }
        var before = await Snapshot(paths);
        var database = new SqliteDatabase(paths);
        await Assert.ThrowsAsync<SqliteException>(() => database.InitializeAsync(default));
        Assert.Equal(before, await Snapshot(paths));
        await using var current = await database.OpenAsync(default);
        Assert.Equal(27L, await Scalar(current, "SELECT MAX(version) FROM schema_migrations;"));
        foreach (var table in new[] { "automations", "reusable_tasks", "automation_runs" })
        {
            var columns = await Columns(current, table);
            Assert.DoesNotContain("revision", columns);
            Assert.DoesNotContain("owner_binding_json", columns);
            Assert.DoesNotContain("definition_metadata_json", columns);
            Assert.DoesNotContain("continuation_descriptor_json", columns);
        }
        Assert.Equal("unrecognised original descriptor", Convert.ToString(await ScalarObject(current,
            "SELECT owner_commit_receipt_json FROM reusable_tasks;")));
    }

    private static async Task CreateHistorical27(Paths paths)
    {
        _ = new SqliteDatabase(paths);
        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
        await connection.OpenAsync();
        await Execute(connection, "PRAGMA foreign_keys=ON; CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY,applied_at TEXT NOT NULL);");
        foreach (var migration in Migrations.All.Where(migration => migration.Version <= 27).OrderBy(migration => migration.Version))
        {
            await Execute(connection, migration.Sql);
            await Execute(connection, $"INSERT INTO schema_migrations VALUES({migration.Version},'2026-10-01T00:00:00Z');");
        }
        await Execute(connection, """
            INSERT INTO automations(id,name,mode,instruction,schedule_kind,schedule_json,is_enabled,created_at,updated_at)
            VALUES('preserved-automation','Original title',3,'Original instruction',0,'unrecognised legacy schedule',0,'original-created','original-updated');
            INSERT INTO reusable_tasks(id,name,description,instruction,is_enabled,created_at,updated_at,graph_json)
            VALUES('preserved-reusable','Disabled reusable','Original description','Original instruction',0,'original-created','original-updated','{"schema":999,"unknown":"must retain"}');
            INSERT INTO automation_runs(id,automation_id,status,scheduled_for,result,error,lease_token)
            VALUES('preserved-run','preserved-automation',2,'original-scheduled','Original result','Original error','original-lease');
            """);
    }

    private static async Task<string> Snapshot(Paths paths)
    {
        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
        await connection.OpenAsync();
        var result = new Dictionary<string, List<object?[]>>();
        var fields = new Dictionary<string, string>
        {
            ["automations"] = "id,name,mode,instruction,schedule_kind,schedule_json,next_run_at,container_id,is_enabled,created_at,updated_at,lease_token,lease_until",
            ["reusable_tasks"] = "id,name,description,instruction,container_id,is_enabled,created_at,updated_at,graph_json",
            ["automation_runs"] = "id,automation_id,status,scheduled_for,started_at,completed_at,result,error,lease_token"
        };
        foreach (var (table, columns) in fields)
        {
            await using var command = connection.CreateCommand(); command.CommandText = $"SELECT {columns} FROM {table} ORDER BY id;";
            await using var reader = await command.ExecuteReaderAsync(); var rows = new List<object?[]>();
            while (await reader.ReadAsync()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
            result.Add(table, rows);
        }
        return JsonSerializer.Serialize(result);
    }
    private static async Task<List<string>> Columns(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand(); command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(); var names = new List<string>();
        while (await reader.ReadAsync()) names.Add(reader.GetString(1)); return names;
    }
    private static async Task Execute(SqliteConnection connection, string sql)
    { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
    private static async Task<object?> ScalarObject(SqliteConnection connection, string sql)
    { await using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
    private static async Task<long> Scalar(SqliteConnection connection, string sql) => Convert.ToInt64(await ScalarObject(connection, sql));
    private sealed class Paths : IAppPaths, IDisposable
    {
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-automation-migration-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory,"canonical.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory,"browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory,"attachments");
        public string LogsDirectory => Path.Combine(DataDirectory,"logs");
        public string LegacyStatePath => Path.Combine(DataDirectory,"missing.json");
        public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(DataDirectory,true); }
    }
}
