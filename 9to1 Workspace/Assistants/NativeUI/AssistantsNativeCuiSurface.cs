using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.Migration;
using HavenOS.Apps.Assistants.Memory;
using HavenOS.Apps.Assistants.MiniComputer;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>One actual presentation over a scoped controller. No provider, store or business producer is owned here.</summary>
public sealed partial class AssistantsNativeCuiSurface : UserControl, IDisposable, IAsyncDisposable
{
    private readonly AssistantsWorkspaceController _controller;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CancellationTokenSource _viewCancellation = new();
    private readonly object _gate = new();
    private readonly List<Task> _originals = [];
    private readonly List<Task> _snapshotPublications = [];
    private readonly HashSet<Task> _viewWithdrawals = [];
    private readonly ConditionalWeakTable<Task, AcknowledgedRefusal> _acknowledgedRefusals = new();
    private readonly List<Exception> _failures = [];
    private readonly Dictionary<Exception, Exception[]> _ownedGroups = new(ReferenceEqualityComparer.Instance);
    private readonly AsyncLocal<Operation?> _executing = new();
    [ThreadStatic] private static List<AssistantsNativeCuiSurface>? _physicalSources;
    private readonly TaskCompletionSource _construction = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<CuiSceneHost> _scenes = [];
    private CancellationTokenRegistration _hostRetirement;
    private AssistantConversationBinding? _binding;
    private readonly AssistantConversationPresentation _conversation = new();
    private AssistantConversationPresentation.Target? _conversationTarget;
    private long _presentationGeneration;
    private long _appliedSnapshot = -1;
    private bool _retiring;
    private Task? _initialization;
    private Task? _close;
    private AssistantsCuiBindings? _bindings;
    private CuiSceneHost? _sceneHost;
    private CuiControlRegistry? _registry;
    private Guid? _savedPromptConversation;
    private string _savedPrompt = "";
    private int _preparationBlocks;
    private sealed class Operation
    {
        internal bool Active;
        internal Operation? Parent;
        internal int PreparationBlocked = 1;
        internal bool HandlesCommandRefusal;
        internal long? MigrationPublicationGeneration;
        internal readonly HashSet<Exception> RefusedCauses = new(ReferenceEqualityComparer.Instance);
    }
    private sealed class AcknowledgedRefusal { }

    public AssistantsNativeCuiSurface(AssistantsWorkspaceController controller, ICuiSceneReadiness readiness,
        CancellationToken hostLifetime = default, Action<AssistantsNativeCuiSurface>? captureOriginalOwner = null,
        ILegacyAgentMigrationController? migration = null, string? migrationUnavailableReason = null,
        IAssistantsNativeOriginalDevelopmentRoute? developmentRoute = null,
        IAssistantMemoryManagementController? memoryManagement = null, string? memoryUnavailableReason = null,
        IAssistantMiniComputerController? miniComputerManagement = null, string? miniComputerUnavailableReason = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _migration = migration; _developmentRoute = developmentRoute;
        _memoryManagement = memoryManagement;
        _miniComputerManagement = miniComputerManagement;
        _miniComputerUnavailableReason = miniComputerUnavailableReason ?? "The actual Mini Computer catalogue and separate Home VM operation source are not yet configured.";
        _memoryUnavailableReason = memoryUnavailableReason ?? "Scoped Assistant memory requires its actual Home store import and configured memory owner.";
        _migrationUnavailableReason = migrationUnavailableReason ?? "Review and import your existing saved Agents through Home before migration.";
        using var physical = EnterPhysical();
        try
        {
            captureOriginalOwner?.Invoke(this); // Partial owner retained BEFORE native control/subscription publication.
            if (_migration is not null && !_migration.IsOriginalCanonicalBridge(_controller.OriginalCanonicalBridge))
                throw new InvalidOperationException("The migration presentation must borrow this SAME canonical Assistant bridge.");
            if (_memoryManagement is not null && !_memoryManagement.IsOriginalCanonicalBridge(_controller.OriginalCanonicalBridge))
                throw new InvalidOperationException("Memory management must borrow this SAME canonical Assistant bridge.");
            if (_miniComputerManagement is not null && !_miniComputerManagement.IsOriginalCanonicalBridge(_controller.OriginalCanonicalBridge))
                throw new InvalidOperationException("Mini Computer must borrow this SAME canonical Assistant bridge.");
            DemandCurrent();
            _bindings = new(DispatchAsync, PublishSynchronous, () => !IsRetiring, _developmentRoute is not null);
            _bindings.ConfigureOriginalCapabilityInitializationSource(_controller.HasOriginalConfiguredCapabilityInitializationSource);
            _migrationBindings = new(_migration is not null, _migrationUnavailableReason, DispatchMigrationAsync, PublishSynchronous, () => !IsRetiring);
            _memoryBindings = new(_memoryManagement is not null, _memoryUnavailableReason, DispatchMemoryAsync, PublishSynchronous, () => !IsRetiring);
            _miniComputerBindings = new(_miniComputerManagement is not null, _miniComputerUnavailableReason, DispatchMiniComputerAsync, PublishSynchronous, () => !IsRetiring);
            _registry = new();
            var home = OwnScene(new CuiSceneHost(CreateAvatarRegistry())); DemandCurrent();
            var configuration = OwnScene(new CuiSceneHost(CreateAvatarRegistry())); DemandCurrent();
            var conversation = OwnScene(new CuiSceneHost(CreateConversationRegistry()));
            _richConversationScene = conversation; DemandCurrent();
            var workRegistry = CreateAvatarRegistry();
            workRegistry.RegisterObjectRenderer("AssistantConversation", _ => conversation);
            var work = OwnScene(new CuiSceneHost(workRegistry)); DemandCurrent();
            _registry.RegisterObjectRenderer("AssistantsHome", _ => home);
            _registry.RegisterObjectRenderer("AssistantConfiguration", _ => configuration);
            _registry.RegisterObjectRenderer("AssistantWork", _ => work);
            var migrationScene = OwnScene(new CuiSceneHost()); DemandCurrent();
            _registry.RegisterObjectRenderer("AssistantLegacyMigration", _ => migrationScene);
            var memoryScene = OwnScene(new CuiSceneHost()); DemandCurrent();
            _registry.RegisterObjectRenderer("AssistantMemory", _ => memoryScene);
            var miniScene = OwnScene(new CuiSceneHost()); DemandCurrent();
            _registry.RegisterObjectRenderer("AssistantMiniComputer", _ => miniScene);
            _sceneHost = OwnScene(new CuiSceneHost(_registry)); DemandCurrent();
            _controller.StateChanged += OnStateChanged;
            Content = _sceneHost; DemandCurrent();
            _hostRetirement = hostLifetime.Register(static actual => ((AssistantsNativeCuiSurface)actual!).RequestRetirement(), this);
            DemandCurrent();
        }
        catch (Exception failure) { Add(failure); RequestRetirement(); throw; }
        finally { _construction.TrySetResult(); }
    }

