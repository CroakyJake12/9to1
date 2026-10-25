using System.Globalization;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private string _runEditId = "run", _runEditCommand = "", _runEditTimeout = "120";
    private string? _runEditBeforeSha256;
    private string? _runEditExistingId;
    private long _runEditInputRevision;
    private DeveloperRunConfigurationEditReview? _runEditReview;
    public DeveloperRunConfigurationEditObservation? OriginalRunConfigurationEdit { get; private set; }
    private bool CanConfigureRunTarget() => _manual && _manualReader is not null && _editorSessionStore is not null &&
        _manualRecoveryKeys is not null && _manualRecoveryPaths is not null && _project.Root.EnvironmentId == "local";
    private DeveloperOperationResult<DeveloperRunConfigurationEditReview> PrepareRunTargetEdit() =>
        _runEditExistingId is { } old && old != _runEditId
            ? DeveloperOperationResult<DeveloperRunConfigurationEditReview>.Failure(DeveloperOperationErrorCode.InvalidInput,
                "An existing target's ID is stable. Choose New target to create another declaration.", "run-configurations")
            : !int.TryParse(_runEditTimeout, NumberStyles.None, CultureInfo.InvariantCulture, out var timeout)
                ? DeveloperOperationResult<DeveloperRunConfigurationEditReview>.Failure(DeveloperOperationErrorCode.InvalidInput,
                    "Enter an integer timeout from 1 to 900 seconds.", "run-configurations")
                : DeveloperRunConfigurationEdit.Prepare(_project, _runEditId, _runEditCommand, timeout, _runEditBeforeSha256);
    private bool CurrentRunTargetEditReview() => _runEditReview is { } reviewed &&
        PrepareRunTargetEdit() is { Succeeded: true, Value: { } current } && current.AfterDefinitionSha256 == reviewed.AfterDefinitionSha256 &&
        DeveloperRunConfigurationEdit.IsCurrent(_project, reviewed);
    private bool TryGetRunEditValue(string path, out object? value)
    {
        value = path switch
        {
            "RunEditId" => _runEditId, "RunEditCommand" => _runEditCommand, "RunEditTimeout" => _runEditTimeout,
            "CanConfigureRunTarget" => !_pending && CanConfigureRunTarget(),
            "CanEditRunTarget" => IsActionAvailable("dev.edit-run-target"),
            "CanReviewRunTarget" => IsActionAvailable("dev.review-run-target"), "CanSaveRunTarget" => IsActionAvailable("dev.save-run-target"),
            "RunEditReview" => _runEditReview?.Summary ?? PrepareRunTargetEdit().Error?.Message ?? "Review the exact declaration before saving it. Saving never starts a command.",
            "RunEditStatus" => !CanConfigureRunTarget() ? "Target authoring requires the genuine manual registered-project reader, canonical store and original protected-key owner."
                : "Create a target or select an existing target and choose Edit selected. Save preserves live buffers and atomically migrates compatible manual recovery. Another affected profile/Task/root/repository recovery refuses the entire update; it is never discarded.",
            _ => null
        };
        return path is "RunEditId" or "RunEditCommand" or "RunEditTimeout" or "CanConfigureRunTarget" or "CanEditRunTarget" or "CanReviewRunTarget" or "CanSaveRunTarget" or "RunEditReview" or "RunEditStatus";
    }
    private bool SetRunEditValue(string path, object? value)
    {
        if (path is not ("RunEditId" or "RunEditCommand" or "RunEditTimeout") || value is not string text) return false;
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
            {
                if (_pending || !CanConfigureRunTarget()) throw new InvalidOperationException("Wait for the current operation before editing its declaration.");
                if (path == "RunEditId") _runEditId = text;
                else if (path == "RunEditCommand") _runEditCommand = text;
                else _runEditTimeout = text;
                _runEditReview = null; _runEditInputRevision++;
            }
            NotifyOriginal(original);
        });
        return true;
    }
    private ValueTask ConfigureRunTarget(string command)
    {
        long generation; lock (_gate)
        {
            if (IsActionAvailable(command) != true) throw new InvalidOperationException("Choose a current supported manual target operation.");
            generation = _generation;
        }
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
            {
                if (command == "dev.review-run-target")
                {
                    _runEditReview = DemandValue(PrepareRunTargetEdit());
                    _status = "Review the complete saved declaration below. Save changes metadata/recovery only; Run remains separately approved in Home.";
                }
                else
                {
                    var selected = command == "dev.edit-run-target" ? _selectedRunTarget : null;
                    _runEditExistingId = selected?.Configuration.Id; _runEditBeforeSha256 = selected?.DefinitionSha256;
                    _runEditId = selected?.Configuration.Id ?? "run"; _runEditCommand = selected?.Configuration.Command ?? "";
                    _runEditTimeout = (selected?.Configuration.TimeoutSeconds ?? 120).ToString(CultureInfo.InvariantCulture);
                    _runEditReview = null; _runEditInputRevision++;
                    _status = selected is null ? "Enter a new exact target ID, console command and timeout." : "Edit the selected target. Its ID and all unsupported provider/dependency fields stay intact.";
                }
            }
            NotifyOriginal(original);
        });
        return ValueTask.CompletedTask;
    }
    private ValueTask SaveRunTargetAsync(CancellationToken token)
    {
        long generation, inputRevision, expectedRecovery;
        DeveloperRunConfigurationEditReview reviewed;
        lock (_gate)
        {
            if (IsActionAvailable("dev.save-run-target") != true || _runEditReview is not { } actual)
                throw new InvalidOperationException("Review the current exact console declaration first.");
            generation = _generation; inputRevision = _runEditInputRevision; reviewed = actual;
            expectedRecovery = _manualRecoveryGeneration; _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                var committed = false; var continueRecovery = false;
                try
                {
                    token.ThrowIfCancellationRequested();
                    var before = await ResolveProjectAsync(original, token).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (_runEditInputRevision != inputRevision || !ReferenceEquals(_runEditReview, reviewed) ||
                            !DeveloperRunConfigurationEdit.IsCurrent(before, reviewed))
                            throw new InvalidOperationException("The declaration or project changed after review. Review it again.");
                        _runEditReview = null; // A saved/uncertain original is never automatically replayed.
                    }
                    var observed = await original.AwaitAsync(InvokeOriginalSource(() => _manualReader!.SaveReviewedRunConfigurationAsync(
                        _editorSessionStore!, _editorSessionExecution, _manualRecoveryKeys!, _manualRecoveryPaths!, before, reviewed,
                        expectedRecovery, body => InvokeOriginalSource(() => { body(); return true; }), _businessToken))).ConfigureAwait(false);
                    lock (_gate) OriginalRunConfigurationEdit = observed;
                    if (observed.OriginalStoreResult is { Succeeded: true, Value: { } saved })
                    {
                        // Record committed metadata privately before presentation. A late notification
                        // failure does not leave live drafts tied to an obsolete project/recovery header.
                        lock (_gate)
                        {
                            if (_project.Reference != before.Reference) throw new InvalidOperationException("The declaration was saved but this page changed. Inspect the retained metadata result.");
                            AdoptSavedRunTarget(saved); committed = true;
                            continueRecovery = saved.MigratedRecovery || _sourceTabs.Any(IsTabDirty) || _manualRecoveryAcknowledged is not null;
                        }
                        await PublishAsync(original, () => _status = observed.OriginalErrors.Count == 0
                            ? "Saved the reviewed console target and compatible recovery atomically. Live drafts are retained; Run requires its own Home approval."
                            : "The declaration was committed, but original lifecycle cleanup needs inspection. Live drafts and the committed result are retained; no retry occurs.").ConfigureAwait(false);
                    }
                    else
                        await PublishAsync(original, () => _status = observed.OriginalStoreResult?.Error?.Message ??
                            "No canonical result proves the declaration was saved. Inspect this original operation; no retry occurs.").ConfigureAwait(false);
                    foreach (var error in observed.OriginalErrors) original.Retain(error);
                }
                catch (Exception error)
                {
                    original.Retain(error);
                    if (IsCurrent(generation))
                        try { await PublishAsync(original, () => _status = "Target configuration needs inspection: " + error.Message).ConfigureAwait(false); }
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
    private void AdoptSavedProjectMetadata(DeveloperResolvedProject project, DeveloperDraftRecoveryScope scope,
        long recoveryGeneration, long? cleanSessionGeneration)
    {
        StoreSelectedTab(); _project = project;
        _manualRecoveryGeneration = recoveryGeneration; _manualRecoveryScope = scope;
        if (cleanSessionGeneration is { } cleanGeneration) _cleanSessionGeneration = cleanGeneration;
        _manualRecoveryRequested = null; _manualRecoverySerial++;
        foreach (var tab in _sourceTabs)
            if (tab.Manual is { } manual) tab.Manual = manual with { Reviewed = false, ReviewRevision = -1, After = "", AfterSha256 = "" };
        if (CurrentSourceTab() is { } selected) SelectTab(selected);
        _manualReviewed = false; _manualReviewRevision = -1; _manualAfterText = ""; _manualAfterSha256 = "";
        _newFileReview = null; _runEditReview = null; _stageEditReview = null;
        InvalidateProjectSearch(); _projectEntries = null;
    }
    private void AdoptSavedRunTarget(DeveloperRunConfigurationEditResult saved)
    {
        AdoptSavedProjectMetadata(saved.Project, saved.Scope, saved.RecoveryGeneration, saved.CleanSessionGeneration);
        _selectedRunTarget = DeveloperRunConfigurationSelection.Observe(_project).Value?.SingleOrDefault(value => value.Configuration.Id == saved.Configuration.Id);
        _runEditBeforeSha256 = DeveloperRunConfigurationSelection.Digest(saved.Configuration); _runEditExistingId = saved.Configuration.Id;
        _runEditId = saved.Configuration.Id; _runEditCommand = saved.Configuration.Command!;
        _runEditTimeout = (saved.Configuration.TimeoutSeconds ?? 120).ToString(CultureInfo.InvariantCulture); _runEditInputRevision++;
    }
}
