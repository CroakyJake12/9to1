using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

public sealed partial class CanonicalAutomationDefinitionOriginalWriteOwner : ICanonicalAutomationOriginalGraphPublicationSource
{
    private sealed class GraphReadOperation { internal Task? Driver; }
    private sealed class SavedGraphRead(CanonicalAutomationDefinitionOriginalWriteOwner owner, GraphReadOperation operation,
        CanonicalAutomationLibraryOriginalReadOwner.OriginalWriteSelection selection, Snapshot snapshot)
        : ICanonicalAutomationOriginalSavedGraphRead
    {
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Owner = owner;
        internal readonly GraphReadOperation Operation = operation;
        internal readonly CanonicalAutomationLibraryOriginalReadOwner.OriginalWriteSelection Selection = selection;
        internal readonly Snapshot OriginalSnapshot = snapshot;
        public AuthenticatedResourceActor Actor => Selection.Observation.Actor;
        public ResourceStoreIdentity OriginalStoreIdentity => Selection.Observation.OriginalStoreIdentity!;
        public string OriginalCreationOriginSha256 => OriginalSnapshot.Descriptor!.CreationOriginSha256!;
        public AutomationDefinition OriginalDefinition => JsonSerializer.Deserialize<AutomationDefinition>(JsonSerializer.Serialize(Selection.Row.Value))!;
    }
    private sealed class SavedGraphSession(CanonicalAutomationDefinitionOriginalWriteOwner owner, SavedGraphRead read,
        HomeGraphCreationAssociationSource.SavedObservation observation, NodeGraphSchemaRegistry schemas)
        : ICanonicalAutomationOriginalGraphEditSession
    {
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Owner = owner;
        internal readonly SavedGraphRead OriginalRead = read;
        internal readonly HomeGraphCreationAssociationSource.SavedObservation Observation = observation;
        internal readonly object Gate = new();
        internal GraphPublishProcess? Publication;
        internal readonly GraphDocument Baseline = observation.Graph;
        internal readonly NodeGraphDraftEditor Editor = new(observation.Graph, schemas);
        public Guid AutomationId => OriginalRead.Selection.Row.Value.Id;
        public AuthenticatedResourceActor Actor => OriginalRead.Actor;
        public GraphDocument SavedDraft => JsonSerializer.Deserialize<GraphDocument>(JsonSerializer.Serialize(Baseline))!;
        public GraphDocument CurrentDraft { get { lock (Gate) return Editor.Current; } }
        public long LocalRevision { get { lock (Gate) return Editor.Current.Revision; } }
        public bool CanEdit => Publication is null && !OriginalRead.Selection.Row.Value.IsEnabled && OriginalRead.Selection.Row.Value.ArchivedAt is null &&
            Baseline.CapabilityProfileId == CanonicalAutomationCoreNodes.ProfileId && CanonicalAutomationCoreNodes.IsRegistered(schemas);
        public bool HasUnsubmittedChanges
        {
            get { lock (Gate) return JsonSerializer.Serialize(Editor.Current with { Revision = Baseline.Revision }) != JsonSerializer.Serialize(Baseline); }
        }
        public IReadOnlyList<GraphNodeType> RegisteredNodeTypes => schemas.ListNodeTypes(Baseline.CapabilityProfileId);
    }
    private readonly ConditionalWeakTable<SavedGraphRead, GraphReadOperation> _savedGraphReads = new();
    private readonly ConditionalWeakTable<SavedGraphSession, SavedGraphSession> _savedGraphSessions = new();
    public bool SupportsOriginalSavedGraphEditing
    { get { lock (_gate) return !_retiring && _creationGraphs is not null && _creationSchemas is not null && CanonicalAutomationCoreNodes.IsRegistered(_creationSchemas); } }

