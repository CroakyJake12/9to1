using System.Security.Cryptography;
using System.Text.Json;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private sealed record SavedStageChoice(DeveloperProjectReference Project, string TaskId, long TaskRevision,
        string StageId, string DefinitionHash, string Label, string Summary, string? UnavailableReason)
    {
        public string SelectionKey => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { Project, TaskId, TaskRevision, StageId, DefinitionHash })));
    }

    private sealed record SavedStageItem(string SelectionKey, string Label, string Details, string AccessibleName, bool CanSelect);
    private SavedStageChoice? _selectedSavedStage;

    private static IReadOnlyList<SavedStageChoice> SavedStageChoices(DeveloperResolvedProject project) =>
        project.Workspace.Tasks.Where(task => task.EnvironmentId == project.Root.EnvironmentId &&
                task.WorkingDirectoryRootId == project.Reference.RootId.ToString("D"))
            .SelectMany(task => (task.Stages ?? []).Select(stage =>
            {
                var unavailable = SavedStageUnavailable(task, stage);
                var command = stage.Command ?? "(no saved command)";
                var summary = $"Task {task.TaskId}, revision {task.Revision}; stage {stage.StageId} ({stage.Kind}).\n" +
                    $"Environment: {task.EnvironmentId}; root: {task.WorkingDirectoryRootId}.\n" +
                    $"Command: {command}\nArguments: {JsonSerializer.Serialize(stage.Arguments)}\n" +
                    $"Dependencies: {string.Join(", ", (task.DependsOn ?? []).Concat(stage.DependsOn ?? []))}. " +
                    $"Timeout: {stage.Timeout?.TotalSeconds ?? 600} seconds. Workspace trust required: {task.RequiresWorkspaceTrust}.";
                return new SavedStageChoice(project.Reference, task.TaskId, task.Revision, stage.StageId,
                    DeveloperTaskWorkspaceService.ComputeSavedTaskDefinitionSha256(task),
                    task.Label + " / " + stage.Label, summary, unavailable);
            })).ToArray();

    private static string? SavedStageUnavailable(DeveloperTaskDefinition task, DeveloperBuildStage stage)
    {
        if (task.Revision < 1 || string.IsNullOrWhiteSpace(task.TaskId) || string.IsNullOrWhiteSpace(stage.StageId) ||
            task.DependsOn is null || stage.DependsOn is null || stage.Arguments is null)
            return "The saved stage definition is incomplete.";
        if (task.DependsOn.Count != 0 || stage.DependsOn.Count != 0)
            return "This stage needs the pipeline dependency coordinator; running it alone would skip prerequisites.";
        if (stage.Arguments.Count != 0)
            return "The current command provider cannot execute this saved argument vector.";
        if (string.IsNullOrWhiteSpace(stage.Command) || stage.Command.Length > 32_000 || stage.Command.Contains('\0'))
            return "The saved command is missing or invalid.";
        var timeout = stage.Timeout?.TotalSeconds ?? 600;
        if (timeout <= 0 || timeout > 900) return "The current stage provider supports timeouts from 1 to 900 seconds.";
        return null;
    }

    private void CaptureSelectedSavedStage() => _selectedSavedStage = SavedStageChoices(_project)
        .SingleOrDefault(choice => choice.TaskId == _stageTaskId && choice.StageId == _stageId);

    private bool IsSelectedSavedStageCurrent(DeveloperResolvedProject project) => _selectedSavedStage is { } selected &&
        selected.Project == project.Reference && selected.TaskId == _stageTaskId && selected.StageId == _stageId &&
        selected.UnavailableReason is null && SavedStageChoices(project).Any(choice => choice.SelectionKey == selected.SelectionKey);

    private SavedStageChoice DemandSelectedSavedStage(DeveloperResolvedProject project, string taskId, string stageId)
    {
        if (!IsSelectedSavedStageCurrent(project) || _selectedSavedStage is not { } selected ||
            selected.TaskId != taskId || selected.StageId != stageId)
            throw new InvalidOperationException("The selected saved stage changed. Inspect and select its current definition before running it.");
        return selected;
    }

    private ValueTask SelectSavedBuildStage(object? parameter)
    {
        long generation; string key;
        lock (_gate)
        {
            if (IsActionAvailable("dev.select-build-stage") != true || parameter is not string actualKey)
                throw new InvalidOperationException("Choose an actual saved stage from this project.");
            generation = _generation; key = actualKey;
        }
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
            {
                var selected = SavedStageChoices(_project).SingleOrDefault(choice => choice.SelectionKey == key)
                    ?? throw new InvalidOperationException("The saved stage list changed. Inspect the current project stages again.");
                _selectedSavedStage = selected; _stageTaskId = selected.TaskId; _stageId = selected.StageId;
                _status = "Saved stage selected for inspection. Selection does not execute a command.";
            }
            NotifyOriginal(original);
        });
        return ValueTask.CompletedTask;
    }
}
