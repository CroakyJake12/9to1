using System.Collections.Frozen;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Independent private final-fence registry. It does not depend on the coordinator/router
/// or physical tool service, so shared DI has no authority/service construction cycle.</summary>
public sealed partial class WorkspaceTaskRunEffectAuthority : IWorkspaceToolFinalFenceAuthority
{
    private readonly TaskRunPermissionAuthority _tasks;
    private readonly IPermissionDecisionEngine _policy;
    private readonly IPermissionOriginalEffectFence _effects;
    private readonly object _sync = new();
    private readonly HashSet<Fence> _issued = [];
    public WorkspaceTaskRunEffectAuthority(TaskRunPermissionAuthority tasks, IPermissionDecisionEngine policy,
        IPermissionOriginalEffectFence effects)
        : this(tasks, policy, effects, null) { }
    public WorkspaceTaskRunEffectAuthority(TaskRunPermissionAuthority tasks, IPermissionDecisionEngine policy,
        IPermissionOriginalEffectFence effects, Func<IWorkspaceOriginalProcessStartConsentSource>? processConsents)
    {
        _processConsents = processConsents;
        if (!ReferenceEquals(policy, effects)) throw new ArgumentException("SAME central policy/effect writer gate required.");
        _tasks = tasks; _policy = policy; _effects = effects;
    }
    public bool IsIssuedOriginal(IWorkspaceToolFinalFence originalFence)
    { lock (_sync) return originalFence is Fence original && _issued.Contains(original); }
    internal Task ValidateOriginalToolAsync(TaskRunAttemptAdmission original, string toolName, CancellationToken token) =>
        _tasks.ValidateOriginalToolAsync(original, toolName, token);
    internal Fence IssueOriginal(TaskRunAttemptAdmission original, Guid actionId, string root,
        OllamaToolCall call, CapabilityDefinition actualCapability)
    {
        if (!_tasks.IsIssuedOriginal(original.Lease) || original.AttemptId != original.Lease.AttemptId)
            throw new UnauthorizedAccessException("SAME actual task admission issuer required.");
        _tasks.DemandOriginalToolIntent(original.Lease, call.Name);
        var scope = "capability:" + actualCapability.Key;
        var requires = actualCapability.Availability == CapabilityAvailability.PermissionRequired ||
            actualCapability.RiskClass >= CapabilityRiskClass.Consequential;
        if (_policy.Evaluate(scope, actualCapability.RiskClass, requires, "Original workspace tool admission").Kind != PermissionDecisionKind.Allowed)
            throw new UnauthorizedAccessException("Current central capability permission requires approval before dispatch.");
        var fence = new Fence(this, original, actionId, root, call, scope, actualCapability.RiskClass, requires);
        lock (_sync)
        {
            if (_issued.Count == 128) throw new InvalidOperationException("Original workspace final-fence custody is full.");
            _issued.Add(fence);
        }
        return fence;
    }
    internal void RetireOriginal(Fence original)
    { original.Seal(); lock (_sync) _issued.Remove(original); }

