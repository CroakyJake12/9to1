using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

/// <summary>Actual Git inspection observation. No launch configuration or Task ID is invented.</summary>
public sealed record DeveloperManualGitObservation(DeveloperProjectReference Project, DeveloperProjectGitInspectionKind Operation,
    string RepositoryBindingId, string CanonicalRepositoryId, string DefinitionSha256, Guid? OperationId,
    ProcessResult? Process, WorkspaceToolPhysicalOutcome? PhysicalOutcome, bool CleanupCompleted, IReadOnlyList<Exception> OriginalErrors)
{
    public bool ProcessSucceeded => CleanupCompleted && OriginalErrors.Count == 0 && Process is { ExitCode: 0, TimedOut: false } &&
        PhysicalOutcome is { OutcomeUnknown: false, OriginalErrors.Count: 0, Effects.Count: 1 } outcome &&
        outcome.Effects[0] is { Kind: WorkspaceToolEffectKind.ProcessStart, Admitted: true, EffectKnown: true, TerminalOutcomeKnown: true, ExitCode: 0 };
}

public sealed partial class DeveloperManualProjectProcessOwner
{
    public bool SupportsGitInspection => executions is IDeveloperProjectOriginalManualGitAdmissionSource;
    public Task<DeveloperManualGitObservation> InspectGitAsync(DeveloperResolvedProject project, DeveloperProjectGitInspectionKind kind,
        string definitionSha256, Action<Action> originalCallerCallback, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalCallerCallback); DemandExternalOriginalJoin();
        var prepared = DeveloperGitCommand.Prepare(project, kind);
        if (!SupportsGitInspection || !prepared.Succeeded || prepared.Value?.DefinitionSha256 != definitionSha256 || project.Repository is null)
            throw new UnauthorizedAccessException("A current supported Git inspection and original process admission are required.");
        lock (_gate)
        {
            if (_sealed || _operations.Count >= 128) throw new InvalidOperationException("Manual process owner is closed or retained custody is full.");
            var operation = new Operation(); operation.Sources.BindOriginalOwner(operation); operation.Sources.BindOriginalCallerCallback(originalCallerCallback);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.Driver = RunPublishedAsync(begin.Task, operation, project, new GitRequest(kind, definitionSha256), token);
            var projection = ProjectGitAsync(operation.Driver, project, kind, definitionSha256);
            operation.Projection = projection; _operations.Add(operation); begin.SetResult(); return projection;
        }
    }
    private static async Task<DeveloperManualGitObservation> ProjectGitAsync(Task<ManualProcessOutcome> original,
        DeveloperResolvedProject project, DeveloperProjectGitInspectionKind kind, string digest)
    {
        var result = await original.ConfigureAwait(false);
        return new(project.Reference, kind, project.Repository!.BindingId, project.Repository.CanonicalRepositoryId, digest,
            result.OperationId, result.Process, result.PhysicalOutcome, result.CleanupCompleted, result.OriginalErrors);
    }
    private static async Task<DeveloperManualProcessObservation> ProjectConsoleAsync(Task<ManualProcessOutcome> original,
        DeveloperResolvedProject project, DeveloperRunTargetChoice selected)
    {
        var result = await original.ConfigureAwait(false);
        return new(project.Reference, selected.Configuration.Id, selected.DefinitionSha256, result.OperationId,
            result.Process, result.PhysicalOutcome, result.CleanupCompleted, result.OriginalErrors);
    }
    private sealed record ManualProcessOutcome(Guid? OperationId, ProcessResult? Process, WorkspaceToolPhysicalOutcome? PhysicalOutcome,
        bool CleanupCompleted, IReadOnlyList<Exception> OriginalErrors);
    private abstract class ManualRequest
    {
        internal abstract Task<ProcessBinding> CaptureAsync(FilesDeveloperOriginalSourceSelection selections, IDeveloperProjectOriginalReadSelection selection,
            IDeveloperProjectOriginalReadAdmission read, IDeveloperProjectOriginalExistingSourceCapture capture, CloudflareOriginalTaskLedger sources, CancellationToken token);
    }
    private sealed class ConsoleRequest(DeveloperRunTargetChoice selected) : ManualRequest
    {
        internal override async Task<ProcessBinding> CaptureAsync(FilesDeveloperOriginalSourceSelection selections, IDeveloperProjectOriginalReadSelection selection,
            IDeveloperProjectOriginalReadAdmission read, IDeveloperProjectOriginalExistingSourceCapture capture, CloudflareOriginalTaskLedger sources, CancellationToken token)
        {
            var binding = await sources.AwaitAsync(sources.Invoke(() => selections.CaptureOriginalManualExecutionAsync(selection, read, capture,
                selected.Configuration.Id, selected.Configuration.Revision, selected.DefinitionSha256, token))).ConfigureAwait(false);
            return new ConsoleProcessBinding(binding);
        }
    }
    private sealed class GitRequest(DeveloperProjectGitInspectionKind kind, string digest) : ManualRequest
    {
        internal override async Task<ProcessBinding> CaptureAsync(FilesDeveloperOriginalSourceSelection selections, IDeveloperProjectOriginalReadSelection selection,
            IDeveloperProjectOriginalReadAdmission read, IDeveloperProjectOriginalExistingSourceCapture capture, CloudflareOriginalTaskLedger sources, CancellationToken token)
        {
            var binding = await sources.AwaitAsync(sources.Invoke(() => selections.CaptureOriginalManualGitAsync(selection, read, capture, kind, digest, token))).ConfigureAwait(false);
            return new GitProcessBinding(binding);
        }
    }
    private abstract class ProcessBinding
    {
        internal abstract string CanonicalRoot { get; }
        internal abstract string Command { get; }
        internal abstract int TimeoutSeconds { get; }
        internal abstract Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireAsync(IDeveloperProjectOriginalManualExecutionAdmissionSource source, CancellationToken token);
    }
    private sealed class ConsoleProcessBinding(IDeveloperProjectOriginalManualExecutionBinding value) : ProcessBinding
    {
        internal override string CanonicalRoot => value.CanonicalRoot;
        internal override string Command => value.Command;
        internal override int TimeoutSeconds => value.TimeoutSeconds;
        internal override Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireAsync(IDeveloperProjectOriginalManualExecutionAdmissionSource source, CancellationToken token)
            => source.AcquireOriginalManualExecutionAsync(value, token);
    }
    private sealed class GitProcessBinding(IDeveloperProjectOriginalManualGitBinding value) : ProcessBinding
    {
        internal override string CanonicalRoot => value.CanonicalRoot;
        internal override string Command => value.Command;
        internal override int TimeoutSeconds => value.TimeoutSeconds;
        internal override Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireAsync(IDeveloperProjectOriginalManualExecutionAdmissionSource source, CancellationToken token)
            => source is IDeveloperProjectOriginalManualGitAdmissionSource git ? git.AcquireOriginalManualGitAsync(value, token)
                : throw new NotSupportedException("The configured Home owner cannot admit Git inspection.");
    }
}
