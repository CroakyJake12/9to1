using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;

/// <summary>Actual fresh manual Home READ over SAME current Files/private native sources.
/// This owns new descriptor and short held-currentness custody, never an old setup product,
/// Task actor, restored attempt, model/provider permission or Dev execution grant.</summary>
public sealed partial class HomeColdProjectReadReconciliation : ITaskRunColdProjectResourceSource,
    IDeveloperOriginalCurrentProjectReadAdmissionSource, IOriginalScopedCanonicalResourceAccessResolver, IAsyncDisposable
{
    public const string ReadAction = "dev.project.current.read";
    public string ResourceKind => "dev.project.current";
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly ResourceAuthorizationService _resources;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomePermissionTrustService _permissions;
    private readonly ITaskRunColdRecoveryJournal _journal;
    private readonly IAuthenticatedResourceActorSource _taskActors;
    private readonly Func<IDeveloperOriginalCurrentProjectSelectionSource> _selections;
    private readonly Func<IDeveloperOriginalCurrentProjectNativeSource> _native;
    private readonly bool _taskActorPair;
    private readonly object _gate = new(); private readonly List<Work> _work = [];
    private readonly ConditionalWeakTable<Work, object> _issued = new();
    private readonly List<Task> _operations = [];
    private IDeveloperOriginalCurrentProjectSelectionSource? _actualSelections;
    private IDeveloperOriginalCurrentProjectSelectionRetirementSource? _selectionRetirement;
    private IDeveloperOriginalCurrentProjectNativeSource? _actualNative;
    private bool _retiring; private Task? _request, _close;
    public HomeColdProjectReadReconciliation(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        ResourceAuthorizationService resources, HomeResourceOperationBroker broker, HomePermissionTrustService permissions,
        ITaskRunColdRecoveryJournal journal, IAuthenticatedResourceActorSource sameTaskActors,
        Func<IDeveloperOriginalCurrentProjectSelectionSource> selections,
        Func<IDeveloperOriginalCurrentProjectNativeSource> native)
    {
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store) || !resources.IsBoundToActorSource(profiles) ||
            !broker.IsBoundToOriginalComposition(resources, permissions))
            throw new InvalidOperationException("SAME actual Home store/profile/resource/broker/permission composition required.");
        _store = store; _profiles = profiles; _resources = resources; _broker = broker; _permissions = permissions;
        _journal = journal; _taskActors = sameTaskActors; _selections = selections; _native = native;
        _taskActorPair = journal is ITaskRunColdAuthoritySourceScope source && source.HasOriginalTaskActorSource(sameTaskActors);
        if (!_taskActorPair) throw new InvalidOperationException("SAME genuine configured journal/Task actor producer required.");
    }
    public bool HasOriginalColdProjectComposition(ITaskRunColdRecoveryJournal sameJournal, IAuthenticatedResourceActorSource sameTaskActors)
        => _taskActorPair && ReferenceEquals(_journal, sameJournal) && ReferenceEquals(_taskActors, sameTaskActors);
    public Task? OriginalRetirementRequestTask { get { lock (_gate) return _request; } }
    private void DemandLive() { lock (_gate) if (_retiring) throw new ObjectDisposedException(nameof(HomeColdProjectReadReconciliation)); }
    private void BindSources(Context sources)
    {
        var selections = sources.Invoke(_selections); var native = sources.Invoke(_native);
        if (selections is not IDeveloperOriginalCurrentProjectSelectionRetirementSource retirement)
            throw new InvalidOperationException("The SAME current selection owner must expose its whole original retirement.");
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(HomeColdProjectReadReconciliation));
            if (_actualSelections is not null && (!ReferenceEquals(_actualSelections, selections) || !ReferenceEquals(_actualNative, native)))
                throw new InvalidOperationException("The actual current project owner composition changed.");
            _actualSelections = selections; _selectionRetirement = retirement; _actualNative = native;
        }
    }
    public Task<ITaskRunColdOriginalProjectInput> PrepareOriginalProjectInputWithinSourceAsync(
        Conversation conversation, ContainerDefinition container, string exactReference, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var original = Reserve(null); return original.StartInput(conversation, container, exactReference, scope, retain, token);
    }
    public Task<ITaskRunColdProjectRestorationLease> PrepareOriginalProjectRestorationWithinSourceAsync(
        ITaskRunColdOriginalProjectMaterial material, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var original = Reserve(material); return original.StartRestoration(scope, retain, token);
    }
    private Work Reserve(ITaskRunColdOriginalProjectMaterial? material)
    {
        DemandExternalOriginalJoin(); return ReserveAdmitted(material);
    }
    private Work ReserveAdmitted(ITaskRunColdOriginalProjectMaterial? material)
    {
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(HomeColdProjectReadReconciliation));
            _work.RemoveAll(actual => actual.CanPruneSuccessfulClose);
            if (_work.Count >= 128) throw new InvalidOperationException("Unresolved current project READ custody is full.");
            var original = new Work(this, material); _work.Add(original); _issued.Add(original, new object()); return original;
        }
    }
    private Work RequireInput(ITaskRunColdOriginalProjectInput input, bool live)
    {
        lock (_gate)
            return input is Work actual && actual.Material is null && actual._command is null && _issued.TryGetValue(actual, out _) && (!live || !_retiring && actual.IsLive)
                ? actual : throw new UnauthorizedAccessException("SAME retained private project input required.");
    }
    private Work RequireLease(ITaskRunColdProjectRestorationLease lease, ITaskRunColdOriginalProjectMaterial material, bool live)
    {
        lock (_gate)
            return lease is Work actual && actual._command is null && ReferenceEquals(actual.Material, material) && _issued.TryGetValue(actual, out _) && (!live || !_retiring && actual.IsLive)
                ? actual : throw new UnauthorizedAccessException("SAME retained private project material/lease required.");
    }
    public bool IsOwnedOriginalProjectInput(ITaskRunColdOriginalProjectInput input)
    { lock (_gate) return input is Work actual && actual.Material is null && actual._command is null && _issued.TryGetValue(actual, out _) && actual.Driver?.IsCompletedSuccessfully == true; }
    public bool IsIssuedOriginalProjectInput(ITaskRunColdOriginalProjectInput input)
        { lock (_gate) return !_retiring && input is Work actual && actual.Material is null && actual._command is null && _issued.TryGetValue(actual, out _) && actual.Driver?.IsCompletedSuccessfully == true && actual.IsLive; }
    public bool IsOwnedOriginalProjectRestoration(ITaskRunColdProjectRestorationLease lease, ITaskRunColdOriginalProjectMaterial material)
    { lock (_gate) return lease is Work actual && actual._command is null && ReferenceEquals(actual.Material, material) && _issued.TryGetValue(actual, out _) && actual.Driver?.IsCompletedSuccessfully == true; }
    public bool IsIssuedOriginalProjectRestoration(ITaskRunColdProjectRestorationLease lease, ITaskRunColdOriginalProjectMaterial material)
        { lock (_gate) return !_retiring && lease is Work actual && actual._command is null && ReferenceEquals(actual.Material, material) && _issued.TryGetValue(actual, out _) && actual.Driver?.IsCompletedSuccessfully == true && actual.IsLive; }
    public Task ValidateOriginalProjectInputWithinSourceAsync(ITaskRunColdOriginalProjectInput input, TaskRunColdChatInput exact,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
        => RequireInput(input, true).StartValidation(scope, retain, async sources =>
        {
            var original = RequireInput(input, true); original.DemandExactInput(exact);
            await original.ValidateFreshShortReadAsync(sources, token).ConfigureAwait(false); original.DemandExactInput(exact);
        });
    public Task<TaskRunColdProjectIdentity> CaptureOriginalClosedProjectIdentityWithinSourceAsync(
        ITaskRunColdOriginalProjectInput input, TaskExecutionSnapshot terminal, TaskRunColdChatInput exact,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
        => StartOperation(scope, retain, async sources =>
        {
            var original = RequireInput(input, false);
            if (terminal.TaskId == Guid.Empty || terminal.ExecutionId == Guid.Empty || terminal.OwnerBinding is null)
                throw new UnauthorizedAccessException("Actual owned settled Task identity required; fields alone grant nothing.");
            if (original.IsLive)
            {
                original.DemandExactInput(exact);
                var validation = sources.Invoke(() => original.StartValidation(sources.Scope, sources.Retain,
                    current => original.ValidateFreshShortReadAsync(current, token)));
                await sources.Await(validation).ConfigureAwait(false);
                Task? close = null; var closes = new List<Task>();
                sources.AcquireClose(() => { close = original.CloseOwned(); return close; }, closes);
                if (close is null) throw new InvalidOperationException("The actual original input close was not acquired.");
                await sources.Await(close).ConfigureAwait(false);
            }
            else
            {
                // Historical identity is recognized only through this private issuer's
                // healthy SAME original close. The old input/leases are never reopened.
                original.DemandExactHealthyClosedInput(exact);
                await ValidateClosedInputThroughFreshOriginalReadAsync(original, sources, token).ConfigureAwait(false);
            }
            original.DemandExactHealthyClosedInput(exact);
            return original.Identity;
        });
    public Task ValidateOriginalProjectRestorationWithinSourceAsync(ITaskRunColdProjectRestorationLease lease,
        ITaskRunColdOriginalProjectMaterial material, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => RequireLease(lease, material, true).StartValidation(scope, retain,
            sources => RequireLease(lease, material, true).ValidateHeldAsync(sources, token));
    public Task ValidateOriginalClosedProjectRestorationWithinSourceAsync(ITaskRunColdProjectRestorationLease lease,
        ITaskRunColdOriginalProjectMaterial material, ITaskRunColdJournalAcknowledgment acknowledgment,
        TaskExecutionSnapshot current, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => StartOperation(scope, retain, async sources =>
        {
            var original = RequireLease(lease, material, false);
            var observed = ObserveClosedProjectAcknowledgment(sources, acknowledgment);
            var originalClaim = sources.Invoke(() => material.OriginalClaim);
            if (!original.IsSuccessfullyClosed || !ReferenceEquals(observed.Claim, originalClaim) || current != observed.Task)
                throw new UnauthorizedAccessException("Exact private ACK and successful actual Home/native close are required.");
            var claims = _journal as ITaskRunColdOriginalProjectClaimSource
                ?? throw new InvalidOperationException("SAME project journal provenance source required.");
            await sources.Await(sources.Invoke(() => claims.ValidateOriginalClosedProjectMaterialWithinSourceAsync(
                material, acknowledgment, sources.Scope, sources.Retain, token))).ConfigureAwait(false);
            // A NEW READ operation/current descriptor. The old lease is never reopened.
            var fresh = ReserveAdmitted(null); var preparation = fresh.StartInput(original.Conversation, original.Container,
                original.Identity.OriginalProjectContextJson, sources.Scope, sources.Retain, token,
                original.Identity.SavedWorkspaceDocumentSha256, original.Identity.RegisteredRootFingerprint);
            Exception? primary = null;
            try { await sources.Await(preparation).ConfigureAwait(false); DemandSameResourceIdentity(original.Identity, fresh.Identity); }
            catch (Exception cause) { primary = cause; }
            Task close = fresh.CloseOwned();
            try { await sources.Await(close).ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Fresh closed project revalidation and actual cleanup failed.", primary is null ? [cleanup] : [primary, cleanup]); }
            if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        });
    private static (ITaskRunColdJournalClaim Claim, TaskExecutionSnapshot Task) ObserveClosedProjectAcknowledgment(
        Context sources, ITaskRunColdJournalAcknowledgment acknowledgment)
        => sources.Invoke(() => (acknowledgment.OriginalClaim, acknowledgment.AcknowledgedTask));
    public bool IsIssuedOriginalCurrentProjectRead(IDeveloperOriginalCurrentProjectSelection selection, IDeveloperProjectOriginalReadAdmission read)
    { lock (_gate) return !_retiring && read is Work actual && _issued.TryGetValue(actual, out _) && ReferenceEquals(actual.Selection, selection) && actual.ReadReady; }
    public Task ValidateOriginalCurrentProjectReadWithinSourceAsync(IDeveloperOriginalCurrentProjectSelection selection,
        IDeveloperProjectOriginalReadAdmission read, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        Work actual; lock (_gate) actual = read is Work original && _issued.TryGetValue(original, out _) && ReferenceEquals(original.Selection, selection)
            ? original : throw new UnauthorizedAccessException("SAME actual Home current-project READ issuer required.");
        return actual.StartValidation(scope, retain, sources => actual.ValidateReadAsync(sources, token), nativeValidation: true);
    }
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope resource, CancellationToken token)
        => new(EvaluateWithinOriginalSourceAsync(actor, action, resource, body => body(), _ => { }, token));
    public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string action, ResourceScope resource,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        Work? actual; lock (_gate) actual = !_retiring && action == ReadAction
            ? _work.SingleOrDefault(value => value.HasScope(actor, resource)) : null;
        if (actual is null) return Task.FromResult(new ResourceAccessDecision(false, "HOME_CURRENT_PROJECT_PRIVATE_SCOPE_REQUIRED", actor.ActorId, resource.Revision, actor.OrganisationId));
        return actual.StartEvaluation(scope, retain, async sources =>
        {
            await actual.ValidateSelectionAsync(sources, token).ConfigureAwait(false);
            var current = await actual.ActorAsync(sources, token).ConfigureAwait(false); actual.DemandLive();
            return new(current == actor && actual.HasScope(actor, resource), "HOME_CURRENT_PROJECT_READ", actor.ActorId, resource.Revision, actor.OrganisationId);
        });
    }
    public void DemandExternalOriginalJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Work[] originals; IDeveloperOriginalCurrentProjectSelectionSource? selections; IDeveloperOriginalCurrentProjectNativeSource? native;
        lock (_gate) { originals = _work.ToArray(); selections = _actualSelections; native = _actualNative; }
        foreach (var original in originals) original.DemandExternalOriginalJoin();
        selections?.DemandExternalOriginalCurrentProjectJoin(); native?.DemandExternalOriginalJoin();
    }
    public void RequestOriginalRetirement()
    {
        TaskCompletionSource begin; Work[] originals;
        lock (_gate)
        {
            _retiring = true; originals = _work.ToArray(); foreach (var original in originals) original.RequestOriginalRetirement();
            if (_request is not null) return; begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _request = RequestPublishedAsync(begin.Task, originals);
        }
        begin.SetResult();
    }
    private async Task RequestPublishedAsync(Task begin, Work[] originals)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var sources = new Context(this, null, body => body(), _ => { }, false);
        try { sources.Invoke(() => { _selectionRetirement?.RequestOriginalCurrentProjectRetirement(); return true; }); }
        catch (Exception cause) { sources.Errors.Retain(cause); }
        try { sources.Invoke(() => { _actualNative?.RequestOriginalRetirement(); return true; }); }
        catch (Exception cause) { sources.Errors.Retain(cause); }
        var cancellations = new List<Task>();
        foreach (var original in originals)
            try
            {
                sources.Invoke(() =>
                {
                    var actual = original.CancelOriginalPendingAsync();
                    cancellations.Add(actual); sources.Retain(actual); return true;
                });
            }
            catch (Exception cause) { sources.Errors.Retain(cause); }
        foreach (var actual in cancellations)
            try { await sources.Await(actual).ConfigureAwait(false); }
            catch (Exception cause) { sources.Errors.Retain(cause); }
        await sources.Settle().ConfigureAwait(false);
    }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); RequestOriginalRetirement(); TaskCompletionSource begin; Task actual;
        lock (_gate)
        {
            if (_close is not null) return _close; begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = ClosePublishedAsync(begin.Task); _close = actual;
        }
        begin.SetResult(); return actual;
    }
    private async Task ClosePublishedAsync(Task begin)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var sources = new Context(this, null, body => body(), _ => { }, false); var closes = new List<Task>();
        Work[] originals; Task[] operations; lock (_gate) { originals = _work.ToArray(); operations = _operations.ToArray(); }
        foreach (var original in originals) sources.AcquireClose(original.CloseOwned, closes);
        if (_request is { } request) closes.Add(request);
        foreach (var actual in operations) closes.Add(actual);
        foreach (var close in closes) try { await sources.Await(close).ConfigureAwait(false); } catch (Exception cause) { sources.Errors.Retain(cause); }
        closes.Clear();
        if (_actualNative is { } native) sources.AcquireClose(native.CloseAndDrainOriginalAsync, closes);
        if (_selectionRetirement is { } selections) sources.AcquireClose(selections.CloseAndDrainOriginalCurrentProjectsAsync, closes);
        foreach (var close in closes) try { await sources.Await(close).ConfigureAwait(false); } catch (Exception cause) { sources.Errors.Retain(cause); }
        await sources.Settle().ConfigureAwait(false);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    private Task<T> StartOperation<T>(Action<Action> scope, Action<Task> retain, Func<Context, Task<T>> body)
    {
        DemandExternalOriginalJoin(); DemandLive(); var sources = new Context(this, null, scope, retain, true);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<T> actual; lock (_gate) { DemandLive(); _operations.RemoveAll(task => task.IsCompletedSuccessfully); if (_operations.Count >= 512) throw new InvalidOperationException("Original project observation custody is full.");
            actual = RunOperationAsync(begin.Task, sources, body); _operations.Add(actual); }
        try { sources.Publish(actual); } catch (Exception cause) { sources.Errors.Retain(cause); }
        finally { begin.SetResult(); } return actual;
    }
    private Task StartOperation(Action<Action> scope, Action<Task> retain, Func<Context, Task> body)
        => StartOperation(scope, retain, async sources => { await body(sources).ConfigureAwait(false); return true; });
    private async Task<T> RunOperationAsync<T>(Task begin, Context sources, Func<Context, Task<T>> body)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        T result = default!; Exception? primary = null;
        try { result = await body(sources).ConfigureAwait(false); } catch (Exception cause) { primary = cause; sources.Errors.Retain(cause); }
        await sources.Settle(primary).ConfigureAwait(false); DemandLive(); return result;
    }
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))).ToLowerInvariant();
    private static void DemandSameResourceIdentity(TaskRunColdProjectIdentity before, TaskRunColdProjectIdentity current)
    {
        if (before with { OriginalHomeResourceActor = current.OriginalHomeResourceActor } != current ||
            before.OriginalHomeResourceActor.ActorId != current.OriginalHomeResourceActor.ActorId ||
            before.OriginalHomeResourceActor.ProfileId != current.OriginalHomeResourceActor.ProfileId ||
            before.OriginalHomeResourceActor.AccountId != current.OriginalHomeResourceActor.AccountId ||
            before.OriginalHomeResourceActor.OrganisationId != current.OriginalHomeResourceActor.OrganisationId)
            throw new UnauthorizedAccessException("The authenticated current project/container/document/root identity changed.");
    }
    private sealed class Context
    {
        private readonly HomeColdProjectReadReconciliation _owner; private readonly Work? _work;
        private readonly Action<Task> _retain; private readonly bool _productive;
        private readonly HomeOwnershipOriginalSourceCallbacks _protocol;
        internal readonly CloudflareOriginalTaskLedger Errors = new();
        internal Context(HomeColdProjectReadReconciliation owner, Work? work, Action<Action> caller, Action<Task> retain, bool productive)
        {
            _owner = owner; _work = work; _retain = retain; _productive = productive;
            _protocol = new(caller, raw => Retain(raw)); Errors.BindOriginalOwner(work ?? (object)owner); Errors.BindOriginalCallerCallback(Scope);
        }
        internal void Scope(Action body) => Physical(() => { _protocol.Run(body); return true; });
        internal T Physical<T>(Func<T> body) => CloudflareOriginalExecutionGuard.InvokeOriginal(_owner,
            () => _work is null ? body() : CloudflareOriginalExecutionGuard.InvokeOriginal(_work, body));
        internal T Invoke<T>(Func<T> body)
        {
            if (_productive) { _owner.DemandLive(); _work?.DemandLive(); }
            if (_productive && (Errors.OriginalErrors.Count != 0 || _protocol.Errors.Length != 0))
                throw new AggregateException("Prior original source callback failure refuses another productive factory.", Errors.OriginalErrors.Concat(_protocol.Errors));
            var result = Errors.Invoke(body);
            if (_productive) { _owner.DemandLive(); _work?.DemandLive(); }
            return result;
        }
        internal void Publish(Task driver)
        {
            // The encompassing driver is retained by its actual parent, never in its own
            // child ledger. Guard the entire external callback before releasing start.
            Scope(() => _retain(driver));
        }
        internal void Retain(Task raw)
        {
            ArgumentNullException.ThrowIfNull(raw); _ = Errors.Track(raw);
            Scope(() => _retain(raw));
        }
        internal Task<T> Await<T>(Task<T> raw) => Errors.AwaitAsync(raw);
        internal Task Await(Task raw) => Errors.AwaitAsync(raw);
        internal Task<T> Capture<T>(Func<Task<T>> factory, Action<T> retainProduct)
            => Errors.CaptureOriginalAcquisitionAsync(() => Invoke(factory), product => Physical(() => { retainProduct(product); return true; }));
        internal Task<T> Capture<T>(Func<ValueTask<T>> factory, Action<T> retainProduct)
            => Capture(() => factory().AsTask(), retainProduct);
        internal void AcquireClose(Func<Task> factory, List<Task> closes)
        {
            Task? actual = null;
            try { _ = Errors.Invoke(() => { actual = factory(); closes.Add(actual); _ = Errors.Track(actual); return actual; }); }
            catch (Exception cause) { Errors.Retain(cause); }
        }
        internal bool SuccessfullySettled => Errors.OriginalErrors.Count == 0 && _protocol.Errors.Length == 0 && Errors.OriginalTasks.All(raw => raw.IsCompletedSuccessfully);
        internal async Task Settle(Exception? primary = null)
        {
            await Errors.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in _protocol.Errors) Errors.Retain(cause);
            if (Errors.OriginalErrors.Count != 0) throw new AggregateException("Actual Home project source/callback/raw custody failed.", Errors.OriginalErrors);
        }
    }
    private sealed partial class Work(HomeColdProjectReadReconciliation owner, ITaskRunColdOriginalProjectMaterial? material)
        : ITaskRunColdOriginalProjectInput, ITaskRunColdProjectRestorationLease, IDeveloperProjectOriginalReadAdmission
    {
        internal ITaskRunColdOriginalProjectMaterial? Material => material;
        private HomeColdProjectReadReconciliation Owner => owner;
        private readonly object _gate = new(); private readonly AsyncLocal<Context?> _current = new();
        private readonly SemaphoreSlim _shortReadGate = new(1, 1);
        private sealed record DescriptorFacts(Guid WorkspaceId, Guid ProjectId, Guid RootId, long WorkspaceRevision,
            long ProjectRevision, string? RepositoryBindingId, string Root, string Reference,
            Conversation Conversation, ContainerDefinition Container, AuthenticatedResourceActor Actor);
        private DescriptorFacts _facts = null!; private string _documentSha = "", _rootFingerprint = "";
        private readonly List<Task> _validations = []; private readonly List<Context> _contexts = [];
        private readonly List<(IDeveloperOriginalCurrentProjectNativeRead Read, Task Capture, Task Close)> _nativeClosed = [];
        private readonly List<Task> _finiteCalls = [];
        private CancellationTokenSource? _stop; private bool _retired; private bool _readReady; private Task? _close, _nativeValidation;
        private readonly TaskCompletionSource<CancellationTokenSource?> _originalStopReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _originalStopCancellation;
        internal Task? Driver { get; private set; }
        internal IDeveloperOriginalCurrentProjectSelection Selection { get; private set; } = null!;
        private IDeveloperOriginalCurrentProjectDescriptor _descriptor = null!;
        private HomeResourcePreparedReview? _review; private JsonElement _arguments; private ResourceScope[] _scopes = [];
        private HomeResourceExecutionCapability? _capability; private HomeClaimedResourceAttestation? _attestation;
        private IDeveloperOriginalCurrentProjectNativeRead? _nativeRead; private Task? _nativeCapture;
        private IHomeLocalOperationLease? _home; private IAsyncDisposable? _completion;
        internal Conversation Conversation { get; private set; } = null!;
        internal ContainerDefinition Container { get; private set; } = null!;
        internal TaskRunColdProjectIdentity Identity { get; private set; } = null!;
        private AuthenticatedResourceActor _actor = null!;
        internal bool IsLive { get { lock (_gate) return !_retired && _close is null; } }
        internal bool ReadReady { get { lock (_gate) return IsLive && Driver?.IsFaulted != true && Driver?.IsCanceled != true && (_command is null || !_commandFailed) && _readReady && _capability?.IsUncompletedClaim(owner._broker) == true; } }
        internal bool IsSuccessfullyClosed { get { lock (_gate) return _close?.IsCompletedSuccessfully == true; } }
        internal bool CanPruneSuccessfulClose
        { get { lock (_gate) return _close?.IsCompletedSuccessfully == true && Driver?.IsCompletedSuccessfully == true &&
            _validations.All(raw => raw.IsCompletedSuccessfully) && _finiteCalls.All(raw => raw.IsCompletedSuccessfully) && _contexts.All(context => context.SuccessfullySettled); } }
        public Conversation OriginalConversation => Conversation;
        public ContainerDefinition OriginalContainer => Container;
        public TaskRunColdProjectIdentity OriginalIdentity => Identity;
        public ITaskRunColdOriginalProjectMaterial OriginalMaterial => material ?? throw new UnauthorizedAccessException("Initial input is not restoration material.");
        public AuthenticatedResourceActor CurrentHomeResourceActor => _actor;
        public Task OriginalPreparation => Driver ?? throw new InvalidOperationException("No actual preparation driver published.");
        internal void DemandLive() { owner.DemandLive(); lock (_gate) if (_retired || _close is not null) throw new ObjectDisposedException("Original current project READ"); }
        public void DemandExternalOriginalJoin()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            owner._actualSelections?.DemandExternalOriginalCurrentProjectJoin(); owner._actualNative?.DemandExternalOriginalJoin();
        }
        public void RequestOriginalRetirement() { lock (_gate) _retired = true; }
        internal Task CancelOriginalPendingAsync()
        {
            Task actual; TaskCompletionSource? begin = null;
            lock (_gate)
            {
                if (_originalStopCancellation is not null) return _originalStopCancellation;
                // A reserved Work with no admitted preparation cannot later create a
                // stop source after close. Accepted preparation publishes the actual
                // source inside its acquisition callback, even if its postguard fails.
                if (Driver is null) _originalStopReady.TrySetResult(null);
                var sources = new Context(owner, this, body => body(), _ => { }, false);
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual = CancelPublishedAsync(begin.Task, sources);
                _originalStopCancellation = actual; _finiteCalls.Add(actual); _contexts.Add(sources);
            }
            begin.SetResult(); return actual;
        }
        private async Task CancelPublishedAsync(Task begin, Context sources)
        {
            await begin.ConfigureAwait(false);
            using var parent = CloudflareOriginalExecutionGuard.EnterOriginal(owner);
            using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try
            {
                var actual = await sources.Await(_originalStopReady.Task).ConfigureAwait(false);
                sources.Physical(() => { actual?.Cancel(); return true; });
            }
            catch (Exception cause) { sources.Errors.Retain(cause); }
            await sources.Settle().ConfigureAwait(false);
        }
        internal Task<ITaskRunColdOriginalProjectInput> StartInput(Conversation conversation, ContainerDefinition container,
            string exactReference, Action<Action> scope, Action<Task> retain, CancellationToken token,
            string? expectedSha = null, string? expectedRoot = null)
        {
            Conversation = conversation; Container = container;
            return Start<ITaskRunColdOriginalProjectInput>(scope, retain, token, async sources =>
            {
                await PrepareReadAsync(sources, exactReference, expectedSha, expectedRoot, token).ConfigureAwait(false);
                await AcquireNativeAndHeldAsync(sources, token).ConfigureAwait(false);
                var identity = BuildIdentity(); DemandOriginalCommit();
                await ReleaseBorrowersAsync().ConfigureAwait(false); Identity = identity; DemandLive(); return this;
            });
        }
        internal Task<ITaskRunColdProjectRestorationLease> StartRestoration(Action<Action> scope, Action<Task> retain, CancellationToken token)
            => Start<ITaskRunColdProjectRestorationLease>(scope, retain, token, async sources =>
            {
                var actual = material ?? throw new UnauthorizedAccessException("SAME actual journal material required.");
                var claims = owner._journal as ITaskRunColdOriginalProjectClaimSource
                    ?? throw new InvalidOperationException("SAME configured private project claim source required.");
                if (!sources.Invoke(() => claims.IsIssuedOriginalProjectMaterial(actual, actual.OriginalClaim, actual.OriginalContext, actual.OriginalExpected)))
                    throw new UnauthorizedAccessException("Public project fields cannot issue restoration material.");
                await sources.Await(sources.Invoke(() => claims.ValidateOriginalProjectMaterialWithinSourceAsync(
                    actual, sources.Scope, sources.Retain, token))).ConfigureAwait(false);
                var captured = sources.Invoke(() => actual.OriginalProjectIdentity); var input = sources.Invoke(() => actual.OriginalInput);
                Conversation = input.Conversation; Container = await SelectContainerAsync(sources, captured, token).ConfigureAwait(false);
                await PrepareReadAsync(sources, captured.OriginalProjectContextJson,
                    captured.SavedWorkspaceDocumentSha256, captured.RegisteredRootFingerprint, token, selectedAlready: true).ConfigureAwait(false);
                await AcquireNativeAndHeldAsync(sources, token).ConfigureAwait(false);
                Identity = BuildIdentity(); DemandSameResourceIdentity(captured, Identity); DemandOriginalCommit(); DemandLive(); return this;
            });
        private async Task<ContainerDefinition> SelectContainerAsync(Context sources, TaskRunColdProjectIdentity captured, CancellationToken token)
        {
            // Actual Files selection resolves the current persisted container. The detached input
            // below is only an expectation; its exact current repository pairing is mandatory.
            var input = sources.Invoke(() => material!.OriginalInput);
            if (input.Conversation.ContainerId != captured.ContainerId || input.ProjectContext != captured.OriginalProjectContextJson ||
                input.WorkspaceRoot != captured.CanonicalRoot || input.ProjectInstructions != captured.OriginalContainerInstructions)
                throw new UnauthorizedAccessException("Authenticated original project input is inconsistent.");
            owner.BindSources(sources);
            if (owner._actualSelections is not IDeveloperOriginalCurrentProjectRestorationSelectionSource restoration)
                throw new InvalidOperationException("The SAME Files source lacks its current actual-container restoration producer.");
            var currentSelection = await sources.Await(sources.Invoke(() => restoration.SelectOriginalRestorationWithinSourceAsync(
                input.Conversation, captured.ContainerId, captured.OriginalContainerSha256, captured.OriginalProjectContextJson,
                captured.SavedWorkspaceDocumentSha256, captured.RegisteredRootFingerprint, sources.Scope, sources.Retain, token))).ConfigureAwait(false);
            var descriptor = sources.Invoke(() => owner._actualSelections!.GetOriginalDescriptor(currentSelection));
            var actualContainer = sources.Invoke(() =>
            {
                if (!owner._actualSelections!.IsIssuedOriginalDescriptor(currentSelection, descriptor) ||
                    Hash(descriptor.OriginalContainer) != captured.OriginalContainerSha256)
                    throw new UnauthorizedAccessException("The actual persisted original container changed.");
                return descriptor.OriginalContainer;
            });
            Selection = currentSelection; _descriptor = descriptor; return actualContainer;
        }
        private Task<T> Start<T>(Action<Action> scope, Action<Task> retain, CancellationToken token, Func<Context, Task<T>> body)
        {
            var sources = new Context(owner, this, scope, retain, true);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<T> actual;
            lock (_gate) { if (Driver is not null) throw new InvalidOperationException("One original project preparation only.");
                if (_retired || _close is not null) throw new ObjectDisposedException("Original current project READ");
                actual = RunPublishedAsync(begin.Task, sources, body, token, preparation: true); Driver = actual; _contexts.Add(sources); }
            try { sources.Publish(actual); } catch (Exception cause) { sources.Errors.Retain(cause); }
            finally { begin.SetResult(); } return actual;
        }
        internal Task StartValidation(Action<Action> scope, Action<Task> retain, Func<Context, Task> body, bool nativeValidation = false)
        {
            DemandLive(); var sources = new Context(owner, this, scope, retain, true);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<bool> actual;
            lock (_gate) { if (_retired || _close is not null) throw new ObjectDisposedException("Original current project READ"); if (_validations.Count >= 4096) throw new InvalidOperationException("Original project validation custody is full.");
                actual = RunPublishedAsync(begin.Task, sources, async current => { await body(current).ConfigureAwait(false); return true; }, CancellationToken.None, false);
                _validations.Add(actual); _contexts.Add(sources); if (nativeValidation) _nativeValidation = actual; }
            try { sources.Publish(actual); } catch (Exception cause) { sources.Errors.Retain(cause); }
            finally { begin.SetResult(); } return actual;
        }
        internal Task<ResourceAccessDecision> StartEvaluation(Action<Action> scope, Action<Task> retain, Func<Context, Task<ResourceAccessDecision>> body)
        {
            DemandLive(); var sources = new Context(owner, this, scope, retain, true); var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ResourceAccessDecision> actual; lock (_gate) { if (_retired || _close is not null) throw new ObjectDisposedException("Original current project READ");
                actual = RunPublishedAsync(begin.Task, sources, body, CancellationToken.None, false); _validations.Add(actual); _contexts.Add(sources); }
            try { sources.Publish(actual); } catch (Exception cause) { sources.Errors.Retain(cause); }
            finally { begin.SetResult(); } return actual;
        }
        private async Task<T> RunPublishedAsync<T>(Task begin, Context sources, Func<Context, Task<T>> body, CancellationToken token, bool preparation)
        {
            await begin.ConfigureAwait(false); using var ownerPhase = CloudflareOriginalExecutionGuard.EnterOriginal(owner);
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this); var prior = _current.Value; _current.Value = sources;
            T result = default!; Exception? primary = null;
            try
            {
                try
                {
                    DemandLive();
                    if (sources.Errors.OriginalErrors.Count != 0) throw new AggregateException(sources.Errors.OriginalErrors);
                    if (preparation) sources.Invoke(() =>
                    {
                        _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
                        _originalStopReady.TrySetResult(_stop); // Capture before a caller postguard can fail.
                        return true;
                    });
                    result = await body(sources).ConfigureAwait(false); DemandLive();
                }
                catch (Exception cause)
                {
                    primary = cause; sources.Errors.Retain(cause);
                    if (preparation) try { await ReleaseBorrowersAsync().ConfigureAwait(false); } catch (Exception cleanup) { sources.Errors.Retain(cleanup); }
                }
                await sources.Settle(primary).ConfigureAwait(false); return result;
            }
            finally
            {
                if (preparation) _originalStopReady.TrySetResult(null);
                _current.Value = prior;
            }
        }
        internal async Task<AuthenticatedResourceActor> ActorAsync(Context sources, CancellationToken token)
            => await sources.Await(sources.Invoke(() => owner._profiles.GetCurrentWithinOriginalSourceAsync(
                sources.Scope, sources.Retain, token))).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("Actual personal Home profile required.");
        private async Task PrepareReadAsync(Context sources, string reference, string? expectedSha, string? expectedRoot, CancellationToken token, bool selectedAlready = false)
        {
            var ct = _stop!.Token; owner.BindSources(sources);
            if (!selectedAlready)
            {
                Selection = await sources.Await(sources.Invoke(() => owner._actualSelections!.SelectOriginalWithinSourceAsync(
                    Conversation, Container, reference, expectedSha, expectedRoot, sources.Scope, sources.Retain, ct))).ConfigureAwait(false);
                _descriptor = sources.Invoke(() => owner._actualSelections!.GetOriginalDescriptor(Selection));
            }
            if (!sources.Invoke(() => owner._actualSelections!.IsIssuedOriginal(Selection) && owner._actualSelections.IsIssuedOriginalDescriptor(Selection, _descriptor)))
                throw new UnauthorizedAccessException("Exact current Files private selection/descriptor required.");
            _facts = sources.Invoke(() => new DescriptorFacts(_descriptor.WorkspaceId, _descriptor.ProjectId, _descriptor.RootId,
                _descriptor.WorkspaceRevision, _descriptor.ProjectRevision, _descriptor.RepositoryBindingId,
                _descriptor.RegisteredProjectRoot, _descriptor.ExactProjectReferenceJson, _descriptor.OriginalConversation,
                _descriptor.OriginalContainer, _descriptor.OriginalActor));
            _actor = await ActorAsync(sources, ct).ConfigureAwait(false);
            if (_actor != _facts.Actor || _facts.Conversation != Conversation || _facts.Container != Container)
                throw new UnauthorizedAccessException("The actual current Home/Files/conversation/container tuple differs.");
            await ValidateSelectionAsync(sources, ct).ConfigureAwait(false);
            _scopes = sources.Invoke(() => owner._actualSelections!.GetOriginalReadScopes(Selection).ToArray());
            if (_scopes.Length != 1 || _scopes[0].Kind != owner.ResourceKind || _scopes[0].Access != ResourceAccess.Read)
                throw new UnauthorizedAccessException("One genuine distinct current project READ scope required.");
            _arguments = JsonSerializer.SerializeToElement(new { _facts.WorkspaceId, _facts.ProjectId, _facts.RootId,
                _facts.WorkspaceRevision, _facts.ProjectRevision, ExactProjectReferenceJson = _facts.Reference,
                ConversationId = Conversation.Id, ContainerId = Container.Id, expectedSha, expectedRoot, operation = "Existing saved document/root READ; no Dev effect" });
            _review = sources.Invoke(() => owner._broker.PrepareReviewForActor(_actor, "dev", ReadAction, _scopes, _arguments,
                "Read the current saved project document and registered working root for this exact conversation/container. This grants no command or tool effect.",
                null, "dev:current-project:" + _actor.AuthenticationRevision));
            var observed = await sources.Await(sources.Invoke(() => owner._broker.AuthorizePreparedReviewWithinOriginalSourceAsync(
                _review, sources.Scope, sources.Retain, ct))).ConfigureAwait(false);
            var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
            for (var polls = 0; observed.State == HomePreparedReviewState.RequestObserved && observed.Request?.State == HomePermissionRequestState.PendingApproval; polls++)
            {
                if (polls >= 300 || DateTimeOffset.UtcNow >= deadline) throw new UnauthorizedAccessException("HOME_CURRENT_PROJECT_READ_APPROVAL_REQUIRED: review and Accept the exact request in Home.");
                await sources.Await(sources.Invoke(() => Task.Delay(TimeSpan.FromSeconds(1), ct))).ConfigureAwait(false);
                observed = await sources.Await(sources.Invoke(() => owner._broker.ObservePreparedReviewWithinOriginalSourceAsync(
                    _review, sources.Scope, sources.Retain, ct))).ConfigureAwait(false);
            }
            if (observed.Request?.State != HomePermissionRequestState.Approved) throw new UnauthorizedAccessException("Actual current project READ was not accepted.");
            await ValidateSelectionAsync(sources, ct).ConfigureAwait(false);
            if (await ActorAsync(sources, ct).ConfigureAwait(false) != _actor) throw new UnauthorizedAccessException("Home actor changed before READ claim.");
            _capability = await sources.Capture(() => owner._broker.BeginExecutionCapabilityWithinOriginalSourceAsync(
                _review.RequestId, _arguments, sources.Scope, sources.Retain, CleanupScope, ct), actual => _capability = actual).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Actual current project READ capability unavailable.");
            var claim = await sources.Await(sources.Invoke(() => owner._broker.ClaimExecutionWithinOriginalSourceAsync(
                _capability, "dev", ReadAction, _scopes, _arguments, sources.Scope, sources.Retain, CleanupScope, ct))).ConfigureAwait(false);
            if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != _actor)
                throw new UnauthorizedAccessException("Actual current project READ claim rejected.");
            _attestation = sources.Invoke(() => owner._broker.CaptureClaimedAttestation(_capability))
                ?? throw new UnauthorizedAccessException("An actual individual Home Accept is required.");
            lock (_gate) _readReady = true;
        }
        internal bool HasScope(AuthenticatedResourceActor actor, ResourceScope scope)
        { lock (_gate) return IsLive && _actor == actor && _scopes.Length == 1 && _scopes[0] == scope; }
        internal async Task ValidateSelectionAsync(Context sources, CancellationToken token)
        {
            DemandLive();
            if (!sources.Invoke(() => ReferenceEquals(owner._selections(), owner._actualSelections) &&
                owner._actualSelections!.IsIssuedOriginal(Selection) && owner._actualSelections.IsIssuedOriginalDescriptor(Selection, _descriptor)))
                throw new UnauthorizedAccessException("The configured current project private issuer retired or changed.");
            await sources.Await(sources.Invoke(() => owner._actualSelections!.RevalidateOriginalWithinSourceAsync(
                Selection, _actor, sources.Scope, sources.Retain, token))).ConfigureAwait(false);
            if (!sources.Invoke(() => owner._actualSelections!.IsIssuedOriginal(Selection))) throw new UnauthorizedAccessException("Current Files project selection retired.");
            DemandLive();
        }
        internal async Task ValidateReadAsync(Context sources, CancellationToken token)
        {
            if (!ReadReady) throw new UnauthorizedAccessException("The SAME actual claimed manual READ is unavailable.");
            await ValidateSelectionAsync(sources, token).ConfigureAwait(false);
            if (await ActorAsync(sources, token).ConfigureAwait(false) != _actor) throw new UnauthorizedAccessException("Current Home READ actor changed.");
            if (!await sources.Await(sources.Invoke(() => owner._permissions.IsExecutionCurrentAsync(_capability!.RequestId, token))).ConfigureAwait(false) ||
                await ActorAsync(sources, token).ConfigureAwait(false) != _actor)
                throw new UnauthorizedAccessException("The actual manual READ was revoked or its actor changed.");
            DemandLive();
        }
        public Task RevalidateOriginalAsync(CancellationToken token)
            => StartValidation(CleanupScope, _ => { }, sources => ValidateReadAsync(sources, token), nativeValidation: true);
        public T RunOriginalRead<T>(Func<T> body, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(body); DemandLive(); token.ThrowIfCancellationRequested(); _stop!.Token.ThrowIfCancellationRequested();
            Context sources = _current.Value ?? throw new UnauthorizedAccessException("Actual current original READ driver ancestry required.");
            TaskCompletionSource finished;
            lock (_gate)
            {
                if (!ReadReady || _nativeValidation?.IsCompletedSuccessfully != true || _finiteCalls.Count >= 20000)
                    throw new UnauthorizedAccessException("Fresh SAME privately validated finite READ is required.");
                finished = new(TaskCreationOptions.RunContinuationsAsynchronously); _finiteCalls.Add(finished.Task);
            }
            try { return sources.Invoke(body); }
            finally { finished.SetResult(); }
        }
        private async Task AcquireNativeAndHeldAsync(Context sources, CancellationToken token)
        {
            DemandLive();
            var ct = _stop!.Token;
            Task<IDeveloperOriginalCurrentProjectNativeRead>? actualCapture = null;
            _nativeRead = await sources.Capture(() =>
            {
                actualCapture = owner._actualNative!.CaptureOriginalWithinSourceAsync(Selection, this, sources.Scope, sources.Retain, ct);
                _nativeCapture = actualCapture; return actualCapture;
            }, actual =>
            {
                if (actualCapture is null || !owner._actualNative!.IsOwnedOriginalRead(Selection, actualCapture, actual))
                    throw new UnauthorizedAccessException("The returned native object lacks SAME private historical source custody.");
                _nativeRead = actual; _nativeCapture = actualCapture;
            }).ConfigureAwait(false);
            if (!sources.Invoke(() => owner._actualNative!.IsIssuedOriginalRead(Selection, _nativeCapture!, _nativeRead)))
                throw new UnauthorizedAccessException("The actual native READ is no longer eligible for use.");
            await sources.Await(sources.Invoke(() => owner._actualNative!.ValidateOriginalReadWithinSourceAsync(
                Selection, _nativeCapture!, _nativeRead, sources.Scope, sources.Retain, ct))).ConfigureAwait(false);
            await ValidateReadAsync(sources, ct).ConfigureAwait(false);
            (_documentSha, _rootFingerprint) = sources.Invoke(() =>
                (_nativeRead.OriginalWorkspaceDocumentSha256, _nativeRead.OriginalRegisteredRootFingerprint));
            // All Files/profile/native validation BEFORE completion and Home. While Home is
            // held, only its already-held check and native descriptor demand are used.
            _completion = await sources.Capture(() => _capability!.AcquireCommitCompletionLeaseAsync(owner._broker, ct), actual => _completion = actual).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The actual READ completion lifetime is unavailable.");
            _home = await sources.Capture(() => owner._store.AcquireLocalOperationLeaseCoreAsync(owner._profiles, _actor,
                new HeldGuard(this), sources.Scope, sources.Retain, ct), actual => _home = actual).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The actual Home READ currentness entry was refused.");
            await ValidateHeldAsync(sources, ct).ConfigureAwait(false);
        }
        internal async Task ValidateHeldAsync(Context sources, CancellationToken token)
        {
            DemandLive();
            if (_home is not IHomeOriginalScopedLocalOperationLease home || _nativeRead is null || _completion is null)
                throw new UnauthorizedAccessException("Actual held Home/completion/native READ custody required.");
            if (!await sources.Await(sources.Invoke(() => home.IsCurrentAsync(sources.Scope, sources.Retain, token).AsTask())).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual held Home READ/current actor was revoked.");
            sources.Invoke(() => { DemandOriginalCommit(); return true; });
        }
        public void DemandOriginalCommit()
        {
            DemandLive();
            IDeveloperOriginalCurrentProjectNativeRead native; HomeResourceExecutionCapability capability; HomeClaimedResourceAttestation attestation;
            lock (_gate)
            {
                if (_retired || _close is not null || !_readReady || _home is null || _completion is null || _nativeRead is null || _attestation is null || _capability is null)
                    throw new UnauthorizedAccessException("SAME actual uncompleted manual READ and held native/Home entry required.");
                native = _nativeRead; capability = _capability; attestation = _attestation;
            }
            // Never hold this private publication lock across another owner's descriptor/broker lock.
            if (!ReferenceEquals(owner._broker.CaptureClaimedAttestation(capability), attestation) || !capability.IsUncompletedClaim(owner._broker))
                throw new UnauthorizedAccessException("The actual claimed manual READ changed.");
            CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { native.DemandOriginalExecutionBinding(); return true; }));
            DemandLive();
        }
        private TaskRunColdProjectIdentity BuildIdentity() => new(_facts.WorkspaceId, _facts.ProjectId, _facts.RootId,
            _facts.WorkspaceRevision, _facts.ProjectRevision, _facts.RepositoryBindingId, _facts.Root,
            _facts.Reference, Container.Id, Hash(Container), Container.Instructions, _documentSha, _rootFingerprint, _actor);
        internal void DemandExactInput(TaskRunColdChatInput input)
        {
            DemandLive(); DemandExactOriginalInputValues(input);
        }
        private void DemandExactOriginalInputValues(TaskRunColdChatInput input)
        {
            if (input.Conversation != Conversation || input.Conversation.ContainerId != Container.Id || input.WorkspaceRoot != Identity.CanonicalRoot ||
                input.ProjectContext != Identity.OriginalProjectContextJson || input.ProjectInstructions != Identity.OriginalContainerInstructions ||
                Hash(Container) != Identity.OriginalContainerSha256)
                throw new UnauthorizedAccessException("The actual original project input differs from its private READ observation.");
        }
        internal async Task ValidateFreshShortReadAsync(Context sources, CancellationToken token)
        {
            var held = false; Task? gate = null; Exception? acquisition = null;
            try { _ = sources.Invoke(() => { gate = _shortReadGate.WaitAsync(token); sources.Retain(gate); return gate; }); }
            catch (Exception cause) { acquisition = cause; }
            if (gate is not null)
                try { await sources.Await(gate).ConfigureAwait(false); held = true; }
                catch (Exception cause) { acquisition = acquisition is null ? cause : new AggregateException(acquisition, gate.Exception ?? cause); }
            if (acquisition is not null) { if (held) _shortReadGate.Release(); ExceptionDispatchInfo.Capture(acquisition).Throw(); }
            if (!held) throw new InvalidOperationException("No actual short project currentness gate acquired.");
            try
            {
                await ValidateReadAsync(sources, token).ConfigureAwait(false); Exception? primary = null;
                try
                {
                    await AcquireNativeAndHeldAsync(sources, token).ConfigureAwait(false);
                    DemandSameResourceIdentity(Identity, BuildIdentity()); DemandOriginalCommit();
                }
                catch (Exception cause) { primary = cause; }
                try { await ReleaseBorrowersAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { throw new AggregateException("Fresh initial project currentness and short cleanup failed.", primary is null ? [cleanup] : [primary, cleanup]); }
                if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
            }
            finally { _shortReadGate.Release(); }
        }
        private sealed class HeldGuard(Work original) : IHomeOriginalScopedStateCommitActorGuard
        {
            public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
                HomeStateCommitPhase phase, CancellationToken token)
                => CheckAsync(state, expected, phase, original.CleanupScope, _ => { }, token);
            public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
                HomeStateCommitPhase phase, Action<Action> scope, Action<Task> retain, CancellationToken token)
            {
                if (!original.IsLive || expected != original._actor || original._attestation is null ||
                    !original.Owner._broker.IsClaimedAttestationCurrentInState(original._attestation, expected, state)) return false;
                return await original.Owner._profiles.CheckAsync(state, expected, phase, scope, retain, token).ConfigureAwait(false);
            }
        }
        // Own cleanup ancestry, independent of the original borrowing caller's later seal.
        private void CleanupScope(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(owner,
            () => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; }));
        private async Task ReleaseBorrowersAsync()
        {
            var sources = new Context(owner, this, body => body(), _ => { }, false); var closes = new List<Task>();
            IHomeLocalOperationLease? home; IAsyncDisposable? completion; IDeveloperOriginalCurrentProjectNativeRead? native; Task? capture;
            lock (_gate) { home = _home; completion = _completion; native = _nativeRead; capture = _nativeCapture;
                _home = null; _completion = null; _nativeRead = null; _nativeCapture = null; _contexts.Add(sources); }
            // Start every actual close independently before waiting. No sibling can skip
            // Home, completion or native cleanup, and no Home audit runs under the entry.
            if (home is not null) sources.AcquireClose(() => home.DisposeAsync().AsTask(), closes);
            if (completion is not null) sources.AcquireClose(() => completion.DisposeAsync().AsTask(), closes);
            Task? nativeClose = null;
            if (native is not null)
            {
                sources.AcquireClose(() => { nativeClose = native.DisposeAsync().AsTask(); return nativeClose; }, closes);
                if (capture is not null && nativeClose is not null) lock (_gate) _nativeClosed.Add((native, capture, nativeClose));
            }
            foreach (var close in closes) try { await sources.Await(close).ConfigureAwait(false); } catch (Exception cause) { sources.Errors.Retain(cause); }
            if (native is not null && capture is not null && nativeClose is not null)
                try { sources.Physical(() => owner._actualNative!.IsClosedOriginalRead(Selection, capture, native, nativeClose)
                    ? true : throw new UnauthorizedAccessException("The actual native READ close lacks its SAME healthy historical proof.")); }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            await sources.Settle().ConfigureAwait(false);
        }
        public Task CloseAndDrainOriginalAsync() { DemandExternalOriginalJoin(); return CloseOwned(); }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
        internal Task CloseOwned()
        {
            TaskCompletionSource begin; Task actual;
            lock (_gate)
            {
                if (_close is not null) return _close; _retired = true; begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual = ClosePublishedAsync(begin.Task); _close = actual;
            }
            begin.SetResult(); return actual;
        }
        private async Task ClosePublishedAsync(Task begin)
        {
            await begin.ConfigureAwait(false); using var ownerPhase = CloudflareOriginalExecutionGuard.EnterOriginal(owner);
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var sources = new Context(owner, this, body => body(), _ => { }, false);
            try { await sources.Await(sources.Physical(CancelOriginalPendingAsync)).ConfigureAwait(false); }
            catch (Exception cause) { sources.Errors.Retain(cause); }
            try { await ReleaseBorrowersAsync().ConfigureAwait(false); } catch (Exception cause) { sources.Errors.Retain(cause); }
            Task[] drivers; lock (_gate) drivers = (Driver is null ? Array.Empty<Task>() : [Driver]).Concat(_validations).Concat(_finiteCalls).ToArray();
            foreach (var driver in drivers) try { await sources.Await(driver).ConfigureAwait(false); } catch (Exception cause) { sources.Errors.Retain(cause); }
            // Capture any genuine product returned after the first snapshot/seal too.
            try { await ReleaseBorrowersAsync().ConfigureAwait(false); } catch (Exception cause) { sources.Errors.Retain(cause); }
            try { await ReleaseCommandNativeReadsAsync().ConfigureAwait(false); } catch (Exception cause) { sources.Errors.Retain(cause); }
            Context[] contexts; lock (_gate) contexts = _contexts.ToArray();
            foreach (var context in contexts)
                try { await context.Settle().ConfigureAwait(false); } catch (Exception cause) { sources.Errors.Retain(cause); }
            if (_capability is not null)
            {
                try
                {
                    var result = await sources.Await(sources.Invoke(() => _capability.IsUncompletedClaim(owner._broker)
                        ? owner._broker.CompleteExecutionAsync(_capability,
                            new(sources.Errors.OriginalErrors.Count == 0 ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                                sources.Errors.OriginalErrors.Count == 0 ? "HOME_CURRENT_PROJECT_READ_CLOSED" : "HOME_CURRENT_PROJECT_READ_UNFINISHED",
                                "The actual current project READ, native descriptor and Home borrower cohort was joined; no Dev effect was granted.", []), CancellationToken.None)
                        : owner._broker.AbortUnclaimedExecutionAsync(_capability, CancellationToken.None))).ConfigureAwait(false);
                    if (!result.Succeeded) throw new InvalidOperationException("Actual Home current-project READ audit unfinished: " + result.Code);
                }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            }
            try { sources.Physical(() => { _stop?.Dispose(); return true; }); } catch (Exception cause) { sources.Errors.Retain(cause); }
            await sources.Settle().ConfigureAwait(false);
        }
    }
}
public sealed class HomeColdProjectReadActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) => appId == "dev" && actionId == HomeColdProjectReadReconciliation.ReadAction
        ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, false, false, RequiresPerActionApproval: true) : null;
}
public sealed class HomeColdProjectReadResourceResolver(Func<HomeColdProjectReadReconciliation> source)
    : IOriginalScopedCanonicalResourceAccessResolver
{
    public string ResourceKind => "dev.project.current";
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope resource, CancellationToken token)
        => source().EvaluateAsync(actor, action, resource, token);
    public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string action,
        ResourceScope resource, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => source().EvaluateWithinOriginalSourceAsync(actor, action, resource, scope, retain, token);
}
