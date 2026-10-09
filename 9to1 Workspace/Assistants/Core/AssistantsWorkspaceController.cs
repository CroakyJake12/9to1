using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Dev;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Core;

/// <summary>
/// Dedicated Assistants product controller over one presentation-scoped canonical bridge.
/// Configuration/history stay in their existing owners. This class retains actual command and
/// observation originals so presentation retirement cannot masquerade as business completion.
/// </summary>
public sealed partial class AssistantsWorkspaceController : IAsyncDisposable
{
    public const int MaximumRetainedOriginals = 64;
    public const int MaximumRetainedObservations = 32;
    public const int MaximumPresentationSubscribers = 16;
    private readonly IAssistantCanonicalBridge _bridge;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly List<Original> _originals = [];
    private readonly List<Observation> _observations = [];
    private readonly List<Exception> _presentationFaults = [];
    private readonly AsyncLocal<Original?> _executing = new();
    [ThreadStatic] private static Dictionary<AssistantsWorkspaceController, int>? _synchronousSources;
    private AssistantsWorkspaceSnapshot _snapshot = AssistantsWorkspaceSnapshot.Empty;
    private bool _retiring;
    private Task? _close;
    private long _navigationGeneration;
    private Action<AssistantsWorkspaceSnapshot>? _stateChanged;

    private sealed class Original
    {
        public Task Task = null!;
        public Original? Parent;
        public bool Live;
        public bool KnownRefusal;
        public readonly List<Task> Sources = [];
        public readonly List<AggregateException> OwnedFaultContainers = [];
        public readonly List<Exception> LocalRefusals = [];
    }
    private sealed class Observation(object original, Action request, Action demandExternalJoin, Func<Task> close)
    {
        public object Original { get; } = original;
        public Action Request { get; } = request;
        public Action DemandExternalJoin { get; } = demandExternalJoin;
        public Func<Task> Close { get; } = close;
        public Task? OriginalClose;
        public bool RetirementRequested;
    }

