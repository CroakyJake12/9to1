using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;

namespace Haven.Desktop.Services;

internal enum SpacesOriginalStoreSetupOperation { Inspect, RequestImport, OpenReview, CompleteImport, RetryAudit }

/// <summary>App-owned existing-store review. Views borrow this owner and its exact Home
/// import session. Construction, inspection and approval alone never bind ownership.</summary>
internal sealed class SpacesOriginalStoreSetupBackend
{
    internal sealed class Snapshot(ExistingSettingsStoreMetadata? metadata, AuthenticatedResourceActor actor,
        HomeOriginalLocalStoreImportSession.Snapshot? import, string? refusal = null)
    {
        internal ExistingSettingsStoreMetadata? Metadata { get; } = metadata;
        internal AuthenticatedResourceActor Actor { get; } = actor;
        internal HomeOriginalLocalStoreImportSession.Snapshot? Import { get; } = import;
        public bool IsOwned => Import?.CanBrowse == true;
        public bool CanRequestImport => Import?.CanRequest == true;
        public bool CanOpenReview => Import?.State == HomeOriginalLocalStoreImportState.PendingApproval;
        public bool CanCompleteImport => Import?.CanComplete == true;
        public bool CanRetryAudit => Import?.CanRetryAudit == true;
        public string StoreId => Metadata?.StoreIdentity.StoreId.ToString("D") ?? "Unavailable";
        public string Message => refusal ?? Import?.Reason ??
            "The existing Spaces store has no durable identity. Preserve it and complete its owning setup or recovery before review.";
    }

    private readonly VersionedAtomicSettingsStore _settings;
    private readonly SpacesLocalStoreEvidenceProvider _evidence;
    private readonly HomeNativeWindowsComposition _home;
    private readonly HomeNativeApprovalWindowOwner _approvals;
    private readonly HomeOriginalLocalStoreImportSession _imports;
    private readonly CancellationToken _appLifetime;
    private readonly ConditionalWeakTable<Snapshot, object> _snapshots = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly List<Task> _operations = [];
    private readonly HashSet<Task> _importSources = [];
    private Snapshot? _currentSnapshot;
    private bool _retiring;
    private Task? _close;

    internal SpacesOriginalStoreSetupBackend(VersionedAtomicSettingsStore settings, SpacesLocalStoreEvidenceProvider evidence,
        HomeNativeWindowsComposition home, HomeNativeApprovalWindowOwner approvals, CancellationToken actualAppLifetime)
    {
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(home); ArgumentNullException.ThrowIfNull(approvals);
        if (!actualAppLifetime.CanBeCanceled || !evidence.IsBoundToOriginalSettings(settings) ||
            !ReferenceEquals(home.Services.GetService(typeof(IVersionedSettingsStore)), settings) ||
            !ReferenceEquals(home.Services.GetService(typeof(SpacesLocalStoreEvidenceProvider)), evidence) ||
            !approvals.IsBoundToOriginalComposition(home))
            throw new UnauthorizedAccessException("Use the SAME original Spaces settings, evidence and Home review owners.");
        _settings = settings; _evidence = evidence; _home = home; _approvals = approvals; _appLifetime = actualAppLifetime;
        _imports = new(SpacesLocalStoreEvidenceProvider.Kind, evidence, home.Profiles, home.LocalStoreOwnership,
            evidence, home.Permissions, home.Ownership);
    }

    internal bool HasOriginalComposition(VersionedAtomicSettingsStore settings, SpacesLocalStoreEvidenceProvider evidence,
        HomeNativeWindowsComposition home, HomeNativeApprovalWindowOwner approvals) =>
        ReferenceEquals(settings, _settings) && ReferenceEquals(evidence, _evidence) && ReferenceEquals(home, _home) &&
        ReferenceEquals(approvals, _approvals) && _imports.IsOriginalSource(evidence, evidence, home.Profiles, home.Ownership);
    internal Task? OriginalClose { get { lock (_gate) return _close; } }

    internal Task<Snapshot> ExecuteWithinOriginalSourceAsync(SpacesOriginalStoreSetupOperation operation,
        Snapshot? expected, Action<Action> originalScope, Action<Task> retain, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(originalScope); ArgumentNullException.ThrowIfNull(retain);
        lock (_gate)
        {
            DemandAlive(); caller.ThrowIfCancellationRequested();
            _operations.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_operations.Count >= 128) throw new InvalidOperationException("Settle original Spaces setup operations before admitting more work.");
            if (operation != SpacesOriginalStoreSetupOperation.Inspect &&
                (expected is null || !_snapshots.TryGetValue(expected, out _)))
                throw new UnauthorizedAccessException("Retain this owner's exact original Spaces setup observation.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = ExecuteAsync(start.Task, operation, expected, originalScope, retain, caller);
            _operations.Add(actual); start.SetResult(); return actual;
        }
    }

