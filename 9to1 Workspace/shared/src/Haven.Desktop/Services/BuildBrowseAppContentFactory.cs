using Haven.Application;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Dev;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Typed native Build (Dev) content factory. It borrows the shared canonical Dev/coordinator/
/// Files owners and opens the real workbench for an addressed canonical project. It creates no
/// project, task, grant or readiness.</summary>
internal sealed class BuildBrowseAppContentFactory : IBrowseAppContentFactory
{
    private readonly IServiceProvider _services;
    private readonly CancellationToken _hostLifetime;

    private BuildBrowseAppContentFactory(IServiceProvider services, CancellationToken hostLifetime)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _hostLifetime = hostLifetime;
    }

    internal static BuildBrowseAppContentFactory BindOriginalSameProcess(IServiceProvider originalProvider,
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
            throw new NotSupportedException("Build is not available in a private browser session.");

        var location = BuildBrowseAppContentAddress.Parse(request.Address);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_hostLifetime);
        try
        {
            return new BuildBrowseAppContentSession(request, location,
                _services.GetRequiredService<IDeveloperWorkspaceStore>(),
                _services.GetRequiredService<DeveloperTaskWorkspaceService>(),
                _services.GetRequiredService<TaskExecutionCoordinator>(),
                _services.GetRequiredService<DeveloperProjectWorkbenchPageFactory>(),
                lifetime);
        }
        catch { lifetime.Dispose(); throw; }
    }
}
