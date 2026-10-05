using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Actual central approval, independent of route/configuration/credential eligibility.
/// The scope contains no secret and is tied to one task/run/exact provider and model. Costs and
/// balances remain unknown; this approval is not a reservation, cap or Home/domain grant.</summary>
public interface ITaskRunCloudUsePermissionSource
{
    ValueTask<ITaskRunCloudUsePermissionLease> AcquireOriginalAsync(
        TaskExecutionOwnerBinding actualOwner, TaskRunRouteCandidate actualCandidate,
        CancellationToken cancellationToken);
}

public interface ITaskRunCloudUsePermissionLease : IAsyncDisposable
{
    string Scope { get; }
    ValueTask RevalidateAsync(CancellationToken cancellationToken);
    T RunOriginalInvocation<T>(Func<T> originalRawStart);
}

/// <summary>Preserves the existing policy's actual structured Ask/Denied result. Only its owning
/// approval UI/action may issue the exact scope grant; this source never grants permission.</summary>
public sealed class TaskRunCloudPermissionRequiredException(PermissionDecision originalDecision)
    : UnauthorizedAccessException(originalDecision.Reason)
{
    public PermissionDecision OriginalDecision { get; } = originalDecision;
}

/// <summary>
/// Reuses ONE concrete central engine so Evaluate and finite invocation admission share the SAME
/// Grant/Revoke/SetPolicy gate. Actor observations are checked asynchronously before admission;
/// no actor/provider/repository callback runs under that gate. The callback starts the original
/// finite Task/stream iteration and returns immediately; never await, enumerate, perform observer
/// work or change policy within it. Later revocation denies new starts but cannot undo an already
/// admitted external effect or guarantee a network exclusion interval.
/// </summary>
public sealed class TaskRunCentralCloudUsePermissionSource : ITaskRunCloudUsePermissionSource
{
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly PermissionDecisionEngine _policy;

    public TaskRunCentralCloudUsePermissionSource(IAuthenticatedResourceActorSource taskActors,
        PermissionDecisionEngine sameCentralPolicy)
    {
        _actors = taskActors ?? throw new ArgumentNullException(nameof(taskActors));
        _policy = sameCentralPolicy ?? throw new ArgumentNullException(nameof(sameCentralPolicy));
    }

    public static string ScopeFor(TaskExecutionOwnerBinding owner, TaskRunRouteCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(candidate);
        if (owner.TaskId == Guid.Empty || owner.ExecutionId == Guid.Empty || owner.ContextId == Guid.Empty)
            throw new ArgumentException("The actual task/run/context identity is incomplete.", nameof(owner));
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.ModelId);
        var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new[] { candidate.ProviderId, candidate.ModelId })));
        var actor = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { owner.ActorId, owner.ProfileId, owner.AccountId, owner.OrganisationId, owner.AuthenticationRevision })));
        // Case-sensitive IDs become a case-stable digest under the engine's case-insensitive scope comparer.
        return $"task:{owner.TaskId:D}:run:{owner.ExecutionId:D}:actor:{actor}:remote-model:{digest}";
    }

    public async ValueTask<ITaskRunCloudUsePermissionLease> AcquireOriginalAsync(
        TaskExecutionOwnerBinding owner, TaskRunRouteCandidate candidate, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.UsesCloud) throw new ArgumentException("A remote-use scope cannot authorize a local route.", nameof(candidate));
        var scope = ScopeFor(owner, candidate);
        await DemandActorAsync(owner, token).ConfigureAwait(false);
        DemandPermission(scope, Reason(candidate));
        token.ThrowIfCancellationRequested();
        return new Lease(this, owner, scope, Reason(candidate));
    }

    private static string Reason(TaskRunRouteCandidate candidate) =>
        $"Approve this task/run's remote model use: {candidate.ProviderId}/{candidate.ModelId}. Current cost and monetary budget are unknown.";

    private void DemandPermission(string scope, string reason)
    {
        var decision = _policy.Evaluate(scope, CapabilityRiskClass.Consequential, requiresPermission: true, reason);
        if (decision.Kind != PermissionDecisionKind.Allowed)
            throw new TaskRunCloudPermissionRequiredException(decision);
    }

    private async ValueTask DemandActorAsync(TaskExecutionOwnerBinding owner, CancellationToken token)
    {
        var current = await _actors.GetCurrentAsync(token).ConfigureAwait(false);
        var expected = new AuthenticatedResourceActor(owner.ActorId, owner.ProfileId, owner.AccountId,
            owner.OrganisationId, owner.AuthenticationRevision);
        if (current != expected) throw new UnauthorizedAccessException("The actual task actor changed before remote admission.");
        token.ThrowIfCancellationRequested();
    }

    private sealed class Lease : ITaskRunCloudUsePermissionLease
    {
        private readonly TaskRunCentralCloudUsePermissionSource _source;
        private readonly TaskExecutionOwnerBinding _owner;
        private readonly string _reason;
        private readonly object _sync = new();
        private bool _closed;
        public string Scope { get; }
        public Lease(TaskRunCentralCloudUsePermissionSource source, TaskExecutionOwnerBinding owner, string scope, string reason)
        { _source = source; _owner = owner; Scope = scope; _reason = reason; }

        public async ValueTask RevalidateAsync(CancellationToken token)
        {
            lock (_sync) ObjectDisposedException.ThrowIf(_closed, this);
            await _source.DemandActorAsync(_owner, token).ConfigureAwait(false);
            _source.DemandPermission(Scope, _reason);
            token.ThrowIfCancellationRequested();
            lock (_sync) ObjectDisposedException.ThrowIf(_closed, this);
        }

        public T RunOriginalInvocation<T>(Func<T> originalRawStart)
        {
            ArgumentNullException.ThrowIfNull(originalRawStart);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                // Pure scope/policy fence only. The actual raw returned Task is neither wrapped nor replaced.
                return _source._policy.RunOriginalEffect(Scope, CapabilityRiskClass.Consequential,
                    requiresPermission: true, _reason, originalRawStart);
            }
        }

        public ValueTask DisposeAsync() { lock (_sync) _closed = true; return ValueTask.CompletedTask; }
    }
}
