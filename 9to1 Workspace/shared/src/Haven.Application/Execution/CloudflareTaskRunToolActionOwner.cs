using System.Collections.Frozen;
using System.Text.Json;
using Haven.Core;
namespace Haven.Application;

/// <summary>Actual fixed CF producer inside the canonical tool loop. It invokes the caller's
/// original runtime body once; the existing MCP SDK owns service transport and credentials.</summary>
public sealed class CloudflareTaskRunToolActionOwner : ITaskRunToolActionOwner
{
    private readonly Func<TaskExecutionCoordinator> _tasks;
    private readonly Func<TaskRunPermissionAuthority> _authority;
    private readonly ITaskRunOriginalFrameOwner _frames;
    private readonly CloudflareTypedToolRuntime _runtime;
    private readonly ICloudflareSavedServiceSource _services;
    private readonly ICloudflareOriginalNamespaceOwner _namespaces;
    private readonly ITaskRunOriginalActionAdmissionSource _actions;
    private readonly CloudflareTaskRunReceiptAuthority _receipts;
    private readonly object _sync = new();
    private readonly HashSet<Preparation> _prepared = new(ReferenceEqualityComparer.Instance);
    public CloudflareTaskRunToolActionOwner(Func<TaskExecutionCoordinator> tasks, Func<TaskRunPermissionAuthority> authority,
        ITaskRunOriginalFrameOwner frames, CloudflareTypedToolRuntime runtime, ICloudflareSavedServiceSource services,
        ICloudflareOriginalNamespaceOwner namespaces, ITaskRunOriginalActionAdmissionSource actions, CloudflareTaskRunReceiptAuthority receipts)
    { _tasks = tasks; _authority = authority; _frames = frames; _runtime = runtime; _services = services; _namespaces = namespaces; _actions = actions; _receipts = receipts; }
    public bool SupportsCanonicalInvocation(ToolRuntimeKind runtime, string toolName) => runtime == ToolRuntimeKind.Mcp &&
        CloudflareTypedToolCatalogue.Descriptors.Any(x => x.ToolName == toolName && x.IsImplemented);
    private sealed class Preparation(CloudflareTaskRunToolActionOwner issuer, TaskRunAttemptAdmission admission,
        Guid actionId, OllamaToolCall call, CloudflareCompiledInvocation invocation) : ICloudflareOriginalToolPreparation
    {
        internal CloudflareTaskRunToolActionOwner Issuer { get; } = issuer;
        public TaskRunAttemptAdmission OriginalAttempt { get; } = admission;
        public Guid ActionId { get; } = actionId;
        public CloudflareCompiledInvocation OriginalInvocation { get; } = invocation;
        internal OllamaToolCall OriginalCall { get; } = call;
        public TaskActionInterruptionPolicy InterruptionPolicy => OriginalInvocation.Descriptor.IsReadOnly ? TaskActionInterruptionPolicy.ReadOnlyCancellable : TaskActionInterruptionPolicy.AtomicCommit;
        public IReadOnlyList<string> RequiredPermissionScopes => Array.AsReadOnly(new[] { OriginalInvocation.Descriptor.ActionId, OriginalInvocation.AccountResourceId });
        public TaskOriginalToolIntent OriginalToolIntent { get; } = new(ToolRuntimeKind.Mcp.ToString(), call.Name, null, invocation.CallDigest);
        internal bool RuntimeOpen;
        internal Task<TaskRunToolActionResult>? Execution;
        internal Task<WorkspaceToolResult>? Runtime;
        internal Task<CloudflareObservedOutcome>? Remote;
        internal CloudflareObservedOutcome? Outcome;
        internal WorkspaceToolResult? RuntimeResult;
        internal TaskRunToolActionResult? Result;
        internal readonly CloudflareOriginalTaskLedger Stages = new();
        internal Action<Action>? OriginalCallerCallback;
        public Task<WorkspaceToolResult> RunOriginalRuntimeAsync(CloudflareTypedToolRuntime actualRuntime, OllamaToolCall call, CancellationToken token)
        {
            lock (Issuer._sync)
            {
                if (!ReferenceEquals(actualRuntime, Issuer._runtime) || !RuntimeOpen || !Issuer._prepared.Contains(this) ||
                    WorkspaceToolOriginalDigest.Call(call) != OriginalToolIntent.CallDigest) throw new UnauthorizedAccessException("SAME admitted original CF runtime/call required.");
                if (Runtime is not null) return Runtime;
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Runtime = RuntimePublishedAsync(begin.Task, token); begin.SetResult(); return Runtime;
            }
        }
        private async Task<WorkspaceToolResult> RuntimePublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            // Only this child cohort: the shared preparation ledger also contains the
            // encompassing frame/caller Tasks and must never be joined from this body.
            var runtimeStages = new CloudflareOriginalTaskLedger(); runtimeStages.BindOriginalOwner(this); runtimeStages.BindOriginalCallerCallback(OriginalCallerCallback);
            try
            {
                return await runtimeStages.RunToOriginalSettlementAsync(async () =>
                {
                    Remote = runtimeStages.Invoke(() => { Remote = Issuer._runtime.ExecuteOriginalAsync(this, OriginalInvocation, OriginalCallerCallback, token); return Remote; });
                    Outcome = await runtimeStages.AwaitAsync(Remote).ConfigureAwait(false);
                    if (!Issuer._runtime.IsIssuedOriginalOutcome(OriginalInvocation, Outcome) || Outcome.Disposition != CloudflareObservedDisposition.ConfirmedResponse || Outcome.OriginalErrors.Count != 0)
                        throw new UnauthorizedAccessException("Actual confirmed original fixed SDK/Home result required.");
                    RuntimeResult = new(new ToolActivity(ActionId, OriginalInvocation.Descriptor.ActionId,
                        "Observed the exact typed Cloudflare service outcome.", true, TimeSpan.Zero, DateTimeOffset.UtcNow),
                        JsonSerializer.Serialize(new { operationKey = Outcome.OperationKey, data = Outcome.Data }));
                    return RuntimeResult;
                }).ConfigureAwait(false);
            }
            catch { Outcome = Issuer._runtime.TryReadOriginalOutcome(OriginalInvocation); throw; }
            finally
            {
                foreach (var actual in runtimeStages.OriginalTasks) Stages.Track(actual);
                foreach (var error in runtimeStages.OriginalErrors) Stages.Retain(error);
            }
        }

    }
    private Preparation Require(ITaskRunToolActionPreparation actual)
    {
        lock (_sync)
            return actual is Preparation prepared && ReferenceEquals(prepared.Issuer, this) && _prepared.Contains(prepared)
                ? prepared : throw new UnauthorizedAccessException("Actual private CF preparation required.");
    }
    public Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(TaskRunAttemptAdmission admission, TaskExecutionSnapshot snapshot,
        Guid actionId, OllamaToolCall call, ToolRuntimeKind runtime, PermissionMode permissionIntent, string? originalWorkspaceRoot, CancellationToken token)
        => PrepareCallerScopedOriginalAsync(admission, snapshot, actionId, call, runtime, permissionIntent, originalWorkspaceRoot, null, token);
    public async Task<ITaskRunToolActionPreparation> PrepareCallerScopedOriginalAsync(TaskRunAttemptAdmission admission, TaskExecutionSnapshot snapshot,
        Guid actionId, OllamaToolCall call, ToolRuntimeKind runtime, PermissionMode permissionIntent, string? originalWorkspaceRoot, Action<Action>? callback, CancellationToken token)
    {
        var stages = new CloudflareOriginalTaskLedger(); stages.BindOriginalCallerCallback(callback);
        return await stages.RunToOriginalSettlementAsync(async () =>
        {
        if (!SupportsCanonicalInvocation(runtime, call.Name) || actionId == Guid.Empty || !Enum.IsDefined(permissionIntent)) throw new NotSupportedException("Only fixed registered Cloudflare operations are supported.");
        if (!admission.Lease.Candidate.RequiredCapabilities.Contains(nameof(ToolCapability.Tools), StringComparer.Ordinal)) throw new UnauthorizedAccessException("The actual model attempt did not admit tool capability.");

        var issued = await stages.AwaitAsync(stages.Invoke(() => _tasks().TryGetIssuedAttemptAsync(snapshot.TaskId, snapshot.ExecutionId, admission.AttemptId, token))).ConfigureAwait(false);
        if (!ReferenceEquals(issued, admission) || snapshot.OwnerBinding != admission.Lease.Owner) throw new UnauthorizedAccessException("SAME actual issued Task/Run/attempt required.");
        var current = await stages.AwaitAsync(stages.Invoke(() => _tasks().GetAsync(snapshot.TaskId, token))).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("Current canonical Task disappeared.");
        var descriptor = CloudflareTypedToolCatalogue.Descriptors.Single(x => x.ToolName == call.Name);
        if (!descriptor.IsReadOnly && current.Plan.Any(x => x.State == TaskPlanNodeState.RequiresReexecution &&
            x.InterruptionPolicy is TaskActionInterruptionPolicy.AtomicCommit or TaskActionInterruptionPolicy.SafeBoundary)) throw new UnauthorizedAccessException("An unresolved actual owner effect requires reconciliation before another mutation.");
        await stages.AwaitAsync(stages.Invoke(() => _authority().ValidateOriginalToolAsync(admission, call.Name, token))).ConfigureAwait(false);
        var service = await stages.AwaitAsync(stages.Invoke(() => CloudflareCallerScopedServiceRead.AcquireOriginalAsync(_services, callback, token))).ConfigureAwait(false);
        var captured = call with { Arguments = call.Arguments.ToFrozenDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal) };
        var compiled = CloudflareTypedToolCatalogue.CompileOriginal(service, snapshot.TaskId, snapshot.ExecutionId, actionId, captured);
        await stages.AwaitAsync(stages.Invoke(() => _namespaces.DemandOriginalNamespaceAsync(compiled, token))).ConfigureAwait(false);
        var preparation = new Preparation(this, admission, actionId, captured, compiled) { OriginalCallerCallback = callback };
        preparation.Stages.BindOriginalCallerCallback(callback);
        foreach (var task in stages.OriginalTasks) preparation.Stages.Track(task);
        foreach (var error in stages.OriginalErrors) preparation.Stages.Retain(error);
        lock (_sync)
        { if (_prepared.Count >= 128) throw new InvalidOperationException("CF original preparation custody is full."); _prepared.Add(preparation); }
        await preparation.Stages.AwaitAsync(preparation.Stages.Invoke(() => _frames.RegisterOriginalAttemptAsync(admission, token))).ConfigureAwait(false);
        return preparation;

        }).ConfigureAwait(false);
    }
    internal void BindOriginalCallerCallback(ITaskRunToolActionPreparation actual, Action<Action>? callback)
    {
        var original = Require(actual); original.Stages.BindOriginalCallerCallback(callback);
        lock (_sync)
        {
            if (original.OriginalCallerCallback is not null && callback is not null && !original.OriginalCallerCallback.Equals(callback))
                throw new UnauthorizedAccessException("Original caller lifetime scope changed.");
            original.OriginalCallerCallback ??= callback;
        }
    }
    public ValueTask ValidateOriginalPreparationAsync(ITaskRunToolActionPreparation original, TaskExecutionSnapshot snapshot, CancellationToken token)
        => new(ValidatePreparationAsync(Require(original), snapshot, token));
    private async Task ValidatePreparationAsync(Preparation original, TaskExecutionSnapshot snapshot, CancellationToken token)
    {
        if (snapshot.TaskId != original.OriginalAttempt.Snapshot.TaskId || snapshot.ExecutionId != original.OriginalAttempt.Snapshot.ExecutionId || snapshot.OwnerBinding != original.OriginalAttempt.Lease.Owner)
            throw new UnauthorizedAccessException("Original canonical CF preparation tuple changed.");
        var actual = await original.Stages.AwaitAsync(original.Stages.Invoke(() => _tasks().TryGetIssuedAttemptAsync(snapshot.TaskId, snapshot.ExecutionId, original.OriginalAttempt.AttemptId, token))).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Current original CF attempt is unavailable.");
        if (!ReferenceEquals(actual, original.OriginalAttempt)) throw new UnauthorizedAccessException("Original CF attempt retired.");
        await original.Stages.AwaitAsync(original.Stages.Invoke(() => _authority().ValidateOriginalToolAsync(actual, original.OriginalCall.Name, token))).ConfigureAwait(false);
        await original.Stages.AwaitAsync(original.Stages.Invoke(() => CloudflareCallerScopedServiceRead.RevalidateOriginalAsync(_services, original.OriginalInvocation.Service, original.OriginalCallerCallback, token))).ConfigureAwait(false);
        await original.Stages.AwaitAsync(original.Stages.Invoke(() => _namespaces.DemandOriginalNamespaceAsync(original.OriginalInvocation, token))).ConfigureAwait(false);
    }
    public Task<TaskRunToolActionResult> ExecuteOriginalAsync(ITaskRunToolActionPreparation preparation, Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token)
    {
        var original = Require(preparation); ArgumentNullException.ThrowIfNull(body);
        lock (_sync)
        {
            if (original.Execution is not null) return original.Execution;
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            original.Stages.BindOriginalOwner(original);
            original.Execution = ExecutePublishedAsync(original, begin.Task, body, token); begin.SetResult(); return original.Execution;
        }
    }
    private async Task<TaskRunToolActionResult> ExecutePublishedAsync(Preparation original, Task begin, Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(original);
        var receipt = original.Stages.Invoke(() => _actions.RequireOriginalActionAdmission(original, original.OriginalAttempt));
        await original.Stages.AwaitAsync(original.Stages.Invoke(() => _actions.ValidateOriginalActionAdmissionAsync(receipt, original, original.OriginalAttempt, token))).ConfigureAwait(false);
        var snapshot = await original.Stages.AwaitAsync(original.Stages.Invoke(() => _tasks().GetAsync(original.OriginalAttempt.Snapshot.TaskId, token))).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("Canonical CF action disappeared.");
        await ValidatePreparationAsync(original, snapshot, token).ConfigureAwait(false);
        var frame = original.Stages.Invoke(() => _frames.StartOriginalToolFrameAsync(original.OriginalAttempt, async finiteToken =>
        {
            lock (_sync) original.RuntimeOpen = true;
            try
            {
                var raw = original.Stages.Invoke(() => CloudflareOriginalExecutionGuard.InvokeOriginal(original, () => body(finiteToken)));
                var returned = await original.Stages.AwaitAsync(raw).ConfigureAwait(false);
                if (!ReferenceEquals(returned, original.RuntimeResult) || original.Runtime?.IsCompletedSuccessfully != true || original.Remote?.IsCompletedSuccessfully != true ||
                    original.Outcome is null || !_runtime.IsIssuedOriginalOutcome(original.OriginalInvocation, original.Outcome))
                    throw new UnauthorizedAccessException("Caller body did not return the SAME original fixed CF runtime result.");
                var observed = new TaskRunToolActionResult(returned, original.OriginalInvocation.Descriptor.IsReadOnly ? null :
                    _receipts.RecordOriginal(original.OriginalAttempt, original.ActionId, original.OriginalInvocation, _runtime, original.Remote, original.Outcome),
                    original.OriginalInvocation.Descriptor.IsReadOnly, false);
                original.Result = observed; return observed;
            }
            finally { lock (_sync) original.RuntimeOpen = false; }
        }, token));
        return await original.Stages.AwaitAsync(frame).ConfigureAwait(false);
    }
    public ValueTask ValidateOriginalResultAsync(ITaskRunToolActionPreparation preparation, TaskRunToolActionResult result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var original = Require(preparation);
        if (!ReferenceEquals(result, original.Result) || !ReferenceEquals(result.OriginalResult, original.RuntimeResult) || original.Execution?.IsCompletedSuccessfully != true ||
            original.Remote?.IsCompletedSuccessfully != true || original.Outcome is null || !_runtime.IsIssuedOriginalOutcome(original.OriginalInvocation, original.Outcome))
            throw new UnauthorizedAccessException("SAME actual completed canonical CF result required.");
        return ValueTask.CompletedTask;
    }
    public async ValueTask RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot acknowledged, CancellationToken token)
    {
        var original = Require(preparation); CloudflareOriginalExecutionGuard.DemandExternalJoin(original);
        if (original.Execution is null) throw new UnauthorizedAccessException("No original CF result to retire.");
        await original.Stages.AwaitAsync(original.Execution).ConfigureAwait(false);
        await ValidateOriginalResultAsync(original, original.Result!, token).ConfigureAwait(false);
        var node = acknowledged.Plan.SingleOrDefault(x => x.ActionId == original.ActionId);
        if (acknowledged.TaskId != original.OriginalAttempt.Snapshot.TaskId || acknowledged.ExecutionId != original.OriginalAttempt.Snapshot.ExecutionId || node is null || node.OriginalToolIntent != original.OriginalToolIntent ||
            node.State != TaskPlanNodeState.Completed || original.Result!.OwnerReceiptReference is { } receipt && node.Acceptance?.OwnerReceiptReference != receipt)
            throw new UnauthorizedAccessException("Actual matching canonical action acknowledgment required before retirement.");
        await original.Stages.AwaitAsync(original.Stages.Invoke(() => _runtime.RetireAcknowledgedOriginalAsync(original, original.OriginalInvocation, acknowledged, token))).ConfigureAwait(false);
        if (original.Result.OwnerReceiptReference is { } actualReceipt) _receipts.RetireOriginal(actualReceipt, original.OriginalAttempt, original.ActionId);
        // Unknown/faulted originals never reach this point and remain retained for recovery.
        lock (_sync) _prepared.Remove(original);
    }
}

