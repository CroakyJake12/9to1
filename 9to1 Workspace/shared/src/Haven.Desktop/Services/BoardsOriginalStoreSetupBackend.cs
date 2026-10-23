using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;

namespace Haven.Desktop.Services;

internal enum BoardsOriginalStoreSetupOperation
{
    Inspect, InitialiseIdentity, BindEmpty, RequestImport, OpenReview, CompleteImport, RetryAudit
}

/// <summary>Typed original storage/ownership operations. CUI owns the setup screens, local
/// state and orchestration. This owner grants neither content access nor a library-ready state.</summary>
internal sealed class BoardsOriginalStoreSetupBackend
{
    internal sealed class Snapshot
    {
        internal Snapshot(INotesOriginalStoreSetupInspection metadata, AuthenticatedResourceActor actor,
            HomeOriginalLocalStoreImportSession.Snapshot? import)
        { Metadata = metadata; Actor = actor; Import = import; }
        internal INotesOriginalStoreSetupInspection Metadata { get; }
        internal AuthenticatedResourceActor Actor { get; }
        internal HomeOriginalLocalStoreImportSession.Snapshot? Import { get; }
        public bool IsOwned => Import?.CanBrowse == true;
        public bool CanInitialise => Metadata.State == NotesOriginalStoreSetupState.MissingIdentity;
        public bool CanBindEmpty => !IsOwned && Import?.CanRequest == true &&
            Metadata.Observation is { IsEmpty: true, StoreIdentity.NewlyCreated: true };
        public bool CanRequestImport => Import?.CanRequest == true;
        public bool CanOpenReview => Import?.State == HomeOriginalLocalStoreImportState.PendingApproval;
        public bool CanCompleteImport => Import?.CanComplete == true;
        public bool CanRetryAudit => Import?.CanRetryAudit == true;
        public string Message => Import?.Reason ?? Metadata.Message;
        public string Code => Import?.State.ToString() ?? Metadata.Code;
    }

    private readonly NotesRepository _notes;
    private readonly NotesLocalStoreEvidenceProvider _evidence;
    private readonly HomeNativeWindowsComposition _home;
    private readonly HomeNativeApprovalWindowOwner _approvals;
    private readonly HomeOriginalLocalStoreImportSession _imports;
    private readonly CancellationToken _appLifetime;
    private readonly ConditionalWeakTable<Snapshot, object> _snapshots = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly List<Task> _operations = [];
    private bool _retiring;
    private Task? _close;

    internal BoardsOriginalStoreSetupBackend(NotesRepository notes, NotesLocalStoreEvidenceProvider evidence,
        HomeNativeWindowsComposition home, HomeNativeApprovalWindowOwner approvals, CancellationToken actualAppLifetime)
    {
        ArgumentNullException.ThrowIfNull(notes); ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(home); ArgumentNullException.ThrowIfNull(approvals);
        if (!actualAppLifetime.CanBeCanceled || !evidence.IsBoundToOriginalNotes(notes) ||
            !ReferenceEquals(home.Services.GetService(typeof(NotesRepository)), notes) ||
            !ReferenceEquals(home.Services.GetService(typeof(NotesLocalStoreEvidenceProvider)), evidence) ||
            !approvals.IsBoundToOriginalComposition(home))
            throw new UnauthorizedAccessException("Use the SAME original Notes metadata/evidence and Home review owners.");
        _notes = notes; _evidence = evidence; _home = home; _approvals = approvals; _appLifetime = actualAppLifetime;
        // Construction performs no IO, UUID initialisation, binding, import or approval.
        _imports = new(NotesLocalStoreEvidenceProvider.Kind, notes, home.Profiles, home.LocalStoreOwnership,
            evidence, home.Permissions, home.Ownership);
    }

    internal bool HasOriginalComposition(NotesRepository notes, NotesLocalStoreEvidenceProvider evidence,
        HomeNativeWindowsComposition home, HomeNativeApprovalWindowOwner approvals) =>
        ReferenceEquals(notes, _notes) && ReferenceEquals(evidence, _evidence) && ReferenceEquals(home, _home) &&
        ReferenceEquals(approvals, _approvals) && _imports.IsOriginalSource(notes, evidence, home.Profiles, home.Ownership);
    internal Task? OriginalClose { get { lock (_gate) return _close; } }

