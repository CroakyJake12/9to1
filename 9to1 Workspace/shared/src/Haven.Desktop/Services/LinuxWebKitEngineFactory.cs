using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Services;

/// <summary>Linux embedded browser engine using Avalonia's WebView (WebKitGTK).</summary>
public sealed class LinuxWebKitEngineFactory : IBrowseEngineHostFactory
{
    public LinuxWebKitEngineFactory()
    {
    }

    public bool IsSupported => OperatingSystem.IsLinux();
    public string UnsupportedReason => "The Linux WebKit engine is unavailable.";
    public Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsSupported) throw new PlatformNotSupportedException(UnsupportedReason);
        if (request.TabId == Guid.Empty) throw new ArgumentException("Tab id is required.", nameof(request));
        return Dispatcher.UIThread.InvokeAsync(() => (IBrowseEngineTab)new LinuxWebKitEngineTab(request)).GetTask();
    }
}

public sealed class LinuxWebKitEngineTab : IBrowseNativeEngineTab
{
    private readonly BrowseEngineTabRequest _request;
    private readonly Avalonia.Controls.NativeWebView _view = new();
    private BrowserSnapshot _state;
    private bool _disposed;

    public LinuxWebKitEngineTab(BrowseEngineTabRequest request)
    {
        _request = request;
        _state = new(request.InitialAddress, "Loading", false, false, true, "Loading");
        _view.NavigationStarted += (_, args) => { _state = _state with { Address = args.Request, IsLoading = true, Status = "Loading" }; StateChanged?.Invoke(this, _state); };
        _view.NavigationCompleted += (_, args) =>
        {
            _state = _state with { Address = args.Request, IsLoading = false, Status = args.IsSuccess ? "Loaded" : "Navigation failed" };
            StateChanged?.Invoke(this, _state);
            if (!args.IsSuccess) Crashed?.Invoke(this, new BrowseEngineCrash("Navigation failed."));
        };
        _view.NewWindowRequested += (_, args) => { args.Handled = true; if (args.Request is not null) PopupRequested?.Invoke(this, new BrowsePopupRequest(args.Request)); };
        try { _view.Navigate(request.InitialAddress); }
        catch (Exception ex) { _state = _state with { IsLoading = false, Status = ex.Message }; StateChanged?.Invoke(this, _state); Crashed?.Invoke(this, new BrowseEngineCrash(ex.Message)); }

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
        token.ThrowIfCancellationRequested(); ThrowIfDisposed();
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
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(LinuxWebKitEngineTab)); }
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
