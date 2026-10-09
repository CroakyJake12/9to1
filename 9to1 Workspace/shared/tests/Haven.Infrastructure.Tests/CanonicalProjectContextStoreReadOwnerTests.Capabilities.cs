using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Genuine initialized SQLite/current OS/Home manual import. PRIVATE, UNRUN.
public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    private static (CapabilityRepository Repository, CapabilityRegistryService Registry, CanonicalCapabilityCatalogueReadOwner Source) Catalogue(Rig rig)
    {
        var repository = new CapabilityRepository(rig.Database);
        var registry = new CapabilityRegistryService(repository);
        var source = new CanonicalCapabilityCatalogueReadOwner(rig.Store, rig.Database, rig.OriginalPaths,
            repository, registry, new HavenOS.Home.Core.HomeResourceStoreOwnershipAuthority(rig.Ownership, rig.Profiles));
        return (repository, registry, source);
    }

    [LinuxOriginalStoreFact]
    public Task Protected_capability_read_does_not_seed_an_empty_initialized_catalogue() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite"); var graph = Catalogue(rig);
        Assert.Equal(0L, await CapabilityCount(rig));
        var observed = await rig.Keep(graph.Registry.DiscoverWithinOriginalSourceAsync(graph.Source, rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(CapabilityOriginalCatalogueState.Available, observed.State);
        Assert.True(graph.Registry.IsIssuedOriginalCatalogue(observed)); Assert.Empty(observed.Definitions);
        Assert.Equal(0L, await CapabilityCount(rig));
        await rig.Keep(graph.Source.RevalidateOriginalRepositoryObservationWithinSourceAsync(
            await rig.Keep(graph.Source.ReadOriginalCapabilitiesWithinSourceAsync(rig.Actor, rig.Scope, rig.Retain, rig.Token)),
            rig.Actor, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(0L, await CapabilityCount(rig));
    });

    [LinuxOriginalStoreFact]
    public Task First_use_protected_catalogue_does_not_create_an_absent_database_or_directory() => Run(async rig =>
    {
        var paths = new Paths(Path.Combine(rig.Root, "uninitialized-private-source"));
        var database = new SqliteDatabase(paths); var store = new CanonicalSqliteOriginalStoreOwner(database, paths, rig.Profiles);
        var repository = new CapabilityRepository(database); var registry = new CapabilityRegistryService(repository);
        var source = new CanonicalCapabilityCatalogueReadOwner(store, database, paths, repository, registry,
            new HavenOS.Home.Core.HomeResourceStoreOwnershipAuthority(rig.Ownership, rig.Profiles));
        try
        {
            Assert.False(Directory.Exists(paths.DataDirectory)); Assert.False(File.Exists(paths.DatabasePath));
            var observed = await rig.Keep(registry.DiscoverWithinOriginalSourceAsync(source, rig.Actor,
                CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
            Assert.Equal(CapabilityOriginalCatalogueState.SetupRequired, observed.State); Assert.Empty(observed.Definitions);
            Assert.False(Directory.Exists(paths.DataDirectory)); Assert.False(File.Exists(paths.DatabasePath));
        }
        finally { await rig.Keep(store.CloseAndDrainAsync()); }
    });

    [LinuxOriginalStoreFact]
    public Task Existing_missing_capability_table_returns_setup_without_schema_initialization() => Run(async rig =>
    {
        // Fixture-owned write preserves the original table rather than deleting it.
        await using (var connection = await rig.Keep(rig.Database.OpenAsync(rig.Token)))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE capabilities RENAME TO capabilities_preserved_for_recovery;";
            await rig.Keep(command.ExecuteNonQueryAsync(rig.Token));
        }
        await rig.Import("canonical.sqlite"); var graph = Catalogue(rig);
        var observed = await rig.Keep(graph.Registry.DiscoverWithinOriginalSourceAsync(graph.Source, rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(CapabilityOriginalCatalogueState.SetupRequired, observed.State); Assert.Empty(observed.Definitions);
        await using var check = await rig.Keep(rig.Database.OpenAsync(rig.Token));
        await using var query = check.CreateCommand(); query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='capabilities';";
        Assert.Equal(0L, Convert.ToInt64(await rig.Keep(query.ExecuteScalarAsync(rig.Token))));
        query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='capabilities_preserved_for_recovery';";
        Assert.Equal(1L, Convert.ToInt64(await rig.Keep(query.ExecuteScalarAsync(rig.Token))));
    });

    [LinuxOriginalStoreFact]
    public Task Legacy_kind_import_does_not_disclose_capability_rows() => Run(async rig =>
    {
        await rig.Import("legacy.saved-agents"); var graph = Catalogue(rig); var before = rig.MetadataReaderTasks;
        var observed = await rig.Keep(graph.Registry.DiscoverWithinOriginalSourceAsync(graph.Source, rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(CapabilityOriginalCatalogueState.SetupRequired, observed.State); Assert.Empty(observed.Definitions);
        Assert.Equal(before, rig.MetadataReaderTasks); Assert.Equal(0L, await CapabilityCount(rig));
    });

    [LinuxOriginalStoreFact]
    public Task Selected_existing_rows_revalidate_against_actual_removed_catalogue_key() => Run(async rig =>
    {
        var graph = Catalogue(rig);
        // Explicit fixture-owning write lifecycle, not the protected READ discovery.
        var seeded = await rig.Keep(graph.Repository.GetCapabilitiesAsync(rig.Token)); Assert.NotEmpty(seeded);
        await rig.Import("canonical.sqlite");
        var original = await rig.Keep(graph.Source.ReadOriginalCapabilitiesWithinSourceAsync(rig.Actor, rig.Scope, rig.Retain, rig.Token));
        var row = Assert.Single(original.Definitions, value => value.Key == "read-file");
        await rig.Keep(graph.Repository.SetCapabilityEnabledAsync(row.Id, false, rig.Token));
        var changed = rig.Keep(graph.Source.RevalidateOriginalRepositoryObservationWithinSourceAsync(original,
            rig.Actor, rig.Scope, rig.Retain, rig.Token));
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => changed); rig.Expect(failure);
        var fresh = await rig.Keep(graph.Source.ReadOriginalCapabilitiesWithinSourceAsync(rig.Actor, rig.Scope, rig.Retain, rig.Token));
        Assert.DoesNotContain(fresh.Definitions, value => value.Id == row.Id);
    });

    [LinuxOriginalStoreFact]
    public Task Maintained_selected_subset_intersects_model_and_Ask_without_broad_workspace_or_dynamic_names() => Run(async rig =>
    {
        var read = ActiveCapability.FromDefinition(Assert.Single(CapabilityRegistryCatalog.BuiltIns, value => value.Key == "read-file"));
        var write = ActiveCapability.FromDefinition(Assert.Single(CapabilityRegistryCatalog.BuiltIns, value => value.Key == "write-file"));
        OllamaToolDefinition Definition(string name) => new(name, name, new Dictionary<string, object>(), []);
        var sources = new ToolDefinitionSources([Definition("read_file"), Definition("write_file"), Definition("run_command")], [], [], [], [], [], [Definition("mcp_unselected")]);
        var plan = ToolAvailabilityPlanner.Default.Create(new ToolAvailabilityContext(HavenMode.Tasks, rig.Root,
            [read, write], PermissionMode.Ask, PermissionMode.Ask, PermissionMode.Ask, false, false, false, false), sources);
        var toolModel = new ModelDescriptor("actual-metadata-tools", 1, "test", "test", "test",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UtcNow);
        var textModel = toolModel with { Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        Assert.Equal("read_file", Assert.Single(ToolAvailabilityPlanner.Default.GetOriginalConfiguredDefinitions(plan.RestrictToModel(toolModel), [read])).Name);
        Assert.Empty(ToolAvailabilityPlanner.Default.GetOriginalConfiguredDefinitions(plan.RestrictToModel(toolModel), [write]));
        Assert.Empty(ToolAvailabilityPlanner.Default.GetOriginalConfiguredDefinitions(plan.RestrictToModel(textModel), [read]));
        Assert.Empty(ToolAvailabilityPlanner.Default.GetOriginalConfiguredDefinitions(plan.RestrictToModel(toolModel), []));
        Assert.DoesNotContain(ToolAvailabilityPlanner.Default.GetOriginalConfiguredDefinitions(plan.RestrictToModel(toolModel), [read]), value => value.Name == "mcp_unselected");
        await Task.CompletedTask;
    });

    [LinuxOriginalStoreFact]
    public Task Actual_catalogue_reader_is_closed_before_post_retainer_io_refusal_returns() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite"); var graph = Catalogue(rig);
        Task<SqliteDataReader>? actualReader = null; var refuse = 1;
        var io = new IOException("actual protected catalogue reader publication IO");
        void Retain(Task actual)
        {
            rig.Retain(actual);
            if (actual is Task<SqliteDataReader> reader && Interlocked.Exchange(ref refuse, 0) != 0)
            { actualReader = reader; throw io; }
        }
        var operation = rig.Keep(graph.Source.ReadOriginalCapabilitiesWithinSourceAsync(rig.Actor, rig.Scope, Retain, rig.Token));
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => operation); rig.Expect(failure); rig.Expect(io);
        Assert.Contains(io, References(failure)); Assert.NotNull(actualReader);
        Assert.True((await actualReader!).IsClosed); Assert.True(operation.IsFaulted);
    });

    [LinuxOriginalStoreFact]
    public Task Missing_existing_identity_returns_setup_without_seeding_or_rekeying() => Run(async rig =>
    {
        await using (var connection = await rig.Keep(rig.Database.OpenAsync(rig.Token)))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE settings SET key=$preserved WHERE key=$original;";
            command.Parameters.AddWithValue("$preserved", "fixture.preserved.original.canonical.sqlite.identity");
            command.Parameters.AddWithValue("$original", CanonicalSqliteOriginalStoreOwner.IdentityKey);
            Assert.Equal(1, await rig.Keep(command.ExecuteNonQueryAsync(rig.Token)));
        }
        var graph = Catalogue(rig);
        var observed = await rig.Keep(graph.Registry.DiscoverWithinOriginalSourceAsync(graph.Source, rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(CapabilityOriginalCatalogueState.SetupRequired, observed.State); Assert.Empty(observed.Definitions);
        await using var check = await rig.Keep(rig.Database.OpenAsync(rig.Token));
        await using var query = check.CreateCommand(); query.CommandText = "SELECT COUNT(*) FROM settings WHERE key=$key;";
        query.Parameters.AddWithValue("$key", CanonicalSqliteOriginalStoreOwner.IdentityKey);
        Assert.Equal(0L, Convert.ToInt64(await rig.Keep(query.ExecuteScalarAsync(rig.Token))));
        query.Parameters["$key"].Value = "fixture.preserved.original.canonical.sqlite.identity";
        Assert.Equal(1L, Convert.ToInt64(await rig.Keep(query.ExecuteScalarAsync(rig.Token))));
    });

    private static async Task<long> CapabilityCount(Rig rig)
    {
        await using var connection = await rig.Keep(rig.Database.OpenAsync(rig.Token));
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM capabilities;";
        return Convert.ToInt64(await rig.Keep(command.ExecuteScalarAsync(rig.Token)));
    }
}
