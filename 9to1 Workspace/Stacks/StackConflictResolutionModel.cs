namespace HavenOS.Apps.Stacks;

/// <summary>The lifecycle of a reviewable conflict resolution proposal. Each terminal choice is distinct and reversible from Proposed.</summary>
public enum StackResolutionState { Proposed, Rejected, AcceptedWithoutMerging, AcceptedAndMerged }

/// <summary>
/// A reviewable conflict resolution (ED-STACKS-002). It captures the exact pre-proposal work snapshot so Reject restores it precisely, records the
/// chosen resolution resource, and tracks whether the merge was completed. The reviewed source revision is pinned so a later source change marks the
/// proposal stale and prevents committing an out-of-date comparison without revalidation. Pure model — no engine/store mutation.
/// </summary>
public sealed record StackResolutionProposal(
    Guid ProposalId,
    Guid ConflictId,
    string Path,
    StackResource? PreProposalWork,
    StackResource? ChosenResolution,
    long ReviewedSourceRevision,
    StackResolutionState State)
{
    public StackResolutionProposal Capture()
    {
        if (ProposalId == Guid.Empty || ConflictId == Guid.Empty) throw new InvalidDataException("A resolution proposal requires proposal and conflict identities.");
        if (string.IsNullOrWhiteSpace(Path)) throw new InvalidDataException("A resolution proposal requires a path.");
        if (!Enum.IsDefined(State)) throw new InvalidDataException("The resolution state is unsupported.");
        return this;
    }
}

/// <summary>Outcome of attempting to commit a proposal. StaleSource refuses to commit an out-of-date comparison without revalidation.</summary>
public enum StackCommitResult { Committed, RetainedWithoutMerge, RejectedRestored, StaleSource }

/// <summary>Pure conflict-resolution semantics: propose/reject/accept-without-merge/accept-and-merge with post-review staleness invalidation.
/// No operation discards or retargets another domain's active work — Reject restores exactly the captured pre-proposal snapshot.</summary>
public static class StackConflictResolutionModel
{
    /// <summary>Create a reviewable proposal, capturing the current (pre-proposal) work so Reject can restore it exactly.</summary>
    public static StackResolutionProposal Propose(StackConflict conflict, StackResource? chosenResolution, long currentSourceRevision)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        return new StackResolutionProposal(Guid.NewGuid(), conflict.Id, conflict.Path, conflict.Local?.Copy(), chosenResolution?.Copy(), currentSourceRevision, StackResolutionState.Proposed).Capture();
    }

    /// <summary>Whether the reviewed comparison is stale because the underlying source advanced after review.</summary>
    public static bool IsStale(StackResolutionProposal proposal, long currentSourceRevision)
        => proposal.ReviewedSourceRevision != currentSourceRevision;

    /// <summary>Reject restores the pre-proposal work. A stale proposal can still be rejected (it discards only the proposal, never newer source).</summary>
    public static (StackCommitResult Result, StackResource? RestoredWork) Reject(StackResolutionProposal proposal)
    {
        var captured = proposal.Capture();
        return (StackCommitResult.RejectedRestored, captured.PreProposalWork?.Copy());
    }

    /// <summary>Accept without Merging retains the resolution but leaves the merge incomplete. Refuses a stale comparison.</summary>
    public static StackCommitResult AcceptWithoutMerging(StackResolutionProposal proposal, long currentSourceRevision)
    {
        var captured = proposal.Capture();
        if (IsStale(captured, currentSourceRevision)) return StackCommitResult.StaleSource;
        return StackCommitResult.RetainedWithoutMerge;
    }

    /// <summary>Accept and Merge commits the reviewed resolution AND completes the merge. Refuses a stale comparison.</summary>
    public static StackCommitResult AcceptAndMerge(StackResolutionProposal proposal, long currentSourceRevision)
    {
        var captured = proposal.Capture();
        if (IsStale(captured, currentSourceRevision)) return StackCommitResult.StaleSource;
        return StackCommitResult.Committed;
    }
}
