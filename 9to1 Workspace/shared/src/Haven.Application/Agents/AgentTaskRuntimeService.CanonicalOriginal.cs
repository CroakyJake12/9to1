using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Only actual producer/iterator custody. The persisted Agent record is a display projection.</summary>
internal sealed class AgentCanonicalOriginal(AgentRun expected)
{
    internal readonly object Gate = new();
    internal AgentRun Expected = expected;
    internal ChatOriginalAgentInvocation? Chat;
    internal int Active;
    internal CancellationTokenSource? Lifetime;
    internal bool CancellationSealed;
    internal Task? OriginalCancellation;
    internal bool LifetimeDisposed;
    internal readonly List<Task> ActualMoves = [];
    internal readonly List<Task> ActualDisposals = [];
    internal readonly List<Task> ActualPersistence = [];
    internal readonly List<AgentRuntimeOriginalCustody> ActualHistoryOperations = [];
    private readonly List<Exception> _causes = [];
    internal IReadOnlyList<Exception> Causes { get { lock (Gate) return _causes.ToArray(); } }
    internal readonly List<Task> ActualPreparations = [];
    internal readonly List<ToolActivity> Activities = [];
    internal readonly StringBuilder Output = new();

    internal void Retain(Exception cause, Task? actual = null)
    {
        IEnumerable<Exception> sources = actual?.IsFaulted == true && actual.Exception is { } fault
            ? fault.InnerExceptions : new[] { cause };
        lock (Gate)
            foreach (var original in sources)
                if (!_causes.Any(known => ReferenceEquals(known, original))) _causes.Add(original);
    }

    internal bool HasSuccessfulStreamComplete => Chat is { HasAcknowledgedCompletion: true }
        && Causes.Count == 0 && ActualMoves.Count > 0
        && ActualMoves.All(actual => actual.IsCompletedSuccessfully)
        && ActualDisposals.Count > 0 && ActualDisposals.All(actual => actual.IsCompletedSuccessfully);
    internal bool HasSuccessfulComplete => HasSuccessfulStreamComplete && LifetimeDisposed
        && (OriginalCancellation is null || OriginalCancellation.IsCompletedSuccessfully)
        && ActualPersistence.All(actual => actual.IsCompletedSuccessfully);
}

public sealed partial class AgentTaskRuntimeService
{
    private readonly object _originalOperationGate = new();
    private readonly List<AgentRuntimeOriginalCustody> _originalOperations = [];
    internal IReadOnlyList<AgentRuntimeOriginalCustody> OriginalRuntimeOperations
    { get { lock (_originalOperationGate) return _originalOperations.ToArray(); } }

    private Task<T> RunOriginalOperation<T>(Func<AgentRuntimeOriginalCustody, Task<T>> body)
    {
        AgentRuntimeOriginalCustody original;
        lock (_originalOperationGate)
        {
            _originalOperations.RemoveAll(static prior => prior.Healthy);
            if (_originalOperations.Count >= 128)
                throw new InvalidOperationException("Actual failed or unknown Agent source operations require owning-service inspection.");
            original = new();
            _originalOperations.Add(original);
        }
        return original.Start(body);
    }

    private static async Task CancelPublishedOriginalAsync(AgentCanonicalOriginal original, Task start)
    {
        await start.ConfigureAwait(false);
        try { original.Lifetime!.Cancel(); }
        catch (OperationCanceledException actualCallbackFault)
        { throw new AggregateException("The actual Agent cancellation callback faulted.", actualCallbackFault); }
    }

