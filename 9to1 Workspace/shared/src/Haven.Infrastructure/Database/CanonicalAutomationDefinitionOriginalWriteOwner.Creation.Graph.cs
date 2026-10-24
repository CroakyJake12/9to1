using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

public sealed partial class CanonicalAutomationDefinitionOriginalWriteOwner
{
    private HomeGraphCreationAssociationSource? _creationGraphs;
    private NodeGraphSchemaRegistry? _creationSchemas;
    private readonly AsyncLocal<CreationProcess?> _creationExecution = new();
    private readonly Dictionary<Guid, CreationProcess> _creationProcesses = [];
    private readonly ConditionalWeakTable<ICanonicalAutomationDefinitionOriginalCreationObservation, CreationDelivery> _creationDeliveries = new();
    private readonly List<CreationDelivery> _ownedCreationDeliveries = [];
    public string OwnerAppId => "automations";
    public string OwningStoreResourceKind => "canonical.sqlite";
    public bool SupportsEntityKind(string kind) => kind == "automation.definition";
    public bool IsBoundToGraphRepository(IVersionedNodeGraphRepository actualRepository) =>
        _creationGraphs?.IsBoundToGraphRepository(actualRepository) == true;
    public bool SupportsOriginalDefinitionCreation { get { lock (_gate) return !_retiring && _creationGraphs is not null && _creationSchemas is not null && _writes is HomeCanonicalAutomationDefinitionWriteSource; } }
    public void BindOriginalCreationGraphSource(HomeGraphCreationAssociationSource sameGraphs, NodeGraphSchemaRegistry sameSchemas)
    {
        ArgumentNullException.ThrowIfNull(sameGraphs); ArgumentNullException.ThrowIfNull(sameSchemas);
        if (_writes is not HomeCanonicalAutomationDefinitionWriteSource writes ||
            !sameGraphs.IsBoundToOriginalProducer(_store.OriginalProfiles, this, writes))
            throw new UnauthorizedAccessException("The SAME actual Home graph/source/protected creator is required.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_creationGraphs is not null) throw new InvalidOperationException("Creation graph composition is already bound.");
            _creationGraphs = sameGraphs; _creationSchemas = sameSchemas; writes.BindOriginalCreationGraphSource(sameGraphs);
        }
    }
    private sealed record CreationReady(CreationReservation Reservation, HomeGraphCreationAssociationSource.Observation Graph);
    private sealed class CreationGraphSelection(CanonicalAutomationDefinitionOriginalWriteOwner owner,
        CreationReservation reservation, GraphPublicationIntent intent) : IOriginalGraphOwnerSelection
    {
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Owner = owner;
        internal readonly CreationReservation Reservation = reservation;
        internal readonly GraphPublicationIntent Intent = intent;
    }
    private readonly ConditionalWeakTable<IOriginalGraphOwnerSelection, CreationGraphSelection> _creationGraphSelections = new();
    private CreationGraphSelection RequireGraphSelection(IOriginalGraphOwnerSelection value, GraphPublicationIntent intent)
    {
        if (value is not CreationGraphSelection actual || !ReferenceEquals(actual.Owner, this) ||
            !_creationGraphSelections.TryGetValue(value, out var issued) || !ReferenceEquals(actual, issued) ||
            !ReferenceEquals(actual.Intent, intent) || !IsIssuedOriginalCreationReservation(actual.Reservation))
            throw new UnauthorizedAccessException("The exact private source-issued graph inception selection is required.");
        return actual;
    }
    public async ValueTask RequireCurrentAsync(IOriginalGraphOwnerSelection value, GraphPublicationIntent intent, CancellationToken token)
    {
        if (value is SavedGraphPublicationSelection)
        { await RequireSavedGraphPublicationCurrentAsync(value, intent, token).ConfigureAwait(false); return; }
        var actual = RequireGraphSelection(value, intent);
        await RevalidateOriginalCreationWithinSourceAsync(actual.Reservation, body => body(), _ => { }, token).ConfigureAwait(false);
    }
    public async ValueTask RequirePublishedAsync(IOriginalGraphOwnerSelection value, GraphPublicationIntent intent,
        GraphPublicationReceipt knownPublication, CancellationToken token)
    {
        if (value is SavedGraphPublicationSelection)
        { await RequireSavedGraphPublicationPublishedAsync(value, intent, knownPublication, token).ConfigureAwait(false); return; }
        var actual = RequireGraphSelection(value, intent);
        if (knownPublication.OperationId != actual.Reservation.GraphOperationId || knownPublication.Owner != intent.Owner ||
            knownPublication.Kind != GraphPublicationKind.SaveDraft || knownPublication.GraphId != actual.Reservation.GraphId ||
            knownPublication.ExpectedGraphRevision != 0 || knownPublication.CommittedGraphRevision != 1)
            throw new UnauthorizedAccessException("The actual inception publication receipt differs from its source-issued proposal.");
        await RevalidateOriginalCreationWithinSourceAsync(actual.Reservation, body => body(), _ => { }, token).ConfigureAwait(false);
    }
    private GraphPublicationIntent CaptureCreationGraphIntent(CreationReservation reservation)
    {
        var schemas = _creationSchemas ?? throw new InvalidOperationException("Actual graph schemas are unavailable.");
        var graph = new GraphDocument(GraphDocument.CurrentSchemaVersion, reservation.GraphId, 1,
            CanonicalAutomationCoreNodes.IsRegistered(schemas) ? CanonicalAutomationCoreNodes.ProfileId : CanonicalAutomationCoreNodes.EmptyProfileId,
            GraphRevisionState.Draft, [], []);
        var owner = new GraphOwnerDescriptor("automations", reservation.OriginalStoreIdentity.StoreId, "automation.definition", reservation.AutomationId);
        var origin = new GraphOwnerCreationOrigin(1, owner.StoreId, reservation.AutomationId, reservation.DefinitionOperationId,
            reservation.GraphOperationId, reservation.Name, reservation.CapturedAt, reservation.OriginalAbsenceSha256, reservation.Actor);
        return GraphPublicationIntent.Capture(owner, reservation.Actor, reservation.GraphOperationId, GraphPublicationKind.SaveDraft,
            graph, 0, [new("automation.definition", owner.StoreId.ToString("D") + "/" + owner.EntityId.ToString("D"),
                reservation.OriginalAbsenceSha256, ResourceAccess.Write), new("nodegraph.definition", reservation.Actor.ProfileId + "/" +
                graph.GraphId.ToString("D"), "0", ResourceAccess.Write)], schemas, new(4096, 16), origin);
    }
    private sealed record CreationResult(CanonicalAutomationCreationCompletionKind Kind, string Message);
    private sealed class CreationProcess(CanonicalAutomationDefinitionOriginalWriteOwner owner, CreationReservation reservation,
        HomeGraphCreationAssociationSource.Observation? recoveredGraph, Snapshot? recoveredSnapshot)
    {
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Owner = owner;
        internal readonly CreationReservation Reservation = reservation;
        internal readonly HomeGraphCreationAssociationSource.Observation? RecoveredGraph = recoveredGraph;
        internal readonly Snapshot? RecoveredSnapshot = recoveredSnapshot;
        internal Task<CreationResult> Driver = null!;
        internal HomeGraphPublicationOwner.Review? Review;
        internal Task<bool>? Withdrawal;
        internal CanonicalSqliteOriginalSourceScope Source = null!;
    }
    public Task<ICanonicalAutomationDefinitionOriginalCreationObservation> StartOriginalCreationWithinSourceAsync(
        ICanonicalAutomationDefinitionOriginalCreationReservation value, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var actual = RequireCreationReservation(value);
        return StartCreationDeliveryAsync(actual, null, null, scope, retain, token);
    }
    private Task<ICanonicalAutomationDefinitionOriginalCreationObservation> StartCreationDeliveryAsync(CreationReservation reservation,
        HomeGraphCreationAssociationSource.Observation? recoveredGraph, Snapshot? recoveredSnapshot,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, async () =>
        {
            if (!SupportsOriginalDefinitionCreation) throw new InvalidOperationException("The full original graph/Home creation producer is unavailable.");
            if (recoveredGraph is null)
                await source.Read(() => RevalidateOriginalCreationWithinSourceAsync(reservation, source.Run, source.Retain, token)).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            CreationDelivery? delivery = null; TaskCompletionSource? begin = null;
            try { source.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_retiring, this);
                    _ownedCreationDeliveries.RemoveAll(value => value.HasJoinedClose);
                    if (_ownedCreationDeliveries.Count >= 128) throw new InvalidOperationException("Original creation delivery custody is full.");
                    foreach (var settled in _creationProcesses.Values.Where(value => value.Driver.IsCompletedSuccessfully).ToArray())
                    {
                        var result = settled.Driver.GetAwaiter().GetResult(); // SAME successful driver independently joined.
                        if ((result.Kind is CanonicalAutomationCreationCompletionKind.CreatedDisabled or CanonicalAutomationCreationCompletionKind.DeclinedBeforeEffect) &&
                            !_ownedCreationDeliveries.Any(value => ReferenceEquals(value.Process, settled) && !value.HasJoinedClose))
                            _creationProcesses.Remove(settled.Reservation.DefinitionOperationId);
                    }
                    if (_creationProcesses.TryGetValue(reservation.DefinitionOperationId, out var finished) &&
                        finished.Driver.IsCompleted && recoveredGraph is not null)
                        _creationProcesses.Remove(reservation.DefinitionOperationId);
                    if (!_creationProcesses.TryGetValue(reservation.DefinitionOperationId, out var process))
                    {
                        if (_creationProcesses.Count >= 128) throw new InvalidOperationException("Original creation process custody is full; retained outcomes require inspection.");
                        process = new(this, reservation, recoveredGraph, recoveredSnapshot) { Review = finished?.Review };
                        process.Source = new(_store.OriginalSourceOwner, Scope(body => body()), _ => { });
                        var original = Reserve(process.Source); begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        process.Driver = DriveCreationAsync(begin.Task, process); _creationProcesses.Add(reservation.DefinitionOperationId, process);
                        Publish(original, process.Driver);
                    }
                    else if (process.Reservation.AutomationId != reservation.AutomationId || process.Reservation.GraphId != reservation.GraphId ||
                        process.Reservation.Actor != reservation.Actor)
                        throw new UnauthorizedAccessException("The original creation process identity or actor changed; no driver was adopted.");
                    delivery = new(this, process); _creationDeliveries.Add(delivery, delivery); _ownedCreationDeliveries.Add(delivery);
                }
            }); }
            finally { begin?.TrySetResult(); }
            await source.JoinAllAsync().ConfigureAwait(false);
            return (ICanonicalAutomationDefinitionOriginalCreationObservation)(delivery ?? throw new InvalidOperationException("No source-issued original creation observation was captured."));
        });
    }
    private bool CreationStopping { get { lock (_gate) return _retiring; } }
    private async Task<CreationResult> DriveCreationAsync(Task begin, CreationProcess process)
    {
        await begin.ConfigureAwait(false); var previous = _creationExecution.Value; _creationExecution.Value = process;
        var source = process.Source; var reservation = process.Reservation;
        try
        {
            HomeGraphCreationAssociationSource.Observation? graph = process.RecoveredGraph;
            if (graph is null)
            {
                if (CreationStopping) return new(CanonicalAutomationCreationCompletionKind.DeclinedBeforeEffect, "Creation stopped before graph review. No graph or definition was saved.");
                var proposal = CaptureCreationGraphIntent(reservation);
                var selection = new CreationGraphSelection(this, reservation, proposal); _creationGraphSelections.Add(selection, selection);
                var review = process.Review = _creationGraphs!.Prepare(proposal, selection);
                if (CreationStopping)
                {
                    await source.Read(() => review.RetireAsync()).ConfigureAwait(false);
                    return new(CanonicalAutomationCreationCompletionKind.DeclinedBeforeEffect, "Creation stopped before graph review. No graph or definition was saved.");
                }
                Task<HomePreparedReviewObservation>? rawSubmission = null;
                source.Run(() =>
                {
                    lock (_gate)
                    {
                        // Same producer gate as native Stop: publication of the
                        // original submission wins before Stop, or no new review starts.
                        if (!_retiring) { rawSubmission = review.SubmitAsync(); source.Retain(rawSubmission); }
                    }
                });
                if (rawSubmission is null)
                {
                    await source.Read(() => review.RetireAsync()).ConfigureAwait(false);
                    return new(CanonicalAutomationCreationCompletionKind.DeclinedBeforeEffect, "Creation stopped before graph review. No graph or definition was saved.");
                }
                var observed = await source.Read(() => rawSubmission).ConfigureAwait(false);
                while (observed.Request?.State == HomePermissionRequestState.PendingApproval)
                {
                    if (process.Withdrawal is { } withdrawal && await source.Read(() => withdrawal).ConfigureAwait(false))
                        return new(CanonicalAutomationCreationCompletionKind.DeclinedBeforeEffect, "The unresolved graph review was withdrawn. No graph or definition was saved.");
                    await source.Read(() => Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
                    observed = await source.Read(() => review.ObserveAsync()).ConfigureAwait(false);
                }
                if (process.Withdrawal is { } finalWithdrawal && await source.Read(() => finalWithdrawal).ConfigureAwait(false))
                    return new(CanonicalAutomationCreationCompletionKind.DeclinedBeforeEffect, "The original unresolved graph review was withdrawn. No graph or definition was saved.");
                if (observed.Request?.State is HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked or HomePermissionRequestState.Cancelled)
                {
                    await source.Read(() => review.RetireAsync()).ConfigureAwait(false);
                    return new(CanonicalAutomationCreationCompletionKind.DeclinedBeforeEffect, "Home did not accept graph creation. No definition was inserted.");
                }
                if (observed.Request?.State != HomePermissionRequestState.Approved)
                    return new(CanonicalAutomationCreationCompletionKind.OutcomeUnconfirmed, "The original graph review remains unresolved. Do not repeat creation; inspect the same request in Home.");
                var receipt = await source.Read(() => review.PublishAsync()).ConfigureAwait(false);
                if (receipt is null)
                    return new(CanonicalAutomationCreationCompletionKind.OutcomeUnconfirmed, "Graph publication was not confirmed. Inspect the retained operation; no definition insertion was attempted.");
                var audit = await source.Read(() => review.FinishAuditAsync()).ConfigureAwait(false);
                if (!audit.Succeeded)
                    return new(CanonicalAutomationCreationCompletionKind.AuditPending, "The owned graph Draft was saved, but its Home audit remains pending. The definition was not inserted; recover this same creation.");
                graph = await source.Read(() => _creationGraphs.CaptureAsync(reservation.Actor, reservation.OriginalStoreIdentity,
                    reservation.GraphId, reservation.AutomationId, CancellationToken.None)).ConfigureAwait(false);
                if (graph is null)
                    return new(CanonicalAutomationCreationCompletionKind.OutcomeUnconfirmed, "The actual graph publication cannot be re-observed. No definition insertion was attempted.");
                if (!await source.Read(() => _creationGraphs.RetirePublishedReviewAsync(review)).ConfigureAwait(false))
                    throw new InvalidOperationException("The acknowledged graph review was not independently retired; its original remains retained.");
            }
            if (!graph.GraphAuditRecorded)
            {
                // The SAME live graph capability may finish its retained outcome.
                // Cold serialized metadata cannot revive that capability.
                if (process.Review is not { } originalReview)
                    return new(CanonicalAutomationCreationCompletionKind.AuditPending,
                        "The actual graph Draft exists, but its original graph audit is pending. The lost live capability was not revived; no definition insert was attempted.");
                var audit = await source.Read(() => originalReview.FinishAuditAsync()).ConfigureAwait(false);
                if (!audit.Succeeded)
                    return new(CanonicalAutomationCreationCompletionKind.AuditPending, "The original graph audit remains pending; no definition insert was attempted.");
                graph = await source.Read(() => _creationGraphs!.CaptureAsync(reservation.Actor, reservation.OriginalStoreIdentity,
                    reservation.GraphId, reservation.AutomationId, CancellationToken.None)).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The original graph cannot be re-observed after audit completion.");
            }
            if (CreationStopping)
                return new(CanonicalAutomationCreationCompletionKind.GraphDraftOnly, "The owned graph Draft is saved. Creation stopped before the separate definition review; recover this same creation later.");
            var intent = await source.Read(() => PrepareCreationIntentAsync(reservation, graph, process.RecoveredSnapshot,
                source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false);
            // An accepted process owns this driver across presentation retirement.
            // Fresh definition approval remains distinct from graph approval.
            Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>? rawCommit = null;
            ICanonicalAutomationDefinitionOriginalChangeAcknowledgment acknowledgment;
            try { acknowledgment = await source.Read(() => rawCommit = CommitOriginalChangeWithinSourceAsync(intent,
                body => body(), _ => { }, CancellationToken.None)).ConfigureAwait(false); }
            catch
            {
                if (rawCommit is null || !IsAcknowledgedOriginalChangeSourceRefusal(rawCommit) ||
                    !source.AcknowledgeOriginalExternalPreEffectRefusal(rawCommit, IsAcknowledgedOriginalChangeSourceRefusal)) throw;
                await source.JoinAllAsync().ConfigureAwait(false);
                return new(CanonicalAutomationCreationCompletionKind.GraphDraftOnly,
                    "The canonical graph Draft is saved. Home declined or withdrew the separate definition review before SQL; recover this same creation later.");
            }
            if (!_commits.TryGetValue(intent, out var commit) || commit.Atomic is not { } atomic ||
                !IsOwnedOriginalChangeAcknowledgment(intent, acknowledgment, atomic))
                throw new UnauthorizedAccessException("The same original SQL creation did not acknowledge its exact atomic receipt.");
            await source.JoinAllAsync().ConfigureAwait(false);
            return new(CanonicalAutomationCreationCompletionKind.CreatedDisabled,
                intent.ChangeKind == CanonicalAutomationOriginalChangeKind.RecoverCreated
                ? "The exact saved creation and its audit were recovered without another insert. It remains disabled and unscheduled."
                : "Saved a new disabled Automation with an empty canonical graph Draft. Add its graph and schedule before execution can become available.");
        }
        finally
        {
            try { await source.JoinAllAsync().ConfigureAwait(false); }
            finally { _creationExecution.Value = previous; }
        }
    }
    private Task<Intent> PrepareCreationIntentAsync(CreationReservation reservation, HomeGraphCreationAssociationSource.Observation graph,
        Snapshot? recovered, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader(source, async () =>
        {
            if (!_creationGraphs!.IsIssued(graph) || graph.Actor != reservation.Actor || graph.Graph.GraphId != reservation.GraphId ||
                graph.Origin.AutomationId != reservation.AutomationId || graph.Origin.DefinitionOperationId != reservation.DefinitionOperationId ||
                graph.Origin.GraphOperationId != reservation.GraphOperationId || graph.Origin.Name != reservation.Name ||
                graph.Origin.CapturedAt != reservation.CapturedAt || graph.Origin.OriginalAbsenceSha256 != reservation.OriginalAbsenceSha256 ||
                !await source.Read(() => _creationGraphs.IsCurrentAsync(graph, token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The exact original source reservation and actual current Home creation publication are required.");
            Snapshot snapshot;
            if (recovered is null)
            {
                await source.Read(() => RevalidateOriginalCreationWithinSourceAsync(reservation, source.Run, source.Retain, token)).ConfigureAwait(false);
                var absence = await ReadCreationAbsenceAsync(reservation.Actor, reservation.OriginalStoreIdentity, reservation.AutomationId,
                    reservation.DefinitionOperationId, reservation.GraphOperationId, source, token).ConfigureAwait(false);
                DemandCreationAbsence(absence, source);
                snapshot = new(new Dictionary<string, string?>(), Hash(absence), null, null, null, null, null);
            }
            else { snapshot = recovered; DemandCreatedSnapshot(snapshot, reservation, graph); }
            var reference = new CanonicalAutomationGraphReference(1, reservation.GraphId, 1, null);
            var proposed = new AutomationDefinition(reservation.AutomationId, reservation.Name, HavenMode.Tasks,
                CanonicalAutomationGraphReferenceCodec.Serialize(reference), AutomationScheduleKind.Unscheduled, "{}", null, null, false,
                reservation.CapturedAt, reservation.CapturedAt) { GraphBinding = reference.Binding, OperationalState = AutomationOperationalState.Disabled };
            if (recovered is not null)
                proposed = proposed with { Revision = recovered.Descriptor!.Revision, OwnerBinding = recovered.Descriptor.Owner,
                    LastOwnerCommit = recovered.Descriptor.Receipt };
            var intent = new Intent(this, null, recovered is null ? CanonicalAutomationOriginalChangeKind.CreateDisabled :
                CanonicalAutomationOriginalChangeKind.RecoverCreated, reservation.DefinitionOperationId, snapshot, null, proposed, new(reservation, graph));
            await source.JoinAllAsync().ConfigureAwait(false);
            source.Run(() => { _intents.Add(intent, intent); _creationGraphs.BindIntent(intent, graph); }); return intent;
        }));
    }
    private Task RevalidateCreationIntentAsync(Intent intent, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var creation = intent.Creation!; var reservation = creation.Reservation;
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader<object?>(source, async () =>
        {
            await DemandCurrentAsync(intent.Actor, intent.OriginalStoreIdentity, intent.OriginalStoreOwnership, source, token).ConfigureAwait(false);
            if (intent.ChangeKind == CanonicalAutomationOriginalChangeKind.CreateDisabled)
            {
                await source.Read(() => RevalidateOriginalCreationWithinSourceAsync(reservation, source.Run, source.Retain, token)).ConfigureAwait(false);
                var absence = await ReadCreationAbsenceAsync(intent.Actor, intent.OriginalStoreIdentity, reservation.AutomationId,
                    reservation.DefinitionOperationId, reservation.GraphOperationId, source, token).ConfigureAwait(false);
                DemandCreationAbsence(absence, source);
                if (Hash(absence) != intent.Snapshot.RowSha256) throw new UnauthorizedAccessException("The full original creation absence/schema observation changed.");
            }
            else
            {
                var current = await ReadSnapshotAsync(intent.Actor, intent.OriginalStoreIdentity, reservation.AutomationId,
                    intent.OriginalRawId, intent.OperationId, source, token).ConfigureAwait(false);
                if (Hash(current) != Hash(intent.Snapshot)) throw new UnauthorizedAccessException("The actual creation receipt/postimage was superseded; recovery is observation only.");
                DemandCreatedSnapshot(current, reservation, creation.Graph);
            }
            if (!await source.Read(() => _creationGraphs!.IsCurrentAsync(creation.Graph, token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual Home graph creation observation changed.");
            await DemandCurrentAsync(intent.Actor, intent.OriginalStoreIdentity, intent.OriginalStoreOwnership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false); return null;
        }), intent);
    }
    public bool IsIssuedOriginalCreationObservation(ICanonicalAutomationDefinitionOriginalCreationObservation value) =>
        value is CreationDelivery actual && ReferenceEquals(actual.Owner, this) &&
        _creationDeliveries.TryGetValue(value, out var issued) && ReferenceEquals(actual, issued);
    private sealed class CreationDelivery(CanonicalAutomationDefinitionOriginalWriteOwner owner, CreationProcess process)
        : ICanonicalAutomationDefinitionOriginalCreationObservation
    {
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Owner = owner;
        internal CreationProcess Process => process;
        private readonly object _gate = new();
        private readonly TaskCompletionSource _retired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConditionalWeakTable<ICanonicalAutomationDefinitionOriginalCreationCompletion, CreationCompletion> _completions = new();
        private readonly AsyncLocal<int> _inside = new();
        private Task<ICanonicalAutomationDefinitionOriginalCreationCompletion>? _wait; private Task? _close;
        internal bool HasJoinedClose;
        public Guid AutomationId => process.Reservation.AutomationId;
        public Guid GraphId => process.Reservation.GraphId;
        public Task<ICanonicalAutomationDefinitionOriginalCreationCompletion> WaitOriginalCompletionAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); lock (_gate) return _wait ??= Wait();
        }
        private async Task<ICanonicalAutomationDefinitionOriginalCreationCompletion> Wait()
        {
            var previous = _inside.Value; _inside.Value = previous + 1;
            try
            {
                CreationResult result;
                if (!process.Driver.IsCompleted && await Task.WhenAny(process.Driver, _retired.Task).ConfigureAwait(false) != process.Driver)
                    result = new(CanonicalAutomationCreationCompletionKind.ObservationRetired, "This creation observation closed. The producer retains the original business operation.");
                else result = await process.Driver.ConfigureAwait(false);
                var completion = new CreationCompletion(this, result.Kind, result.Message); _completions.Add(completion, completion); return completion;
            }
            finally { _inside.Value = previous; }
        }
        public bool IsIssuedOriginalCompletion(ICanonicalAutomationDefinitionOriginalCreationCompletion value) =>
            value is CreationCompletion actual && ReferenceEquals(actual.OriginalObservation, this) &&
            _completions.TryGetValue(value, out var issued) && ReferenceEquals(actual, issued);
        public void RequestOriginalRetirement() => _retired.TrySetResult();
        public void DemandExternalOriginalJoin()
        { if (_inside.Value != 0) throw new InvalidOperationException("An original creation delivery cannot join its own close."); }
        public Task CloseAndDrainOriginalAsync()
        { DemandExternalOriginalJoin(); RequestOriginalRetirement(); lock (_gate) return _close ??= Close(); }
        private async Task Close()
        {
            Task<ICanonicalAutomationDefinitionOriginalCreationCompletion>? wait; lock (_gate) wait = _wait;
            if (wait is not null) await wait.ConfigureAwait(false); HasJoinedClose = true;
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    }
    private sealed class CreationCompletion(CreationDelivery delivery, CanonicalAutomationCreationCompletionKind kind, string message)
        : ICanonicalAutomationDefinitionOriginalCreationCompletion
    {
        public ICanonicalAutomationDefinitionOriginalCreationObservation OriginalObservation => delivery;
        public CanonicalAutomationCreationCompletionKind Kind => kind;
        public string Message => message;
        public Guid AutomationId => delivery.AutomationId;
        public Guid GraphId => delivery.GraphId;
    }
}
