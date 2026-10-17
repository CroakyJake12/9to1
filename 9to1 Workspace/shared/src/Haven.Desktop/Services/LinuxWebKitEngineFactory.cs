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
    private readonly Func<Guid, IBrowseEngineTab, Uri, BrowserSitePermissionKind, BrowserSitePermissionDecision> _permission;
    public LinuxWebKitEngineFactory(Func<Guid, IBrowseEngineTab, Uri, BrowserSitePermissionKind, BrowserSitePermissionDecision> permission)
        => _permission = permission ?? throw new ArgumentNullException(nameof(permission));

    public bool IsSupported => OperatingSystem.IsLinux();
    public string UnsupportedReason => "The Linux WebKit engine is unavailable.";
    public Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsSupported) throw new PlatformNotSupportedException(UnsupportedReason);
        if (request.TabId == Guid.Empty) throw new ArgumentException("Tab id is required.", nameof(request));
        return Dispatcher.UIThread.InvokeAsync(() => (IBrowseEngineTab)new LinuxWebKitEngineTab(request, _permission)).GetTask();
    }
}

public sealed class LinuxWebKitEngineTab : IBrowseNativeEngineTab
{
    private readonly BrowseEngineTabRequest _request;
    private readonly Func<Guid, IBrowseEngineTab, Uri, BrowserSitePermissionKind, BrowserSitePermissionDecision> _permission;
    private readonly Avalonia.Controls.NativeWebView _view = new();
    private BrowserSnapshot _state;

    public LinuxWebKitEngineTab(BrowseEngineTabRequest request, Func<Guid, IBrowseEngineTab, Uri, BrowserSitePermissionKind, BrowserSitePermissionDecision> permission)
    {
        _request = request;
        _permission = permission;
        _state = new(request.InitialAddress, "Loading", false, false, true, "Loading");
        _view.NavigationCompleted += (_, _) => { _state = _state with { IsLoading = false, Title = "Page" }; StateChanged?.Invoke(this, _state); };
        try { _view.Navigate(request.InitialAddress); } catch { }
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
        _view.Navigate(address);
        _state = _state with { Address = address, IsLoading = true };
        StateChanged?.Invoke(this, _state);
        return Task.CompletedTask;
    }
    public Task GoBackAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); if (_view.CanGoBack) _view.GoBack(); return Task.CompletedTask; }
    public Task GoForwardAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); if (_view.CanGoForward) _view.GoForward(); return Task.CompletedTask; }
    public Task ReloadAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); _view.Refresh(); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); _view.Stop(); _state = _state with { IsLoading = false, Status = "Loading stopped." }; StateChanged?.Invoke(this, _state); return Task.CompletedTask; }
    public Task<string?> ExecuteScriptAsync(string script, CancellationToken token) => Task.FromResult<string?>(null);
    public Task OpenDeveloperToolsAsync(CancellationToken token) => Task.CompletedTask;
    public ValueTask DisposeAsync() { return ValueTask.CompletedTask; }
}