    internal sealed partial class Fence(WorkspaceTaskRunEffectAuthority issuer, TaskRunAttemptAdmission original,
        Guid actionId, string root, OllamaToolCall call, string scope, CapabilityRiskClass risk, bool requires)
        : IWorkspaceOriginalReadFence
    {
        private WorkspaceTaskRunEffectAuthority Issuer => issuer;
        private readonly object _sync = new();
        private int _pins;
        private bool _closed;
        private ITaskRunToolActionPreparation? _preparation;
        private ITaskRunOriginalActionAdmissionSource? _actionSource;
        private TaskRunOriginalActionAdmission? _actionReceipt;
        private readonly List<Task> _actionValidations = [];
        internal void BindOriginalPreparation(ITaskRunToolActionPreparation preparation, ITaskRunOriginalActionAdmissionSource source)
        {
            ArgumentNullException.ThrowIfNull(preparation); ArgumentNullException.ThrowIfNull(source);
            lock (_sync)
            {
                if (_closed || _preparation is not null || !ReferenceEquals(preparation.OriginalAttempt, OriginalAttempt) || preparation.ActionId != ActionId)
                    throw new UnauthorizedAccessException("SAME private preparation/final fence binding required.");
                _preparation = preparation; _actionSource = source;
            }
        }
        internal async Task ValidateOriginalActionAsync(CancellationToken token)
        {
            ITaskRunToolActionPreparation preparation; ITaskRunOriginalActionAdmissionSource source;
            lock (_sync)
            {
                if (_closed) throw new UnauthorizedAccessException("Original workspace action fence retired.");
                preparation = _preparation ?? throw new UnauthorizedAccessException("Original preparation is not bound.");
                source = _actionSource ?? throw new InvalidOperationException("Actual canonical action admission source is unavailable.");
            }
            var receipt = source.RequireOriginalActionAdmission(preparation, OriginalAttempt);
            Task validation;
            try { validation = source.ValidateOriginalActionAdmissionAsync(receipt, preparation, OriginalAttempt, token); }
            catch (OperationCanceledException error) { throw new AggregateException("Synchronous original action validation source fault.", error); }
            lock (_sync)
            {
                if (_actionReceipt is not null && !ReferenceEquals(_actionReceipt, receipt))
                    throw new UnauthorizedAccessException("The original action receipt cannot be replaced.");
                _actionReceipt = receipt; _actionValidations.Add(validation);
            }
            try { await validation.ConfigureAwait(false); }
            catch (Exception) when (validation.IsFaulted)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(validation.Exception!).Throw(); throw; }
            lock (_sync) DemandOriginalAction();
        }
        private void DemandOriginalAction()
        {
            if (_closed || _preparation is null || _actionSource is null || _actionReceipt is null ||
                _actionValidations.Count == 0 || _actionValidations.Any(task => !task.IsCompletedSuccessfully))
                throw new UnauthorizedAccessException("SAME successful original action validation required.");
            // Source Demand is metadata-only, without callbacks or repository/policy I/O.
            _actionSource.DemandOriginalActionAdmission(_actionReceipt, _preparation, OriginalAttempt);
        }
        public TaskRunAttemptAdmission OriginalAttempt { get; } = original;
        public Guid ActionId { get; } = actionId;
        public string CanonicalWorkspaceRoot { get; } = root;
        public OllamaToolCall OriginalCall { get; } = call;
        public async ValueTask<IAsyncDisposable?> AcquireOriginalCommitPinAsync(CancellationToken token)
        {
            // Canonical actor/model/privacy I/O occurs BEFORE native owner locks. The returned pin
            // is raw retirement custody only and never acquires Home/Files/SQLite/Context locks.
            if (!issuer.IsIssuedOriginal(this) || OriginalAttempt.Lease is not ITaskRunAdmissionCommitLease actual) return null;
            await ValidateOriginalActionAsync(token).ConfigureAwait(false);
            await issuer._tasks.ValidateOriginalToolAsync(OriginalAttempt, OriginalCall.Name, token).ConfigureAwait(false);
            var pin = await actual.AcquireOriginalCommitPinAsync(token).ConfigureAwait(false);
            if (pin is null) return null;
            try { await AcquireOriginalProcessStartEntryAsync(token).ConfigureAwait(false); }
            catch (Exception primary)
            {
                var errors = new List<Exception> { primary };
                Task? homeClose = null; Task? pinClose = null;
                try { homeClose = CloseOriginalProcessConsentAsync(); await homeClose.ConfigureAwait(false); }
                catch (Exception cause) { errors.Add((Exception?)homeClose?.Exception ?? cause); }
                try { pinClose = pin.DisposeAsync().AsTask(); _ = _entryClosing.Track(pinClose); await pinClose.ConfigureAwait(false); }
                catch (Exception cause) { errors.Add((Exception?)pinClose?.Exception ?? cause); }
                throw new AggregateException("Original attempt pin/Home entry acquisition and independent cleanup failed.", errors);
            }
            bool accepted;
            lock (_sync) { accepted = !_closed; if (accepted) _pins++; }
            if (!accepted)
            {
                var errors = new List<Exception>(); Task? homeClose = null; Task? pinClose = null;
                if (HasOriginalProcessConsent)
                    try { homeClose = CloseOriginalProcessConsentAsync(); await homeClose.ConfigureAwait(false); }
                    catch (Exception cause) { errors.Add((Exception?)homeClose?.Exception ?? cause); }
                try { pinClose = pin.DisposeAsync().AsTask(); _ = _entryClosing.Track(pinClose); await pinClose.ConfigureAwait(false); }
                catch (Exception cause) { errors.Add((Exception?)pinClose?.Exception ?? cause); }
                if (errors.Count != 0) throw new AggregateException("Rejected original process entry and independent attempt pin cleanup failed.", errors);
                return null;
            }
            return new Pin(this, pin);
        }
        public void DemandOriginalEffect(string actualRoot, WorkspaceToolEffectKind kind, string target, string sha)
        {
            lock (_sync)
            {
                if (_closed || _pins == 0 || actualRoot != CanonicalWorkspaceRoot || !Enum.IsDefined(kind) ||
                    string.IsNullOrWhiteSpace(target) || sha.Length != 64 || !sha.All(Uri.IsHexDigit))
                    throw new UnauthorizedAccessException("Original task/call/raw-pin effect pairing refused.");
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (kind != WorkspaceToolEffectKind.ProcessStart &&
                    !target.StartsWith(CanonicalWorkspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
                    throw new UnauthorizedAccessException("Original effect target is outside the captured workspace.");
                if (kind == WorkspaceToolEffectKind.ProcessStart && OriginalCall.Name is not ("run_command" or "run_tests") ||
                    kind == WorkspaceToolEffectKind.AtomicWrite && OriginalCall.Name is not ("write_file" or "replace_in_file" or "apply_change_set") ||
                    kind == WorkspaceToolEffectKind.RollbackDelete && OriginalCall.Name != "apply_change_set")
                    throw new UnauthorizedAccessException("Original typed tool does not own this native effect kind.");
                DemandOriginalAction();
                if (kind == WorkspaceToolEffectKind.ProcessStart) DemandOriginalProcessStartConsent(actualRoot, target, sha);
                // Physical owner independently validates exact call->path/content/process preimage.
                // SHA describes that original effect; it is never independently an authority.
            }
        }
        public T RunOriginalEffect<T>(string actualRoot, WorkspaceToolEffectKind kind, string target, string sha, Func<T> body)
        {
            lock (_sync)
            {
                DemandOriginalEffect(actualRoot, kind, target, sha);
                return issuer._effects.RunOriginalEffect(scope, risk, requires, "Original workspace final native effect", () =>
                {
                    DemandOriginalAction(); // Same active private claim at the finite central policy/native boundary.
                    return kind == WorkspaceToolEffectKind.ProcessStart
                        ? RunOriginalProcessStartConsent(actualRoot, target, sha, body) : body();
                });
            }
        }
        private readonly List<Task> _readValidations = [];
        public async ValueTask RevalidateOriginalReadAsync(CancellationToken token)
        {
            await ValidateOriginalActionAsync(token).ConfigureAwait(false);
            Task validation;
            try { validation = issuer._tasks.ValidateOriginalToolAsync(OriginalAttempt, OriginalCall.Name, token); }
            catch (OperationCanceledException error)
            { throw new AggregateException("Synchronous original read authority source fault.", error); }
            lock (_sync) _readValidations.Add(validation);
            try { await validation.ConfigureAwait(false); }
            catch (Exception) when (validation.IsFaulted)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(validation.Exception!).Throw(); throw; }
            lock (_sync)
            {
                if (_closed || _pins == 0) throw new UnauthorizedAccessException("Original read retirement pin is unavailable.");
                DemandOriginalAction();
            }
        }
        public T RunOriginalRead<T>(string actualRoot, string target, Func<T> nativeRead, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(nativeRead);
            token.ThrowIfCancellationRequested();
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var capturedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(CanonicalWorkspaceRoot));
            lock (_sync)
            {
                if (_closed || _pins == 0 || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(actualRoot)).Equals(capturedRoot, comparison) ||
                    OriginalCall.Name is not ("list_files" or "search_files") || _readValidations.Count == 0 ||
                    _readValidations.Any(task => !task.IsCompletedSuccessfully))
                    throw new UnauthorizedAccessException("SAME validated original action/raw-pin read pairing required.");
                var declared = OriginalCall.Arguments.TryGetValue("path", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? "." : OriginalCall.Arguments.TryGetValue("path", out value) ? value.ToString() : ".";
                var start = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(CanonicalWorkspaceRoot, declared)));
                var exactTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
                if (!(start.Equals(capturedRoot, comparison) || start.StartsWith(capturedRoot + Path.DirectorySeparatorChar, comparison)) ||
                    !(exactTarget.Equals(start, comparison) || exactTarget.StartsWith(start + Path.DirectorySeparatorChar, comparison)))
                    throw new UnauthorizedAccessException("The original read target escapes the captured folder.");
                DemandOriginalAction();
                // Same writer gate as Grant/Revoke/SetPolicy, finite native read initiation or
                // observation only. All awaited authority/read/cleanup tasks run outside it.
                return issuer._effects.RunOriginalEffect(scope, risk, requires, "Original workspace finite native read", () =>
                {
                    token.ThrowIfCancellationRequested(); DemandOriginalAction(); return nativeRead();
                });
            }
        }
        internal void Seal() { lock (_sync) _closed = true; }
        private sealed class Pin(Fence owner, IAsyncDisposable actual) : IAsyncDisposable
        {
            private readonly object _sync = new(); private Task? _close;
            public ValueTask DisposeAsync()
            {
                TaskCompletionSource completion;
                lock (_sync)
                {
                    if (_close is not null) return new(_close);
                    completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = completion.Task;
                }
                _ = ClosePublishedAsync(completion);
                return new(completion.Task);
            }
            private async Task ClosePublishedAsync(TaskCompletionSource completion)
            {
                Task? close = null;
                try
                {
                    close = actual.DisposeAsync().AsTask();
                    await close.ConfigureAwait(false);
                    lock (owner._sync) owner._pins--;
                    completion.TrySetResult();
                }
                catch (Exception error) { completion.TrySetException((Exception?)close?.Exception ?? error); }
                // Failed release retains pin custody; attempted release is not proven retirement.
            }
        }
    }
}