    public bool IsIssuedOriginalSavedGraphRead(ICanonicalAutomationOriginalSavedGraphRead value)
    {
        lock (_gate) return value is SavedGraphRead actual && (!_retiring || IsActiveGraphPublicationRead(actual)) && ReferenceEquals(actual.Owner, this) &&
            _savedGraphReads.TryGetValue(actual, out var operation) && ReferenceEquals(operation, actual.Operation) &&
            operation.Driver?.IsCompletedSuccessfully == true;
    }
    private SavedGraphRead RequireSavedGraphRead(ICanonicalAutomationOriginalSavedGraphRead value) =>
        IsIssuedOriginalSavedGraphRead(value) ? (SavedGraphRead)value :
            throw new UnauthorizedAccessException("The SAME successful private saved-graph definition READ is required.");
    private Task<SavedGraphRead> CaptureSavedGraphReadWithinSourceAsync(ICanonicalAutomationLibraryOriginalObservation sameObservation,
        AutomationOwnerRead<AutomationDefinition> sameRow, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var operation = new GraphReadOperation();
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        var driver = Admit(source, () => _store.RetainOriginalReader<SavedGraphRead>(source, async () =>
        {
            var selected = source.Invoke(() => _library.ObserveOriginalWriteSelection(sameObservation, sameRow));
            await source.Read(() => _library.RevalidateOriginalObservationWithinSourceAsync(selected.Observation,
                selected.Observation.Actor, source.Run, source.Retain, token)).ConfigureAwait(false);
            var identity = selected.Observation.OriginalStoreIdentity!;
            await DemandCurrentAsync(selected.Observation.Actor, identity, selected.Ownership, source, token).ConfigureAwait(false);
            var snapshot = await ReadSnapshotAsync(selected.Observation.Actor, identity, selected.Row.Value.Id,
                RawText(selected.Row.RetainedProtectedDescriptors, "id"), Guid.Empty, source, token).ConfigureAwait(false);
            DemandDescriptor(snapshot, selected.Observation.Actor, identity, selected.Row.Value.Id);
            if (snapshot.RowSha256 != AutomationDefinitionChange.ComputeRawRowSHA256(selected.Row.RetainedProtectedDescriptors) ||
                snapshot.Descriptor is not { GraphBinding: not null, CreationOriginSha256: { Length: 64 } } descriptor ||
                !CanonicalAutomationGraphReferenceCodec.TryDeserialize(selected.Row.Value.Instruction, out var reference) ||
                descriptor.GraphBinding != reference.Binding || selected.Row.Value.GraphBinding != reference.Binding)
                throw new UnauthorizedAccessException("The actual protected definition/descriptor/canonical graph binding differ.");
            await DemandCurrentAsync(selected.Observation.Actor, identity, selected.Ownership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            var result = new SavedGraphRead(this, operation, selected, snapshot);
            source.Run(() => _savedGraphReads.Add(result, operation)); return result;
        }));
        operation.Driver = driver; return driver;
    }
    public Task RevalidateOriginalSavedGraphReadWithinSourceAsync(ICanonicalAutomationOriginalSavedGraphRead value,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var actual = RequireSavedGraphRead(value);
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader(source, async () =>
        {
            var selection = actual.Selection;
            await DemandCurrentAsync(actual.Actor, actual.OriginalStoreIdentity, selection.Ownership, source, token).ConfigureAwait(false);
            var observed = await ReadSnapshotAsync(actual.Actor, actual.OriginalStoreIdentity, selection.Row.Value.Id,
                RawText(selection.Row.RetainedProtectedDescriptors, "id"), Guid.Empty, source, token).ConfigureAwait(false);
            if (Hash(observed) != Hash(actual.OriginalSnapshot))
                throw new UnauthorizedAccessException("The original saved definition/owner revision changed. Preserve local edits and obtain a fresh selection.");
            await DemandCurrentAsync(actual.Actor, actual.OriginalStoreIdentity, selection.Ownership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false); return true;
        }));
    }
    public Task<GraphDocument?> ReadOriginalSavedGraphWithinSourceAsync(ICanonicalAutomationLibraryOriginalObservation sameObservation,
        AutomationOwnerRead<AutomationDefinition> sameRow, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader<GraphDocument?>(source, async () =>
        {
            var selected = source.Invoke(() => _library.ObserveOriginalWriteSelection(sameObservation, sameRow));
            if (!CanonicalAutomationGraphReferenceCodec.IsReference(selected.Row.Value.Instruction)) return null;
            var original = await source.Read(() => CaptureSavedGraphReadWithinSourceAsync(sameObservation, sameRow,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            var home = _creationGraphs ?? throw new InvalidOperationException("The actual Home saved graph source is unavailable.");
            var observation = await source.Read(() => home.CaptureSavedAsync(original, token)).ConfigureAwait(false);
            if (!home.IsIssuedSavedObservation(observation) || !await source.Read(() => home.IsSavedCurrentAsync(observation, token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The SAME current saved Home graph observation is required.");
            await source.JoinAllAsync().ConfigureAwait(false); return observation.Graph;
        }));
    }
    public Task<ICanonicalAutomationOriginalGraphEditSession> OpenOriginalGraphEditWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation, AutomationOwnerRead<AutomationDefinition> sameRow,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader<ICanonicalAutomationOriginalGraphEditSession>(source, async () =>
        {
            var original = await source.Read(() => CaptureSavedGraphReadWithinSourceAsync(sameObservation, sameRow,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            var home = _creationGraphs ?? throw new InvalidOperationException("The actual Home saved graph source is unavailable.");
            var schemas = _creationSchemas ?? throw new InvalidOperationException("The actual graph schema catalogue is unavailable.");
            var observation = await source.Read(() => home.CaptureSavedAsync(original, token)).ConfigureAwait(false);
            if (!home.IsIssuedSavedObservation(observation) || !await source.Read(() => home.IsSavedCurrentAsync(observation, token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("A copied saved graph observation cannot open a draft session.");
            if (schemas.Validate(observation.Graph).Count != 0)
                throw new InvalidDataException("The saved graph requires registered schema migration; it was preserved.");
            await source.JoinAllAsync().ConfigureAwait(false);
            var result = new SavedGraphSession(this, original, observation, schemas);
            source.Run(() => _savedGraphSessions.Add(result, result)); return result;
        }));
    }
    public bool IsIssuedOriginalGraphEditSession(ICanonicalAutomationOriginalGraphEditSession value)
    {
        lock (_gate) return !_retiring && value is SavedGraphSession actual && ReferenceEquals(actual.Owner, this) &&
            _savedGraphSessions.TryGetValue(actual, out var same) && ReferenceEquals(same, actual) && IsIssuedOriginalSavedGraphRead(actual.OriginalRead);
    }
    public Task<GraphEditResult> EditOriginalGraphWithinSourceAsync(ICanonicalAutomationOriginalGraphEditSession value,
        long expectedLocalRevision, CanonicalAutomationGraphEdit edit, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        if (!IsIssuedOriginalGraphEditSession(value) || value is not SavedGraphSession actual)
            throw new UnauthorizedAccessException("The SAME original saved-graph editing session is required.");
        ArgumentNullException.ThrowIfNull(edit);
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader<GraphEditResult>(source, async () =>
        {
            await source.Read(() => RevalidateOriginalSavedGraphReadWithinSourceAsync(actual.OriginalRead,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            if (!await source.Read(() => _creationGraphs!.IsSavedCurrentAsync(actual.Observation, token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual Home graph changed; local drafts were retained without substitution.");
            await source.JoinAllAsync().ConfigureAwait(false);
            return source.Invoke(() =>
            {
                lock (actual.Gate)
                {
                    GraphEditResult Refuse(string code) => new(false, code, actual.Editor.Current, []);
                    if (!actual.CanEdit) return Refuse("graph.profile.edit-unavailable");
                    if (actual.Editor.Current.Revision != expectedLocalRevision) return Refuse("graph.revision.conflict");
                    if (!Enum.IsDefined(edit.Kind)) return Refuse("graph.edit.unsupported");
                    switch (edit.Kind)
                    {
                        case CanonicalAutomationGraphEditKind.AddNode:
                            if (edit.NodeTypeId is null || !actual.RegisteredNodeTypes.Any(type => type.TypeId == edit.NodeTypeId))
                                return Refuse("graph.node.type-unavailable");
                            return actual.Editor.AddNode(expectedLocalRevision, CanonicalAutomationCoreNodes.CreateNode(_creationSchemas!, edit.NodeTypeId));
                        case CanonicalAutomationGraphEditKind.RemoveNode:
                            return edit.NodeId is { } node ? actual.Editor.RemoveNode(expectedLocalRevision, node) : Refuse("graph.node.not-found");
                        case CanonicalAutomationGraphEditKind.SetValue:
                            if (edit.NodeId is not { } target || !actual.Editor.Current.Nodes.Any(node => node.NodeId == target && node.TypeId == CanonicalAutomationCoreNodes.EmitType))
                                return Refuse("graph.node.value-unavailable");
                            return actual.Editor.SetConfiguration(expectedLocalRevision, target, JsonSerializer.SerializeToElement(new { value = edit.Value ?? "" }));
                        case CanonicalAutomationGraphEditKind.Connect:
                            return edit.OutputPortId is { } output && edit.InputPortId is { } input
                                ? actual.Editor.Connect(expectedLocalRevision, new(Guid.NewGuid(), output, input)) : Refuse("graph.connection.port.missing");
                        case CanonicalAutomationGraphEditKind.Disconnect:
                            return edit.ConnectionId is { } connection ? actual.Editor.Disconnect(expectedLocalRevision, connection) : Refuse("graph.connection.not-found");
                        case CanonicalAutomationGraphEditKind.Undo: return actual.Editor.Undo(expectedLocalRevision);
                        case CanonicalAutomationGraphEditKind.Redo: return actual.Editor.Redo(expectedLocalRevision);
                        default: return Refuse("graph.edit.unsupported");
                    }
                }
            });
        }));
    }
    public Task<bool> IsOriginalBoundSavedGraphWithinSourceAsync(AuthenticatedResourceActor actor,
        ResourceStoreIdentity identity, GraphOwnerDescriptor owner, GraphDocument draft, long? active,
        string originSha256, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader(source, async () =>
        {
            if (owner.AppId != "automations" || owner.EntityKind != "automation.definition" || owner.StoreId != identity.StoreId ||
                owner.EntityId == Guid.Empty || draft.GraphId == Guid.Empty || originSha256.Length != 64) return false;
            var ownership = await source.Read(() => _library.OriginalOwnership.GetVerifiedAsync("canonical.sqlite",
                identity.StoreId.ToString("D"), token).AsTask()).ConfigureAwait(false);
            if (ownership is null) return false;
            await DemandCurrentAsync(actor, identity, ownership, source, token).ConfigureAwait(false);
            // Original graph-backed rows are created with this exact D-format ID.
            // This fixed-ID READ issues no public row or selection authority.
            var snapshot = await ReadSnapshotAsync(actor, identity, owner.EntityId, owner.EntityId.ToString("D"),
                Guid.Empty, source, token).ConfigureAwait(false);
            DemandDescriptor(snapshot, actor, identity, owner.EntityId);
            var binding = new AutomationDefinitionGraphBinding(draft.GraphId, draft.Revision, active);
            var valid = snapshot.Descriptor is { } descriptor && descriptor.GraphBinding == binding &&
                descriptor.CreationOriginSha256 == originSha256 &&
                CanonicalAutomationGraphReferenceCodec.TryDeserialize(RawText(snapshot.Row, "instruction"), out var reference) && reference.Binding == binding;
            await DemandCurrentAsync(actor, identity, ownership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false); return valid;
        }));
    }
    private bool IsActiveGraphPublicationRead(SavedGraphRead actual) =>
        _graphPublicationExecution.Value is { } process && ReferenceEquals(process.Owner, this) &&
        ReferenceEquals(process.Session.OriginalRead, actual) && !process.Driver.IsCompleted;
    private sealed record GraphReady(HomeGraphCreationAssociationSource.PublishedSavedObservation Publication);
    private sealed class SavedGraphPublicationSelection(CanonicalAutomationDefinitionOriginalWriteOwner owner,
        SavedGraphSession session, GraphPublicationIntent intent) : IOriginalGraphOwnerSelection
    {
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Owner = owner;
        internal readonly SavedGraphSession Session = session;
        internal readonly GraphPublicationIntent Intent = intent;
    }
    private readonly ConditionalWeakTable<IOriginalGraphOwnerSelection, SavedGraphPublicationSelection> _savedGraphPublicationSelections = new();
    private readonly AsyncLocal<GraphPublishProcess?> _graphPublicationExecution = new();
    private readonly List<GraphPublishProcess> _graphPublishProcesses = [];
    private readonly List<GraphPublishDelivery> _graphPublishDeliveries = [];
    private readonly ConditionalWeakTable<ICanonicalAutomationOriginalGraphPublicationObservation, GraphPublishDelivery> _graphPublishIssuance = new();
    private SavedGraphPublicationSelection RequireSavedGraphPublicationSelection(IOriginalGraphOwnerSelection value, GraphPublicationIntent intent)
    {
        if (value is not SavedGraphPublicationSelection actual || !ReferenceEquals(actual.Owner, this) ||
            !_savedGraphPublicationSelections.TryGetValue(value, out var issued) || !ReferenceEquals(issued, actual) ||
            !ReferenceEquals(actual.Intent, intent) || !IsIssuedOriginalSavedGraphRead(actual.Session.OriginalRead))
            throw new UnauthorizedAccessException("The exact private saved-graph publication selection is required.");
        return actual;
    }
    private async ValueTask RequireSavedGraphPublicationCurrentAsync(IOriginalGraphOwnerSelection value,
        GraphPublicationIntent intent, CancellationToken token)
    {
        var actual = RequireSavedGraphPublicationSelection(value, intent);
        await RevalidateOriginalSavedGraphReadWithinSourceAsync(actual.Session.OriginalRead, body => body(), _ => { }, token).ConfigureAwait(false);
        if (!await _creationGraphs!.IsSavedCurrentAsync(actual.Session.Observation, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual baseline Home graph changed before publication.");
    }
    private async ValueTask RequireSavedGraphPublicationPublishedAsync(IOriginalGraphOwnerSelection value,
        GraphPublicationIntent intent, GraphPublicationReceipt receipt, CancellationToken token)
    {
        var actual = RequireSavedGraphPublicationSelection(value, intent);
        if (receipt.OperationId != intent.OperationId || receipt.Owner != intent.Owner || receipt.Kind != intent.Kind ||
            receipt.GraphId != intent.Graph.GraphId || receipt.ExpectedGraphRevision != intent.ExpectedGraphRevision ||
            receipt.CommittedGraphRevision != intent.Graph.Revision)
            throw new UnauthorizedAccessException("The actual publisher receipt differs from this private saved-graph proposal.");
        // Home is expected to have changed. The original SQL row must still be
        // unchanged, while the publisher independently verifies its actual record.
        await RevalidateOriginalSavedGraphReadWithinSourceAsync(actual.Session.OriginalRead, body => body(), _ => { }, token).ConfigureAwait(false);
    }
    private sealed record GraphPublishResult(CanonicalAutomationGraphPublicationCompletionKind Kind, string Message);
    private sealed class GraphPublishProcess(CanonicalAutomationDefinitionOriginalWriteOwner owner, SavedGraphSession session,
        GraphPublicationIntent intent, SavedGraphPublicationSelection selection, Guid operation)
    {
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Owner = owner;
        internal readonly SavedGraphSession Session = session;
        internal readonly GraphPublicationIntent Intent = intent;
        internal readonly SavedGraphPublicationSelection Selection = selection;
        internal readonly Guid Operation = operation;
        internal Task<GraphPublishResult> Driver = null!;
        internal CanonicalSqliteOriginalSourceScope Source = null!;
        internal HomeGraphPublicationOwner.Review? Review;
        internal Task<bool>? Withdrawal;
        internal HomeGraphCreationAssociationSource.PublishedSavedObservation? Published;
        internal Intent? BindingIntent;
    }
    public Task<ICanonicalAutomationOriginalGraphPublicationObservation> StartOriginalGraphPublicationWithinSourceAsync(
        ICanonicalAutomationOriginalGraphEditSession value, long expectedLocalRevision, GraphPublicationKind kind,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        if (!IsIssuedOriginalGraphEditSession(value) || value is not SavedGraphSession session)
            throw new UnauthorizedAccessException("The SAME actual saved-graph editor is required for publication.");
        if (!Enum.IsDefined(kind)) throw new ArgumentException("A registered graph publication kind is required.");
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, async () =>
        {
            // Reobserving the SAME submission never repeats its graph effect.
            GraphPublishProcess? existing; lock (session.Gate) existing = session.Publication;
            if (existing is null)
            {
                await source.Read(() => RevalidateOriginalSavedGraphReadWithinSourceAsync(session.OriginalRead,
                    source.Run, source.Retain, token)).ConfigureAwait(false);
                if (!await source.Read(() => _creationGraphs!.IsSavedCurrentAsync(session.Observation, token)).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The actual saved Home graph changed. Local edits remain separate.");
            }
            await source.JoinAllAsync().ConfigureAwait(false);
            GraphPublishDelivery? delivery = null; TaskCompletionSource? begin = null;
            try { source.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                lock (_gate) lock (session.Gate)
                {
                    ObjectDisposedException.ThrowIf(_retiring, this);
                    _graphPublishDeliveries.RemoveAll(item => item.HasJoinedClose);
                    _graphPublishProcesses.RemoveAll(item => item.Driver.IsCompletedSuccessfully &&
                        (item.Driver.GetAwaiter().GetResult().Kind is CanonicalAutomationGraphPublicationCompletionKind.SavedDisabled or
                            CanonicalAutomationGraphPublicationCompletionKind.ActiveGraphDisabled or CanonicalAutomationGraphPublicationCompletionKind.DeclinedBeforeEffect) &&
                        !_graphPublishDeliveries.Any(delivery => ReferenceEquals(delivery.Process, item) && !delivery.HasJoinedClose));
                    if (_graphPublishDeliveries.Count >= 128 || _graphPublishProcesses.Count >= 128)
                        throw new InvalidOperationException("Retain unresolved original graph publications for inspection before more work.");
                    var process = session.Publication;
                    if (process is null)
                    {
                        if (!session.CanEdit || session.Editor.Current.Revision != expectedLocalRevision)
                            throw new UnauthorizedAccessException("The exact editable local graph revision is required.");
                        if (kind == GraphPublicationKind.SaveDraft ? !session.HasUnsubmittedChanges : session.HasUnsubmittedChanges)
                            throw new InvalidOperationException("Save a changed Draft; activate only its exact already-saved revision.");
                        var draft = kind == GraphPublicationKind.SaveDraft
                            ? session.Editor.Current with { Revision = checked(session.Baseline.Revision + 1) } : session.Baseline;
                        if (kind == GraphPublicationKind.Activate)
                        {
                            if (session.Observation.ActiveRevision == draft.Revision || draft.Nodes.Count(node => node.TypeId == CanonicalAutomationCoreNodes.ManualType) != 1 ||
                                !draft.Nodes.Any(node => node.TypeId == CanonicalAutomationCoreNodes.EmitType))
                                throw new InvalidOperationException("Activation requires one registered manual trigger and an action in a saved, not-yet-active Draft.");
                            var projection = new NodeGraphAutomationAdapter(_creationSchemas!, CanonicalAutomationCoreNodes.Bindings()).Compile(draft);
                            if (AutomationGraphRunner.ValidateStructure(projection.Definition).Count != 0)
                                throw new InvalidDataException("The actual owning graph projection is not structurally valid for activation.");
                        }
                        var owner = new GraphOwnerDescriptor("automations", session.OriginalRead.OriginalStoreIdentity.StoreId,
                            "automation.definition", session.AutomationId);
                        var graphOperation = Guid.NewGuid(); var operation = Guid.NewGuid();
                        var proposal = GraphPublicationIntent.Capture(owner, session.Actor, graphOperation, kind, draft,
                            session.Baseline.Revision,
                            [new("automation.definition", owner.StoreId.ToString("D") + "/" + owner.EntityId.ToString("D"),
                                Hash(session.OriginalRead.OriginalSnapshot), ResourceAccess.Write),
                             new("nodegraph.definition", session.Actor.ProfileId + "/" + draft.GraphId.ToString("D"),
                                session.Baseline.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Write)],
                            _creationSchemas!, new(16384, 32));
                        var selected = new SavedGraphPublicationSelection(this, session, proposal);
                        _savedGraphPublicationSelections.Add(selected, selected);
                        process = new(this, session, proposal, selected, operation);
                        process.Source = new(_store.OriginalSourceOwner, Scope(body => body()), _ => { });
                        var original = Reserve(process.Source); begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        process.Driver = DriveSavedGraphPublicationAsync(begin.Task, process);
                        session.Publication = process; _graphPublishProcesses.Add(process); Publish(original, process.Driver);
                    }
                    else if (process.Intent.Kind != kind || process.Session.Editor.Current.Revision != expectedLocalRevision)
                        throw new UnauthorizedAccessException("This editor retains another exact original publication; it cannot lend that operation to a new proposal.");
                    delivery = new(this, process); _graphPublishIssuance.Add(delivery, delivery); _graphPublishDeliveries.Add(delivery);
                }
            }); }
            finally { begin?.TrySetResult(); }
            await source.JoinAllAsync().ConfigureAwait(false);
            return (ICanonicalAutomationOriginalGraphPublicationObservation)(delivery ?? throw new InvalidOperationException("No actual publication delivery was retained."));
        });
    }
    private async Task<GraphPublishResult> DriveSavedGraphPublicationAsync(Task begin, GraphPublishProcess process)
    {
        await begin.ConfigureAwait(false); var previous = _graphPublicationExecution.Value; _graphPublicationExecution.Value = process;
        var source = process.Source;
        try
        {
            if (CreationStopping) return new(CanonicalAutomationGraphPublicationCompletionKind.DeclinedBeforeEffect, "Stopped before graph review. Saved graph and definition were kept.");
            var review = process.Review = _creationGraphs!.Prepare(process.Intent, process.Selection);
            Task<HomePreparedReviewObservation>? submission = null;
            source.Run(() => { lock (_gate) if (!_retiring) { submission = review.SubmitAsync(); source.Retain(submission); } });
            if (submission is null)
            {
                await source.Read(() => _creationGraphs.RetirePublishedReviewAsync(review)).ConfigureAwait(false);
                return new(CanonicalAutomationGraphPublicationCompletionKind.DeclinedBeforeEffect, "Stopped before graph review. No graph was published.");
            }
            var observed = await source.Read(() => submission).ConfigureAwait(false);
            while (observed.Request?.State == HomePermissionRequestState.PendingApproval)
            {
                if (process.Withdrawal is { } withdrawal && await source.Read(() => withdrawal).ConfigureAwait(false))
                { await source.Read(() => _creationGraphs.RetirePublishedReviewAsync(review)).ConfigureAwait(false);
                  return new(CanonicalAutomationGraphPublicationCompletionKind.DeclinedBeforeEffect, "The pending graph review was withdrawn before publication."); }
                await source.Read(() => Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
                observed = await source.Read(() => review.ObserveAsync()).ConfigureAwait(false);
            }
            if (process.Withdrawal is { } finalWithdrawal && await source.Read(() => finalWithdrawal).ConfigureAwait(false))
            {
                await source.Read(() => _creationGraphs.RetirePublishedReviewAsync(review)).ConfigureAwait(false);
                return new(CanonicalAutomationGraphPublicationCompletionKind.DeclinedBeforeEffect, "The exact pending graph review was withdrawn before publication.");
            }
            if (observed.Request?.State is HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked or HomePermissionRequestState.Cancelled)
            {
                await source.Read(() => _creationGraphs.RetirePublishedReviewAsync(review)).ConfigureAwait(false);
                return new(CanonicalAutomationGraphPublicationCompletionKind.DeclinedBeforeEffect, "Home declined graph publication. Saved graph and definition were kept.");
            }
            if (observed.Request?.State != HomePermissionRequestState.Approved)
                return new(CanonicalAutomationGraphPublicationCompletionKind.OutcomeUnconfirmed, "The exact graph review remains unresolved. Do not submit a replacement operation.");
            var receipt = await source.Read(() => review.PublishAsync()).ConfigureAwait(false);
            if (receipt is null) return new(CanonicalAutomationGraphPublicationCompletionKind.OutcomeUnconfirmed,
                "The graph publication outcome is unconfirmed. Its original review is retained without replay.");
            var proof = await source.Read(() => review.CaptureObservationAsync()).ConfigureAwait(false);
            if (proof is null) return new(CanonicalAutomationGraphPublicationCompletionKind.GraphPublishedBindingPending,
                "Home published the graph, but its live publication proof could not be retained. The saved definition was not rebound.");
            var audit = await source.Read(() => review.FinishAuditAsync()).ConfigureAwait(false);
            if (!audit.Succeeded) return new(CanonicalAutomationGraphPublicationCompletionKind.AuditPending,
                "The graph is published, but its original Home audit is pending. The saved definition was not rebound.");
            process.Published = await source.Read(() => _creationGraphs.CapturePublishedSavedAsync(process.Session.Observation,
                process.Intent, proof, CancellationToken.None)).ConfigureAwait(false);
            if (CreationStopping) return new(CanonicalAutomationGraphPublicationCompletionKind.GraphPublishedBindingPending,
                "The graph is published. Stopped before the separate definition-binding review; the exact original operation is retained.");
            process.BindingIntent = await source.Read(() => PrepareSavedGraphBindingIntentAsync(process,
                source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false);
            Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>? raw = null;
            ICanonicalAutomationDefinitionOriginalChangeAcknowledgment acknowledgment;
            try { acknowledgment = await source.Read(() => raw = CommitOriginalChangeWithinSourceAsync(process.BindingIntent,
                body => body(), _ => { }, CancellationToken.None)).ConfigureAwait(false); }
            catch
            {
                if (raw is null || !IsAcknowledgedOriginalChangeSourceRefusal(raw) ||
                    !source.AcknowledgeOriginalExternalPreEffectRefusal(raw, IsAcknowledgedOriginalChangeSourceRefusal)) throw;
                await source.JoinAllAsync().ConfigureAwait(false);
                return new(CanonicalAutomationGraphPublicationCompletionKind.GraphPublishedBindingPending,
                    "Home published the graph but declined its separate saved-definition binding. The original publication is retained; it was not repeated.");
            }
            if (!_commits.TryGetValue(process.BindingIntent, out var commit) || commit.Atomic is not { } atomic ||
                !IsOwnedOriginalChangeAcknowledgment(process.BindingIntent, acknowledgment, atomic) ||
                !IsOwnedOriginalChangeNativeRelease(process.BindingIntent, atomic) || acknowledgment.Definition.GraphBinding != process.Published.Binding)
                throw new UnauthorizedAccessException("The SAME actual graph-binding SQL receipt and healthy native release are required.");
            if (!await source.Read(() => _creationGraphs.RetirePublishedReviewAsync(review)).ConfigureAwait(false))
                throw new InvalidOperationException("The actual completed graph review could not be independently retired.");
            await source.JoinAllAsync().ConfigureAwait(false);
            return process.Intent.Kind == GraphPublicationKind.SaveDraft
                ? new(CanonicalAutomationGraphPublicationCompletionKind.SavedDisabled, "Graph Draft saved and linked to the same Automation. It remains disabled and unscheduled.")
                : new(CanonicalAutomationGraphPublicationCompletionKind.ActiveGraphDisabled, "The exact saved graph revision is Active and linked. Automation execution remains disabled and unscheduled.");
        }
        finally { try { await source.JoinAllAsync().ConfigureAwait(false); } finally { _graphPublicationExecution.Value = previous; } }
    }
    private Task<Intent> PrepareSavedGraphBindingIntentAsync(GraphPublishProcess process,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader(source, async () =>
        {
            var read = process.Session.OriginalRead; var published = process.Published;
            if (published is null || !_creationGraphs!.IsIssuedPublishedSavedObservation(published) ||
                !await source.Read(() => _creationGraphs.IsPublishedSavedCurrentAsync(published, token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The SAME live actual graph publication is required before binding review.");
            await source.Read(() => RevalidateOriginalSavedGraphReadWithinSourceAsync(read, source.Run, source.Retain, token)).ConfigureAwait(false);
            var snapshot = await ReadSnapshotAsync(read.Actor, read.OriginalStoreIdentity, read.OriginalDefinition.Id,
                RawText(read.Selection.Row.RetainedProtectedDescriptors, "id"), process.Operation, source, token).ConfigureAwait(false);
            if (Hash(snapshot) != Hash(read.OriginalSnapshot) || snapshot.Operation is not null)
                throw new UnauthorizedAccessException("The complete original SQL baseline changed before graph binding review.");
            DemandDescriptor(snapshot, read.Actor, read.OriginalStoreIdentity, read.OriginalDefinition.Id); DemandEditableSnapshot(snapshot);
            var reference = new CanonicalAutomationGraphReference(1, published.Binding.GraphId,
                published.Binding.DraftRevision, published.Binding.ActiveRevision);
            var proposed = read.OriginalDefinition with { Instruction = CanonicalAutomationGraphReferenceCodec.Serialize(reference),
                GraphBinding = published.Binding, UpdatedAt = DateTimeOffset.UtcNow, IsEnabled = false, NextRunAt = null,
                OperationalState = AutomationOperationalState.Disabled };
            var kind = process.Intent.Kind == GraphPublicationKind.SaveDraft ? CanonicalAutomationOriginalChangeKind.BindGraphDraftDisabled :
                CanonicalAutomationOriginalChangeKind.BindGraphActiveDisabled;
            await source.JoinAllAsync().ConfigureAwait(false);
            var intent = new Intent(this, read.Selection, kind, process.Operation, snapshot, proposed: proposed, graphPublication: new(published));
            source.Run(() => { _intents.Add(intent, intent); _creationGraphs.BindPublishedSavedIntent(intent, published); }); return intent;
        }));
    }
    public bool IsIssuedOriginalGraphPublicationObservation(ICanonicalAutomationOriginalGraphPublicationObservation value) =>
        value is GraphPublishDelivery actual && ReferenceEquals(actual.Owner, this) &&
        _graphPublishIssuance.TryGetValue(value, out var issued) && ReferenceEquals(actual, issued);
    private sealed class GraphPublishDelivery(CanonicalAutomationDefinitionOriginalWriteOwner owner, GraphPublishProcess process)
        : ICanonicalAutomationOriginalGraphPublicationObservation
    {
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Owner = owner;
        internal GraphPublishProcess Process => process;
        internal bool HasJoinedClose;
        private readonly object _gate = new();
        private readonly TaskCompletionSource _retired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConditionalWeakTable<ICanonicalAutomationOriginalGraphPublicationCompletion, GraphPublishCompletion> _completions = new();
        private readonly AsyncLocal<int> _inside = new();
        private Task<ICanonicalAutomationOriginalGraphPublicationCompletion>? _wait; private Task? _close;
        public Guid OperationId => process.Operation;
        public Guid GraphOperationId => process.Intent.OperationId;
        public Guid AutomationId => process.Session.AutomationId;
        public Guid GraphId => process.Intent.Graph.GraphId;
        public GraphPublicationKind PublicationKind => process.Intent.Kind;
        public Task<ICanonicalAutomationOriginalGraphPublicationCompletion> WaitOriginalCompletionAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); lock (_gate) return _wait ??= Wait(); }
        private async Task<ICanonicalAutomationOriginalGraphPublicationCompletion> Wait()
        {
            var previous = _inside.Value; _inside.Value = previous + 1;
            try
            {
                var result = !process.Driver.IsCompleted && await Task.WhenAny(process.Driver, _retired.Task).ConfigureAwait(false) != process.Driver
                    ? new GraphPublishResult(CanonicalAutomationGraphPublicationCompletionKind.ObservationRetired,
                        "The graph publication view closed. Its producer retains the original business operation.")
                    : await process.Driver.ConfigureAwait(false);
                var completion = new GraphPublishCompletion(this, result.Kind, result.Message); _completions.Add(completion, completion); return completion;
            }
            finally { _inside.Value = previous; }
        }
        public bool IsIssuedOriginalCompletion(ICanonicalAutomationOriginalGraphPublicationCompletion value) =>
            value is GraphPublishCompletion actual && ReferenceEquals(actual.OriginalObservation, this) &&
            _completions.TryGetValue(value, out var issued) && ReferenceEquals(actual, issued);
        public void RequestOriginalRetirement() => _retired.TrySetResult();
        public Task CloseAndDrainOriginalAsync()
        {
            if (_inside.Value != 0) throw new InvalidOperationException("An original graph publication delivery cannot join its own close.");
            RequestOriginalRetirement(); lock (_gate) return _close ??= Close();
        }
        private async Task Close()
        {
            Task<ICanonicalAutomationOriginalGraphPublicationCompletion>? wait; lock (_gate) wait = _wait;
            if (wait is not null) await wait.ConfigureAwait(false); HasJoinedClose = true;
        }
    }
    private sealed class GraphPublishCompletion(GraphPublishDelivery delivery, CanonicalAutomationGraphPublicationCompletionKind kind, string message)
        : ICanonicalAutomationOriginalGraphPublicationCompletion
    {
        public ICanonicalAutomationOriginalGraphPublicationObservation OriginalObservation => delivery;
        public CanonicalAutomationGraphPublicationCompletionKind Kind => kind;
        public string Message => message;
    }
}
