using CakeOS.Cui.Themes;
using Haven.Browser;
using HavenOS.Apps.Browse;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Unstarted native Canvas presentation in the SAME canonical Home/Files process.
/// Registration creates no Files root, permission, model, installed identity or Ready state.</summary>
internal sealed class CanvasBrowseAppContentFactory : IBrowseAppContentFactory
{
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly CancellationToken _appLifetime, _hostLifetime;
    private readonly CuiThemePreferenceContext? _theme;
    private CanvasBrowseAppContentFactory(IServiceProvider provider, HomeNativeWindowsComposition home,
        CancellationToken appLifetime, CancellationToken hostLifetime, CuiThemePreferenceContext? theme)
    { _provider = provider; _home = home; _appLifetime = appLifetime; _hostLifetime = hostLifetime; _theme = theme; }
    internal static CanvasBrowseAppContentFactory BindOriginalSameProcess(IServiceProvider provider,
        HomeNativeWindowsComposition sameHome, CancellationToken appLifetime, CancellationToken hostLifetime,
        CuiThemePreferenceContext? actualThemePreference = null)
    {
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, sameHome);
        if (!appLifetime.CanBeCanceled || !hostLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual Canvas application and host lifetimes.");
        appLifetime.ThrowIfCancellationRequested(); hostLifetime.ThrowIfCancellationRequested();
        return new(provider, sameHome, appLifetime, hostLifetime, actualThemePreference);
    }
    public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _appLifetime.ThrowIfCancellationRequested(); _hostLifetime.ThrowIfCancellationRequested();
        if (request.TabId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProfileId))
            throw new UnauthorizedAccessException("Retain the original Canvas tab and Home profile context.");
        if (request.Privacy != BrowserTabPrivacy.Standard)
            throw new NotSupportedException("An owned device Canvas workspace is not a private browser session.");
        _ = CanvasBrowseAppContentAddress.Parse(request.Address);
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(_provider, _home);
        var files = _provider.GetRequiredService<NativeFilesWorkspaceService>();
        var authority = _provider.GetRequiredService<NativeFilesWorkspaceAuthority>();
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _hostLifetime);
        try
        {
            var route = new NativeCanvasDesktopRoute(_provider, _home, files, authority, request.ProfileId, _appLifetime, lifetime.Token);
            return new CanvasBrowseAppContentSession(request, route, lifetime, _theme);
        }
        catch { lifetime.Dispose(); throw; }
    }
}
