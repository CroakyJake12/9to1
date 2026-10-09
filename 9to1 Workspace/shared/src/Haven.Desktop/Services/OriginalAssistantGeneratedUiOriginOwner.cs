#if !ANDROID
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Desktop.Controls;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

// Process custody over the configured message reader and the maintained runtime.
// Values, public documents and arbitrary publishers cannot issue provenance.
internal sealed class OriginalAssistantGeneratedUiOriginOwner : ICanonicalGeneratedUiOriginalMessageAuthority
{
    private readonly HomePersonalDenFactory _home;
    private readonly IConversationRepository _conversations;
    private readonly ConversationRepository _repository;
    private readonly CanonicalSqliteOriginalStoreOwner _store;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly GenUiInstanceStore _instances;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<OriginalAssistantGeneratedUiHost, Publisher> _publishers = new();
    private readonly ConditionalWeakTable<ICanonicalGeneratedUiOriginalSelection, Selection> _selections = new();
    private readonly ConditionalWeakTable<ICanonicalGeneratedUiOriginalSnapshot, Snapshot> _snapshots = new();
    private readonly List<Selection> _owned = [];
    private readonly List<Source> _sources = [];
    private readonly List<ICanonicalGeneratedUiOriginalSourcePin> _pins = [];
    private Source? _cleanup;

