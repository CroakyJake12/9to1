using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Apps.Spaces.Tasks;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>
/// The genuine borrowed native Build/Dev host resources. The production implementation borrows
/// them from a real <see cref="NativeFilesDesktopRoute"/> over the SAME Home graph. The seam
/// exposes the REAL admission/current-profile custody (never merely observation): the caller must
/// admit the original shell initialization and revalidate the actual authenticated profile before
/// publication and on live access. Tests may supply a deterministic implementation to drive the
/// session's structural paths.
/// </summary>
internal interface IBuildBorrowedResources
{
    ICuiSceneReadiness BorrowOriginalHomeReadiness();
    void DemandOriginalHomeJoin();
    Task CloseAndDrainAsync();
    /// <summary>Admits the genuine same-process Home/Files shell initialization (no Files UI is
    /// shown) and retains/joins its ORIGINAL task. The returned task is the actual admission task.</summary>
    Task AdmitOriginalShellInitializationAsync(CancellationToken cancellationToken);
    /// <summary>Revalidates that the request's profile label matches the ACTUAL admitted actor.
    /// A non-blank profile label is never authorization by itself.</summary>
    void CheckOriginalAppContentProfile(string expectedProfile);
    /// <summary>Genuine retained async revalidation around awaited acquisition/publication. Unlike
    /// the cached sync check, it re-reads the current actor and Home runtime and verifies the actual
    /// authenticated profile (revocation/actor-replacement aware). It never copies actors or treats a
    /// matching cached label as authorization.</summary>
    Task RevalidateOriginalAppContentAsync(string expectedProfile, CancellationToken cancellationToken);
    /// <summary>The genuine original admission task retained by <see cref="AdmitOriginalShellInitializationAsync"/>.</summary>
    Task? OriginalAdmission { get; }
}

/// <summary>Production borrowed resources rooted in an authentic same-process Files route.</summary>
internal sealed class BuildRouteBorrowedResources(NativeFilesDesktopRoute route) : IBuildBorrowedResources
{
    private Task? _admission;

    public ICuiSceneReadiness BorrowOriginalHomeReadiness() => route.BorrowOriginalHomeReadinessForMetadata();
    public void DemandOriginalHomeJoin() => route.DemandExternalOriginalRetirementJoin();
    public Task CloseAndDrainAsync() => route.CloseAndDrainAsync();

    public Task AdmitOriginalShellInitializationAsync(CancellationToken cancellationToken)
    {
        var admission = route.AdmitOriginalShellInitializationAsync(cancellationToken);
        _admission ??= admission;
        return admission;
    }

    public void CheckOriginalAppContentProfile(string expectedProfile) => route.CheckOriginalAppContentProfile(expectedProfile);
    public Task RevalidateOriginalAppContentAsync(string expectedProfile, CancellationToken cancellationToken) =>
        route.RevalidateOriginalAppContentForBuildAsync(expectedProfile, cancellationToken);
    public Task? OriginalAdmission => _admission;
}

/// <summary>Typed native Build (Dev) content factory. It resolves every pure prerequisite BEFORE
/// acquiring the native Files route (which owns a linked CTS/retirement), so a resolution failure
/// cannot leak an acquired route. After the route is acquired only the trivial composition/session
/// wrapping remains (no fallible step), so the session always becomes the route's owner and its
/// cleanup is genuinely settled by the session lifecycle. It borrows the shared canonical
/// Dev/coordinator/Files owners and opens the real saved-project landing or the real compiled-CUI
/// workbench for an addressed canonical project. It creates no project, task, grant or readiness.
/// </summary>
internal sealed class BuildBrowseAppContentFactory : IBrowseAppContentFactory
{
    private readonly IServiceProvider _services;
    private readonly HomeNativeWindowsComposition _home;
    private readonly CancellationToken _appLifetime, _hostLifetime;
    private readonly Func<BrowseNavigationIntent, Task, bool>? _queueNavigation;

