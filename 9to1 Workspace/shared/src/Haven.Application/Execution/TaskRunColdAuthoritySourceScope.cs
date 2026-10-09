using Haven.Core;

namespace Haven.Application;

/// <summary>Actual post-await source ancestry/custody for the configured cold authority.
/// Caller callbacks are finite source guards, never permission or capsule provenance.</summary>
public interface ITaskRunColdAuthoritySourceScope
{
    bool HasOriginalTaskActorSource(IAuthenticatedResourceActorSource sameSource);
    Task ValidateOriginalClaimWithinSourceAsync(ITaskRunColdJournalClaim sameClaim,
        TaskExecutionSnapshot actualExpected, Action<Action> invokeOriginalCaller,
        Action<Task> retainOriginal, CancellationToken token);
    ValueTask ValidateOriginalContextWithinSourceAsync(ITaskRunColdContextLease sameContext,
        TaskExecutionSnapshot actualExpected, Action<Action> invokeOriginalCaller,
        Action<Task> retainOriginal, CancellationToken token);
    ValueTask ValidateOriginalClosedContextWithinSourceAsync(ITaskRunColdContextLease sameContext,
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, Action<Action> invokeOriginalCaller,
        Action<Task> retainOriginal, CancellationToken token);
    Task ValidateOriginalAcknowledgmentWithinSourceAsync(ITaskRunColdJournalAcknowledgment sameAcknowledgment,
        Action<Action> invokeOriginalCaller, Action<Task> retainOriginal, CancellationToken token);
}