    private static async Task JoinOriginalCancellationAndCloseAsync(AgentCanonicalOriginal original)
    {
        Task? actualCancellation;
        CancellationTokenSource? lifetime;
        lock (original.Gate)
        {
            original.CancellationSealed = true;
            actualCancellation = original.OriginalCancellation;
            lifetime = original.Lifetime;
        }
        var causes = new List<Exception>();
        if (actualCancellation is not null)
            try { await actualCancellation.ConfigureAwait(false); }
            catch (Exception cause)
            {
                original.Retain(cause, actualCancellation);
                causes.AddRange(actualCancellation.Exception is { } actualFaults ? actualFaults.InnerExceptions : new[] { cause });
            }
        try
        {
            lock (original.Gate)
            {
                if (!original.LifetimeDisposed) { original.LifetimeDisposed = true; lifetime?.Dispose(); }
            }
        }
        catch (Exception cause) { original.Retain(cause); causes.Add(cause); }
        if (causes.Count > 0) throw new AggregateException("Actual Agent cancellation and close failed.", causes);
    }

    private async Task<AgentRun> ConsumeCanonicalOriginalAsync(
        AgentCanonicalOriginal custody, AgentRun run, CancellationToken token)
    {
        var chatOriginal = custody.Chat ?? throw new InvalidOperationException("No actual original Chat producer is retained.");
        var actual = chatOriginal.ConsumeOriginal().GetAsyncEnumerator(token);
        ChatStreamEvent? permissionRequired = null;
        try
        {
            while (true)
            {
                Task<bool>? move = null;
                try
                {
                    move = actual.MoveNextAsync().AsTask();
                    custody.ActualMoves.Add(move);
                    if (!await move.ConfigureAwait(false)) break;
                    var value = actual.Current;
                    run = WithCanonicalObservation(run, chatOriginal);
                    switch (value.Kind)
                    {
                        case ChatStreamEventKind.AssistantDelta when value.Delta is not null:
                            custody.Output.Append(value.Delta);
                            break;
                        case ChatStreamEventKind.ToolActivity when value.ToolActivity is not null:
                            custody.Activities.Add(value.ToolActivity);
                            run = run with
                            {
                                Result = custody.Output.ToString(),
                                ActivityJson = CaptureOriginalActivity(custody, run.CanonicalTask, completed: false),
                                ProgressPercent = Math.Min(90, 10 + custody.Activities.Count * 10)
                            };
                            await PersistAsync(run, CancellationToken.None).ConfigureAwait(false);
                            break;
                        case ChatStreamEventKind.PermissionRequired:
                            if (permissionRequired is not null)
                                throw new InvalidOperationException("The actual Agent produced multiple permission requests.");
                            permissionRequired = value;
                            break;
                        case ChatStreamEventKind.PreflightFailed:
                            throw new InvalidOperationException(string.Join("; ", value.PreflightResult?.Missing
                                .Select(item => item.Reason).Where(reason => !string.IsNullOrWhiteSpace(reason))
                                ?? new[] { "The selected model cannot satisfy this Agent task." }));
                    }
                }
                catch (Exception bodyFailure) { custody.Retain(bodyFailure, move); break; }
            }
        }
        finally
        {
            Task? dispose = null;
            try
            {
                dispose = actual.DisposeAsync().AsTask();
                custody.ActualDisposals.Add(dispose);
                await dispose.ConfigureAwait(false);
            }
            catch (Exception cleanupFailure) { custody.Retain(cleanupFailure, dispose); }
        }
        run = WithCanonicalObservation(run, chatOriginal);
        if (custody.Causes.Count > 0)
            return ProjectFailedOriginal(custody, run, token.IsCancellationRequested);
        if (permissionRequired is { PermissionRequest: { } request, CanonicalTaskContext: { } observed })
        {
            var owned = chatOriginal.AcknowledgedObservation;
            if (owned is null || owned.State != TaskExecutionLifecycle.Suspended
                || run.CanonicalTask is not { } canonical || observed.TaskId != canonical.TaskId
                || observed.ContextId != canonical.ContextId || observed.ExecutionId != canonical.ExecutionId
                || observed.PersistenceRevision != canonical.PersistenceRevision
                || request.ExecutionId != canonical.ExecutionId
                || !chatOriginal.Original.CanReturnPublishedPermissionRefusal(owned))
                throw new InvalidOperationException("No SAME acknowledged suspended Agent permission producer exists.");
            return run with
            {
                Status = AgentRunStatus.Suspended,
                Result = custody.Output.ToString().Trim(),
                Error = "Permission response required. The SAME Task/Run remains suspended; approval does not resume it.",
                ActivityJson = CaptureOriginalActivity(custody, canonical, completed: false),
                CompletedAt = null,
                ProgressPercent = Math.Min(90, run.ProgressPercent)
            };
        }
        if (!custody.HasSuccessfulStreamComplete)
            throw new InvalidOperationException("The original canonical task has no acknowledged completion and full iterator drain.");
        return run with
        {
            Status = AgentRunStatus.Completed,
            Result = custody.Output.ToString().Trim(),
            Error = string.Empty,
            ActivityJson = CaptureOriginalActivity(custody, run.CanonicalTask, completed: true),
            CompletedAt = DateTimeOffset.UtcNow,
            ProgressPercent = 100
        };
    }

