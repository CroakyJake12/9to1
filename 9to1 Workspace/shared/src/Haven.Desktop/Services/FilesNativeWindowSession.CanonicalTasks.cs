using Haven.Application;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

internal sealed partial class FilesNativeWindowSession
{
    [ThreadStatic] private static FilesNativeWindowSession? _originalTaskCompositionCallback;

    // The original approved Home provider remains borrowed. Missing task services do not
    // manufacture a registry, actor, startup connection or a normally available task route.
    private void AttachOriginalCanonicalTaskRoutes(MainView actualShell)
    {
        var spaces = InvokeOriginalTaskComposition(() => _originalProvider.GetService<SpaceRegistry>());
        CheckAlive();
        if (spaces is not null)
        {
            InvokeOriginalTaskComposition(() =>
            {
                actualShell.ConfigureOriginalSpaceRegistry(spaces);
                return true;
            });
            CheckAlive();
        }
        var canonical = InvokeOriginalTaskComposition(() => _originalProvider.GetService<TaskExecutionCoordinator>());
        CheckAlive();
        var actors = InvokeOriginalTaskComposition(() => _originalProvider.GetService<HostLocalTaskActorSource>());
        CheckAlive();
        if (spaces is null || canonical is null || actors is null) return;

        // Borrow the configured SAME global Dev/Files owners. Missing Dev aliases leave
        // the native Dev route unavailable; this attachment never constructs substitutes.
        var development = InvokeOriginalTaskComposition(() => _originalProvider.GetService<DeveloperTaskWorkspaceService>());
        CheckAlive();
        DeveloperProjectWorkbenchPageFactory? developmentFactory = null;
        if (development is not null)
        {
            developmentFactory = InvokeOriginalTaskComposition(() => _originalProvider.GetService<DeveloperProjectWorkbenchPageFactory>());
            CheckAlive();
            if (developmentFactory is not null)
            {
                var files = InvokeOriginalTaskComposition(() => _originalProvider.GetService<FilesNativeBrowserService>());
                CheckAlive();
                if (files is null || !InvokeOriginalTaskComposition(() =>
                    developmentFactory.IsBoundToOriginalComposition(development, canonical, files)))
                    throw new InvalidOperationException("The native Dev factory must retain the SAME configured Dev, Task and Files owners.");
                CheckAlive();
            }
        }
        var actualDevelopmentFactory = developmentFactory;

        InvokeOriginalTaskComposition(() =>
        {
            actualShell.ConfigureCanonicalSpaceTaskRoutes(spaces, canonical,
                page => InvokeOriginalTaskComposition(() => NativeCanonicalTaskSceneReadiness.BindOriginal(
                    page, _originalStartup, actors, _originalConnectionLifetime, _windowLifetime.Token)),
                development);
            return true;
        });
        CheckAlive();
        if (actualDevelopmentFactory is not null)
        {
            InvokeOriginalTaskComposition(() =>
            {
                actualShell.ConfigureOriginalCanonicalDevelopmentFactory((view, creator, capture, businessToken) =>
                    InvokeOriginalTaskComposition(() => actualDevelopmentFactory.CreateOriginal(view.Project,
                        view.Task.Snapshot ?? throw new InvalidOperationException("The SAME original Task/Run snapshot is required."),
                        creator, capture, businessToken)));
                return true;
            });
            CheckAlive();
        }
        // The maintained factory captures the SAME partial Page before child creation or
        // constructor publication. Return its product directly to the owning shell cohort:
        // no post-factory check can discard an already acquired child, and no second Activate
        // or view-token substitution is introduced. Global business services remain borrowed.
    }

    private T InvokeOriginalTaskComposition<T>(Func<T> originalCallback)
    {
        CheckAlive();
        var previous = _originalTaskCompositionCallback;
        _originalTaskCompositionCallback = this;
        try
        {
            // In particular, return an acquired child unchanged to the page's original
            // acquisition owner before it performs its own validation/publication checks.
            return originalCallback();
        }
        finally { _originalTaskCompositionCallback = previous; }
    }

    private void DemandExternalOriginalTaskCompositionJoin()
    {
        if (ReferenceEquals(_originalTaskCompositionCallback, this))
            throw new InvalidOperationException("An original native task composition callback cannot join its own window close.");
    }
}
