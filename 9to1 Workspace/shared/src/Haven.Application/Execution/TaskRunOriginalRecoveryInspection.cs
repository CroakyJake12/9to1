using Haven.Core;

namespace Haven.Application;

/// <summary>Inspection availability only. None of these states authorizes replay or proves no effect.</summary>
public enum TaskRunOriginalRecoveryAvailability
{
    OriginalUnavailable = 0,
    LiveOriginalRetained = 1,
    OriginalTaskWriteUnacknowledged = 2
}

/// <summary>A detached observation of an actual CLR source task, not a canonical task identity or settlement receipt.</summary>
public sealed record TaskRunOriginalWorkObservation(string Stage, int SourceTaskId, TaskStatus Status);

internal sealed record TaskRunOriginalSource(string Stage, Task Actual, TaskStatus ObservedStatus);
internal sealed record TaskRunOriginalRecoveryCapture(TaskRunOriginalSource[] Sources, Exception[] Causes);

/// <summary>Read-only current-owner inspection. Raw originals remain private; this object is never a replay grant.</summary>
public sealed class TaskRunOriginalRecoveryInspection
{
    internal readonly TaskExecutionCoordinator Issuer;
    internal readonly TaskRunInvocationCustody? OriginalCustody;
    internal readonly TaskExecutionSnapshot ActualSnapshot;
    internal readonly IReadOnlyList<TaskRunOriginalSource> ActualSources;
    internal readonly IReadOnlyList<Exception> ActualCauses;

    internal TaskRunOriginalRecoveryInspection(
        TaskExecutionCoordinator issuer, TaskExecutionSnapshot snapshot, TaskRunInvocationCustody? original,
        DateTimeOffset observedAt, TaskRunOriginalRecoveryCapture capture, IReadOnlyList<ExecutionFailure> safeCauses)
    {
        Issuer = issuer;
        OriginalCustody = original;
        ActualSnapshot = snapshot;
        Snapshot = DetachObservation(snapshot);
        ObservedAt = observedAt;
        OriginalObservationId = original?.ObservationId;
        Availability = original is null ? TaskRunOriginalRecoveryAvailability.OriginalUnavailable
            : original.OriginalBinding is null ? TaskRunOriginalRecoveryAvailability.OriginalTaskWriteUnacknowledged
            : TaskRunOriginalRecoveryAvailability.LiveOriginalRetained;
        ActualSources = Array.AsReadOnly(capture.Sources);
        ActualCauses = Array.AsReadOnly(capture.Causes);
        OriginalWork = Array.AsReadOnly(ActualSources.Select(source =>
            new TaskRunOriginalWorkObservation(source.Stage, source.Actual.Id, source.ObservedStatus)).ToArray());
        Causes = Array.AsReadOnly(safeCauses.ToArray());
    }

    public TaskExecutionSnapshot Snapshot { get; }
    public DateTimeOffset ObservedAt { get; }
    public Guid? OriginalObservationId { get; }
    public TaskRunOriginalRecoveryAvailability Availability { get; }
    public IReadOnlyList<TaskRunOriginalWorkObservation> OriginalWork { get; }
    public IReadOnlyList<ExecutionFailure> Causes { get; }

    private static TaskExecutionSnapshot DetachObservation(TaskExecutionSnapshot value) => value with
    {
        Plan = Array.AsReadOnly(value.Plan.Select(node => node with
        {
            RequiredPermissionScopes = node.RequiredPermissionScopes is { } scopes ? Array.AsReadOnly(scopes.ToArray()) : null
        }).ToArray()),
        Steers = Array.AsReadOnly(value.Steers.Select(steer => steer with
        {
            AffectedActionIds = Array.AsReadOnly(steer.AffectedActionIds.ToArray()),
            RequiredPermissionScopes = steer.RequiredPermissionScopes is { } scopes ? Array.AsReadOnly(scopes.ToArray()) : null
        }).ToArray()),
        Queue = Array.AsReadOnly(value.Queue.ToArray()),
        ApprovedPermissionScopes = Array.AsReadOnly(value.ApprovedPermissionScopes.ToArray()),
        Attempts = Array.AsReadOnly(value.Attempts.Select(attempt => attempt with
        {
            Candidate = attempt.Candidate with { RequiredCapabilities = Array.AsReadOnly(attempt.Candidate.RequiredCapabilities.ToArray()) }
        }).ToArray()),
        RecoveryObservation = value.RecoveryObservation is { } recovery
            ? recovery with { Causes = Array.AsReadOnly(recovery.Causes.ToArray()) } : null
    };
}

