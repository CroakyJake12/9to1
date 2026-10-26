using Haven.Desktop.Views.Pages.Tasks;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Home;
using HavenOS.Home.Core;
using Avalonia.Controls;

namespace Haven.Desktop.Services;

/// <summary>Native Haven presentation for the real Spaces surface. It borrows the
/// shared Home services and creates one distinct Spaces view owner per tab.</summary>
internal sealed class SpacesBrowseAppContentFactory : IBrowseAppContentFactory
{
    private readonly IServiceProvider _services;
    private readonly CancellationToken _hostLifetime, _appLifetime;
    private readonly Func<SpaceTaskWidgetPage, NativeCanonicalTaskSceneReadiness> _bindTaskFrame;
    // The same typed host navigation contract the Home launcher already uses. Spaces reuses it rather
    // than inventing a second channel, so tab ownership, origin checks and retirement stay host-owned.
    private readonly Func<BrowseNavigationIntent, Task, bool>? _queueNavigation;
    private readonly App? _actualApp;
    private readonly Window? _actualWindow;

    private SpacesBrowseAppContentFactory(IServiceProvider services, CancellationToken appLifetime, CancellationToken hostLifetime,
        Func<BrowseNavigationIntent, Task, bool>? queueNavigation,
        Func<SpaceTaskWidgetPage, NativeCanonicalTaskSceneReadiness> bindTaskFrame,
        App? actualApp = null, Window? actualWindow = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _appLifetime = appLifetime;
        _hostLifetime = hostLifetime;
        _queueNavigation = queueNavigation;
        _actualApp = actualApp;
        _actualWindow = actualWindow;
        _bindTaskFrame = bindTaskFrame;
    }

    internal static SpacesBrowseAppContentFactory BindOriginalSameProcess(IServiceProvider originalProvider,
        HomeNativeWindowsComposition sameHome, CancellationToken originalAppLifetime,
        CancellationToken originalHostLifetime,
        Func<BrowseNavigationIntent, Task, bool>? queueNavigation = null,
        App? actualApp = null, Window? actualWindow = null)
    {
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(originalProvider, sameHome);
        ArgumentNullException.ThrowIfNull(originalProvider);
        originalAppLifetime.ThrowIfCancellationRequested();
        originalHostLifetime.ThrowIfCancellationRequested();
        if (!originalAppLifetime.CanBeCanceled || !originalHostLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual native app and host lifetimes.");
        if ((actualApp is null) != (actualWindow is null))
            throw new ArgumentException("Retain the same actual App and native Window together.");
        return new(originalProvider, originalAppLifetime, originalHostLifetime, queueNavigation,
            page => NativeCanonicalTaskSceneReadiness.BindOriginalSameProcess(page, originalProvider, sameHome,
                originalProvider.GetRequiredService<HostLocalTaskActorSource>(), originalAppLifetime, originalHostLifetime),
            actualApp, actualWindow);
    }

    public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _appLifetime.ThrowIfCancellationRequested();
        _hostLifetime.ThrowIfCancellationRequested();
        if (request.TabId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProfileId))
            throw new UnauthorizedAccessException("Retain the original tab and authenticated Home profile.");
        if (request.Privacy != BrowserTabPrivacy.Standard)
            throw new NotSupportedException("Spaces is not available in a private browser session.");
        return new SpacesBrowseAppContentSession(request, _services, _hostLifetime, _queueNavigation, _bindTaskFrame, _appLifetime,
            _actualApp, _actualWindow);
    }
}
