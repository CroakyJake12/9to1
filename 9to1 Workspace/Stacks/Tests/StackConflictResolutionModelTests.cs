using HavenOS.Apps.Stacks;
using Xunit;

namespace HavenOS.Apps.Stacks.Tests;

/// <summary>ED-STACKS-002: Reject restores pre-proposal work; Accept without Merging retains the resolution without completing merge; Accept and Merge
/// commits both; a source update after review invalidates the stale comparison. No operation quietly discards another domain's active work.</summary>
public sealed class StackConflictResolutionModelTests
{
    private static StackResource Res(string content) => new(System.Text.Encoding.UTF8.GetBytes(content));

    private static StackConflict Conflict() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "file.cs", Res("base"), Res("local"), Res("incoming"), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public void RejectRestoresPreProposalWork()
    {
        var proposal = StackConflictResolutionModel.Propose(Conflict(), Res("resolved"), currentSourceRevision: 5);
        var (result, restored) = StackConflictResolutionModel.Reject(proposal);
        Assert.Equal(StackCommitResult.RejectedRestored, result);
        // The pre-proposal work was the local resource; it is restored exactly.
        Assert.Equal("local", System.Text.Encoding.UTF8.GetString(restored!.Content));
    }

    [Fact]
    public void AcceptWithoutMergingRetainsResolutionButLeavesMergeIncomplete()
    {
        var proposal = StackConflictResolutionModel.Propose(Conflict(), Res("resolved"), currentSourceRevision: 5);
        Assert.Equal(StackCommitResult.RetainedWithoutMerge, StackConflictResolutionModel.AcceptWithoutMerging(proposal, currentSourceRevision: 5));
    }

    [Fact]
    public void AcceptAndMergeCommitsBoth()
    {
        var proposal = StackConflictResolutionModel.Propose(Conflict(), Res("resolved"), currentSourceRevision: 5);
        Assert.Equal(StackCommitResult.Committed, StackConflictResolutionModel.AcceptAndMerge(proposal, currentSourceRevision: 5));
    }

    [Fact]
    public void PostReviewSourceChangeMarksProposalStale()
    {
        var proposal = StackConflictResolutionModel.Propose(Conflict(), Res("resolved"), currentSourceRevision: 5);
        Assert.True(StackConflictResolutionModel.IsStale(proposal, currentSourceRevision: 6));
        // A stale comparison cannot be committed without revalidation.
        Assert.Equal(StackCommitResult.StaleSource, StackConflictResolutionModel.AcceptAndMerge(proposal, currentSourceRevision: 6));
        Assert.Equal(StackCommitResult.StaleSource, StackConflictResolutionModel.AcceptWithoutMerging(proposal, currentSourceRevision: 6));
    }

    [Fact]
    public void StaleProposalCanStillBeRejectedWithoutDiscardingNewerSource()
    {
        var proposal = StackConflictResolutionModel.Propose(Conflict(), Res("resolved"), currentSourceRevision: 5);
        // Reject discards only the proposal and restores pre-proposal work, even if source advanced.
        var (result, restored) = StackConflictResolutionModel.Reject(proposal);
        Assert.Equal(StackCommitResult.RejectedRestored, result);
        Assert.NotNull(restored);
    }

    [Fact]
    public void ProposalRequiresIdentitiesAndPath()
    {
        Assert.Throws<InvalidDataException>(() => new StackResolutionProposal(Guid.Empty, Guid.NewGuid(), "p", null, null, 1, StackResolutionState.Proposed).Capture());
        Assert.Throws<InvalidDataException>(() => new StackResolutionProposal(Guid.NewGuid(), Guid.NewGuid(), " ", null, null, 1, StackResolutionState.Proposed).Capture());
    }
}
