using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;

namespace HavenOS.AIStudio;

public sealed class StudioNativeApplication : Application
{
    private readonly Func<CancellationToken, Task<StudioOriginalNativeHost>>? _originalHostFactory;
    private readonly StudioOriginalCallbackLifetime _callbacks = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Exception> _failures = [];
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private StudioOriginalNativeHost? _host;
    private StudioNativeWindow? _window;
    private Window? _unavailableWindow;
    private CuiSceneHost? _unavailableHost;
    private Task? _unavailableInitialization, _originalStartup, _actualFactory;
    private Task? _close, _nativeCloseRequest, _nativeFinalClose;
    private Task? _actualWindowClose, _actualConnectionClose, _actualDenClose, _actualProviderClose;
    private bool _closing, _replacingPlaceholder, _nativeExitAuthorized;
    internal static StudioNativeApplication? OriginalRunningApplication { get; private set; }

    public StudioNativeApplication() : this(null) { }

    /// <summary>The supplied factory owns its partial acquisition cleanup and must return the SAME
    /// actual protected connection/domain provider tuple. An installed trusted factory is not supplied by default.</summary>
    public StudioNativeApplication(Func<CancellationToken, Task<StudioOriginalNativeHost>>? originalHostFactory)
    { _originalHostFactory = originalHostFactory; }

    public Task? OriginalStartupTask => _originalStartup;
    public Task? OriginalCloseTask => _close;
    public Task? OriginalPhysicalConnectionCloseTask => _actualConnectionClose;
    public Task? OriginalNativeFinalCloseTask => _nativeFinalClose;
    public IReadOnlyList<Exception> OriginalFailures => _failures.ToArray();

    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Studio");

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            OriginalRunningApplication = this; _desktop = desktop;
            // No installed verifier/domain host is inferred from an app name or a Core.Read reply.
            _unavailableHost = new CuiSceneHost();
            _unavailableWindow = new Window { Title = "AI Studio", Width = 1100, Height = 850, Content = _unavailableHost };
            _unavailableWindow.Opened += OnOriginalOpened;
            _unavailableWindow.Closing += OnUnavailableClosing;
            _unavailableWindow.Closed += (_, _) =>
            {
                if (!_nativeExitAuthorized && !_replacingPlaceholder)
                    Add(new InvalidOperationException("The native Studio window closed before original drain."));
            };
            desktop.MainWindow = _unavailableWindow;
            desktop.ShutdownRequested += (_, args) =>
            {
                if (_nativeExitAuthorized) return;
                args.Cancel = true;
                BeginOriginalNativeCloseRequest();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void RequireLive(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_closing, this);
    }

    private void OnOriginalOpened(object? sender, EventArgs args)
    {
        if (_closing) return;
        try
        {
            _callbacks.Run(async token =>
            {
                RequireLive(token);
                var actualHost = _unavailableHost
                    ?? throw new InvalidOperationException("The acquired availability host is missing.");
                await actualHost.ShowAsync(StudioNativeScene.CreateUnavailable(), token);
                RequireLive(token);
            }, originalPublished: actual => _unavailableInitialization = actual);
            if (_originalHostFactory is not null)
                _callbacks.Run(StartOriginalHostAsync, originalPublished: actual => _originalStartup = actual);
        }
        catch (Exception error) { Add(error); }
    }

    private async Task StartOriginalHostAsync(CancellationToken token)
    {
        if (_unavailableInitialization is not { } actualAvailability)
            throw new InvalidOperationException("The original availability initialization is missing.");
        await actualAvailability;
        RequireLive(token);
        _actualFactory = _originalHostFactory!(token)
            ?? throw new InvalidOperationException("The original Studio host factory returned no task.");
        var acquired = await (Task<StudioOriginalNativeHost>)_actualFactory
            ?? throw new InvalidOperationException("The original Studio host factory returned no acquired owner.");
        // Capture the acquired tuple before any later cancellation/notification can refuse publication.
        _host = acquired;
        RequireLive(token);
        var actualInitialization = acquired.OriginalConnection.InitializeOriginalAsync(async originalToken =>
        {
            var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(() =>
                InitializeAcquiredWindowAsync(acquired, originalToken));
            var actual = await dispatch;
            if (actual is null) throw new InvalidOperationException("The original native initializer supplied no task.");
            await actual;
        }, token);
        await actualInitialization;
        RequireLive(token);
    }

