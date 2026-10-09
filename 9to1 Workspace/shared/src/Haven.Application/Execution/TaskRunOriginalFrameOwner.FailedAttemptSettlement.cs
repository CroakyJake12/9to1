using System.Runtime.CompilerServices;

namespace Haven.Application;

/// <summary>Failure/cleanup custody conveys no provider/native effect knowledge.</summary>
public enum TaskRunOriginalFailureEffectKnowledge { Unknown = 0 }

/// <summary>Opaque snapshot of actual failed-and-settled originals. This is not a new
/// admission, recovery/no-effect grant, healthy result, or substitute retirement acknowledgment.</summary>
public sealed class TaskRunOriginalFailedAttemptSettlement
{
    internal TaskRunOriginalFailedAttemptSettlement(TaskRunOriginalFailureObservation observation,
        TaskRunAttemptAdmission admission, TaskRunFailurePersistenceAcknowledgment failureAcknowledgment,
        Task registration, Task settlement, Task leaseClose, TaskRunOriginalRetirementAcknowledgment? retirement)
    {
        OriginalObservation = observation; OriginalAdmission = admission;
        OriginalFailureAcknowledgment = failureAcknowledgment; OriginalRegistration = registration;
        OriginalSettlement = settlement; OriginalLeaseClose = leaseClose;
        OriginalRetirementAcknowledgment = retirement;
    }
    public TaskRunOriginalFailureObservation OriginalObservation { get; }
    public TaskRunAttemptAdmission OriginalAdmission { get; }
    public Task OriginalFrame => OriginalObservation.OriginalFrame;
    public Exception OriginalCause => OriginalObservation.OriginalCause;
    public TaskRunFailurePersistenceAcknowledgment OriginalFailureAcknowledgment { get; }
    public Task OriginalRegistration { get; }
    public Task OriginalSettlement { get; }
    public Task OriginalLeaseClose { get; }
    public TaskRunOriginalRetirementAcknowledgment? OriginalRetirementAcknowledgment { get; }
    public TaskRunOriginalFailureEffectKnowledge ProviderNativeEffects => TaskRunOriginalFailureEffectKnowledge.Unknown;
}

/// <summary>Lookup implemented by the SAME actual configured frame/settlement owner.
/// It starts no work. Null, failure, IDs or Suspended state establish no terminal evidence.</summary>
public interface ITaskRunOriginalFailedAttemptSettlementSource
{
    TaskRunOriginalFailedAttemptSettlement? TryGetOriginalFailedAttemptSettlement(
        TaskRunOriginalFailureObservation sameObservation, TaskRunAttemptAdmission sameAdmission);
    bool IsIssuedOriginalFailedAttemptSettlement(TaskRunOriginalFailedAttemptSettlement receipt,
        TaskRunOriginalFailureObservation sameObservation, TaskRunAttemptAdmission sameAdmission);
}

public sealed partial class TaskRunOriginalFrameOwner : ITaskRunOriginalFailedAttemptSettlementSource
{
    private readonly ConditionalWeakTable<TaskRunOriginalFailedAttemptSettlement, Observation> _originalFailedSettlements = new();

    public TaskRunOriginalFailedAttemptSettlement? TryGetOriginalFailedAttemptSettlement(
        TaskRunOriginalFailureObservation sameObservation, TaskRunAttemptAdmission sameAdmission)
    {
        lock (_sync)
        {
            if (!IsOriginalObservation(sameObservation, sameAdmission, out var actual)
                || !HasOriginalFailedSettlement(actual!)) return null;
            var attempt = actual!.Attempt;
            var receipt = new TaskRunOriginalFailedAttemptSettlement(sameObservation, sameAdmission,
                actual.AcknowledgedReceipt!, attempt.Registration!, attempt.Settlement!,
                attempt.OriginalLeaseClose!, attempt.RetirementReceipt);
            _originalFailedSettlements.Add(receipt, actual);
            return receipt;
        }
    }

    public bool IsIssuedOriginalFailedAttemptSettlement(TaskRunOriginalFailedAttemptSettlement receipt,
        TaskRunOriginalFailureObservation sameObservation, TaskRunAttemptAdmission sameAdmission)
    {
        if (receipt is null || sameObservation is null || sameAdmission is null) return false;
        lock (_sync)
        {
            return _originalFailedSettlements.TryGetValue(receipt, out var issued)
                && IsOriginalObservation(sameObservation, sameAdmission, out var actual)
                && ReferenceEquals(issued, actual) && HasOriginalFailedSettlement(issued)
                && ReferenceEquals(receipt.OriginalObservation, sameObservation)
                && ReferenceEquals(receipt.OriginalAdmission, sameAdmission)
                && ReferenceEquals(receipt.OriginalFailureAcknowledgment, issued.AcknowledgedReceipt)
                && ReferenceEquals(receipt.OriginalRegistration, issued.Attempt.Registration)
                && ReferenceEquals(receipt.OriginalSettlement, issued.Attempt.Settlement)
                && ReferenceEquals(receipt.OriginalLeaseClose, issued.Attempt.OriginalLeaseClose)
                && (receipt.OriginalRetirementAcknowledgment is null
                    || ReferenceEquals(receipt.OriginalRetirementAcknowledgment, issued.Attempt.RetirementReceipt));
        }
    }

    private static bool HasOriginalFailedSettlement(Observation actual)
    {
        var attempt = actual.Attempt;
        return actual.AcknowledgedReceipt is { } acknowledged
            && ReferenceEquals(acknowledged.OriginalAdmission, attempt.Admission)
            && ReferenceEquals(acknowledged.OriginalObservation.OriginalFrame, actual.Frame.Original)
            && ReferenceEquals(acknowledged.OriginalObservation.OriginalCause, actual.Cause)
            && attempt.Sealed && attempt.OwnedLease is not null
            && attempt.Registration is { IsCompletedSuccessfully: true }
            && attempt.Settlement is { IsCompletedSuccessfully: true }
            && attempt.OriginalLeaseClose is { IsCompletedSuccessfully: true }
            && attempt.Errors.Count == 0 && attempt.CapacityRefusal is null
            && attempt.Frames.All(frame => frame.Original is { IsCompleted: true }
                && frame.ResultObservationErrors.Count == 0
                && frame.Errors.Count == 0
                && frame.BodyErrors.All(cause => frame.AcknowledgedBodyErrors.Any(known => ReferenceEquals(known, cause))));
    }
}
