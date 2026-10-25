using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Apps.Dev;

public sealed record DeveloperSavedStageChoice(DeveloperProjectReference Project, DeveloperTaskDefinition Task,
    DeveloperBuildStage Stage, string DefinitionSha256, string? UnavailableReason)
{
    public string SelectionKey => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        new { Project, Task.TaskId, Task.Revision, Stage.StageId, DefinitionSha256 }))).ToLowerInvariant();
    public int TimeoutSeconds => Stage.Timeout is { } timeout ? checked((int)Math.Ceiling(timeout.TotalSeconds)) : 600;
}

/// <summary>Inspection of actual saved task rows. No process, trust, Task-run identity or
/// inferred command is created. A single stage cannot bypass the pipeline coordinator.</summary>
public static class DeveloperSavedStageSelection
{
    public static DeveloperOperationResult<IReadOnlyList<DeveloperSavedStageChoice>> Observe(DeveloperResolvedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var tasks = project.Workspace.Tasks;
        if (tasks is null || tasks.Count > 200 || tasks.Any(task => task is null || string.IsNullOrWhiteSpace(task.TaskId)) ||
            tasks.Select(task => task.TaskId).Distinct(StringComparer.Ordinal).Count() != tasks.Count)
            return Invalid("Saved tasks are incomplete, ambiguous or exceed the 200-task limit.");
        var choices = new List<DeveloperSavedStageChoice>();
        foreach (var task in tasks.Where(task => task.EnvironmentId == project.Root.EnvironmentId &&
                     task.WorkingDirectoryRootId == project.Root.RootId.ToString("D")))
        {
            if (task.DependsOn is null || task.Stages is null || task.Stages.Count > 200 ||
                task.Stages.Any(stage => stage is null || string.IsNullOrWhiteSpace(stage.StageId) || stage.DependsOn is null || stage.Arguments is null) ||
                task.Stages.Select(stage => stage.StageId).Distinct(StringComparer.Ordinal).Count() != task.Stages.Count)
                return Invalid("A saved task has incomplete or ambiguous stages, or exceeds the 200-stage limit.");
            var captured = DeveloperPipelineExecutor.CaptureTaskDefinition(task);
            var digest = DeveloperTaskWorkspaceService.ComputeSavedTaskDefinitionSha256(captured);
            foreach (var stage in captured.Stages)
            {
                if (choices.Count >= 2000) return Invalid("This project exceeds the 2,000-stage inspection limit.");
                choices.Add(new(project.Reference, captured, stage, digest, Unavailable(project, captured, stage)));
            }
        }
        return DeveloperOperationResult<IReadOnlyList<DeveloperSavedStageChoice>>.Success(Array.AsReadOnly(choices.ToArray()));
    }

    private static string? Unavailable(DeveloperResolvedProject project, DeveloperTaskDefinition task, DeveloperBuildStage stage)
    {
        if (task.Revision < 1 || task.TaskId.Length > 128 || task.TaskId.Any(char.IsControl) ||
            stage.StageId.Length > 128 || stage.StageId.Any(char.IsControl) || !Enum.IsDefined(stage.Kind))
            return "The saved task needs bounded stable IDs, a positive revision and a supported stage kind.";
        if (project.Root.EnvironmentId != "local") return "Remote stages require their configured execution provider.";
        if (task.DependsOn.Count != 0 || stage.DependsOn.Count != 0)
            return "This stage needs the pipeline dependency coordinator; running it alone would skip prerequisites.";
        if (stage.Arguments.Count != 0)
            return "This command provider cannot execute a saved argument vector. An explicit saved command is required.";
        if (string.IsNullOrWhiteSpace(stage.Command) || stage.Command.Length > 32_000 || stage.Command.Contains('\0'))
            return "The saved command is missing or invalid.";
        if (stage.Timeout is { } timeout && (timeout.TotalSeconds <= 0 || timeout.TotalSeconds > 900))
            return "This command provider supports timeouts from 1 to 900 seconds.";
        return null;
    }

    public static DeveloperOperationResult<DeveloperSavedStageChoice> Select(DeveloperResolvedProject project,
        string taskId, long taskRevision, string stageId, string expectedDefinitionSha256)
    {
        var observed = Observe(project);
        if (!observed.Succeeded) return new(observed.RequestId, null, observed.Error);
        var choice = observed.Value!.SingleOrDefault(choice => choice.Task.TaskId == taskId && choice.Task.Revision == taskRevision &&
            choice.Stage.StageId == stageId && choice.DefinitionSha256 == expectedDefinitionSha256);
        if (choice is null) return DeveloperOperationResult<DeveloperSavedStageChoice>.Failure(DeveloperOperationErrorCode.RevisionConflict,
            "The saved stage changed. Select its current definition before running it.", stageId);
        if (choice.UnavailableReason is { } reason)
            return DeveloperOperationResult<DeveloperSavedStageChoice>.Failure(DeveloperOperationErrorCode.CapabilityUnavailable, reason, stageId);
        return DeveloperOperationResult<DeveloperSavedStageChoice>.Success(choice);
    }
    private static DeveloperOperationResult<IReadOnlyList<DeveloperSavedStageChoice>> Invalid(string message) =>
        DeveloperOperationResult<IReadOnlyList<DeveloperSavedStageChoice>>.Failure(DeveloperOperationErrorCode.InvalidStoredData, message, "saved-stages");
}