    public AssistantsCuiBindings Bindings => _bindings ?? throw new InvalidOperationException("The actual native bindings were not acquired.");
    public AssistantsWorkspaceController OriginalController => _controller;
    public ICuiSceneReadiness OriginalReadiness => _readiness;
    public CuiSceneHost? SceneHost => _sceneHost;
    public CuiControlRegistry? ControlRegistry => _registry;
    public Task? OriginalInitialization { get { lock (_gate) return _initialization; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public AssistantConversationBinding? CurrentConversationBinding => IsRetiring ? null : _binding;
    public long PresentationGeneration => Interlocked.Read(ref _presentationGeneration);
    public bool HasUnsavedChanges => _miniComputerBindings?.HasUnconfirmedChanges == true || _memoryBindings?.HasUnconfirmedChanges == true || HasUnresolvedProjectCreation || _migrationBindings?.HasUnconfirmedChanges == true || _bindings?.OriginalDraft?.IsDirty == true ||
        (_binding is not null && (_savedPromptConversation != _binding.Conversation.Id ||
            _bindings?.OriginalPrompt != _savedPrompt));
    public bool IsOriginalClosePrepared => !IsRetiring && OriginalInitialization?.IsCompletedSuccessfully == true &&
        !HasUnsavedChanges && Volatile.Read(ref _preparationBlocks) == 0 &&
        ReferenceEquals(_binding, _controller.Snapshot.ConversationBinding);
    public bool IsRetiring { get { lock (_gate) return _retiring; } }
    public bool IsPresentationCurrent(AssistantConversationBinding actual, long generation) => !IsRetiring &&
        generation == PresentationGeneration && ReferenceEquals(_binding, actual) &&
        ReferenceEquals(_controller.Snapshot.ConversationBinding, actual);

    public Task InitializeAsync(CancellationToken token = default)
    {
        lock (_gate)
        {
            if (_initialization is not null) return _initialization;
            return RunAsync(async () =>
            {
                Dispatcher.UIThread.VerifyAccess();
                await SourceAsync(() => _controller.InitializeAsync(token));
                var definitions = new[] { AssistantsCuiScene.Home, AssistantsCuiScene.Configuration,
                    AssistantsCuiScene.Conversation, AssistantsCuiScene.Work, AssistantsCuiScene.LegacyMigration, AssistantsCuiScene.Memory, AssistantsCuiScene.MiniComputer, AssistantsCuiScene.Shell };
                for (var index = 0; index < definitions.Length; index++)
                {
                    DemandCurrent();
                    ICuiBindingContext sceneBindings = definitions[index] == AssistantsCuiScene.LegacyMigration ? MigrationBindings :
                        definitions[index] == AssistantsCuiScene.Memory ? MemoryBindings :
                        definitions[index] == AssistantsCuiScene.MiniComputer ? MiniComputerBindings : Bindings;
                    ICuiActionDispatcher sceneActions = definitions[index] == AssistantsCuiScene.LegacyMigration ? MigrationBindings :
                        definitions[index] == AssistantsCuiScene.Memory ? MemoryBindings :
                        definitions[index] == AssistantsCuiScene.MiniComputer ? MiniComputerBindings : Bindings;
                    var scene = new CuiNativeScene("assistants", "Assistants", "Chat", AssistantsCuiScenes.ReadDocument(definitions[index]),
                        sceneBindings, sceneActions, _readiness) { IsPublicationCurrent = () => !IsRetiring };
                    var availability = await SourceAsync(() => _scenes[index].ShowAsync(scene, token));
                    if (availability.State != CuiSceneAvailabilityState.Ready)
                        throw new UnauthorizedAccessException(availability.Message);
                }
                PublishSynchronous(() => ApplySnapshot(_controller.Snapshot));
            }, actual => _initialization = actual);
        }
    }

    /// <summary>Real saves before permanent retirement. Failure/newer edits leave this same view usable.</summary>
    public Task<bool> PrepareToCloseAsync(CancellationToken token = default)
    {
        DemandExternalOriginalRetirementJoin();
        var result = false;
        var actual = RunAsync(async () =>
        {
            // Await the encompassing observer originals, not only their dispatcher
            // callbacks. Later publications still keep the exact close checks busy.
            Task[] publications;
            lock (_gate) publications = _snapshotPublications.ToArray();
            foreach (var publication in publications)
            {
                await SourceAsync(() => publication);
                lock (_gate) _snapshotPublications.Remove(publication);
            }
            if (_memoryBindings?.HasUnconfirmedChanges == true)
            { PublishSynchronous(() => MemoryBindings.SetError("Finish the pending memory review or explicitly discard its local draft before closing.")); return; }
            if (HasUnresolvedProjectCreation)
            { PublishSynchronous(() => Bindings.SetConversationStatus("The earlier project Task creation is unresolved. Review its retained operation before closing.")); return; }
            if (_migrationBindings?.HasUnconfirmedChanges == true)
            { PublishSynchronous(() => MigrationBindings.SetError("Confirm your conversion or cancel this review before closing.")); return; }
            if (Bindings.OriginalDraft?.IsDirty == true)
            {
                if (Bindings.OriginalDraft.Configuration.Name.Trim().Length == 0) return;
                await SaveConfigurationAsync(token);
            }
            if (_binding is not null && (_savedPromptConversation != _binding.Conversation.Id || Bindings.OriginalPrompt != _savedPrompt))
                await SaveDraftAsync(token);
            // Join this actual dispatcher publication after earlier queued snapshot
            // observers. The close decision still refuses live actions/newer edits.
            await SourceAsync(() => Dispatcher.UIThread.InvokeAsync(() =>
            {
                var current = _controller.Snapshot;
                if (current.Revision >= _appliedSnapshot) PublishSynchronous(() => ApplySnapshot(current));
            }, DispatcherPriority.Background).GetTask());
            result = !HasUnsavedChanges && OriginalInitialization?.IsCompletedSuccessfully == true &&
                Volatile.Read(ref _preparationBlocks) == 1 && ReferenceEquals(_binding, _controller.Snapshot.ConversationBinding);
        }, handlesCommandRefusal: true);
        return CompletePreparationAsync();
        async Task<bool> CompletePreparationAsync() { await actual; return result; }
    }

    public void RequestRetirement()
    {
        using var retirementSource = EnterPhysical(); // Actual cancellation and borrowed-owner callbacks cannot join this stop stack.
        lock (_gate) { _retiring = true; }
        _miniComputerBindings?.Revoke();
        _memoryBindings?.Revoke();
        _migrationBindings?.Revoke();
        _bindings?.Revoke(); // Synchronous read/write/action fence; private values cannot be read after revocation.
        try { _viewCancellation.Cancel(); } catch (Exception failure) { Add(failure); }
        try { _miniComputerManagement?.RequestRetirement(); } catch (Exception failure) { Add(failure); }
        try { _memoryManagement?.RequestRetirement(); } catch (Exception failure) { Add(failure); }
        try { _migration?.RequestRetirement(); } catch (Exception failure) { Add(failure); }
        try { RequestOriginalGeneratedUiRetirement(); } catch (Exception failure) { Add(failure); }
        try { _controller.RequestRetirement(); } catch (Exception failure) { Add(failure); }
        if (Dispatcher.UIThread.CheckAccess())
        {
            using var physical = EnterPhysical();
            try { IsVisible = false; } catch (Exception failure) { Add(failure); }
        }
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("The actual Assistant native callback must return before its external join.");
        for (var operation = _executing.Value; operation is not null; operation = operation.Parent)
            if (Volatile.Read(ref operation.Active))
                throw new InvalidOperationException("The actual Assistant presentation original cannot join itself.");
        _miniComputerManagement?.DemandExternalOriginalRetirementJoin();
        _memoryManagement?.DemandExternalOriginalRetirementJoin();
        _migration?.DemandExternalOriginalRetirementJoin();
        DemandOriginalGeneratedUiJoin();
        _controller.DemandExternalOriginalRetirementJoin();
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        TaskCompletionSource start; Task actual;
        lock (_gate)
        {
            if (_close is not null) return _close;
            if (!_retiring && HasUnsavedChanges)
                throw new InvalidOperationException("Save or discard the original Assistant draft before retirement.");
            _retiring = true;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = actual = CloseCoreAsync(start.Task);
        }
        RequestRetirement(); start.TrySetResult(); return actual;
    }

    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private CuiSceneHost OwnScene(CuiSceneHost actual) { _scenes.Add(actual); return actual; }
    private void DemandCurrent() { if (IsRetiring) throw new ObjectDisposedException(nameof(AssistantsNativeCuiSurface)); }
    private void PublishSynchronous(Action publication)
    {
        using var physical = EnterPhysical();
        DemandCurrent(); publication(); DemandCurrent();
    }

    private void OnStateChanged(AssistantsWorkspaceSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_retiring) return;
            if (Dispatcher.UIThread.CheckAccess())
            {
                if (snapshot.Revision >= _appliedSnapshot) PublishSynchronous(() => ApplySnapshot(snapshot));
                return;
            }
            _ = RunAsync(async () => await SourceAsync(() => Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsRetiring || snapshot.Revision < _appliedSnapshot) return;
                PublishSynchronous(() => ApplySnapshot(snapshot));
            }).GetTask()), actual => _snapshotPublications.Add(actual));
        }
    }

    private void ApplySnapshot(AssistantsWorkspaceSnapshot snapshot)
    {
        ApplyOriginalConversationBranchPresentation(snapshot);
        if (!ReferenceEquals(_binding, snapshot.ConversationBinding))
        {
            _binding = snapshot.ConversationBinding;
            Interlocked.Increment(ref _presentationGeneration);
            _conversationTarget = snapshot.Conversation is { } data ? _conversation.Bind(data) : null;
            if (_savedPromptConversation != snapshot.Conversation?.Conversation.Id)
            { _savedPromptConversation = snapshot.Conversation?.Conversation.Id; _savedPrompt = snapshot.Conversation?.Draft?.Content ?? ""; }
            if (_conversationTarget is null) _conversation.Clear();
        }
        _appliedSnapshot = snapshot.Revision;
        Bindings.ApplySnapshot(snapshot);
        Bindings.SetMessages(_conversation.Messages);
    }

    private ValueTask DispatchAsync(string command, object? parameter, CancellationToken token) => new(RunAsync(async () =>
    {
        DemandCurrent();
        switch (command)
        {
            case "assistants.project.newtask": await BeginProjectSelectionAsync(false, token); break;
            case "assistants.resources.add": await BeginConfigurationProjectPreferenceAsync(null, token); break;
            case "assistants.configuration.capabilities.read": await ReadConfigurationCapabilitiesAsync(token); break;
            case "assistants.configuration.capabilities.setup.review": await ReviewConfigurationCapabilityInitializationAsync(token); break;
            case "assistants.configuration.capabilities.setup.start": await StartConfigurationCapabilityInitializationAsync(token); break;
            case "assistants.configuration.capability.choose": await ChooseConfigurationCapabilityAsync(parameter, token); break;
            case "assistants.configuration.project.review": await BeginConfigurationProjectPreferenceAsync(parameter, token); break;
            case "assistants.development.open": await BeginProjectSelectionAsync(true, token); break;
            case "assistants.project.choose": await ChooseOriginalProjectAsync(parameter, token); break;
            case "assistants.project.retry": await RetryOriginalProjectTaskAsync(token); break;
            case "assistants.project.pending.read": await ReadCompatiblePendingAsync(token); break;
            case "assistants.project.pending.resume": await BeginCompatibleResumeAsync(parameter, token); break;
            case "assistants.project.refresh":
                if (_projectTarget is { } target && IsProjectTargetCurrent(target)) await ReadProjectCatalogueAsync(target, token);
                break;
            case "assistants.project.search":
                if (_projectTarget is { } searchTarget && IsProjectTargetCurrent(searchTarget)) await ReadProjectCatalogueAsync(searchTarget, token);
                break;
            case "assistants.project.older":
                if (_projectTarget is { } olderTarget && IsProjectTargetCurrent(olderTarget)) await ReadProjectCatalogueAsync(olderTarget, token, older: true);
                break;
            case "assistants.project.cancel": CancelOriginalProjectSelection(); break;
            case "assistants.computer.open":
                if (await PrepareNavigationAsync(token)) await OpenOriginalMiniComputerAsync(token);
                break;
            case "assistants.memory.open":
                if (await PrepareNavigationAsync(token)) await OpenOriginalMemoryAsync(token);
                break;
            case "assistants.legacy.open":
                if (await PrepareNavigationAsync(token))
                { PublishSynchronous(Bindings.ShowLegacyMigration); await LoadMigrationListAsync(null, token); }
                break;
            case "assistants.home": if (await PrepareNavigationAsync(token)) PublishSynchronous(Bindings.ShowHome); break;
            case "assistants.create.start": if (await PrepareNavigationAsync(token)) PublishSynchronous(() => Bindings.EditConfiguration(null)); break;
            case "assistants.configuration.open":
            case "assistants.resources.open":
                if (await PrepareNavigationAsync(token))
                {
                    PublishSynchronous(() => Bindings.EditConfiguration(_controller.Snapshot.SelectedAssistant));
                    await ReadConfigurationModelsAsync(token);
                }
                break;
            case "assistants.configuration.back": PublishSynchronous(Bindings.ShowWork); break;
            case "assistants.configuration.discard": PublishSynchronous(Bindings.DiscardConfiguration); break;
            case "assistants.configuration.models.read": await ReadConfigurationModelsAsync(token); break;
            case "assistants.configuration.model.choose": await ChooseConfigurationModelAsync(parameter, token); break;
            case "assistants.configuration.effort.choose" when parameter is AssistantsCuiBindings.EffortRow effort:
                PublishSynchronous(() => Bindings.ChooseConfiguredEffort(effort.Original)); break;
            case "assistants.avatar.choose": PublishSynchronous(Bindings.ShowAvatarChooser); break;
            case "assistants.avatar.select": PublishSynchronous(() => Bindings.ChooseOriginalAvatar(parameter)); break;
            case "assistants.avatar.cancel": PublishSynchronous(Bindings.CancelAvatarChooser); break;
            case "assistants.configuration.save": await SaveConfigurationAsync(token); break;
            case "assistants.refresh": await SourceAsync(() => _controller.InitializeAsync(token)); break;
            case "assistants.open" when parameter is AssistantsCuiBindings.AssistantRow assistant:
                if (!await PrepareNavigationAsync(token)) return;
                await SourceAsync(() => _controller.OpenAssistantAsync(assistant.Identity, token));
                PublishSynchronous(Bindings.ShowWork); break;
            case "assistants.conversation.open" when parameter is AssistantsCuiBindings.ConversationRow conversation:
                if (!await PrepareNavigationAsync(token)) return;
                await SourceAsync(() => _controller.OpenConversationAsync(conversation.Id, token));
                PublishSynchronous(Bindings.ShowWork);
                await RefreshOriginalConversationModelsAsync(token); break;
            case "assistants.conversation.new":
            case "assistants.task.new":
                if (!await PrepareNavigationAsync(token)) return;
                await SourceAsync(() => _controller.NewConversationAsync(Guid.NewGuid(), command == "assistants.task.new" ? "New task" : "New conversation",
                    Guid.NewGuid(), token, command == "assistants.task.new" ? AssistantConversationKind.Task : AssistantConversationKind.Chat));
                // The SAME accepted producer has returned its current conversation.
                // Immediate navigation must use that binding without a UI pump.
                PublishSynchronous(() =>
                {
                    var current = _controller.Snapshot;
                    if (current.Revision >= _appliedSnapshot) ApplySnapshot(current);
                });
                await RefreshOriginalConversationModelsAsync(token);
                break;
            case "assistants.work.refresh": await SourceAsync(() => _controller.RefreshWorkAsync(token)); break;
            case "assistants.model.refresh": await RefreshOriginalConversationModelsAsync(token); break;
            case "assistants.model.choose" when parameter is AssistantsCuiBindings.ModelRow model:
                PublishSynchronous(() => Bindings.SelectModel(model.Original)); break;
            case "assistants.conversation.find.open": PublishSynchronous(Bindings.OpenOriginalConversationSearch); break;
            case "assistants.conversation.find": await FindOriginalConversationAsync(token); break;
            case "assistants.conversation.find.clear": PublishSynchronous(Bindings.ClearOriginalConversationSearch); break;
            case "assistants.conversation.find.show": ShowOriginalConversationSearchResult(parameter); break;
            case "assistants.branch.create": await ChangeOriginalConversationBranchAsync(parameter, true, token); break;
            case "assistants.branch.switch": await ChangeOriginalConversationBranchAsync(parameter, false, token); break;
            case "assistants.attachment.pick": await PickOriginalAttachmentAsync(token); break;
            case "assistants.attachment.detach": await DetachOriginalAttachmentAsync(parameter, token); break;
            case "assistants.draft.save": await SaveDraftAsync(token); break;
            case "assistants.send": await SendAsync(false, token); break;
            case "assistants.task.start": await SendAsync(true, token); break;
            case "assistants.task.steer":
            case "assistants.task.queue":
                await SubmitOriginalFollowUpAsync(command == "assistants.task.steer" ? TaskFollowUpMode.Steer : TaskFollowUpMode.Queue, token); break;
            case "assistants.pause":
            case "assistants.task.stop":
                await SourceAsync(() => _controller.ControlOriginalRunAsync(DemandTask(),
                    command == "assistants.pause" ? TaskRunOriginalRunControlKind.Pause : TaskRunOriginalRunControlKind.Stop, token)); break;
            case "assistants.task.recovery.review": await ReviewOriginalColdTaskRecoveryAsync(token); break;
            case "assistants.task.recovery.resume": await ResumeOriginalColdTaskAsync(token); break;
            case "assistants.resume":
                var resume = await SourceAsync(() => _controller.StartObservedOriginalResumeAsync(DemandTask(), token));
                ReleasePreparationBlock(); // The SAME accepted durable producer is now observed, not held by this view.
                try { await SourceAsync(resume.WaitAsync); }
                finally { await SourceAsync(() => _controller.CloseOriginalObservationAsync(resume)); }
                if (!IsRetiring) await SourceAsync(() => _controller.RefreshWorkAsync(token));
                break;
            case "assistants.disable":
            case "assistants.enable":
            case "assistants.archive":
            case "assistants.restore":
                await ChangeOriginalIdentityAvailabilityAsync(command, token); break;
            default: throw new InvalidOperationException("This Assistant action has no configured owner.");
        }
    }, handlesCommandRefusal: true));

    private AssistantDefinitionSnapshot DemandDefinition() => _controller.Snapshot.SelectedAssistant
        ?? throw new InvalidOperationException("Open an Assistant first.");
    private ProviderExecutionContext DemandTask() => _controller.Snapshot.Work?.Controls?.CanonicalTaskContext
        ?? throw new InvalidOperationException("This conversation has no current canonical Task control.");

    private async Task<bool> PrepareNavigationAsync(CancellationToken token)
    {
        if (Bindings.OriginalDraft?.IsDirty == true)
        { PublishSynchronous(() => Bindings.SetConversationStatus("Save or discard your configuration changes before navigating.")); return false; }
        if (_binding is not null && (_savedPromptConversation != _binding.Conversation.Id || Bindings.OriginalPrompt != _savedPrompt))
            await SaveDraftAsync(token);
        if (HasUnsavedChanges)
        { PublishSynchronous(() => Bindings.SetConversationStatus("Newer draft changes remain unsaved.")); return false; }
        return true;
    }

    private async Task SaveConfigurationAsync(CancellationToken token)
    {
        var draft = Bindings.Draft ?? throw new InvalidOperationException("The configuration form is unavailable.");
        var submitted = draft.CaptureSubmission();
        var saved = submitted.Identity is { } identity
            ? await SourceAsync(() => _controller.ConfigureAsync(identity, submitted.ExpectedRevision, submitted.Configuration, Guid.NewGuid(), token))
            : await SourceAsync(() => _controller.CreateAsync(submitted.Configuration, Guid.NewGuid(), token));
        var definition = saved.Assistants.SingleOrDefault(candidate => candidate.Identity == (submitted.Identity ?? saved.SelectedAssistant?.Identity))
            ?? throw new InvalidOperationException("The actual saved Assistant acknowledgement is unavailable.");
        PublishSynchronous(() =>
        {
            // ConfigureAsync already reissued the SAME conversation against the
            // acknowledged definition revision. Publish that exact binding before
            // immediate memory navigation, rather than waiting on queued observers.
            var current = _controller.Snapshot;
            var publication = current.Revision >= saved.Revision ? current : saved;
            if (publication.Revision >= _appliedSnapshot) ApplySnapshot(publication);
            Bindings.ConfigurationSaved(submitted, definition);
        });
        if (_controller.Snapshot.ConversationBinding is not null)
            await SourceAsync(() => _controller.ListAvailableModelsAsync(token));
        await ReadConfigurationModelsAsync(token);
    }

    private async Task SaveDraftAsync(CancellationToken token)
    {
        var binding = _controller.Snapshot.ConversationBinding ?? throw new InvalidOperationException("Open a conversation first.");
        var submitted = Bindings.OriginalPrompt;
        await SourceAsync(() => _controller.SaveDraftAsync(AssistantDraftAttachmentProjection.CurrentBranch(_controller.Snapshot.Conversation), submitted,
            AssistantDraftAttachmentProjection.DemandIds(_controller.Snapshot.Conversation), token));
        if (ReferenceEquals(_binding, binding)) { _savedPromptConversation = binding.Conversation.Id; _savedPrompt = submitted; }
    }

    private async Task SendAsync(bool task, CancellationToken token)
    {
        var binding = _controller.Snapshot.ConversationBinding ?? throw new InvalidOperationException("Open a canonical conversation first.");
        var generation = PresentationGeneration;
        var prompt = Bindings.Prompt;
        var model = Bindings.SelectedModel ?? throw new InvalidOperationException("Choose a currently authorized model.");
        var attachmentIds = AssistantDraftAttachmentProjection.DemandIds(_controller.Snapshot.Conversation);
        if (attachmentIds.Count != 0)
        {
            await SourceAsync(() => _controller.SaveDraftAsync(AssistantDraftAttachmentProjection.CurrentBranch(_controller.Snapshot.Conversation),
                prompt, attachmentIds, token));
            if (IsRetiring || generation != PresentationGeneration || !ReferenceEquals(binding, _binding) || Bindings.Prompt != prompt)
                throw new InvalidOperationException("The conversation or prompt changed while saving attachment input.");
            _savedPromptConversation = binding.Conversation.Id; _savedPrompt = prompt;
        }
        var input = new AssistantTaskInput(prompt, model.Model, binding.Definition.Configuration.Model.Effort,
            attachmentIds, model.ProviderId);
        PublishSynchronous(() => Bindings.SetStreaming(true));
        AssistantOriginalSendObservation? taskObservation = null;
        AssistantOriginalConversationObservation? conversationObservation = null;
        try
        {
            if (task)
            {
                var original = taskObservation = await SourceAsync(() => _controller.StartOriginalTaskAsync(input, token));
                if (attachmentIds.Count == 0) await AcknowledgeAcceptedPromptAsync(prompt, token);
                if (!IsRetiring) await SourceAsync(() => _controller.RefreshWorkAsync(token)); // Publish actual controls while the SAME business task is still observed.
                ReleasePreparationBlock();
                await ObserveEventsAsync(original.ObserveOriginalEventsAsync, binding, generation);
                await SourceAsync(() => original.WaitAsync(_viewCancellation.Token), viewWithdrawal: true);
                if (attachmentIds.Count != 0 && IsPresentationCurrent(binding, generation)) await AcknowledgeAcceptedPromptAsync(prompt, token);
            }
            else
            {
                var original = conversationObservation = await SourceAsync(() => _controller.StartOriginalSendAsync(input, token));
                if (attachmentIds.Count == 0) await AcknowledgeAcceptedPromptAsync(prompt, token);
                ReleasePreparationBlock();
                await ObserveEventsAsync(original.ObserveOriginalEventsAsync, binding, generation);
                await SourceAsync(() => original.WaitAsync(_viewCancellation.Token), viewWithdrawal: true);
                if (attachmentIds.Count != 0 && IsPresentationCurrent(binding, generation)) await AcknowledgeAcceptedPromptAsync(prompt, token);
            }
        }
        catch (OperationCanceledException) when (_viewCancellation.IsCancellationRequested) { }
        finally
        {
            try
            {
                // Acquisition already accepted genuine work even when a later draft
                // receipt or UI stage fails. Always detach/join its SAME observer.
                if (taskObservation is { } actualTask)
                    await SourceAsync(() => _controller.CloseOriginalObservationAsync(actualTask));
                if (conversationObservation is { } actualConversation)
                    await SourceAsync(() => _controller.CloseOriginalObservationAsync(actualConversation));
            }
            finally { if (!IsRetiring) PublishSynchronous(() => Bindings.SetStreaming(false)); }
        }
        if (!IsRetiring) await SourceAsync(() => _controller.RefreshWorkAsync(token));
    }

    private async Task AcknowledgeAcceptedPromptAsync(string prompt, CancellationToken token)
    {
        if (IsRetiring) return;
        var cleared = false;
        PublishSynchronous(() => cleared = Bindings.ClearSubmittedPrompt(prompt));
        if (cleared) await SaveDraftAsync(token);
    }

    private async Task ObserveEventsAsync(Func<CancellationToken, IAsyncEnumerable<ChatStreamEvent>> events,
        AssistantConversationBinding binding, long generation)
    {
        IAsyncEnumerator<ChatStreamEvent>? enumerator = null;
        try
        {
            using (EnterPhysical()) enumerator = events(_viewCancellation.Token).GetAsyncEnumerator(_viewCancellation.Token);
            while (await SourceAsync(() => enumerator.MoveNextAsync().AsTask(), viewWithdrawal: true))
            {
                ChatStreamEvent observed;
                using (EnterPhysical()) observed = enumerator.Current;
                await SourceAsync(() => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!IsPresentationCurrent(binding, generation) || _conversationTarget is not { } target) return;
                    PublishSynchronous(() => { if (_conversation.Apply(target, observed)) Bindings.SetMessages(_conversation.Messages); });
                }).GetTask());
            }
        }
        finally { if (enumerator is not null) await SourceAsync(() => enumerator.DisposeAsync().AsTask()); }
    }

    private Task RunAsync(Func<Task> body, Action<Task>? publishOriginal = null, bool handlesCommandRefusal = false)
    {
        var start = new TaskCompletionSource(); Task actual;
        lock (_gate)
        {
            DemandCurrent();
            _originals.RemoveAll(original => original.IsCompletedSuccessfully);
            if (_originals.Count >= 128) throw new InvalidOperationException("Assistant native original custody requires external retirement.");
            var operation = new Operation { Parent = _executing.Value, HandlesCommandRefusal = handlesCommandRefusal };
            Interlocked.Increment(ref _preparationBlocks);
            actual = RunCoreAsync(start.Task, operation, body); _originals.Add(actual);
            publishOriginal?.Invoke(actual);
        }
        start.TrySetResult(); return actual;
    }

    private async Task RunCoreAsync(Task start, Operation operation, Func<Task> body)
    {
        await start;
        var previous = _executing.Value; Volatile.Write(ref operation.Active, true);
        _executing.Value = operation;
        try { await body(); }
        catch (Exception refusal) when (operation.HandlesCommandRefusal && operation.RefusedCauses.Contains(refusal))
        {
            // The SAME controller Task acknowledged this pre-effect refusal. This
            // settles the presentation callback, never reports the command succeeded.
            if (operation.MigrationPublicationGeneration is { } migrationGeneration)
            {
                PublishSynchronous(() =>
                { if (IsMigrationPublicationCurrent(migrationGeneration)) MigrationBindings.SetError(refusal.Message); });
            }
            else if (!IsRetiring) PublishSynchronous(() =>
            {
                if (IsRetiring) return;
                // The SAME acknowledged command has settled its final saving/loading
                // state. Publish that current revision before returning to editable
                // controls; its queued StateChanged delivery may still be pending.
                var current = _controller.Snapshot;
                if (current.Revision >= _appliedSnapshot) ApplySnapshot(current);
                Bindings.SetConversationStatus(current.Error ?? "The action was declined. Review its settings and try again.");
                if (Bindings.IsProjectSelectionVisible) Bindings.SetProjectStatus(refusal.Message);
            });
        }
        finally { ReleasePreparationBlock(); Volatile.Write(ref operation.Active, false); _executing.Value = previous; }
    }

    private void ReleasePreparationBlock()
    {
        if (_executing.Value is { } actual && Interlocked.Exchange(ref actual.PreparationBlocked, 0) == 1)
            Interlocked.Decrement(ref _preparationBlocks);
    }

    private async Task<T> SourceAsync<T>(Func<Task<T>> acquire, bool viewWithdrawal = false)
    {
        ReserveSource();
        Task<T> actual;
        using (EnterPhysical()) actual = acquire() ?? throw new InvalidOperationException("The native source returned no Task.");
        lock (_gate) _originals.Add(actual);
        try { return await actual; }
        catch (Exception observed) { ThrowSource(actual, observed, viewWithdrawal); throw; }
    }
    private async Task SourceAsync(Func<Task> acquire, bool viewWithdrawal = false)
    {
        ReserveSource();
        Task actual;
        using (EnterPhysical()) actual = acquire() ?? throw new InvalidOperationException("The native source returned no Task.");
        lock (_gate) _originals.Add(actual);
        try { await actual; }
        catch (Exception observed) { ThrowSource(actual, observed, viewWithdrawal); throw; }
    }
    private void ReserveSource()
    {
        lock (_gate)
        {
            _originals.RemoveAll(actual => actual.IsCompletedSuccessfully || _acknowledgedRefusals.TryGetValue(actual, out _));
            if (_originals.Count >= 256)
            {
                var refusal = new InvalidOperationException("The native Assistant retains unresolved sources; inspect before new work.");
                Add(refusal); throw refusal;
            }
        }
    }
    private void ThrowSource(Task actual, Exception observed, bool viewWithdrawal)
    {
        bool acknowledged;
        using (EnterPhysical()) acknowledged = _controller.IsAcknowledgedOriginalCommandRefusal(actual) ||
            _migration?.IsAcknowledgedOriginalCommandRefusal(actual) == true;
        if (acknowledged && _executing.Value is { HandlesCommandRefusal: true } original)
        {
            _acknowledgedRefusals.GetValue(actual, static _ => new AcknowledgedRefusal());
            _projectSubmission?.ObserveAcknowledgedOriginalPreEffectRefusal(actual);
            original.RefusedCauses.Add(observed);
            ExceptionDispatchInfo.Capture(observed).Throw();
        }
        if (viewWithdrawal && actual.IsCanceled && _viewCancellation.IsCancellationRequested)
        { lock (_gate) _viewWithdrawals.Add(actual); ExceptionDispatchInfo.Capture(observed).Throw(); }
        var causes = actual.Exception?.InnerExceptions.ToArray() ?? [observed];
        foreach (var cause in causes) Add(cause);
        if (causes.Length == 1 && causes[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(causes[0]).Throw();
        var wrapper = new AggregateException("The actual Assistant native source failed.", causes);
        lock (_gate) _ownedGroups[wrapper] = causes;
        throw wrapper;
    }

    private async Task CloseCoreAsync(Task start)
    {
        await start; await _construction.Task;
        Task[] originals; lock (_gate) originals = _originals.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
        foreach (var original in originals) await JoinAsync(original);
        if (HasUnsavedChanges) Add(new InvalidOperationException("The original Assistant draft remains unsaved; its native buffer is retained."));
        Task? miniClose = null;
        try { using (EnterPhysical()) miniClose = _miniComputerManagement?.CloseAndDrainAsync(); }
        catch (Exception failure) { Add(failure); }
        if (miniClose is not null) await JoinAsync(miniClose);
        Task? memoryClose = null;
        try { using (EnterPhysical()) memoryClose = _memoryManagement?.CloseAndDrainAsync(); }
        catch (Exception failure) { Add(failure); }
        if (memoryClose is not null) await JoinAsync(memoryClose);
        Task? migrationClose = null;
        try { using (EnterPhysical()) migrationClose = _migration?.CloseAndDrainAsync(); }
        catch (Exception failure) { Add(failure); }
        if (migrationClose is not null) await JoinAsync(migrationClose);
        ThrowFailures(); // Its SAME borrowed bridge cannot retire before the migration originals settle.
        Task? generatedClose = null;
        try { using (EnterPhysical()) generatedClose = AcquireOriginalGeneratedUiClose();
            if (generatedClose is not null) lock (_gate) _originals.Add(generatedClose); }
        catch (Exception failure) { Add(failure); }
        if (generatedClose is not null) await JoinAsync(generatedClose);
        ThrowFailures(); // SAME generated renderer/event originals before borrowed Core/Den retirement.
        Task? controllerClose = null;
        try { using (EnterPhysical()) controllerClose = _controller.CloseAndDrainAsync(); }
        catch (Exception failure) { Add(failure); }
        if (controllerClose is not null) await JoinAsync(controllerClose);
        ThrowFailures(); // Actual failed/unknown controller or source keeps its native diagnostic tree.
        // Original child hosts settle before their containing Work/Shell trees are released.
        // A failed child keeps its actual containing native tree and custody here.
        for (var index = 0; index < _scenes.Count; index++)
        {
            if (index >= 3) ThrowFailures();
            var scene = _scenes[index];
            Task? close = null;
            try { using (EnterPhysical()) close = scene.CloseOriginalAsync(); }
            catch (Exception failure) { Add(failure); }
            if (close is not null) await JoinAsync(close);
        }
        ThrowFailures();
        var detach = Dispatcher.UIThread.InvokeAsync(() =>
        {
            using var physical = EnterPhysical();
            _controller.StateChanged -= OnStateChanged;
            Content = null;
            _binding = null; _conversation.Clear(); _projectTarget = null; _originalProjectChoice = null; _projectSubmission = null; _projectSubmissionTarget = null;
            _hostRetirement.Dispose(); _viewCancellation.Dispose();
        }).GetTask();
        await JoinAsync(detach); ThrowFailures();
    }

    private async Task JoinAsync(Task actual)
    {
        try { await actual; }
        catch (Exception observed)
        {
            if (_acknowledgedRefusals.TryGetValue(actual, out _)) return;
            lock (_gate) if (_viewWithdrawals.Contains(actual)) return;
            foreach (var cause in actual.Exception?.InnerExceptions.ToArray() ?? [observed]) Add(cause);
        }
    }
    private void Add(Exception cause)
    {
        lock (_gate)
        {
            if (_ownedGroups.TryGetValue(cause, out var children)) { foreach (var child in children) Add(child); return; }
            if (!_failures.Any(existing => ReferenceEquals(existing, cause))) _failures.Add(cause);
        }
    }
    private void ThrowFailures()
    {
        Exception[] failures; lock (_gate) failures = _failures.ToArray();
        if (failures.Length == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Length != 0) throw new AggregateException("Assistant native presentation did not drain cleanly.", failures);
    }

    private IDisposable EnterPhysical()
    {
        (_physicalSources ??= []).Add(this);
        return new PhysicalScope();
    }
    private sealed class PhysicalScope : IDisposable
    { public void Dispose() => _physicalSources!.RemoveAt(_physicalSources.Count - 1); }
}