    private async Task InitializeAcquiredWindowAsync(StudioOriginalNativeHost acquired, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        RequireLive(token);
        if (!ReferenceEquals(_host, acquired) || _window is not null)
            throw new InvalidOperationException("The original Studio host/window acquisition changed.");
        var window = new StudioNativeWindow(acquired.Services, acquired.RasterDecoder);
        _window = window;
        window.BindOriginalNativeOwnerClose(() =>
        {
            BeginOriginalNativeCloseRequest();
            return CloseAndDrainAsync();
        });
        RequireLive(token);
        _desktop!.MainWindow = window;
        RequireLive(token);
        var actualInitialization = window.StartOriginalInitializationAsync(token);
        window.Show();
        RequireLive(token);
        await actualInitialization;
        RequireLive(token);
        // The old availability scene has settled before this deliberate replacement closes it.
        if (_unavailableInitialization is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The original availability scene has not settled.");
        _replacingPlaceholder = true;
        try
        {
            _unavailableHost?.Dispose();
            _unavailableWindow?.Close();
            _unavailableHost = null; _unavailableWindow = null;
        }
        finally { _replacingPlaceholder = false; }
    }

    private void OnUnavailableClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_nativeExitAuthorized || _replacingPlaceholder) return;
        args.Cancel = true;
        BeginOriginalNativeCloseRequest();
    }

    private void BeginOriginalNativeCloseRequest()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_nativeCloseRequest is not null) return;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _nativeCloseRequest = ObserveOriginalCloseRequestAsync(start.Task);
        // The final UI/native action is published before enqueueing; it runs after the observer settles.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _nativeFinalClose = completion.Task;
        _ = _nativeCloseRequest.ContinueWith(actual =>
        {
            if (!actual.IsCompletedSuccessfully)
            {
                completion.TrySetException((Exception?)actual.Exception ??
                    new InvalidOperationException("The original native close request did not settle successfully."));
                return;
            }
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (!_nativeCloseRequest.IsCompletedSuccessfully || _close is not { IsCompletedSuccessfully: true })
                            throw new InvalidOperationException("Actual original native-owner settlement is missing.");
                        if (_window is not null) _window.AuthorizeOriginalNativeClose(_close);
                        _nativeExitAuthorized = true;
                        _desktop!.Shutdown();
                        completion.TrySetResult();
                    }
                    catch (Exception error) { Add(error); completion.TrySetException(error); }
                });
            }
            catch (Exception error) { Add(error); completion.TrySetException(error); }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        start.SetResult();
    }

    private async Task ObserveOriginalCloseRequestAsync(Task start)
    {
        await start;
        var actual = CloseAndDrainAsync();
        await actual;
        // A Window-originated request may have its own actual post-await observer; join it before native shutdown.
        if (_window?.OriginalNativeCloseObservationTask is { } observer) await observer;
    }

    public Task CloseAndDrainAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_close is not null) return _close;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _close = CloseOriginalAsync(start.Task);
        _closing = true;
        start.SetResult();
        return _close;
    }

    private async Task CloseOriginalAsync(Task start)
    {
        await start;
        List<Exception> errors = [];
        Task? actualCallbacks = null;
        Capture(() => _callbacks.CloseAndDrainAsync(), actual => actualCallbacks = actual);
        if (_window is not null) Capture(_window.CloseAndDrainAsync, actual => _actualWindowClose = actual);
        if (_host is not null) Capture(_host.OriginalConnection.CloseAndDrainAsync, actual => _actualConnectionClose = actual);
        try { _lifetime.Cancel(); } catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        if (actualCallbacks is not null) await Join(actualCallbacks);
        // An already-admitted factory can return its acquired owner after close began. Retain/drain it too.
        if (_window is not null && _actualWindowClose is null)
            Capture(_window.CloseAndDrainAsync, actual => _actualWindowClose = actual);
        if (_host is not null && _actualConnectionClose is null)
            Capture(_host.OriginalConnection.CloseAndDrainAsync, actual => _actualConnectionClose = actual);
        if (_actualWindowClose is not null) await Join(_actualWindowClose);
        if (_actualConnectionClose is not null) await Join(_actualConnectionClose);
        if (_host is not null)
        {
            Capture(_host.Den.CloseAndDrainAsync, actual => _actualDenClose = actual);
            if (_actualDenClose is not null) await Join(_actualDenClose);
        }
        foreach (var original in _failures) StudioOriginalCallbackLifetime.Add(errors, original);
        if (actualCallbacks is null || !_callbacks.OriginalsCapturedAndSettled ||
            _host is not null && (_actualWindowClose is not { IsCompletedSuccessfully: true } ||
                _actualConnectionClose is not { IsCompletedSuccessfully: true } ||
                _actualDenClose is not { IsCompletedSuccessfully: true }))
            StudioOriginalCallbackLifetime.Add(errors, new InvalidOperationException("Actual provider/native/physical-connection retirement is not established."));
        // Missing/faulted/pending producer proof cannot authorize destruction of the supplied shared provider.
        StudioOriginalCallbackLifetime.Throw(errors);
        if (_host is not null)
        {
            Capture(() => _host.OriginalProviderLifetime.DisposeAsync().AsTask(), actual => _actualProviderClose = actual);
            if (_actualProviderClose is not null) await Join(_actualProviderClose);
            if (_actualProviderClose is not { IsCompletedSuccessfully: true })
                StudioOriginalCallbackLifetime.Add(errors, new InvalidOperationException("The original provider close supplied no successful task."));
        }
        try { _unavailableHost?.Dispose(); } catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        try { _lifetime.Dispose(); } catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        StudioOriginalCallbackLifetime.Throw(errors);

        void Capture(Func<Task> acquire, Action<Task> retain)
        {
            try
            {
                var actual = acquire() ?? throw new InvalidOperationException("The original close producer returned no task.");
                retain(actual);
            }
            catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        }
        async Task Join(Task actual)
        {
            try { await actual; } catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        }
    }

    private void Add(Exception error) => StudioOriginalCallbackLifetime.Add(_failures, error);

    internal int VerifyOriginalNativeExit(int nativeExitCode)
    {
        if (_close is not { IsCompletedSuccessfully: true } ||
            _nativeCloseRequest is not { IsCompletedSuccessfully: true } ||
            _nativeFinalClose is not { IsCompletedSuccessfully: true } ||
            !_nativeExitAuthorized || _window?.NativeClosedWithoutOriginalDrain == true || _failures.Count != 0)
            return nativeExitCode == 0 ? 1 : nativeExitCode;
        return nativeExitCode;
    }
}
