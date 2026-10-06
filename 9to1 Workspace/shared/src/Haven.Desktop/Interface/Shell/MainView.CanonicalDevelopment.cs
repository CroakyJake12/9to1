using Avalonia.Threading;
using Haven.Application;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Apps.Spaces.Tasks;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    // A host consumer of the maintained source-created native workbench factory.
    // This supplies no actor/startup/readiness grant and does not create Task/Run IDs.
    internal delegate DeveloperProjectWorkbenchPage OriginalCanonicalDevelopmentPageFactory(
        SpaceDeveloperView actualView,
        Func<DeveloperProjectWorkbenchPage, ICuiSceneReadiness> createOriginalReadinessChild,
        Action<DeveloperProjectWorkbenchPage> retainActualPartial,
        CancellationToken explicitBusinessCancellationToken);
    private OriginalCanonicalDevelopmentPageFactory? _captureOriginalCanonicalDevPage;

    internal void ConfigureOriginalCanonicalDevelopmentFactory(OriginalCanonicalDevelopmentPageFactory actualFactory)
    {
        Dispatcher.UIThread.VerifyAccess(); _originalShellWork.DemandAdmission();
        ArgumentNullException.ThrowIfNull(actualFactory);
        if (_canonicalSpaceRoutes?.Development is null)
            throw new InvalidOperationException("The SAME canonical Space/Dev services must be bound before the native child factory.");
        if (_captureOriginalCanonicalDevPage is not null)
            throw new InvalidOperationException("The source-created native Dev factory is already bound.");
        _captureOriginalCanonicalDevPage = actualFactory;
    }

    // Used by Root's optional native attachment before any lazy ordinary route.
    // The exact DI singleton and its SAME settings store are supplied by the host.
    internal void ConfigureOriginalSpaceRegistry(SpaceRegistry actualSingleton)
    {
        Dispatcher.UIThread.VerifyAccess(); _originalShellWork.DemandAdmission();
        ArgumentNullException.ThrowIfNull(actualSingleton);
        if (_spaceRegistry is not null && !ReferenceEquals(_spaceRegistry, actualSingleton))
            throw new InvalidOperationException("The shell already uses a different original Space registry.");
        _spaceRegistry = actualSingleton;
    }
}
