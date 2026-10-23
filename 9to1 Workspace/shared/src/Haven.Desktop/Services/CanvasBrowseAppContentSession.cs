using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Canvas;

namespace Haven.Desktop.Services;

/// <summary>One retained native presentation of the canonical Canvas CUI content.
/// The route binds this parent before source work; shared Home/Files never belong to a tab.</summary>
internal sealed class CanvasBrowseAppContentSession : IBrowseNativeAppContentSession,
    IBrowseAppContentClosePreparationParticipant
{
    private readonly BrowseAppContentRequest _request;
    private readonly ICanvasOriginalDesktopRoute _route;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _token;
    private readonly CuiThemePreferenceContext? _theme;
    private readonly object _gate = new();
    private readonly List<Task> _preparations = [];
    private Task? _initialization, _close, _surfaceClose, _routeClose;
    private Task<Task>? _initializationDispatch, _closeDispatch;
    private Task? _initializationWork, _closeWork;
    private Task<IBrowseAppContentClosePreparation?>? _preparation;
    private CanvasHostSurface? _surface;
    private Preparation? _prepared;
    private volatile bool _published, _closing;

    internal CanvasBrowseAppContentSession(BrowseAppContentRequest request, ICanvasOriginalDesktopRoute route,
        CancellationTokenSource lifetime, CuiThemePreferenceContext? theme = null)
    {
        _request = request; _route = route; _lifetime = lifetime; _token = lifetime.Token; _theme = theme;
        route.BindOriginalPresentationOwner(this); // Before the first Home or native source callback.
    }
    public AppContentAddress Address => _request.Address;
    public string Title => "Canvas";
    public string IconKey => "canvas";
    public event EventHandler? StateChanged;
    public Task? OriginalInitialization { get { lock (_gate) return _initialization; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public bool IsPublicationCurrent => _published && !_closing && !_token.IsCancellationRequested && _route.IsAlive &&
        OriginalInitialization is { IsCompletedSuccessfully: true } && _surface is { IsAlive: true };
    public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
    public Control OriginalNativeView
    {
        get
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsPublicationCurrent) throw new InvalidOperationException("The original Canvas content has not completed admission.");
            _route.DemandPublicationCurrent();
            return _surface!;
        }
    }
    private CloudflareOriginalTaskLedger Ledger()
    { var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this); return originals; }
    private void CheckPreparationAdmission()
    {
        _preparations.RemoveAll(task => task.IsCompletedSuccessfully);
        if (_preparations.Count >= 128)
            throw new InvalidOperationException("Settle the retained Canvas preparation failures before further admission.");
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
        var originals = Ledger();
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
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var originals = Ledger();
        await originals.RunToOriginalSettlementAsync(async () =>
        {
            DemandAlive(); linked.Token.ThrowIfCancellationRequested();
            await originals.AwaitAsync(originals.Invoke(() => _route.RevalidateAsync(linked.Token)));
            DemandAlive(); linked.Token.ThrowIfCancellationRequested();
            // Retain the actual acquisition inside the finite callback, before its scope can fail.
            originals.Invoke(() => _surface = _route.AcquireOriginalSurface(_theme));
            var surface = _surface ?? throw new InvalidOperationException("The actual Canvas content was not retained.");
            await originals.AwaitAsync(originals.Invoke(() => surface.InitializeAsync(linked.Token)));
            await originals.AwaitAsync(originals.Invoke(() => _route.RevalidateAsync(linked.Token)));
            DemandAlive(); linked.Token.ThrowIfCancellationRequested();
            originals.Invoke(() => { _route.DemandPublicationCurrent(); return true; });
            _published = true;
            try { originals.Invoke(() => { StateChanged?.Invoke(this, EventArgs.Empty); return true; }); }
            catch { _published = false; throw; }
            DemandAlive(); originals.Invoke(() => { _route.DemandPublicationCurrent(); return true; });
        });
    }
    public bool SupportsReversibleClosePreparation => true;
    public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate)
        {
            if (_closing) return Task.FromResult(false);
            CheckPreparationAdmission();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = PrepareWithoutRetainingLeaseAsync(start.Task, cancellationToken);
            _preparations.Add(actual); start.SetResult(); return actual;
        }
    }
    private async Task<bool> PrepareWithoutRetainingLeaseAsync(Task start, CancellationToken cancellationToken)
    {
        await start.ConfigureAwait(false);
        var prepared = await PrepareCloseAsync(cancellationToken).ConfigureAwait(false);
        if (prepared is null) return false;
        await prepared.AbortAsync().ConfigureAwait(false);
        return true;
    }
    public Task<IBrowseAppContentClosePreparation?> PrepareCloseAsync(CancellationToken cancellationToken = default)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate)
        {
            if (_closing) return Task.FromResult<IBrowseAppContentClosePreparation?>(null);
            if (_prepared is { IsCurrent: true } prepared) return Task.FromResult<IBrowseAppContentClosePreparation?>(prepared);
            if (_preparation is { IsCompletedSuccessfully: false }) return _preparation;
            CheckPreparationAdmission();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _preparation = PrepareDriverAsync(start.Task, cancellationToken); _preparations.Add(_preparation);
            start.SetResult(); return _preparation;
        }
    }
    private async Task<IBrowseAppContentClosePreparation?> PrepareDriverAsync(Task start, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        var originals = Ledger();
        return await originals.RunToOriginalSettlementAsync<IBrowseAppContentClosePreparation?>(async () =>
        {
            var dispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task<IBrowseAppContentClosePreparation?>>(() => PrepareOnUiAsync(caller)).GetTask());
            var actual = await originals.AwaitAsync(dispatch).ConfigureAwait(false);
            return await originals.AwaitAsync(actual).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
    private async Task<IBrowseAppContentClosePreparation?> PrepareOnUiAsync(CancellationToken caller)
    {
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var originals = Ledger(); CanvasHostSurface.ClosePreparation? acquired = null;
        return await originals.RunToOriginalSettlementAsync<IBrowseAppContentClosePreparation?>(async () =>
        {
          try
          {
            if (_initialization is null) return null;
            await originals.AwaitAsync(_initialization);
            DemandAlive(); linked.Token.ThrowIfCancellationRequested();
            await originals.AwaitAsync(originals.Invoke(() => _route.RevalidateAsync(linked.Token)));
            acquired = await originals.AwaitAsync(originals.Invoke(() => _surface!.PrepareCloseAsync(linked.Token)));
            if (acquired is null) return null; // Real pending Home/commit work vetoes, preserving the exact live view.
            await originals.AwaitAsync(originals.Invoke(() => _route.RevalidateAsync(linked.Token)));
            DemandAlive(); originals.Invoke(() => { _route.DemandPublicationCurrent(); return true; });
            return _prepared = new Preparation(this, acquired);
          }
          catch (Exception primary)
          {
            originals.Retain(primary);
            if (acquired is not null)
                try { await originals.AwaitAsync(originals.Invoke(acquired.AbortAsync)); }
                catch (Exception error) { originals.Retain(error); }
            throw new AggregateException("Canvas close preparation did not complete.", originals.OriginalErrors);
          }
        });
    }
    private sealed class Preparation(CanvasBrowseAppContentSession owner, CanvasHostSurface.ClosePreparation original)
        : IBrowseAppContentClosePreparation
    {
        private Task? _abort;
        private bool _accepted;
        public IBrowseAppContentSession Session => owner;
        public bool IsCurrent => !_accepted && !owner._closing && !owner._token.IsCancellationRequested &&
            ReferenceEquals(owner._prepared, this) && original.IsCurrent && owner._route.IsAlive;
        public Task AbortAsync()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(owner);
            lock (owner._gate)
            {
                if (_abort is not null) return _abort;
                if (!IsCurrent) return Task.FromException(new InvalidOperationException("The original Canvas preparation is no longer current."));
                owner.CheckPreparationAdmission();
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _abort = AbortOriginalAsync(start.Task); owner._preparations.Add(_abort); start.SetResult(); return _abort;
            }
        }
        private async Task AbortOriginalAsync(Task start)
        {
            await start.ConfigureAwait(false);
            var originals = owner.Ledger();
            await originals.RunToOriginalSettlementAsync(async () =>
            {
                var dispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task>(() =>
                {
                    if (!IsCurrent) throw new InvalidOperationException("The original Canvas preparation changed before abort.");
                    return originals.Invoke(original.AbortAsync);
                }).GetTask());
                var actual = await originals.AwaitAsync(dispatch).ConfigureAwait(false);
                await originals.AwaitAsync(actual).ConfigureAwait(false);
                lock (owner._gate) if (ReferenceEquals(owner._prepared, this)) owner._prepared = null;
            }).ConfigureAwait(false);
        }
        public Task BeginClose()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(owner);
            Dispatcher.UIThread.VerifyAccess();
            if (_accepted) return owner.OriginalClose ?? throw new InvalidOperationException("The accepted Canvas close is absent.");
            if (!IsCurrent) throw new InvalidOperationException("The original Canvas preparation is no longer current.");
            owner._route.DemandPublicationCurrent();
            owner._surfaceClose = original.BeginClose(); // Seal the same content's mutation admission before publishing host close.
            _accepted = true;
            return owner.CloseAndDrainAsync();
        }
    }
    public Task CloseAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate)
        {
            if (_close is not null) return _close;
            _closing = true; _published = false;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseDriverAsync(start.Task); start.SetResult(); return _close;
        }
    }
    private async Task CloseDriverAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var originals = Ledger();
        await originals.RunToOriginalSettlementAsync(async () =>
        {
            _closeDispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task>(() => CloseOnUiAsync(originals)).GetTask());
            _closeWork = await originals.AwaitAsync(_closeDispatch).ConfigureAwait(false);
            await originals.AwaitAsync(_closeWork).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
    private async Task CloseOnUiAsync(CloudflareOriginalTaskLedger originals)
    {
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        void Capture(Action body) { try { originals.Invoke(() => { body(); return true; }); } catch (Exception error) { originals.Retain(error); } }
        void BeginContentClose()
        {
            if (_surface is not { } surface || _surfaceClose is not null) return;
            Capture(() => _surfaceClose = surface.CloseAndDrainAsync());
            // Keep an already-published original even if its finite acquisition faults.
            _surfaceClose ??= surface.OriginalCloseTask;
        }
        // Actual cancellation callbacks physically guard the concrete content AND route/parent.
        Capture(() =>
        {
            if (_surface is { } surface) surface.RunOriginalHostSource(_lifetime.Cancel);
            else _route.RunWithinOriginalSource(_lifetime.Cancel);
        });
        BeginContentClose();
        Capture(() => _routeClose = _route.CloseAndDrainAsync());
        Task[] preparations; lock (_gate) preparations = _preparations.ToArray();
        foreach (var task in new[] { _initialization }.OfType<Task>().Concat(preparations))
            try { await originals.AwaitAsync(task); } catch (Exception error) { originals.Capture(task, error); }
        BeginContentClose(); // Retain a source acquired just before retirement won publication.
        foreach (var task in new[] { _surfaceClose, _routeClose }.OfType<Task>())
            try { await originals.AwaitAsync(task); } catch (Exception error) { originals.Capture(task, error); }
        Capture(_lifetime.Dispose);
        if (originals.OriginalErrors.Count != 0)
            throw new AggregateException("Original Canvas presentation and native children did not drain.", originals.OriginalErrors);
    }
    private void DemandAlive()
    { ObjectDisposedException.ThrowIf(_closing, this); _token.ThrowIfCancellationRequested(); if (!_route.IsAlive) throw new ObjectDisposedException("The original Canvas route retired."); }
}
