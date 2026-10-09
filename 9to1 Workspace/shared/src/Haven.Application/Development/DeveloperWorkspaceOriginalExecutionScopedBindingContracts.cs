namespace Haven.Application;

/// <summary>Optional source-custody port on the SAME configured saved-root issuer. These
/// callbacks carry the actual borrowing Home driver into each finite source factory and retain
/// its exact raw Tasks; they add no actor, approval, installation or native execution authority.</summary>
public interface IDeveloperWorkspaceOriginalExecutionScopedBindingSource
{
    Task RevalidateOriginalWithinSourceAsync(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        AuthenticatedResourceActor sameActor, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
    Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalExecutionPinWithinSourceAsync(
        IDeveloperWorkspaceOriginalExecutionBinding sameBinding, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
}
