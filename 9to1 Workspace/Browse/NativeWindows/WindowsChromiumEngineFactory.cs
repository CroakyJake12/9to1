using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Microsoft.Web.WebView2.Core;

namespace HavenOS.Apps.Browse;

/// <summary>Actual Windows Blink/WebView2 producer. Firefox remains a distinct
/// unavailable backend until its own installed adapter is supplied.</summary>
public sealed class WindowsChromiumEngineFactory : IBrowseEngineHostFactory, IBrowseEngineAvailability
{
    private readonly Func<Guid, IBrowseEngineTab, Uri, BrowserSitePermissionKind, BrowserSitePermissionDecision> _originalPermission;
    private readonly BrowserDownloadTransport? _originalTransport;
    private readonly IBrowserNativeDownloadService? _originalDownloads;
    public WindowsChromiumEngineFactory(
        Func<Guid, IBrowseEngineTab, Uri, BrowserSitePermissionKind, BrowserSitePermissionDecision> originalPermission,
        BrowserDownloadTransport? originalTransport = null, IBrowserNativeDownloadService? originalDownloads = null)
    {
        _originalPermission = originalPermission ?? throw new ArgumentNullException(nameof(originalPermission));
        if ((originalTransport is null) != (originalDownloads is null)) throw new ArgumentException("Native downloads require the same transport and approval owner together.");
        _originalTransport = originalTransport; _originalDownloads = originalDownloads;
    }
    public bool IsSupported
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try { return !string.IsNullOrWhiteSpace(CoreWebView2Environment.GetAvailableBrowserVersionString(null)); }
            catch (Exception unavailable) when (unavailable is WebView2RuntimeNotFoundException or DllNotFoundException or BadImageFormatException) { return false; }
        }
    }
    public string UnsupportedReason => "The Windows Chromium engine requires WebView2 on Windows. Firefox has no installed engine supplier.";
    public bool IsEngineSupported(BrowseEngineKind engine) => IsSupported && engine == BrowseEngineKind.Chromium;
    public string UnsupportedReasonFor(BrowseEngineKind engine) => engine == BrowseEngineKind.Gecko
        ? "The Firefox engine is not installed. Your tab and Firefox preference are retained. Choose Chromium when its Windows engine is available."
        : "The Chromium engine requires Windows and the installed WebView2 runtime.";
    public Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsEngineSupported(request.Engine)) throw new PlatformNotSupportedException(UnsupportedReasonFor(request.Engine));
        if (request.TabId == Guid.Empty || !Path.IsPathFullyQualified(request.ProfileDirectory)) throw new ArgumentException("The canonical tab must supply its original isolated absolute engine profile.");
        return Dispatcher.UIThread.InvokeAsync(() =>
        { token.ThrowIfCancellationRequested(); return (IBrowseEngineTab)new WindowsChromiumEngineTab(request, _originalPermission, _originalTransport, _originalDownloads); }).GetTask();
    }
}

