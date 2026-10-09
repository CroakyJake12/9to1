using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>READ-only maintained capability rows behind the SAME protected SQLite
/// identity/current actor and distinct current canonical.sqlite Home receipt. Neither
/// Den ownership nor this observation grants any tool/project/effect permission.</summary>
public sealed partial class CanonicalCapabilityCatalogueReadOwner : ICapabilityOriginalRepositoryReadSource
{
    private readonly CanonicalSqliteOriginalStoreOwner _store;
    private readonly CapabilityRepository _repository;
    private readonly CapabilityRegistryService _registry;
    private readonly IAppPaths _paths;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly ConditionalWeakTable<ICapabilityOriginalRepositoryObservation, Observation> _issued = new();
    private sealed class Observation(CanonicalCapabilityCatalogueReadOwner owner, AuthenticatedResourceActor actor,
        CapabilityOriginalCatalogueState state, string detail, IReadOnlyList<CapabilityDefinition> definitions,
        ResourceStoreIdentity? identity = null, VerifiedResourceStoreOwnership? receipt = null)
        : ICapabilityOriginalRepositoryObservation
    {
        internal readonly CanonicalCapabilityCatalogueReadOwner Owner = owner;
        internal readonly ResourceStoreIdentity? Identity = identity;
        internal readonly VerifiedResourceStoreOwnership? Receipt = receipt;
        public AuthenticatedResourceActor Actor { get; } = actor;
        public CapabilityOriginalCatalogueState State { get; } = state;
        public string Detail { get; } = detail;
        public IReadOnlyList<CapabilityDefinition> Definitions { get; } = definitions;
    }
    public CanonicalCapabilityCatalogueReadOwner(CanonicalSqliteOriginalStoreOwner sameStore,
        SqliteDatabase sameDatabase, IAppPaths samePaths, CapabilityRepository sameRepository,
        CapabilityRegistryService sameRegistry, HomeResourceStoreOwnershipAuthority sameOwnership)
    {
        if (!sameStore.HasOriginalComposition(sameDatabase, samePaths, sameStore.OriginalProfiles) ||
            !sameRepository.HasOriginalSqliteFactory(sameDatabase) || !sameRegistry.HasOriginalRepository(sameRepository))
            throw new InvalidOperationException("The SAME original SQLite/paths/repository/registry objects are required.");
        _store = sameStore; _paths = samePaths; _repository = sameRepository; _registry = sameRegistry; _ownership = sameOwnership;
    }
    public CanonicalSqliteOriginalStoreOwner OriginalStore => _store;
    public CapabilityRepository OriginalRepository => _repository;
    public CapabilityRegistryService OriginalRegistry => _registry;
    public HomeResourceStoreOwnershipAuthority OriginalOwnership => _ownership;
    public bool HasOriginalCapabilityRepository(ICapabilityRepository sameActual) => ReferenceEquals(_repository, sameActual);
    public bool IsIssuedOriginalRepositoryObservation(ICapabilityOriginalRepositoryObservation sameActual) =>
        sameActual is Observation actual && ReferenceEquals(actual.Owner, this) &&
        _issued.TryGetValue(sameActual, out var issued) && ReferenceEquals(actual, issued);

