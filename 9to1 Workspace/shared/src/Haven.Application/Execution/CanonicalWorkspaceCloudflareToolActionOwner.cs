using System.Text.Json;
using Haven.Core;
namespace Haven.Application;

/// <summary>One canonical dispatcher, with fixed owning implementations. Public tool names only
/// select an owner; a private issuing preparation registry determines validation and retirement.</summary>
public sealed class CanonicalWorkspaceCloudflareToolActionOwner(WorkspaceTaskRunToolActionOwner workspace,
    CloudflareTaskRunToolActionOwner cloudflare, CloudflareTypedToolRuntime runtime, ICloudflareSavedServiceSource services) : ITaskRunToolActionOwner, IWorkspaceOriginalProcessStartConsentBindingOwner
{
    private readonly object _sync = new();
    private readonly Dictionary<ITaskRunToolActionPreparation, ITaskRunToolActionOwner> _issued = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ITaskRunToolActionPreparation, Action<Action>?> _callerScopes = new(ReferenceEqualityComparer.Instance);
    public bool SupportsCanonicalInvocation(ToolRuntimeKind kind, string name) => workspace.SupportsCanonicalInvocation(kind, name) || cloudflare.SupportsCanonicalInvocation(kind, name);
    public Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(TaskRunAttemptAdmission attempt, TaskExecutionSnapshot snapshot,
        Guid action, OllamaToolCall call, ToolRuntimeKind kind, PermissionMode intent, string? root, CancellationToken token)
        => PrepareCallerScopedOriginalAsync(attempt, snapshot, action, call, kind, intent, root, null, token);
    public async Task<ITaskRunToolActionPreparation> PrepareCallerScopedOriginalAsync(TaskRunAttemptAdmission attempt, TaskExecutionSnapshot snapshot,
        Guid action, OllamaToolCall call, ToolRuntimeKind kind, PermissionMode intent, string? root, Action<Action>? callback, CancellationToken token)
    {
        var stages = new CloudflareOriginalTaskLedger(); stages.BindOriginalCallerCallback(callback);
        return await stages.RunToOriginalSettlementAsync(async () =>
        {
        ITaskRunToolActionOwner selected = workspace.SupportsCanonicalInvocation(kind, call.Name) ? workspace :
            cloudflare.SupportsCanonicalInvocation(kind, call.Name) ? cloudflare : throw new NotSupportedException("Unknown canonical owner; no fallback execution.");

        var actual = await stages.AwaitAsync(stages.Invoke(() => ReferenceEquals(selected, cloudflare)
            ? cloudflare.PrepareCallerScopedOriginalAsync(attempt, snapshot, action, call, kind, intent, root, callback, token)
            : selected.PrepareOriginalAsync(attempt, snapshot, action, call, kind, intent, root, token))).ConfigureAwait(false);
        lock (_sync)
        {
            if (_issued.Count >= 256) throw new InvalidOperationException("Canonical composite preparation custody is full.");
            if (!_issued.TryAdd(actual, selected)) throw new UnauthorizedAccessException("Preparation was already issued through this dispatcher.");
            _callerScopes.Add(actual, callback);
        }
        return actual;

        }).ConfigureAwait(false);
    }
    private ITaskRunToolActionOwner Require(ITaskRunToolActionPreparation actual)
    { lock (_sync) return _issued.GetValueOrDefault(actual) ?? throw new UnauthorizedAccessException("SAME private issuing-owner preparation required."); }
    public void BindOriginalProcessStartConsent(ITaskRunToolActionPreparation preparation, IWorkspaceOriginalProcessStartConsent consent)
    {
        if (!ReferenceEquals(Require(preparation), workspace))
            throw new UnauthorizedAccessException("A Cloudflare preparation cannot own a Workspace process consent.");
        workspace.BindOriginalProcessStartConsent(preparation, consent);
    }
    public Task<TaskRunToolActionResult> ExecuteOriginalAsync(ITaskRunToolActionPreparation preparation, Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token)
        => Require(preparation).ExecuteOriginalAsync(preparation, body, token);
    public ValueTask ValidateOriginalPreparationAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot current, CancellationToken token)
        => Require(preparation).ValidateOriginalPreparationAsync(preparation, current, token);
    public ValueTask ValidateOriginalResultAsync(ITaskRunToolActionPreparation preparation, TaskRunToolActionResult result, CancellationToken token)
        => Require(preparation).ValidateOriginalResultAsync(preparation, result, token);
    public async ValueTask RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot acknowledged, CancellationToken token)
    {
        var actual = Require(preparation); var stages = new CloudflareOriginalTaskLedger();
        lock (_sync) stages.BindOriginalCallerCallback(_callerScopes.GetValueOrDefault(preparation));
        await stages.AwaitAsync(stages.Invoke(() => actual.RetireAcknowledgedOriginalAsync(preparation, acknowledged, token))).ConfigureAwait(false);
        lock (_sync) { _issued.Remove(preparation); _callerScopes.Remove(preparation); }
    }
    public Task<WorkspaceToolResult> ExecuteOriginalCloudflareRuntimeAsync(OllamaToolCall call,
        ITaskRunToolActionPreparation preparation, CancellationToken token)
        => ExecuteOriginalCloudflareRuntimeAsync(call, preparation, null, token);
    public Task<WorkspaceToolResult> ExecuteOriginalCloudflareRuntimeAsync(OllamaToolCall call,
        ITaskRunToolActionPreparation preparation, Action<Action>? callback, CancellationToken token)
    {
        if (!ReferenceEquals(Require(preparation), cloudflare) || preparation is not ICloudflareOriginalToolPreparation actual)
            throw new UnauthorizedAccessException("Actual fixed CF preparation required at the caller runtime branch.");
        cloudflare.BindOriginalCallerCallback(preparation, callback);
        return actual.RunOriginalRuntimeAsync(runtime, call, token);
    }
    public Task<IReadOnlyList<OllamaToolDefinition>> GetCloudflareDefinitionsAsync(IReadOnlyCollection<ActiveCapability> active, CancellationToken token)
        => GetCloudflareDefinitionsAsync(active, null, token);
    public async Task<IReadOnlyList<OllamaToolDefinition>> GetCloudflareDefinitionsAsync(IReadOnlyCollection<ActiveCapability> active, Action<Action>? callback, CancellationToken token)
    {
        var stages = new CloudflareOriginalTaskLedger(); stages.BindOriginalCallerCallback(callback);
        return await stages.RunToOriginalSettlementAsync<IReadOnlyList<OllamaToolDefinition>>(async () =>
        {
 CloudflareSavedService service;
        try { service = await stages.AwaitAsync(stages.Invoke(() => CloudflareCallerScopedServiceRead.AcquireOriginalAsync(services, callback, token))).ConfigureAwait(false); }
        catch (CloudflareSetupRequiredException) { return []; } // Existing ordinary connection/workspace tools remain unchanged.
        if (!active.Any(x => x.Key.Equals(ExternalConnectionNaming.CapabilityKey(service.Connection.Id), StringComparison.OrdinalIgnoreCase))) return [];
        await stages.AwaitAsync(stages.Invoke(() => CloudflareCallerScopedServiceRead.RevalidateOriginalAsync(services, service, callback, token))).ConfigureAwait(false);
        return Array.AsReadOnly(CloudflareTypedToolCatalogue.Descriptors.Where(x => x.IsImplemented && (x.Kind is not (CloudflareOperationKind.KvMarkerGet or CloudflareOperationKind.KvMarkerVerifyAbsent) || CloudflareRawTaskMarkerTransport.IsSupportedSavedService(service))).Select(x =>
        {
            var ns = x.Kind is not (CloudflareOperationKind.KvList or CloudflareOperationKind.KvCreate);
            return new OllamaToolDefinition(x.ToolName, x.IsReadOnly ? "Inspect the explicitly configured Cloudflare isolated Task resource through Home permissions." :
                "Perform this exact isolated Cloudflare operation after individual Home approval. No DNS, Workers, secrets, Access or billing scope.",
                ns ? new Dictionary<string, object> { ["namespace_id"] = new { type = "string", pattern = "^[a-f0-9]{32}$" } } : new Dictionary<string, object>(),
                ns ? new[] { "namespace_id" } : Array.Empty<string>());
        }).ToArray());

        }).ConfigureAwait(false);
    }
}

