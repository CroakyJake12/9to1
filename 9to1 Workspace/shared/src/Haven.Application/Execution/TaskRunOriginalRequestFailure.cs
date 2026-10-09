namespace Haven.Application;

/// <summary>Opaque FINAL exhausted finite-call binding. It starts no settlement and
/// conveys no cleanup completion, effect knowledge, permission or retirement acknowledgment.</summary>
public interface TaskRunOriginalFinalRequestFailure
{
    object OriginalCallerRequest { get; }
    Exception OriginalOutwardFailure { get; }
    TaskRunOriginalFailureObservation OriginalObservation { get; }
    TaskRunAttemptAdmission OriginalAdmission { get; }
}

/// <summary>One exact finite routed-request failure and its original failed-attempt custody.
/// The consumer independently retains its actual public call Task and native-effect evidence.</summary>
public interface TaskRunOriginalRequestFailure
{
    object OriginalCallerRequest { get; }
    Exception OriginalOutwardFailure { get; }
    TaskRunOriginalFailedAttemptSettlement OriginalFailedAttempt { get; }
}

/// <summary>Implemented by the SAME configured resilient router. Only an exhausted finite
/// Complete/Tools request is covered; streaming, catalogue-only and orchestration errors are unknown.</summary>
public interface ITaskRunOriginalRequestFailureSource
{
    TaskRunOriginalFinalRequestFailure? TryGetOriginalFinalRequestFailure(OllamaToolRequest sameRequest,
        Exception sameOutwardFailure);
    TaskRunOriginalFinalRequestFailure? TryGetOriginalFinalRequestFailure(OllamaChatRequest sameRequest,
        Exception sameOutwardFailure);
    bool IsIssuedOriginalFinalRequestFailure(TaskRunOriginalFinalRequestFailure binding, object sameRequest,
        Exception sameOutwardFailure);

    TaskRunOriginalRequestFailure? TryGetOriginalRequestFailure(OllamaChatRequest sameRequest,
        TaskRunAttemptAdmission sameAdmission, Exception sameOutwardFailure);
    TaskRunOriginalRequestFailure? TryGetOriginalRequestFailure(OllamaToolRequest sameRequest,
        TaskRunAttemptAdmission sameAdmission, Exception sameOutwardFailure);
    bool IsIssuedOriginalRequestFailure(TaskRunOriginalRequestFailure receipt, object sameRequest,
        TaskRunAttemptAdmission sameAdmission, Exception sameOutwardFailure);
}
