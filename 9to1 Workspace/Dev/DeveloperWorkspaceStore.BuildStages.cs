namespace HavenOS.Apps.Dev;

public sealed record DeveloperBuildStageEditResult(DeveloperResolvedProject Project, DeveloperTaskDefinition Task,
    string StageId, DeveloperDraftRecoveryScope Scope, long RecoveryGeneration, long? CleanSessionGeneration, bool MigratedRecovery);

public sealed partial class FileDeveloperWorkspaceStore
{
    /// <summary>Uses the SAME reviewed-metadata CAS, AES-GCM old-header verification/new-header
    /// migration, key owner, clean-layout migration and foreign-partition refusal as Run editing.</summary>
    public async Task<DeveloperOperationResult<DeveloperBuildStageEditResult>> SaveReviewedBuildStageAsync(
        DeveloperResolvedProject project, DeveloperBuildStageEditReview reviewed, DeveloperDraftRecoveryScope actualManualScope,
        long expectedRecoveryGeneration, Func<CancellationToken, Task<byte[]>> readRequiredOriginalProtectedKey,
        Func<CancellationToken, Task> revalidateOriginalSource, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var result = await SaveReviewedProjectMetadataAsync(project, new ReviewedBuildStage(reviewed), actualManualScope,
            expectedRecoveryGeneration, readRequiredOriginalProtectedKey, revalidateOriginalSource, token).ConfigureAwait(false);
        if (!result.Succeeded || result.Value is not { } saved) return new(result.RequestId, null, result.Error);
        return new(result.RequestId, new(saved.Project, saved.Project.Workspace.Tasks.Single(task => task.TaskId == reviewed.Task.TaskId),
            reviewed.StageId, saved.Scope, saved.RecoveryGeneration, saved.CleanSessionGeneration, saved.MigratedRecovery), null);
    }
    private sealed class ReviewedBuildStage(DeveloperBuildStageEditReview value) : ReviewedProjectMetadata
    {
        internal override bool IsCurrent(DeveloperResolvedProject project) => DeveloperBuildStageEdit.IsCurrent(project, value);
        internal override DeveloperResolvedProject Apply(DeveloperResolvedProject canonical)
        {
            var task = DeveloperPipelineExecutor.CaptureTaskDefinition(value.Task);
            var tasks = canonical.Workspace.Tasks.ToList(); var index = tasks.FindIndex(row => row.TaskId == task.TaskId);
            if (index < 0) tasks.Add(task); else tasks[index] = task;
            return AdvanceReviewedMetadata(canonical, canonical.Project, canonical.Workspace with { Tasks = Array.AsReadOnly(tasks.ToArray()) });
        }
    }
}