    private BuildBrowseAppContentFactory(IServiceProvider services, HomeNativeWindowsComposition home,
        CancellationToken appLifetime, CancellationToken hostLifetime,
        Func<BrowseNavigationIntent, Task, bool>? queueNavigation)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _home = home ?? throw new ArgumentNullException(nameof(home));
        _appLifetime = appLifetime;
        _hostLifetime = hostLifetime;
        _queueNavigation = queueNavigation;
    }

    internal static BuildBrowseAppContentFactory BindOriginalSameProcess(IServiceProvider originalProvider,
        HomeNativeWindowsComposition sameHome, CancellationToken originalAppLifetime,
        CancellationToken originalHostLifetime, Func<BrowseNavigationIntent, Task, bool>? queueNavigation = null)
    {
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(originalProvider, sameHome);
        ArgumentNullException.ThrowIfNull(originalProvider);
        originalAppLifetime.ThrowIfCancellationRequested();
        originalHostLifetime.ThrowIfCancellationRequested();
        if (!originalAppLifetime.CanBeCanceled || !originalHostLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual native app and host lifetimes.");
        return new(originalProvider, sameHome, originalAppLifetime, originalHostLifetime, queueNavigation);
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
            // 1) Resolve ALL pure prerequisites BEFORE constructing the session or acquiring the native
            //    route. None of these acquire a route/CTS/retirement, so a failure here disposes only
            //    the outer lifetime.
            var spaces = _services.GetRequiredService<SpaceRegistry>();
            var conversations = _services.GetRequiredService<IConversationRepository>();
            var coordinator = _services.GetRequiredService<TaskExecutionCoordinator>();
            var taskViews = new SpaceTaskWorkspaceService(spaces, conversations, coordinator);
            var development = _services.GetRequiredService<DeveloperTaskWorkspaceService>();
            var workspace = new SpaceDevelopmentWorkspace(spaces, taskViews, development);
            var actors = _services.GetRequiredService<IAuthenticatedResourceActorSource>();
            var catalog = new SpaceDevelopmentReferenceCatalog(spaces, taskViews, workspace, actors);
            var workspaces = _services.GetRequiredService<IDeveloperWorkspaceStore>();
            var workbenchFactory = _services.GetRequiredService<DeveloperProjectWorkbenchPageFactory>();

            // 2) Construct the session FIRST with a non-throwing route-free ctor. No native route has
            //    been acquired yet, so construction cannot leak an acquired resource.
            var session = new BuildBrowseAppContentSession(request, location,
                workspaces, development, coordinator, workbenchFactory,
                spaces, catalog, lifetime, _queueNavigation);

            // 3) Acquire the native route LAST. If acquisition throws, no route was retained and the
            //    outer lifetime is disposed; nothing leaks. Once acquired, attach it through the
            //    non-throwing AttachBorrowed so THIS session becomes the route's retained external
            //    owner and settles its asynchronous cleanup in the session lifecycle (never block or
            //    detach cleanup).
            var filesRoute = NativeFilesDesktopRoute.BindOriginalSameProcess(_services, _home, _appLifetime, lifetime.Token);
            session.AttachBorrowed(new BuildRouteBorrowedResources(filesRoute));
            // Attach the genuine manual (no-AI) project creation agent. It registers a fresh canonical
            // workspace/project from a selected existing Files folder through the real setup owners and
            // returns the saved reference for the SAME manual-open path. The landing exposes a typed
            // create action only when this agent is present.
            // Retain ONE adapter per session so a partially effected manual attempt keeps its
            // once-created setup/workspace identity across a user retry (no remint, no duplicate).
            var files = _services.GetRequiredService<NativeFilesWorkspaceService>();
            var manualAdapter = new DeveloperManualProjectEntryAdapter(
                _services.GetRequiredService<FilesDeveloperOriginalSourceSelection>(),
                _services.GetRequiredService<IDeveloperProjectOriginalReadAdmissionSource>(),
                _services.GetRequiredService<FilesDeveloperOriginalSetupScopeSource>(),
                _services.GetRequiredService<HomeDeveloperProjectSetupJournal>(),
                _services.GetRequiredService<IDeveloperProjectOriginalSetupPermissionSource>(),
                _services.GetRequiredService<FilesDeveloperOriginalFolderSetupProducer>(),
                _services.GetRequiredService<IDeveloperWorkspaceStore>());
            session.AttachManualEntryAgent(async (root, ct) =>
            {
                var configuration = await files.GetConfigurationAsync(ct)
                    ?? throw new UnauthorizedAccessException("The original configured Files workspace is unavailable.");
                if (!configuration.AppFolders.TryGetValue("write", out var writeFolder) || writeFolder.Value == Guid.Empty)
                    throw new InvalidOperationException("The configured Files root has no canonical write destination folder for a new manual project.");
                return await manualAdapter.RegisterExistingFolderAsync(configuration.StoreId, writeFolder.Value, root, ct);
            });
            var templateProvider = new DeveloperCuiProjectTemplateProvider();
            var templateCatalog = new DeveloperProjectCatalog([templateProvider]);
            var templateMaterializer = new DeveloperProjectTemplateMaterializer([templateProvider]);
            var templateEntry = new DeveloperTemplateProjectEntryAdapter(manualAdapter, templateMaterializer);
            session.AttachTemplateProjectAgents(templateCatalog, templateMaterializer,
                async (root, materialization, ct) =>
                {
                    var configuration = await files.GetConfigurationAsync(ct)
                        ?? throw new UnauthorizedAccessException("The original configured Files workspace is unavailable.");
                    if (!configuration.AppFolders.TryGetValue("write", out var writeFolder) || writeFolder.Value == Guid.Empty)
                        throw new InvalidOperationException("The configured Files root has no canonical write destination folder for a template project.");
                    return await templateEntry.RegisterExistingFolderAsync(configuration.StoreId, writeFolder.Value,
                        root, materialization, ct);
                },
                session.OpenExistingManualTemplateProjectAsync);
            return session;
        }
        catch { lifetime.Dispose(); throw; }
    }
}
