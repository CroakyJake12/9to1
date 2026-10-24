using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Web.Productivity.Boards;
using PermissionState = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState;

namespace Haven.Desktop.Services;

/// <summary>Structural native-host seam, not a permission issuer. Production is the actual
/// same-process Home/Notes route below; deterministic session tests do not prove Home readiness.</summary>
internal interface IBoardsOriginalDesktopRoute : ICuiSceneReadiness
{
    bool IsAlive { get; }
    BoardsBrowserCore AcquireOriginalCore();
    Task RevalidateAsync(CancellationToken cancellationToken);
    void DemandPublicationCurrent();
    Task CloseAndDrainAsync();
}

internal interface IBoardsOriginalPresentationOwnerRoute
{
    void BindOriginalPresentationOwner(BoardsBrowseAppContentSession originalOwner);
}

internal interface IBoardsOriginalSharedInkRoute
{
    HomeProductivityEngine BorrowOriginalSharedInkEngine();
}

internal interface IBoardsOriginalStoreSetupRoute : IBoardsOriginalPresentationOwnerRoute
{
    Task<BoardsOriginalStoreSetupBackend.Snapshot> ExecuteOriginalStoreSetupAsync(
        BoardsOriginalStoreSetupOperation operation, BoardsOriginalStoreSetupBackend.Snapshot? expected,
        CancellationToken cancellationToken);
}

/// <summary>The original Home observer and reviewed Notes backend for one Boards presentation.
/// Every content read/save still uses HomeNotesResourceAdmissionSource plus the original Notes
/// transaction/fence. A runtime observation or native approval-window observation is no grant.
/// Store ownership/setup/import is never manufactured here, and shared owners are never closed.</summary>
internal sealed class NativeBoardsDesktopRoute : IBoardsOriginalDesktopRoute, IBoardsOriginalStoreSetupRoute, IBoardsOriginalSharedInkRoute
{
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly HomeNotesResourceAdmissionSource _admission;
    private readonly HomeNativeApprovalWindowOwner _approvals;
    private readonly BoardsOriginalStoreSetupBackend? _setup;
    private readonly NotesOriginalWorkspaceRepository _repository;
    private readonly string _profile;
    private readonly CancellationToken _appLifetime, _lifetime;
    private readonly object _gate = new();
    private readonly List<Task> _operations = [];
    private AuthenticatedResourceActor? _actor;
    private CuiSceneAvailability? _observed;
    private bool _retiring, _coreAcquired;
    private Task? _close;
    private BoardsBrowseAppContentSession? _presentationOwner;

    internal NativeBoardsDesktopRoute(IServiceProvider provider, HomeNativeWindowsComposition home,
        NotesRepository notes, VerifiedNotesRepository verified, HomeNotesResourceAdmissionSource admission,
        HomeNativeApprovalWindowOwner approvals, string profile, string sessionId,
        CancellationToken actualAppLifetime, CancellationToken actualRouteLifetime)
        : this(provider, home, notes, verified, admission, approvals, null, profile, sessionId, actualAppLifetime, actualRouteLifetime) { }

    internal NativeBoardsDesktopRoute(IServiceProvider provider, HomeNativeWindowsComposition home,
        NotesRepository notes, VerifiedNotesRepository verified, HomeNotesResourceAdmissionSource admission,
        HomeNativeApprovalWindowOwner approvals, BoardsOriginalStoreSetupBackend? setup, string profile, string sessionId,
        CancellationToken actualAppLifetime, CancellationToken actualRouteLifetime)
    {
        _provider = provider; _home = home; _admission = admission; _approvals = approvals;
        _setup = setup;
        _profile = profile; _appLifetime = actualAppLifetime; _lifetime = actualRouteLifetime;
        _repository = NotesOriginalWorkspaceRepository.BindOriginal(notes, verified, admission,
            sessionId, PresentReadAsync, PresentSaveAsync, actualRouteLifetime, InvokeOriginalRepositoryCallback);
    }

