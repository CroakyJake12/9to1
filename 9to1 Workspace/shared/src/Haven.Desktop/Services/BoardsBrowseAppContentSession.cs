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
internal sealed class BoardsBrowseAppContentSession : IBrowseNativeAppContentSession,
    IBrowseAppContentClosePreparationParticipant, ICuiActionDispatcher, ICuiActionAvailability
{
    internal const string CuiResourceName = "Haven.Desktop.Boards.cui";
    private readonly BrowseAppContentRequest _request;
    private readonly BoardsBrowseAppContentAddress _address;
    private readonly IBoardsOriginalDesktopRoute _route;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _token;
    private readonly Func<bool> _reduceMotion;
    private readonly CuiThemePreferenceContext? _theme;
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

    internal BoardsBrowseAppContentSession(BrowseAppContentRequest request, BoardsBrowseAppContentAddress address,
        IBoardsOriginalDesktopRoute route, CancellationTokenSource lifetime, Func<bool> reduceMotion,
        CuiThemePreferenceContext? theme = null)
    { _request = request; _address = address; _route = route; _lifetime = lifetime; _token = lifetime.Token;
        _reduceMotion = reduceMotion; _theme = theme; }

    public AppContentAddress Address => _request.Address;
    public string Title => "Boards";
    public string IconKey => "boards";
    public event EventHandler? StateChanged;
    public Task? OriginalInitialization { get { lock (_gate) return _initialization; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public bool IsPublicationCurrent => _published && !_closing && !_token.IsCancellationRequested && _route.IsAlive &&
        OriginalInitialization is { IsCompletedSuccessfully: true } && _host is not null && _presentation is not null;
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
        _initializationDispatch = Dispatcher.UIThread.InvokeAsync<Task>(() => InitializeOnUiAsync(caller)).GetTask();
        _initializationWork = await _initializationDispatch.ConfigureAwait(false);
        await _initializationWork.ConfigureAwait(false);
    }
    private async Task InitializeOnUiAsync(CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var token = scope.Token;
        DemandAlive(); token.ThrowIfCancellationRequested();
        await _route.RevalidateAsync(token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        _core = _route.AcquireOriginalCore(); // Retain BEFORE any later IO, presentation or CUI acquisition.
        _core.PrepareUnadmittedPresentation();
        await _core.ListAsync(token); // Actual Home review/ownership and verified snapshot; no raw fallback.
        await _route.RevalidateAsync(token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        if (_address.DocumentId is { } id)
        {
            var opened = await _core.OpenAsync(id, token: token);
            if (!opened.Succeeded) throw new InvalidOperationException(opened.Message);
            await _route.RevalidateAsync(token);
        }
        DemandAlive(); token.ThrowIfCancellationRequested();
        using var stream = typeof(BoardsBrowseAppContentSession).Assembly.GetManifestResourceStream(CuiResourceName)
            ?? throw new InvalidOperationException("The SAME canonical Boards CUI resource is unavailable.");
        using var reader = new StreamReader(stream);
        var document = new CuiDocumentSource(reader.ReadToEnd(), "Boards.cui").Document;
        _presentation = new BoardsCuiWorkspacePresentation(_core, _reduceMotion);
        _host = new CuiSceneHost(_presentation.ControlRegistry);
        _show = _host.ShowAsync(new("boards", "Boards", "Boards", document, _core, this, _route)
        { ThemePreference = _theme, ControlRegistry = _presentation.ControlRegistry,
            IsPublicationCurrent = () => !_closing && !_token.IsCancellationRequested && _route.IsAlive }, token);
        var availability = await _show;
        if (availability.State != CuiSceneAvailabilityState.Ready)
            throw new InvalidOperationException(availability.Message);
        await _route.RevalidateAsync(token);
        DemandAlive(); token.ThrowIfCancellationRequested(); _route.DemandPublicationCurrent();
        _core.AdmitPresentation();
        _published = true;
        _core.NotifySelectionChanged();
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
        DemandAlive(); _route.DemandPublicationCurrent();
    }

    public bool? IsActionAvailable(string command) => IsPublicationCurrent && !_preparing && _prepared is null && _core?.IsActionAvailable(command) == true;
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        DemandAlive();
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Boards action is unavailable.");
        lock (_gate)
        {
            _actions.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_actions.Count >= 128) throw new InvalidOperationException("Settle retained Boards action failures before admitting more work.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = DispatchOriginalAsync(start.Task, command, parameter, cancellationToken);
            _actions.Add(original); start.SetResult(); return new(original);
        }
    }
    private async Task DispatchOriginalAsync(Task start, string command, object? parameter, CancellationToken caller)
    {
        await start;
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        await _route.RevalidateAsync(scope.Token);
        DemandAlive(); _route.DemandPublicationCurrent();
        await _core!.DispatchAsync(command, parameter, scope.Token);
        await _route.RevalidateAsync(scope.Token);
        DemandAlive(); _route.DemandPublicationCurrent();
    }

    public bool SupportsReversibleClosePreparation => true;
    public async Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default) =>
        await PrepareCloseAsync(cancellationToken).ConfigureAwait(false) is not null;
    public Task<IBrowseAppContentClosePreparation?> PrepareCloseAsync(CancellationToken cancellationToken = default)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate)
        {
            if (_close is not null || _closing) return Task.FromResult<IBrowseAppContentClosePreparation?>(null);
            if (_prepared is { IsCurrent: true } prepared) return Task.FromResult<IBrowseAppContentClosePreparation?>(prepared);
            if (_preparationTask is { IsCompleted: false }) return _preparationTask;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _preparing = true;
            _preparationTask = PrepareDriverAsync(start.Task, cancellationToken); _preparations.Add(_preparationTask);
            start.SetResult(); return _preparationTask;
        }
    }
    private async Task<IBrowseAppContentClosePreparation?> PrepareDriverAsync(Task start, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task<IBrowseAppContentClosePreparation?>>(() => PrepareOnUiAsync(caller)).GetTask();
        var work = await dispatch.ConfigureAwait(false);
        return await work.ConfigureAwait(false);
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
            // External preparation: exact loader actions, never an action joining itself.
            if (_host is { } host && _show is { } show) await host.CaptureOriginalActionSettlement(show);
            await _route.RevalidateAsync(scope.Token);
            var result = await _core!.PrepareToCloseAsync(scope.Token);
            if (!result.Succeeded) return null; // Genuine refused save/unknown outcome keeps same draft/view alive.
            DemandAlive(); _route.DemandPublicationCurrent();
            var id = _core.DocumentId; var revision = _core.DurableRevision; var generation = _core.EditGeneration;
            if (!_core.TrySuspendPreparedPresentation(id, revision, generation)) return null;
            return _prepared = new Preparation(this, id, revision, generation);
        }
        finally { _preparing = false; }
    }
    private sealed class Preparation(BoardsBrowseAppContentSession owner, Guid? id, long revision, long generation)
        : IBrowseAppContentClosePreparation
    {
        private Task? _abort;
        private bool _aborted, _accepted;
        public IBrowseAppContentSession Session => owner;
        public bool IsCurrent => !_aborted && !_accepted && !owner._closing && !owner._token.IsCancellationRequested &&
            ReferenceEquals(owner._prepared, this) && owner._core is { IsPresentationAdmitted: false, IsDirty: false, HasUnknownCommitOutcome: false } core &&
            core.DocumentId == id && core.DurableRevision == revision && core.EditGeneration == generation;
        public Task AbortAsync()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(owner);
            lock (owner._gate)
            {
                if (_abort is not null) return _abort;
                if (_accepted) return Task.FromException(new InvalidOperationException("An accepted Boards close cannot be reversed."));
                return _abort = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_aborted) return;
                    if (!IsCurrent) throw new InvalidOperationException("The original Boards preparation is no longer current.");
                    owner._prepared = null;
                    try { owner._core!.AdmitPresentation(); }
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
        _closeDispatch = Dispatcher.UIThread.InvokeAsync<Task>(CloseOnUiAsync).GetTask();
        _closeWork = await _closeDispatch.ConfigureAwait(false);
        await _closeWork.ConfigureAwait(false);
    }
    private async Task CloseOnUiAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        var originals = new CloudflareOriginalTaskLedger();
        async Task Join(Task? actual)
        { if (actual is null) return; try { await originals.AwaitAsync(actual); } catch (Exception error) { originals.Capture(actual, error); } }
        try { _lifetime.Cancel(); } catch (Exception error) { originals.Retain(error); }
        await Join(OriginalInitialization);
        Task[] preparations; Task[] actions;
        lock (_gate) { preparations = _preparations.ToArray(); actions = _actions.ToArray(); }
        foreach (var preparation in preparations) await Join(preparation);
        foreach (var action in actions) await Join(action);
        if (_host is not null)
            try { await Join(_host.CloseOriginalAsync()); } catch (Exception error) { originals.Retain(error); }
        if (_core is not null)
            try { await Join(_core.DisposeAsync().AsTask()); } catch (Exception error) { originals.Retain(error); }
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
