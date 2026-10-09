namespace Haven.Core;

public enum TaskRunIteratorTerminalOutcome { ReachedEnd = 0, Failed = 1, ClosedBeforeEnd = 2 }
public enum TaskRunOriginalSettlementOutcome { NoAttemptAdmissionWasInvoked = 0, Pending = 1, Joined = 2, Failed = 3, OriginalUnavailable = 4, JoinedByOriginalCompletion = 5 }

/// <summary>Honest recovery projection of the original call. It is neither a replay grant nor a runtime-absence witness.</summary>
public sealed record TaskRunRecoveryObservation(
    Guid ObservationId, string ReasonCode, DateTimeOffset ObservedAt,
    TaskRunIteratorTerminalOutcome IteratorOutcome, TaskRunOriginalSettlementOutcome SettlementOutcome,
    IReadOnlyList<ExecutionFailure> Causes);
