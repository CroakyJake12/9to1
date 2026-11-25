using HavenOS.Apps.Stacks;
using Xunit;

namespace HavenOS.Apps.Stacks.Tests;

/// <summary>ED-STACKS-004: Prune cannot target Main, requires the configured 2FA contract and enters recoverable Deleted state; permanent purge is
/// separate and blocked by retained active/release/audit dependencies; Restore reuses stable identity where backing objects survive.</summary>
public sealed class StackPrunePurgeModelTests
{
    private static StackDomainRecord Domain(StackDomainKind kind, bool deleted = false) => new() { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Name = "d", Kind = kind, IsDeleted = deleted };

    [Fact]
    public void PruneRefusesMain()
    {
        Assert.Equal(StackLifecycleBlocker.MainCannotBePruned, StackPrunePurgeModel.CanPrune(Domain(StackDomainKind.Main), false, false, false));
    }

    [Fact]
    public void PruneRequiresTwoFactorWhenConfigured()
    {
        var branch = Domain(StackDomainKind.Branch);
        Assert.Equal(StackLifecycleBlocker.TwoFactorRequired, StackPrunePurgeModel.CanPrune(branch, requiresTwoFactor: true, actorHasTwoFactor: false, hasActiveDescendants: false));
        Assert.Equal(StackLifecycleBlocker.None, StackPrunePurgeModel.CanPrune(branch, requiresTwoFactor: true, actorHasTwoFactor: true, hasActiveDescendants: false));
    }

    [Fact]
    public void PruneBlockedByActiveDescendants()
    {
        var branch = Domain(StackDomainKind.Branch);
        Assert.Equal(StackLifecycleBlocker.ActiveDescendantsRemain, StackPrunePurgeModel.CanPrune(branch, false, true, hasActiveDescendants: true));
    }

    [Fact]
    public void EnterDeletedProducesRecoverableState()
    {
        var branch = Domain(StackDomainKind.Branch);
        StackPrunePurgeModel.EnterDeleted(branch, DateTimeOffset.UtcNow);
        Assert.True(branch.IsDeleted);
        Assert.NotNull(branch.DeletedAt);
        Assert.False(branch.IsActive);
    }

    [Fact]
    public void EnterDeletedRefusesMainAndDoubleDelete()
    {
        Assert.Throws<InvalidOperationException>(() => StackPrunePurgeModel.EnterDeleted(Domain(StackDomainKind.Main), DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => StackPrunePurgeModel.EnterDeleted(Domain(StackDomainKind.Branch, deleted: true), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void PurgeBlockedByRetainedDependencies()
    {
        var deleted = Domain(StackDomainKind.Leaf, deleted: true);
        Assert.Equal(StackLifecycleBlocker.ActiveDependenciesRetained, StackPrunePurgeModel.CanPurge(deleted, hasActiveDependencies: true, hasReleaseReferences: false, hasAuditDependencies: false));
        Assert.Equal(StackLifecycleBlocker.ReleaseReferencesRetained, StackPrunePurgeModel.CanPurge(deleted, false, hasReleaseReferences: true, false));
        Assert.Equal(StackLifecycleBlocker.AuditDependenciesRetained, StackPrunePurgeModel.CanPurge(deleted, false, false, hasAuditDependencies: true));
        // Allowed only when nothing is retained.
        Assert.Equal(StackLifecycleBlocker.None, StackPrunePurgeModel.CanPurge(deleted, false, false, false));
    }

    [Fact]
    public void PurgeRequiresDeletedState()
    {
        var live = Domain(StackDomainKind.Branch);
        Assert.Equal(StackLifecycleBlocker.NotDeleted, StackPrunePurgeModel.CanPurge(live, false, false, false));
    }

    [Fact]
    public void RestoreReusesIdentityWithinRetentionButRefusedAfterExpiry()
    {
        var deleted = Domain(StackDomainKind.Leaf, deleted: true);
        Assert.Equal(StackRestoreMode.ReuseIdentity, StackPrunePurgeModel.CanRestore(deleted, retentionElapsed: false, backingObjectsSurvive: true));
        Assert.Equal(StackRestoreMode.RecreateRequired, StackPrunePurgeModel.CanRestore(deleted, retentionElapsed: false, backingObjectsSurvive: false));
        Assert.Equal(StackRestoreMode.Refused, StackPrunePurgeModel.CanRestore(deleted, retentionElapsed: true, backingObjectsSurvive: true));
    }
}
