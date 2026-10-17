using Haven.Application;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Only trusted native composition supplies the original Home graph and lifetimes.</summary>
internal sealed class FilesBrowseAppContentFactory : IBrowseAppContentFactory
{
    private readonly Func<CancellationToken, NativeFilesDesktopRoute> _bindOriginal;
    private readonly CancellationToken _hostLifetime;

    private FilesBrowseAppContentFactory(Func<CancellationToken, NativeFilesDesktopRoute> bind,
        CancellationToken hostLifetime)
    { _bindOriginal = bind; _hostLifetime = hostLifetime; }

    internal static FilesBrowseAppContentFactory BindOriginalSameProcess(IServiceProvider originalProvider,
        HomeNativeWindowsComposition sameHome, CancellationToken originalAppLifetime,
        CancellationToken originalHostLifetime)
    {
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(originalProvider, sameHome);
        DemandLifetimes(originalAppLifetime, originalHostLifetime);
        return new(tab => NativeFilesDesktopRoute.BindOriginalSameProcess(originalProvider, sameHome,
            originalAppLifetime, tab), originalHostLifetime);
    }

    internal static FilesBrowseAppContentFactory BindOriginal(IServiceProvider originalProvider,
        IHomeNativeStartupSession originalStartup, CancellationToken originalConnectionLifetime,
        CancellationToken originalHostLifetime)
    {
        ArgumentNullException.ThrowIfNull(originalProvider); ArgumentNullException.ThrowIfNull(originalStartup);
        DemandLifetimes(originalConnectionLifetime, originalHostLifetime);
        return new(tab => NativeFilesDesktopRoute.BindOriginal(originalProvider, originalStartup,
            originalConnectionLifetime, tab), originalHostLifetime);
    }

    private static void DemandLifetimes(CancellationToken connection, CancellationToken host)
    {
        connection.ThrowIfCancellationRequested(); host.ThrowIfCancellationRequested();
        if (!connection.CanBeCanceled || !host.CanBeCanceled)
            throw new ArgumentException("Retain the actual native connection and host lifetimes.");
    }

    public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); _hostLifetime.ThrowIfCancellationRequested();
        if (request.TabId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProfileId))
            throw new UnauthorizedAccessException("Retain the original tab and authenticated Home profile.");
        if (request.Privacy != BrowserTabPrivacy.Standard)
            throw new NotSupportedException("Files is not available in a private browser session.");
        var location = FilesBrowseAppContentAddress.Parse(request.Address);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_hostLifetime);
        try
        {
            // Allocation only: route constructors validate service identity but create no Control.
            var route = _bindOriginal(lifetime.Token);
            return new FilesBrowseAppContentSession(request, location, route, lifetime);
        }
        catch { lifetime.Dispose(); throw; }
    }
}