public sealed class CloudflareTaskRunReceiptAuthority : ITaskRunActionReceiptAuthority
{
    private sealed record Evidence(TaskRunAttemptAdmission Attempt, Guid Action, CloudflareCompiledInvocation Invocation,
        CloudflareTypedToolRuntime Runtime, Task<CloudflareObservedOutcome> Work, CloudflareObservedOutcome Outcome);
    private readonly object _sync = new(); private readonly Dictionary<string, Evidence> _issued = new(StringComparer.Ordinal);
    internal string RecordOriginal(TaskRunAttemptAdmission attempt, Guid action, CloudflareCompiledInvocation invocation,
        CloudflareTypedToolRuntime runtime, Task<CloudflareObservedOutcome> work, CloudflareObservedOutcome outcome)
    {
        if (!work.IsCompletedSuccessfully || !ReferenceEquals(work.Result, outcome) || !runtime.IsIssuedOriginalOutcome(invocation, outcome) ||
            outcome.Disposition != CloudflareObservedDisposition.ConfirmedResponse || outcome.OriginalErrors.Count != 0 || invocation.Descriptor.IsReadOnly)
            throw new UnauthorizedAccessException("Actual original SDK/Home confirmed mutation required.");
        var receipt = "cloudflare-native:" + Guid.NewGuid().ToString("N");
        lock (_sync) { if (_issued.Count >= 2048) throw new InvalidOperationException("CF receipt custody is full."); _issued.Add(receipt, new(attempt, action, invocation, runtime, work, outcome)); }
        return receipt;
    }
    public Task ValidateOriginalAsync(TaskExecutionSnapshot snapshot, Guid attemptId, Guid actionId, string receipt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Evidence? original; lock (_sync) original = _issued.GetValueOrDefault(receipt);
        if (original is null || original.Attempt.Snapshot.TaskId != snapshot.TaskId || original.Attempt.Snapshot.ExecutionId != snapshot.ExecutionId ||
            original.Attempt.AttemptId != attemptId || original.Action != actionId || original.Attempt.Lease.Owner != snapshot.OwnerBinding ||
            !original.Work.IsCompletedSuccessfully || !ReferenceEquals(original.Work.Result, original.Outcome) || !original.Runtime.IsIssuedOriginalOutcome(original.Invocation, original.Outcome))
            throw new UnauthorizedAccessException("Actual original CF owner receipt does not bind this action.");
        return Task.CompletedTask;
    }
    internal void RetireOriginal(string receipt, TaskRunAttemptAdmission attempt, Guid action)
    { lock (_sync) if (_issued.TryGetValue(receipt, out var original) && ReferenceEquals(original.Attempt, attempt) && original.Action == action) _issued.Remove(receipt); }
}
