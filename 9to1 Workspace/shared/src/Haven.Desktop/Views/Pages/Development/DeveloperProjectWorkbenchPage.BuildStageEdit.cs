using System.Globalization;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private string _stageEditTaskId = "build", _stageEditStageId = "compile", _stageEditCommand = "", _stageEditTimeout = "600";
    private DeveloperBuildStageKind _stageEditKind = DeveloperBuildStageKind.Compile;
    private string? _stageEditBeforeSha256, _stageEditExistingTaskId, _stageEditExistingStageId;
    private long _stageEditInputRevision;
    private DeveloperBuildStageEditReview? _stageEditReview;
    public DeveloperBuildStageEditObservation? OriginalBuildStageEdit { get; private set; }
    private bool CanConfigureBuildStage() => CanConfigureRunTarget();
    private DeveloperSavedStageChoice? CurrentStageEditChoice() => _selectedSavedStage is { } selected && selected.Project == _project.Reference
        ? DeveloperSavedStageSelection.Observe(_project).Value?.SingleOrDefault(choice => choice.Task.TaskId == selected.TaskId &&
            choice.Stage.StageId == selected.StageId && choice.DefinitionSha256 == selected.DefinitionHash && choice.UnavailableReason is null &&
            (choice.Task.Source is "user" or "workspace") && (choice.Stage.Kind is DeveloperBuildStageKind.Compile or DeveloperBuildStageKind.Test) &&
            (choice.Stage.Timeout is null || choice.Stage.Timeout.Value.Ticks % TimeSpan.TicksPerSecond == 0)) : null;
    private DeveloperOperationResult<DeveloperBuildStageEditReview> PrepareBuildStageEdit() =>
        _stageEditExistingTaskId is { } task && task != _stageEditTaskId || _stageEditExistingStageId is { } stage && stage != _stageEditStageId
            ? DeveloperOperationResult<DeveloperBuildStageEditReview>.Failure(DeveloperOperationErrorCode.InvalidInput,
                "Existing task/stage IDs are stable. Choose New Compile or New Test to create another task.", "build-stages")
            : !int.TryParse(_stageEditTimeout, NumberStyles.None, CultureInfo.InvariantCulture, out var timeout)
                ? DeveloperOperationResult<DeveloperBuildStageEditReview>.Failure(DeveloperOperationErrorCode.InvalidInput,
                    "Enter an integer timeout from 1 to 900 seconds.", "build-stages")
                : DeveloperBuildStageEdit.Prepare(_project, _stageEditTaskId, _stageEditStageId, _stageEditKind, _stageEditCommand, timeout,
                    _stageEditBeforeSha256, _stageEditExistingStageId);
    private bool CurrentBuildStageEditReview() => _stageEditReview is { } reviewed &&
        PrepareBuildStageEdit() is { Succeeded: true, Value: { } current } && current.AfterTaskSha256 == reviewed.AfterTaskSha256 &&
        DeveloperBuildStageEdit.IsCurrent(_project, reviewed);
    private bool TryGetBuildStageEditValue(string path, out object? value)
    {
        value = path switch
        {
            "StageEditTaskId" => _stageEditTaskId, "StageEditStageId" => _stageEditStageId, "StageEditCommand" => _stageEditCommand,
            "StageEditTimeout" => _stageEditTimeout, "StageEditKind" => _stageEditKind.ToString(),
            "CanConfigureBuildStage" => !_pending && CanConfigureBuildStage(),
            "CanEditBuildStage" => IsActionAvailable("dev.edit-build-stage"), "CanReviewBuildStage" => IsActionAvailable("dev.review-build-stage"),
            "CanSaveBuildStage" => IsActionAvailable("dev.save-build-stage"),
            "StageEditReview" => _stageEditReview?.Summary ?? PrepareBuildStageEdit().Error?.Message ?? "Review the exact stage before saving. Saving never runs it.",
            "StageEditStatus" => !CanConfigureBuildStage() ? "Open a configured manual project to author its saved stages."
                : "Create an independent Compile or Test task, or edit the selected supported stage. Saving preserves live drafts and compatible protected recovery. Dependent pipelines, argument vectors, managed providers and fractional timeouts stay in their owning tools.",
            _ => null
        };
        return path is "StageEditTaskId" or "StageEditStageId" or "StageEditCommand" or "StageEditTimeout" or "StageEditKind" or
            "CanConfigureBuildStage" or "CanEditBuildStage" or "CanReviewBuildStage" or "CanSaveBuildStage" or "StageEditReview" or "StageEditStatus";
    }
    private bool SetBuildStageEditValue(string path, object? value)
    {
        if (path is not ("StageEditTaskId" or "StageEditStageId" or "StageEditCommand" or "StageEditTimeout") || value is not string text) return false;
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
            {
                if (_pending || !CanConfigureBuildStage()) throw new InvalidOperationException("Wait for the current operation before editing its stage.");
                if (path == "StageEditTaskId") _stageEditTaskId = text;
                else if (path == "StageEditStageId") _stageEditStageId = text;
                else if (path == "StageEditCommand") _stageEditCommand = text;
                else _stageEditTimeout = text;
                _stageEditReview = null; _stageEditInputRevision++;
            }
            NotifyOriginal(original);
        });
        return true;
    }
    private ValueTask ConfigureBuildStage(string command)
    {
        long generation; lock (_gate)
        {
            if (IsActionAvailable(command) != true) throw new InvalidOperationException("Choose a supported current manual stage operation.");
            generation = _generation;
        }
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
            {
                if (command == "dev.review-build-stage")
                {
                    _stageEditReview = DemandValue(PrepareBuildStageEdit());
                    _status = "Review the exact saved stage below. Save changes metadata and compatible recovery only; Run asks permission separately.";
                }
                else
                {
                    var selected = command == "dev.edit-build-stage" ? CurrentStageEditChoice() : null;
                    if (command == "dev.edit-build-stage" && selected is null) throw new InvalidOperationException("Select the current supported stage again.");
                    _stageEditKind = selected?.Stage.Kind ?? (command == "dev.new-test-stage" ? DeveloperBuildStageKind.Test : DeveloperBuildStageKind.Compile);
                    _stageEditExistingTaskId = selected?.Task.TaskId; _stageEditExistingStageId = selected?.Stage.StageId;
                    _stageEditBeforeSha256 = selected?.DefinitionSha256;
                    _stageEditTaskId = selected?.Task.TaskId ?? (_stageEditKind == DeveloperBuildStageKind.Test ? "tests" : "build");
                    _stageEditStageId = selected?.Stage.StageId ?? (_stageEditKind == DeveloperBuildStageKind.Test ? "test" : "compile");
                    _stageEditCommand = selected?.Stage.Command ?? "";
                    _stageEditTimeout = (selected?.Stage.Timeout?.TotalSeconds ?? 600).ToString(CultureInfo.InvariantCulture);
                    _stageEditReview = null; _stageEditInputRevision++;
                    _status = selected is null ? "Enter a new independent stage's task ID, stage ID, command and timeout."
                        : "Edit the selected stage. Its stable IDs and all other saved stages stay unchanged.";
                }
            }
            NotifyOriginal(original);
        });
        return ValueTask.CompletedTask;
    }
    private ValueTask SaveBuildStageAsync(CancellationToken token)
    {
        long generation, inputRevision, expectedRecovery; DeveloperBuildStageEditReview reviewed;
        lock (_gate)
        {
            if (IsActionAvailable("dev.save-build-stage") != true || _stageEditReview is not { } actual)
                throw new InvalidOperationException("Review the current exact stage first.");
            generation = _generation; inputRevision = _stageEditInputRevision; reviewed = actual;
            expectedRecovery = _manualRecoveryGeneration; _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation)); var committed = false; var continueRecovery = false;
                try
                {
                    token.ThrowIfCancellationRequested(); var before = await ResolveProjectAsync(original, token).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (_stageEditInputRevision != inputRevision || !ReferenceEquals(_stageEditReview, reviewed) || !DeveloperBuildStageEdit.IsCurrent(before, reviewed))
                            throw new InvalidOperationException("The stage or project changed after review. Review it again.");
                        _stageEditReview = null;
                    }
                    var observed = await original.AwaitAsync(InvokeOriginalSource(() => _manualReader!.SaveReviewedBuildStageAsync(
                        _editorSessionStore!, _editorSessionExecution, _manualRecoveryKeys!, _manualRecoveryPaths!, before, reviewed, expectedRecovery,
                        body => InvokeOriginalSource(() => { body(); return true; }), _businessToken))).ConfigureAwait(false);
                    lock (_gate) OriginalBuildStageEdit = observed;
                    if (observed.OriginalStoreResult is { Succeeded: true, Value: { } saved })
                    {
                        lock (_gate)
                        {
                            if (_project.Reference != before.Reference) throw new InvalidOperationException("The stage was saved but this page changed. Inspect the retained result.");
                            AdoptSavedBuildStage(saved); committed = true;
                            continueRecovery = saved.MigratedRecovery || _sourceTabs.Any(IsTabDirty) || _manualRecoveryAcknowledged is not null;
                        }
                        await PublishAsync(original, () => _status = observed.OriginalErrors.Count == 0
                            ? "Saved the reviewed stage and compatible recovery. Your live drafts are kept. Run still asks permission."
                            : "The stage was saved, but cleanup needs inspection. Your drafts and the saved result are kept; no retry occurs.").ConfigureAwait(false);
                    }
                    else await PublishAsync(original, () => _status = observed.OriginalStoreResult?.Error?.Message ??
                        "No result proves the stage was saved. Inspect the retained operation before retrying.").ConfigureAwait(false);
                    foreach (var error in observed.OriginalErrors) original.Retain(error);
                }
                catch (Exception error)
                {
                    original.Retain(error);
                    if (IsCurrent(generation))
                        try { await PublishAsync(original, () => _status = "Stage configuration needs inspection: " + error.Message).ConfigureAwait(false); }
                        catch (Exception publication) { original.Retain(publication); }
                }
                finally
                {
                    lock (_gate) _pending = false;
                    if (committed && continueRecovery) ScheduleManualRecoverySave(forceCompleteSnapshot: true);
                    if (IsCurrent(generation))
                        try { await PublishAsync(original, () => { }).ConfigureAwait(false); } catch (Exception error) { original.Retain(error); }
                    original.ThrowRetained();
                }
            }));
        }
        catch { lock (_gate) _pending = false; throw; }
    }
    private void AdoptSavedBuildStage(DeveloperBuildStageEditResult saved)
    {
        AdoptSavedProjectMetadata(saved.Project, saved.Scope, saved.RecoveryGeneration, saved.CleanSessionGeneration);
        _stageTaskId = saved.Task.TaskId; _stageId = saved.StageId; CaptureSelectedSavedStage();
        var stage = saved.Task.Stages.Single(value => value.StageId == saved.StageId);
        _stageEditExistingTaskId = _stageEditTaskId = saved.Task.TaskId; _stageEditExistingStageId = _stageEditStageId = saved.StageId;
        _stageEditBeforeSha256 = DeveloperTaskWorkspaceService.ComputeSavedTaskDefinitionSha256(saved.Task);
        _stageEditKind = stage.Kind; _stageEditCommand = stage.Command!;
        _stageEditTimeout = (stage.Timeout?.TotalSeconds ?? 600).ToString(CultureInfo.InvariantCulture); _stageEditInputRevision++;
    }
}
