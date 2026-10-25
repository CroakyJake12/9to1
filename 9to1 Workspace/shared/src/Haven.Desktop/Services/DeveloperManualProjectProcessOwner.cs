using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

/// <summary>Retained original manual Run, independent from presentation. No canonical Task is
/// invented. Only a privately validated physical outcome plus actual exit proves completion.</summary>
public sealed record DeveloperManualProcessObservation(DeveloperProjectReference Project, string ConfigurationId,
    string DefinitionSha256, Guid? OperationId, ProcessResult? Process, WorkspaceToolPhysicalOutcome? PhysicalOutcome,
    bool CleanupCompleted, IReadOnlyList<Exception> OriginalErrors)
{
    public bool ProcessSucceeded => CleanupCompleted && OriginalErrors.Count == 0 && Process is { ExitCode: 0, TimedOut: false } &&
        PhysicalOutcome is { OutcomeUnknown: false, OriginalErrors.Count: 0, Effects.Count: 1 } outcome &&
        outcome.Effects[0] is { Kind: WorkspaceToolEffectKind.ProcessStart, Admitted: true, EffectKnown: true, TerminalOutcomeKnown: true, ExitCode: 0 };
}

/// <summary>The genuine manual project process owner. SAME registered Files READ and exact saved
/// declaration, separate explicit Home execution review, maintained original invocation/executor,
/// actual terminal output and independent closes. Owns only its operations; global Files/Home
/// providers remain borrowed and must drain after this owner.</summary>
public sealed partial class DeveloperManualProjectProcessOwner(NativeFilesWorkspaceService files,
    FilesDeveloperOriginalSourceSelection selections, IDeveloperProjectOriginalReadAdmissionSource reads,
    IDeveloperProjectOriginalManualExecutionAdmissionSource executions, IWorkspaceOriginalInvocationSource physical)
    : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<Operation> _operations = [];
    private bool _sealed; private Task? _close;
    private readonly CancellationTokenSource _retirement = new();
    private readonly List<Exception> _retirementErrors = [];
    private Task? _retirementRequest;
    private sealed class Operation
    {
        internal readonly CloudflareOriginalTaskLedger Sources = new();
        internal Task<ManualProcessOutcome> Driver = null!;
        internal Task Projection = null!;
        internal CancellationTokenSource? Stop;
    }
    public void DemandExternalOriginalJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Operation[] operations; lock (_gate) operations = _operations.ToArray();
        foreach (var operation in operations) CloudflareOriginalExecutionGuard.DemandExternalJoin(operation);
    }
    public Task<DeveloperManualProcessObservation> RunAsync(DeveloperResolvedProject project, DeveloperRunTargetChoice selected,
        Action<Action> originalCallerCallback, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalCallerCallback); DemandExternalOriginalJoin();
        if (selected.Project != project.Reference || selected.UnavailableReason is not null ||
            DeveloperRunConfigurationSelection.Digest(selected.Configuration) != selected.DefinitionSha256 ||
            DeveloperRunConfigurationSelection.Unavailable(project, selected.Configuration) is { })
            throw new UnauthorizedAccessException("A current supported declared console target is required for manual Run.");
        lock (_gate)
        {
            if (_sealed || _operations.Count >= 128) throw new InvalidOperationException("Manual process owner is closed or its retained operation custody is full.");
            var operation = new Operation();
            operation.Sources.BindOriginalOwner(operation); operation.Sources.BindOriginalCallerCallback(originalCallerCallback);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.Driver = RunPublishedAsync(begin.Task, operation, project, new ConsoleRequest(selected), token);
            var projection = ProjectConsoleAsync(operation.Driver, project, selected);
            operation.Projection = projection; _operations.Add(operation); begin.SetResult(); return projection;
        }
    }
    private async Task<ManualProcessOutcome> RunPublishedAsync(Task begin, Operation operation,
        DeveloperResolvedProject project, ManualRequest requestIdentity, CancellationToken token)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(operation);
        var sources = operation.Sources;
        IDeveloperProjectOriginalReadAdmission? read = null;
        IDeveloperProjectOriginalManualExecutionAdmission? execution = null;
        IWorkspaceOriginalInvocation? invocation = null;
        ProcessResult? result = null; WorkspaceToolPhysicalOutcome? outcome = null;
        Guid? operationId = null; bool cleanupCompleted = true;
        try
        {
            await sources.RunToOriginalSettlementAsync(async () =>
            {
                operation.Stop = sources.Invoke(() => CancellationTokenSource.CreateLinkedTokenSource(token, _retirement.Token));
                var business = operation.Stop.Token;
                var configuration = await sources.AwaitAsync(sources.Invoke(() => files.GetConfigurationAsync(business))).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The genuine configured Files workspace is unavailable.");
                var selection = await sources.AwaitAsync(sources.Invoke(() => selections.SelectOriginalRegisteredProjectAsync(configuration.StoreId, project, business))).ConfigureAwait(false);
                read = await sources.CaptureOriginalAcquisitionAsync(() => reads.AcquireOriginalAsync(selection, business), actual => read = actual).ConfigureAwait(false);
                var capture = await sources.AwaitAsync(sources.Invoke(() => selections.CaptureOriginalAsync(selection, read, business))).ConfigureAwait(false);
                var binding = await requestIdentity.CaptureAsync(selections, selection, read, capture, sources, business).ConfigureAwait(false);
                execution = await sources.CaptureOriginalAcquisitionAsync(() => binding.AcquireAsync(executions, business), actual => execution = actual).ConfigureAwait(false);
                operationId = execution.OperationId;
                var fence = execution.OriginalFence;
                if (physical is not IWorkspaceOriginalProcessStartEntryReleaseSource release)
                    throw new NotSupportedException("The maintained native process owner cannot release the Home start entry before waiting.");
                sources.Invoke(() => { release.DemandOriginalProcessStartEntryReleaseSupport(fence); return true; });
                invocation = sources.Invoke(() => physical.AcquireOriginalInvocation(fence));
                if (!physical.IsIssuedOriginalForFence(invocation, fence)) throw new UnauthorizedAccessException("The maintained native process invocation was not issued.");
                var request = WorkspaceToolProcessRequestFactory.CreateOriginal(binding.CanonicalRoot, binding.Command, binding.TimeoutSeconds);
                // Same maintained executor as Task commands, with its native root descriptor and
                // exact request preimage. No raw Process.Start or enum-only terminal factory.
                result = await sources.AwaitAsync(sources.Invoke(() => invocation.Tools.RunProcessAsync(request, business))).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception error) { sources.Retain(error); }
        if (invocation is not null)
        {
            try
            {
                outcome = await sources.AwaitAsync(sources.Invoke(() => invocation.CompleteOriginalAsync(result is not null, CancellationToken.None))).ConfigureAwait(false);
                if (!physical.ValidateOriginalOutcome(invocation, outcome)) throw new UnauthorizedAccessException("The physical manual Run outcome is not the SAME privately issued original.");
                if (execution is not null) sources.Invoke(() => { execution.ObserveOriginalProcessOutcome(invocation, result, outcome); return true; });
                foreach (var error in outcome.OriginalErrors) sources.Retain(error);
            }
            catch (Exception error) { sources.Retain(error); }
        }
        // Start every original close independently. No physical close failure can skip Home audit,
        // and no Home completion failure can skip the genuine READ close.
        var closes = new List<Task>();
        var closeSources = new List<Func<ValueTask>>();
        if (invocation is not null) closeSources.Add(invocation.DisposeAsync);
        if (execution is not null) closeSources.Add(execution.DisposeAsync);
        if (read is not null) closeSources.Add(read.DisposeAsync);
        foreach (var close in closeSources)
        {
            try { _ = sources.Invoke(() => { var actual = close().AsTask(); closes.Add(actual); _ = sources.Track(actual); return actual; }); }
            catch (Exception error) { cleanupCompleted = false; sources.Retain(error); }
        }
        foreach (var actual in closes)
            try { await sources.AwaitAsync(actual).ConfigureAwait(false); }
            catch (Exception error) { cleanupCompleted = false; sources.Retain(error); }
        try { sources.Invoke(() => { operation.Stop?.Dispose(); return true; }); } catch (Exception error) { cleanupCompleted = false; sources.Retain(error); }
        await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        return new(operationId, result, outcome, cleanupCompleted, Array.AsReadOnly(sources.OriginalErrors.ToArray()));
    }
    public void RequestOriginalRetirement()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource admitted;
        lock (_gate)
        {
            _sealed = true;
            if (_retirementRequest is not null) return;
            admitted = new(TaskCreationOptions.RunContinuationsAsynchronously); _retirementRequest = admitted.Task;
        }
        try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _retirement.Cancel(); return true; }); }
        catch (Exception error)
        {
            lock (_gate) _retirementErrors.Add(error);
            throw;
        }
        finally { admitted.SetResult(); } // Joins the actual finite callback cohort before CTS disposal.
    }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin();
        try { RequestOriginalRetirement(); }
        catch (Exception error)
        { lock (_gate) if (!_retirementErrors.Any(prior => ReferenceEquals(prior, error))) _retirementErrors.Add(error); }
        lock (_gate)
        {
            if (_close is not null) return _close; _sealed = true;
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = ClosePublishedAsync(begin.Task, _operations.ToArray()); begin.SetResult(); return _close;
        }
    }
    private async Task ClosePublishedAsync(Task begin, Operation[] operations)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var errors = new List<Exception>(); Task? retirement;
        lock (_gate) retirement = _retirementRequest;
        if (retirement is not null) await retirement.ConfigureAwait(false);
        lock (_gate) errors.AddRange(_retirementErrors);
        foreach (var operation in operations)
        {
            try
            {
                var observed = await operation.Driver.ConfigureAwait(false);
                errors.AddRange(observed.OriginalErrors);
            }
            catch (Exception error) { errors.Add(error); }
            try { await operation.Projection.ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        }
        try { _retirement.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Original manual Run operations require inspection.", errors.Distinct<Exception>(ReferenceEqualityComparer.Instance));
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}