    internal OriginalAssistantGeneratedUiOriginOwner(HomePersonalDenFactory sameHome, IConversationRepository sameConversations,
        ConversationRepository sameRepository, CanonicalSqliteOriginalStoreOwner sameStore, SqliteDatabase sameDatabase,
        HomeResourceStoreOwnershipAuthority sameOwnership, GenUiInstanceStore sameInstances)
    {
        if (!sameStore.HasOriginalDatabase(sameDatabase) || !sameRepository.HasOriginalGeneratedUiFactory(sameDatabase))
            throw new InvalidOperationException("Use the SAME configured canonical repository and protected database.");
        _home = sameHome; _conversations = sameConversations; _repository = sameRepository;
        _store = sameStore; _ownership = sameOwnership; _instances = sameInstances;
        _work = new(() => Task.CompletedTask, Cleanup);
    }
    public GenUiInstanceStore OriginalInstances => _instances;
    internal CanonicalSqliteOriginalStoreOwner OriginalStore => _store;
    internal HomeResourceStoreOwnershipAuthority OriginalOwnership => _ownership;
    internal HomePersonalDenFactory OriginalHome => _home;
    internal ConversationRepository OriginalRepository => _repository;
    internal IConversationRepository OriginalConversations => _conversations;
    internal Task? OriginalClose => _work.OriginalClose;
    internal void DemandExternalOriginalJoin() { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _work.DemandExternalClose(); }
    internal void RequestOriginalRetirement() => _work.RequestRetirement();
    internal Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin();
        // A historical healthy Task cannot attest to a callback that escaped
        // after that actual cleanup. First close still independently owns cleanup.
        if (_work.OriginalClose is { IsCompletedSuccessfully: true } joined)
        { joined.GetAwaiter().GetResult(); DemandProductiveOriginalSources(); }
        return _work.CloseAndDrainAsync();
    }

    internal sealed class Publisher(OriginalAssistantGeneratedUiHost host, DenAssistantCanonicalBridge bridge)
    { internal readonly OriginalAssistantGeneratedUiHost Host = host; internal readonly DenAssistantCanonicalBridge Bridge = bridge; }
    internal void BindOriginalPublisher(OriginalAssistantGeneratedUiHost sameHost, DenAssistantCanonicalBridge sameBridge)
    {
        _work.RunSynchronous(original =>
        {
            DemandProductiveOriginalSources();
            if (!sameBridge.HasOriginalGeneratedUiMessageComposition(_home, _conversations) ||
                !sameHost.HasOriginalOriginComposition(sameBridge, _instances))
                throw new InvalidOperationException("Only the actual App-created host over the SAME bridge/runtime may publish an origin.");
            lock (_gate)
            {
                if (_publishers.TryGetValue(sameHost, out _)) throw new InvalidOperationException("The exact host publisher is already bound.");
                _publishers.Add(sameHost, new(sameHost, sameBridge));
            }
            DemandProductiveOriginalSources();
        });
    }
    internal sealed class MessageEvidence
    {
        internal MessageEvidence(Publisher publisher, AssistantConversationBinding binding,
            ICanonicalGeneratedUiOriginalBindingEvidence membership, ConversationRepository.OriginalGeneratedMessage message,
            ResourceStoreIdentity identity, VerifiedResourceStoreOwnership receipt)
        { Publisher = publisher; Binding = binding; Membership = membership; Message = message; Identity = identity; Receipt = receipt; }
        private readonly Publisher Publisher;
        internal readonly AssistantConversationBinding Binding;
        internal readonly ICanonicalGeneratedUiOriginalBindingEvidence Membership;
        internal readonly ConversationRepository.OriginalGeneratedMessage Message;
        internal readonly ResourceStoreIdentity Identity;
        internal readonly VerifiedResourceStoreOwnership Receipt;
        internal Publisher OriginalPublisher => Publisher;
    }
    private readonly ConditionalWeakTable<MessageEvidence, object> _messages = new();
    internal Task<MessageEvidence> ObserveOriginalMessageForHostAsync(OriginalAssistantGeneratedUiHost sameHost,
        AssistantConversationBinding sameBinding, Guid messageId, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        _work.RunAsync(async original =>
        {
            var source = NewSource(scope, retain);
            var publisher = source.InvokeProductive(() => _publishers.TryGetValue(sameHost, out var actual) ? actual :
                throw new UnauthorizedAccessException("The original generated host is not a configured publisher."));
            var membership = await source.Read(() => publisher.Bridge.ObserveOriginalGeneratedUiBindingWithinSourceAsync(
                sameBinding, source.Run, source.Retain, token)).ConfigureAwait(false);
            var projected = await ReadMessage(publisher.Bridge, membership, messageId, source, token).ConfigureAwait(false);
            await source.Join().ConfigureAwait(false);
            return source.InvokeProductive(() =>
            {
                var actual = new MessageEvidence(publisher, sameBinding, membership, projected.Message, projected.Identity, projected.Receipt);
                _messages.Add(actual, this); return actual;
            });
        });
    private sealed class Selection(MessageEvidence message, int ordinal, GenUiTemplateRequest request,
        GenUiOriginalInstanceObservation registration, GenerativeUiSurface child, CustomTemplateRuntime? custom)
        : ICanonicalGeneratedUiOriginalSelection
    {
        internal readonly MessageEvidence Message = message;
        internal readonly int Ordinal = ordinal;
        internal readonly GenUiTemplateRequest Request = request;
        internal readonly string DeclarationHash = Hash(request.Signature);
        internal readonly GenUiOriginalInstanceObservation Registration = registration;
        internal readonly GenerativeUiSurface Child = child;
        internal readonly CustomTemplateRuntime? Custom = custom;
        internal GenUiOriginalMutationReceipt? LastMutation;
        internal EventHandler<GenUiActionResult>? Completed;
        internal bool Released;
    }
    internal ICanonicalGeneratedUiOriginalSelection CaptureOriginalRenderedSelection(OriginalAssistantGeneratedUiHost sameHost,
        MessageEvidence sameMessage, int ordinal, GenUiTemplateRequest sameRequest, GenUiDocument sameCreatedDocument,
        GenUiOriginalInstanceObservation sameRegistration, GenerativeUiSurface sameChild, CustomTemplateRuntime? custom)
    {
        Selection? selection = null;
        _work.RunSynchronous(original =>
        {
            DemandProductiveOriginalSources();
            lock (_gate)
            {
                if (!_messages.TryGetValue(sameMessage, out _) || !_publishers.TryGetValue(sameHost, out var publisher) ||
                    !ReferenceEquals(sameMessage.OriginalPublisher, publisher) || ordinal is < 0 or > 3 ||
                    !ReferenceEquals(sameRegistration.Document, sameCreatedDocument) || !_instances.IsCurrentOriginalObservation(sameRegistration))
                    throw new UnauthorizedAccessException("Use the exact created document/registration and source-issued message occurrence.");
                var parsed = GenUiChatDirectiveParser.Parse(sameMessage.Message.Content);
                if (parsed.Error is not null || ordinal >= parsed.Requests.Count ||
                    parsed.Requests[ordinal].Signature != sameRequest.Signature ||
                    sameCreatedDocument.Origin.ThreadId != sameMessage.Message.ConversationId || sameCreatedDocument.Origin.AppKey != "assistants")
                    throw new UnauthorizedAccessException("The created document no longer belongs to the exact canonical declaration slot.");
                if (_owned.Count >= 256) throw new InvalidOperationException("Original generated origins require process retirement at capacity.");
                var issued = new Selection(sameMessage, ordinal, sameRequest, sameRegistration, sameChild, custom);
                selection = issued;
                issued.Completed = (_, result) => RememberOriginalAction(issued, result);
                _selections.Add(issued, issued); _owned.Add(issued); // Before callback subscription/publication.
                sameChild.ActionCompleted += issued.Completed;
            }
            DemandProductiveOriginalSources();
        });
        return selection!;
    }
    private void RememberOriginalAction(Selection selection, GenUiActionResult sameResult)
    {
        using var physical = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        DemandProductiveOriginalSources();
        lock (_gate)
        {
            if (selection.Released || !_selections.TryGetValue(selection, out var actual) || !ReferenceEquals(actual, selection) ||
                !_instances.TryObserveOriginalMutation(sameResult, out var receipt) || receipt is null ||
                !_instances.IsOriginalContinuation(selection.Registration, receipt))
                throw new InvalidOperationException("Only the same independently joined router apply can continue this generated origin.");
            selection.LastMutation = receipt;
        }
    }
    internal bool OwnsOriginalDocument(OriginalAssistantGeneratedUiHost sameHost, GenUiDocument sameDocument)
    {
        lock (_gate) return _owned.Any(item => !item.Released && ReferenceEquals(item.Message.OriginalPublisher.Host, sameHost) &&
            ReferenceEquals(item.Registration.Document, sameDocument));
    }
    public bool IsIssuedOriginalSelection(ICanonicalGeneratedUiOriginalSelection same) =>
        same is Selection actual && _selections.TryGetValue(same, out var issued) && ReferenceEquals(actual, issued) && !actual.Released;
    private Selection RequireSelection(ICanonicalGeneratedUiOriginalSelection same) => IsIssuedOriginalSelection(same) ?
        (Selection)same : throw new UnauthorizedAccessException("Only this process source's exact original selection is accepted.");
    private sealed class Snapshot(Selection selection, GenUiAppDefinition definition, GenUiOriginalMutationReceipt? mutation)
        : ICanonicalGeneratedUiOriginalSnapshot
    {
        internal readonly Selection Selection = selection;
        internal readonly string DefinitionDigest = Hash(JsonSerializer.Serialize(definition));
        public AuthenticatedResourceActor HomeActor => Selection.Message.Membership.HomeActor;
        public Guid ConversationId => Selection.Message.Message.ConversationId;
        public Guid MessageId => Selection.Message.Message.MessageId;
        public int TemplateOrdinal => Selection.Ordinal;
        public string MessageContentSha256 => Hash(Selection.Message.Message.Content);
        public string OriginalDeclarationSha256 => Selection.DeclarationHash;
        public VerifiedResourceStoreOwnership OriginalDenOwnership => Selection.Message.Membership.OriginalDenOwnership;
        public GenUiAppDefinition Definition { get; } = definition;
        public GenUiOriginalInstanceObservation OriginalRegistration => Selection.Registration;
        public GenUiOriginalMutationReceipt? OriginalMutation { get; } = mutation;
    }
    public Task<ICanonicalGeneratedUiOriginalSnapshot> ObserveOriginalSnapshotWithinSourceAsync(
        ICanonicalGeneratedUiOriginalSelection same, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        _work.RunAsync<ICanonicalGeneratedUiOriginalSnapshot>(async original =>
        {
            var source = NewSource(scope, retain); var selected = source.InvokeProductive(() => RequireSelection(same));
            await RevalidateMessage(selected.Message, source, token).ConfigureAwait(false);
            await source.Join().ConfigureAwait(false);
            return source.InvokeProductive(() =>
            {
                lock (_gate)
                {
                    var mutation = selected.LastMutation;
                    var document = mutation?.OriginalSuccessor?.Document ?? selected.Registration.Document;
                    if (mutation is null ? !_instances.IsCurrentOriginalObservation(selected.Registration) :
                        !_instances.IsOriginalContinuation(selected.Registration, mutation))
                        throw new UnauthorizedAccessException("The exact observed runtime lineage is no longer current.");
                    var plan = new GenUiGenerationPlan("Save the interaction from this original canonical message", "assistants", selected.Request.TemplateKey);
                    var definition = GenUiGenerationPipeline.CreateSpecification(plan, document).Definition;
                    var semantic = GenUiSemanticValidator.ValidateAndRepair(definition);
                    if (!semantic.IsValid || semantic.Repairs.Count != 0) throw new InvalidDataException("The original runtime definition requires repair.");
                    var snapshot = new Snapshot(selected, definition, mutation); _snapshots.Add(snapshot, snapshot); return snapshot;
                }
            });
        });
    public bool IsIssuedOriginalSnapshot(ICanonicalGeneratedUiOriginalSnapshot same) => same is Snapshot actual &&
        _snapshots.TryGetValue(same, out var issued) && ReferenceEquals(actual, issued) && IsIssuedOriginalSelection(actual.Selection);
    private Snapshot RequireSnapshot(ICanonicalGeneratedUiOriginalSnapshot same) => IsIssuedOriginalSnapshot(same) ?
        (Snapshot)same : throw new UnauthorizedAccessException("Only the same original message authority snapshot is accepted.");
    public void DemandOriginalSnapshotCurrent(ICanonicalGeneratedUiOriginalSnapshot same)
    {
        DemandProductiveOriginalSources();
        lock (_gate)
        {
            var actual = RequireSnapshot(same);
            if (!ReferenceEquals(actual.Selection.LastMutation, actual.OriginalMutation) ||
                Hash(JsonSerializer.Serialize(actual.Definition)) != actual.DefinitionDigest ||
                (actual.OriginalMutation is null ? !_instances.IsCurrentOriginalObservation(actual.OriginalRegistration) :
                    !_instances.IsOriginalContinuation(actual.OriginalRegistration, actual.OriginalMutation)))
                throw new UnauthorizedAccessException("The exact runtime snapshot was replaced, changed or aliased.");
        }
    }
    public Task RevalidateOriginalSnapshotWithinSourceAsync(ICanonicalGeneratedUiOriginalSnapshot same,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _work.RunAsync(async original =>
        {
            var source = NewSource(scope, retain); var actual = source.InvokeProductive(() => RequireSnapshot(same));
            source.Run(() => DemandOriginalSnapshotCurrent(actual));
            await RevalidateMessage(actual.Selection.Message, source, token).ConfigureAwait(false);
            source.Run(() => DemandOriginalSnapshotCurrent(actual)); await source.Join().ConfigureAwait(false);
        });
    public Task<ICanonicalGeneratedUiOriginalSourcePin> AcquireOriginalCommitPinWithinSourceAsync(
        ICanonicalGeneratedUiOriginalSnapshot same, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        _work.RunAsync<ICanonicalGeneratedUiOriginalSourcePin>(async original =>
        {
            var source = NewSource(scope, retain); var actual = source.InvokeProductive(() => RequireSnapshot(same));
            source.Run(() => DemandOriginalSnapshotCurrent(actual));
            var result = await source.ReadCapture(() => actual.Selection.Message.OriginalPublisher.Bridge.AcquireOriginalGeneratedUiEvidencePinWithinSourceAsync(
                actual.Selection.Message.Membership, actual, source.Run, source.Retain, captured =>
                { lock (_gate) _pins.Add(captured); }, token), captured =>
                { lock (_gate) if (!_pins.Any(item => ReferenceEquals(item, captured))) _pins.Add(captured); }).ConfigureAwait(false);
            source.Run(() => DemandOriginalSnapshotCurrent(actual)); await source.Join().ConfigureAwait(false);
            source.Run(() => { }); return result;
        });
    private CanonicalGeneratedUiInteractionOriginalOwner? _interactionStore;
    internal void BindOriginalInteractionStore(CanonicalGeneratedUiInteractionOriginalOwner same)
    {
        _work.RunSynchronous(original =>
        {
            DemandProductiveOriginalSources();
            if (_interactionStore is not null || !ReferenceEquals(same.OriginalStore, _store) ||
                !ReferenceEquals(same.OriginalMessageAuthority, this) || !ReferenceEquals(same.OriginalOwnership, _ownership))
                throw new UnauthorizedAccessException("Bind the SAME configured protected interaction store once before origins are published.");
            _interactionStore = same;
            DemandProductiveOriginalSources();
        });
    }
    internal Task<GenUiDocument> ComposeOriginalAuditedRestorationAsync(OriginalAssistantGeneratedUiHost sameHost,
        ICanonicalGeneratedUiOriginalSelection sameSelection, ICanonicalGeneratedUiOriginalObservation sameObservation,
        GenUiDocument sameFreshTemplate, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        _work.RunAsync(async original =>
        {
            var source = NewSource(scope, retain); var selected = source.InvokeProductive(() => RequireSelection(sameSelection));
            var store = source.InvokeProductive(() => _interactionStore ?? throw new InvalidOperationException("The original interaction store is not bound."));
            source.Run(() =>
            {
                if (!ReferenceEquals(selected.Message.OriginalPublisher.Host, sameHost) ||
                    !store.IsIssuedOriginalObservation(sameObservation) ||
                    sameObservation.OriginalSnapshot is not Snapshot snapshot || !ReferenceEquals(snapshot.Selection, selected) ||
                    sameObservation.State != CanonicalGeneratedUiOriginalReadState.Restorable)
                    throw new UnauthorizedAccessException("Only the SAME current selection and source-issued audited saved observation may be restored.");
                // Custom handlers retain their original document receipt. A separate
                // canonical handler-transfer supplier is required before restoration.
                if (selected.Request.TemplateKey == "custom") throw new NotSupportedException("Restoring custom action registrations is not available yet.");
            });
            await source.Read(() => store.RevalidateOriginalObservationWithinSourceAsync(sameObservation,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            await RevalidateMessage(selected.Message, source, token).ConfigureAwait(false);
            await source.Join().ConfigureAwait(false);
            return source.InvokeProductive(() =>
            {
                var saved = sameObservation.SavedDefinition ?? throw new UnauthorizedAccessException("The exact successful Home audit did not expose a saved definition.");
                if (sameFreshTemplate.Origin.ThreadId != selected.Message.Message.ConversationId || sameFreshTemplate.Origin.AppKey != "assistants" ||
                    sameFreshTemplate.Origin.TemplateId != saved.Document.Origin.TemplateId ||
                    sameFreshTemplate.Origin.InstanceId == saved.Document.Origin.InstanceId)
                    throw new UnauthorizedAccessException("Restore into the SAME freshly created canonical template occurrence.");
                var document = sameFreshTemplate with { Root = RestoreNode(saved.Document.Root, sameFreshTemplate.Root),
                    State = saved.Document.State.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal) };
                GenerativeUiContractValidator.ValidateAndThrow(document);
                var definition = GenUiGenerationPipeline.CreateSpecification(new("Restore an audited interaction", "assistants", selected.Request.TemplateKey), document).Definition;
                var validation = GenUiSemanticValidator.ValidateAndRepair(definition);
                if (!validation.IsValid || validation.Repairs.Count != 0) throw new InvalidDataException("The audited state cannot be restored into the current canonical template without repair.");
                return document;
            });
        });
    private static GenUiComponent RestoreNode(GenUiComponent saved, GenUiComponent actual)
    {
        if (saved.ComponentId != actual.ComponentId || saved.ComponentType != actual.ComponentType || saved.Children.Count != actual.Children.Count ||
            saved.Actions.Count != actual.Actions.Count || saved.Actions.Where((action, index) =>
                action.ActionId != actual.Actions[index].ActionId || action.Route != actual.Actions[index].Route ||
                action.RiskClass != actual.Actions[index].RiskClass || action.RequiresPermission != actual.Actions[index].RequiresPermission).Any())
            throw new InvalidDataException("The stored interaction no longer matches the same canonical declaration shape.");
        return actual with { Properties = saved.Properties.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal),
            Children = saved.Children.Select((child, index) => RestoreNode(child, actual.Children[index])).ToArray() };
    }

    private async Task RevalidateMessage(MessageEvidence same, Source source, CancellationToken token)
    {
        source.Run(() => { if (!_messages.TryGetValue(same, out _)) throw new UnauthorizedAccessException("The exact original message was not issued."); });
        var current = await ReadMessage(same.OriginalPublisher.Bridge, same.Membership, same.Message.MessageId, source, token).ConfigureAwait(false);
        source.Run(() =>
        {
            if (current.Identity != same.Identity || current.Receipt != same.Receipt || current.Message.BranchId != same.Message.BranchId ||
                current.Message.Content != same.Message.Content || current.Message.ConversationId != same.Message.ConversationId)
                throw new UnauthorizedAccessException("The original effective message, branch or current ownership receipt changed.");
        });
    }
    private async Task<(ConversationRepository.OriginalGeneratedMessage Message, ResourceStoreIdentity Identity, VerifiedResourceStoreOwnership Receipt)>
        ReadMessage(DenAssistantCanonicalBridge bridge, ICanonicalGeneratedUiOriginalBindingEvidence membership, Guid messageId, Source source, CancellationToken token)
    {
        await source.Read(() => bridge.RevalidateOriginalGeneratedUiBindingEvidenceForProcessWithinSourceAsync(membership, source.Run, source.Retain, token)).ConfigureAwait(false);
        var actor = source.InvokeProductive(() => membership.HomeActor);
        var identity = await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(actor, source.Run, source.Retain, token)).ConfigureAwait(false);
        var receipt = await source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync("canonical.sqlite", identity.StoreId.ToString("D"),
            source.Run, source.Retain, token).AsTask()).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("Import the existing canonical store in Home before observing generated interaction provenance.");
        async Task Demand()
        {
            if (!await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(receipt, actor, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The current original Home canonical READ receipt is unavailable.");
            await source.Read(() => bridge.RevalidateOriginalGeneratedUiBindingEvidenceForProcessWithinSourceAsync(membership, source.Run, source.Retain, token)).ConfigureAwait(false);
        }
        await Demand().ConfigureAwait(false);
        CanonicalSqliteOriginalStoreLease? lease = null; ConversationRepository.OriginalGeneratedMessage? message = null;
        var errors = new List<Exception>(); Task? close = null;
        try
        {
            await source.ReadCapture(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(actor, false, source.Run, source.Retain, token),
                actual => lease = actual).ConfigureAwait(false);
            source.Run(() => { if (!_store.IsIssuedOriginalLease(lease!) || lease!.OriginalIdentity != identity)
                throw new UnauthorizedAccessException("The exact protected READ lease/store identity changed."); });
            message = await source.Read(() => _repository.ReadOriginalGeneratedMessageWithinSourceAsync(_store, lease!, membership.ConversationId,
                messageId, source.Run, source.Retain, token)).ConfigureAwait(false);
            source.Run(() =>
            {
                if (!_repository.IsIssuedOriginalGeneratedMessage(message) ||
                    !_repository.MatchesOriginalGeneratedUiConversationScope(message, bridge.ObserveOriginalGeneratedUiExpectedConversation(membership)))
                    throw new UnauthorizedAccessException("The protected canonical conversation/message no longer matches the SAME Den-issued membership scope.");
            });
        }
        catch (Exception cause) { errors.Add(cause); }
        if (lease is not null)
        {
            try { _ = source.Invoke(() => { close = lease.CloseAndDrainAsync(); source.Retain(close); return close; }); }
            catch (Exception cause) { errors.Add(cause); }
            if (close is not null) try { await close.ConfigureAwait(false); }
                catch (Exception cause) { errors.Add(close.Exception ?? cause); }
        }
        try { await source.Join().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("The exact canonical generated message and READ cleanup remain unresolved.", errors);
        await Demand().ConfigureAwait(false); await source.Join().ConfigureAwait(false); return (message!, identity, receipt);
    }
    // The existing bounded cohorts stay in process custody. A successfully joined
    // callback can still escape later; its exact retained cause must fence a new
    // productive body and the final publication of already accepted results.
    private void DemandProductiveOriginalSources()
    {
        Source[] owned; lock (_gate) owned = _sources.ToArray();
        var causes = owned.SelectMany(source => source.Errors.OriginalErrors)
            .Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (causes.Length != 0) throw new AggregateException("Original generated origin sources remain unresolved.", causes);
    }
    private Source NewSource(Action<Action> scope, Action<Task> retain)
    {
        DemandProductiveOriginalSources();
        var source = new Source(this, scope, retain);
        lock (_gate) { if (_sources.Count >= 256) throw new InvalidOperationException("Original source custody requires process retirement at capacity."); _sources.Add(source); }
        return source;
    }
    private async Task Cleanup()
    {
        var source = _cleanup = new Source(this, body => body(), _ => { }, productive: false);
        foreach (var pin in _pins.ToArray())
        {
            Task? raw = null;
            try { _ = source.Invoke(() => { raw = pin.CloseAndDrainOriginalAsync(); source.Retain(raw); return raw; }); }
            catch (Exception cause) { source.Errors.Retain(cause); }
            if (raw is not null) try { await raw.ConfigureAwait(false); } catch (Exception cause) { source.Errors.Capture(raw, cause); }
        }
        foreach (var selected in _owned.ToArray())
        {
            Task? raw = null;
            try { _ = source.Invoke(() => { raw = selected.Child.CloseAndDrainAsync(); source.Retain(raw); return raw; }); }
            catch (Exception cause) { source.Errors.Retain(cause); }
            if (raw is not null) try { await raw.ConfigureAwait(false); } catch (Exception cause) { source.Errors.Capture(raw, cause); }
        }
        foreach (var cohort in _sources.ToArray()) try { await cohort.Join().ConfigureAwait(false); }
            catch (Exception cause) { source.Errors.Retain(cause); }
        await source.Join().ConfigureAwait(false); // No instance/action detach behind unresolved source or child originals.
        foreach (var selected in _owned.ToArray()) source.Run(() =>
        {
            lock (_gate)
            {
                var current = selected.LastMutation?.OriginalSuccessor?.Document ?? selected.Registration.Document;
                if (!ReferenceEquals(_instances.TryGet(current.Origin.InstanceId), current))
                    throw new InvalidOperationException("A foreign replacement cannot be detached as this process's original instance.");
                if (selected.Custom is { } custom && !custom.ReleaseOriginalInstanceActions(selected.Registration.Document))
                    throw new InvalidOperationException("The exact original custom registration was replaced.");
                selected.Child.ActionCompleted -= selected.Completed;
                if (!_instances.RemoveOriginalCurrentObservation(selected.LastMutation?.OriginalSuccessor ?? selected.Registration))
                    throw new InvalidOperationException("No same original runtime occurrence was detached; a foreign replacement remains preserved.");
                selected.Released = true;
            }
        });
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed class Source
    {
        internal readonly CloudflareOriginalTaskLedger Errors = new();
        private readonly Action<Task> _retain;
        private readonly OriginalAssistantGeneratedUiOriginOwner _owner;
        private readonly bool _productive;
        internal Source(OriginalAssistantGeneratedUiOriginOwner owner, Action<Action> scope, Action<Task> retain, bool productive = true)
        {
            _owner = owner; _productive = productive; _retain = retain; Errors.BindOriginalOwner(owner);
            Errors.BindOriginalCallerCallback(body =>
            {
                var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
                try
                {
                    scope(() =>
                    {
                        if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                        { var cause = new InvalidOperationException("The original origin callback is inactive, repeated or foreign-thread."); Errors.Retain(cause); throw cause; }
                        try { body(); } catch (Exception cause) { Errors.Retain(cause); throw; }
                    });
                    if (Volatile.Read(ref used) != 1) throw new InvalidOperationException("The actual original origin callback was not invoked.");
                }
                catch (Exception cause) { Errors.Retain(cause); throw; }
                finally { Volatile.Write(ref active, 0); }
            });
        }
        // Ordinary finite invocation remains available for independently owned
        // raw custody and resource close, even after productive admission fails.
        internal T Invoke<T>(Func<T> body) => CloudflareOriginalExecutionGuard.InvokeOriginal(_owner, () => Errors.Invoke(body));
        private void DemandProductive() { if (_productive) _owner.DemandProductiveOriginalSources(); }
        internal T InvokeProductive<T>(Func<T> body) => CloudflareOriginalExecutionGuard.InvokeOriginal(_owner, () =>
        {
            DemandProductive();
            var result = Errors.Invoke(() => { DemandProductive(); return body(); });
            DemandProductive(); return result;
        });
        internal void Run(Action body) => InvokeProductive(() => { body(); return 0; });
        internal void Retain(Task raw) { Errors.Track(raw); Invoke(() => { _retain(raw); return true; }); }
        internal Task<T> Read<T>(Func<Task<T>> factory) => ReadCapture(factory, _ => { });
        internal async Task Read(Func<Task> factory)
        {
            Task? raw = null; var errors = new List<Exception>();
            try { _ = InvokeProductive(() => { raw = factory(); Errors.Track(raw); Retain(raw); return raw; }); }
            catch (Exception cause) { errors.Add(cause); }
            if (raw is not null) try { await raw.ConfigureAwait(false); }
                catch (Exception cause) { Errors.Capture(raw, cause); errors.Add(raw.Exception ?? cause); }
            else if (errors.Count == 0) errors.Add(new InvalidOperationException("No exact original source Task was captured."));
            if (errors.Count != 0) throw new AggregateException("Original source acquisition/task custody failed.", errors);
            DemandProductive();
        }
        internal async Task<T> ReadCapture<T>(Func<Task<T>> factory, Action<T> capture)
        {
            Task<T>? raw = null; T result = default!; var errors = new List<Exception>();
            try { _ = InvokeProductive(() => { raw = factory(); Errors.Track(raw); Retain(raw); return raw; }); }
            catch (Exception cause) { errors.Add(cause); }
            if (raw is not null)
                try { result = await raw.ConfigureAwait(false); capture(result); } // Actual late result before rejected publication.
                catch (Exception cause) { Errors.Capture(raw, cause); errors.Add(raw.Exception ?? cause); }
            else if (errors.Count == 0) errors.Add(new InvalidOperationException("No exact original source Task was captured."));
            if (errors.Count != 0) throw new AggregateException("Original source acquisition/task custody failed.", errors);
            DemandProductive();
            return result;
        }
        internal async Task Join()
        { await Errors.ObserveAllOriginalTasksAsync().ConfigureAwait(false); if (Errors.OriginalErrors.Count != 0) throw new AggregateException("Original origin source custody failed.", Errors.OriginalErrors); }
    }
}
#endif
