using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed partial class HomeLocalStoreOwnership
{
    private readonly object _originalScopedImportGate = new();
    private readonly List<ScopedImportSources> _originalScopedImports = [];
    private readonly ConditionalWeakTable<HomeOwnershipOriginalSourceCallbacks, ScopedImportSources> _originalImportCallbacks = new();
    private readonly ConditionalWeakTable<OriginalImportInvocation, ScopedImportSources> _originalScopedImportInvocations = new();

    public bool HasOriginalImportComposition(IAuthenticatedResourceActorSource sameActors,
        HomePermissionTrustService samePermissions, IResourceStoreOwnershipReceiptAuthority sameReadAuthority) =>
        ReferenceEquals(profiles, sameActors) && ReferenceEquals(permissions, samePermissions) &&
        permissions.IsBoundToStore(store) && sameReadAuthority is HomeResourceStoreOwnershipAuthority actual &&
        actual.HasOriginalLocalStoreOwnership(this, profiles);

    public bool HasOriginalImportEvidenceProvider(IHomeLocalStoreEvidenceProvider sameProvider) =>
        evidence is HomeLocalStoreEvidenceRegistry registry && registry.HasOriginalEvidenceProvider(sameProvider);

    public Task<HomePermissionAuthorization> RequestImportWithinOriginalSourceAsync(AuthenticatedResourceActor expectedActor,
        string kind, string storeId, string sessionId, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        PublishScopedImport(scope, retain, null, source => RequestImportCoreAsync(expectedActor, kind, storeId, sessionId, token, source));

    public Task<HomeLocalStoreBinding> CompleteImportWithinOriginalSourceAsync(string requestId,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var invocation = new OriginalImportInvocation(OriginalImportOperation.Complete, requestId);
        var actual = PublishScopedImport(scope, retain, invocation,
            source => CompleteOriginalImportCoreAsync(invocation, requestId, token, source));
        _originalImportInvocations.Add(actual, invocation);
        return actual;
    }
    public Task<HomeLocalStoreBinding> RetryImportAuditWithinOriginalSourceAsync(string requestId,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var invocation = new OriginalImportInvocation(OriginalImportOperation.RetryAudit, requestId);
        var actual = PublishScopedImport(scope, retain, invocation,
            source => RetryOriginalImportAuditCoreAsync(invocation, requestId, token, source));
        _originalImportInvocations.Add(actual, invocation);
        return actual;
    }

    public bool TryObserveOriginalImportAuditPending(Task<HomeLocalStoreBinding> sameCanonical, Exception sameCause,
        out HomeStoreImportAuditPendingException? pending)
    {
        pending = null;
        if (sameCanonical is null || sameCause is null ||
            !_originalImportInvocations.TryGetValue(sameCanonical, out var invocation) ||
            invocation.PendingCause is not { } actual || !ReferenceEquals(actual, sameCause) ||
            !HasOnlyOriginalCause(sameCanonical, actual) || !invocation.AuditGateReleased ||
            invocation.Completion is not { } completion ||
            !_importAudits.TryGetValue(invocation.RequestId, out var retained) || !ReferenceEquals(retained, completion)) return false;
        pending = actual; return true;
    }

    public bool IsOwnedOriginalImportPendingSource(Task sameRaw, Task<HomeLocalStoreBinding> sameCanonical,
        HomeStoreImportAuditPendingException samePending)
    {
        if (!TryObserveOriginalImportAuditPending(sameCanonical, samePending, out _) ||
            !_originalImportInvocations.TryGetValue(sameCanonical, out var invocation) ||
            !_originalScopedImportInvocations.TryGetValue(invocation, out var sources)) return false;
        return ReferenceEquals(sameRaw, sameCanonical) || sources.IsExactPendingAuditSource(sameRaw, samePending);
    }

    public bool IsAcknowledgedOriginalImportSource(Task sameRaw, Task<HomeLocalStoreBinding> sameCanonical,
        Exception sameCause, Task<HomeLocalStoreBinding>? successfulRetry, AuthenticatedResourceActor sameActor)
    {
        if (ReferenceEquals(sameRaw, sameCanonical) && TryObserveOriginalPreEffectRefusal(sameCanonical, sameCause)) return true;
        if (successfulRetry is null || !_originalImportInvocations.TryGetValue(sameCanonical, out var invocation) ||
            invocation.PendingCause is not { } pending ||
            !TryObserveOriginalImportAuditRecovery(sameCanonical, pending, successfulRetry, sameActor, out _) ||
            !_originalScopedImportInvocations.TryGetValue(invocation, out var sources)) return false;
        if (ReferenceEquals(sameRaw, sameCanonical)) return ReferenceEquals(sameCause, pending);
        return sources.IsExactPendingAuditSource(sameRaw, pending) && HasOnlyOriginalCause(sameRaw, sameCause);
    }

    private Task<T> PublishScopedImport<T>(Action<Action> scope, Action<Task> retain,
        OriginalImportInvocation? invocation, Func<HomeOwnershipOriginalSourceCallbacks, Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var sources = new ScopedImportSources(this, scope, retain);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<T> actual;
        lock (_originalScopedImportGate)
        {
            _originalScopedImports.RemoveAll(value => value.IsHealthy);
            if (_originalScopedImports.Count >= 256) throw new InvalidOperationException("Original unresolved ownership import sources remain retained.");
            actual = Drive(start.Task); sources.Driver = actual;
            _originalScopedImports.Add(sources); _originalImportCallbacks.Add(sources.Protocol, sources);
            if (invocation is not null) _originalScopedImportInvocations.Add(invocation, sources);
        }
        try { sources.Protocol.Run(() => retain(actual)); }
        catch { /* The accepted driver owns the exact publication error. */ }
        finally { start.SetResult(); }
        return actual;
        async Task<T> Drive(Task gate)
        {
            await gate.ConfigureAwait(false); using var original = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            T result = default!; Exception? primary = null;
            try
            {
                if (sources.Protocol.Errors.Length != 0) throw new AggregateException("Actual import publication failed.", sources.Protocol.Errors);
                result = await body(sources.Protocol).ConfigureAwait(false);
            }
            catch (Exception cause) { primary = cause; }
            await sources.Settle(invocation, primary).ConfigureAwait(false); return result;
        }
    }

    private Task<HomePermissionAuthorization> ReadOriginalImportAuthorizationAsync(HomeOwnershipOriginalSourceCallbacks? source,
        string requestId, bool begin, CancellationToken token) => source is null
        ? (begin ? permissions.BeginExecutionAsync(requestId, token) : permissions.GetAuthorizationAsync(requestId, token))
        : source.ReadAsync(() => begin
            ? permissions.BeginImportExecutionWithinOriginalSourceAsync(requestId, source.Run, source.Retain, token)
            : permissions.GetImportAuthorizationWithinOriginalSourceAsync(requestId, source.Run, source.Retain, token));

    private async Task<HomePermissionOperationResult> RecordOriginalImportExecutionAsync(OriginalImportInvocation invocation,
        HomeOwnershipOriginalSourceCallbacks? source, string requestId, HomeExecutionOutcome outcome, CancellationToken token)
    {
        if (source is null) return await permissions.RecordExecutionAsync(requestId, outcome, token).ConfigureAwait(false);
        if (!_originalImportCallbacks.TryGetValue(source, out var original))
            throw new InvalidOperationException("The actual import audit callback owner is unavailable.");
        Task<HomePermissionOperationResult>? actual = null; Exception? publication = null;
        try
        {
            source.Run(() =>
            {
                // This retain delegate belongs only to this exact permission audit command
                // and its descendants. Other import tasks never enter its source set.
                actual = permissions.RecordImportExecutionWithinOriginalSourceAsync(requestId, outcome,
                    original.RunAuditScope, original.RetainAuditSource, token, original.RetainAuditCallbackFailure);
                original.CaptureAuditCommand(actual);
                source.Retain(actual);
            });
        }
        catch (Exception cause) { publication = cause; }
        HomePermissionOperationResult? result = null; Exception? failure = null;
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); }
            catch (Exception cause) { failure = actual.Exception is { InnerExceptions.Count: > 1 } group ? group : cause; }
        if (failure is not null) original.CaptureAuditFailure(actual!, failure);
        if (publication is not null)
            throw failure is null ? publication : new AggregateException("Actual import audit publication and raw source failed.", publication, failure);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result ?? throw new InvalidOperationException("No actual Home import audit task returned.");
    }

    private bool IsOriginalScopedAuditFailure(OriginalImportInvocation invocation,
        HomeOwnershipOriginalSourceCallbacks? source, Exception sameFailure) =>
        invocation.Completion is not null && source is not null &&
        _originalImportCallbacks.TryGetValue(source, out var original) &&
        _originalScopedImportInvocations.TryGetValue(invocation, out var sameOriginal) &&
        ReferenceEquals(original, sameOriginal) && original.HasExactAuditFailure(sameFailure);

    private static async Task WaitOriginalImportAuditGateAsync(ImportCompletion completion,
        HomeOwnershipOriginalSourceCallbacks? source, CancellationToken token)
    {
        if (source is null) { await completion.Gate.WaitAsync(token).ConfigureAwait(false); return; }
        Task? actual = null;
        try { await source.ReadAsync(async () => { actual = completion.Gate.WaitAsync(token); source.Retain(actual); await actual.ConfigureAwait(false); return true; }).ConfigureAwait(false); }
        catch (Exception primary)
        {
            Exception? cleanup = null;
            if (actual?.IsCompletedSuccessfully == true) try { source.Run(() => completion.Gate.Release()); } catch (Exception cause) { cleanup = cause; }
            if (cleanup is not null) throw new AggregateException("Actual import audit gate acquisition and independent release failed.", primary, cleanup);
            throw;
        }
    }

    private sealed class ScopedImportSources
    {
        internal readonly HomeOwnershipOriginalSourceCallbacks Protocol;
        private readonly object _gate = new(); private readonly List<Task> _raw = [];
        private readonly HashSet<Task> _auditRaw = new(ReferenceEqualityComparer.Instance);
        private readonly List<Exception> _auditCallbackErrors = [];
        private Task? _auditTask;
        private Exception? _auditFailure;
        internal Task Driver = null!;
        internal ScopedImportSources(HomeLocalStoreOwnership owner, Action<Action> scope, Action<Task> retain)
        {
            Protocol = new(body => CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () => { scope(body); return true; }), raw =>
            {
                lock (_gate) if (!_raw.Any(value => ReferenceEquals(value, raw))) _raw.Add(raw);
                CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () => { retain(raw); return true; });
            });
        }
        internal bool IsHealthy => Driver.IsCompletedSuccessfully && Protocol.Errors.Length == 0 && Raw.All(value => value.IsCompletedSuccessfully);
        private Task[] Raw { get { lock (_gate) return _raw.ToArray(); } }
        internal void RunAuditScope(Action body)
        {
            try { Protocol.Run(body); }
            catch (Exception cause) { RetainAuditCallbackFailure(cause); throw; }
        }
        internal void RetainAuditSource(Task sameRaw)
        {
            ArgumentNullException.ThrowIfNull(sameRaw);
            lock (_gate) _auditRaw.Add(sameRaw);
            try { Protocol.Retain(sameRaw); }
            catch (Exception cause) { RetainAuditCallbackFailure(cause); throw; }
        }
        internal void RetainAuditCallbackFailure(Exception sameCause)
        {
            lock (_gate)
                if (!_auditCallbackErrors.Any(value => ReferenceEquals(value, sameCause)))
                    _auditCallbackErrors.Add(sameCause);
        }
        internal void CaptureAuditCommand(Task sameActual)
        {
            lock (_gate)
            {
                if (_auditTask is not null && !ReferenceEquals(_auditTask, sameActual))
                    throw new InvalidOperationException("The original import audit command changed.");
                _auditTask = sameActual;
            }
        }
        internal void CaptureAuditFailure(Task sameActual, Exception sameCause)
        {
            if (!HasOnlyOriginalCause(sameActual, sameCause)) return;
            lock (_gate) if (ReferenceEquals(sameActual, _auditTask)) _auditFailure = sameCause;
        }
        internal bool IsExactPendingAuditSource(Task sameRaw, HomeStoreImportAuditPendingException pending)
        {
            if (pending.InnerException is not { } cause || !HasExactAuditFailure(cause)) return false;
            lock (_gate) return _auditRaw.Contains(sameRaw) && sameRaw.IsFaulted;
        }
        internal bool HasExactAuditFailure(Exception sameCause)
        {
            Task? command; Task[] raw; HashSet<Task> audit;
            lock (_gate)
            {
                if (_auditCallbackErrors.Count != 0 || !ReferenceEquals(_auditFailure, sameCause)) return false;
                command = _auditTask; raw = _raw.ToArray();
                audit = new(_auditRaw, ReferenceEqualityComparer.Instance);
            }
            if (command is null || !HasOnlyOriginalCause(command, sameCause) || Protocol.Errors.Length != 0 ||
                raw.Length > 4096 || audit.Count > 4096 || !audit.Contains(command)) return false;
            var all = new HashSet<Task>(raw, ReferenceEqualityComparer.Instance);
            if (audit.Any(value => !all.Contains(value))) return false;
            var direct = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
            var failures = new List<Exception>();
            foreach (var sameRaw in raw)
            {
                if (sameRaw.IsCompletedSuccessfully) continue;
                // Cancellation, unfinished work and a genuinely multi-terminal Task are
                // not an acknowledged audit loss. Unrelated import failures stay sticky.
                if (!audit.Contains(sameRaw) || !sameRaw.IsFaulted ||
                    sameRaw.Exception is not { InnerExceptions.Count: 1 } error) return false;
                var cause = error.InnerExceptions[0]; failures.Add(cause);
                if (ReferenceEquals(sameRaw, command) || cause is AggregateException) continue;
                if (!AllowedAuditCause(cause)) return false;
                direct.Add(cause);
            }
            if (sameCause is not AggregateException)
            {
                if (!AllowedAuditCause(sameCause)) return false;
                direct.Add(sameCause); // Preserve the existing direct typed audit failure.
            }
            // Aggregates are accepted only as wrappers around the exact raw audit
            // occurrences, never by exception type alone or a temporal task-list delta.
            if (direct.Count == 0 || !HasOnlyAuditOccurrences(sameCause, direct, out var observed) ||
                !direct.SetEquals(observed)) return false;
            foreach (var failure in failures)
                if (!HasOnlyAuditOccurrences(failure, direct, out _)) return false;
            lock (_gate)
                return _auditCallbackErrors.Count == 0 && ReferenceEquals(_auditTask, command) &&
                    ReferenceEquals(_auditFailure, sameCause) && _raw.Count == raw.Length &&
                    _auditRaw.Count == audit.Count && Protocol.Errors.Length == 0;
        }
        private static bool AllowedAuditCause(Exception cause) =>
            cause is IOException or UnauthorizedAccessException or InvalidOperationException;
        private static bool HasOnlyAuditOccurrences(Exception actual, HashSet<Exception> direct,
            out HashSet<Exception> observed)
        {
            observed = new(ReferenceEqualityComparer.Instance);
            var pending = new Stack<Exception>(); var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
            pending.Push(actual); var enqueued = 1;
            while (pending.TryPop(out var same))
            {
                if (direct.Contains(same)) { observed.Add(same); continue; }
                if (!seen.Add(same)) continue;
                if (seen.Count > 4096 || same.GetType() != typeof(AggregateException) ||
                    same is not AggregateException { InnerExceptions.Count: > 0 } group) return false;
                foreach (var child in group.InnerExceptions)
                {
                    if (++enqueued > 4096) return false;
                    pending.Push(child);
                }
            }
            return true;
        }
        internal async Task Settle(OriginalImportInvocation? invocation, Exception? primary)
        {
            List<Exception> errors = []; var tasks = Raw;
            foreach (var raw in tasks)
                try { await raw.ConfigureAwait(false); }
                catch (Exception cause)
                {
                    if (invocation?.PendingCause is { } pending && IsExactPendingAuditSource(raw, pending)) continue;
                    if (raw.Exception is { } group) { Add(group); foreach (var direct in group.InnerExceptions) Add(direct); }
                    else Add(cause);
                }
            foreach (var cause in Protocol.Errors) Add(cause);
            if (primary is not null && errors.Count == 0 && invocation is not null &&
                (ReferenceEquals(primary, invocation.PreEffectRefusal) || ReferenceEquals(primary, invocation.PendingCause)))
                ExceptionDispatchInfo.Capture(primary).Throw();
            if (primary is not null) Add(primary);
            if (errors.Count != 0) throw new AggregateException("Actual scoped import sources/cleanup failed.", errors);
            void Add(Exception cause) { if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
        }
    }
}

public sealed partial class HomeResourceStoreOwnershipAuthority
{
    internal bool HasOriginalLocalStoreOwnership(HomeLocalStoreOwnership actual, IAuthenticatedResourceActorSource sameActors) =>
        ReferenceEquals(ownership, actual) && ReferenceEquals(actors, sameActors);
}
public sealed partial class HomeLocalStoreEvidenceRegistry
{
    internal bool HasOriginalEvidenceProvider(IHomeLocalStoreEvidenceProvider actual) =>
        _providers.Any(value => ReferenceEquals(value, actual)) && _providers.Count(value => value.ResourceKind == actual.ResourceKind) == 1;
}
