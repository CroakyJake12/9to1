using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed class PlannerLocalStoreEvidenceProviderTests
{
    [Fact]
    public async Task Spaces_evidence_uses_actual_settings_uuid_and_persisted_registry_snapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-spaces-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new Paths(root);
            var settings = new VersionedAtomicSettingsStore(paths);
            var identity = await settings.GetStoreIdentityAsync(TestContext.Current.CancellationToken);
            var provider = new SpacesLocalStoreEvidenceProvider(settings, settings);
            var initial = await provider.ReadAsync(identity.StoreId.ToString("D"), TestContext.Current.CancellationToken);
            Assert.NotNull(initial);
            Assert.True(initial.NewlyCreated);
            Assert.True(initial.IsEmpty);
            await new SpaceRegistry(settings).CreateAsync("Canonical custom Space", cancellationToken: TestContext.Current.CancellationToken);
            var changed = await provider.ReadAsync(identity.StoreId.ToString("D"), TestContext.Current.CancellationToken);
            Assert.NotNull(changed);
            Assert.False(changed.IsEmpty);
            Assert.NotEqual(initial.Revision, changed.Revision);
            var reopened = new VersionedAtomicSettingsStore(paths);
            var restarted = await new SpacesLocalStoreEvidenceProvider(reopened, reopened).ReadAsync(identity.StoreId.ToString("D"), TestContext.Current.CancellationToken);
            Assert.NotNull(restarted);
            Assert.False(restarted.NewlyCreated);
            Assert.Equal(changed.Revision, restarted.Revision);
            Assert.Null(await provider.ReadAsync(Guid.NewGuid().ToString("D"), TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Actual_store_identity_empty_snapshot_and_reopen_require_current_canonical_evidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-planner-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new Paths(root);
            var database = new SqliteDatabase(paths);
            await database.InitializeAsync(TestContext.Current.CancellationToken);
            var identity = await database.GetStoreIdentityAsync(TestContext.Current.CancellationToken);
            var provider = new PlannerLocalStoreEvidenceProvider(database, database);
            var first = await provider.ReadAsync(identity.StoreId.ToString("D"), TestContext.Current.CancellationToken);
            Assert.NotNull(first);
            Assert.True(first.NewlyCreated);
            // Canonical migrations already seed collections/calendars; no implicit ownership claim.
            Assert.False(first.IsEmpty);
            Assert.Null(await provider.ReadAsync(Guid.NewGuid().ToString("D"), TestContext.Current.CancellationToken));
            await using (var connection = await database.OpenAsync(TestContext.Current.CancellationToken))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO planner_collections(id,name,sort_order,created_at,updated_at) VALUES($id,'Existing',0,$now,$now)";
                command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            var changed = await provider.ReadAsync(identity.StoreId.ToString("D"), TestContext.Current.CancellationToken);
            Assert.NotNull(changed);
            Assert.False(changed.IsEmpty);
            Assert.NotEqual(first.Revision, changed.Revision);
            var reopened = new SqliteDatabase(paths);
            var afterRestart = await new PlannerLocalStoreEvidenceProvider(reopened, reopened).ReadAsync(identity.StoreId.ToString("D"), TestContext.Current.CancellationToken);
            Assert.NotNull(afterRestart);
            Assert.False(afterRestart.NewlyCreated);
            Assert.Equal(changed.Revision, afterRestart.Revision);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "store.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
