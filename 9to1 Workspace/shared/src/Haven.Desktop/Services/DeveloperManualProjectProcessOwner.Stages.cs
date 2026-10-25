using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

public sealed record DeveloperManualStageObservation(DeveloperProjectReference Project, string SavedTaskId,
    long SavedTaskRevision, string StageId, DeveloperBuildStageKind StageKind, string DefinitionSha256, Guid? OperationId,
    ProcessResult? Process, WorkspaceToolPhysicalOutcome? PhysicalOutcome, bool CleanupCompleted, IReadOnlyList<Exception> OriginalErrors)
{
    public bool ProcessSucceeded => CleanupCompleted && OriginalErrors.Count == 0 && Process is { ExitCode: 0, TimedOut: false } &&
        PhysicalOutcome is { OutcomeUnknown: false, OriginalErrors.Count: 0, Effects.Count: 1 } outcome &&
        outcome.Effects[0] is { Kind: WorkspaceToolEffectKind.ProcessStart, Admitted: true, EffectKnown: true, TerminalOutcomeKnown: true, ExitCode: 0 };
}

public sealed partial class DeveloperManualProjectProcessOwner
{
    public bool SupportsSavedStages => executions is IDeveloperProjectOriginalManualStageAdmissionSource;
    public Task<DeveloperManualStageObservation> RunStageAsync(DeveloperResolvedProject project, DeveloperSavedStageChoice selected,
        Action<Action> originalCallerCallback, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalCallerCallback); DemandExternalOriginalJoin();
        var actual = DeveloperSavedStageSelection.Select(project, selected.Task.TaskId, selected.Task.Revision,
            selected.Stage.StageId, selected.DefinitionSha256);
        if (!SupportsSavedStages || selected.Project != project.Reference || !actual.Succeeded || actual.Value is not { } captured)
            throw new UnauthorizedAccessException("A current supported saved stage and original process admission are required.");
        // Re-select from the actual project snapshot. Never execute a caller-substituted Stage
        // with a copied definition hash or mutable list supplied after inspection.
        lock (_gate)
        {
            if (_sealed || _operations.Count >= 128) throw new InvalidOperationException("Manual process owner is closed or retained custody is full.");
            var operation = new Operation(); operation.Sources.BindOriginalOwner(operation); operation.Sources.BindOriginalCallerCallback(originalCallerCallback);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.Driver = RunPublishedAsync(begin.Task, operation, project, new StageRequest(captured), token);
            var projection = ProjectStageAsync(operation.Driver, project, captured);
            operation.Projection = projection; _operations.Add(operation); begin.SetResult(); return projection;
        }
    }
    private static async Task<DeveloperManualStageObservation> ProjectStageAsync(Task<ManualProcessOutcome> original,
        DeveloperResolvedProject project, DeveloperSavedStageChoice selected)
    {
        var result = await original.ConfigureAwait(false);
        return new(project.Reference, selected.Task.TaskId, selected.Task.Revision, selected.Stage.StageId, selected.Stage.Kind,
            selected.DefinitionSha256, result.OperationId, result.Process, result.PhysicalOutcome, result.CleanupCompleted, result.OriginalErrors);
    }
    private sealed class StageRequest(DeveloperSavedStageChoice selected) : ManualRequest
    {
        internal override async Task<ProcessBinding> CaptureAsync(FilesDeveloperOriginalSourceSelection selections, IDeveloperProjectOriginalReadSelection selection,
            IDeveloperProjectOriginalReadAdmission read, IDeveloperProjectOriginalExistingSourceCapture capture, CloudflareOriginalTaskLedger sources, CancellationToken token)
        {
            var binding = await sources.AwaitAsync(sources.Invoke(() => selections.CaptureOriginalManualStageAsync(selection, read, capture,
                selected.Task.TaskId, selected.Task.Revision, selected.Stage.StageId, selected.DefinitionSha256, token))).ConfigureAwait(false);
            return new StageProcessBinding(binding);
        }
    }
    private sealed class StageProcessBinding(IDeveloperProjectOriginalManualStageBinding value) : ProcessBinding
    {
        internal override string CanonicalRoot => value.CanonicalRoot;
        internal override string Command => value.Command;
        internal override int TimeoutSeconds => value.TimeoutSeconds;
        internal override Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireAsync(IDeveloperProjectOriginalManualExecutionAdmissionSource source, CancellationToken token)
            => source is IDeveloperProjectOriginalManualStageAdmissionSource stage ? stage.AcquireOriginalManualStageAsync(value, token)
                : throw new NotSupportedException("The configured process provider cannot run saved stages.");
    }
}