    private static AgentRun WithCanonicalObservation(AgentRun run, ChatOriginalAgentInvocation original) =>
        run with { CanonicalTask = original.BindingObservation() ?? run.CanonicalTask };

    private static string CaptureOriginalActivity(
        AgentCanonicalOriginal original, AgentRunCanonicalBinding? canonical, bool completed) =>
        JsonSerializer.Serialize(AgentActivityObservation.Capture(original.Expected.Id, original.Activities,
            completed && original.HasSuccessfulStreamComplete) with { CanonicalTask = canonical, CanonicalBindingVersion = canonical is null ? 0 : 1 });

    private static AgentRun ProjectFailedOriginal(AgentCanonicalOriginal original, AgentRun run, bool cancellationRequested)
    {
        if (original.Chat is { } actualChat) run = WithCanonicalObservation(run, actualChat);
        var causes = original.Causes;
        var genuinelyCanceled = cancellationRequested && original.ActualMoves.Any(actual => actual.IsCanceled)
            && causes.All(cause => cause is OperationCanceledException);
        return run with
        {
            Status = run.CanonicalTask?.State == TaskExecutionLifecycle.Suspended ? AgentRunStatus.Suspended
                : genuinelyCanceled ? AgentRunStatus.Cancelled : AgentRunStatus.Failed,
            Result = original.Output.ToString().Trim(),
            Error = string.Join("; ", causes.Select(cause => SensitiveTextRedactor.Redact(cause.Message, 512))),
            ActivityJson = CaptureOriginalActivity(original, run.CanonicalTask, completed: false),
            CompletedAt = run.CanonicalTask?.State == TaskExecutionLifecycle.Suspended ? null : DateTimeOffset.UtcNow,
            ProgressPercent = Math.Min(90, run.ProgressPercent)
        };
    }

    private void RecordCompletedOriginal(AgentCanonicalOriginal original, AgentRun acknowledgedRun)
    {
        original.Expected = acknowledgedRun;
        if (acknowledgedRun.Status != AgentRunStatus.Completed || !original.HasSuccessfulComplete) return;
        var observation = AgentActivityObservation.Capture(acknowledgedRun.Id, original.Activities, true)
            with { CanonicalTask = acknowledgedRun.CanonicalTask, CanonicalBindingVersion = 1 };
        if (observation.ObservationComplete)
        {
            _recordedObservations[acknowledgedRun.Id] = (acknowledgedRun, observation, original);
            foreach (var older in _recordedObservations.OrderByDescending(item => item.Value.Expected.CompletedAt).Skip(256))
                _recordedObservations.TryRemove(older.Key, out _);
        }
        _canonicalRuns.TryRemove(new KeyValuePair<Guid, AgentCanonicalOriginal>(acknowledgedRun.Id, original));
    }
}
