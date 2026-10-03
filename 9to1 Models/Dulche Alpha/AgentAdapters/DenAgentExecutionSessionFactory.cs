using Haven.Application;
using NineToOne.Dulche.Den;

namespace Dulche.Runtime.Agents;

/// <summary>Trusted host composition over the original admitted ExecutionDen. All canonical
/// definitions/runs stay in its SAME Store; the adapter invokes the already registered Chat loop.
/// Constructor arguments are trusted registrations, never a public wire/client authority API.</summary>
public sealed class DenAgentExecutionSessionFactory : IDenAgentExecutionSessionFactory
{
    private readonly DulcheDen _den;
    private readonly DenAgentReference _reference;
    private readonly AgentInvocationContext _originalContext;
    private readonly CancellationToken _originalLifetime;
    private readonly IAgentPermissionBroker _permissions;
    private readonly IDenAgentCurrentRuntimeContextSource _host;
    private readonly DenPersistentAgentCatalog _catalog;
    private readonly DenAgentExecutionStateStore _state;
    private readonly AgentExecutionService _coordinator;
    private readonly OriginalStateBoundary _coordinatorState;
    private readonly Session _session;

    public DenAgentExecutionSessionFactory(DulcheDen originalAdmittedExecutionDen, DenAgentReference reference,
        AgentInvocationContext originalContext, CancellationToken originalLifetime,
        IAgentPermissionBroker originalHomePermissions, IDenAgentCurrentRuntimeContextSource originalHomeContext,
        AgentDependencyCatalogService dependencies, IModelProviderRegistry providers, IModelRouter router,
        AgentTaskRuntimeService originalRegisteredChatRuntime)
    {
        ArgumentNullException.ThrowIfNull(originalAdmittedExecutionDen); ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(originalContext);
        if (!originalLifetime.CanBeCanceled || originalLifetime.IsCancellationRequested)
            throw new UnauthorizedAccessException("The original admitted host lifetime is unavailable.");
        _den = originalAdmittedExecutionDen; _reference = reference; _originalContext = originalContext;
        _originalLifetime = originalLifetime; _permissions = originalHomePermissions; _host = originalHomeContext;
        _catalog = new(_den, reference.NamespaceId); _state = new(_den, reference.NamespaceId);
        var validator = new DenAgentRuntimeValidator(_den, reference.NamespaceId, dependencies, providers, router);
        var adapter = new DenAgentChatExecutionAdapter(reference, _catalog, validator, _host, _state,
            _permissions, originalRegisteredChatRuntime);
        _coordinatorState = new(this, _state);
        _coordinator = new(_coordinatorState, _catalog, _permissions, adapter);
        _session = new(this);
    }

    public async ValueTask<AgentResult<IDenAgentExecutionSession>> OpenCurrentAsync(DenAgentReference reference,
        AgentInvocationContext originalContext, CancellationToken originalLifetime,
        CancellationToken cancellationToken = default)
    {
        if (reference != _reference || !ReferenceEquals(originalContext, _originalContext) ||
            originalLifetime != _originalLifetime || !originalLifetime.CanBeCanceled || originalLifetime.IsCancellationRequested)
            return Refuse<IDenAgentExecutionSession>(reference.AgentId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_originalLifetime, cancellationToken);
        var failure = await RequireCurrentAsync(linked.Token).ConfigureAwait(false);
        if (failure is not null) return AgentResult<IDenAgentExecutionSession>.Failure(failure);
        return AgentResult<IDenAgentExecutionSession>.Success(_session);
    }