    public AssistantsWorkspaceController(IAssistantCanonicalBridge bridge) =>
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));

    public event Action<AssistantsWorkspaceSnapshot> StateChanged
    {
        add
        {
            lock (_gate)
            {
                DemandAdmission();
                if ((_stateChanged?.GetInvocationList().Length ?? 0) >= MaximumPresentationSubscribers)
                    throw new InvalidOperationException("Assistants presentation subscriber custody is full.");
                _stateChanged += value;
            }
        }
        remove { lock (_gate) _stateChanged -= value; }
    }
    public AssistantsWorkspaceSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    /// <summary>Actual component pairing only; this reference issues no actor/resource grant.</summary>
    public IAssistantCanonicalBridge OriginalCanonicalBridge => _bridge;
    public Task? OriginalClose { get { lock (_gate) return _close; } }

    /// <summary>Pure acknowledgment of this controller's SAME settled no-effect command refusal.
    /// Query immediately after observing that original command; later healthy custody pruning
    /// may withdraw the observation. This issues no effect, continuation or permission authority.</summary>
    public bool IsAcknowledgedOriginalCommandRefusal(Task actualCommand)
    {
        if (actualCommand is null || !actualCommand.IsCompleted) return false;
        lock (_gate)
        {
            var original = _originals.SingleOrDefault(value => ReferenceEquals(value.Task, actualCommand));
            return original is not null && !Volatile.Read(ref original.Live) && original.KnownRefusal
                && original.Sources.All(source => source.IsCompletedSuccessfully || IsKnownSourceRefusal(source));
        }
    }

    public Task<AssistantsWorkspaceSnapshot> InitializeAsync(CancellationToken token = default) =>
        CommandAsync(async () =>
        {
            var catalogue = await ObserveSourceAsync(() => _bridge.ListAsync(token)).ConfigureAwait(false);
            return Publish(state => state with
            {
                Assistants = Freeze(catalogue.Definitions.Where(value => value.Kind == ConfiguredIdentityKind.Assistant)),
                HostCapabilities = Freeze(catalogue.HostCapabilities), Error = null
            });
        }, false, token);

    public Task<AssistantsWorkspaceSnapshot> CreateAsync(AssistantConfiguration configuration,
        Guid operationId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration = FreezeConfiguration(configuration);
        var generation = BeginNavigation(clearAssistant: true);
        return CommandAsync(async () =>
        {
            var saved = await ObserveSourceAsync(() => _bridge.CreateAsync(ConfiguredIdentityKind.Assistant, configuration, operationId, token)).ConfigureAwait(false);
            UpsertDefinition(saved);
            return IsCurrentNavigation(generation)
                ? Publish(state => state with { SelectedAssistant = saved, Conversations = [], ConversationBinding = null,
                    Conversation = null, Work = null, Error = null }) : Snapshot;
        }, true, token);
    }

    public Task<AssistantsWorkspaceSnapshot> OpenAssistantAsync(AssistantIdentity identity, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var generation = BeginNavigation(clearAssistant: true);
        return CommandAsync(async () =>
        {
            var definition = await ObserveSourceAsync(() => _bridge.GetAsync(identity, token)).ConfigureAwait(false);
            if (definition.Kind != ConfiguredIdentityKind.Assistant)
                throw IssueLocalRefusal("This product opens configured Assistants. Resolve an ambiguous legacy definition explicitly.");
            var conversations = await ObserveSourceAsync(() => _bridge.ReadConversationsAsync(identity, token: token)).ConfigureAwait(false);
            UpsertDefinition(definition);
            return IsCurrentNavigation(generation)
                ? Publish(state => state with { SelectedAssistant = definition, Conversations = Freeze(conversations),
                    ConversationBinding = null, Conversation = null, Work = null, Error = null }) : Snapshot;
        }, false, token);
    }

    public Task<AssistantsWorkspaceSnapshot> ConfigureAsync(AssistantIdentity identity, long expectedRevision,
        AssistantConfiguration configuration, Guid operationId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(configuration);
        configuration = FreezeConfiguration(configuration);
        var generation = CurrentNavigation();
        return CommandAsync(async () =>
        {
            var saved = await ObserveSourceAsync(() => _bridge.UpdateAsync(identity, expectedRevision, configuration, operationId, token)).ConfigureAwait(false);
            UpsertDefinition(saved);
            // Publish only an acknowledged definition. A failure/conflict leaves the prior saved
            // state intact; the native form owns and preserves its separate unsaved draft.
            Publish(state => IsCurrentNavigation(generation) && state.SelectedAssistant?.Identity == identity
                ? state with { SelectedAssistant = saved, Error = null } : state);
            var binding = Snapshot.ConversationBinding;
            if (IsCurrentNavigation(generation) && binding?.Definition.Identity == identity)
            {
                var fresh = await ObserveSourceAsync(() => _bridge.OpenConversationAsync(identity, binding.Conversation.Id, token)).ConfigureAwait(false);
                Publish(state => ReferenceEquals(state.ConversationBinding, binding)
                    ? state with { ConversationBinding = fresh, Models = [] } : state);
            }
            return Snapshot;
        }, true, token);
    }

    public Task<AssistantOriginalProjectCatalogue> ReadOriginalProjectCandidatesAsync(int maximum = 32,
        CancellationToken token = default) => CommandAsync(() => ObserveSourceAsync(() =>
            _bridge.ReadOriginalProjectCandidatesAsync(maximum, token)), false, token);

    public Task<AssistantOriginalProjectChoice> AuthorizeOriginalProjectChoiceAsync(AssistantOriginalProjectCandidate candidate,
        CancellationToken token = default) => CommandAsync(() => ObserveSourceAsync(() =>
            _bridge.AuthorizeOriginalProjectChoiceAsync(candidate, token)), false, token);

    public Task<AssistantsWorkspaceSnapshot> NewProjectConversationOriginalAsync(AssistantOriginalProjectChoice actualChoice,
        Guid conversationId, string title, Guid operationId, CancellationToken token = default)
    {
        var definition = DemandSelected(); var generation = BeginNavigation();
        return CommandAsync(async () =>
        {
            var binding = await ObserveSourceAsync(() => _bridge.CreateProjectConversationOriginalAsync(definition.Identity,
                definition.Revision, conversationId, title, operationId, actualChoice, token)).ConfigureAwait(false);
            return await ReadAndPublishConversationAsync(binding, generation, token).ConfigureAwait(false);
        }, true, token);
    }

    public Task<AssistantsWorkspaceSnapshot> NewConversationAsync(Guid conversationId, string title,
        Guid operationId, CancellationToken token = default, AssistantConversationKind kind = AssistantConversationKind.Chat)
    {
        var definition = DemandSelected(); var generation = BeginNavigation();
        return CommandAsync(async () =>
        {
            var binding = await ObserveSourceAsync(() => _bridge.CreateConversationAsync(definition.Identity, definition.Revision,
                conversationId, title, operationId, token, kind)).ConfigureAwait(false);
            return await ReadAndPublishConversationAsync(binding, generation, token).ConfigureAwait(false);
        }, true, token);
    }

    public Task<AssistantsWorkspaceSnapshot> OpenConversationAsync(Guid conversationId, CancellationToken token = default)
    {
        var identity = DemandSelected().Identity; var generation = BeginNavigation();
        return CommandAsync(async () =>
        {
            var binding = await ObserveSourceAsync(() => _bridge.OpenConversationAsync(identity, conversationId, token)).ConfigureAwait(false);
            return await ReadAndPublishConversationAsync(binding, generation, token).ConfigureAwait(false);
        }, false, token);
    }

    public Task<AssistantsWorkspaceSnapshot> RefreshWorkAsync(CancellationToken token = default)
    {
        var binding = DemandBinding();
        return CommandAsync(async () =>
        {
            var data = await ObserveSourceAsync(() => _bridge.ReadConversationAsync(binding, token)).ConfigureAwait(false);
            var work = await ObserveSourceAsync(() => _bridge.ReadWorkAsync(binding, token)).ConfigureAwait(false);
            var conversations = await ObserveSourceAsync(() => _bridge.ReadConversationsAsync(binding.Definition.Identity, token: token)).ConfigureAwait(false);
            return Publish(state => ReferenceEquals(state.ConversationBinding, binding)
                ? state with { Conversation = data, Work = work, Conversations = Freeze(conversations), Error = null } : state);
        }, false, token);
    }

    public Task<AssistantOriginalSendObservation> StartOriginalTaskAsync(AssistantTaskInput input, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input); var binding = DemandBinding();
        return CommandAsync(async () =>
        {
            DemandObservationCapacity();
            var actual = await ObserveSourceAsync(() => _bridge.StartOriginalTaskAsync(binding, input, token)).ConfigureAwait(false);
            RetainObservation(new(actual, actual.RequestRetirement, actual.DemandExternalOriginalRetirementJoin, actual.CloseAndDrainAsync));
            return actual;
        }, false, token);
    }

    public Task<AssistantOriginalConversationObservation> StartOriginalSendAsync(AssistantTaskInput input, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input); var binding = DemandBinding();
        return CommandAsync(async () =>
        {
            DemandObservationCapacity();
            var actual = await ObserveSourceAsync(() => _bridge.SendConversationOriginalAsync(binding, input, token)).ConfigureAwait(false);
            RetainObservation(new(actual, actual.RequestRetirement, actual.DemandExternalOriginalRetirementJoin, actual.CloseAndDrainAsync));
            return actual;
        }, false, token);
    }

    public Task<AssistantsWorkspaceSnapshot> ListAvailableModelsAsync(CancellationToken token = default)
    {
        var binding = DemandBinding();
        return CommandAsync(async () =>
        {
            var models = await ObserveSourceAsync(() => _bridge.ListAvailableModelsAsync(binding, token)).ConfigureAwait(false);
            return Publish(state => ReferenceEquals(state.ConversationBinding, binding)
                ? state with { Models = Freeze(models), Error = null } : state);
        }, false, token);
    }

    public Task SaveDraftAsync(Guid? branchId, string content, IReadOnlyList<Guid> attachmentIds, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(content); ArgumentNullException.ThrowIfNull(attachmentIds);
        var binding = DemandBinding(); var ids = Freeze(attachmentIds);
        return CommandAsync(async () =>
        {
            await ObserveSourceAsync(() => _bridge.SaveConversationDraftAsync(binding, branchId, content, ids, token)).ConfigureAwait(false);
            return true;
        }, true, token);
    }

    public Task<MessageAttachment> ImportAttachmentAsync(string selectedPath, Guid? branchId, CancellationToken token = default)
    {
        var binding = DemandBinding();
        return CommandAsync(() => ObserveSourceAsync(() => _bridge.ImportAttachmentOriginalAsync(binding, selectedPath, branchId, token)), false, token);
    }

    public Task RemoveAttachmentAsync(Guid attachmentId, CancellationToken token = default)
    {
        var binding = DemandBinding();
        return CommandAsync(async () =>
        {
            await ObserveSourceAsync(() => _bridge.RemoveAttachmentOriginalAsync(binding, attachmentId, token)).ConfigureAwait(false);
            return true;
        }, false, token);
    }

    public Task<ConversationBranch> CreateBranchAsync(Guid messageId, string? name, CancellationToken token = default)
    {
        var binding = DemandBinding();
        return CommandAsync(() => ObserveSourceAsync(() => _bridge.CreateBranchAsync(binding, messageId, name, token)), false, token);
    }

    public Task SwitchBranchAsync(Guid branchId, CancellationToken token = default)
    {
        var binding = DemandBinding();
        return CommandAsync(async () =>
        {
            await ObserveSourceAsync(() => _bridge.SwitchBranchAsync(binding, branchId, token)).ConfigureAwait(false);
            return true;
        }, false, token);
    }

    // Return the SAME actual source close Task; this is not a replacement receipt or
    // business cancellation. The native surface joins it independently of its UI driver.
    public Task CloseOriginalObservationAsync(AssistantOriginalConversationObservation actual) => DetachObservation(actual);
    public Task CloseOriginalObservationAsync(AssistantOriginalSendObservation actual) => DetachObservation(actual);
    public Task CloseOriginalObservationAsync(TaskRunOriginalResumeObservationLease actual) => DetachObservation(actual);

    public Task<FollowUpDecision> SubmitFollowUpAsync(ProviderExecutionContext expectedTask, string instruction,
        TaskFollowUpMode mode, CancellationToken token = default)
    {
        var binding = DemandBinding();
        return CommandAsync(() => ObserveSourceAsync(() => _bridge.SubmitFollowUpAsync(binding, expectedTask, instruction, mode, token)), false, token);
    }

    public Task<TaskRunOriginalRunControlResult> ControlOriginalRunAsync(ProviderExecutionContext expectedTask,
        TaskRunOriginalRunControlKind kind, CancellationToken token = default)
    {
        var binding = DemandBinding();
        return CommandAsync(() => ObserveSourceAsync(() => _bridge.ControlOriginalRunAsync(binding, expectedTask, kind, token)), false, token);
    }

    public Task<TaskRunOriginalResumeObservationLease> StartObservedOriginalResumeAsync(
        ProviderExecutionContext expectedTask, CancellationToken token = default)
    {
        var binding = DemandBinding();
        return CommandAsync(async () =>
        {
            DemandObservationCapacity();
            var actual = await ObserveSourceAsync(() => _bridge.StartObservedOriginalResumeAsync(binding, expectedTask, token)).ConfigureAwait(false);
            RetainObservation(new(actual, actual.RequestOriginalObservationRetirement,
                _bridge.DemandExternalOriginalRetirementJoin, actual.DetachAndDrainAsync));
            return actual;
        }, false, token);
    }

    public Task<AssistantDevelopmentBinding> OpenDevelopmentOriginalAsync(DeveloperProjectReference reference,
        ProviderExecutionContext expectedTask, CancellationToken token = default)
    {
        var binding = DemandBinding();
        return CommandAsync(() => ObserveSourceAsync(() => _bridge.OpenDevelopmentOriginalAsync(binding, reference, expectedTask, token)), false, token);
    }

    public Task<AssistantsWorkspaceSnapshot> DisableAsync(long expectedRevision, Guid operationId, CancellationToken token = default)
    {
        var selected = DemandSelected();
        return ConfigureAsync(selected.Identity, expectedRevision, selected.Configuration with { Enabled = false }, operationId, token);
    }

    public Task<AssistantsWorkspaceSnapshot> ArchiveAsync(long expectedRevision, Guid operationId, CancellationToken token = default)
    {
        var selected = DemandSelected();
        return ConfigureAsync(selected.Identity, expectedRevision, selected.Configuration with { Archived = true }, operationId, token);
    }

    public void RequestRetirement()
    {
        Observation[] observations;
        lock (_gate) { _retiring = true; observations = _observations.ToArray(); }
        Publish(state => state with { IsRetiring = true }, notify: false);
        // Already-admitted controller commands may still be waiting to enter the scoped
        // bridge. Its admission seals after those commands settle in DrainAsync.
        foreach (var observation in observations)
            RequestObservationRetirement(observation);
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_synchronousSources?.ContainsKey(this) == true)
            throw new InvalidOperationException("An Assistants source/publication callback cannot join its own controller.");
        using (EnterSynchronousSource()) _bridge.DemandExternalOriginalRetirementJoin();
        for (var current = _executing.Value; current is not null; current = current.Parent)
            if (Volatile.Read(ref current.Live))
                throw new InvalidOperationException("An Assistants presentation original cannot join its own retirement.");
        Observation[] observations; lock (_gate) observations = _observations.ToArray();
        foreach (var observation in observations) observation.DemandExternalJoin();
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        TaskCompletionSource start; Original[] originals; Task actual;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true; originals = _originals.ToArray();
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = DrainAsync(start.Task, originals); _close = actual;
        }
        start.TrySetResult(); return actual;
    }

    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private async Task DrainAsync(Task start, Original[] originals)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        try { RequestRetirement(); } catch (Exception error) { errors.Add(error); }
        foreach (var original in originals)
        {
            try { await original.Task.ConfigureAwait(false); }
            catch (Exception error) { if (!original.KnownRefusal) Capture(errors, original.Task, error); }
            Task[] sources; lock (_gate) sources = original.Sources.ToArray();
            foreach (var source in sources)
                try { await source.ConfigureAwait(false); }
                catch (Exception error)
                {
                    // Join each raw returned source independently. Await's first exception
                    // cannot hide an unknown sibling behind a routine CAS refusal.
                    if (!IsKnownSourceRefusal(source)) Capture(errors, source, error);
                }
        }
        Observation[] observations;
        lock (_gate) { observations = _observations.ToArray(); errors.AddRange(_presentationFaults); }
        foreach (var observation in observations)
        {
            RequestObservationRetirement(observation);
            Task? close = null;
            try { close = DetachObservation(observation.Original); await close.ConfigureAwait(false); }
            catch (Exception error) { if (close is null) errors.Add(error); else Capture(errors, close, error); }
        }
        Task? bridgeClose = null;
        try { using (EnterSynchronousSource()) _bridge.RequestRetirement(); } catch (Exception error) { errors.Add(error); }
        try
        {
            using (EnterSynchronousSource()) bridgeClose = _bridge.CloseAndDrainAsync();
            await bridgeClose.ConfigureAwait(false);
        }
        catch (Exception error) { if (bridgeClose is null) errors.Add(error); else Capture(errors, bridgeClose, error); }
        if (errors.Count != 0) throw new AggregateException("Assistants presentation originals did not drain cleanly.", errors.Distinct<Exception>(ReferenceEqualityComparer.Instance));
        Publish(state => state with { IsRetiring = true, IsLoading = false, IsSaving = false }, notify: false);
    }

    private Task<T> CommandAsync<T>(Func<Task<T>> operation, bool saving, CancellationToken token)
    {
        Original original; TaskCompletionSource start; Task<T> actual;
        lock (_gate)
        {
            DemandAdmission();
            _originals.RemoveAll(value => value.Task.IsCompletedSuccessfully || (value.Task.IsCompleted && value.KnownRefusal));
            if (_originals.Count >= MaximumRetainedOriginals)
                throw new InvalidOperationException("Assistants presentation custody is full. Inspect retained unresolved originals before new commands.");
            original = new() { Parent = _executing.Value };
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = ExecuteAsync(start.Task, original, operation, saving, token);
            original.Task = actual; _originals.Add(original);
        }
        start.TrySetResult(); return actual;
    }

    private async Task<T> ExecuteAsync<T>(Task start, Original original, Func<Task<T>> operation, bool saving, CancellationToken token)
    {
        await start.ConfigureAwait(false); var previous = _executing.Value; _executing.Value = original;
        Volatile.Write(ref original.Live, true); var entered = false; T result;
        try
        {
            await _commands.WaitAsync(token).ConfigureAwait(false); entered = true;
            Publish(state => state with { IsLoading = !saving, IsSaving = saving, Error = null });
            result = await operation().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            lock (_gate) original.KnownRefusal = (IsObservedRefusal(original, error)
                && original.Sources.All(value => value.IsCompletedSuccessfully || IsKnownSourceRefusal(value)))
                || (error is OperationCanceledException && !entered && original.Sources.Count == 0);
            Publish(state => state with { Error = error.Message }); throw;
        }
        finally
        {
            if (entered) { Publish(state => state with { IsLoading = false, IsSaving = false }); _commands.Release(); }
            Volatile.Write(ref original.Live, false); _executing.Value = previous;
        }
        return result is AssistantsWorkspaceSnapshot ? (T)(object)Snapshot : result;
    }

    private async Task<AssistantsWorkspaceSnapshot> ReadAndPublishConversationAsync(
        AssistantConversationBinding binding, long generation, CancellationToken token)
    {
        using (EnterSynchronousSource())
            if (!_bridge.IsIssuedOriginalBinding(binding))
                throw new UnauthorizedAccessException("The original Assistants bridge did not issue this conversation binding.");
        var data = await ObserveSourceAsync(() => _bridge.ReadConversationAsync(binding, token)).ConfigureAwait(false);
        var work = await ObserveSourceAsync(() => _bridge.ReadWorkAsync(binding, token)).ConfigureAwait(false);
        var conversations = await ObserveSourceAsync(() => _bridge.ReadConversationsAsync(binding.Definition.Identity, token: token)).ConfigureAwait(false);
        return IsCurrentNavigation(generation) ? Publish(state => state with
        {
            SelectedAssistant = binding.Definition, ConversationBinding = binding, Conversation = data,
            Work = work, Conversations = Freeze(conversations), Models = [], Error = null
        }) : Snapshot;
    }

    private void UpsertDefinition(AssistantDefinitionSnapshot definition) => Publish(state => state with
    { Assistants = Freeze(state.Assistants.Where(value => value.Identity != definition.Identity).Append(definition)
        .OrderBy(value => value.Configuration.Name, StringComparer.CurrentCultureIgnoreCase)) });

    private AssistantsWorkspaceSnapshot Publish(Func<AssistantsWorkspaceSnapshot, AssistantsWorkspaceSnapshot> update, bool notify = true)
    {
        AssistantsWorkspaceSnapshot state; Action<AssistantsWorkspaceSnapshot>? handlers;
        lock (_gate)
        {
            state = update(_snapshot) with { Revision = checked(_snapshot.Revision + 1), IsRetiring = _retiring };
            _snapshot = state; handlers = notify && _presentationFaults.Count == 0 ? _stateChanged : null;
        }
        if (handlers is not null)
            foreach (Action<AssistantsWorkspaceSnapshot> handler in handlers.GetInvocationList())
                try { using (EnterSynchronousSource()) handler(state); }
                catch (Exception error) { RecordPresentationFault(error); }
        return state;
    }

    private AssistantDefinitionSnapshot DemandSelected()
    { lock (_gate) { DemandAdmission(); return _snapshot.SelectedAssistant ?? throw new InvalidOperationException("Open an Assistant first."); } }
    private AssistantConversationBinding DemandBinding()
    { lock (_gate) { DemandAdmission(); return _snapshot.ConversationBinding ?? throw new InvalidOperationException("Open an Assistant conversation first."); } }
    private void DemandAdmission()
    { if (_retiring) throw new ObjectDisposedException(nameof(AssistantsWorkspaceController), "Assistants presentation is retiring."); }
    private long CurrentNavigation() { lock (_gate) { DemandAdmission(); return _navigationGeneration; } }
    private long BeginNavigation(bool clearAssistant = false)
    {
        long generation; lock (_gate) { DemandAdmission(); generation = ++_navigationGeneration; }
        Publish(state => state with
        {
            SelectedAssistant = clearAssistant ? null : state.SelectedAssistant,
            Conversations = clearAssistant ? [] : state.Conversations,
            DeclinedPendingConversations = clearAssistant ? [] : state.DeclinedPendingConversations,
            LastCompatibleConversationOutcome = null,
            ConversationBinding = null, Conversation = null, Work = null, Models = [], IsLoading = true, Error = null
        });
        return generation;
    }
    private bool IsCurrentNavigation(long generation) { lock (_gate) return generation == _navigationGeneration && !_retiring; }
    private void DemandObservationCapacity()
    {
        lock (_gate)
        {
            _observations.RemoveAll(value => value.OriginalClose?.IsCompletedSuccessfully == true);
            if (_observations.Count >= MaximumRetainedObservations)
                throw new InvalidOperationException("Assistants observation custody is full. Close the retained presentation before new work.");
        }
    }
    private void RetainObservation(Observation observation)
    {
        bool retiring; lock (_gate) { _observations.Add(observation); retiring = _retiring; }
        if (retiring) RequestObservationRetirement(observation);
    }
    private Task DetachObservation(object actual)
    {
        DemandExternalOriginalRetirementJoin();
        ArgumentNullException.ThrowIfNull(actual); Observation observation;
        lock (_gate) observation = _observations.SingleOrDefault(value => ReferenceEquals(value.Original, actual))
            ?? throw new UnauthorizedAccessException("This controller did not retain the original observation.");
        observation.DemandExternalJoin();
        Task close;
        RequestObservationRetirement(observation);
        using (EnterSynchronousSource()) close = observation.Close();
        lock (_gate)
        {
            if (observation.OriginalClose is { } previous && !ReferenceEquals(previous, close))
                throw new InvalidOperationException("The observation source replaced its original close Task.");
            observation.OriginalClose = close;
        }
        return close;
    }
    private void RequestObservationRetirement(Observation observation)
    {
        lock (_gate)
        {
            if (observation.RetirementRequested) return;
            observation.RetirementRequested = true;
        }
        try { using (EnterSynchronousSource()) observation.Request(); }
        catch (Exception error) { RecordPresentationFault(error); }
    }
    private void RecordPresentationFault(Exception error)
    {
        lock (_gate)
        {
            if (_presentationFaults.Any(value => ReferenceEquals(value, error))) return;
            _presentationFaults.Add(error);
            // Stop later publications after the first failed presentation original.
            // The current bounded subscriber batch is independently attempted and retained.
            _retiring = true;
        }
    }
    private static AssistantConfiguration FreezeConfiguration(AssistantConfiguration configuration) => configuration with
    {
        ToolIds = Freeze(configuration.ToolIds), ConnectedAppIds = Freeze(configuration.ConnectedAppIds),
        KnowledgeResourceIds = Freeze(configuration.KnowledgeResourceIds), ProjectReferences = Freeze(configuration.ProjectReferences),
        Modalities = Freeze(configuration.Modalities), Proactive = configuration.Proactive with
        {
            EventKinds = configuration.Proactive.EventKinds is { } kinds ? Freeze(kinds) : null,
            NotificationChannels = configuration.Proactive.NotificationChannels is { } channels ? Freeze(channels) : null,
            AutomationIds = configuration.Proactive.AutomationIds is { } automations ? Freeze(automations) : null
        }
    };
    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
    private async Task<T> ObserveSourceAsync<T>(Func<Task<T>> source)
    {
        Task<T> actual;
        using (EnterSynchronousSource()) actual = source();
        RetainRawSource(actual);
        try { return await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted && actual.Exception!.InnerExceptions.Count > 1)
        {
            var container = actual.Exception!; RetainOwnedFaultContainer(container); throw container;
        }
    }
    private async Task ObserveSourceAsync(Func<Task> source)
    {
        Task actual;
        using (EnterSynchronousSource()) actual = source();
        RetainRawSource(actual);
        try { await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted && actual.Exception!.InnerExceptions.Count > 1)
        {
            var container = actual.Exception!; RetainOwnedFaultContainer(container); throw container;
        }
    }
    private void RetainRawSource(Task actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (_executing.Value is not { } original)
            throw new InvalidOperationException("A bridge source requires an admitted Assistants command original.");
        lock (_gate) if (!original.Sources.Any(value => ReferenceEquals(value, actual))) original.Sources.Add(actual);
    }
    private IDisposable EnterSynchronousSource()
    {
        var sources = _synchronousSources ??= [];
        sources[this] = sources.GetValueOrDefault(this) + 1;
        return new SynchronousSourceScope(this);
    }
    private sealed class SynchronousSourceScope(AssistantsWorkspaceController owner) : IDisposable
    {
        public void Dispose()
        {
            var sources = _synchronousSources!;
            if (sources[owner] == 1) sources.Remove(owner); else sources[owner]--;
        }
    }
    private void RetainOwnedFaultContainer(AggregateException container)
    {
        lock (_gate) (_executing.Value ?? throw new InvalidOperationException("No original owns this fault container.")).OwnedFaultContainers.Add(container);
    }
    private bool IsOwnedFaultContainer(AggregateException container)
    { lock (_gate) return _originals.Any(original => original.OwnedFaultContainers.Any(value => ReferenceEquals(value, container))); }
    private AssistantCommandRefusedException IssueLocalRefusal(string reason)
    {
        var actual = new AssistantCommandRefusedException(reason);
        lock (_gate) (_executing.Value ?? throw new InvalidOperationException("No original owns this refusal.")).LocalRefusals.Add(actual);
        return actual;
    }
    private bool IsObservedRefusal(Original original, Exception error) =>
        original.LocalRefusals.Any(value => ReferenceEquals(value, error))
        || (IsKnownRefusal(error) && (error is AggregateException container && IsOwnedFaultContainer(container)
            || original.Sources.Any(source => source.Exception?.InnerExceptions.Any(value => ReferenceEquals(value, error)) == true)));
    private bool IsKnownSourceRefusal(Task source) =>
        HavenOS.Apps.Assistants.Canonical.AssistantOriginalExternalRefusalReceipts.IsAcknowledgedOriginal(source)
        || (source.IsFaulted && source.Exception is { } container
            && container.InnerExceptions.Count > 0 && container.InnerExceptions.All(IsKnownRefusal));
    private bool IsKnownRefusal(Exception error) => error switch
    {
        // Only the exact CLR source containers captured/registered by this controller
        // can be unwrapped. A foreign group is an opaque actual cause, even when empty
        // or containing refusal-looking exceptions.
        AggregateException aggregate => IsOwnedFaultContainer(aggregate) && aggregate.InnerExceptions.Count > 0
            && aggregate.InnerExceptions.All(IsKnownRefusal),
        AssistantCommandRefusedException => true,
        DenException { Code: DenErrorCode.InvalidRecord or DenErrorCode.NotFound or DenErrorCode.Forbidden or DenErrorCode.Conflict
            or DenErrorCode.IdempotencyMismatch or DenErrorCode.CapabilityUnavailable or DenErrorCode.RetentionBlocked
            or DenErrorCode.PurgeConfirmationRequired or DenErrorCode.TemporaryChatMemoryWriteBlocked } => true,
        _ => false
    };
    private void Capture(List<Exception> errors, Task actual, Exception observed)
    {
        foreach (var cause in actual.Exception?.InnerExceptions ?? new[] { observed }.AsEnumerable()) CaptureCause(errors, cause);
    }
    private void CaptureCause(List<Exception> errors, Exception cause)
    {
        if (cause is AggregateException aggregate && IsOwnedFaultContainer(aggregate))
        { foreach (var inner in aggregate.InnerExceptions) CaptureCause(errors, inner); return; }
        if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause);
    }
}
