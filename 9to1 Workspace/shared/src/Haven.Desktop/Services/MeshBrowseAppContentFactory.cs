using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Borrow the actual process Home and existing Mesh services; allocate no UI or store here.</summary>
internal sealed class MeshBrowseAppContentFactory : IBrowseAppContentFactory
{
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly CancellationToken _appLifetime, _hostLifetime;
    private MeshBrowseAppContentFactory(IServiceProvider provider, HomeNativeWindowsComposition home,
        CancellationToken appLifetime, CancellationToken hostLifetime)
    { _provider = provider; _home = home; _appLifetime = appLifetime; _hostLifetime = hostLifetime; }

    internal static MeshBrowseAppContentFactory BindOriginalSameProcess(IServiceProvider provider,
        HomeNativeWindowsComposition sameHome, CancellationToken originalAppLifetime, CancellationToken originalHostLifetime)
    {
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, sameHome);
        if (!originalAppLifetime.CanBeCanceled || !originalHostLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual Mesh application and Browse host lifetimes.");
        originalAppLifetime.ThrowIfCancellationRequested(); originalHostLifetime.ThrowIfCancellationRequested();
        return new(provider, sameHome, originalAppLifetime, originalHostLifetime);
    }

    public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _appLifetime.ThrowIfCancellationRequested(); _hostLifetime.ThrowIfCancellationRequested();
        if (request.TabId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProfileId))
            throw new UnauthorizedAccessException("Retain the original Mesh tab and actual Home profile context.");
        if (request.Privacy != BrowserTabPrivacy.Standard)
            throw new NotSupportedException("The device-local Mesh workspace is unavailable in a private browser session.");
        if (request.Address.Namespace != AppContentNamespace.NativeApp || request.Address.Authority != "mesh" ||
            request.Address.ResourceSegments.Count != 0)
            throw new NotSupportedException("Open the canonical 9to1://mesh workspace; saved-document deep links are not supported by this route yet.");
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(_provider, _home);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _hostLifetime);
        try { return new MeshBrowseAppContentSession(request, _provider, _home, _appLifetime, _hostLifetime, lifetime); }
        catch { lifetime.Dispose(); throw; }
    }
}