/// <summary>Canonical Chat workspace owner. It wraps the existing executor once, preserves exact
/// original results, and accepts only a privately validated native owner receipt. Local reads do
/// not require CAKE login, Home installation manager, publisher, or fabricated profile.</summary>
public sealed partial class WorkspaceTaskRunToolActionOwner : ITaskRunToolActionOwner
{
    private readonly Func<TaskExecutionCoordinator> _coordinator;
    private readonly WorkspaceTaskRunReceiptAuthority _receipts;
    private readonly ITaskRunOriginalFrameOwner _frames;
    private readonly IWorkspaceToolService _service;
    private readonly CapabilityRegistryService _capabilities;
    private readonly WorkspaceTaskRunEffectAuthority _effects;
    private readonly CapabilityPlatform _platform;
    private readonly object _sync = new();
    private readonly HashSet<Preparation> _prepared = [];
    public WorkspaceTaskRunToolActionOwner(Func<TaskExecutionCoordinator> coordinator, ITaskRunOriginalFrameOwner frames,
        IWorkspaceToolService service, CapabilityRegistryService capabilities, WorkspaceTaskRunEffectAuthority effects,
        CapabilityPlatform platform, WorkspaceTaskRunReceiptAuthority receipts)
    {
        if (platform is not (CapabilityPlatform.Windows or CapabilityPlatform.Android or CapabilityPlatform.Linux)) throw new ArgumentException("Actual configured host platform required.");
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator)); _receipts = receipts; _frames = frames; _service = service; _capabilities = capabilities; _effects = effects; _platform = platform;
    }
    private sealed class Preparation(WorkspaceTaskRunToolActionOwner owner, TaskRunAttemptAdmission admission,
        Guid actionId, string root, OllamaToolCall call, bool readOnly, WorkspaceTaskRunEffectAuthority.Fence? fence)
        : IWorkspaceToolActionPreparation
    {
        public TaskRunAttemptAdmission OriginalAttempt { get; } = admission;
        public Guid ActionId { get; } = actionId;
        public TaskActionInterruptionPolicy InterruptionPolicy => ReadOnly ? TaskActionInterruptionPolicy.ReadOnlyCancellable : TaskActionInterruptionPolicy.AtomicCommit;
        public IReadOnlyList<string> RequiredPermissionScopes { get; } = Array.AsReadOnly(new[] { "workspace:" + call.Name });
        public TaskOriginalToolIntent OriginalToolIntent { get; } = new(ToolRuntimeKind.Workspace.ToString(), call.Name, root, WorkspaceToolOriginalDigest.Call(call));
        public string CanonicalWorkspaceRoot { get; } = root;
        public OllamaToolCall OriginalCall { get; } = call;
        public IWorkspaceOriginalInvocation? OriginalInvocation { get; set; }
        public WorkspaceTaskRunEffectAuthority.Fence? Fence { get; } = fence;
        public bool ReadOnly { get; } = readOnly;
        public Task<TaskRunToolActionResult>? OriginalExecution;
        public Task<TaskRunToolActionResult>? OriginalToolFrame;
        public int OriginalToolBodyEntered;
        public Task? OriginalPreBodyClose;
        public TaskRunToolActionResult? Result;
        public WorkspaceToolPhysicalOutcome? Outcome;
        public readonly List<Exception> Errors = [];
        public readonly List<Exception> ObservationErrors = [];
        public bool RuntimeAdmissionOpen;
        public CancellationToken OriginalRuntimeToken;
        public Task<WorkspaceToolResult>? OriginalRuntime;
        public Task<WorkspaceToolResult>? OriginalRuntimeBody;
        public bool IsIssuedOriginalRuntime(IWorkspaceToolService actualService, string actualRoot, OllamaToolCall actualCall) =>
            ReferenceEquals(actualService, owner._service) && actualRoot == CanonicalWorkspaceRoot &&
            WorkspaceToolOriginalDigest.Call(actualCall) == WorkspaceToolOriginalDigest.Call(OriginalCall) && owner.IsIssued(this);
        public Task<WorkspaceToolResult> RunOriginalRuntimeAsync(IWorkspaceToolService actualService,
            string actualRoot, OllamaToolCall actualCall, Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(body);
            if (!IsIssuedOriginalRuntime(actualService, actualRoot, actualCall))
                throw new UnauthorizedAccessException("Actual original runtime/service/call required.");
            TaskCompletionSource<WorkspaceToolResult> completion;
            lock (owner._sync)
            {
                if (OriginalRuntime is not null) return OriginalRuntime;
                if (!RuntimeAdmissionOpen) throw new UnauthorizedAccessException("Original acknowledged tool frame is not dispatching.");
                token.ThrowIfCancellationRequested();
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously); OriginalRuntime = completion.Task;
            }
            _ = RunPublishedRuntimeAsync(body, completion);
            return completion.Task;
        }
        private async Task RunPublishedRuntimeAsync(Func<CancellationToken, Task<WorkspaceToolResult>> body,
            TaskCompletionSource<WorkspaceToolResult> completion)
        {
            try
            {
                OriginalRuntimeBody = body(OriginalRuntimeToken);
                completion.TrySetResult(await OriginalRuntimeBody.ConfigureAwait(false));
            }
            catch (Exception error) { completion.TrySetException((Exception?)OriginalRuntimeBody?.Exception ?? error); }
        }
    }
    public bool SupportsCanonicalInvocation(ToolRuntimeKind runtime, string toolName) =>
        runtime == ToolRuntimeKind.Workspace && _service is IWorkspaceOriginalInvocationSource &&
            ((toolName is "read_file" or "preview_change_set" or "write_file" or "replace_in_file" or "apply_change_set" or "run_command" or "run_tests") ||
             (toolName is "list_files" or "search_files") && _service is IWorkspaceOriginalTraversalSource traversal &&
                 traversal.SupportsOriginalTraversal(toolName));

    private bool IsIssued(Preparation original) { lock (_sync) return _prepared.Contains(original); }
    public async Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(TaskRunAttemptAdmission original,
        TaskExecutionSnapshot snapshot, Guid actionId, OllamaToolCall call, ToolRuntimeKind runtime,
        PermissionMode permissionIntent, string? originalWorkspaceRoot, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(call);
        if (!SupportsCanonicalInvocation(runtime, call.Name) || actionId == Guid.Empty || !Enum.IsDefined(permissionIntent) ||
            string.IsNullOrWhiteSpace(originalWorkspaceRoot) || call.Arguments.Count > 128)
            throw new InvalidOperationException("This owner supports an actual canonical workspace invocation only.");
        var acknowledged = await _coordinator().TryGetIssuedAttemptAsync(snapshot.TaskId, snapshot.ExecutionId, original.AttemptId, token).ConfigureAwait(false);
        if (!ReferenceEquals(acknowledged, original) || snapshot.OwnerBinding != original.Lease.Owner)
            throw new UnauthorizedAccessException("SAME original issued task attempt required.");
        var readOnly = call.Name is "read_file" or "list_files" or "search_files" or "preview_change_set";
        if (!readOnly && call.Name is not ("write_file" or "replace_in_file" or "apply_change_set" or "run_command" or "run_tests"))
            throw new InvalidOperationException("The actual workspace owner does not declare this typed tool.");
        var current = await _coordinator().GetAsync(snapshot.TaskId, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Actual canonical task state is unavailable.");
        if (!readOnly && current.Plan.Any(node => node.State == TaskPlanNodeState.RequiresReexecution &&
                node.InterruptionPolicy is TaskActionInterruptionPolicy.AtomicCommit or TaskActionInterruptionPolicy.SafeBoundary))
            throw new UnauthorizedAccessException("An unresolved owner effect requires inspection before another mutation.");
        await _effects.ValidateOriginalToolAsync(original, call.Name, token).ConfigureAwait(false);
        var root = Path.GetFullPath(originalWorkspaceRoot);
        var captured = call with { Arguments = call.Arguments.ToFrozenDictionary(value => value.Key, value => value.Value.Clone(), StringComparer.Ordinal) };
        var implementation = readOnly ? "workspace.read-file" : call.Name is "run_command" ? "workspace.run-command" :
            call.Name is "run_tests" ? "workspace.run-tests" : "workspace.write-file";
        var declared = (await _capabilities.DiscoverAsync(_platform, token).ConfigureAwait(false)).Where(value =>
            value.ProviderId == "haven.workspace" && value.ImplementationKey == implementation && value.IsEnabled && value.IsAgentUsable).Take(2).ToArray();
        if (declared.Length != 1) throw new InvalidOperationException("Current registered workspace capability is unavailable or ambiguous.");
        WorkspaceTaskRunEffectAuthority.Fence? fence = null;
        Preparation? prepared = null;
        try
        {
            fence = _effects.IssueOriginal(original, actionId, root, captured, declared[0]);
            prepared = new(this, original, actionId, root, captured, readOnly, fence);
            var actionSource = (object)_coordinator() as ITaskRunOriginalActionAdmissionSource
                ?? throw new InvalidOperationException("The configured canonical coordinator has no original action admission source.");
            fence.BindOriginalPreparation(prepared, actionSource); // No action ACK is claimed during preparation.
            lock (_sync)
            {
                if (_prepared.Count == 128) throw new InvalidOperationException("Retained original workspace action custody is full.");
                _prepared.Add(prepared); // Bound custody before any physical acquisition/callback.
            }
            if (_service is not IWorkspaceOriginalInvocationSource source)
                throw new InvalidOperationException("The genuine physical workspace read/effect producer is unavailable before dispatch.");
            // Canonical read_file/preview use the SAME retained physical root/read owner. No
            // legacy service fallback or direct directory traversal is an original witness.
            prepared.OriginalInvocation = source.AcquireOriginalInvocation(fence);
            if (!source.IsIssuedOriginal(prepared.OriginalInvocation)) throw new UnauthorizedAccessException("Physical original invocation issuer mismatch.");
            await _frames.RegisterOriginalAttemptAsync(original, token).ConfigureAwait(false);
            return prepared;
        }
        catch (Exception primary)
        {
            var errors = prepared?.Errors ?? new List<Exception>(); Add(errors, primary);
            var closed = true;
            if (prepared?.OriginalInvocation is { } physical)
            {
                Task? close = null;
                try { close = physical.CloseAndDrainAsync(); await close.ConfigureAwait(false); }
                catch (Exception error) { closed = false; Add(errors, (Exception?)close?.Exception ?? error); }
            }
            if (fence is { HasOriginalProcessConsent: true })
            {
                Task? consentClose = null;
                try { consentClose = fence.CloseOriginalProcessConsentAsync(); await consentClose.ConfigureAwait(false); }
                catch (Exception error) { closed = false; Add(errors, (Exception?)consentClose?.Exception ?? error); }
            }
            if (fence is not null && closed) _effects.RetireOriginal(fence);
            // Already-admitted failed partial originals remain bounded in the private registry.
            if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
            throw new AggregateException("Original preparation and independent partial cleanup failed.", errors);
        }
    }
    public Task<TaskRunToolActionResult> ExecuteOriginalAsync(ITaskRunToolActionPreparation preparation,
        Func<CancellationToken, Task<WorkspaceToolResult>> originalBody, CancellationToken token)
    {
        if (preparation is not Preparation actual || !IsIssued(actual)) throw new UnauthorizedAccessException("Actual original preparation required.");
        lock (_sync)
        {
            // Single-use/coalesced execution; an original receipt or unknown outcome never redispatches.
            if (actual.OriginalExecution is not null) return actual.OriginalExecution;
            var completion = new TaskCompletionSource<TaskRunToolActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            actual.OriginalExecution = completion.Task; // Publish before frame/body callbacks can reenter.
            _ = StartPublishedAsync(actual, originalBody, token, completion);
            return completion.Task;
        }
    }
    private async Task StartPublishedAsync(Preparation original, Func<CancellationToken, Task<WorkspaceToolResult>> body,
        CancellationToken token, TaskCompletionSource<TaskRunToolActionResult> completion)
    {
        // ExecuteOriginalAsync publishes under its registry lock. Yield before any frame
        // callback or synchronous pre-body refusal can initiate independent native cleanup.
        await Task.Yield();
        Task<TaskRunToolActionResult>? frame = null;
        try
        {
            frame = _frames.StartOriginalToolFrameAsync(original.OriginalAttempt, ct =>
            {
                Interlocked.Exchange(ref original.OriginalToolBodyEntered, 1);
                return ExecuteCoreAsync(original, body, ct);
            }, token);
            original.OriginalToolFrame = frame; // SAME source Task, never an idle/response proxy.
            completion.TrySetResult(await frame.ConfigureAwait(false));
        }
        catch (Exception error)
        {
            var primary = (Exception?)frame?.Exception ?? error;
            if (Volatile.Read(ref original.OriginalToolBodyEntered) != 0)
            {
                // Entered bodies retain their existing Complete/Close finally and exact result/fault path.
                completion.TrySetException(primary); return;
            }
            var errors = new List<Exception> { primary };
            if (original.OriginalInvocation is { } physical)
            {
                try
                {
                    original.OriginalPreBodyClose = physical.CloseAndDrainAsync();
                    await original.OriginalPreBodyClose.ConfigureAwait(false);
                }
                catch (Exception cause) { errors.Add((Exception?)original.OriginalPreBodyClose?.Exception ?? cause); }
            }
            // Only a successfully joined actual close permits retiring this SAME issued fence.
            // No CompleteOriginalAsync(true), fabricated result, receipt or action ACK is published.
            if (original.OriginalPreBodyClose?.IsCompletedSuccessfully == true && original.Fence is { } fence)
                try { _effects.RetireOriginal(fence); } catch (Exception cause) { errors.Add(cause); }
            lock (_sync) foreach (var cause in errors) Add(original.Errors, cause);
            if (errors.Count == 1 && frame?.IsCanceled == true && error is OperationCanceledException canceled)
                completion.TrySetCanceled(canceled.CancellationToken);
            else completion.TrySetException(errors.Count == 1 ? primary :
                new AggregateException("Original workspace pre-body frame and independent physical cleanup failed.", errors));
        }
    }
    public async ValueTask ValidateOriginalPreparationAsync(ITaskRunToolActionPreparation preparation,
        TaskExecutionSnapshot snapshot, CancellationToken token)
    {
        if (preparation is not Preparation original || !IsIssued(original) ||
            snapshot.TaskId != original.OriginalAttempt.Snapshot.TaskId || snapshot.ExecutionId != original.OriginalAttempt.Snapshot.ExecutionId ||
            snapshot.OwnerBinding != original.OriginalAttempt.Lease.Owner || original.OriginalExecution is not null)
            throw new UnauthorizedAccessException("SAME not-yet-dispatched original tool preparation required.");
        var actual = await _coordinator().TryGetIssuedAttemptAsync(snapshot.TaskId, snapshot.ExecutionId,
            original.OriginalAttempt.AttemptId, token).ConfigureAwait(false);
        if (!ReferenceEquals(actual, original.OriginalAttempt)) throw new UnauthorizedAccessException("Actual current original attempt required.");
        await _effects.ValidateOriginalToolAsync(original.OriginalAttempt, original.OriginalCall.Name, token).ConfigureAwait(false);
    }

    private async Task<TaskRunToolActionResult> ExecuteCoreAsync(Preparation original,
        Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token)
    {
        WorkspaceToolResult? result = null; Task<WorkspaceToolResult>? actualBody = null;
        try
        {
            var admission = original.OriginalAttempt;
            var current = await _coordinator().GetAsync(admission.Snapshot.TaskId, token).ConfigureAwait(false);
            if (current is null || current.ExecutionId != admission.Snapshot.ExecutionId || current.OwnerBinding != admission.Lease.Owner ||
                !current.Plan.Any(node => node.ActionId == original.ActionId && node.OriginalToolIntent == original.OriginalToolIntent &&
                    node.InterruptionPolicy == original.InterruptionPolicy && node.State is TaskPlanNodeState.Pending or TaskPlanNodeState.Running))
                throw new UnauthorizedAccessException("Actual action acknowledgement required before effect.");
            await (original.Fence ?? throw new UnauthorizedAccessException("Actual original action fence required.")).ValidateOriginalActionAsync(token).ConfigureAwait(false);
            await _effects.ValidateOriginalToolAsync(admission, original.OriginalCall.Name, token).ConfigureAwait(false);
            lock (_sync) { original.RuntimeAdmissionOpen = true; original.OriginalRuntimeToken = token; }
            actualBody = body(token); result = await actualBody.ConfigureAwait(false);
            if (!WorkspaceToolRuntime.IsIssuedOriginalResult(original, result))
                throw new UnauthorizedAccessException("Original body returned no actual privately issued runtime result.");
            if (result.OriginalRuntimeError is { } observation)
                Add(original.ObservationErrors, observation); // Kept independently of genuine known-effect receipt acceptance.

        }
        catch (Exception error) { Add(original.Errors, (Exception?)actualBody?.Exception ?? error); }
        finally
        {
            lock (_sync) original.RuntimeAdmissionOpen = false;
            // Join the SAME runtime body independently even if an outer callback returned early/faulted.
            if (original.OriginalRuntime is { } runtime)
            {
                try { await runtime.ConfigureAwait(false); }
                catch (Exception error) { Add(original.Errors, (Exception?)runtime.Exception ?? error); }
            }
            if (original.Fence is { HasOriginalProcessConsent: true } entryFence)
            {
                Task? consentClose = null;
                try { consentClose = entryFence.CloseOriginalProcessConsentAsync(); await consentClose.ConfigureAwait(false); }
                catch (Exception error) { Add(original.Errors, (Exception?)consentClose?.Exception ?? error); }
            }
            if (original.OriginalInvocation is { } physical)
            {
                Task<WorkspaceToolPhysicalOutcome>? outcome = null;
                try
                {
                    outcome = physical.CompleteOriginalAsync(result is not null && WorkspaceToolRuntime.IsIssuedOriginalResult(original, result) && result.OriginalEffectBodyCompleted, CancellationToken.None);
                    original.Outcome = await outcome.ConfigureAwait(false);
                    if (_service is not IWorkspaceOriginalInvocationSource source || !source.ValidateOriginalOutcome(physical, original.Outcome))
                        throw new UnauthorizedAccessException("Original physical receipt issuer mismatch.");
                    foreach (var cause in original.Outcome.OriginalErrors) Add(original.Errors, cause);
                }
                catch (Exception error) { Add(original.Errors, (Exception?)outcome?.Exception ?? error); }
                Task? close = null;
                try { close = physical.CloseAndDrainAsync(); await close.ConfigureAwait(false); }
                catch (Exception error) { Add(original.Errors, (Exception?)close?.Exception ?? error); }
            }
            if (original.Fence is { } fence) _effects.RetireOriginal(fence);
        }
        if (original.Errors.Count != 0) throw new AggregateException("Original workspace action and physical cleanup failed.", original.Errors);
        if (result is null) throw new InvalidOperationException("The original workspace body returned no result.");
        // A successful read is an owner-correlated observation, never a mutation receipt/checkpoint.
        var receipt = original.ReadOnly ? null : original.Outcome?.OriginalReceiptReference;
        var readComplete = original.ReadOnly && result.OriginalEffectBodyCompleted && original.Outcome is
            { KnownNoEffect: true, OutcomeUnknown: false, OriginalErrors.Count: 0, Effects.Count: 0 };
        var observed = new TaskRunToolActionResult(result, receipt, readComplete,
            original.Outcome?.KnownNoEffect == true);
        lock (_sync)
        {
            original.Result = observed;
            if (receipt is not null && original.Outcome is { } outcome && original.OriginalInvocation is { } physical &&
                _service is IWorkspaceOriginalInvocationSource source)
                _receipts.RecordOriginal(original.OriginalAttempt, original.ActionId, receipt, physical, outcome, source);
        }
        return observed;
    }
    public async ValueTask RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation preparation,
        TaskExecutionSnapshot acknowledged, CancellationToken token)
    {
        if (preparation is not Preparation original || !IsIssued(original) || original.Result is not { } result ||
            original.OriginalExecution is not { IsCompletedSuccessfully: true } || original.Errors.Count != 0 ||
            result.OriginalResult.OriginalRuntimeError is not null)
            return; // Every failed/unknown original remains retained and never redispatches.
        var taskId = original.OriginalAttempt.Snapshot.TaskId;
        var node = acknowledged.Plan.SingleOrDefault(value => value.ActionId == original.ActionId);
        if (acknowledged.TaskId != taskId || acknowledged.ExecutionId != original.OriginalAttempt.Snapshot.ExecutionId ||
            node is null || node.State != TaskPlanNodeState.Completed || node.OriginalToolIntent != original.OriginalToolIntent ||
            (result.OwnerReceiptReference is { } receipt
                ? node.Acceptance is not { } acceptance || acceptance.AttemptId != original.OriginalAttempt.AttemptId || acceptance.OwnerReceiptReference != receipt
                : !result.ReadOnlyObservationComplete || node.Acceptance is not null))
            throw new UnauthorizedAccessException("Actual acknowledged owner acceptance/read observation required for retirement.");
        var current = await _coordinator().GetAsync(taskId, token).ConfigureAwait(false);
        var retained = current?.Plan.SingleOrDefault(value => value.ActionId == original.ActionId);
        if (current is null || current.ExecutionId != acknowledged.ExecutionId || current.PersistenceRevision < acknowledged.PersistenceRevision ||
            retained is null || retained.State != TaskPlanNodeState.Completed || retained.OriginalToolIntent != node.OriginalToolIntent || retained.Acceptance != node.Acceptance)
            throw new UnauthorizedAccessException("Actual current acknowledged action does not prove terminal custody.");
        if (result.OwnerReceiptReference is { } actualReceipt)
            _receipts.RetireAcknowledgedOriginal(actualReceipt, original.OriginalAttempt, original.ActionId);
        lock (_sync) _prepared.Remove(original);
    }

    public ValueTask ValidateOriginalResultAsync(ITaskRunToolActionPreparation preparation, TaskRunToolActionResult result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (preparation is not Preparation actual || !IsIssued(actual) || actual.Result is not { } recorded || !ReferenceEquals(recorded, result) ||
            !ReferenceEquals(recorded.OriginalResult, result.OriginalResult))
            throw new UnauthorizedAccessException("SAME privately recorded original workspace result required.");
        return ValueTask.CompletedTask;
    }
    private static void Add(List<Exception> errors, Exception original)
    { if (!errors.Any(value => ReferenceEquals(value, original))) errors.Add(original); }
}

