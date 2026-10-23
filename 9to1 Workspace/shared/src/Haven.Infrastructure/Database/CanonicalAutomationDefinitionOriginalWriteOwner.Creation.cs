using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class CanonicalAutomationDefinitionOriginalWriteOwner
{
    private const int MaximumCreationIdentityScan = 16_384;
    private readonly ConditionalWeakTable<ICanonicalAutomationDefinitionOriginalCreationReservation, CreationReservation> _creationReservations = new();
    private sealed record CreationSchemaObject(string Type, string Name, string Table, string? Sql);
    private sealed record CreationAbsenceSnapshot(string SchemaSha256, bool SupportedTables,
        bool TargetAbsent, bool OperationsAbsent, bool Coherent);
    private sealed class CreationReservation(CanonicalAutomationDefinitionOriginalWriteOwner owner,
        CanonicalAutomationLibraryOriginalReadOwner.OriginalCreationOrigin origin, string name,
        Guid automation, Guid graph, Guid definitionOperation, Guid graphOperation,
        string schemaDigest, DateTimeOffset capturedAt) : ICanonicalAutomationDefinitionOriginalCreationReservation
    {
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Owner = owner;
        internal readonly CanonicalAutomationLibraryOriginalReadOwner.OriginalCreationOrigin Origin = origin;
        public AuthenticatedResourceActor Actor => Origin.Observation.Actor;
        public ResourceStoreIdentity OriginalStoreIdentity => Origin.Observation.OriginalStoreIdentity!;
        public VerifiedResourceStoreOwnership OriginalStoreOwnership => Origin.Ownership;
        public Guid AutomationId { get; } = automation;
        public Guid GraphId { get; } = graph;
        public Guid DefinitionOperationId { get; } = definitionOperation;
        public Guid GraphOperationId { get; } = graphOperation;
        public string Name { get; } = name;
        public string OriginalSchemaSha256 { get; } = schemaDigest;
        public DateTimeOffset CapturedAt { get; } = capturedAt;
        public string OriginalAbsenceSha256 => Hash(new { SchemaVersion = 1, Actor, OriginalStoreIdentity,
            AutomationId, GraphId, DefinitionOperationId, GraphOperationId, Name, OriginalSchemaSha256,
            SqlDefinitionTargetAbsent = true, OwnerDescriptorAbsent = true, OperationReceiptsAbsent = true,
            HomeGraphAbsenceObserved = false, GraphPublicationAdmitted = false });
    }
    public bool IsIssuedOriginalCreationReservation(ICanonicalAutomationDefinitionOriginalCreationReservation value) =>
        value is CreationReservation actual && ReferenceEquals(actual.Owner, this) &&
        _creationReservations.TryGetValue(value, out var issued) && ReferenceEquals(actual, issued);
    private CreationReservation RequireCreationReservation(ICanonicalAutomationDefinitionOriginalCreationReservation value) =>
        IsIssuedOriginalCreationReservation(value) ? (CreationReservation)value :
            throw new UnauthorizedAccessException("The SAME actual protected producer must issue this creation origin.");
    public string GetOriginalCreationReservationDigest(ICanonicalAutomationDefinitionOriginalCreationReservation value) =>
        RequireCreationReservation(value).OriginalAbsenceSha256;

    public Task<ICanonicalAutomationDefinitionOriginalCreationReservation> CaptureOriginalCreationWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation, string name,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256)
            throw new ArgumentException("Use an Automation name of up to 256 characters.", nameof(name));
        var capturedName = name.Trim();
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader<ICanonicalAutomationDefinitionOriginalCreationReservation>(source, async () =>
        {
            var origin = source.Invoke(() => _library.ObserveOriginalCreationOrigin(sameObservation));
            await source.Read(() => _library.RevalidateOriginalObservationWithinSourceAsync(origin.Observation,
                origin.Observation.Actor, source.Run, source.Retain, token)).ConfigureAwait(false);
            var identity = origin.Observation.OriginalStoreIdentity!;
            await DemandCurrentAsync(origin.Observation.Actor, identity, origin.Ownership, source, token).ConfigureAwait(false);
            // These identifiers are issued ONCE by this actual producer, before the
            // same reservation is returned. No retry API accepts replacement IDs.
            var automation = Guid.NewGuid(); var graph = Guid.NewGuid();
            var definitionOperation = Guid.NewGuid(); var graphOperation = Guid.NewGuid();
            if (new[] { automation, graph, definitionOperation, graphOperation }.Distinct().Count() != 4)
                throw new InvalidOperationException("Creation identifiers must be distinct; no reservation was issued.");
            var snapshot = await ReadCreationAbsenceAsync(origin.Observation.Actor, identity, automation,
                definitionOperation, graphOperation, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            DemandCreationAbsence(snapshot, source);
            await DemandCurrentAsync(origin.Observation.Actor, identity, origin.Ownership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            var actual = new CreationReservation(this, origin, capturedName, automation, graph,
                definitionOperation, graphOperation, snapshot.SchemaSha256, DateTimeOffset.UtcNow);
            source.Run(() => _creationReservations.Add(actual, actual)); return actual;
        }));
    }
    public Task RevalidateOriginalCreationWithinSourceAsync(ICanonicalAutomationDefinitionOriginalCreationReservation value,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var actual = RequireCreationReservation(value);
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader<object?>(source, async () =>
        {
            await source.Read(() => _library.RevalidateOriginalObservationWithinSourceAsync(actual.Origin.Observation,
                actual.Actor, source.Run, source.Retain, token)).ConfigureAwait(false);
            await DemandCurrentAsync(actual.Actor, actual.OriginalStoreIdentity, actual.OriginalStoreOwnership, source, token).ConfigureAwait(false);
            var snapshot = await ReadCreationAbsenceAsync(actual.Actor, actual.OriginalStoreIdentity, actual.AutomationId,
                actual.DefinitionOperationId, actual.GraphOperationId, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            DemandCreationAbsence(snapshot, source);
            if (snapshot.SchemaSha256 != actual.OriginalSchemaSha256)
            {
                var refusal = new InvalidOperationException("The original physical creation schema changed. Keep this reservation for inspection and capture a fresh origin.");
                source.RegisterOriginalPreEffectRefusal(refusal); throw refusal;
            }
            await DemandCurrentAsync(actual.Actor, actual.OriginalStoreIdentity, actual.OriginalStoreOwnership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            return null;
        }));
    }
    private static void DemandCreationAbsence(CreationAbsenceSnapshot snapshot, CanonicalSqliteOriginalSourceScope source)
    {
        var reason = !snapshot.SupportedTables ? "Creation requires the existing canonical Automation, reusable-task and settings tables without unknown triggers."
            : !snapshot.Coherent ? "The physical store changed while checking creation absence. Refresh before preparing a new creation."
            : !snapshot.TargetAbsent ? "The reserved Automation ID is already present. No existing definition was adopted or overwritten."
            : !snapshot.OperationsAbsent ? "The reserved ID or operation has existing owner history. Preserve it for inspection; this is not permission to replay creation."
            : null;
        if (reason is null) return;
        // All SAME native statements/readers/lease and source observations have
        // already joined successfully. This exact producer records pre-effect refusal.
        var refusal = new InvalidOperationException(reason); source.RegisterOriginalPreEffectRefusal(refusal); throw refusal;
    }
    private async Task<CreationAbsenceSnapshot> ReadCreationAbsenceAsync(AuthenticatedResourceActor actor,
        ResourceStoreIdentity identity, Guid automation, Guid definitionOperation, Guid graphOperation,
        CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        CanonicalSqliteOriginalStoreLease? lease = null; CreationAbsenceSnapshot? snapshot = null; var errors = new List<Exception>();
        try
        {
            await source.ReadCapture(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(actor, false,
                source.Run, source.Retain, token), actual => lease = actual).ConfigureAwait(false);
            if (!_store.IsIssuedOriginalLease(lease!) || lease!.OriginalIdentity != identity)
                throw new UnauthorizedAccessException("The SAME physical protected store is required for creation absence.");
            await source.JoinAllAsync().ConfigureAwait(false);
            var before = await Query(lease, command => command.CommandText = "PRAGMA data_version;",
                reader => reader.GetInt64(0), 1, token).ConfigureAwait(false);
            var schema = await Query(lease, command => command.CommandText =
                "SELECT type,name,tbl_name,sql FROM sqlite_master WHERE tbl_name IN ('automations','reusable_tasks','settings') ORDER BY type,name;",
                reader => new CreationSchemaObject(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)), 128, token).ConfigureAwait(false);
            var tables = schema.Where(item => item.Type == "table").ToArray();
            var supported = tables.Length == 3 && new[] { "automations", "reusable_tasks", "settings" }
                .All(name => tables.Count(table => table.Name == name && table.Sql?.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase) == true) == 1)
                && schema.All(item => item.Type != "trigger");
            var targetAbsent = false; var operationsAbsent = false;
            if (supported)
            {
                // Compare parsed UUIDs across both existing entity tables. Binary
                // equality alone misses the same UUID stored with another case or
                // valid N/D/B/P/X form. One bounded native SELECT holds that scan.
                var collisions = await Query(lease, command => command.CommandText =
                    "SELECT id FROM automations UNION ALL SELECT id FROM reusable_tasks LIMIT 16385;",
                    reader => Guid.TryParse(reader.GetString(0), out var id) && id == automation,
                    MaximumCreationIdentityScan, token).ConfigureAwait(false);
                targetAbsent = collisions.All(collides => !collides);
                var history = await Query(lease, command =>
                {
                    command.CommandText = "SELECT key FROM settings WHERE key LIKE $owner OR key IN ($definition,$graph) LIMIT 1;";
                    command.Parameters.AddWithValue("$owner", DescriptorPrefix + automation.ToString("N") + ".%");
                    command.Parameters.AddWithValue("$definition", ReceiptPrefix + definitionOperation.ToString("N"));
                    command.Parameters.AddWithValue("$graph", ReceiptPrefix + graphOperation.ToString("N"));
                }, reader => reader.GetString(0), 1, token).ConfigureAwait(false);
                operationsAbsent = history.Count == 0;
            }
            var after = await Query(lease, command => command.CommandText = "PRAGMA data_version;",
                reader => reader.GetInt64(0), 1, token).ConfigureAwait(false);
            snapshot = new(Hash(schema), supported, targetAbsent, operationsAbsent,
                before.Count == 1 && after.Count == 1 && before[0] == after[0]);
            await source.Read(() => lease.RevalidateWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
        }
        catch (Exception cause) { errors.Add(cause); }
        Task? close = null;
        if (lease is not null)
            try { close = lease.CloseAndDrainAsync(); source.Retain(close); await close.ConfigureAwait(false); }
            catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, close, cause); }
        try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
        return snapshot ?? throw new InvalidOperationException("No healthy original creation absence observation was retained.");
    }
}
