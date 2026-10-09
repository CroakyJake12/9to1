#if !ANDROID
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Assistants.NativeUI;

namespace Haven.Desktop.Views.Pages.Assistants;

/// <summary>The dedicated authored Assistants Home when the actual host cannot observe
/// its required owner. This presentation owns no Den, controller or business producer.</summary>
internal sealed class NativeAssistantsSetupDesktopPage : UserControl,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly OriginalAssistantsDependencyStatus _status;
    private readonly CancellationToken _appLifetime;
    private readonly CancellationToken _windowLifetime;
    private readonly Window _window;
    private readonly CancellationTokenSource _viewLifetime;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly TaskCompletionSource _construction = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AssistantsCuiBindings? _bindings;
    private CuiControlLoader? _loader;
    private Control? _root;
    private Avalonia.Controls.ResourceDictionary? _visualResources;
    private Task? _initialization;
    private Exception? _constructionFailure;
    private CancellationTokenRegistration _retirement;
    private int _windowClosed;
    private bool _windowSubscribed;

    private NativeAssistantsSetupDesktopPage(OriginalAssistantsDependencyStatus status,
        CancellationToken appLifetime, CancellationToken windowLifetime, Window actualWindow,
        Action<NativeAssistantsSetupDesktopPage> captureOriginalOwner)
    {
        _status = status; _appLifetime = appLifetime; _windowLifetime = windowLifetime; _window = actualWindow;
        _viewLifetime = CancellationTokenSource.CreateLinkedTokenSource(appLifetime, windowLifetime);
        _work = new(StopOriginalAsync, CloseOriginalViewAsync);
        try
        {
            CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            {
                captureOriginalOwner(this); // SAME actual partial page before any native callbacks.
                DemandPublication();
                _bindings = AssistantsCuiBindings.CreateUnavailable(status.Message);
                _window.Closed += OnOriginalWindowClosed; _windowSubscribed = true;
                _retirement = _viewLifetime.Token.Register(static owner =>
                    ((NativeAssistantsSetupDesktopPage)owner!).RequestRetirement(), this);
                DemandPublication(); return true;
            });
        }
        catch (Exception failure) { _constructionFailure = failure; _bindings?.Revoke(); throw; }
        finally { _construction.TrySetResult(); }
    }

    internal static NativeAssistantsSetupDesktopPage BindOriginal(OriginalAssistantsDependencyStatus status,
        CancellationToken appLifetime, CancellationToken windowLifetime, Window actualWindow,
        Action<NativeAssistantsSetupDesktopPage> captureOriginalOwner)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(status); ArgumentNullException.ThrowIfNull(actualWindow);
        ArgumentNullException.ThrowIfNull(captureOriginalOwner);
        if (status.IsObserved) throw new ArgumentException("A setup page requires an actual unavailable owner observation.", nameof(status));
        if (!appLifetime.CanBeCanceled || !windowLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual App and native window original lifetimes.");
        appLifetime.ThrowIfCancellationRequested(); windowLifetime.ThrowIfCancellationRequested();
        if (!actualWindow.IsVisible) throw new ObjectDisposedException("The actual native Assistants window is closed.");
        return new(status, appLifetime, windowLifetime, actualWindow, captureOriginalOwner);
    }

    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal CuiSceneAvailability Availability => new(CuiSceneAvailabilityState.Unavailable, "MissingOwner", _status.Message);
    internal bool IsOriginalWindow(Window actual, CancellationToken appLifetime, CancellationToken windowLifetime) =>
        ReferenceEquals(actual, _window) && appLifetime == _appLifetime && windowLifetime == _windowLifetime;
    internal bool IsOriginalStatus(OriginalAssistantsDependencyStatus status) => status.Code == _status.Code && status.Message == _status.Message; // Display reuse only; this never grants readiness.
    internal bool IsOriginalClosePrepared => !_work.IsRetiring && !_viewLifetime.IsCancellationRequested &&
        _constructionFailure is null && _initialization?.IsCompletedSuccessfully == true;

    internal Task InitializeAsync(CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_initialization is not null) return _initialization;
        return _work.RunAsync(original =>
        {
            caller.ThrowIfCancellationRequested();
            Acquire(original, () =>
            {
                DemandPublication();
                var appearance = CuiThemeScopeApplier.DetectAppearance();
                _visualResources = CuiSceneVisualResources.Create("Chat", appearance);
                var loader = _loader = new CuiControlLoader(); // Retain before bindings/load callbacks.
                loader.SetSurface("Chat"); loader.SetAppearance(appearance);
                loader.SetBindingContext(_bindings!); loader.SetActionDispatcher(_bindings!);
                DemandPublication();
                // CuiSceneHost's Unavailable path substitutes generic markup. Loading the SAME
                // authored Home directly preserves its dedicated identity without issuing Ready.
                var loaded = loader.TryLoad(AssistantsCuiScenes.ReadDocument(AssistantsCuiScene.Home));
                _root = loaded.Root;
                if (_root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
                    throw new InvalidDataException("The dedicated Assistants setup Home did not produce a usable native control.");
                DemandPublication(); loader.WireBindings(_root); DemandPublication();
                Resources.MergedDictionaries.Add(_visualResources);
                SetValue(ThemeVariantScope.RequestedThemeVariantProperty, CuiSceneVisualResources.Variant(appearance));
                Content = _root; DemandPublication(); original.DemandPublication(); return true;
            });
            return Task.CompletedTask; // Actual finite synchronous renderer body has completed.
        }, actual => _initialization = actual);
    }

    internal Task<bool> PrepareToCloseAsync(CancellationToken caller = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        return _work.RunAsync(original =>
        {
            caller.ThrowIfCancellationRequested();
            return Task.FromResult(Acquire(original, () => { DemandPublication(); return IsOriginalClosePrepared; }));
        }); // No draft/configuration/business work exists in this unavailable read-only presentation.
    }

    internal void PublishOriginalTab(Action callback)
    {
        Dispatcher.UIThread.VerifyAccess(); ArgumentNullException.ThrowIfNull(callback);
        if (_initialization?.IsCompletedSuccessfully != true)
            throw new InvalidOperationException("Await the SAME actual setup CUI initialization before publication.");
        _work.RunSynchronous(original => Acquire(original, () =>
        { DemandPublication(); original.DemandPublication(); callback(); DemandPublication(); original.DemandPublication(); return true; }));
    }

    private T Acquire<T>(DesktopOriginalWorkLifetime.Original original, Func<T> source)
    {
        try { return CloudflareOriginalExecutionGuard.InvokeOriginal(this, source); }
        catch (Exception failure)
        {
            original.Retain(failure);
            if (failure is OperationCanceledException)
                throw new AggregateException("The actual setup callback returned no canceled original Task.", failure);
            throw;
        }
    }
    private void DemandPublication()
    {
        Dispatcher.UIThread.VerifyAccess(); _work.DemandAdmission(); _viewLifetime.Token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _windowClosed) != 0 || !_window.IsVisible)
            throw new ObjectDisposedException("The actual Assistants setup window is closed.");
    }
    public void DemandExternalOriginalRetirementJoin()
    { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _work.DemandExternalClose(); }
    public void RequestRetirement() { _bindings?.Revoke(); _work.RequestRetirement(); }
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    private Task StopOriginalAsync() { _bindings?.Revoke(); _viewLifetime.Cancel(); return Task.CompletedTask; }
    private void OnOriginalWindowClosed(object? sender, EventArgs args)
    { Volatile.Write(ref _windowClosed, 1); RequestRetirement(); }
    private static async Task AwaitActualSourceAsync(Task actual)
    {
        try { await actual; }
        catch (Exception caught)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } payload)
            {
                if (payload.InnerExceptions.Count == 1)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(payload.InnerExceptions[0]).Throw();
                throw payload; // Actual CLR source payload, immediate siblings and opaque foreign groups preserved.
            }
            if (caught is OperationCanceledException)
                throw new AggregateException("The actual setup cleanup source canceled without an owner-issued receipt.", caught);
            throw;
        }
    }
    private async Task CloseOriginalViewAsync()
    {
        await _construction.Task;
        // Admission is sealed and the SAME binder revoked before snapshotting actual loader work.
        if (_loader is { } actualLoader)
        {
            var idle = CloudflareOriginalExecutionGuard.InvokeOriginal(this, actualLoader.WhenActionsIdleAsync);
            await AwaitActualSourceAsync(idle); // A raw failed action keeps this actual native tree and resources retained.
            await Dispatcher.UIThread.InvokeAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                () => { actualLoader.Dispose(); return true; }));
            var terminal = CloudflareOriginalExecutionGuard.InvokeOriginal(this, actualLoader.WhenActionsIdleAsync);
            await AwaitActualSourceAsync(terminal); // Join SAME actual disposal/action tasks before control/resource release.
        }
        if (_constructionFailure is { } cause)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw();
        await Dispatcher.UIThread.InvokeAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            if (ReferenceEquals(Content, _root)) Content = null;
            if (_visualResources is { } resources) Resources.MergedDictionaries.Remove(resources);
            if (_windowSubscribed) { _window.Closed -= OnOriginalWindowClosed; _windowSubscribed = false; }
            _retirement.Dispose(); _viewLifetime.Dispose(); return true;
        }));
    }
}
#endif
