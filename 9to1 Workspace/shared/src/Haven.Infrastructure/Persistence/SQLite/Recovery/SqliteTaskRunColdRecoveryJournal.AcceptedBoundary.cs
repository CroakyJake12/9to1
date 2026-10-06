using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class SqliteTaskRunColdRecoveryJournal
{
    public Task ValidateOriginalAcceptedBoundaryWithinSourceAsync(ITaskRunColdJournalClaim sameClaim,
        TaskExecutionSnapshot actualExpected, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        // RequireClaim checks the SAME private issuer/entry/claim references before any
        // callback. DemandClaimRow authenticates the whole capsule AND sticky state.
        var claim = RequireClaim(sameClaim);
        return WithinOriginalSourceAsync(claim.Entry.Sources, originalSynchronousScope,
            retainOriginalTask, async () =>
            {
                await ValidateOriginalClaimAsync(claim, actualExpected, token).ConfigureAwait(false);
                var capsule = claim.Entry.Capsule;
                TaskRunColdRecoveryBoundary.DemandRestorableBoundary(capsule, actualExpected);
                if (capsule.Boundary != TaskRunColdBoundaryKind.SettledUnfinishedToolResponse)
                    throw new InvalidOperationException("The private claim is not an authenticated invoked boundary.");
                // Actual configured principal last, after all protected journal/input
                // reads. This does not revive an old attempt or authorize resources.
                await DemandActorAsync(OriginalSources(claim.Entry.Sources),
                    actualExpected.OwnerBinding!, token).ConfigureAwait(false);
            });
    }
}
