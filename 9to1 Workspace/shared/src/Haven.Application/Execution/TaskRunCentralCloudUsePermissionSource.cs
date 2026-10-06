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
    // Null on legacy/caller-created exceptions. Only this source issues the opaque reference.
    public ITaskRunCloudPermissionOriginalRequest? OriginalRequest { get; internal init; }
}

/// <summary>
/// Reuses ONE concrete central engine so Evaluate and finite invocation admission share the SAME
/// Grant/Revoke/SetPolicy gate. Actor observations are checked asynchronously before admission;
/// no actor/provider/repository callback runs under that gate. The callback starts the original
/// finite Task/stream iteration and returns immediately; never await, enumerate, perform observer
/// work or change policy within it. Later revocation denies new starts but cannot undo an already
/// admitted external effect or guarantee a network exclusion interval.
/// </summary>
/// <summary>UI observations from a private Ask issuer; copied properties/IDs cannot resolve it.</summary>
public interface ITaskRunCloudPermissionOriginalRequest
{
    TaskExecutionOwnerBinding OriginalOwner { get; }
    TaskRunRouteCandidate OriginalCandidate { get; }
    PermissionDecision OriginalDecision { get; }
}

public sealed partial class TaskRunCentralCloudUsePermissionSource : ITaskRunCloudUsePermissionSource
{
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly PermissionDecisionEngine _policy;
    private readonly object _requestGate = new();
    private readonly Dictionary<string, OriginalRequest> _originalRequests = new(StringComparer.Ordinal);
    private Exception? _capacityRefusal;
    internal IAuthenticatedResourceActorSource OriginalTaskActors => _actors;
    private sealed class OriginalRequest(TaskExecutionOwnerBinding owner, TaskRunRouteCandidate candidate,
        PermissionDecision decision, string identityKey) : ITaskRunCloudPermissionOriginalRequest
    {
        public TaskExecutionOwnerBinding OriginalOwner { get; } = owner;
        public TaskRunRouteCandidate OriginalCandidate { get; } = candidate;
        public PermissionDecision OriginalDecision { get; } = decision;
        public Task<PermissionDecision>? Resolution;
        public bool? Approved;
        public string IdentityKey { get; } = identityKey;
    }

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
        DemandPermission(scope, Reason(candidate), owner, candidate);
        token.ThrowIfCancellationRequested();
        return new Lease(this, owner, scope, Reason(candidate), candidate);
    }

    private static string Reason(TaskRunRouteCandidate candidate) =>
        $"Approve this task/run's remote model use: {candidate.ProviderId}/{candidate.ModelId}. Current cost and monetary budget are unknown.";

    private void DemandPermission(string scope, string reason, TaskExecutionOwnerBinding? owner = null,
        TaskRunRouteCandidate? candidate = null)
    {
        var decision = _policy.Evaluate(scope, CapabilityRiskClass.Consequential, requiresPermission: true, reason);
        if (decision.Kind == PermissionDecisionKind.Allowed) return;
        ITaskRunCloudPermissionOriginalRequest? original = null;
        if (decision.Kind == PermissionDecisionKind.Ask && owner is not null && candidate is not null)
        {
            var detached = candidate with { RequiredCapabilities = Array.AsReadOnly(candidate.RequiredCapabilities.ToArray()) };
            var key = JsonSerializer.Serialize(new { Owner = owner, Candidate = detached });
            lock (_requestGate)
            {
                if (_capacityRefusal is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_capacityRefusal).Throw();
                // A fresh Ask after revoke must not reuse a prior completed approval/denial.
                // Prior records remain strongly retained until their real caller acknowledges retirement.
                var request = _originalRequests.Values.LastOrDefault(value => value.IdentityKey == key &&
                    value.Resolution is not { IsCompletedSuccessfully: true });
                if (request is null)
                {
                    if (_originalRequests.Count == 128)
                    {
                        _capacityRefusal = new InvalidOperationException("Original remote-permission request custody is full.");
                        throw _capacityRefusal;
                    }
                    request = new(owner, detached, decision, key); _originalRequests.Add(Guid.NewGuid().ToString("N"), request);
                }
                original = request;
            }
        }
        throw new TaskRunCloudPermissionRequiredException(decision) { OriginalRequest = original };
    }

    internal bool IsIssuedOriginalRequest(ITaskRunCloudPermissionOriginalRequest original)
    { lock (_requestGate) return original is OriginalRequest request && _originalRequests.Values.Any(value => ReferenceEquals(value, request)); }

    // No grant or new Ask is issued by this pure current-policy check after awaited owner reads.
    internal void DemandOriginalAllowedUnstartedRequest(ITaskRunCloudPermissionOriginalRequest original)
    {
        if (!IsIssuedOriginalRequest(original)) throw new UnauthorizedAccessException("The original Ask issuer retired.");
        var scope = ScopeFor(original.OriginalOwner, original.OriginalCandidate);
        var decision = _policy.Evaluate(scope, CapabilityRiskClass.Consequential, requiresPermission: true,
            Reason(original.OriginalCandidate));
        if (decision.Kind != PermissionDecisionKind.Allowed) throw new TaskRunCloudPermissionRequiredException(decision);
    }

    // Called only by the source-owned explicit-decision handler after actual Task/route checks.
    // Completed remediation, IDs or explanation text never call this port themselves.
    internal Task<PermissionDecision> ResolveOriginalRequestAsync(ITaskRunCloudPermissionOriginalRequest original,
        bool approved, Func<CancellationToken, Task> finalOriginalCurrentness, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(finalOriginalCurrentness);
        TaskCompletionSource<PermissionDecision> completion; OriginalRequest request;
        lock (_requestGate)
        {
            if (original is not OriginalRequest actual || !_originalRequests.Values.Any(value => ReferenceEquals(value, actual)))
                throw new UnauthorizedAccessException("SAME original remote-permission request issuer required.");
            request = actual;
            if (request.Resolution is not null)
            {
                if (request.Approved != approved) throw new InvalidOperationException("An original explicit permission decision cannot be replaced.");
                return request.Resolution;
            }
            token.ThrowIfCancellationRequested(); request.Approved = approved;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); request.Resolution = completion.Task;
        }
        _ = ResolvePublishedAsync(request, approved, finalOriginalCurrentness, token, completion);
        return completion.Task;
    }

    private async Task ResolvePublishedAsync(OriginalRequest request, bool approved, Func<CancellationToken, Task> finalOriginalCurrentness, CancellationToken token,
        TaskCompletionSource<PermissionDecision> completion)
    {
        Task? actorRead = null, finalCurrent = null;
        try
        {
            if (request.OriginalDecision.Kind != PermissionDecisionKind.Ask ||
                request.OriginalDecision.Scope != ScopeFor(request.OriginalOwner, request.OriginalCandidate))
                throw new UnauthorizedAccessException("Original Ask/scope pairing changed.");
            actorRead = DemandActorAsync(request.OriginalOwner, token).AsTask();
            await actorRead.ConfigureAwait(false); token.ThrowIfCancellationRequested();
            if (approved)
            {
                // The source actor read may have yielded. Revalidate the trusted owner AFTER
                // that actual original, before this finite grant. No task/provider lookup
                // is performed while the central policy writer gate is held.
                finalCurrent = finalOriginalCurrentness(token) ?? throw new InvalidOperationException("No original final-currentness task was returned.");
                try { await finalCurrent.ConfigureAwait(false); }
                catch (Exception) when (finalCurrent.IsFaulted)
                { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(finalCurrent.Exception!).Throw(); throw; }
                token.ThrowIfCancellationRequested();
                // No issuer/owner lock is held across the central writer gate. This finite grant
                // resolves only the admitted explicit decision, never starts a provider operation.
                _policy.Grant(request.OriginalDecision.Scope);
                completion.TrySetResult(new(PermissionDecisionKind.Allowed, request.OriginalDecision.Scope,
                    "Explicit task/run/model grant recorded; every later invocation must revalidate current policy and context."));
            }
            else completion.TrySetResult(new(PermissionDecisionKind.Denied, request.OriginalDecision.Scope,
                "The user declined this original remote-use request; no scope was granted."));
        }
        catch (Exception error) { completion.TrySetException((Exception?)finalCurrent?.Exception ?? actorRead?.Exception ?? error); }
    }

    internal void RetireResolvedOriginalRequest(ITaskRunCloudPermissionOriginalRequest original)
    {
        lock (_requestGate)
        {
            if (original is not OriginalRequest request || request.Resolution is not { IsCompletedSuccessfully: true })
                throw new InvalidOperationException("Only a genuinely successful explicit original decision may retire.");
            var pair = _originalRequests.FirstOrDefault(value => ReferenceEquals(value.Value, request));
            if (pair.Key is not null) _originalRequests.Remove(pair.Key);
        }
    }

    private async ValueTask DemandActorAsync(TaskExecutionOwnerBinding owner, CancellationToken token)
    {
        var actual = _actors.GetCurrentAsync(token).AsTask();
        AuthenticatedResourceActor? current;
        try { current = await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(actual.Exception!).Throw(); throw; }
        var expected = new AuthenticatedResourceActor(owner.ActorId, owner.ProfileId, owner.AccountId,
            owner.OrganisationId, owner.AuthenticationRevision);
        if (current != expected) throw new UnauthorizedAccessException("The actual task actor changed before remote admission.");
        token.ThrowIfCancellationRequested();
    }

    private sealed partial class Lease : ITaskRunCloudUsePermissionLease
    {
        private readonly TaskRunCentralCloudUsePermissionSource _source;
        private readonly TaskExecutionOwnerBinding _owner;
        private readonly string _reason;
        private readonly TaskRunRouteCandidate _candidate;
        private readonly object _sync = new();
        private bool _closed;
        public string Scope { get; }
        public Lease(TaskRunCentralCloudUsePermissionSource source, TaskExecutionOwnerBinding owner, string scope, string reason, TaskRunRouteCandidate candidate)
        { _source = source; _owner = owner; Scope = scope; _reason = reason; _candidate = candidate with { RequiredCapabilities = Array.AsReadOnly(candidate.RequiredCapabilities.ToArray()) }; }

        public async ValueTask RevalidateAsync(CancellationToken token)
        {
            lock (_sync) ObjectDisposedException.ThrowIf(_closed, this);
            await _source.DemandActorAsync(_owner, token).ConfigureAwait(false);
            _source.DemandPermission(Scope, _reason, _owner, _candidate);
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
