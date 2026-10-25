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
        (DeveloperSavedStageSelection.Observe(project).Value ?? []).Select(choice =>
        {
            var task = choice.Task; var stage = choice.Stage;
            var summary = $"{task.Label} / {stage.Label} ({stage.Kind})\n" +
                $"Command: {stage.Command ?? "(no saved command)"}\nArguments: {JsonSerializer.Serialize(stage.Arguments)}\n" +
                $"Dependencies: {string.Join(", ", task.DependsOn.Concat(stage.DependsOn))}. " +
                $"Timeout: {stage.Timeout?.TotalSeconds ?? 600} seconds.\n" +
                "Runs saved files. Unsaved editor changes are kept; review and save them first if the stage needs them.";
            return new SavedStageChoice(project.Reference, task.TaskId, task.Revision, stage.StageId,
                choice.DefinitionSha256, task.Label + " / " + stage.Label, summary, choice.UnavailableReason);
        }).ToArray();

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
