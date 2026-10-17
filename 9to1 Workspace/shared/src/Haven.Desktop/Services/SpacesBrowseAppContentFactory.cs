using Haven.Application;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Home;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Native Haven presentation for the real Spaces surface. It borrows the
/// shared Home services and creates one distinct Spaces view owner per tab.</summary>
internal sealed class SpacesBrowseAppContentFactory : IBrowseAppContentFactory
{
    private readonly IServiceProvider _services;
    private readonly CancellationToken _hostLifetime;

    private SpacesBrowseAppContentFactory(IServiceProvider services, CancellationToken hostLifetime)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _hostLifetime = hostLifetime;
    }

    internal static SpacesBrowseAppContentFactory BindOriginalSameProcess(IServiceProvider originalProvider,
        HomeNativeWindowsComposition sameHome, CancellationToken originalAppLifetime,
        CancellationToken originalHostLifetime)
    {
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(originalProvider, sameHome);
        ArgumentNullException.ThrowIfNull(originalProvider);
        originalAppLifetime.ThrowIfCancellationRequested();
        originalHostLifetime.ThrowIfCancellationRequested();
        if (!originalAppLifetime.CanBeCanceled || !originalHostLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual native app and host lifetimes.");
        return new(originalProvider, originalHostLifetime);
    }

    public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _hostLifetime.ThrowIfCancellationRequested();
        if (request.TabId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProfileId))
            throw new UnauthorizedAccessException("Retain the original tab and authenticated Home profile.");
        if (request.Privacy != BrowserTabPrivacy.Standard)
            throw new NotSupportedException("Spaces is not available in a private browser session.");
        return new SpacesBrowseAppContentSession(request, _services, _hostLifetime);
    }
}