    public Task RevalidateOriginalRepositoryObservationWithinSourceAsync(
        ICapabilityOriginalRepositoryObservation sameActual, AuthenticatedResourceActor actor,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, scope, retain);
        return _store.RetainOriginalReader(source, async () =>
        {
            var original = source.Invoke(() => IsIssuedOriginalRepositoryObservation(sameActual) &&
                sameActual.Actor == actor ? (Observation)sameActual :
                throw new UnauthorizedAccessException("The SAME capability source/actor did not issue this observation."));
            var fresh = (Observation)await source.Read(() => ReadOriginalCapabilitiesWithinSourceAsync(
                actor, source.Run, source.Retain, token)).ConfigureAwait(false);
            source.Run(() =>
            {
                if (original.State != fresh.State || original.Identity != fresh.Identity ||
                    original.Receipt != fresh.Receipt ||
                    JsonSerializer.Serialize(original.Definitions) != JsonSerializer.Serialize(fresh.Definitions))
                    throw new UnauthorizedAccessException("The actual protected capability catalogue or Home READ ownership changed. Refresh it before use.");
            });
            await source.JoinAllAsync().ConfigureAwait(false); return 0;
        });
    }

    public Task<ICapabilityOriginalRepositoryObservation> ReadOriginalCapabilitiesWithinSourceAsync(
        AuthenticatedResourceActor actor, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, scope, retain);
        return _store.RetainOriginalReader<ICapabilityOriginalRepositoryObservation>(source, async () =>
        {
            await DemandActorAsync(actor, source, token).ConfigureAwait(false);
            // Absence is a negative setup observation only, never positive path authority.
            // Existing files still require the actual protected native/SQLite owner below.
            var exists = source.Invoke(() =>
            {
                try { _ = File.GetAttributes(_paths.DatabasePath); return true; }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
            });
            if (!exists) return await SetupAsync("The existing canonical SQLite database is unavailable. Initialise it through its actual owning lifecycle.").ConfigureAwait(false);
            var identity = await source.Read(() => _store.ReadExistingStoreIdentityWithinOriginalSourceAsync(actor,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            if (identity is null) return await SetupAsync("The existing canonical SQLite identity is missing. Preserve the store for its actual owning recovery/setup lifecycle.").ConfigureAwait(false);
            var ownership = await source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync("canonical.sqlite",
                identity.StoreId.ToString("D"), source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
            if (ownership is null) return await SetupAsync("Import the actual canonical SQLite store through Home before reading its capability catalogue.").ConfigureAwait(false);
            await DemandReceiptAsync(ownership, identity, actor, source, token).ConfigureAwait(false);

            CanonicalSqliteOriginalStoreLease? lease = null; var errors = new List<Exception>();
            IReadOnlyList<CapabilityDefinition> rows = []; bool schemaPresent = false;
            try
            {
                await source.ReadCapture(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(actor, false,
                    source.Run, source.Retain, token), value => lease = value).ConfigureAwait(false);
                if (!_store.IsIssuedOriginalLease(lease!) || lease!.OriginalIdentity != identity)
                    throw new UnauthorizedAccessException("The protected original catalogue lease/identity changed.");
                await DemandReceiptAsync(ownership, identity, actor, source, token).ConfigureAwait(false);
                schemaPresent = await HasExistingTableAsync(lease, token).ConfigureAwait(false);
                if (schemaPresent) rows = await ReadRowsAsync(lease, token).ConfigureAwait(false);
                await lease.RevalidateWithinSourceAsync(source.Run, source.Retain, token).ConfigureAwait(false);
                await DemandReceiptAsync(ownership, identity, actor, source, token).ConfigureAwait(false);
            }
            catch (Exception error) { errors.Add(error); }
            if (lease is not null) try { await lease.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            CanonicalSqliteOriginalStoreOwner.Throw(errors);
            await DemandActorAsync(actor, source, token).ConfigureAwait(false);
            var current = await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(actor,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            if (current != identity) throw new UnauthorizedAccessException("The original canonical store changed before catalogue publication.");
            await DemandReceiptAsync(ownership, identity, actor, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            var observed = new Observation(this, actor, schemaPresent ? CapabilityOriginalCatalogueState.Available
                : CapabilityOriginalCatalogueState.SetupRequired,
                schemaPresent ? "Actual existing capability rows read without seeding or schema writes."
                    : "The existing capability table is unavailable. Prepare it through the actual owning write lifecycle.",
                schemaPresent ? rows : [], identity, ownership);
            source.Run(() => _issued.Add(observed, observed)); return observed;

            async Task<ICapabilityOriginalRepositoryObservation> SetupAsync(string detail)
            {
                await DemandActorAsync(actor, source, token).ConfigureAwait(false);
                await source.JoinAllAsync().ConfigureAwait(false);
                var unavailable = new Observation(this, actor, CapabilityOriginalCatalogueState.SetupRequired, detail, []);
                source.Run(() => _issued.Add(unavailable, unavailable)); return unavailable;
            }
        });
    }
    private async Task DemandActorAsync(AuthenticatedResourceActor actor, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        var current = await source.Read(() => _store.OriginalProfiles.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (current != actor) throw new UnauthorizedAccessException("The actual current protected store actor changed before catalogue READ.");
    }
    private async Task DemandReceiptAsync(VerifiedResourceStoreOwnership receipt, ResourceStoreIdentity identity,
        AuthenticatedResourceActor actor, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        if (receipt.ResourceKind != "canonical.sqlite" || receipt.Receipt is null || receipt.ProfileId != actor.ProfileId ||
            receipt.StoreId != identity.StoreId.ToString("D") ||
            !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(receipt, actor,
                source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Current actual canonical.sqlite Home READ ownership is required.");
    }
    private static async Task<bool> HasExistingTableAsync(CanonicalSqliteOriginalStoreLease lease, CancellationToken token)
    {
        Microsoft.Data.Sqlite.SqliteCommand? command = null; var errors = new List<Exception>(); object? result = null;
        try
        {
            lease.InvokeOriginalSource(() =>
            {
                command = lease.Connection.CreateCommand(); command.Transaction = lease.Transaction;
                command.CommandText = "SELECT type FROM sqlite_master WHERE name=$name;";
                command.Parameters.AddWithValue("$name", "capabilities");
            });
            result = await lease.ReadOriginalSourceAsync(() => command!.ExecuteScalarAsync(token)).ConfigureAwait(false);
            if (result is not null && result is not DBNull && result is not "table")
                throw new InvalidDataException("The capability schema name is not an actual existing table.");
        }
        catch (Exception error) { errors.Add(error); }
        if (command is not null) try { await lease.CloseOriginalSourceAsync(command).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors); return result is "table";
    }
    private static async Task<IReadOnlyList<CapabilityDefinition>> ReadRowsAsync(CanonicalSqliteOriginalStoreLease lease, CancellationToken token)
    {
        Microsoft.Data.Sqlite.SqliteCommand? command = null; Microsoft.Data.Sqlite.SqliteDataReader? reader = null;
        Task<Microsoft.Data.Sqlite.SqliteDataReader>? actualReader = null;
        var rows = new List<CapabilityDefinition>(); var errors = new List<Exception>();
        try
        {
            lease.InvokeOriginalSource(() =>
            {
                command = lease.Connection.CreateCommand(); command.Transaction = lease.Transaction;
                command.CommandText = "SELECT * FROM capabilities WHERE is_enabled=1 ORDER BY owner_app_key,name LIMIT 1025;";
            });
            try { reader = await lease.ReadOriginalSourceAsync(() => actualReader = command!.ExecuteReaderAsync(token)).ConfigureAwait(false); }
            finally
            {
                // Acquire the SAME late successful raw reader even when caller retention
                // rejects publication. Close it through the owner before finite return.
                if (actualReader is not null)
                    try { reader = await actualReader.ConfigureAwait(false); }
                    catch (Exception observed) { CanonicalSqliteOriginalStoreOwner.Capture(errors, actualReader, observed); }
            }
            while (await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                lease.InvokeOriginalSource(() => rows.Add(CapabilityRepository.MapOriginalDatabaseRow(reader)));
            if (rows.Count > 1024) throw new InvalidDataException("The actual catalogue exceeds its bounded READ size.");
        }
        catch (Exception error) { errors.Add(error); }
        if (reader is not null) try { await lease.CloseOriginalSourceAsync(reader).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (command is not null) try { await lease.CloseOriginalSourceAsync(command).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors); return Array.AsReadOnly(rows.ToArray());
    }
}
