using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Browse;
using NineToOne.Web.Productivity.Boards;

namespace Haven.Desktop.Services;

/// <summary>One retained native presentation of the SAME Boards core, authored CUI and Write
/// renderer. Home/Notes are borrowed. Real initialization, action, preparation and retirement
/// tasks remain owned even when publication fails. Private reversible close preparation freezes
/// the actual clean core; a rejected preparation restores the same editor and document.</summary>
internal sealed partial class BoardsBrowseAppContentSession : IBrowseNativeAppContentSession,
    IBrowseAppContentClosePreparationParticipant, ICuiResultReportingDispatcher, ICuiActionAvailability
{
    internal const string CuiResourceName = "Haven.Desktop.Boards.cui";
    private readonly BrowseAppContentRequest _request;
    private readonly BoardsBrowseAppContentAddress _address;
    private readonly IBoardsOriginalDesktopRoute _route;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _token;
    private readonly Func<bool> _reduceMotion;
    private readonly CuiThemePreferenceContext? _theme;
    private readonly SessionBindings _bindings;
    private readonly object _gate = new();
    private Task? _initialization, _close;
    private Task<Task>? _initializationDispatch, _closeDispatch;
    private Task? _initializationWork, _closeWork;
    private Task<IBrowseAppContentClosePreparation?>? _preparationTask;
    private readonly List<Task> _preparations = [];
    private readonly List<Task> _actions = [];
    private BoardsBrowserCore? _core;
    private BoardsCuiWorkspacePresentation? _presentation;
    private CuiSceneHost? _host;
    private Task<CuiSceneAvailability>? _show;
    private Preparation? _prepared;
    private volatile bool _published, _closing, _preparing;
    private int _activeDispatches;
    private long _acceptedDispatchVersion; // UI-thread admission sequence, never an app state or authority receipt.

    internal BoardsBrowseAppContentSession(BrowseAppContentRequest request, BoardsBrowseAppContentAddress address,
        IBoardsOriginalDesktopRoute route, CancellationTokenSource lifetime, Func<bool> reduceMotion,
        CuiThemePreferenceContext? theme = null)
    { _request = request; _address = address; _route = route; _lifetime = lifetime; _token = lifetime.Token;
        _reduceMotion = reduceMotion; _theme = theme; _bindings = new(this);
        if (route is IBoardsOriginalPresentationOwnerRoute original) original.BindOriginalPresentationOwner(this); }

    public AppContentAddress Address => _request.Address;
    public string Title => "Boards";
    public string IconKey => "boards";
    public event EventHandler? StateChanged;
    public Task? OriginalInitialization { get { lock (_gate) return _initialization; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    internal Task<CuiSceneAvailability>? OriginalCuiShow => _show;
    public bool IsPublicationCurrent => _published && !_closing && !_token.IsCancellationRequested && _route.IsAlive &&
        OriginalInitialization is { IsCompletedSuccessfully: true } && _host is not null;
    public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
    public Control OriginalNativeView
    {
        get
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsPublicationCurrent) throw new InvalidOperationException("The original Boards presentation has not completed admission.");
            _route.DemandPublicationCurrent();
            return _host!;
        }
    }
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_initialization is not null) return _initialization;
            DemandAlive();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _initialization = InitializeDriverAsync(start.Task, cancellationToken); start.SetResult(); return _initialization;
        }
    }
    private async Task InitializeDriverAsync(Task start, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        await originals.RunToOriginalSettlementAsync(async () =>
        {
            _initializationDispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task>(() => InitializeOnUiAsync(caller)).GetTask());
            _initializationWork = await originals.AwaitAsync(_initializationDispatch).ConfigureAwait(false);
            await originals.AwaitAsync(_initializationWork).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
    private async Task InitializeOnUiAsync(CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var token = scope.Token;
        DemandAlive(); token.ThrowIfCancellationRequested();
        await RevalidateOriginalAsync(token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        // A production Notes route first mounts the SAME authored CUI setup/library
        // document. CUI commands/effects choose when to acquire genuine content; a
        // missing library never masquerades as a ready editor or trigger implicit setup.
        if (_route is not IBoardsOriginalStoreSetupRoute)
            await AcquireOriginalWorkspaceAsync(token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        using var stream = typeof(BoardsBrowseAppContentSession).Assembly.GetManifestResourceStream(CuiResourceName)
            ?? throw new InvalidOperationException("The SAME canonical Boards CUI resource is unavailable.");
        using var reader = new StreamReader(stream);
        var document = new CuiDocumentSource(reader.ReadToEnd(), "Boards.cui").Document;
        var registry = new CuiControlRegistry();
        NineToOne.Web.BrowserCanonicalIcons.Register(registry);
        registry.RegisterObjectRenderer("BoardsNotebookSurface", _ => _presentation?.Control
            ?? throw new InvalidOperationException("The original Boards content has not been admitted."));
        registry.RegisterObjectRenderer("BoardsHistorySurface", _ => _presentation?.HistoryControl
            ?? throw new InvalidOperationException("The original Boards history has not been admitted."));
        registry.RegisterObjectRenderer("BoardsSharedInkSurface", _ => _presentation?.SharedInkControl
            ?? throw new InvalidOperationException("The original Boards drawing has not been admitted."));
        _host = new CuiSceneHost(registry);
        _show = _host.ShowAsync(new("boards", "Boards", "Boards", document, _bindings, this, _route)
        { ThemePreference = _theme, ControlRegistry = registry,
            IsPublicationCurrent = () => !_closing && !_token.IsCancellationRequested && _route.IsAlive }, token);
        var availability = await _show;
        if (availability.State != CuiSceneAvailabilityState.Ready)
            throw new InvalidOperationException(availability.Message);
        await RevalidateOriginalAsync(token);
        DemandAlive(); token.ThrowIfCancellationRequested(); _route.DemandPublicationCurrent();
        _published = true;
        _core?.NotifySelectionChanged();
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
        DemandAlive(); _route.DemandPublicationCurrent();
    }

    private Task RevalidateOriginalAsync(CancellationToken token)
    {
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        // Preserve synchronous/faulted OCE as an actual source fault and an actual canceled
        // Task as cancellation; an async await adapter must not relabel the former.
        return originals.RunToOriginalSettlementAsync(() => _route.RevalidateAsync(token));
    }

    private async Task AcquireOriginalWorkspaceAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_core is { IsPresentationAdmitted: true }) return;
        if (_core is null)
        {
            _core = _route.AcquireOriginalCore(); // Retain before content IO or presentation acquisition.
            _core.PrepareUnadmittedPresentation();
            _core.PropertyChanged += _bindings.ForwardOriginalChange;
        }
        await _core.ListAsync(token); // Separate actual Notes review/Begin; ownership is not content consent.
        await RevalidateOriginalAsync(token);
        if (_address.DocumentId is { } id)
        {
            var opened = await _core.OpenAsync(id, token: token);
            if (!opened.Succeeded) throw new InvalidOperationException(opened.Message);
            await RevalidateOriginalAsync(token);
        }
        DemandAlive(); token.ThrowIfCancellationRequested(); _route.DemandPublicationCurrent();
        _presentation ??= new BoardsCuiWorkspacePresentation(_core, _reduceMotion);
        InitializeSharedInkMount();
        _core.AdmitPresentation(); _core.NotifySelectionChanged();
    }

    private sealed class SessionBindings(BoardsBrowseAppContentSession owner)
        : ICuiWritableBindingContext, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool TryGetValue(string path, out object? value)
        {
            if (path == "OriginalStoreGateRequired") { value = owner._route is IBoardsOriginalStoreSetupRoute; return true; }
            if (path == "StorageSourceBusy") { value = owner._activeDispatches > 0; return true; }
            if (path == "StorageSourceIdle") { value = owner._activeDispatches == 0; return true; }
            if (path == "SharedInkStatus") { value = owner._sharedInkStatus; return true; }
            if (owner._core is { } core) return core.TryGetValue(path, out value);
            value = null; return false;
        }
        public bool TrySetValue(string path, object? value) => owner._core?.TrySetValue(path, value) == true;
        internal void ForwardOriginalChange(object? sender, PropertyChangedEventArgs args) =>
            CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
            { owner.RefreshSharedInk(); PropertyChanged?.Invoke(this, args); return true; });
        internal void DrawingChanged() => CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
        { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SharedInkStatus")); return true; });
        internal void SourcesChanged()
        {
            ForwardOriginalChange(this, new PropertyChangedEventArgs("StorageSourceBusy"));
            ForwardOriginalChange(this, new PropertyChangedEventArgs("StorageSourceIdle"));
        }
    }

    private static bool IsStorageCommand(string command) => command.StartsWith("boards.storage.", StringComparison.Ordinal);
    public bool? IsActionAvailable(string command) => !_closing && !_token.IsCancellationRequested &&
        !_preparing && _prepared is null && _route.IsAlive &&
        (command == "boards.host.observe" ? _host is not null : IsStorageCommand(command) ? _route is IBoardsOriginalStoreSetupRoute && _host is not null && _activeDispatches == 0 :
            IsPublicationCurrent && _activeDispatches == 0 && (IsSharedInkCommand(command)
                ? IsSharedInkActionAvailable(command) : _core?.IsActionAvailable(command) == true));

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
        new(DispatchWithOutcomeAsync(command, parameter, cancellationToken).AsTask());

    public ValueTask<CuiCommandOutcome> DispatchWithOutcomeAsync(string command, object? parameter,
        CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess(); DemandAlive();
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Boards action is unavailable.");
        lock (_gate)
        {
            _actions.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_actions.Count >= 128) throw new InvalidOperationException("Settle retained Boards action failures before admitting more work.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ++_activeDispatches; // Reserve the accepted source before its asynchronous driver can run.
            ++_acceptedDispatchVersion;
            var original = DispatchOriginalAsync(start.Task, command, parameter, cancellationToken);
            _actions.Add(original); start.SetResult(); return new(original);
        }
    }
    private async Task<CuiCommandOutcome> DispatchOriginalAsync(Task start, string command, object? parameter, CancellationToken caller)
    {
        await start;
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var failures = new CloudflareOriginalTaskLedger();
        try
        {
        _bindings.SourcesChanged();
        await RevalidateOriginalAsync(scope.Token);
        DemandAlive(); _route.DemandPublicationCurrent();
        object? value = null; CuiCommandOutcome? domainOutcome = null;
        if (command == "boards.host.observe")
            value = new BoardsHostStorageCapabilities(_route is IBoardsOriginalStoreSetupRoute);
        else if (IsStorageCommand(command))
        {
            var route = (IBoardsOriginalStoreSetupRoute)_route;
            if (command == "boards.storage.open") await AcquireOriginalWorkspaceAsync(scope.Token);
            else
            {
                var operation = command switch
                {
                    "boards.storage.inspect" => BoardsOriginalStoreSetupOperation.Inspect,
                    "boards.storage.initialise" => BoardsOriginalStoreSetupOperation.InitialiseIdentity,
                    "boards.storage.bind-empty" => BoardsOriginalStoreSetupOperation.BindEmpty,
                    "boards.storage.request-import" => BoardsOriginalStoreSetupOperation.RequestImport,
                    "boards.storage.review" => BoardsOriginalStoreSetupOperation.OpenReview,
                    "boards.storage.complete-import" => BoardsOriginalStoreSetupOperation.CompleteImport,
                    "boards.storage.retry-audit" => BoardsOriginalStoreSetupOperation.RetryAudit,
                    _ => throw new NotSupportedException("Unknown typed local-library operation.")
                };
                value = await route.ExecuteOriginalStoreSetupAsync(operation, parameter as BoardsOriginalStoreSetupBackend.Snapshot, scope.Token);
            }
        }
        else if (IsSharedInkCommand(command)) domainOutcome = ExecuteSharedInkCommand(command);
        else if (command == "InsertSharedDrawing" && !ProbeSharedDrawingInsertion())
            domainOutcome = new(false, false, "SharedEngineUnavailable", _sharedInkStatus, null);
        else domainOutcome = await _core!.DispatchWithOutcomeAsync(command, parameter, scope.Token);
        await RevalidateOriginalAsync(scope.Token);
        DemandAlive(); _route.DemandPublicationCurrent();
        return domainOutcome ?? new(true, false, "Succeeded", null, value);
        }
        catch (Exception error) { failures.Retain(error); throw; }
        finally
        {
            --_activeDispatches;
            try { _bindings.SourcesChanged(); }
            catch (Exception error)
            {
                failures.Retain(error);
                throw new AggregateException("Original Boards source and publication notification failed.", failures.OriginalErrors);
            }
        }
    }

    public bool SupportsReversibleClosePreparation => true;
    public async Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        var preparation = await PrepareCloseAsync(cancellationToken).ConfigureAwait(false);
        if (preparation is null) return false;
        // A legacy boolean caller cannot retain a reversible lease. Keep the live editor
        // usable when it chooses to continue; genuine strong callers retain PrepareCloseAsync.
        await preparation.AbortAsync().ConfigureAwait(false);
        return true;
    }
    public Task<IBrowseAppContentClosePreparation?> PrepareCloseAsync(CancellationToken cancellationToken = default)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate)
        {
            if (_close is not null || _closing) return Task.FromResult<IBrowseAppContentClosePreparation?>(null);
            if (_prepared is { IsCurrent: true } prepared) return Task.FromResult<IBrowseAppContentClosePreparation?>(prepared);
            if (_preparationTask is { IsCompleted: false }) return _preparationTask;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // Reserve the SAME preparation task now. Freeze admission only after already
            // accepted CUI pipelines and the causal effects they publish have settled.
            _preparationTask = PrepareDriverAsync(start.Task, cancellationToken); _preparations.Add(_preparationTask);
            start.SetResult(); return _preparationTask;
        }
    }
    private async Task<IBrowseAppContentClosePreparation?> PrepareDriverAsync(Task start, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        return await originals.RunToOriginalSettlementAsync(async () =>
        {
            var dispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task<IBrowseAppContentClosePreparation?>>(() => PrepareOnUiAsync(caller)).GetTask());
            var work = await originals.AwaitAsync(dispatch).ConfigureAwait(false);
            return await originals.AwaitAsync(work).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
    private async Task<IBrowseAppContentClosePreparation?> PrepareOnUiAsync(CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        try
        {
            var initialization = OriginalInitialization;
            if (initialization is null) return null;
            await initialization;
            DemandAlive(); scope.Token.ThrowIfCancellationRequested();
            // External preparation: each capture covers a finite set of accepted loader
            // pipelines. A parent publishes its result and admits its next CUI effect before
            // completing. Join a new capture whenever such admission changed this sequence.
            // UI-thread stable sequence + no active source permits the atomic freeze below;
            // future user actions cannot slip between that check and the freeze.
            if (_host is { } host && _show is { } show)
            {
                while (true)
                {
                    var version = _acceptedDispatchVersion;
                    var ink = CaptureSharedInkSubmissions();
                    Task[] accepted;
                    lock (_gate) accepted = _actions.ToArray();
                    var frontier = new CloudflareOriginalTaskLedger(); frontier.BindOriginalOwner(this);
                    foreach (var action in accepted) _ = frontier.Track(action);
                    foreach (var submission in ink) _ = frontier.Track(submission);
                    // Direct dispatches are owned by the session and may be absent from
                    // the loader snapshot. Retain them BEFORE capture callbacks and join
                    // their actual originals even if loader acquisition/settlement fails.
                    await frontier.RunToOriginalSettlementAsync(async () =>
                    {
                        var actual = frontier.Invoke(() => host.CaptureOriginalActionSettlement(show));
                        await frontier.AwaitAsync(actual);
                        foreach (var action in accepted) await frontier.AwaitAsync(action);
                        foreach (var submission in ink) await frontier.AwaitAsync(submission);
                    });
                    DemandAlive(); scope.Token.ThrowIfCancellationRequested();
                    if (version == _acceptedDispatchVersion && _activeDispatches == 0 &&
                        ink.Length == CaptureSharedInkSubmissions().Length) break;
                }
            }
            _preparing = true;
            RefreshSharedInk();
            await RevalidateOriginalAsync(scope.Token);
            var core = _core; var restore = core?.IsPresentationAdmitted == true;
            if (restore)
            {
                var result = await core!.PrepareToCloseAsync(scope.Token);
                if (!result.Succeeded) return null;
            }
            else if (core is { IsDirty: true } or { HasUnknownCommitOutcome: true }) return null;
            DemandAlive(); _route.DemandPublicationCurrent();
            var id = core?.DocumentId; var revision = core?.DurableRevision ?? 0; var generation = core?.EditGeneration ?? 0;
            if (restore && !core!.TrySuspendPreparedPresentation(id, revision, generation)) return null;
            return _prepared = new Preparation(this, core, restore, id, revision, generation);
        }
        finally { _preparing = false; RefreshSharedInk(); }
    }
    private sealed class Preparation(BoardsBrowseAppContentSession owner, BoardsBrowserCore? originalCore,
        bool restoreCore, Guid? id, long revision, long generation)
        : IBrowseAppContentClosePreparation
    {
        private Task? _abort;
        private bool _aborted, _accepted;
        public IBrowseAppContentSession Session => owner;
        public bool IsCurrent => !_aborted && !_accepted && !owner._closing && !owner._token.IsCancellationRequested &&
            ReferenceEquals(owner._prepared, this) && ReferenceEquals(owner._core, originalCore) &&
            (originalCore is null || originalCore is { IsPresentationAdmitted: false, IsDirty: false, HasUnknownCommitOutcome: false } core &&
                core.DocumentId == id && core.DurableRevision == revision && core.EditGeneration == generation);
        public Task AbortAsync()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(owner);
            lock (owner._gate)
            {
                if (_abort is not null) return _abort;
                if (_accepted) return Task.FromException(new InvalidOperationException("An accepted Boards close cannot be reversed."));
                return _abort = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(owner);
                    if (_aborted) return;
                    if (!IsCurrent) throw new InvalidOperationException("The original Boards preparation is no longer current.");
                    owner._prepared = null;
                    try { if (restoreCore) originalCore!.AdmitPresentation(); }
                    catch { owner._prepared = this; throw; }
                    _aborted = true;
                }).GetTask();
            }
        }
        public Task BeginClose()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(owner);
            lock (owner._gate)
            {
                if (_accepted) return owner._close!;
                if (!IsCurrent) throw new InvalidOperationException("Use the exact current original Boards preparation.");
                owner._route.DemandPublicationCurrent();
                _accepted = true;
                return owner.BeginClose();
            }
        }
    }

    public Task CloseAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate)
        {
            if (_close is not null) return _close;
            if (_prepared is { IsCurrent: true } prepared) return prepared.BeginClose();
            if (_initialization is { IsCompletedSuccessfully: true })
                throw new InvalidOperationException("Prepare the original Boards view before retirement; dirty work remains usable.");
            // Failed/pending/unstarted admission has no admitted editing surface. Its acquired
            // owners and exact original failures are still drained; no false clean retirement.
            return BeginClose();
        }
    }
    private Task BeginClose()
    {
        if (_close is not null) return _close;
        _closing = true; _published = false;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _close = CloseDriverAsync(start.Task); start.SetResult(); return _close;
    }
    private async Task CloseDriverAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        await originals.RunToOriginalSettlementAsync(async () =>
        {
            _closeDispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task>(CloseOnUiAsync).GetTask());
            _closeWork = await originals.AwaitAsync(_closeDispatch).ConfigureAwait(false);
            await originals.AwaitAsync(_closeWork).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
    private async Task CloseOnUiAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var originals = new CloudflareOriginalTaskLedger();
        async Task Join(Task? actual)
        { if (actual is null) return; try { await originals.AwaitAsync(actual); } catch (Exception error) { originals.Capture(actual, error); } }
        try { BeginSharedInkRetirement(); } catch (Exception error) { originals.Retain(error); }
        try { _lifetime.Cancel(); } catch (Exception error) { originals.Retain(error); }
        await Join(OriginalInitialization);
        await Join(_initializationDispatch); await Join(_initializationWork);
        Task[] preparations; Task[] actions;
        lock (_gate) { preparations = _preparations.ToArray(); actions = _actions.ToArray(); }
        foreach (var preparation in preparations) await Join(preparation);
        foreach (var action in actions) await Join(action);
        await Join(_sharedInkClose);
        if (_host is not null)
            try { await Join(_host.CloseOriginalAsync()); } catch (Exception error) { originals.Retain(error); }
        if (_core is not null)
        {
            _core.PropertyChanged -= _bindings.ForwardOriginalChange;
            try { await Join(_core.DisposeAsync().AsTask()); } catch (Exception error) { originals.Retain(error); }
        }
        if (_core is not { IsDirty: true } and not { HasUnknownCommitOutcome: true })
            try { _presentation?.Dispose(); } catch (Exception error) { originals.Retain(error); }
        // Route drains only tab observations; it never disposes borrowed Home/Notes/global windows.
        try { await Join(_route.CloseAndDrainAsync()); } catch (Exception error) { originals.Retain(error); }
        try { _lifetime.Dispose(); } catch (Exception error) { originals.Retain(error); }
        if (originals.OriginalErrors.Count != 0)
            throw new AggregateException("The original Boards presentation did not retire cleanly; its failed owners remain retained.", originals.OriginalErrors);
    }
    private void DemandAlive() { ObjectDisposedException.ThrowIf(_closing, this); _token.ThrowIfCancellationRequested(); }
}
