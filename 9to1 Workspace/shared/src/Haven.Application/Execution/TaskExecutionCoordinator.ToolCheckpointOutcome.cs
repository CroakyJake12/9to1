using System.Runtime.CompilerServices;
using Haven.Core;

namespace Haven.Application;

/// <summary>Private references to an actual validated result and its owning outcome/write
/// stages. They are observations until the SAME action's actual owner close also succeeds.</summary>
internal sealed class TaskRunOriginalToolOutcomeCustody(
    TaskExecutionCoordinator issuer, ITaskRunToolActionPreparation preparation,
    TaskRunToolActionResult result, TaskRunOriginalActionAdmission claim,
    object actionOriginal, TaskRunProcessStageCustody stage, TaskExecutionSnapshot acknowledgment,
    TaskPlanNode originalNode)
{
    internal readonly TaskExecutionCoordinator Issuer = issuer;
    internal readonly ITaskRunToolActionPreparation Preparation = preparation;
    internal readonly TaskRunToolActionResult Result = result;
    internal readonly TaskRunOriginalActionAdmission Claim = claim;
    internal readonly object ActionOriginal = actionOriginal;
    internal readonly TaskRunProcessStageCustody Stage = stage;
    internal readonly TaskExecutionSnapshot Acknowledgment = acknowledgment;
    internal readonly TaskPlanNode OriginalNode = originalNode;
}

public sealed partial class TaskExecutionCoordinator
{
    // The weak key is the genuine preparation retained by its owning invocation/checkpoint.
    // There is no separate global archive of completed tool work or reconstructed receipts.
    private readonly ConditionalWeakTable<ITaskRunToolActionPreparation, TaskRunOriginalToolOutcomeCustody> _originalToolOutcomeCustody = new();

    private void BindOriginalToolOutcomeCustody(ITaskRunToolActionPreparation preparation,
        TaskRunToolActionResult result, TaskRunOriginalActionAdmission claim,
        TaskRunProcessStageCustody stage, TaskExecutionSnapshot acknowledgment, TaskPlanNode originalNode)
    {
        lock (_originalActionGate)
        {
            var action = DemandOriginalActionIdentity(claim, preparation, claim.OriginalAttempt);
            if (_originalToolOutcomeCustody.TryGetValue(preparation, out _))
                throw new InvalidOperationException("This actual preparation already has its original outcome acknowledgment.");
            _originalToolOutcomeCustody.Add(preparation,
                new(this, preparation, result, claim, action, stage, acknowledgment, originalNode));
        }
    }

    internal TaskRunOriginalToolOutcomeCustody CaptureOriginalToolOutcomeCustody(
        ITaskRunToolActionPreparation samePreparation, TaskRunToolActionResult sameResult,
        TaskExecutionSnapshot sameAcknowledgment)
    {
        lock (_originalActionGate)
        {
            if (!_originalToolOutcomeCustody.TryGetValue(samePreparation, out var original)
                || !ReferenceEquals(original.Issuer, this) || !ReferenceEquals(original.Preparation, samePreparation)
                || !ReferenceEquals(original.Result, sameResult) || !ReferenceEquals(original.Acknowledgment, sameAcknowledgment)
                || !_originalActionPreparations.TryGetValue(samePreparation, out var action)
                || !ReferenceEquals(original.ActionOriginal, action) || !ReferenceEquals(original.Claim, action.Receipt)
                || original.Stage.ActualDriver is not Task<TaskExecutionSnapshot> actual
                || !actual.IsCompletedSuccessfully || !ReferenceEquals(actual.Result, sameAcknowledgment))
                throw new InvalidOperationException("Only the SAME successful actual tool-result acknowledgment may be captured.");
            return original;
        }
    }

    internal bool HasSuccessfulOriginalToolCheckpointOutcome(TaskRunOriginalToolOutcomeCustody original)
    {
        if (original is null) return false;
        lock (_originalActionGate)
        {
            if (!ReferenceEquals(original.Issuer, this)
                || !_originalToolOutcomeCustody.TryGetValue(original.Preparation, out var issued) || !ReferenceEquals(issued, original)
                || !_originalActionPreparations.TryGetValue(original.Preparation, out var action)
                || !ReferenceEquals(original.ActionOriginal, action) || !ReferenceEquals(original.Claim, action.Receipt)
                || !ReferenceEquals(action.Attempt, original.Claim.OriginalAttempt)
                || !ReferenceEquals(action.RetirementAcknowledged, original.Acknowledgment)
                || action.Retirement is not { IsCompletedSuccessfully: true }
                || !action.Driver.IsCompletedSuccessfully || action.Sources.Any(task => !task.IsCompletedSuccessfully)
                || action.Validations.Any(task => !task.IsCompletedSuccessfully)
                || !original.Stage.HealthyClosed
                || original.Stage.ActualDriver is not Task<TaskExecutionSnapshot> actual
                || !actual.IsCompletedSuccessfully || !ReferenceEquals(actual.Result, original.Acknowledgment)) return false;
            var node = original.OriginalNode;
            var acknowledgedNode = original.Acknowledgment.Plan.SingleOrDefault(item => item.ActionId == action.ActionId);
            return acknowledgedNode is not null && SameOriginalToolCheckpointNode(node, acknowledgedNode)
                && node is { State: TaskPlanNodeState.Completed, OriginalOperationOutcome.RequestedOperationCompleted: true }
                && node.OriginalToolIntent == action.Intent
                && (node.InterruptionPolicy == TaskActionInterruptionPolicy.ReadOnlyCancellable
                    || node.Acceptance?.OwnerReceiptReference == original.Result.OwnerReceiptReference);
        }
    }

    private static bool SameOriginalToolCheckpointNode(TaskPlanNode original, TaskPlanNode current) =>
        original.ActionId == current.ActionId && original.ParentActionId == current.ParentActionId
        && original.Summary == current.Summary && original.State == current.State
        && original.InterruptionPolicy == current.InterruptionPolicy && original.PlanVersion == current.PlanVersion
        && original.SupersedesActionId == current.SupersedesActionId && original.RemediationId == current.RemediationId
        && original.Acceptance == current.Acceptance && original.OriginalToolIntent == current.OriginalToolIntent
        && original.OriginalOperationOutcome == current.OriginalOperationOutcome
        && (original.RequiredPermissionScopes ?? []).SequenceEqual(current.RequiredPermissionScopes ?? [], StringComparer.Ordinal);
}
