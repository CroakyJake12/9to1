using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private sealed record RunTargetItem(string SelectionKey, string Label, string Details, bool CanSelect);
    private DeveloperRunTargetChoice? _selectedRunTarget;
    private bool IsSelectedRunTargetCurrent(DeveloperResolvedProject project) => _selectedRunTarget is { UnavailableReason: null } selected &&
        selected.Project == project.Reference && DeveloperRunConfigurationSelection.Observe(project) is { Succeeded: true, Value: { } choices } &&
        choices.Any(choice => choice.SelectionKey == selected.SelectionKey && choice.UnavailableReason is null);

    private bool TryGetRunTargetValue(string path, out object? value)
    {
        if (path is not ("RunTargets" or "RunTargetsStatus" or "SelectedRunTargetSummary" or "CanRunSelectedTarget"))
        { value = null; return false; }
        var observed = DeveloperRunConfigurationSelection.Observe(_project);
        value = path switch
        {
            "RunTargets" => observed.Value?.Select(choice => new RunTargetItem(choice.SelectionKey,
                choice.Configuration.Id + " — " + choice.Configuration.TargetKind,
                choice.UnavailableReason is { } reason ? "Unavailable: " + reason : "Select to inspect the exact root, environment and command.", !_pending)).ToArray() ?? [],
            "RunTargetsStatus" => !observed.Succeeded ? observed.Error!.Message
                : observed.Value!.Count == 0 ? "This project declares no saved Run configurations. Configure a Workspace console target with an explicit Command to use this owner."
                : "Select a declared target to inspect its exact project, root, environment and command. Unsupported providers, arguments, prelaunch tasks and preview requests remain unavailable.",
            "SelectedRunTargetSummary" => _selectedRunTarget is not { } selected ? "Select a saved Run target."
                : selected.Summary + "\n" + (_manual ? "Execution needs an existing authorized task execution context; manual project mode does not grant it."
                    : selected.UnavailableReason ?? (!IsSelectedRunTargetCurrent(_project) ? "This target changed. Select its current declaration again."
                        : "Run uses this exact declaration through the current Task, Files and Home process admission owners.")),
            "CanRunSelectedTarget" => IsActionAvailable("dev.run-selected-target"),
            _ => null
        };
        return true;
    }
    private ValueTask SelectRunTarget(object? parameter)
    {
        long generation; string key;
        lock (_gate)
        {
            if (IsActionAvailable("dev.select-run-target") != true || parameter is not string actual)
                throw new InvalidOperationException("Select an actual declared Run target from this project.");
            generation = _generation; key = actual;
        }
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
            {
                var observed = DeveloperRunConfigurationSelection.Observe(_project);
                _selectedRunTarget = observed.Value?.SingleOrDefault(choice => choice.SelectionKey == key)
                    ?? throw new InvalidOperationException("This target declaration is no longer current. Inspect the current project configuration.");
                _status = "Run target selected for inspection. Selection grants no execution or preview permission.";
            }
            NotifyOriginal(original);
        });
        return ValueTask.CompletedTask;
    }

    private ValueTask RunSelectedTargetAsync(CancellationToken token)
    {
        long generation; DeveloperRunTargetChoice selected;
        lock (_gate)
        {
            if (IsActionAvailable("dev.run-selected-target") != true || _selectedRunTarget is not { } target)
                throw new InvalidOperationException("Select a current supported target with genuine task execution authority before running it.");
            generation = _generation; selected = target; _pending = true;
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
                    lock (_gate)
                        if (!ReferenceEquals(_selectedRunTarget, selected) || !IsSelectedRunTargetCurrent(project))
                            throw new InvalidOperationException("The selected Run target changed before admission. Select and inspect it again.");
                    var context = await CaptureContextAsync(original, token).ConfigureAwait(false);
                    original.DemandPublication();
                    var operation = RunTaskSourceAsync(original, project, context, () => _development.RunInspectedConfigurationAsync(
                        project.Reference, context, selected.Configuration.Id, selected.Configuration.Revision, selected.DefinitionSha256, _businessToken), _businessToken);
                    var observed = DemandValue(await original.AwaitAsync(operation).ConfigureAwait(false));
                    OriginalDeveloperObservation = observed;
                    await PublishAsync(original, () =>
                    {
                        _project = project; SetProcessObservation(observed);
                        _status = observed.RequiresOutcomeInspection ? "The original Run outcome needs inspection and will not be replayed automatically."
                            : observed.ProcessSucceeded is true ? "The selected console target completed successfully in the existing task."
                            : observed.ProcessSucceeded is false ? "The selected console target failed or timed out. Inspect its actual output."
                            : "No process result proves this Run succeeded. Inspect the original target action.";
                    }).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    original.Retain(error);
                    if (IsCurrent(generation))
                        try { await PublishAsync(original, () => _status = "Run target requires inspection: " + error.Message).ConfigureAwait(false); }
                        catch (Exception publication) { original.Retain(publication); }
                }
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
