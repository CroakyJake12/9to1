using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public interface IHomeOriginalScopedResourceStoreIdentitySource : IResourceStoreIdentitySource
{
    ValueTask<ResourceStoreIdentity> GetStoreIdentityWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}

public enum HomeOriginalLocalStoreImportState
{
    Unavailable, RequiresReview, PendingApproval, Declined, Approved, Imported, AuditPending, OutcomeUnconfirmed
}

/// <summary>App-owned first-use import over the configured source and SAME Home graph.
/// Views borrow this session; observed identifiers never recreate an import request.</summary>
public sealed class HomeOriginalLocalStoreImportSession : IAsyncDisposable
{
    public sealed class Snapshot
    {
        internal Snapshot(Guid? storeId, HomeOriginalLocalStoreImportState state, string reason, string? requestId)
        { StoreId = storeId; State = state; Reason = reason; RequestId = requestId; }
        public Guid? StoreId { get; }
        public HomeOriginalLocalStoreImportState State { get; }
        public string Reason { get; }
        public string? RequestId { get; }
        public bool CanRequest => State is HomeOriginalLocalStoreImportState.RequiresReview or HomeOriginalLocalStoreImportState.Declined;
        public bool CanComplete => State == HomeOriginalLocalStoreImportState.Approved;
        public bool CanRetryAudit => State == HomeOriginalLocalStoreImportState.AuditPending;
        public bool CanBrowse => State == HomeOriginalLocalStoreImportState.Imported;
    }
    private enum Operation { Inspect, Request, Refresh, Complete, RetryAudit }
    private readonly string _kind;
    private readonly IResourceStoreIdentitySource _identities;
    private readonly HomeLocalProfileIdentity _actors;
    private readonly HomeLocalStoreOwnership _ownership;
    private readonly IHomeOriginalScopedLocalStoreEvidenceProvider _evidence;
    private readonly HomePermissionTrustService _permissions;
    private readonly IResourceStoreOwnershipReceiptAuthority _readAuthority;
    private readonly ConditionalWeakTable<Snapshot, object> _snapshots = new();
    private readonly ConditionalWeakTable<Task, object> _issuedSources = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly List<Invocation> _commands = [];
    private readonly List<ImportObservation> _observations = [];
    private readonly AsyncLocal<Invocation?> _current = new();
    [ThreadStatic] private static HomeOriginalLocalStoreImportSession? _physical;
    private bool _retired;
    private Task? _close;
    private AuthenticatedResourceActor? _actor;
    private Guid? _storeId;
    private string? _requestId;
    private string? _auditRequestId;
    private bool _unknown;
    private sealed class ImportObservation(Task<HomeLocalStoreBinding> canonical, Exception cause, AuthenticatedResourceActor actor)
    {
        internal readonly Task<HomeLocalStoreBinding> Canonical = canonical;
        internal readonly Exception Cause = cause;
        internal readonly AuthenticatedResourceActor Actor = actor;
        internal Task<HomeLocalStoreBinding>? Retry;
    }
    private sealed class Invocation(Action<Action> scope, Action<Task> retain)
    {
        internal readonly Action<Action> Scope = scope;
        internal readonly Action<Task> BorrowerRetain = retain;
        internal readonly List<Task> Sources = [];
        internal readonly List<Exception> Errors = [];
        internal Task<Snapshot> Command = null!;
        internal bool Active;
        internal bool EffectEntered;
    }