    private void InvokeOriginalRepositoryCallback(Action body)
    {
        var parent = _presentationOwner ?? throw new InvalidOperationException("Bind the original Boards presentation before content IO.");
        CloudflareOriginalExecutionGuard.InvokeOriginal(parent, () =>
            CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; }));
    }

    public void BindOriginalPresentationOwner(BoardsBrowseAppContentSession originalOwner)
    {
        ArgumentNullException.ThrowIfNull(originalOwner);
        lock (_gate)
        {
            if (_operations.Count != 0 || _presentationOwner is not null || _retiring)
                throw new InvalidOperationException("Bind the original Boards presentation before any route work begins.");
            _presentationOwner = originalOwner;
        }
    }

    public Task<BoardsOriginalStoreSetupBackend.Snapshot> ExecuteOriginalStoreSetupAsync(
        BoardsOriginalStoreSetupOperation operation, BoardsOriginalStoreSetupBackend.Snapshot? expected,
        CancellationToken cancellationToken) => RunOriginalAsync(async originals =>
        {
            await originals.AwaitAsync(originals.Invoke(() => RevalidateAsync(cancellationToken))).ConfigureAwait(false);
            void Own(Action body) => originals.Invoke(() => { DemandAlive(); cancellationToken.ThrowIfCancellationRequested(); body(); return true; });
            var setup = _setup ?? throw new InvalidOperationException("The original local-library setup owner is unavailable.");
            var actual = originals.Invoke(() => setup.ExecuteWithinOriginalSourceAsync(operation, expected,
                Own, task => { originals.Track(task); }, cancellationToken));
            var result = await originals.AwaitAsync(actual).ConfigureAwait(false);
            await originals.AwaitAsync(originals.Invoke(() => RevalidateAsync(cancellationToken))).ConfigureAwait(false);
            return result;
        });

    public bool IsAlive { get { lock (_gate) return !_retiring && !_lifetime.IsCancellationRequested && !_appLifetime.IsCancellationRequested; } }
    public HomeProductivityEngine BorrowOriginalSharedInkEngine()
    {
        DemandPublicationCurrent();
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(_provider, _home);
        return _home.Productivity.Engine;
    }
    public BoardsBrowserCore AcquireOriginalCore()
    {
        lock (_gate)
        {
            DemandAlive();
            if (_coreAcquired) throw new InvalidOperationException("This presentation already acquired its original Boards core.");
            var core = new BoardsBrowserCore(_repository, unknownOutcome: _repository.IsUnknownCommitOutcome,
                summarySource: _repository,
                storageDetail: "Boards use the original Home-owned Notes store on this device. Account sign-in does not sync this library. Keep a separate backup of important revision material.");
            _coreAcquired = true;
            return core;
        }
    }

    public Task RevalidateAsync(CancellationToken cancellationToken) => RunOriginalAsync(async originals =>
    {
        var observation = await ObserveAsync(originals, cancellationToken).ConfigureAwait(false);
        if (observation.State != CuiSceneAvailabilityState.Ready)
            throw new InvalidOperationException(observation.Message);
        return true;
    });
    public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
        new(RunOriginalAsync(originals => ObserveAsync(originals, cancellationToken)));

    private async Task<CuiSceneAvailability> ObserveAsync(CloudflareOriginalTaskLedger originals, CancellationToken caller)
    {
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime, _appLifetime);
        var token = scope.Token;
        void Demand() { token.ThrowIfCancellationRequested(); DemandAlive(); }
        void Own(Action body) => originals.Invoke(() => { Demand(); body(); return true; });
        async Task<AuthenticatedResourceActor> ReadActorAsync()
        {
            var actor = await originals.AwaitAsync(originals.Invoke(() => _home.Profiles.GetCurrentAsync(
                Own, actual => { _ = originals.Track(actual); }, token).AsTask())).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The original Home actor is unavailable.");
            Demand();
            if (actor.ProfileId != _profile || actor.AccountId is not null || actor.OrganisationId is not null ||
                string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.AuthenticationRevision))
                throw new UnauthorizedAccessException("This Boards tab is not the actual local Home profile.");
            lock (_gate)
            {
                if (_actor is not null && _actor != actor)
                    throw new UnauthorizedAccessException("The original Boards actor changed; reopen from its current Home profile.");
                _actor ??= actor;
            }
            return actor;
        }
        Demand();
        var actor = await ReadActorAsync().ConfigureAwait(false);
        var observed = await originals.AwaitAsync(originals.Invoke(() =>
            WindowsHomeSameProcessRuntimeObservation.CheckAsync(_provider, _home, _appLifetime,
                _lifetime, Own, Demand, token))).ConfigureAwait(false);
        if (await ReadActorAsync().ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The actual Home actor changed before Boards publication.");
        Demand();
        observed = originals.Invoke(() => WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(
            _provider, _home, observed));
        lock (_gate) _observed = observed;
        Demand();
        return observed;
    }

    public void DemandPublicationCurrent()
    {
        DemandAlive();
        CuiSceneAvailability observed;
        lock (_gate)
        {
            if (_actor is null || _actor.ProfileId != _profile || _observed is not { State: CuiSceneAvailabilityState.Ready })
                throw new UnauthorizedAccessException("Boards has no current original Home observation.");
            observed = _observed!;
        }
        if (WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(_provider, _home, observed).State != CuiSceneAvailabilityState.Ready)
            throw new UnauthorizedAccessException("The original Home services are no longer available.");
        // Synchronous publication identity only. Fresh actor checks surround every awaited
        // acquisition/action; protected Notes operations independently revalidate the real actor.
        DemandAlive();
    }

    private Task PresentReadAsync(HomeNotesLibraryReadReview review, CancellationToken token) =>
        PresentOriginalAsync(() => _admission.GetLibrarySnapshotAuthorizationAsync(review, token), token);
    private Task PresentSaveAsync(HomeNotesSaveReview review, CancellationToken token) =>
        PresentOriginalAsync(() => _admission.GetSaveAuthorizationAsync(review, token), token);

    private Task PresentOriginalAsync(Func<Task<HomePermissionAuthorization>> currentAuthorization, CancellationToken token) =>
        RunOriginalAsync(async originals =>
        {
            await originals.AwaitAsync(originals.Invoke(() => RevalidateAsync(token))).ConfigureAwait(false);
            var authorization = await originals.AwaitAsync(originals.Invoke(currentAuthorization)).ConfigureAwait(false);
            if (authorization.IsAllowed && authorization.State == PermissionState.Approved) return true;
            if (authorization.State != PermissionState.PendingApproval)
                throw new UnauthorizedAccessException(authorization.Message);
            // Retain both the actual dispatcher operation AND the returned original decision wait.
            // Global Home owns the review window; canceling this route only cancels its own wait.
            var dispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task<HomeNativeApprovalDecisionObservation>>(() =>
                originals.Invoke(() => _approvals.OpenAndObserveDecisionOriginalAsync(authorization.RequestId, token))).GetTask());
            var decisionTask = await originals.AwaitAsync(dispatch).ConfigureAwait(false);
            var decision = await originals.AwaitAsync(decisionTask).ConfigureAwait(false);
            await originals.AwaitAsync(originals.Invoke(() => RevalidateAsync(token))).ConfigureAwait(false);
            if (!decision.DecisionRecorded || decision.RequestId != authorization.RequestId || decision.State != PermissionState.Approved)
                throw new UnauthorizedAccessException(decision.Message);
            return true; // Observation only: repository must still execute genuine original Begin.
        });

    private Task<T> RunOriginalAsync<T>(Func<CloudflareOriginalTaskLedger, Task<T>> body)
    {
        lock (_gate)
        {
            DemandAlive();
            _operations.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_operations.Count >= 128) throw new InvalidOperationException("Settle retained Boards Home failures before admitting more operations.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = RunBodyAsync(start.Task, body); _operations.Add(original); start.SetResult(); return original;
        }
    }
    private async Task<T> RunBodyAsync<T>(Task start, Func<CloudflareOriginalTaskLedger, Task<T>> body)
    {
        await start.ConfigureAwait(false);
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        // The physical parent scope survives restored ExecutionContext inside any
        // nested actor/store callback. A callback cannot join its pending parent.
        originals.BindOriginalCallerCallback(body =>
        {
            var parent = _presentationOwner
                ?? throw new InvalidOperationException("The original Boards presentation owner is not bound.");
            CloudflareOriginalExecutionGuard.InvokeOriginal(parent, () => { body(); return true; });
        });
        return await originals.RunToOriginalSettlementAsync(() => body(originals)).ConfigureAwait(false);
    }
    public Task CloseAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
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
        Task[] actual; lock (_gate) actual = _operations.ToArray();
        var failures = new CloudflareOriginalTaskLedger();
        foreach (var task in actual)
            try { await failures.AwaitAsync(task).ConfigureAwait(false); } catch (Exception error) { failures.Capture(task, error); }
        if (failures.OriginalErrors.Count != 0)
            throw new AggregateException("Original Boards Home operations did not settle cleanly.", failures.OriginalErrors);
        // No Home, Notes, verified repository or global approval-window disposal belongs to a tab.
    }
    private void DemandAlive()
    { ObjectDisposedException.ThrowIf(_retiring, this); _appLifetime.ThrowIfCancellationRequested(); _lifetime.ThrowIfCancellationRequested(); }
}
