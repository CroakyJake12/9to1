using Haven.Core;
namespace Haven.Application;

/// <summary>Typed app adapter over the existing MCP service. Invoke only from the owning canonical finite tool frame;
/// the normal Task tool owner remains responsible for durable intent, acceptance, replay refusal and whole-frame settlement.</summary>
public sealed class CloudflareTypedToolRuntime(
    Func<TaskExecutionCoordinator> trustedTasks,
    ICloudflareSavedServiceSource services,
    ICloudflareOriginalPermissionSource homePermissions,
    ITaskRunOriginalActionAdmissionSource actionClaims,
    ICloudflareMcpInvocationClient mcp)
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Original> _originals = new(StringComparer.Ordinal);
    private sealed class Original(CloudflareCompiledInvocation invocation, ITaskRunToolActionPreparation preparation)
    {
        internal CloudflareCompiledInvocation Invocation { get; } = invocation;
        internal ITaskRunToolActionPreparation Preparation { get; } = preparation;
        internal TaskRunAttemptAdmission Admission => Preparation.OriginalAttempt;
        internal Task<CloudflareObservedOutcome>? Work;
        internal readonly CloudflareOriginalTaskLedger Stages = new();
        internal CloudflareMcpDispatchResult? Dispatch;
        internal CloudflareObservedOutcome? Outcome;
        internal Task<CloudflareMcpDispatchResult>? ActualDispatch;
        internal ICloudflareOriginalPermission? Permission;
    }
    public Task<CloudflareObservedOutcome> ExecuteOriginalAsync(ITaskRunToolActionPreparation actualPreparation,
        CloudflareCompiledInvocation invocation, CancellationToken token) => ExecuteOriginalAsync(actualPreparation, invocation, null, token);
    internal Task<CloudflareObservedOutcome> ExecuteOriginalAsync(ITaskRunToolActionPreparation actualPreparation,
        CloudflareCompiledInvocation invocation, Action<Action>? callback, CancellationToken token)
    {
        if (!CloudflareTypedToolCatalogue.IsIssuedOriginal(invocation)) throw new UnauthorizedAccessException("Actual compiled typed invocation required.");
        lock (_sync)
        {
            if (_originals.TryGetValue(invocation.OperationKey, out var prior))
            {
                if (!ReferenceEquals(prior.Invocation, invocation) || !ReferenceEquals(prior.Preparation, actualPreparation))
                    throw new UnauthorizedAccessException("A repeated operation key requires the SAME original invocation and admission.");
                prior.Stages.BindOriginalCallerCallback(callback);
                return prior.Work!; // Never create a second remote effect, including unknown completion.
            }
            if (_originals.Count >= 128) throw new InvalidOperationException("Cloudflare original custody capacity reached; retire acknowledged healthy operations first.");
            var original = new Original(invocation, actualPreparation);
            original.Stages.BindOriginalOwner(original); original.Stages.BindOriginalCallerCallback(callback);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            original.Work = RunPublishedAsync(original, begin.Task, token);
            _originals.Add(invocation.OperationKey, original); begin.SetResult(); return original.Work;
        }
    }
    public CloudflareObservedOutcome? TryReadOriginalOutcome(CloudflareCompiledInvocation sameInvocation)
    {
        lock (_sync) return _originals.TryGetValue(sameInvocation.OperationKey, out var original) &&
            ReferenceEquals(original.Invocation, sameInvocation) && original.Work?.IsCompleted == true ? original.Outcome : null;
    }
    public bool IsIssuedOriginalOutcome(CloudflareCompiledInvocation invocation, CloudflareObservedOutcome outcome)
    {
        lock (_sync) return _originals.TryGetValue(invocation.OperationKey, out var original) &&
            ReferenceEquals(original.Invocation, invocation) && ReferenceEquals(original.Outcome, outcome) && original.Work?.IsCompletedSuccessfully == true;
    }
    internal async Task RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation samePreparation,
        CloudflareCompiledInvocation invocation, TaskExecutionSnapshot acknowledged, CancellationToken token)
    {
        Original original;
        lock (_sync) original = _originals.GetValueOrDefault(invocation.OperationKey)
            ?? throw new UnauthorizedAccessException("No original CF custody to retire.");
        CloudflareOriginalExecutionGuard.DemandExternalJoin(original);
        if (!ReferenceEquals(original.Invocation, invocation) || !ReferenceEquals(original.Preparation, samePreparation) ||
            original.Work?.IsCompletedSuccessfully != true || original.Outcome?.Disposition != CloudflareObservedDisposition.ConfirmedResponse ||
            original.Stages.OriginalErrors.Count != 0 || original.Stages.OriginalTasks.Any(x => !x.IsCompletedSuccessfully))
            throw new UnauthorizedAccessException("Only the SAME entirely healthy original can retire after acknowledgment.");
        var current = await original.Stages.AwaitAsync(original.Stages.Invoke(() => trustedTasks().GetAsync(invocation.TaskId, token))).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Canonical CF acknowledgment disappeared.");
        var expected = acknowledged.Plan.SingleOrDefault(x => x.ActionId == invocation.ActionId);
        var actual = current.Plan.SingleOrDefault(x => x.ActionId == invocation.ActionId);
        if (current.ExecutionId != invocation.ExecutionId || current.PersistenceRevision < acknowledged.PersistenceRevision ||
            expected is null || actual is null || actual != expected || actual.State != TaskPlanNodeState.Completed || actual.OriginalToolIntent != samePreparation.OriginalToolIntent)
            throw new UnauthorizedAccessException("Actual matching canonical action CAS acknowledgment required.");
        if (homePermissions is not ICloudflareOriginalPermissionRetirement retirement || original.Permission is null)
            throw new InvalidOperationException("The actual Home original-retirement producer is not configured.");
        await original.Stages.AwaitAsync(original.Stages.Invoke(() => retirement.RetireAcknowledgedOriginalAsync(invocation, original.Permission, token))).ConfigureAwait(false);
        lock (_sync) if (ReferenceEquals(_originals.GetValueOrDefault(invocation.OperationKey), original)) _originals.Remove(invocation.OperationKey);
    }
    private async Task<CloudflareObservedOutcome> RunPublishedAsync(Original original, Task begin, CancellationToken token)
    {
        await begin.ConfigureAwait(false);
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(original);
        ICloudflareOriginalPermission? permission = null;
        CloudflareOriginalTaskBinding? binding = null;
        System.Text.Json.JsonElement? data = null;
        try
        {
            var inv = original.Invocation;
            var task = await original.Stages.AwaitAsync(original.Stages.Invoke(() => trustedTasks().GetAsync(inv.TaskId, token))).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Canonical Task disappeared.");
            var issued = await original.Stages.AwaitAsync(original.Stages.Invoke(() => trustedTasks().GetIssuedAttemptAsync(inv.TaskId, inv.ExecutionId, original.Admission.AttemptId, token))).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The actual current issued attempt is unavailable before dispatch.");
            if (original.Preparation.ActionId != inv.ActionId || !ReferenceEquals(issued, original.Admission) || task.ExecutionId != inv.ExecutionId || task.ContextId != original.Admission.Snapshot.ContextId)
                throw new UnauthorizedAccessException("SAME current issued Task/run/attempt required.");
            var node = task.Plan.SingleOrDefault(x => x.ActionId == inv.ActionId);
            if (node?.OriginalToolIntent is not { } intent || intent.ToolName != inv.Descriptor.ToolName || intent.CallDigest != inv.CallDigest ||
                intent.RuntimeKey != ToolRuntimeKind.Mcp.ToString()) throw new UnauthorizedAccessException("The exact durable typed operation intent must be acknowledged before dispatch.");
            await original.Stages.AwaitAsync(original.Stages.Invoke(() => issued.Lease.RevalidateAsync(token))).ConfigureAwait(false);
            await original.Stages.AwaitAsync(original.Stages.Invoke(() => CloudflareCallerScopedServiceRead.RevalidateOriginalAsync(services, inv.Service, original.Stages.OriginalCallerCallback, token))).ConfigureAwait(false);
            var actionAdmission = original.Stages.Invoke(() => actionClaims.RequireOriginalActionAdmission(original.Preparation, issued));
            await original.Stages.AwaitAsync(original.Stages.Invoke(() => actionClaims.ValidateOriginalActionAdmissionAsync(actionAdmission, original.Preparation, issued, token))).ConfigureAwait(false);
            binding = new(original.Preparation, actionAdmission, task, inv) { OriginalCallerCallback = original.Stages.OriginalCallerCallback };
            permission = await original.Stages.CaptureOriginalAcquisitionAsync(() => homePermissions.AcquireOriginalAsync(binding, token), actual => { permission = actual; original.Permission = actual; }).ConfigureAwait(false);
            original.Permission = permission;
            await original.Stages.AwaitAsync(original.Stages.Invoke(() => homePermissions.ValidateOriginalAsync(binding, permission, token))).ConfigureAwait(false);
            await original.Stages.AwaitAsync(original.Stages.Invoke(() => permission.RevalidateOriginalAsync(binding, token))).ConfigureAwait(false);
            // Re-read after every permission/config await. The held Home entry still owns final remote dispatch currentness.
            var latest = await original.Stages.AwaitAsync(original.Stages.Invoke(() => trustedTasks().GetAsync(inv.TaskId, token))).ConfigureAwait(false);
            var latestIssued = await original.Stages.AwaitAsync(original.Stages.Invoke(() => trustedTasks().GetIssuedAttemptAsync(inv.TaskId, inv.ExecutionId, issued.AttemptId, token))).ConfigureAwait(false);
            if (!ReferenceEquals(latestIssued, issued) || latest?.Plan.SingleOrDefault(x => x.ActionId == inv.ActionId)?.OriginalToolIntent != intent)
                throw new UnauthorizedAccessException("Canonical operation/attempt changed before dispatch.");
            await original.Stages.AwaitAsync(original.Stages.Invoke(() => issued.Lease.RevalidateAsync(token))).ConfigureAwait(false);
            await original.Stages.AwaitAsync(original.Stages.Invoke(() => CloudflareCallerScopedServiceRead.RevalidateOriginalAsync(services, inv.Service, original.Stages.OriginalCallerCallback, token))).ConfigureAwait(false);
            await original.Stages.AwaitAsync(original.Stages.Invoke(() => permission.RevalidateOriginalAsync(binding, token))).ConfigureAwait(false);
            original.Dispatch = await original.Stages.AwaitAsync(original.Stages.Invoke(() => { original.ActualDispatch = mcp.InvokeOriginalAsync(binding, services, actionClaims, permission, token); return original.ActualDispatch; })).ConfigureAwait(false);
            if (!mcp.IsIssuedOriginalResult(inv, original.Dispatch)) throw new UnauthorizedAccessException("SAME MCP original result issuer required.");
            foreach (var error in original.Dispatch.OriginalErrors) original.Stages.Retain(error);
            if (original.Dispatch.Response is { } response) data = CloudflareTypedToolCatalogue.DemandBoundedResponse(inv, response);
            else throw new InvalidOperationException("Remote completion is unknown. Inspect/reconcile; do not replay the operation.");
        }
        catch (Exception error) { original.Stages.Retain(error); }
        finally
        {
            await original.Stages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (original.Dispatch is null && original.ActualDispatch is { IsCompletedSuccessfully: true } returned && mcp.IsIssuedOriginalResult(original.Invocation, returned.Result))
            {
                original.Dispatch = returned.Result;
                foreach (var error in original.Dispatch.OriginalErrors) original.Stages.Retain(error);
                if (original.Dispatch.Response is { } response)
                    try { data = CloudflareTypedToolCatalogue.DemandBoundedResponse(original.Invocation, response); } catch (Exception error) { original.Stages.Retain(error); }
            }
            if (permission is not null && binding is not null && original.Dispatch is not null)
                try { await original.Stages.AwaitAsync(original.Stages.Invoke(() => permission.RecordOriginalOutcomeAsync(binding, original.Dispatch, CancellationToken.None))).ConfigureAwait(false); }
                catch (Exception error) { original.Stages.Retain(error); }
            if (permission is not null)
                try { await original.Stages.AwaitAsync(original.Stages.Invoke(() => permission.DisposeAsync())).ConfigureAwait(false); }
                catch (Exception error) { original.Stages.Retain(error); }
            await original.Stages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        }
        // Data is an actual response observation. Audit/cleanup faults keep the overall outcome uncertain without erasing it.
        var errors = original.Stages.OriginalErrors;
        var disposition = data is not null && errors.Count == 0 ? CloudflareObservedDisposition.ConfirmedResponse :
            original.Dispatch?.DispatchStarted == false || original.Dispatch is null ? CloudflareObservedDisposition.KnownNoDispatch : CloudflareObservedDisposition.Uncertain;
        // A faulting adapter without an issued result may have dispatched; it cannot be called known-no-effect.
        if (original.Dispatch is null && binding is not null && permission is not null) disposition = CloudflareObservedDisposition.Uncertain;
        original.Outcome = new(original.Invocation.OperationKey, disposition, data, errors);
        if (errors.Count != 0) throw new CloudflareOriginalOutcomeException(original.Outcome);
        return original.Outcome;
    }
}