internal sealed class WindowsChromiumEngineTab : IBrowseNativeEngineTab, IBrowseOriginalPageCommitSource, IBrowseEngineRetirementGuard, IBrowseOriginalDownloadContentSource
{
    private readonly BrowseEngineTabRequest _originalRequest;
    private readonly NativeWebView _view = new();
    private readonly Func<Guid, IBrowseEngineTab, Uri, BrowserSitePermissionKind, BrowserSitePermissionDecision> _originalPermission;
    private readonly BrowserDownloadTransport? _transport;
    private readonly IBrowserNativeDownloadService? _downloads;
    private readonly List<Task> _originalSources = [];
    private readonly object _sourceGate = new();
    private CoreWebView2? _originalCore;
    private IPlatformHandle? _originalCreatedHandle, _originalDestroyedHandle;
    private WebViewAdapterEventArgs? _originalDestroyedArguments;
    private IAsyncDisposable? _originalRetirementScope;
    private Task? _originalRetirementScopeClose;
    private readonly TaskCompletionSource<WebViewAdapterEventArgs> _originalAdapterDestroyed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BrowseChromiumDownloadBridge? _originalDownloadBridge;
    private BrowserSnapshot _state;
    private Task? _originalClose;
    private bool _retiring, _disposed;
    public Control OriginalNativeView => _view;
    public IBrowserOriginalDownloadContent? ObserveOriginalDownloadContent(BrowserDownloadRecord currentCanonicalRow)
    {
        Dispatcher.UIThread.VerifyAccess();
        using var invocation = OriginalNativeInvocation.EnterExternal(this);
        return _retiring || _disposed ? null : _originalDownloadBridge?.ObserveOriginalDownloadContent(currentCanonicalRow);
    }
    public bool CanRetireOriginalView => _originalDownloadBridge?.CanRetireOriginal == true || _originalDownloadBridge is null;
    public BrowserSnapshot State => _state;
    public event EventHandler<BrowserSnapshot>? StateChanged;
    public event EventHandler<BrowsePopupRequest>? PopupRequested;
    public event EventHandler<BrowseEngineCrash>? Crashed;
    public event EventHandler<BrowserSnapshot>? PageCommitted;
    internal WindowsChromiumEngineTab(BrowseEngineTabRequest request,
        Func<Guid, IBrowseEngineTab, Uri, BrowserSitePermissionKind, BrowserSitePermissionDecision> originalPermission,
        BrowserDownloadTransport? transport, IBrowserNativeDownloadService? downloads)
    {
        _originalRequest = request; _originalPermission = originalPermission; _transport = transport; _downloads = downloads;
        _state = new(request.InitialAddress, request.InitialAddress.Host, false, false, false, "Chromium is waiting for its native surface.");
        _view.EnvironmentRequested += OnEnvironmentRequested;
        _view.AdapterCreated += OnAdapterCreated; _view.AdapterDestroyed += OnAdapterDestroyed;
        _view.NavigationStarted += OnNavigationStarted; _view.NavigationCompleted += OnNavigationCompleted;
        _view.NewWindowRequested += OnPopup;
        // NativeWebView queues this original URI until its real native adapter exists.
        _view.Source = request.InitialAddress;
    }
    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
        using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
        if (args is not WindowsWebView2EnvironmentRequestedEventArgs windows)
            throw new PlatformNotSupportedException("The original Chromium view did not request the Windows WebView2 environment.");
        windows.UserDataFolder = _originalRequest.ProfileDirectory;
        windows.ProfileName = _originalRequest.Privacy == BrowserTabPrivacy.Private ? "Private-" + _originalRequest.TabId.ToString("N") : "9to1";
        windows.IsInPrivateModeEnabled = _originalRequest.Privacy == BrowserTabPrivacy.Private;
    }
    private void OnAdapterCreated(object? sender, WebViewAdapterEventArgs args)
    {
        using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
        var originalHandle = args.TryGetPlatformHandle();
        _originalCreatedHandle = originalHandle;
        if (_retiring || _disposed) return;
        if (originalHandle is not IWindowsWebView2PlatformHandle handle || handle.CoreWebView2 == IntPtr.Zero)
            throw new PlatformNotSupportedException("Chromium requires the original Windows WebView2 platform handle.");
        _originalCore = CoreWebView2.CreateFromComICoreWebView2(handle.CoreWebView2);
        _originalCore.PermissionRequested += OnPermission;
        _originalCore.ProcessFailed += OnProcessFailed;
        _originalDownloadBridge = new(_originalCore, _originalRequest.Privacy == BrowserTabPrivacy.Private, _transport, _downloads);
        Publish("Chromium native renderer connected.", false);
    }
    private void OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs args)
    {
        using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
        // Observe actual source-issued handle/arguments before retirement guards.
        // The same view owns this event; a queued UI detach is not its receipt.
        _originalDestroyedArguments = args; _originalDestroyedHandle = args.TryGetPlatformHandle();
        if (!ReferenceEquals(sender, _view))
        { _originalAdapterDestroyed.TrySetException(new InvalidOperationException("A foreign view issued the native retirement event.")); return; }
        _originalAdapterDestroyed.TrySetResult(args);
        if (_retiring || _disposed) return;
        _state = _state with { IsLoading = false, Status = "The original Chromium adapter stopped." };
        Crashed?.Invoke(this, new(_state.Status));
    }
    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args)
    {
        using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
        if (_retiring || _disposed) return;
        Crashed?.Invoke(this, new("Chromium process failed: " + args.ProcessFailedKind));
    }
    private void OnPermission(object? sender, CoreWebView2PermissionRequestedEventArgs args)
    {
        using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
        args.Handled = true; args.SavesInProfile = false;
        args.State = CoreWebView2PermissionState.Deny;
        if (_retiring || _disposed || !Uri.TryCreate(args.Uri, UriKind.Absolute, out var origin)) return;
        if (!Enum.TryParse<BrowserSitePermissionKind>(args.PermissionKind.ToString(), out var kind) || !Enum.IsDefined(kind)) return;
        var decision = _originalPermission(_originalRequest.TabId, this, origin, kind);
        if (_retiring || _disposed) return;
        args.State = decision switch { BrowserSitePermissionDecision.Allow => CoreWebView2PermissionState.Allow,
            BrowserSitePermissionDecision.Ask => CoreWebView2PermissionState.Default, _ => CoreWebView2PermissionState.Deny };
    }
    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs args)
    {
        using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
        if (_retiring || _disposed) { args.Cancel = true; return; }
        var assessment = BrowserNativeRequestPolicy.AssessTopLevel(args.Request);
        if (!assessment.IsAllowed) { args.Cancel = true; Publish("Navigation blocked: " + assessment.Reason, false); return; }
        _state = _state with { Address = args.Request, Title = args.Request?.Host ?? "Browse" };
        Publish("Loading…", true);
    }
    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
        if (_retiring || _disposed) return;
        if (args.Request is not null) _state = _state with { Address = args.Request };
        if (args.IsSuccess && _originalCore is not null) _state = _state with { Title = _originalCore.DocumentTitle };
        Publish(args.IsSuccess ? "Chromium page loaded." : "Chromium could not load this page.", false);
        if (args.IsSuccess && args.Request is { Scheme: "http" or "https" }) PageCommitted?.Invoke(this, _state);
    }
    private void OnPopup(object? sender, WebViewNewWindowRequestedEventArgs args)
    {
        using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
        args.Handled = true;
        if (_retiring || _disposed || args.Request is null) return;
        PopupRequested?.Invoke(this, new(args.Request));
    }
    private void Publish(string status, bool loading)
    {
        using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
        _state = _state with { CanGoBack = _view.CanGoBack, CanGoForward = _view.CanGoForward, IsLoading = loading, Status = status };
        StateChanged?.Invoke(this, _state);
    }
    private Task<T> Issue<T>(Func<Task<T>> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_sourceGate)
        {
            ObjectDisposedException.ThrowIf(_retiring || _disposed, this);
            _originalSources.RemoveAll(source => source.IsCompletedSuccessfully);
            if (_originalSources.Count >= 128) throw new InvalidOperationException("Chromium retains unresolved original sources.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = RunOriginalAsync(start.Task, operation, token); _originalSources.Add(original); start.SetResult(); return original;
        }
    }
    private async Task<T> RunOriginalAsync<T>(Task start, Func<Task<T>> operation, CancellationToken token)
    {
        using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
        await start.ConfigureAwait(false); token.ThrowIfCancellationRequested();
        var originalUi = Dispatcher.UIThread.InvokeAsync(() =>
        { using var invocation = OriginalNativeInvocation.EnterExternal(this); token.ThrowIfCancellationRequested(); return operation(); }).GetTask();
        lock (_sourceGate) _originalSources.Add(originalUi);
        var originalBody = await originalUi.ConfigureAwait(false);
        lock (_sourceGate) _originalSources.Add(originalBody);
        return await originalBody.ConfigureAwait(false);
    }
    private Task Issue(Action operation, CancellationToken token) => Issue(() => { operation(); return Task.FromResult(true); }, token);
    public Task NavigateAsync(Uri address, CancellationToken token) => Issue(() =>
    {
        var assessment = BrowserNativeRequestPolicy.AssessTopLevel(address);
        if (!assessment.IsAllowed) throw new ArgumentException(assessment.Reason, nameof(address));
        _view.Navigate(address);
    }, token);
    public Task GoBackAsync(CancellationToken token) => Issue(() => { if (_view.CanGoBack) _view.GoBack(); }, token);
    public Task GoForwardAsync(CancellationToken token) => Issue(() => { if (_view.CanGoForward) _view.GoForward(); }, token);
    public Task ReloadAsync(CancellationToken token) => Issue(() => { _view.Refresh(); }, token);
    public Task StopAsync(CancellationToken token) => Issue(() => { _view.Stop(); Publish("Loading stopped.", false); }, token);
    public Task<string?> ExecuteScriptAsync(string script, CancellationToken token) => Issue(() => _view.InvokeScript(script), token);
    public Task OpenDeveloperToolsAsync(CancellationToken token) => Issue(() =>
    {
        var core = _originalCore ?? throw new PlatformNotSupportedException("The original Chromium adapter is not connected.");
        core.OpenDevToolsWindow();
    }, token);
    public void DemandExternalJoin()
    { OriginalNativeInvocation.DemandExternalJoin(this); using var guard = OriginalNativeInvocation.EnterExternal(this); _originalDownloadBridge?.DemandExternalJoin(); }
    public ValueTask DisposeAsync()
    {
        DemandExternalJoin();
        lock (_sourceGate)
        {
            if (_originalClose is not null) return new(_originalClose);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _retiring = true; _originalClose = CloseAsync(start.Task); start.SetResult(); return new(_originalClose);
        }
    }
    private async Task CloseAsync(Task start)
    {
        using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
        await start; var failures = new List<Exception>();
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] originals; lock (_sourceGate) originals = _originalSources.Where(source => !joined.Contains(source)).Distinct().ToArray();
            if (originals.Length == 0) break;
            foreach (var source in originals)
            { joined.Add(source); try { await source; } catch (Exception failure) { failures.Add(failure); } }
        }
        if (_originalDownloadBridge is not null)
        {
            var originalBridgeRequest = Dispatcher.UIThread.InvokeAsync(() => { using var invocation = OriginalNativeInvocation.EnterExternal(this); return _originalDownloadBridge.DisposeAsync().AsTask(); }).GetTask();
            lock (_sourceGate) _originalSources.Add(originalBridgeRequest);
            try
            {
                var originalBridgeClose = await originalBridgeRequest;
                lock (_sourceGate) _originalSources.Add(originalBridgeClose);
                await originalBridgeClose;
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0) throw new AggregateException("Chromium retained its original source failures.", failures);
        var originalRetirement = Dispatcher.UIThread.InvokeAsync(() =>
        {
            using var invocation = OriginalNativeInvocation.EnterExternal(this);
            // Public package API returns this SAME native scope directly.
            // Its asynchronous disposal owns pending native detach/destruction.
            _originalRetirementScope = _view.BeginReparentingAsync();
            if (_originalCore is not null) { _originalCore.PermissionRequested -= OnPermission; _originalCore.ProcessFailed -= OnProcessFailed; }
            _view.NavigationStarted -= OnNavigationStarted; _view.NavigationCompleted -= OnNavigationCompleted; _view.NewWindowRequested -= OnPopup;
            if (_view.Parent is Panel panel) panel.Children.Remove(_view);
            else if (_view.Parent is ContentControl content && ReferenceEquals(content.Content, _view)) content.Content = null;
            else if (_view.Parent is not null) throw new InvalidOperationException("The original native view is still held by an unrecognised owner.");
        }).GetTask();
        lock (_sourceGate) _originalSources.Add(originalRetirement);
        try { await originalRetirement.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        if (_originalRetirementScope is { } sameOriginalScope)
        {
            try
            {
                var originalScopeRequest = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    using var invocation = OriginalNativeInvocation.EnterExternal(this);
                    _originalRetirementScopeClose ??= sameOriginalScope.DisposeAsync().AsTask();
                    return _originalRetirementScopeClose;
                }).GetTask();
                lock (_sourceGate) _originalSources.Add(originalScopeRequest);
                var originalScopeClose = await originalScopeRequest.ConfigureAwait(false);
                lock (_sourceGate) _originalSources.Add(originalScopeClose);
                await originalScopeClose.ConfigureAwait(false);
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        else failures.Add(new InvalidOperationException("The original native view did not issue its asynchronous retirement scope."));
        if (failures.Count != 0) throw new AggregateException("Chromium retained its actual native retirement failures.", failures);
        if (_originalCreatedHandle is not null)
        {
            lock (_sourceGate) _originalSources.Add(_originalAdapterDestroyed.Task);
            var actualDestroyed = await _originalAdapterDestroyed.Task.ConfigureAwait(false);
            if (!ReferenceEquals(actualDestroyed, _originalDestroyedArguments))
                throw new InvalidOperationException("The retained native destruction event changed.");
            if (_originalCreatedHandle is IWindowsWebView2PlatformHandle created &&
                _originalDestroyedHandle is IWindowsWebView2PlatformHandle destroyed &&
                created.CoreWebView2 != IntPtr.Zero && destroyed.CoreWebView2 != IntPtr.Zero && created.CoreWebView2 != destroyed.CoreWebView2)
                throw new InvalidOperationException("A different native Chromium adapter issued the destruction event.");
        }
        var originalRetiredUi = Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_view.Parent is not null) throw new InvalidOperationException("The original native view still belongs to a mounted owner.");
            _view.EnvironmentRequested -= OnEnvironmentRequested;
            _view.AdapterCreated -= OnAdapterCreated; _view.AdapterDestroyed -= OnAdapterDestroyed;
            _disposed = true;
        }).GetTask();
        lock (_sourceGate) _originalSources.Add(originalRetiredUi);
        await originalRetiredUi.ConfigureAwait(false);
    }
}
