using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Desktop.Views.Shell;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Native-only entry into the approved owning Desktop route candidate.
/// The actual Windows Home owner must supply its SAME initialized provider and authenticated
/// startup connection. No default registration or pipe/provider/actor/grant is manufactured here.</summary>
internal sealed partial class FilesNativeWindowSession : IAsyncDisposable
{
    private readonly IServiceProvider _originalProvider;
    private readonly IHomeNativeStartupSession _originalStartup;
    private readonly CancellationToken _originalConnectionLifetime;
    private readonly CancellationTokenSource _windowLifetime;
    private readonly Func<MainView, CancellationToken, Task> _originalShellInitialization;
    private readonly object _sync = new();
    private Task<FilesNativeWindowSession>? _originalShow;
    private Task? _originalShellTask;
    private Task? _originalInitializerTask;
    private Task? _originalClose;
    private NativeFilesDesktopRoute? _route;
    private MainView? _shell;
    private MainWindow? _window;
    private bool _closing;
    private bool _closeSettled;

    private FilesNativeWindowSession(IServiceProvider originalProvider,
        IHomeNativeStartupSession originalStartup, CancellationToken originalConnectionLifetime,
        CancellationToken originalHostLifetime,
        Func<MainView, CancellationToken, Task> originalShellInitialization)
    {
        _originalProvider = originalProvider;
        _originalStartup = originalStartup;
        _originalConnectionLifetime = originalConnectionLifetime;
        _originalShellInitialization = originalShellInitialization;
        _windowLifetime = CancellationTokenSource.CreateLinkedTokenSource(originalConnectionLifetime,
            originalHostLifetime);
    }

    // The owning bootstrap retains the returned SAME launch Task until it returns or refuses.
    // A failed launch independently closes everything it acquired before returning its failure.
    internal static Task<FilesNativeWindowSession> ShowOriginalAsync(IServiceProvider originalHomeProvider,
        IHomeNativeStartupSession originalStartup, CancellationToken originalConnectionLifetime,
        CancellationToken originalHostLifetime,
        Func<MainView, CancellationToken, Task> originalShellInitialization,
        CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(originalHomeProvider);
        ArgumentNullException.ThrowIfNull(originalStartup);
        ArgumentNullException.ThrowIfNull(originalShellInitialization);
        originalConnectionLifetime.ThrowIfCancellationRequested();
        originalHostLifetime.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
        if (!originalConnectionLifetime.CanBeCanceled || !originalHostLifetime.CanBeCanceled)
            throw new UnauthorizedAccessException("Retain the genuine native Home connection and host lifetimes.");
        var session = new FilesNativeWindowSession(originalHomeProvider, originalStartup,
            originalConnectionLifetime, originalHostLifetime, originalShellInitialization);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session._originalShow = session.ShowOriginalCoreAsync(start.Task, cancellationToken);
        start.SetResult();
        return session._originalShow;
    }

