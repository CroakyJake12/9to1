using Haven.Core;

namespace Haven.Application;

public enum TaskRunOriginalRunControlKind { Pause = 0, Stop = 1 }
public enum TaskRunOriginalRunControlDisposition { Suspended = 0, CancelledAfterOriginalClose = 1, AlreadyCompletedByOriginal = 2 }

/// <summary>Detached command availability only. Every operation freshly validates the original owner.</summary>
public sealed record TaskRunOriginalRunControlAvailability(
    ProviderExecutionContext CanonicalTaskContext, bool CanPause, bool CanStop, bool CanResumeUnstartedOriginal);

/// <summary>Observed result of the same original command and real terminal acknowledgment.
/// This value grants no actor, candidate, effect, continuation or cleanup authority.</summary>
public sealed record TaskRunOriginalRunControlResult(
    TaskRunOriginalRunControlKind Kind, TaskRunOriginalRunControlDisposition Disposition,
    ProviderExecutionContext CanonicalTaskContext, TaskExecutionLifecycle State,
    bool RequiresInspection, bool CanResumeUnstartedOriginal);