/// <summary>Receipt routing only. The selected domain still validates its private actual original.
/// Unknown/unconfigured CF receipts fail; Workspace receipts retain their existing validation.</summary>
public sealed class CanonicalWorkspaceCloudflareReceiptAuthority(WorkspaceTaskRunReceiptAuthority workspace,
    CloudflareTaskRunReceiptAuthority cloudflare) : ITaskRunActionReceiptAuthority
{
    public Task ValidateOriginalAsync(TaskExecutionSnapshot snapshot, Guid attempt, Guid action, string receipt, CancellationToken token) =>
        receipt.StartsWith("cloudflare-native:", StringComparison.Ordinal)
            ? cloudflare.ValidateOriginalAsync(snapshot, attempt, action, receipt, token)
            : workspace.ValidateOriginalAsync(snapshot, attempt, action, receipt, token);
}

/// <summary>Constructor-cycle-free forwarding to the SAME canonical Data issuer. Demand performs
/// no DI lookup or external callback beneath the Home/effect gate.</summary>
public sealed class CloudflareOriginalActionAdmissionSource(Func<TaskExecutionCoordinator> originalCoordinator) : ITaskRunOriginalActionAdmissionSource
{
    private readonly object _sync = new(); private ITaskRunOriginalActionAdmissionSource? _actual;
    private ITaskRunOriginalActionAdmissionSource Resolve()
    {
        var current = (object)originalCoordinator() as ITaskRunOriginalActionAdmissionSource
            ?? throw new InvalidOperationException("The actual canonical original action issuer is not configured.");
        lock (_sync)
        { if (_actual is not null && !ReferenceEquals(_actual, current)) throw new UnauthorizedAccessException("Original action issuer changed."); return _actual ??= current; }
    }
    public TaskRunOriginalActionAdmission RequireOriginalActionAdmission(ITaskRunToolActionPreparation preparation, TaskRunAttemptAdmission attempt)
        => Resolve().RequireOriginalActionAdmission(preparation, attempt);
    public Task ValidateOriginalActionAdmissionAsync(TaskRunOriginalActionAdmission receipt, ITaskRunToolActionPreparation preparation, TaskRunAttemptAdmission attempt, CancellationToken token)
        => Resolve().ValidateOriginalActionAdmissionAsync(receipt, preparation, attempt, token);
    public void DemandOriginalActionAdmission(TaskRunOriginalActionAdmission receipt, ITaskRunToolActionPreparation preparation, TaskRunAttemptAdmission attempt)
    {
        ITaskRunOriginalActionAdmissionSource current; lock (_sync) current = _actual ?? throw new UnauthorizedAccessException("No actual action admission was issued.");
        current.DemandOriginalActionAdmission(receipt, preparation, attempt);
    }
}