    public HomeOriginalLocalStoreImportSession(string resourceKind, IResourceStoreIdentitySource identities,
        IAuthenticatedResourceActorSource actors, HomeLocalStoreOwnership ownership, IHomeLocalStoreEvidenceProvider evidence,
        HomePermissionTrustService permissions, IResourceStoreOwnershipReceiptAuthority readAuthority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKind);
        ArgumentNullException.ThrowIfNull(identities); ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(permissions); ArgumentNullException.ThrowIfNull(readAuthority);
        if (actors is not HomeLocalProfileIdentity profiles || identities is not IHomeOriginalScopedResourceStoreIdentitySource ||
            evidence is not IHomeOriginalScopedLocalStoreEvidenceProvider scopedEvidence || evidence.ResourceKind != resourceKind ||
            readAuthority is not IResourceStoreOriginalScopedOwnershipAuthority ||
            !ownership.HasOriginalImportComposition(actors, permissions, readAuthority) || !ownership.HasOriginalImportEvidenceProvider(evidence))
            throw new ArgumentException("Import requires the SAME configured scoped identity, evidence, Home profile, permissions and receipt authority.");
        _kind = resourceKind; _identities = identities; _actors = profiles; _ownership = ownership;
        _evidence = scopedEvidence; _permissions = permissions; _readAuthority = readAuthority;
    }
    public bool IsOriginalSource(IResourceStoreIdentitySource identities, IHomeLocalStoreEvidenceProvider evidence,
        IAuthenticatedResourceActorSource actors, IResourceStoreOwnershipReceiptAuthority readAuthority) =>
        ReferenceEquals(identities, _identities) && ReferenceEquals(evidence, _evidence) &&
        ReferenceEquals(actors, _actors) && ReferenceEquals(readAuthority, _readAuthority);
    public bool IsIssuedOriginalSnapshot(Snapshot actual) => actual is not null && _snapshots.TryGetValue(actual, out _);
    public Task<Snapshot> InspectWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) => Admit(Operation.Inspect, scope, retain, token);
    public Task<Snapshot> RequestImportWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) => Admit(Operation.Request, scope, retain, token);
    public Task<Snapshot> RefreshWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) => Admit(Operation.Refresh, scope, retain, token);
    public Task<Snapshot> CompleteImportWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) => Admit(Operation.Complete, scope, retain, token);
    public Task<Snapshot> RetryAuditWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) => Admit(Operation.RetryAudit, scope, retain, token);

    private Task<Snapshot> Admit(Operation operation, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        lock (_gate)
        {
            if (_retired) throw new ObjectDisposedException(nameof(HomeOriginalLocalStoreImportSession));
            _commands.RemoveAll(value => value.Command.IsCompletedSuccessfully && value.Errors.Count == 0 &&
                value.Sources.All(source => source.IsCompletedSuccessfully || IsAcknowledgedOriginalSourceCore(source)));
            if (_commands.Count >= 128) throw new InvalidOperationException("Original import commands remain unresolved; preserve them for recovery.");
            var invocation = new Invocation(scope, retain);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            invocation.Command = DriveAsync(start.Task, invocation, operation, token);
            _commands.Add(invocation); start.SetResult(); return invocation.Command;
        }
    }
    private async Task<Snapshot> DriveAsync(Task start, Invocation invocation, Operation operation, CancellationToken token)
    {
        await start.ConfigureAwait(false); await _serial.WaitAsync(token).ConfigureAwait(false);
        _current.Value = invocation; invocation.Active = true; Snapshot? result = null;
        try
        {
            try { result = await ExecuteAsync(operation, token).ConfigureAwait(false); }
            catch (Exception cause)
            {
                if (invocation.EffectEntered) _unknown = true;
                Add(invocation.Errors, cause);
            }
            await JoinSourcesAsync(invocation, allowPendingObservation: result?.State == HomeOriginalLocalStoreImportState.AuditPending).ConfigureAwait(false);
            if (invocation.EffectEntered && invocation.Errors.Count != 0) _unknown = true;
            Throw(invocation.Errors);
            return result ?? throw new InvalidOperationException("The actual import produced no observation.");
        }
        finally { invocation.Active = false; _current.Value = null; _serial.Release(); }
    }
    private async Task<Snapshot> ExecuteAsync(Operation operation, CancellationToken token)
    {
        var current = await InspectCoreAsync(token).ConfigureAwait(false);
        if (operation is Operation.Inspect or Operation.Refresh || _unknown) return current;
        if (operation == Operation.Request)
        {
            if (!current.CanRequest) return current;
            Current().EffectEntered = true;
            var authorization = await ReadAsync(() => _ownership.RequestImportWithinOriginalSourceAsync(_actor!, _kind,
                _storeId!.Value.ToString("D"), _actor!.AuthenticationRevision, Run, Retain, token),
                actual => _requestId = actual.RequestId).ConfigureAwait(false);
            // Capture the actual accepted request even if its publication postguard fails.
            _requestId = authorization.RequestId;
            return await InspectCoreAsync(token).ConfigureAwait(false);
        }
        if (operation == Operation.Complete && !current.CanComplete || operation == Operation.RetryAudit && !current.CanRetryAudit)
            return current;
        var request = operation == Operation.Complete ? _requestId! : _auditRequestId!;
        Task<HomeLocalStoreBinding>? canonical = null;
        Current().EffectEntered = true;
        try
        {
            var binding = await ReadAsync(() => canonical = operation == Operation.Complete
                ? _ownership.CompleteImportWithinOriginalSourceAsync(request, Run, Retain, token)
                : _ownership.RetryImportAuditWithinOriginalSourceAsync(request, Run, Retain, token)).ConfigureAwait(false);
            if (binding.ResourceKind != _kind || binding.StoreId != _storeId!.Value.ToString("D") || binding.ProfileId != _actor!.ProfileId)
                throw new InvalidOperationException("The original import acknowledgement does not match this source and profile.");
            if (operation == Operation.RetryAudit)
            {
                lock (_gate)
                {
                    foreach (var observed in _observations)
                        if (observed.Cause is HomeStoreImportAuditPendingException pending &&
                            _ownership.TryObserveOriginalImportAuditRecovery(observed.Canonical, pending, canonical!, observed.Actor, out _))
                            observed.Retry = canonical;
                }
            }
            _requestId = null; _auditRequestId = null;
            return await InspectCoreAsync(token).ConfigureAwait(false);
        }
        catch (Exception cause)
        {
            if (canonical is not null && _ownership.TryObserveOriginalImportAuditPending(canonical, cause, out var pending))
            {
                lock (_gate) _observations.Add(new(canonical, cause, _actor!));
                _requestId = null; _auditRequestId = pending!.RequestId;
                return Issue(HomeOriginalLocalStoreImportState.AuditPending,
                    "Ownership was acknowledged. Finish the retained Home audit; the import itself will not run again.", _auditRequestId);
            }
            if (canonical is not null && _ownership.TryObserveOriginalPreEffectRefusal(canonical, cause))
            {
                lock (_gate) _observations.Add(new(canonical, cause, _actor!));
                _requestId = null;
                return Issue(HomeOriginalLocalStoreImportState.RequiresReview,
                    "The original import was declined before any ownership effect. Review the current source and request permission again.", null);
            }
            throw;
        }
    }
    private async Task<Snapshot> InspectCoreAsync(CancellationToken token)
    {
        var actor = await ReadAsync(() => _actors.GetCurrentAsync(Run, Retain, token).AsTask()).ConfigureAwait(false);
        if (actor is null || actor.AccountId is not null || actor.OrganisationId is not null)
            return Issue(HomeOriginalLocalStoreImportState.Unavailable, "Recover the local Home profile before reviewing this store.", null);
        var identity = await ReadAsync(() => ((IHomeOriginalScopedResourceStoreIdentitySource)_identities)
            .GetStoreIdentityWithinOriginalSourceAsync(Run, Retain, token).AsTask()).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty)
            return Issue(HomeOriginalLocalStoreImportState.Unavailable, "The configured store identity needs its owning setup or recovery.", null);
        if (_actor is null) { _actor = actor; _storeId = identity.StoreId; }
        if (_actor != actor || _storeId != identity.StoreId)
            throw new UnauthorizedAccessException("The original Home profile or configured store changed. Preserve this session and reopen setup.");
        var id = identity.StoreId.ToString("D");
        var evidence = await ReadAsync(() => _evidence.ReadWithinOriginalSourceAsync(id, Run, Retain, token).AsTask()).ConfigureAwait(false);
        if (evidence is null || evidence.ResourceKind != _kind || evidence.StoreId != id ||
            !evidence.AccessibleToCurrentOsPrincipal || string.IsNullOrWhiteSpace(evidence.Revision))
            return Issue(HomeOriginalLocalStoreImportState.Unavailable, "The configured original store is unavailable to this local profile.", _requestId);
        var authority = (IResourceStoreOriginalScopedOwnershipAuthority)_readAuthority;
        var receipt = await ReadAsync(() => authority.GetVerifiedWithinOriginalSourceAsync(_kind, id, Run, Retain, token).AsTask()).ConfigureAwait(false);
        if (await ReadAsync(() => _actors.GetCurrentAsync(Run, Retain, token).AsTask()).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The actual Home profile changed during import review.");
        if (_unknown) return Issue(HomeOriginalLocalStoreImportState.OutcomeUnconfirmed,
            "An original import operation did not settle. Preserve its request and source for recovery; do not repeat import.", _auditRequestId ?? _requestId);
        if (_auditRequestId is not null) return Issue(HomeOriginalLocalStoreImportState.AuditPending,
            "The acknowledged ownership import still needs its original audit to finish.", _auditRequestId);
        if (receipt is not null && receipt.ResourceKind == _kind && receipt.StoreId == id && receipt.ProfileId == actor.ProfileId && await ReadAsync(() => authority.IsCurrentWithinOriginalSourceAsync(receipt, actor, Run, Retain, token).AsTask()).ConfigureAwait(false))
            return Issue(HomeOriginalLocalStoreImportState.Imported, "Home has verified this configured store for the current local profile.", null);
        if (_requestId is null) return Issue(HomeOriginalLocalStoreImportState.RequiresReview,
            "Review this existing store, then request its explicit ownership import in Home. No saved definition has been converted.", null);
        var request = await ReadAsync(() => _permissions.ReadImportRequestWithinOriginalSourceAsync(_requestId, Run, Retain, token)).ConfigureAwait(false);
        if (request is null || request.RequestId != _requestId || request.Caller.Origin != actor.ProfileId ||
            request.Caller.CallerId != actor.ActorId || request.Caller.IdentityVersion != actor.AuthenticationRevision ||
            request.Scope.TargetAppId != "9to1.home.local-profile" || request.Scope.ActionName != "home.profile.importStore" ||
            request.SessionId != actor.AuthenticationRevision || !request.Caller.IsVerified || request.Scope.IncludesAllObjects ||
            request.Scope.Objects.Count != 1 || request.Scope.Objects[0].ObjectType != "local-resource-store" ||
            request.Scope.Objects[0].ObjectId != _kind + ":" + id)
            throw new UnauthorizedAccessException("The pending Home request no longer matches this original store/profile review.");
        var approval = await ReadAsync(() => _permissions.GetImportAuthorizationWithinOriginalSourceAsync(_requestId, Run, Retain, token)).ConfigureAwait(false);
        if (approval.RequestId != _requestId ||
            await ReadAsync(() => _actors.GetCurrentAsync(Run, Retain, token).AsTask()).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The actual Home profile or request changed while reading its decision.");
        return request.State switch
        {
            HomePermissionRequestState.PendingApproval => Issue(HomeOriginalLocalStoreImportState.PendingApproval, "Review the pending ownership request in Home, then check its decision here.", _requestId),
            HomePermissionRequestState.Approved when approval.IsAllowed => Issue(HomeOriginalLocalStoreImportState.Approved, "Home approved this exact request. Confirm completing its original ownership import.", _requestId),
            HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked or HomePermissionRequestState.Cancelled => Issue(HomeOriginalLocalStoreImportState.Declined, "Home declined this request. Originals remain unchanged; a new review needs a new explicit request.", _requestId),
            _ => Issue(HomeOriginalLocalStoreImportState.OutcomeUnconfirmed, "The original request is not ready for import. Preserve its actual state for recovery.", _requestId)
        };
    }
    private Snapshot Issue(HomeOriginalLocalStoreImportState state, string reason, string? requestId)
    { var actual = new Snapshot(_storeId, state, reason, requestId); _snapshots.Add(actual, new object()); return actual; }

    private Invocation Current() => _current.Value is { Active: true } actual ? actual :
        throw new InvalidOperationException("An admitted original import command is required.");
    private void Run(Action body)
    {
        var current = Current(); var prior = _physical; _physical = this;
        var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
        Exception? failure = null;
        try
        {
            try
            {
                current.Scope(() =>
                {
                    if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                    { var error = new InvalidOperationException("The original import callback is inactive, foreign-thread or consumed."); Add(current.Errors, error); throw error; }
                    try { body(); } catch (Exception cause) { Add(current.Errors, cause); throw; }
                });
            }
            catch (Exception cause) { failure = cause; Add(current.Errors, cause); }
            if (Volatile.Read(ref used) == 0) Add(current.Errors, new InvalidOperationException("The original import callback was not invoked."));
            // Preserve a body failure even if the borrower swallowed or replaced it.
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            Throw(current.Errors);
        }
        finally { Interlocked.Exchange(ref active, 0); _physical = prior; }
    }
    private void Retain(Task actual)
    {
        ArgumentNullException.ThrowIfNull(actual); var current = Current();
        lock (_gate)
        {
            if (!current.Sources.Any(value => ReferenceEquals(value, actual))) current.Sources.Add(actual);
            _issuedSources.GetValue(actual, _ => new object());
        }
        current.BorrowerRetain(actual);
    }
    private async Task<T> ReadAsync<T>(Func<Task<T>> factory, Action<T>? capture = null)
    {
        Task<T>? actual = null; Exception? publication = null; T result = default!;
        try { Run(() => { actual = factory() ?? throw new InvalidOperationException("The original import source returned no Task."); Retain(actual); }); }
        catch (Exception cause) { publication = cause; }
        Exception? source = null;
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); capture?.Invoke(result); } catch (Exception cause) { source = cause; }
        if (publication is not null && source is not null && !ReferenceEquals(publication, source))
            throw new AggregateException("Original import publication and actual source failed.", publication, source);
        if (publication is not null) ExceptionDispatchInfo.Capture(publication).Throw();
        if (source is not null) ExceptionDispatchInfo.Capture(source).Throw();
        return actual is null ? throw new InvalidOperationException("No original import source Task was captured.") : result;
    }
    public bool IsAcknowledgedOriginalSource(Task sameActualSource)
    {
        lock (_gate)
            return _issuedSources.TryGetValue(sameActualSource, out _) &&
                IsAcknowledgedOriginalSourceCore(sameActualSource);
    }
    /// <summary>Pure proof for an actual pending observation; this never qualifies
    /// retirement. Only a successful SAME audit retry can acknowledge the source.</summary>
    public bool IsOwnedOriginalPendingSource(Task sameActualSource, Snapshot sameSnapshot)
    {
        lock (_gate) return sameSnapshot is not null && _snapshots.TryGetValue(sameSnapshot, out _) &&
            _issuedSources.TryGetValue(sameActualSource, out _) &&
            sameSnapshot.State == HomeOriginalLocalStoreImportState.AuditPending &&
            sameSnapshot.RequestId is not null && sameSnapshot.RequestId == _auditRequestId &&
            sameSnapshot.StoreId == _storeId && IsOwnedPendingSource(sameActualSource);
    }
    private bool IsAcknowledgedOriginalSourceCore(Task raw)
    {
        foreach (var observed in _observations)
        {
            var cause = ReferenceEquals(raw, observed.Canonical) ? observed.Cause : raw.Exception?.InnerExceptions.Count == 1 ? raw.Exception.InnerException : null;
            if (cause is not null && _ownership.IsAcknowledgedOriginalImportSource(raw, observed.Canonical, cause, observed.Retry, observed.Actor)) return true;
        }
        return false;
    }
    private bool IsOwnedPendingSource(Task raw)
    {
        lock (_gate) return _observations.Any(observed => observed.Cause is HomeStoreImportAuditPendingException pending &&
            _ownership.IsOwnedOriginalImportPendingSource(raw, observed.Canonical, pending));
    }
    private async Task JoinSourcesAsync(Invocation invocation, bool allowPendingObservation)
    {
        var index = 0;
        while (true)
        {
            Task? raw; lock (_gate) raw = index < invocation.Sources.Count ? invocation.Sources[index++] : null;
            if (raw is null) break;
            try { await raw.ConfigureAwait(false); }
            catch (Exception cause)
            {
                if (IsAcknowledgedOriginalSource(raw) || allowPendingObservation && IsOwnedPendingSource(raw)) continue;
                Add(invocation.Errors, raw.Exception ?? cause);
            }
        }
    }
    public void RequestRetirement() { lock (_gate) _retired = true; }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (ReferenceEquals(_physical, this) || _current.Value is { Active: true })
            throw new InvalidOperationException("An import callback cannot join its own original session.");
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_gate)
        {
            _retired = true;
            if (_close is not null) return _close;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = DrainAsync(start.Task, _commands.ToArray()); start.SetResult(); return _close;
        }
    }
    private async Task DrainAsync(Task start, Invocation[] commands)
    {
        await start.ConfigureAwait(false); List<Exception> errors = [];
        foreach (var invocation in commands)
        {
            try { await invocation.Command.ConfigureAwait(false); } catch (Exception cause) { Add(errors, invocation.Command.Exception ?? cause); }
            await JoinSourcesAsync(invocation, allowPendingObservation: false).ConfigureAwait(false);
            foreach (var cause in invocation.Errors) Add(errors, cause);
        }
        Throw(errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private static void Add(List<Exception> errors, Exception cause)
    { lock (errors) if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
    private static void Throw(List<Exception> errors)
    {
        Exception[] actual; lock (errors) actual = errors.ToArray();
        if (actual.Length == 1) ExceptionDispatchInfo.Capture(actual[0]).Throw();
        if (actual.Length > 1) throw new AggregateException("Actual Home import sources remain unresolved.", actual);
    }
}
