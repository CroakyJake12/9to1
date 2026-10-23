using Microsoft.Extensions.DependencyInjection;
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
    DeveloperTaskWorkspaceService development, TaskExecutionCoordinator canonical, FilesNativeBrowserService files,
    IServiceProvider services)
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
            }, sourceExplorerReader: services.GetService<DeveloperManualSourceReadOwner>(),
            editorSessionStore: services.GetService<FileDeveloperWorkspaceStore>(),
            editorSessionExecution: services.GetService<FilesDeveloperOriginalCurrentProjectExecutionBridge>());
    }
    /// <summary>Creates the SAME compiled-CUI workbench in manual (no-AI) mode for a resolved saved
    /// project. No Task/Run/context/model or generation is involved: the real saved project identity
    /// is the admission. Reads/search go through the GENUINE Home/Files/kernel selective owner (a real
    /// configured read admission, never raw path IO); source edits and process execution are NOT
    /// offered here and fail closed. Caller retains the Page before publication.</summary>
    public DeveloperProjectWorkbenchPage CreateManual(DeveloperResolvedProject actualProject,
        Func<DeveloperProjectWorkbenchPage, ICuiSceneReadiness> createOriginalReadinessChild,
        Action<DeveloperProjectWorkbenchPage> retainOriginalChild,
        CancellationToken explicitBusinessCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actualProject);
        ArgumentNullException.ThrowIfNull(createOriginalReadinessChild);
        ArgumentNullException.ThrowIfNull(retainOriginalChild);
        // The genuine manual read owner is composed from the REAL configured Files/Home/kernel owners.
        // It is resolved lazily so tests/compositions without those owners still build and only the
        // genuine manual read entry fails closed with a clear message.
        var manualRead = services.GetService<DeveloperManualSourceReadOwner>()
            ?? throw new InvalidOperationException("The genuine manual read owners (Files selection, Home read admission, kernel capture) are not configured.");
        var manualEdit = services.GetService<DeveloperManualSourceEditOwner>()
            ?? throw new InvalidOperationException("The genuine manual edit owners (Home WRITE admission, kernel edit apply surface) are not configured.");
        DeveloperProjectWorkbenchPage? page = null;
        return new DeveloperProjectWorkbenchPage(development, canonical, actualProject, null,
            (project, relative, token) => throw new NotSupportedException("Manual open reads through its genuine Home/Files/kernel capture; no Task resolution is used."),
            files.DemandExternalOriginalDeveloperReadJoin, explicitBusinessCancellationToken,
            originalPage => { page = originalPage; retainOriginalChild(originalPage); },
            createOriginalReadinessChild, manualRead, manualEdit, services.GetService<FileDeveloperWorkspaceStore>(),
            services.GetService<FilesDeveloperOriginalCurrentProjectExecutionBridge>());
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
            originalPage => { page = originalPage; retainOriginalChild(originalPage); }, createOriginalReadinessChild,
            sourceExplorerReader: services.GetService<DeveloperManualSourceReadOwner>(),
            editorSessionStore: services.GetService<FileDeveloperWorkspaceStore>(),
            editorSessionExecution: services.GetService<FilesDeveloperOriginalCurrentProjectExecutionBridge>());
    }

}