public sealed partial class TaskExecutionCoordinator
{
    /// <summary>Inspects the same current task/run under fresh command authority. Missing historical custody stays unavailable.</summary>
    public async Task<TaskRunOriginalRecoveryInspection> InspectOriginalRecoveryAsync(
        Guid taskId, Guid expectedExecutionId, CancellationToken cancellationToken)
    {
        if (taskId == Guid.Empty || expectedExecutionId == Guid.Empty)
            throw new ArgumentException("Exact canonical task and execution identities are required.");
        var initial = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        RequireRun(initial, expectedExecutionId);
        if (initial.OwnerBinding is null)
            throw new InvalidOperationException("An original recovery inspection requires the actual canonical owner binding.");
        ValidateOwner(initial, initial.OwnerBinding);
        await ValidateTaskCommandAsync(initial, "task.inspect-original-recovery", cancellationToken).ConfigureAwait(false);

        // An awaited actor/policy check cannot freeze repository state. Benign queue changes
        // may advance the revision; replacement owner/run/recovery observations are refused.
        var current = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        RequireRun(current, expectedExecutionId);
        if (current.ContextId != initial.ContextId || current.CreatedAt != initial.CreatedAt
            || current.OwnerBinding != initial.OwnerBinding
            || current.RecoveryObservation?.ObservationId != initial.RecoveryObservation?.ObservationId)
            throw new InvalidOperationException("The original recovery owner or observation changed during inspection.");
        ValidateOwner(current, current.OwnerBinding);

        TaskRunInvocationCustody? original = null;
        if (_originalInvocations.TryGetValue(taskId, out var retained))
        {
            var binding = retained.OriginalBinding ?? retained.ProposedBinding;
            if (ReferenceEquals(retained.Issuer, this) && ReferenceEquals(retained.OriginalSelf, retained)
                && binding is not null && binding.TaskId == current.TaskId && binding.ContextId == current.ContextId
                && binding.ExecutionId == current.ExecutionId && binding.CreatedAt == current.CreatedAt
                && binding.OwnerBinding == current.OwnerBinding)
                original = retained;
        }

        // This final actual actor check precedes disclosure. No task/body/settlement is joined,
        // no persistence is performed and neither durable text nor this projection grants replay.
        await ValidateTaskCommandAsync(current, "task.inspect-original-recovery", cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var final = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        RequireRun(final, expectedExecutionId);
        if (final.ContextId != current.ContextId || final.CreatedAt != current.CreatedAt || final.OwnerBinding != current.OwnerBinding
            || final.RecoveryObservation?.ObservationId != current.RecoveryObservation?.ObservationId)
            throw new InvalidOperationException("The original recovery binding changed before inspection disclosure.");
        if (original is not null && (!_originalInvocations.TryGetValue(taskId, out var sameOriginal)
            || !ReferenceEquals(sameOriginal, original)))
        {
            original = null;
        }
        // The final repository await may outlive an authentication revision. Ask the
        // genuine command authority again after it, before any private disclosure.
        await ValidateTaskCommandAsync(final, "task.inspect-original-recovery", cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (original is not null && (!_originalInvocations.TryGetValue(taskId, out var finalOriginal)
            || !ReferenceEquals(finalOriginal, original))) original = null;
        // Capture once after authorization. Both public diagnostics/statuses and private
        // original references come from this SAME finite observation, never two live reads.
        var capture = original?.CaptureRecoveryOriginals() ?? new TaskRunOriginalRecoveryCapture([], []);
        var safeCauses = capture.Causes.SelectMany(OriginalDiagnosticCauses).Select(cause => new ExecutionFailure(
            "ORIGINAL_INVOCATION_CAUSE", SensitiveTextRedactor.Redact(cause.GetType().Name, 128),
            SensitiveTextRedactor.Redact(cause.Message, 2_000), AffectedComponent: "task-recovery-inspection")).ToArray();
        return new TaskRunOriginalRecoveryInspection(this, final, original, _time.GetUtcNow(), capture, safeCauses);
    }
}
