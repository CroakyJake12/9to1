using System.Text;

namespace HavenOS.Apps.Dev;

public sealed class DeveloperBuildStageEditReview
{
    internal DeveloperBuildStageEditReview(DeveloperProjectReference project, string workspaceSha256, string? beforeTaskSha256,
        string? editedStageId, DeveloperTaskDefinition task, string stageId)
    { Project = project; WorkspaceSha256 = workspaceSha256; BeforeTaskSha256 = beforeTaskSha256; EditedStageId = editedStageId; Task = task; StageId = stageId; }
    public DeveloperProjectReference Project { get; }
    public string WorkspaceSha256 { get; }
    public string? BeforeTaskSha256 { get; }
    public string? EditedStageId { get; }
    public DeveloperTaskDefinition Task { get; }
    public string StageId { get; }
    public DeveloperBuildStage Stage => Task.Stages.Single(stage => stage.StageId == StageId);
    public string AfterTaskSha256 => DeveloperTaskWorkspaceService.ComputeSavedTaskDefinitionSha256(Task);
    public string Summary => $"{(EditedStageId is null ? "Create" : "Update")} {Stage.Kind} stage {Task.TaskId} / {StageId}.\n" +
        $"Command: {Stage.Command}\nTimeout: {Stage.Timeout?.TotalSeconds ?? 600}s.\n" +
        $"Saved task revision: {Task.Revision}. Other stages and their dependencies stay unchanged.\n" +
        "Save changes metadata only. Compatible protected drafts and layouts migrate atomically; source files and live drafts are kept. Running requires a separate permission.";
}

/// <summary>Deliberate independent Compile/Test authoring. Complete previous task and workspace
/// digests prevent another stage, caller list or same-revision edit from being silently adopted.</summary>
public static class DeveloperBuildStageEdit
{
    public static DeveloperOperationResult<DeveloperBuildStageEditReview> Prepare(DeveloperResolvedProject project,
        string taskId, string stageId, DeveloperBuildStageKind kind, string command, int timeoutSeconds,
        string? expectedBeforeTaskSha256 = null, string? editedStageId = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 && id.Trim() == id && !id.Any(char.IsControl);
        if (project.Root.EnvironmentId != "local" || project.Reference.ProjectRevision == long.MaxValue || project.Reference.WorkspaceRevision == long.MaxValue ||
            !ValidId(taskId) || !ValidId(stageId) || kind is not (DeveloperBuildStageKind.Compile or DeveloperBuildStageKind.Test) ||
            string.IsNullOrWhiteSpace(command) || command.Length > 32_000 || command.Contains('\0') || timeoutSeconds is < 1 or > 900)
            return Invalid("Choose stable task/stage IDs, an independent Compile or Test command and timeout from 1 to 900 seconds.");
        try { _ = new UTF8Encoding(false, true).GetByteCount(command); }
        catch (EncoderFallbackException) { return Invalid("The exact command needs valid Unicode; it was not normalized."); }
        var observed = DeveloperSavedStageSelection.Observe(project);
        if (!observed.Succeeded) return Invalid(observed.Error!.Message);
        var existing = project.Workspace.Tasks.SingleOrDefault(task => task.TaskId == taskId);
        DeveloperTaskDefinition after;
        if (expectedBeforeTaskSha256 is null)
        {
            if (existing is not null || editedStageId is not null)
                return Invalid("That saved task already exists. Select its current stage to edit it, or choose a new task ID; it will not be adopted or overwritten.");
            if (project.Workspace.Tasks.Count >= 200 || observed.Value!.Count >= 2000)
                return Invalid("The bounded saved-task/stage catalogue is full. No task or stage was removed.");
            after = new(taskId, taskId, "user", "local", project.Root.RootId.ToString("D"), [],
                [new(stageId, kind, stageId, [], command, [], TimeSpan.FromSeconds(timeoutSeconds))], true);
        }
        else
        {
            if (existing is null || existing.Revision is < 1 or long.MaxValue || existing.EnvironmentId != project.Root.EnvironmentId ||
                existing.WorkingDirectoryRootId != project.Root.RootId.ToString("D") || existing.Source is not ("user" or "workspace") ||
                existing.DependsOn.Count != 0 || DeveloperTaskWorkspaceService.ComputeSavedTaskDefinitionSha256(existing) != expectedBeforeTaskSha256)
                return Invalid("The inspected task changed, belongs to another root/provider, or needs its dependency coordinator. Select its current declaration.");
            after = DeveloperPipelineExecutor.CaptureTaskDefinition(existing);
            var stages = after.Stages.ToList(); var index = stages.FindIndex(stage => stage.StageId == stageId);
            if (editedStageId != stageId || index < 0) return Invalid("An existing stage ID is stable. Select its current declaration before editing.");
            var stage = stages[index];
            if (stage.Kind is not (DeveloperBuildStageKind.Compile or DeveloperBuildStageKind.Test) || stage.DependsOn.Count != 0 || stage.Arguments.Count != 0 ||
                stage.Timeout is { } duration && (duration.TotalSeconds < 1 || duration.TotalSeconds > 900 || duration.Ticks % TimeSpan.TicksPerSecond != 0))
                return Invalid("This editor supports independent Compile/Test stages with explicit commands and whole-second timeouts. Other saved fields stay in source.");
            stages[index] = stage with { Kind = kind, Command = command,
                Timeout = stage.Timeout is null && timeoutSeconds == 600 ? null : TimeSpan.FromSeconds(timeoutSeconds) };
            after = after with { Stages = Array.AsReadOnly(stages.ToArray()), Revision = checked(after.Revision + 1) };
            if (DeveloperTaskWorkspaceService.ComputeSavedTaskDefinitionSha256(after with { Revision = existing.Revision }) == expectedBeforeTaskSha256)
                return Invalid("The saved stage is unchanged. No metadata or recovery migration is needed.");
        }
        return DeveloperOperationResult<DeveloperBuildStageEditReview>.Success(new(project.Reference,
            DeveloperRunConfigurationEdit.WorkspaceDigest(project.Workspace), expectedBeforeTaskSha256, editedStageId,
            DeveloperPipelineExecutor.CaptureTaskDefinition(after), stageId));
    }
    public static bool IsCurrent(DeveloperResolvedProject project, DeveloperBuildStageEditReview reviewed) =>
        project.Reference == reviewed.Project && DeveloperRunConfigurationEdit.WorkspaceDigest(project.Workspace) == reviewed.WorkspaceSha256 &&
        Prepare(project, reviewed.Task.TaskId, reviewed.StageId, reviewed.Stage.Kind, reviewed.Stage.Command!,
            reviewed.Stage.Timeout is { } timeout ? checked((int)timeout.TotalSeconds) : 600, reviewed.BeforeTaskSha256, reviewed.EditedStageId)
            is { Succeeded: true, Value: { } current } && current.AfterTaskSha256 == reviewed.AfterTaskSha256;
    private static DeveloperOperationResult<DeveloperBuildStageEditReview> Invalid(string message) =>
        DeveloperOperationResult<DeveloperBuildStageEditReview>.Failure(DeveloperOperationErrorCode.InvalidInput, message, "build-stages");
}
