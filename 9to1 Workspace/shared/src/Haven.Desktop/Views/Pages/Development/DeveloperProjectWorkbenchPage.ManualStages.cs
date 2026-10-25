using Haven.Desktop.Services;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    internal DeveloperManualStageObservation? OriginalManualStageObservation { get; private set; }
    private readonly List<DeveloperManualStageObservation> _originalManualStages = [];

    private ValueTask RunManualSavedStageAsync(CancellationToken token)
    {
        long generation; SavedStageChoice inspected;
        lock (_gate)
        {
            if (IsActionAvailable("dev.run-stage") != true || _manualProcessOwner is not { SupportsSavedStages: true })
                throw new InvalidOperationException("Saved-stage execution is unavailable for this project.");
            inspected = DemandSelectedSavedStage(_project, _stageTaskId, _stageId);
            generation = _generation; _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                try
                {
                    token.ThrowIfCancellationRequested();
                    var project = await ResolveProjectAsync(original, token).ConfigureAwait(false);
                    var current = DeveloperSavedStageSelection.Select(project, inspected.TaskId, inspected.TaskRevision,
                        inspected.StageId, inspected.DefinitionHash);
                    if (project.Reference != inspected.Project || !current.Succeeded || current.Value is not { } selected)
                        throw new InvalidOperationException(current.Error?.Message ?? "The saved stage changed. Select its current definition before running it.");
                    using var stop = CancellationTokenSource.CreateLinkedTokenSource(_businessToken);
                    lock (_gate) _manualRunStop = stop;
                    try
                    {
                        await PublishAsync(original, () => _status = "Waiting for permission to run the saved stage. Unsaved editor changes are kept.").ConfigureAwait(false);
                        var owner = _manualProcessOwner ?? throw new InvalidOperationException("Saved-stage execution is unavailable.");
                        var observed = await original.AwaitAsync(InvokeOriginalSource(() => owner.RunStageAsync(project, selected,
                            body => InvokeOriginalSource(() => { body(); return true; }), stop.Token))).ConfigureAwait(false);
                        // Actual terminal/lifecycle evidence is retained before any UI callback.
                        lock (_gate) { OriginalManualStageObservation = observed; _originalManualStages.Add(observed); }
                        await PublishAsync(original, () =>
                        {
                            _processStatus = observed.ProcessSucceeded ? $"{selected.Stage.Label} finished."
                                : observed.Process is { TimedOut: true } ? "The stage timed out. Its output is kept below."
                                : observed.Process is { } process ? $"The stage exited with code {process.ExitCode}. Check the output below."
                                : "The stage did not start. Check permissions and project setup.";
                            _processOutput = observed.Process is { } output ? output.StandardOutput +
                                (string.IsNullOrEmpty(output.StandardError) ? "" : "\n[standard error]\n" + output.StandardError) : "";
                            if (observed.OriginalErrors.Count != 0)
                                _processOutput += "\n[problems]\n" + string.Join(Environment.NewLine, observed.OriginalErrors.Select(error => error.Message));
                            _status = observed.ProcessSucceeded ? "The saved stage finished. Your unsaved drafts are unchanged." : "The saved stage did not finish cleanly. Output and problems are kept below.";
                        }).ConfigureAwait(false);
                        foreach (var error in observed.OriginalErrors) original.Retain(error);
                    }
                    finally { lock (_gate) if (ReferenceEquals(_manualRunStop, stop)) _manualRunStop = null; }
                }
                catch (Exception error) { original.Retain(error); }
                finally
                {
                    lock (_gate) _pending = false;
                    if (IsCurrent(generation))
                        try { await PublishAsync(original, () => { }).ConfigureAwait(false); }
                        catch (Exception error) { original.Retain(error); }
                    original.ThrowRetained();
                }
            }));
        }
        catch { lock (_gate) _pending = false; throw; }
    }
}
