using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

public sealed partial class CanonicalAutomationDefinitionOriginalWriteOwner
{
    private readonly ConditionalWeakTable<ICanonicalAutomationDefinitionOriginalCreationRecovery, CreationRecovery> _creationRecoveries = new();
    private sealed class CreationRecovery(CanonicalAutomationDefinitionOriginalWriteOwner owner,
        HomeGraphCreationAssociationSource.Observation graph) : ICanonicalAutomationDefinitionOriginalCreationRecovery
    {
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Owner = owner;
        internal readonly HomeGraphCreationAssociationSource.Observation Graph = graph;
        public Guid AutomationId => Graph.Origin.AutomationId;
        public Guid GraphId => Graph.Graph.GraphId;
        public string Name => Graph.Origin.Name;
    }
    public bool IsIssuedOriginalCreationRecovery(ICanonicalAutomationDefinitionOriginalCreationRecovery value) =>
        value is CreationRecovery actual && ReferenceEquals(actual.Owner, this) &&
        _creationRecoveries.TryGetValue(value, out var issued) && ReferenceEquals(actual, issued);
    public Task<IReadOnlyList<ICanonicalAutomationDefinitionOriginalCreationRecovery>> ReadOriginalCreationRecoveryWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader<IReadOnlyList<ICanonicalAutomationDefinitionOriginalCreationRecovery>>(source, async () =>
        {
            var origin = source.Invoke(() => _library.ObserveOriginalCreationOrigin(sameObservation));
            await source.Read(() => _library.RevalidateOriginalObservationWithinSourceAsync(origin.Observation,
                origin.Observation.Actor, source.Run, source.Retain, token)).ConfigureAwait(false);
            var identity = origin.Observation.OriginalStoreIdentity!;
            await DemandCurrentAsync(origin.Observation.Actor, identity, origin.Ownership, source, token).ConfigureAwait(false);
            var observations = await source.Read(() => (_creationGraphs ?? throw new InvalidOperationException("Actual creation graph recovery is unavailable."))
                .ReadCurrentAsync(origin.Observation.Actor, identity, token)).ConfigureAwait(false);
            var result = new List<ICanonicalAutomationDefinitionOriginalCreationRecovery>();
            foreach (var graph in observations)
            {
                var actual = new CreationRecovery(this, graph);
                source.Run(() => _creationRecoveries.Add(actual, actual)); result.Add(actual);
            }
            await DemandCurrentAsync(origin.Observation.Actor, identity, origin.Ownership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false); return result.AsReadOnly();
        }));
    }
    public Task<ICanonicalAutomationDefinitionOriginalCreationObservation> RecoverOriginalCreationWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation, ICanonicalAutomationDefinitionOriginalCreationRecovery sameRecovery,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        if (!IsIssuedOriginalCreationRecovery(sameRecovery)) throw new UnauthorizedAccessException("Refresh the actual protected creation recovery list.");
        var selected = (CreationRecovery)sameRecovery;
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader<ICanonicalAutomationDefinitionOriginalCreationObservation>(source, async () =>
        {
            var origin = source.Invoke(() => _library.ObserveOriginalCreationOrigin(sameObservation));
            await source.Read(() => _library.RevalidateOriginalObservationWithinSourceAsync(origin.Observation,
                origin.Observation.Actor, source.Run, source.Retain, token)).ConfigureAwait(false);
            var actor = origin.Observation.Actor; var identity = origin.Observation.OriginalStoreIdentity!;
            await DemandCurrentAsync(actor, identity, origin.Ownership, source, token).ConfigureAwait(false);
            var graph = await source.Read(() => _creationGraphs!.CaptureAsync(actor, identity, selected.GraphId,
                selected.AutomationId, token)).ConfigureAwait(false);
            if (graph is null || graph.FingerprintSha256 != selected.Graph.FingerprintSha256 ||
                graph.Origin != selected.Graph.Origin)
                throw new UnauthorizedAccessException("The actual stored creation publication changed. Refresh; no replacement IDs or publication were issued.");
            var inception = graph.Origin;
            var absence = await ReadCreationAbsenceAsync(actor, identity, inception.AutomationId,
                inception.DefinitionOperationId, inception.GraphOperationId, source, token).ConfigureAwait(false);
            if (!absence.SupportedTables || !absence.Coherent) throw new InvalidDataException("The current actual creation schema is unavailable or changed; no recovery WRITE was prepared.");
            var reservation = new CreationReservation(this, origin, inception.Name, inception.AutomationId, graph.Graph.GraphId,
                inception.DefinitionOperationId, inception.GraphOperationId, absence.SchemaSha256, inception.CapturedAt, inception.OriginalAbsenceSha256);
            source.Run(() => _creationReservations.Add(reservation, reservation));
            Snapshot? created = null;
            if (!absence.TargetAbsent || !absence.OperationsAbsent)
            {
                created = await ReadSnapshotAsync(actor, identity, reservation.AutomationId, reservation.AutomationId.ToString("D"),
                    reservation.DefinitionOperationId, source, token).ConfigureAwait(false);
                DemandCreatedSnapshot(created, reservation, graph);
            }
            else DemandCreationAbsence(absence, source);
            await DemandCurrentAsync(actor, identity, origin.Ownership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            return await source.Read(() => StartCreationDeliveryAsync(reservation, graph, created,
                source.Run, source.Retain, token)).ConfigureAwait(false);
        }));
    }
    public Task<GraphDocument?> ReadOriginalCanonicalGraphWithinSourceAsync(ICanonicalAutomationLibraryOriginalObservation sameObservation,
        AutomationOwnerRead<AutomationDefinition> sameRow, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader<GraphDocument?>(source, async () =>
        {
            var selected = source.Invoke(() => _library.ObserveOriginalWriteSelection(sameObservation, sameRow));
            if (!CanonicalAutomationGraphReferenceCodec.TryDeserialize(selected.Row.Value.Instruction, out var reference)) return null;
            await source.Read(() => _library.RevalidateOriginalObservationWithinSourceAsync(selected.Observation,
                selected.Observation.Actor, source.Run, source.Retain, token)).ConfigureAwait(false);
            var identity = selected.Observation.OriginalStoreIdentity!;
            await DemandCurrentAsync(selected.Observation.Actor, identity, selected.Ownership, source, token).ConfigureAwait(false);
            var graph = await source.Read(() => _creationGraphs!.CaptureAsync(selected.Observation.Actor, identity, reference.GraphId,
                selected.Row.Value.Id, token)).ConfigureAwait(false);
            if (graph is null) return null;
            var snapshot = await ReadSnapshotAsync(selected.Observation.Actor, identity, selected.Row.Value.Id,
                RawText(selected.Row.RetainedProtectedDescriptors, "id"), graph.Origin.DefinitionOperationId, source, token).ConfigureAwait(false);
            DemandDescriptor(snapshot, selected.Observation.Actor, identity, selected.Row.Value.Id);
            if (snapshot.RowSha256 != AutomationDefinitionChange.ComputeRawRowSHA256(selected.Row.RetainedProtectedDescriptors) ||
                snapshot.Descriptor is not { } descriptor || descriptor.GraphBinding != reference.Binding || descriptor.CreationOriginSha256 != Hash(graph.Origin))
                throw new UnauthorizedAccessException("The actual protected row/descriptor does not match this current canonical graph reference.");
            await DemandCurrentAsync(selected.Observation.Actor, identity, selected.Ownership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false); return graph.Graph;
        }));
    }
    private static void DemandCreatedSnapshot(Snapshot snapshot, CreationReservation reservation,
        HomeGraphCreationAssociationSource.Observation graph)
    {
        DemandDescriptor(snapshot, reservation.Actor, reservation.OriginalStoreIdentity, reservation.AutomationId);
        DemandNoLegacyLease(snapshot.Row);
        var descriptor = snapshot.Descriptor; var operation = snapshot.Operation;
        if (descriptor is null || operation is null || descriptor.Revision != 1 || descriptor.PreviousDescriptorSha256 is not null ||
            operation.Kind != CanonicalAutomationOriginalChangeKind.CreateDisabled || descriptor.OperationId != reservation.DefinitionOperationId ||
            descriptor.GraphBinding != new AutomationDefinitionGraphBinding(reservation.GraphId, 1, null) ||
            descriptor.CreationOriginSha256 != Hash(graph.Origin) || string.IsNullOrWhiteSpace(descriptor.OriginalCreateRequestId) ||
            descriptor.OriginalCreateArgumentsSha256 is not { Length: 64 } || !descriptor.OriginalCreateArgumentsSha256.All(Uri.IsHexDigit) ||
            !MatchesCreatedRow(snapshot.Row, reservation))
            throw new InvalidDataException("The actual original creation row, immutable receipt and inception graph do not exactly agree. Keep this outcome unconfirmed; no INSERT or audit completion was admitted.");
        DemandDurableOperation(operation, snapshot, reservation.Actor, reservation.OriginalStoreIdentity, reservation.AutomationId,
            reservation.DefinitionOperationId, CanonicalAutomationOriginalChangeKind.CreateDisabled, descriptor.Receipt.PayloadSha256);
    }
    private static bool MatchesCreatedRow(IReadOnlyDictionary<string, string?> row, CreationReservation reservation)
    {
        string Text(string value) => JsonSerializer.Serialize(new { storageClass = "text", value });
        string Integer(long value) => JsonSerializer.Serialize(new { storageClass = "integer", value });
        var expected = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["id"] = Text(reservation.AutomationId.ToString("D")), ["name"] = Text(reservation.Name),
            ["mode"] = Integer((int)HavenMode.Tasks),
            ["instruction"] = Text(CanonicalAutomationGraphReferenceCodec.Serialize(new(1, reservation.GraphId, 1, null))),
            ["schedule_kind"] = Integer((int)AutomationScheduleKind.Unscheduled), ["schedule_json"] = Text("{}"),
            ["next_run_at"] = null, ["container_id"] = null, ["is_enabled"] = Integer(0),
            ["created_at"] = Text(reservation.CapturedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
            ["updated_at"] = Text(reservation.CapturedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
            ["lease_token"] = null, ["lease_until"] = null
        };
        return row.Count == expected.Count && expected.All(pair => row.TryGetValue(pair.Key, out var actual) && actual == pair.Value);
    }
    public CanonicalAutomationOriginalCreationAuditEvidence? ObserveOriginalCreationAudit(
        ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent, ICanonicalAutomationDefinitionOriginalChangeAcknowledgment sameAcknowledgment,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> sameAtomic)
    {
        if (!IsOwnedOriginalChangeAcknowledgment(sameIntent, sameAcknowledgment, sameAtomic) || sameIntent is not Intent intent ||
            intent.Creation is null || sameAcknowledgment is not Acknowledgment actual ||
            intent.ChangeKind != CanonicalAutomationOriginalChangeKind.RecoverCreated) return null;
        var descriptor = actual.Durable.Descriptor;
        if (descriptor.OriginalCreateRequestId is not { Length: > 0 } request ||
            descriptor.OriginalCreateArgumentsSha256 is not { Length: 64 } digest || descriptor.CreationOriginSha256 is not { Length: 64 } origin)
            return null;
        return new(request, digest, origin, descriptor.Receipt);
    }
}