    private async Task<FilesNativeWindowSession> ShowOriginalCoreAsync(Task start, CancellationToken caller)
    {
        await start;
        Exception? primary = null;
        List<Exception> cleanup = [];
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(caller, _windowLifetime.Token);
            var token = operation.Token;
            _route = NativeFilesDesktopRoute.BindOriginal(_originalProvider, _originalStartup,
                _originalConnectionLifetime, _windowLifetime.Token);
            // Denied/unready/pending Home creates and publishes no normal Files window.
            await _route.AdmitOriginalShellInitializationAsync(token);
            CheckAlive();
            _shell = _originalProvider.GetRequiredService<MainView>();
            _shell.ApplyEdition(HavenStartupExperiencePolicy.Edition);
            _shell.AttachOriginalFilesRoute(_route, _originalProvider);
            AttachOriginalCanonicalTaskRoutes(_shell);
            // This is the actual owning initializer over this exact shell, not a success receipt
            // copied from another view. Its original Task is retained before awaiting it.
            var initialize = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalShellTask = InitializeOriginalShellAsync(initialize.Task, _shell, token);
            initialize.SetResult();
            await _originalShellTask;
            token.ThrowIfCancellationRequested(); CheckAlive();
            await _shell.OpenFilesAsync(false, token);
            await _shell.RevalidateOriginalFilesWindowPublicationAsync(token);
            token.ThrowIfCancellationRequested(); CheckAlive();
            var preferences = _originalProvider.GetRequiredService<UserPreferencesService>();
            _window = new MainWindow(preferences) { DataContext = _shell, PreserveWorkspaceSessionOnClose = false };
            _window.Closing += OnOriginalWindowClosing;
            // Construction stays unpublished. A fresh owning check precedes the actual native Show.
            await _shell.RevalidateOriginalFilesWindowPublicationAsync(token);
            token.ThrowIfCancellationRequested(); CheckAlive();
            _shell.CheckOriginalFilesWindowPublicationCurrent();
            _window.Show();
            token.ThrowIfCancellationRequested(); CheckAlive();
            _shell.CheckOriginalFilesWindowPublicationCurrent();
            return this;
        }
        catch (Exception error) { primary = error; }
        try { await CloseAndDrainAsync(); } catch (Exception error) { Add(cleanup, error); }
        Throw(primary, cleanup, "Original Files window launch and acquired-resource close failed.");
        throw new InvalidOperationException("Unreachable Files native launch.");
    }

    private async Task InitializeOriginalShellAsync(Task start, MainView originalShell,
        CancellationToken cancellationToken)
    {
        await start;
        CheckAlive(); cancellationToken.ThrowIfCancellationRequested();
        _originalInitializerTask = _originalShellInitialization(originalShell, cancellationToken)
            ?? throw new InvalidOperationException("The owning Desktop initializer returned no Task.");
        await _originalInitializerTask;
        cancellationToken.ThrowIfCancellationRequested(); CheckAlive();
    }

    private void OnOriginalWindowClosing(object? sender, WindowClosingEventArgs arguments)
    {
        lock (_sync)
        {
            if (_closeSettled) return;
            // Keep native window teardown behind the original asynchronous work drain.
            arguments.Cancel = true;
        }
        _ = CloseAndDrainAsync();
        // The owning host retains and awaits CloseAndDrainAsync; async-void is not used here.
    }

    internal Task CloseAndDrainAsync()
    {
        DemandExternalOriginalTaskCompositionJoin();
        lock (_sync)
        {
            if (_originalClose is not null) return _originalClose;
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalClose = CloseOriginalCoreAsync(start.Task);
            start.SetResult();
            return _originalClose;
        }
    }

    private async Task CloseOriginalCoreAsync(Task start)
    {
        await start;
        List<Exception> failures = [];
        try { _windowLifetime.Cancel(); } catch (Exception error) { Add(failures, error); }
        Task? originalRouteClose = null;
        try
        {
            originalRouteClose = _shell?.BeginOriginalFilesClose() ?? _route?.CloseAndDrainAsync();
        }
        catch (Exception error) { Add(failures, error); }
        // A session is returned only after its owning initializer settles. Failed opening invokes
        // this same close after the initializer's actual exception has been captured above.
        try { if (_originalShellTask is not null) await _originalShellTask; }
        catch (Exception error) { Add(failures, error); }
        try { if (_originalInitializerTask is not null) await _originalInitializerTask; }
        catch (Exception error) { Add(failures, error); }
        try { if (originalRouteClose is not null) await originalRouteClose; }
        catch (Exception error) { Add(failures, error); }
        async Task OnUiAsync(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else await Dispatcher.UIThread.InvokeAsync(action);
        }
        try { await OnUiAsync(() => _shell?.Dispose()); }
        catch (Exception error) { Add(failures, error); }
        lock (_sync) _closeSettled = true;
        try { await OnUiAsync(() => _window?.Close()); }
        catch (Exception error) { Add(failures, error); }
        try { _windowLifetime.Dispose(); }
        catch (Exception error) { Add(failures, error); }
        // The canonical Home provider, startup connection and core service remain borrowed.
        // Their original owner drains this retained close Task before its own teardown.
        Throw(null, failures, "Original Files view/window work and teardown failed.");
    }

    private void CheckAlive()
    {
        lock (_sync) ObjectDisposedException.ThrowIf(_closing, this);
        _originalConnectionLifetime.ThrowIfCancellationRequested();
        _windowLifetime.Token.ThrowIfCancellationRequested();
    }
    private static void Add(List<Exception> failures, Exception error)
    { if (!failures.Any(row => ReferenceEquals(row, error))) failures.Add(error); }
    private static void Throw(Exception? primary, List<Exception> cleanup, string message)
    {
        List<Exception> failures = [];
        if (primary is not null) Add(failures, primary);
        foreach (var error in cleanup) Add(failures, error);
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(message, failures);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