    private async Task<Snapshot> ExecuteAsync(Task start, SpacesOriginalStoreSetupOperation operation, Snapshot? expected,
        Action<Action> scope, Action<Task> retain, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, caller);
        var token = lifetime.Token;
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
        sources.BindOriginalCallerCallback(scope);
        // Sources own their explicit business token. Accepted post-binding audit and gate
        // cleanup deliberately use CancellationToken.None and must retain this same scope.
        void Own(Action body) => sources.Invoke(() => { body(); return true; });
        void Retain(Task raw) { sources.Track(raw); retain(raw); }
        void RetainImport(Task raw)
        {
            // The SAME App-owned import session qualifies its own acknowledged pending-audit
            // originals. Keep their exact custody here without feeding them into a generic
            // borrower error ledger that cannot interpret that canonical proof. The actual
            // import command remains an awaited outer child and retirement joins every source.
            lock (_gate)
            {
                _importSources.RemoveWhere(task => task.IsCompletedSuccessfully);
                _importSources.Add(raw);
            }
        }
        async Task<T> Read<T>(Func<Task<T>> body)
        {
            Task<T>? actual = null; Exception? callbackFailure = null; T value = default!;
            try { _ = sources.Invoke(() => { actual = body(); Retain(actual); return actual; }); }
            catch (Exception error) { callbackFailure = error; sources.Retain(error); }
            if (actual is not null) value = await sources.AwaitAsync(actual).ConfigureAwait(false);
            if (callbackFailure is not null) throw new AggregateException("The original Spaces setup callback failed.", sources.OriginalErrors);
            return actual is null ? throw new InvalidOperationException("No original Spaces setup source task was captured.") : value;
        }
        return await sources.RunToOriginalSettlementAsync(async () =>
        {
            bool entered = false;
            try
            {
                Task? gateWait = null; Exception? gateFailure = null;
                try { _ = sources.Invoke(() => { gateWait = _serial.WaitAsync(token); Retain(gateWait); return gateWait; }); }
                catch (Exception error) { gateFailure = error; sources.Retain(error); }
                if (gateWait is not null) { await sources.AwaitAsync(gateWait).ConfigureAwait(false); entered = true; }
                if (gateFailure is not null) throw new AggregateException("The original Spaces setup gate callback failed.", sources.OriginalErrors);
                if (!entered) throw new InvalidOperationException("No original Spaces setup gate was acquired.");
                // An issued observation from an older Settings borrower cannot operate on a
                // newer request held by the shared import session. Inspect again to adopt it.
                if (operation != SpacesOriginalStoreSetupOperation.Inspect && !ReferenceEquals(expected, _currentSnapshot))
                {
                    // An ordinary stale view has entered no store, review or import effect.
                    // Return a non-actionable issued refusal; do not replace the newer
                    // observation/session or turn this expected outcome into a close failure.
                    var refused = new Snapshot(null, expected!.Actor, null,
                        "The displayed Spaces request changed. Inspect Spaces storage before continuing.");
                    _snapshots.Add(refused, new object()); return refused;
                }
                var actor = await Read(() => _home.Profiles.GetCurrentAsync(Own, Retain, token).AsTask()).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("Recover the actual local Home profile before configuring Spaces.");
                if (actor.AccountId is not null || actor.OrganisationId is not null || expected is not null && expected.Actor != actor)
                    throw new UnauthorizedAccessException("The displayed Home profile changed. Reopen Spaces store setup.");
                if (operation != SpacesOriginalStoreSetupOperation.Inspect)
                {
                    switch (operation)
                    {
                        case SpacesOriginalStoreSetupOperation.RequestImport:
                            if (!expected!.CanRequestImport) throw new InvalidOperationException("Inspect this existing Spaces store before requesting import.");
                            var current = await Read(() => _evidence.InspectWithinOriginalSourceAsync(Own, Retain, token)).ConfigureAwait(false);
                            if (current is null || current != expected.Metadata)
                            {
                                var refused = new Snapshot(null, actor, null,
                                    "The inspected Spaces store changed. Inspect Spaces storage before requesting import.");
                                _snapshots.Add(refused, new object()); return refused;
                            }
                            _ = await Read(() => _imports.RequestImportWithinOriginalSourceAsync(Own, RetainImport, token)).ConfigureAwait(false);
                            break;
                        case SpacesOriginalStoreSetupOperation.OpenReview:
                            if (!expected!.CanOpenReview || expected.Import?.RequestId is not { } requestId ||
                                !_imports.IsIssuedOriginalSnapshot(expected.Import))
                                throw new UnauthorizedAccessException("Retain the original pending Spaces import request.");
                            var dispatch = sources.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task<HomeNativeApprovalDecisionObservation>>(() =>
                                sources.Invoke(() => _approvals.OpenAndObserveDecisionOriginalAsync(requestId, token))).GetTask());
                            Retain(dispatch);
                            var decisionTask = await sources.AwaitAsync(dispatch).ConfigureAwait(false); Retain(decisionTask);
                            var decision = await sources.AwaitAsync(decisionTask).ConfigureAwait(false);
                            if (decision.RequestId != requestId)
                                throw new InvalidDataException("Home returned a decision observation for another request.");
                            break;
                        case SpacesOriginalStoreSetupOperation.CompleteImport:
                            if (!expected!.CanCompleteImport) throw new UnauthorizedAccessException("Home has not approved this exact Spaces import.");
                            // The canonical session compares the approved evidence at the effect
                            // boundary and owns pre-effect refusal and audit-only recovery.
                            _ = await Read(() => _imports.CompleteImportWithinOriginalSourceAsync(Own, RetainImport, token)).ConfigureAwait(false);
                            break;
                        case SpacesOriginalStoreSetupOperation.RetryAudit:
                            if (!expected!.CanRetryAudit) throw new InvalidOperationException("No acknowledged Spaces import audit needs recovery.");
                            _ = await Read(() => _imports.RetryAuditWithinOriginalSourceAsync(Own, RetainImport, token)).ConfigureAwait(false);
                            break;
                        default: throw new ArgumentOutOfRangeException(nameof(operation));
                    }
                }
                var metadata = await Read(() => _evidence.InspectWithinOriginalSourceAsync(Own, Retain, token)).ConfigureAwait(false);
                HomeOriginalLocalStoreImportSession.Snapshot? import = null;
                if (metadata is not null)
                    import = await Read(() => _imports.InspectWithinOriginalSourceAsync(Own, RetainImport, token)).ConfigureAwait(false);
                var currentActor = await Read(() => _home.Profiles.GetCurrentAsync(Own, Retain, token).AsTask()).ConfigureAwait(false);
                if (actor != currentActor) throw new UnauthorizedAccessException("The actual Home profile changed during Spaces setup.");
                token.ThrowIfCancellationRequested();
                var result = new Snapshot(metadata, actor, import); _snapshots.Add(result, new object());
                _currentSnapshot = result; return result;
            }
            finally { if (entered) _serial.Release(); }
        }).ConfigureAwait(false);
    }

    internal void DemandExternalOriginalRetirementJoin()
    { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _imports.DemandExternalOriginalRetirementJoin(); }
    internal Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = DrainAsync(start.Task); start.SetResult(); return _close;
        }
    }
    private async Task DrainAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var sources = new CloudflareOriginalTaskLedger(); Task[] actual;
        lock (_gate) actual = _operations.ToArray();
        foreach (var raw in actual)
            try { await sources.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception error) { sources.Capture(raw, error); }
        try { await sources.AwaitAsync(_imports.CloseAndDrainAsync()).ConfigureAwait(false); }
        catch (Exception error) { sources.Retain(error); }
        Task[] imported; lock (_gate) imported = _importSources.ToArray();
        foreach (var raw in imported)
        {
            if (_imports.IsAcknowledgedOriginalSource(raw)) continue;
            try { await sources.AwaitAsync(raw).ConfigureAwait(false); }
            catch (Exception error) { sources.Capture(raw, error); }
        }
        if (sources.OriginalErrors.Count != 0)
            throw new AggregateException("Original Spaces setup/import did not settle cleanly; retain the same Home and store for recovery.", sources.OriginalErrors);
    }
    private void DemandAlive()
    { ObjectDisposedException.ThrowIf(_retiring || OriginalClose is not null, this); _appLifetime.ThrowIfCancellationRequested(); }
}