/// <summary>Independent native receipt registry consumed by Task admission. It has no coordinator,
/// router, frame owner or physical service constructor dependency; receipt strings alone never grant.</summary>
public sealed class WorkspaceTaskRunReceiptAuthority : ITaskRunActionReceiptAuthority
{
    private sealed record Evidence(TaskRunAttemptAdmission Original, Guid ActionId,
        IWorkspaceOriginalInvocation Invocation, WorkspaceToolPhysicalOutcome Outcome, IWorkspaceOriginalInvocationSource Source);
    private readonly object _sync = new();
    private readonly Dictionary<string, Evidence> _issued = new(StringComparer.Ordinal);
    internal void RecordOriginal(TaskRunAttemptAdmission original, Guid actionId, string receipt,
        IWorkspaceOriginalInvocation invocation, WorkspaceToolPhysicalOutcome outcome, IWorkspaceOriginalInvocationSource source)
    {
        if (outcome.OriginalReceiptReference != receipt || !source.IsIssuedOriginal(invocation) || !source.ValidateOriginalOutcome(invocation, outcome))
            throw new UnauthorizedAccessException("Actual native owner receipt issuance required.");
        lock (_sync)
        {
            if (_issued.Count == 2048 || !_issued.TryAdd(receipt, new(original, actionId, invocation, outcome, source)))
                throw new InvalidOperationException("Retained native receipt custody is full or conflicted.");
        }
    }
    internal void RetireAcknowledgedOriginal(string receipt, TaskRunAttemptAdmission original, Guid actionId)
    {
        lock (_sync)
        {
            if (_issued.TryGetValue(receipt, out var actual) && ReferenceEquals(actual.Original, original) && actual.ActionId == actionId)
                _issued.Remove(receipt);
        }
    }
    public Task ValidateOriginalAsync(TaskExecutionSnapshot snapshot, Guid attemptId, Guid actionId, string receipt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Evidence? actual;
        lock (_sync) actual = _issued.GetValueOrDefault(receipt);
        if (actual is null || actual.Original.Snapshot.TaskId != snapshot.TaskId || actual.Original.Snapshot.ExecutionId != snapshot.ExecutionId ||
            actual.Original.Lease.Owner != snapshot.OwnerBinding || actual.Original.AttemptId != attemptId || actual.ActionId != actionId ||
            !actual.Source.ValidateOriginalOutcome(actual.Invocation, actual.Outcome))
            throw new UnauthorizedAccessException("Actual native owner receipt does not bind this original task action.");
        return Task.CompletedTask;
    }
}