    private async ValueTask<AgentFailure?> RequireCurrentAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var authority = await _den.Store.ReadAuthoritySnapshotAsync(token).ConfigureAwait(false);
        if (authority.DenId != _reference.DenId || !authority.Namespaces.Any(value => value.Id == _reference.NamespaceId))
            return Denied(_reference.AgentId);
        var projection = await _catalog.GetProjectionAsync(_reference, token).ConfigureAwait(false);
        if (projection.Error is { } projectionFailure) return projectionFailure;
        if (!projection.Value!.Execution.Enabled) return new(AgentFailureCode.AgentDisabled,
            "The current canonical Agent is disabled.", _reference.AgentId);
        var current = await _host.GetForInvocationAsync(_reference, _originalContext, token).ConfigureAwait(false);
        if (current.Error is { } contextFailure) return contextFailure;
        if (current.Value is not { } observed || observed.Reference != _reference ||
            !Guid.TryParseExact(_reference.AgentId, "D", out var agentId) ||
            observed.Scope != "agent:" + agentId.ToString("N")) return Denied(_reference.AgentId);
        var capabilities = await _permissions.ResolveCapabilitiesAsync(projection.Value.Execution.CapabilityPolicy,
            _originalContext, token).ConfigureAwait(false);
        if (capabilities.Error is { } permissionFailure) return permissionFailure;
        if (capabilities.Value is null || !capabilities.Value.IsSubsetOf(projection.Value.Execution.CapabilityPolicy.AllowedCapabilities))
            return Denied(_reference.AgentId);
        if (!await _den.AccessPolicy.IsAllowedAsync(_den.PrincipalId, _reference.NamespaceId, _reference.AgentId,
            DenPermission.Execute, token).ConfigureAwait(false)) return Denied(_reference.AgentId);
        var finalProjection = await _catalog.GetProjectionAsync(_reference, token).ConfigureAwait(false);
        if (finalProjection.Error is { } revisionFailure) return revisionFailure;
        var finalHost = await _host.GetForInvocationAsync(_reference, _originalContext, token).ConfigureAwait(false);
        if (finalHost.Error is { } finalFailure) return finalFailure;
        if (finalHost.Value != current.Value) return new(AgentFailureCode.RevisionConflict,
            "The original current host changed during session admission.", _reference.AgentId, Retryable: false);
        token.ThrowIfCancellationRequested(); return null;
    }

    private sealed class Session(DenAgentExecutionSessionFactory owner) : IDenAgentExecutionSession
    {
        private readonly object _startGate = new();
        private AgentRunRequest? _originalRequest;
        private Task<AgentResult<AgentExecutionSnapshot>>? _originalStart;
        private OriginalRoot? _originalRoot;
        // Immutable scalar provenance captured from the actual returned owning Start result;
        // caller-visible mutable collections never replace the original attempt/session binding.
        private sealed record OriginalRoot(string RunId, string AgentId, long DefinitionRevision,
            string AttemptId, string? SessionId, string CallerId, string SurfaceId,
            string? ProviderId, string? ModelId, string? EndpointId);

        internal bool MatchesOriginal(AgentRunSnapshot run)
        {
            if (run.AgentId != Reference.AgentId || run.DefinitionRevision != Reference.DefinitionRevision ||
                run.CallerId != owner._originalContext.CallerId || run.SurfaceId != owner._originalContext.SurfaceId) return false;
            var original = Volatile.Read(ref _originalRoot);
            return original is null || run.AgentRunId == original.RunId && run.CurrentAttemptId == original.AttemptId &&
                run.SessionId == original.SessionId && run.ProviderId == original.ProviderId &&
                run.ModelId == original.ModelId && run.EndpointId == original.EndpointId;
        }

        public DenAgentReference Reference => owner._reference;

        // SAME original request joins the SAME published original Task. Other copies require
        // fresh Home admission; failures with unknown/known creation are never blindly retried.
        public ValueTask<AgentResult<AgentExecutionSnapshot>> StartAsync(AgentRunRequest originalRequest,
            CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(originalRequest.Context, owner._originalContext) || originalRequest.AgentId != Reference.AgentId ||
                originalRequest.DefinitionRevision != Reference.DefinitionRevision) return ValueTask.FromResult(Refuse<AgentExecutionSnapshot>(Reference.AgentId));
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<AgentResult<AgentExecutionSnapshot>> original;
            lock (_startGate)
            {
                if (_originalStart is not null) return ReferenceEquals(_originalRequest, originalRequest)
                    ? new(_originalStart) : ValueTask.FromResult(Refuse<AgentExecutionSnapshot>(Reference.AgentId));
                _originalRequest = originalRequest;
                original = StartOriginalAsync(start.Task, originalRequest, cancellationToken);
                _originalStart = original;
            }
            start.SetResult(); return new(original);
        }
        private async Task<AgentResult<AgentExecutionSnapshot>> StartOriginalAsync(Task start, AgentRunRequest request, CancellationToken caller)
        {
            await start.ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner._originalLifetime, caller);
            var failure = await owner.RequireCurrentAsync(linked.Token).ConfigureAwait(false);
            if (failure is not null) return AgentResult<AgentExecutionSnapshot>.Failure(failure);
            var result = await owner._coordinator.StartAsync(request, linked.Token).ConfigureAwait(false);
            // A returned actual committed snapshot is retained before any further observation.
            // No final cancellation check can erase that known creation/refusal receipt.
            if (result.Value is { } actual) Volatile.Write(ref _originalRoot, new(actual.Run.AgentRunId,
                actual.Run.AgentId, actual.Run.DefinitionRevision, actual.Run.CurrentAttemptId, actual.Run.SessionId,
                actual.Run.CallerId, actual.Run.SurfaceId, actual.Run.ProviderId, actual.Run.ModelId, actual.Run.EndpointId));
            return result;
        }
        public ValueTask<AgentResult<AgentExecutionSnapshot>> GetAsync(string originalRunId, CancellationToken cancellationToken = default) =>
            WithOriginalRunAsync(originalRunId, owner._coordinator.GetAsync, true, cancellationToken);
        public ValueTask<AgentResult<AgentExecutionSnapshot>> ExecuteNextStepAsync(string originalRunId, CancellationToken cancellationToken = default) =>
            WithOriginalRunAsync(originalRunId, owner._coordinator.ExecuteNextStepAsync, false, cancellationToken);
        public ValueTask<AgentResult<AgentExecutionSnapshot>> PauseAsync(string originalRunId, CancellationToken cancellationToken = default) =>
            WithOriginalRunAsync(originalRunId, owner._coordinator.PauseAsync, false, cancellationToken);
        public ValueTask<AgentResult<AgentExecutionSnapshot>> StopAsync(string originalRunId, CancellationToken cancellationToken = default) =>
            WithOriginalRunAsync(originalRunId, owner._coordinator.StopAsync, false, cancellationToken);
        public ValueTask<AgentResult<AgentExecutionSnapshot>> CancelAsync(string originalRunId, CancellationToken cancellationToken = default) =>
            WithOriginalRunAsync(originalRunId, owner._coordinator.CancelAsync, false, cancellationToken);
        public ValueTask<AgentResult<AgentExecutionSnapshot>> ResumeAsync(string originalRunId, CancellationToken cancellationToken = default) =>
            WithOriginalRunAsync(originalRunId, owner._coordinator.ResumeAsync, false, cancellationToken);
        public ValueTask<AgentResult<AgentExecutionSnapshot>> RecoverAsync(string originalRunId, CancellationToken cancellationToken = default) =>
            WithOriginalRunAsync(originalRunId, owner._coordinator.RecoverAsync, false, cancellationToken);

        private async ValueTask<AgentResult<AgentExecutionSnapshot>> WithOriginalRunAsync(string runId,
            Func<string, CancellationToken, ValueTask<AgentResult<AgentExecutionSnapshot>>> owningOperation,
            bool readOnly, CancellationToken caller)
        {
            var original = Volatile.Read(ref _originalRoot);
            if (original is null || original.RunId != runId) return Refuse<AgentExecutionSnapshot>(runId);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner._originalLifetime, caller);
            var token = linked.Token;
            var failure = await owner.RequireCurrentAsync(token).ConfigureAwait(false);
            if (failure is not null) return AgentResult<AgentExecutionSnapshot>.Failure(failure);
            var actual = await owner._state.ReadAsync(runId, token).ConfigureAwait(false);
            if (actual is null || actual.Run.AgentId != Reference.AgentId || actual.Run.DefinitionRevision != Reference.DefinitionRevision ||
                actual.Run.CallerId != owner._originalContext.CallerId || actual.Run.SurfaceId != owner._originalContext.SurfaceId ||
                actual.Run.CurrentAttemptId != original.AttemptId || actual.Run.SessionId != original.SessionId ||
                actual.Run.ProviderId != original.ProviderId || actual.Run.ModelId != original.ModelId || actual.Run.EndpointId != original.EndpointId)
                return Refuse<AgentExecutionSnapshot>(runId);
            failure = await owner.RequireCurrentAsync(token).ConfigureAwait(false);
            if (failure is not null) return AgentResult<AgentExecutionSnapshot>.Failure(failure);
            try
            {
                var result = await owningOperation(runId, token).ConfigureAwait(false);
                if (readOnly)
                {
                    failure = await owner.RequireCurrentAsync(token).ConfigureAwait(false);
                    if (failure is not null) return AgentResult<AgentExecutionSnapshot>.Failure(failure);
                    var published = await owner._coordinatorState.ReadAsync(runId, token).ConfigureAwait(false);
                    if (result.Value is { } value && (published is null || !MatchesOriginal(value.Run) || value.Revision != published.Revision))
                        return AgentResult<AgentExecutionSnapshot>.Failure(new(AgentFailureCode.RevisionConflict,
                            "The original read snapshot changed before its current publication.", runId, Retryable: false));
                }
                // Mutations return their original owning result/known-commit receipt unchanged.
                return result;
            }
            catch (OriginalBoundaryRefusal) { return Refuse<AgentExecutionSnapshot>(runId); }
        }
    }
    // The existing coordinator must see only the SAME private original root/attempt/session.
    // This owning adapter adds no persistence or registry: reads delegate to the real Den state,
    // and commits retain its exact ExpectedRevision CAS. Post-read rotation cannot be authorized
    // by a second latest snapshot or overwrite its original revision.
    private sealed class OriginalBoundaryRefusal : UnauthorizedAccessException { }
    private sealed class OriginalStateBoundary(DenAgentExecutionSessionFactory owner,
        DenAgentExecutionStateStore original) : IAgentExecutionStateStore
    {
        public async ValueTask<AgentExecutionSnapshot?> ReadAsync(string runId, CancellationToken token = default)
        {
            var actual = await original.ReadAsync(runId, token).ConfigureAwait(false);
            if (actual is not null && !owner._session.MatchesOriginal(actual.Run)) throw new OriginalBoundaryRefusal();
            return actual;
        }
        public async ValueTask<IReadOnlyList<AgentRunSnapshot>> ListRunsAsync(string agentId, int limit, CancellationToken token = default)
        {
            if (agentId != owner._reference.AgentId) throw new OriginalBoundaryRefusal();
            var actual = await original.ListRunsAsync(agentId, limit, token).ConfigureAwait(false);
            // This internal full Agent index is the owning coordinator's idempotency
            // collision observation. Hiding a foreign invocation would authorize a new
            // creation under its existing key. Public reads/commits remain original-bound.
            return actual;
        }
        public async ValueTask<AgentExecutionSnapshot> CommitAsync(AgentExecutionChangeSet change, CancellationToken token = default)
        {
            var current = await original.ReadAsync(change.AgentRunId, token).ConfigureAwait(false);
            if (current is not null && !owner._session.MatchesOriginal(current.Run) ||
                change.Run is not null && !owner._session.MatchesOriginal(change.Run)) throw new OriginalBoundaryRefusal();
            // No returned known mutation receipt is hidden by an additional post-commit await.
            // The real original store checks ExpectedRevision atomically after any later race.
            return await original.CommitAsync(change, token).ConfigureAwait(false);
        }
    }

    private static AgentFailure Denied(string target) => new(AgentFailureCode.PermissionDenied,
        "The SAME original Home context and current canonical Agent session are required.", target);
    private static AgentResult<T> Refuse<T>(string target) => AgentResult<T>.Failure(Denied(target));
}
