using Haven.Core;

namespace Haven.Application;

public enum TaskRunOriginalRunControlKind { Pause = 0, Stop = 1, EndPass = 2 }
public enum TaskRunOriginalRunControlDisposition { Suspended = 0, CancelledAfterOriginalClose = 1, AlreadyCompletedByOriginal = 2, EndedAfterOriginalPause = 3 }

/// <summary>Detached command availability only. Every operation freshly validates the original owner.</summary>
public sealed record TaskRunOriginalRunControlAvailability(
    ProviderExecutionContext CanonicalTaskContext, bool CanPause, bool CanStop, bool CanResumeUnstartedOriginal)
{
    public bool CanEndPass { get; init; }
    public bool CanResumeOriginalToolCheckpoint { get; init; }
    /// <summary>A source-authenticated durable cold boundary is present. This is an
    /// observation only; cold resume must still claim the same journal entry and
    /// freshly validate current domain/actor authority.</summary>
    public bool CanResumeColdOriginal { get; init; }
}

/// <summary>Observed result of the same original command and real terminal acknowledgment.
/// This value grants no actor, candidate, effect, continuation or cleanup authority.</summary>
public sealed record TaskRunOriginalRunControlResult(
    TaskRunOriginalRunControlKind Kind, TaskRunOriginalRunControlDisposition Disposition,
    ProviderExecutionContext CanonicalTaskContext, TaskExecutionLifecycle State,
    bool RequiresInspection, bool CanResumeUnstartedOriginal)
{
    public bool CanResumeOriginalToolCheckpoint { get; init; }
}
