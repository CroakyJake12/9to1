using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Genuine SAME initialized SQLite/current OS/Home import. PRIVATE, UNRUN.
public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    private static (CapabilityRegistryService Registry, CanonicalCapabilityCatalogueReadOwner Catalogue,
        ExternalConnectionRepository Connections, PlannerRepository Planner, ConnectionCapabilityProvider Provider,
        CanonicalConnectionCapabilityReadOwner Dynamic) DynamicCatalogue(Rig rig, bool compose = true)
    {
        var repository = new CapabilityRepository(rig.Database);
        var connections = new ExternalConnectionRepository(rig.Database); var planner = new PlannerRepository(rig.Database);
        var provider = new ConnectionCapabilityProvider(connections, planner);
        var registry = new CapabilityRegistryService(repository, [provider]);
        var catalogue = new CanonicalCapabilityCatalogueReadOwner(rig.Store, rig.Database, rig.OriginalPaths,
            repository, registry, new HavenOS.Home.Core.HomeResourceStoreOwnershipAuthority(rig.Ownership, rig.Profiles));
        var dynamic = new CanonicalConnectionCapabilityReadOwner(catalogue, rig.Database, connections, planner, provider);
        if (compose) registry.BindOriginalDynamicReadSources([dynamic]);
        return (registry, catalogue, connections, planner, provider, dynamic);
    }
    private static ExternalConnection ActualConnection(string name, ExternalConnectionState state = ExternalConnectionState.Ready) =>
        new(Guid.NewGuid(), name, "fixture.metadata", ExternalConnectionKind.Mcp, "fixture", true, state, "metadata",
            "{\"privateFixtureMarker\":\"never read by protected capability metadata\"}", null, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [LinuxOriginalStoreFact]
    public Task Protected_dynamic_read_uses_actual_provider_projection_without_seeding() => Run(async rig =>
    {
        var graph = DynamicCatalogue(rig); var connection = ActualConnection("actual configured fixture");
        await rig.Keep(graph.Connections.UpsertAsync(connection, rig.Token));
        var now = DateTimeOffset.UtcNow;
        var calendar = new CalendarAccount(Guid.NewGuid(), CalendarProviderKind.Google, "private fixture display",
            "fixture.account.identifier", CalendarSyncStatus.Offline, null, now, now, now);
        await rig.Keep(graph.Planner.UpsertCalendarAccountAsync(calendar, rig.Token));
        await rig.Import("canonical.sqlite"); var count = await CapabilityCount(rig);
        var actual = await rig.Keep(graph.Registry.DiscoverWithinOriginalSourceAsync(graph.Catalogue, rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(CapabilityOriginalCatalogueState.Available, actual.State); Assert.False(actual.HasUnscopedDynamicProviders);
        Assert.Equal(2, actual.Definitions.Count);
        var expected = graph.Provider.ProjectOriginalCapabilityMetadata(
            [new(connection.Id, connection.Name, connection.Kind, connection.IsEnabled, connection.State, connection.UpdatedAt)],
            [new(calendar.Id, calendar.Provider, calendar.Status, calendar.LastSyncedAt, calendar.UpdatedAt)], CapabilityPlatform.Linux);
        Assert.Equal(expected.OrderBy(value => value.Id), actual.Definitions.OrderBy(value => value.Id));
        Assert.Equal(CapabilityAvailability.PermissionRequired, Assert.Single(actual.Definitions, value => value.Id == calendar.Id).Availability);
        Assert.Equal(count, await CapabilityCount(rig));
    });

    [LinuxOriginalStoreFact]
    public Task Protected_dynamic_metadata_never_reads_configuration_or_account_identity_columns() => Run(async rig =>
    {
        var graph = DynamicCatalogue(rig); var connection = ActualConnection("actual metadata only");
        await rig.Keep(graph.Connections.UpsertAsync(connection, rig.Token));
        await using (var database = await rig.Keep(rig.Database.OpenAsync(rig.Token)))
        {
            await using var command = database.CreateCommand();
            command.CommandText = "ALTER TABLE external_connections RENAME COLUMN configuration_json TO preserved_private_configuration;";
            await rig.Keep(command.ExecuteNonQueryAsync(rig.Token));
            command.CommandText = "ALTER TABLE calendar_accounts RENAME COLUMN account_identifier TO preserved_private_account_identity;";
            await rig.Keep(command.ExecuteNonQueryAsync(rig.Token));
        }
        await rig.Import("canonical.sqlite");
        var observed = await rig.Keep(graph.Registry.DiscoverWithinOriginalSourceAsync(graph.Catalogue, rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(connection.Id, Assert.Single(observed.Definitions).Id);
        Assert.False(observed.HasUnscopedDynamicProviders);
    });

    [LinuxOriginalStoreFact]
    public Task Uncomposed_dynamic_provider_does_not_open_its_ordinary_absent_database() => Run(async rig =>
    {
        var paths = new Paths(Path.Combine(rig.Root, "ordinary-provider-uninitialized")); var ordinaryDatabase = new SqliteDatabase(paths);
        var ordinary = new ConnectionCapabilityProvider(new ExternalConnectionRepository(ordinaryDatabase), new PlannerRepository(ordinaryDatabase));
        var repository = new CapabilityRepository(rig.Database); var registry = new CapabilityRegistryService(repository, [ordinary]);
        var source = new CanonicalCapabilityCatalogueReadOwner(rig.Store, rig.Database, rig.OriginalPaths, repository, registry,
            new HavenOS.Home.Core.HomeResourceStoreOwnershipAuthority(rig.Ownership, rig.Profiles));
        await rig.Import("canonical.sqlite");
        var observed = await rig.Keep(registry.DiscoverWithinOriginalSourceAsync(source, rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        Assert.True(observed.HasUnscopedDynamicProviders); Assert.Empty(observed.Definitions);
        Assert.False(File.Exists(paths.DatabasePath)); Assert.False(Directory.Exists(paths.DataDirectory));
    });

    [LinuxOriginalStoreFact]
    public Task Missing_dynamic_table_reports_setup_without_schema_initialization() => Run(async rig =>
    {
        var graph = DynamicCatalogue(rig);
        await using (var database = await rig.Keep(rig.Database.OpenAsync(rig.Token)))
        {
            await using var command = database.CreateCommand();
            command.CommandText = "ALTER TABLE external_connections RENAME TO preserved_original_connections;";
            await rig.Keep(command.ExecuteNonQueryAsync(rig.Token));
        }
        await rig.Import("canonical.sqlite");
        var actual = await rig.Keep(graph.Dynamic.ReadOriginalDynamicCapabilitiesWithinSourceAsync(rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(CapabilityOriginalCatalogueState.SetupRequired, actual.State); Assert.Empty(actual.Definitions);
        var joined = await rig.Keep(graph.Registry.DiscoverWithinOriginalSourceAsync(graph.Catalogue, rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        Assert.True(joined.HasUnscopedDynamicProviders); Assert.Empty(joined.Definitions);
        await using var check = await rig.Keep(rig.Database.OpenAsync(rig.Token));
        await using var query = check.CreateCommand(); query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='external_connections';";
        Assert.Equal(0L, Convert.ToInt64(await rig.Keep(query.ExecuteScalarAsync(rig.Token))));
    });

    [LinuxOriginalStoreFact]
    public Task Changed_actual_connection_state_invalidates_its_original_metadata_observation() => Run(async rig =>
    {
        var graph = DynamicCatalogue(rig); var row = ActualConnection("actual connection state");
        await rig.Keep(graph.Connections.UpsertAsync(row, rig.Token)); await rig.Import("canonical.sqlite");
        var actual = await rig.Keep(graph.Dynamic.ReadOriginalDynamicCapabilitiesWithinSourceAsync(rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(CapabilityAvailability.PermissionRequired, Assert.Single(actual.Definitions).Availability);
        await rig.Keep(graph.Connections.UpsertAsync(row with { State = ExternalConnectionState.Offline, UpdatedAt = row.UpdatedAt.AddSeconds(1) }, rig.Token));
        var refusal = rig.Keep(graph.Dynamic.RevalidateOriginalDynamicObservationWithinSourceAsync(actual,
            rig.Actor, rig.Scope, rig.Retain, rig.Token));
        var cause = await Assert.ThrowsAnyAsync<Exception>(() => refusal); rig.Expect(cause);
        var fresh = await rig.Keep(graph.Dynamic.ReadOriginalDynamicCapabilitiesWithinSourceAsync(rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(CapabilityAvailability.DependencyRequired, Assert.Single(fresh.Definitions).Availability);
    });

    [LinuxOriginalStoreFact]
    public Task Dynamic_actual_reader_is_closed_before_post_retainer_io_refusal_returns() => Run(async rig =>
    {
        var graph = DynamicCatalogue(rig); await rig.Import("canonical.sqlite");
        Task<SqliteDataReader>? rawReader = null; var once = 1; var io = new IOException("actual dynamic metadata publication IO");
        void Retain(Task actual)
        {
            rig.Retain(actual);
            if (actual is Task<SqliteDataReader> reader && Interlocked.Exchange(ref once, 0) != 0) { rawReader = reader; throw io; }
        }
        var operation = rig.Keep(graph.Dynamic.ReadOriginalDynamicCapabilitiesWithinSourceAsync(rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, Retain, rig.Token));
        var cause = await Assert.ThrowsAnyAsync<Exception>(() => operation); rig.Expect(cause); rig.Expect(io);
        Assert.Contains(io, References(cause)); Assert.NotNull(rawReader); Assert.True((await rawReader!).IsClosed);
    });

    [LinuxOriginalStoreFact]
    public Task Same_name_dynamic_view_is_corrupt_schema_and_never_setup_absence() => Run(async rig =>
    {
        var graph = DynamicCatalogue(rig);
        await using (var database = await rig.Keep(rig.Database.OpenAsync(rig.Token)))
        {
            await using var command = database.CreateCommand();
            command.CommandText = "ALTER TABLE external_connections RENAME TO preserved_original_connections;";
            await rig.Keep(command.ExecuteNonQueryAsync(rig.Token));
            command.CommandText = "CREATE VIEW external_connections AS SELECT * FROM preserved_original_connections;";
            await rig.Keep(command.ExecuteNonQueryAsync(rig.Token));
        }
        await rig.Import("canonical.sqlite");
        var operation = rig.Keep(graph.Dynamic.ReadOriginalDynamicCapabilitiesWithinSourceAsync(rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        var cause = await Assert.ThrowsAnyAsync<Exception>(() => operation); rig.Expect(cause);
        Assert.Contains(References(cause), value => value is InvalidDataException);
    });

    [LinuxOriginalStoreFact]
    public Task Actual_dynamic_composition_is_immutable_before_and_after_discovery() => Run(async rig =>
    {
        var graph = DynamicCatalogue(rig);
        Assert.Same(graph.Dynamic, Assert.Single(graph.Registry.OriginalDynamicReadSources));
        Assert.Throws<InvalidOperationException>(() => graph.Registry.BindOriginalDynamicReadSources([graph.Dynamic]));
        await rig.Import("canonical.sqlite");
        await rig.Keep(graph.Registry.DiscoverWithinOriginalSourceAsync(graph.Catalogue, rig.Actor,
            CapabilityPlatform.Linux, rig.Scope, rig.Retain, rig.Token));
        Assert.Throws<InvalidOperationException>(() => graph.Registry.BindOriginalDynamicReadSources([]));
    });
}
