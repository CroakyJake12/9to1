using Haven.Application;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Apps.Spaces.Tasks;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>
/// The genuine borrowed native Build/Dev host resources. The production implementation borrows
/// them from a real <see cref="NativeFilesDesktopRoute"/> over the SAME Home graph; tests may
/// supply a deterministic implementation to drive the session's structural paths.
/// </summary>
internal interface IBuildBorrowedResources
{
    ICuiSceneReadiness BorrowOriginalHomeReadiness();
    void DemandOriginalHomeJoin();
    Task CloseAndDrainAsync();
}

/// <summary>Production borrowed resources rooted in an authentic same-process Files route.</summary>
internal sealed class BuildRouteBorrowedResources(NativeFilesDesktopRoute route) : IBuildBorrowedResources
{
    public ICuiSceneReadiness BorrowOriginalHomeReadiness() => route.BorrowOriginalHomeReadinessForMetadata();
    public void DemandOriginalHomeJoin() => route.DemandExternalOriginalRetirementJoin();
    public Task CloseAndDrainAsync() => route.CloseAndDrainAsync();
}

/// <summary>Landing composition: the real saved-project catalogue over the SAME canonical Dev/Home
/// owners plus the borrowed native Home readiness/join. It grants no actor, task, project or
/// authority; it is identity/observation only.</summary>
internal sealed record BuildLandingComposition(
    SpaceRegistry Spaces,
    SpaceDevelopmentReferenceCatalog Catalog,
    IBuildBorrowedResources Borrowed);

/// <summary>Typed native Build (Dev) content factory. It borrows the shared canonical Dev/coordinator/
/// Files owners and opens the real saved-project landing or the real compiled-CUI workbench for an
/// addressed canonical project. It creates no project, task, grant or readiness.</summary>
internal sealed class BuildBrowseAppContentFactory : IBrowseAppContentFactory
{
    private readonly IServiceProvider _services;
    private readonly HomeNativeWindowsComposition _home;
    private readonly CancellationToken _appLifetime, _hostLifetime;

    private BuildBrowseAppContentFactory(IServiceProvider services, HomeNativeWindowsComposition home,
        CancellationToken appLifetime, CancellationToken hostLifetime)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _home = home ?? throw new ArgumentNullException(nameof(home));
        _appLifetime = appLifetime;
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
        return new(originalProvider, sameHome, originalAppLifetime, originalHostLifetime);
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
            // Authentic files route over the SAME Home graph supplies the genuine borrowed Home
            // readiness and join guard used by the canonical Dev catalogue.
            var filesRoute = NativeFilesDesktopRoute.BindOriginalSameProcess(_services, _home, _appLifetime, lifetime.Token);
            var spaces = _services.GetRequiredService<SpaceRegistry>();
            var taskViews = new SpaceTaskWorkspaceService(spaces,
                _services.GetRequiredService<IConversationRepository>(),
                _services.GetRequiredService<TaskExecutionCoordinator>());
            var development = _services.GetRequiredService<DeveloperTaskWorkspaceService>();
            var workspace = new SpaceDevelopmentWorkspace(spaces, taskViews, development);
            var catalog = new SpaceDevelopmentReferenceCatalog(spaces, taskViews, workspace,
                _services.GetRequiredService<IAuthenticatedResourceActorSource>());
            var composition = new BuildLandingComposition(spaces, catalog,
                new BuildRouteBorrowedResources(filesRoute));

            return new BuildBrowseAppContentSession(request, location,
                _services.GetRequiredService<IDeveloperWorkspaceStore>(),
                development,
                _services.GetRequiredService<TaskExecutionCoordinator>(),
                _services.GetRequiredService<DeveloperProjectWorkbenchPageFactory>(),
                composition, lifetime);
        }
        catch { lifetime.Dispose(); throw; }
    }
}
