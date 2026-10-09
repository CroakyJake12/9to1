using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

/// <summary>Typed native composition over SAME global Dev/coordinator/Files owners. The caller
/// supplies the actual original Task/Run and genuine native readiness, and retains the page
/// before constructor publication. This factory creates no project, task, login or authority.</summary>
public sealed class DeveloperProjectWorkbenchPageFactory(
    DeveloperTaskWorkspaceService development, TaskExecutionCoordinator canonical, FilesNativeBrowserService files)
{
    // Composition identity only; no actor, resource, startup or scene-readiness grant.
    public bool IsBoundToOriginalComposition(DeveloperTaskWorkspaceService sameDevelopment,
        TaskExecutionCoordinator sameCanonical, FilesNativeBrowserService sameFiles) =>
        ReferenceEquals(development, sameDevelopment) && ReferenceEquals(canonical, sameCanonical) && ReferenceEquals(files, sameFiles);

    public DeveloperProjectWorkbenchPage CreateOriginal(DeveloperResolvedProject actualProject,
        TaskExecutionSnapshot actualTask, ICuiSceneReadiness originalReadiness,
        Action<DeveloperProjectWorkbenchPage> retainOriginalChild,
        CancellationToken explicitBusinessCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actualProject); ArgumentNullException.ThrowIfNull(actualTask);
        ArgumentNullException.ThrowIfNull(originalReadiness); ArgumentNullException.ThrowIfNull(retainOriginalChild);
        DeveloperProjectWorkbenchPage? page = null;
        return new DeveloperProjectWorkbenchPage(development, canonical, actualProject, actualTask.TaskId, actualTask.ExecutionId,
            actualTask.ContextId, originalReadiness,
            (project, relative, token) =>
            {
                var originalPage = page ?? throw new InvalidOperationException("The actual workbench acquisition has not been retained.");
                return files.ResolveOriginalDeveloperDocumentAsync(project, relative,
                    originalPage.CaptureOriginalDocumentObservationCurrentness(), token);
            }, files.DemandExternalOriginalDeveloperReadJoin, explicitBusinessCancellationToken,
            originalPage =>
            {
                page = originalPage; // SAME returned partial before the caller's capture/publication callback.
                retainOriginalChild(originalPage);
            });
    }
    /// <summary>Creates a genuine presentation child bound to this exact Dev host, independent
    /// from the originating widget becoming inactive. Caller retains the Page before creator.
    /// Missing child/source/host binding refuses native publication; business owners stay borrowed.</summary>
    public DeveloperProjectWorkbenchPage CreateOriginal(DeveloperResolvedProject actualProject,
        TaskExecutionSnapshot actualTask, Func<DeveloperProjectWorkbenchPage, ICuiSceneReadiness> createOriginalReadinessChild,
        Action<DeveloperProjectWorkbenchPage> retainOriginalChild,
        CancellationToken explicitBusinessCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actualProject); ArgumentNullException.ThrowIfNull(actualTask);
        ArgumentNullException.ThrowIfNull(createOriginalReadinessChild); ArgumentNullException.ThrowIfNull(retainOriginalChild);
        DeveloperProjectWorkbenchPage? page = null;
        return new DeveloperProjectWorkbenchPage(development, canonical, actualProject, actualTask.TaskId, actualTask.ExecutionId,
            actualTask.ContextId, null,
            (project, relative, token) =>
            {
                var originalPage = page ?? throw new InvalidOperationException("The actual workbench acquisition has not been retained.");
                return files.ResolveOriginalDeveloperDocumentAsync(project, relative,
                    originalPage.CaptureOriginalDocumentObservationCurrentness(), token);
            }, files.DemandExternalOriginalDeveloperReadJoin, explicitBusinessCancellationToken,
            originalPage => { page = originalPage; retainOriginalChild(originalPage); }, createOriginalReadinessChild);
    }

}
