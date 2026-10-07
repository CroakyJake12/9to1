#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private MainWindow? _actualSameProcessNativeWindow;
    private MainView? _actualSameProcessNativeShell;
    private bool _windowsNativeRoutesTransferred;
    private NativeFilesDesktopRoute? _actualSameProcessFilesRoute;

    private void ConfigureOriginalWindowsNativeRoutes(MainWindow actualWindow, MainView actualShell)
    {
        var home = _actualWindowsHome;
        if (home is null) return;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            var provider = _services ?? throw new InvalidOperationException("The original App provider is unavailable.");
            WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, home);
            if (!ReferenceEquals(actualWindow.DataContext, actualShell) ||
                _actualSameProcessNativeWindow is not null || _actualSameProcessNativeShell is not null)
                throw new InvalidOperationException("Retain the actual original Windows shell and native window once.");
            _actualSameProcessNativeWindow = actualWindow; _actualSameProcessNativeShell = actualShell;
            var windowLifetime = actualWindow.AcquireOriginalWindowLifetime();
            var appLifetime = original.Token; // SAME underlying App work token; no second stop owner.
            original.DemandPublication();
            var route = NativeFilesDesktopRoute.BindOriginalSameProcess(provider, home, appLifetime, windowLifetime);
            _actualSameProcessFilesRoute = route; // Capture before any shell callback/publication.
            actualShell.AttachOriginalFilesRoute(route, provider);
            original.DemandPublication();
            var spaces = provider.GetRequiredService<SpaceRegistry>();
            var canonical = provider.GetRequiredService<TaskExecutionCoordinator>();
            var taskActors = provider.GetRequiredService<HostLocalTaskActorSource>();
            var taskAuthority = provider.GetRequiredService<TaskRunPermissionAuthority>();
            original.DemandPublication();
            var development = provider.GetRequiredService<DeveloperTaskWorkspaceService>();
            var files = provider.GetRequiredService<FilesNativeBrowserService>();
            var factory = provider.GetRequiredService<DeveloperProjectWorkbenchPageFactory>();
            if (!factory.IsBoundToOriginalComposition(development, canonical, files))
                throw new InvalidOperationException("The original native Dev factory must retain the SAME Task, Dev and Files owners.");
            original.DemandPublication();
            actualShell.ConfigureOriginalSpaceRegistry(spaces);
            actualShell.ConfigureCanonicalSpaceTaskRoutes(spaces, canonical,
                page => NativeCanonicalTaskSceneReadiness.BindOriginalSameProcess(page, provider, home,
                    taskActors, appLifetime, windowLifetime), development);
            original.DemandPublication();
            actualShell.ConfigureOriginalCanonicalDevelopmentFactory((view, creator, capture, businessToken) =>
                factory.CreateOriginal(view.Project,
                    view.Task.Snapshot ?? throw new InvalidOperationException("The SAME original Task/Run snapshot is required."),
                    creator, capture, businessToken));
            original.DemandPublication();
            actualShell.ConfigureOriginalDevelopmentCatalog(route, taskAuthority, taskActors, appLifetime, windowLifetime);
            original.DemandPublication();
            return true;
        }));
    }

    private void DemandOriginalWindowsNativeRouteRetirementJoin()
    {
        _actualSameProcessFilesRoute?.DemandExternalOriginalRetirementJoin();
        _actualSameProcessNativeShell?.DemandExternalOriginalRetirementJoin();
        _actualSameProcessNativeWindow?.DemandExternalOriginalRetirementJoin();
    }

    private async Task JoinOriginalUntransferredWindowsNativeRoutesAsync()
    {
        MainWindow? window; NativeFilesDesktopRoute? route;
        lock (_actualStartupAcquisitionGate)
        {
            if (_windowsNativeRoutesTransferred) return;
            window = _actualSameProcessNativeWindow; route = _actualSameProcessFilesRoute;
        }
        DemandOriginalWindowsNativeRouteRetirementJoin(); // Whole pure preflight first.
        var failures = new List<Exception>(); var actuals = new List<Task>();
        if (window is not null)
        {
            try { window.RequestRetirement(); } catch (Exception error) { AddAppCause(failures, error); }
            try { actuals.Add(window.CloseAndDrainAsync()); } catch (Exception error) { AddAppCause(failures, error); }
        }
        // Also capture the actual route if attachment itself failed; a shell may not own it.
        if (route is not null)
            try { actuals.Add(route.CloseAndDrainAsync()); } catch (Exception error) { AddAppCause(failures, error); }
        foreach (var actual in actuals.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinOriginalAppTaskAsync(actual, failures);
        ThrowAppCauses(failures);
    }
}
#endif