    internal Task<Snapshot> ExecuteWithinOriginalSourceAsync(BoardsOriginalStoreSetupOperation operation,
        Snapshot? expected, Action<Action> originalScope, Action<Task> retain, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(originalScope); ArgumentNullException.ThrowIfNull(retain);
        lock (_gate)
        {
            DemandAlive(); caller.ThrowIfCancellationRequested();
            _operations.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_operations.Count >= 128) throw new InvalidOperationException("Settle original local-library setup operations before admitting more work.");
            if (operation != BoardsOriginalStoreSetupOperation.Inspect &&
                (expected is null || !_snapshots.TryGetValue(expected, out _)))
                throw new UnauthorizedAccessException("Retain this owner's exact original setup observation.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = ExecuteAsync(start.Task, operation, expected, originalScope, retain, caller);
            _operations.Add(actual); start.SetResult(); return actual;
        }
    }

    private async Task<Snapshot> ExecuteAsync(Task start, BoardsOriginalStoreSetupOperation operation, Snapshot? expected,
        Action<Action> scope, Action<Task> retain, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, caller);
        var token = lifetime.Token;
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        originals.BindOriginalCallerCallback(scope);
        void Own(Action body) => originals.Invoke(() => { token.ThrowIfCancellationRequested(); body(); return true; });
        void Retain(Task raw) { originals.Track(raw); retain(raw); }
        async Task<T> Read<T>(Func<Task<T>> body)
        {
            Task<T>? actual = null; Exception? callbackFailure = null; T value = default!;
            try { _ = originals.Invoke(() => { actual = body(); Retain(actual); return actual; }); }
            catch (Exception error) { callbackFailure = error; originals.Retain(error); }
            if (actual is not null) value = await originals.AwaitAsync(actual).ConfigureAwait(false);
            if (callbackFailure is not null) throw new AggregateException("The original setup callback failed after source acquisition.", originals.OriginalErrors);
            return actual is null ? throw new InvalidOperationException("No original setup source task was captured.") : value;
        }
        return await originals.RunToOriginalSettlementAsync(async () =>
        {
            bool entered = false;
            try
            {
                // Capture a successful semaphore acquisition even after a caller postguard
                // fails, so no abandoned original can retain this owner's serial gate.
                Task? gateWait = null; Exception? gateFailure = null;
                try { _ = originals.Invoke(() => { gateWait = _serial.WaitAsync(token); Retain(gateWait); return gateWait; }); }
                catch (Exception error) { gateFailure = error; originals.Retain(error); }
                if (gateWait is not null) { await originals.AwaitAsync(gateWait).ConfigureAwait(false); entered = true; }
                if (gateFailure is not null) throw new AggregateException("The original setup gate callback failed.", originals.OriginalErrors);
                if (!entered) throw new InvalidOperationException("No original setup gate was acquired.");
                var actor = await Read(() => _home.Profiles.GetCurrentAsync(Own, Retain, token).AsTask()).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("Recover the actual local Home profile before configuring this library.");
                if (actor.AccountId is not null || actor.OrganisationId is not null || expected is not null && expected.Actor != actor)
                    throw new UnauthorizedAccessException("The displayed local Home profile changed. Reopen library setup.");
                if (operation != BoardsOriginalStoreSetupOperation.Inspect)
                {
                    if (expected!.Metadata.Observation is { } prior &&
                        !await Read(() => _notes.IsOriginalStoreObservationCurrentWithinOriginalSourceAsync(prior, Own, Retain, token).AsTask()).ConfigureAwait(false))
                        throw new UnauthorizedAccessException("The original library changed. Inspect it again before setup.");
                    switch (operation)
                    {
                        case BoardsOriginalStoreSetupOperation.InitialiseIdentity:
                            if (!expected.CanInitialise) throw new InvalidOperationException("Inspect the missing library identity before explicitly initialising it.");
                            _ = await Read(() => _notes.InitialiseOriginalStoreIdentityWithinOriginalSourceAsync(Own, Retain, token).AsTask()).ConfigureAwait(false);
                            break;
                        case BoardsOriginalStoreSetupOperation.BindEmpty:
                            if (!expected.CanBindEmpty) throw new UnauthorizedAccessException("Existing or unknown libraries require explicit Home import.");
                            _ = await Read(() => _home.LocalStoreOwnership.BindNewEmptyWithinOriginalSourceAsync(actor,
                                NotesLocalStoreEvidenceProvider.Kind, expected.Metadata.Observation!.StoreIdentity.StoreId.ToString("D"), Own, Retain, token)).ConfigureAwait(false);
                            break;
                        case BoardsOriginalStoreSetupOperation.RequestImport:
                            if (!expected.CanRequestImport) throw new InvalidOperationException("Inspect this original library before requesting import.");
                            _ = await Read(() => _imports.RequestImportWithinOriginalSourceAsync(Own, Retain, token)).ConfigureAwait(false);
                            break;
                        case BoardsOriginalStoreSetupOperation.OpenReview:
                            if (!expected.CanOpenReview || expected.Import?.RequestId is not { } requestId ||
                                !_imports.IsIssuedOriginalSnapshot(expected.Import))
                                throw new UnauthorizedAccessException("Retain the original pending Home import request.");
                            var dispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task<HomeNativeApprovalDecisionObservation>>(() =>
                                originals.Invoke(() => _approvals.OpenAndObserveDecisionOriginalAsync(requestId, token))).GetTask());
                            Retain(dispatch);
                            var decisionTask = await originals.AwaitAsync(dispatch).ConfigureAwait(false); Retain(decisionTask);
                            _ = await originals.AwaitAsync(decisionTask).ConfigureAwait(false);
                            // Information only. Canonical import refresh and Complete below still
                            // validate the original request, actor, evidence and actual approval.
                            break;
                        case BoardsOriginalStoreSetupOperation.CompleteImport:
                            if (!expected.CanCompleteImport) throw new UnauthorizedAccessException("Home has not approved this original import.");
                            _ = await Read(() => _imports.CompleteImportWithinOriginalSourceAsync(Own, Retain, token)).ConfigureAwait(false);
                            break;
                        case BoardsOriginalStoreSetupOperation.RetryAudit:
                            if (!expected.CanRetryAudit) throw new InvalidOperationException("No acknowledged original import audit needs recovery.");
                            _ = await Read(() => _imports.RetryAuditWithinOriginalSourceAsync(Own, Retain, token)).ConfigureAwait(false);
                            break;
                        default: throw new ArgumentOutOfRangeException(nameof(operation));
                    }
                }
                var metadata = await Read(() => _notes.InspectOriginalStoreSetupWithinOriginalSourceAsync(Own, Retain, token).AsTask()).ConfigureAwait(false);
                if (!_notes.IsIssuedOriginalSetupInspection(metadata)) throw new UnauthorizedAccessException("Foreign local-library metadata observation.");
                HomeOriginalLocalStoreImportSession.Snapshot? import = null;
                if (metadata.State == NotesOriginalStoreSetupState.Available)
                    import = await Read(() => _imports.InspectWithinOriginalSourceAsync(Own, Retain, token)).ConfigureAwait(false);
                var currentActor = await Read(() => _home.Profiles.GetCurrentAsync(Own, Retain, token).AsTask()).ConfigureAwait(false);
                if (actor != currentActor) throw new UnauthorizedAccessException("The actual Home profile changed during library setup.");
                token.ThrowIfCancellationRequested();
                var result = new Snapshot(metadata, actor, import); _snapshots.Add(result, new object()); return result;
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
        var originals = new CloudflareOriginalTaskLedger(); Task[] actual;
        lock (_gate) actual = _operations.ToArray();
        foreach (var raw in actual)
            try { await originals.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception error) { originals.Capture(raw, error); }
        try { await originals.AwaitAsync(_imports.CloseAndDrainAsync()).ConfigureAwait(false); }
        catch (Exception error) { originals.Retain(error); }
        if (originals.OriginalErrors.Count != 0)
            throw new AggregateException("Original library setup/import did not settle cleanly; retain the same Home and store for recovery.", originals.OriginalErrors);
    }
    private void DemandAlive()
    { ObjectDisposedException.ThrowIf(_retiring || OriginalClose is not null, this); _appLifetime.ThrowIfCancellationRequested(); }
}
