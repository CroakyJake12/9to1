using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class CanonicalCapabilityCatalogueReadOwner
{
    internal sealed record DynamicCatalogueRows(AuthenticatedResourceActor Actor, CapabilityPlatform Platform,
        ResourceStoreIdentity? Identity, VerifiedResourceStoreOwnership? Receipt,
        CapabilityOriginalCatalogueState State, string Detail, IReadOnlyList<CapabilityDefinition> Definitions);

    internal Task<DynamicCatalogueRows> ReadOriginalConnectionMetadataWithinSourceAsync(
        CanonicalConnectionCapabilityReadOwner actualSource, AuthenticatedResourceActor actor, CapabilityPlatform platform,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, scope, retain);
        return _store.RetainOriginalReader(source, async () =>
        {
            source.Run(() =>
            {
                if (!ReferenceEquals(actualSource.OriginalCatalogue, this) ||
                    platform is not (CapabilityPlatform.Windows or CapabilityPlatform.Android or CapabilityPlatform.Linux))
                    throw new UnauthorizedAccessException("The SAME configured dynamic catalogue source/current platform is required.");
            });
            await DemandActorAsync(actor, source, token).ConfigureAwait(false);
            var exists = source.Invoke(() =>
            {
                try { _ = File.GetAttributes(_paths.DatabasePath); return true; }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
            });
            if (!exists) return await Setup("The existing canonical SQLite database is unavailable.").ConfigureAwait(false);
            var identity = await source.Read(() => _store.ReadExistingStoreIdentityWithinOriginalSourceAsync(actor,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            if (identity is null) return await Setup("The existing canonical SQLite identity requires owning recovery/setup.").ConfigureAwait(false);
            var receipt = await source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync("canonical.sqlite",
                identity.StoreId.ToString("D"), source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
            if (receipt is null) return await Setup("Import the actual canonical SQLite store through Home before observing its configured connections.").ConfigureAwait(false);
            await DemandReceiptAsync(receipt, identity, actor, source, token).ConfigureAwait(false);
            CanonicalSqliteOriginalStoreLease? lease = null; var errors = new List<Exception>();
            IReadOnlyList<CapabilityDefinition> definitions = []; bool initialized = false;
            try
            {
                await source.ReadCapture(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(actor, false,
                    source.Run, source.Retain, token), value => lease = value).ConfigureAwait(false);
                if (!_store.IsIssuedOriginalLease(lease!) || lease!.OriginalIdentity != identity)
                    throw new UnauthorizedAccessException("The actual protected dynamic catalogue lease/identity changed.");
                await DemandReceiptAsync(receipt, identity, actor, source, token).ConfigureAwait(false);
                var tables = await ReadDynamicMetadataRowsAsync(lease,
                    "SELECT name,type FROM sqlite_master WHERE name IN ('external_connections','calendar_accounts') ORDER BY name;",
                    reader => (Name: reader.String("name"), Type: reader.String("type")), token).ConfigureAwait(false);
                if (tables.Any(value => value.Type != "table")) throw new InvalidDataException("Preserve the invalid existing dynamic capability schema for recovery.");
                initialized = tables.Count == 2 && tables.Any(value => value.Name == "external_connections") && tables.Any(value => value.Name == "calendar_accounts");
                if (initialized)
                {
                    // Fixed metadata columns only. Never fetch configuration_json,
                    // account_identifier, credentials, service content or remote tools.
                    var connections = await ReadDynamicMetadataRowsAsync(lease,
                        "SELECT id,name,kind,is_enabled,state,updated_at FROM external_connections ORDER BY name,updated_at DESC LIMIT 1025;",
                        ExternalConnectionRepository.ReadOriginalCapabilityMetadata, token).ConfigureAwait(false);
                    var calendars = await ReadDynamicMetadataRowsAsync(lease,
                        "SELECT id,provider,status,last_synced_at,updated_at FROM calendar_accounts ORDER BY provider,display_name LIMIT 1025;",
                        PlannerRepository.ReadOriginalCapabilityMetadata, token).ConfigureAwait(false);
                    definitions = source.Invoke(() => actualSource.OriginalProvider.ProjectOriginalCapabilityMetadata(connections, calendars, platform));
                }
                await lease.RevalidateWithinSourceAsync(source.Run, source.Retain, token).ConfigureAwait(false);
                await DemandReceiptAsync(receipt, identity, actor, source, token).ConfigureAwait(false);
            }
            catch (Exception cause) { errors.Add(cause); }
            if (lease is not null) try { await lease.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            CanonicalSqliteOriginalStoreOwner.Throw(errors);
            await DemandActorAsync(actor, source, token).ConfigureAwait(false);
            var current = await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(actor,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            if (current != identity) throw new UnauthorizedAccessException("The actual dynamic metadata store identity changed before publication.");
            await DemandReceiptAsync(receipt, identity, actor, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            return new DynamicCatalogueRows(actor, platform, identity, receipt,
                initialized ? CapabilityOriginalCatalogueState.Available : CapabilityOriginalCatalogueState.SetupRequired,
                initialized ? "Actual configured connection capability metadata observed; service permissions remain separate."
                    : "The existing connection/calendar metadata tables require their actual owning setup lifecycle.",
                initialized ? definitions : []);

            async Task<DynamicCatalogueRows> Setup(string detail)
            {
                await DemandActorAsync(actor, source, token).ConfigureAwait(false);
                await source.JoinAllAsync().ConfigureAwait(false);
                return new(actor, platform, null, null, CapabilityOriginalCatalogueState.SetupRequired, detail, []);
            }
        });
    }

    private static async Task<IReadOnlyList<T>> ReadDynamicMetadataRowsAsync<T>(CanonicalSqliteOriginalStoreLease lease,
        string fixedOriginalSql, Func<SqliteDataReader,T> maintainedMap, CancellationToken token)
    {
        SqliteCommand? command = null; SqliteDataReader? reader = null; Task<SqliteDataReader>? actualReader = null;
        var errors = new List<Exception>(); var rows = new List<T>();
        try
        {
            lease.InvokeOriginalSource(() =>
            {
                command = lease.Connection.CreateCommand(); command.Transaction = lease.Transaction;
                command.CommandText = fixedOriginalSql;
            });
            try { reader = await lease.ReadOriginalSourceAsync(() => actualReader = command!.ExecuteReaderAsync(token)).ConfigureAwait(false); }
            finally
            {
                if (actualReader is not null)
                    try { reader = await actualReader.ConfigureAwait(false); }
                    catch (Exception observed) { CanonicalSqliteOriginalStoreOwner.Capture(errors, actualReader, observed); }
            }
            while (await lease.ReadOriginalSourceAsync(() => reader!.ReadAsync(token)).ConfigureAwait(false))
                lease.InvokeOriginalSource(() => rows.Add(maintainedMap(reader!)));
            if (rows.Count > 1024) throw new InvalidDataException("The original configured metadata catalogue exceeds its bounded size.");
        }
        catch (Exception cause) { errors.Add(cause); }
        if (reader is not null) try { await lease.CloseOriginalSourceAsync(reader).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (command is not null) try { await lease.CloseOriginalSourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors); return Array.AsReadOnly(rows.ToArray());
    }
}
