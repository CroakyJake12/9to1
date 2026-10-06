using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Dev;

/// <summary>Private configured Files project/root source. Public saved IDs and a resolved
/// metadata DTO cannot mint a binding, execution consent, or restored setup product.</summary>
public interface IDeveloperWorkspaceOriginalProjectExecutionBindingSource
    : IDeveloperWorkspaceOriginalExecutionBindingSource, IDeveloperWorkspaceOriginalExecutionScopedBindingSource
{
    Task<IDeveloperWorkspaceOriginalExecutionBinding> ResolveOriginalProjectBindingAsync(
        DeveloperResolvedProject sameProject, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
    Task ValidateOriginalProjectBindingAsync(DeveloperResolvedProject sameProject,
        IDeveloperWorkspaceOriginalExecutionBinding sameBinding, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
}

/// <summary>Additive exact-root path. The ordinary ID-only compatibility method grants
/// nothing in the real adapter. Home separately reviews the SAME tool preparation, and
/// its held native descriptor pin is required again at the finite process start.</summary>
public interface IDeveloperWorkspaceOriginalProjectExecutionTrustService : IDeveloperWorkspaceTrustService
{
    bool IsBoundToOriginalToolOwner(ITaskRunToolActionOwner sameOwner);
    Task<IDeveloperWorkspaceOriginalExecutionBinding> ResolveOriginalBindingAsync(
        DeveloperResolvedProject sameProject, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
    Task ValidateOriginalBindingAsync(DeveloperResolvedProject sameProject,
        IDeveloperWorkspaceOriginalExecutionBinding sameBinding, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
    Task<IWorkspaceOriginalProcessStartConsent> AcquireAndBindOriginalConsentAsync(
        DeveloperResolvedProject sameProject, IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        IWorkspaceToolActionPreparation samePreparation, TaskExecutionSnapshot current,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken);
    void DemandExternalOriginalExecutionTrustJoin();
}
