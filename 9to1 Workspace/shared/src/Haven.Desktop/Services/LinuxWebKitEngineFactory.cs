using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Services;

/// <summary>Linux embedded browser engine using Avalonia's NativeWebView (WebKitGTK).</summary>
public sealed class LinuxWebKitEngineFactory : IBrowseEngineHostFactory
{
    public bool IsSupported => OperatingSystem.IsLinux();
    public string UnsupportedReason => "The Linux WebKit engine is unavailable.";
    public Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsSupported) throw new PlatformNotSupportedException(UnsupportedReason);
        if (request.TabId == Guid.Empty) throw new ArgumentException("Tab id is required.", nameof(request));
        if (!Path.IsPathFullyQualified(request.ProfileDirectory)) throw new ArgumentException("The tab must supply an absolute engine profile.", nameof(request));
        return Dispatcher.UIThread.InvokeAsync(() => (IBrowseEngineTab)new LinuxWebKitEngineTab(request)).GetTask();
    }
}

public sealed class LinuxWebKitEngineTab : IBrowseNativeEngineTab
{
    private readonly BrowseEngineTabRequest _request;
    private readonly Avalonia.Controls.NativeWebView _view = new();
    private BrowserSnapshot _state;
    private bool _disposed;
    private readonly EventHandler<Avalonia.Controls.WebViewNavigationStartingEventArgs> _navigationStarted;
    private readonly EventHandler<Avalonia.Controls.WebViewNavigationCompletedEventArgs> _navigationCompleted;
    private readonly EventHandler<Avalonia.Controls.WebViewNewWindowRequestedEventArgs> _newWindowRequested;

    public LinuxWebKitEngineTab(BrowseEngineTabRequest request)
    {
        _request = request;
        _state = new(request.InitialAddress, "Loading", false, false, true, "Loading");
        _navigationStarted = (_, args) =>
        {
            if (_disposed) return;
            _state = _state with { Address = args.Request, IsLoading = true, Status = "Loading" };
            StateChanged?.Invoke(this, _state);
        };
        _navigationCompleted = (_, args) =>
        {
            if (_disposed) return;
            _state = _state with { Address = args.Request, IsLoading = false, CanGoBack = _view.CanGoBack, CanGoForward = _view.CanGoForward, Status = args.IsSuccess ? "Loaded" : "Navigation failed" };
            StateChanged?.Invoke(this, _state);
            if (!args.IsSuccess) Crashed?.Invoke(this, new BrowseEngineCrash("Navigation failed."));
        };
        _newWindowRequested = (_, args) =>
        {
            if (_disposed || args.Request is null) return;
            args.Handled = true;
            PopupRequested?.Invoke(this, new BrowsePopupRequest(args.Request));
        };
        _view.EnvironmentRequested += OnEnvironmentRequested;
        _view.NavigationStarted += _navigationStarted;
        _view.NavigationCompleted += _navigationCompleted;
        _view.NewWindowRequested += _newWindowRequested;
        try { _view.Navigate(request.InitialAddress); }
        catch (Exception ex)
        {
            _state = _state with { IsLoading = false, Status = ex.Message };
            StateChanged?.Invoke(this, _state);
            Crashed?.Invoke(this, new BrowseEngineCrash(ex.Message));
        }
    }

    private void OnEnvironmentRequested(object? sender, Avalonia.Controls.WebViewEnvironmentRequestedEventArgs args)
    {
        if (_disposed) return;
        Directory.CreateDirectory(_request.ProfileDirectory);
        var cacheDirectory = Path.Combine(_request.ProfileDirectory, "cache");
        Directory.CreateDirectory(cacheDirectory);
        if (args is Avalonia.Platform.LinuxWpeWebViewEnvironmentRequestedEventArgs linux)
        {
            linux.DataDirectory = _request.ProfileDirectory;
            linux.CacheDirectory = cacheDirectory;
        }
        args.EnableDevTools = false;
    }

    public Control OriginalNativeView => _view;
    public bool CanRetireOriginalView => true;
    public BrowserSnapshot State => _state;
    public event EventHandler<BrowserSnapshot>? StateChanged;
#pragma warning disable CS0067
    public event EventHandler<BrowsePopupRequest>? PopupRequested;
    public event EventHandler<BrowseEngineCrash>? Crashed;
#pragma warning restore CS0067
    public Task NavigateAsync(Uri address, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        _view.Navigate(address);
        _state = _state with { Address = address, IsLoading = true, Status = "Loading" };
        StateChanged?.Invoke(this, _state);
        return Task.CompletedTask;
    }
    public Task GoBackAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); ThrowIfDisposed(); if (_view.CanGoBack) _view.GoBack(); return Task.CompletedTask; }
    public Task GoForwardAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); ThrowIfDisposed(); if (_view.CanGoForward) _view.GoForward(); return Task.CompletedTask; }
    public Task ReloadAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); ThrowIfDisposed(); _view.Refresh(); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); ThrowIfDisposed(); _view.Stop(); _state = _state with { IsLoading = false, Status = "Loading stopped." }; StateChanged?.Invoke(this, _state); return Task.CompletedTask; }
    public async Task<string?> ExecuteScriptAsync(string script, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return await _view.InvokeScript(script).ConfigureAwait(false);
    }
    public Task OpenDeveloperToolsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return Task.FromException(new PlatformNotSupportedException("Developer tools are not supported by the Linux WebKit engine."));
    }
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _view.EnvironmentRequested -= OnEnvironmentRequested;
        _view.NavigationStarted -= _navigationStarted;
        _view.NavigationCompleted -= _navigationCompleted;
        _view.NewWindowRequested -= _newWindowRequested;
        StateChanged?.Invoke(this, _state with { Status = "Closed" });
        return ValueTask.CompletedTask;
    }
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(LinuxWebKitEngineTab)); }
}
