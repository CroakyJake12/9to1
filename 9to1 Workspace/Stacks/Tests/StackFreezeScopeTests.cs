using HavenOS.Apps.Stacks;
using Xunit;

namespace HavenOS.Apps.Stacks.Tests;

/// <summary>ED-STACKS-003: Freeze is a scoped temporary exclusion over an explicit selected-descendant set with optional path narrowing; early thaw
/// requires management; expiry revalidates queued work rather than applying silently.</summary>
public sealed class StackFreezeScopeTests
{
    private static readonly Guid Target = Guid.NewGuid();
    private static readonly Guid Frozen = Guid.NewGuid();
    private static readonly Guid Sibling = Guid.NewGuid();
    private static readonly Guid NewDescendant = Guid.NewGuid();

    private static StackFreeze Freeze(StackFreezeState state = StackFreezeState.Active, string? path = null, DateTimeOffset? expiresAt = null) =>
        new(Guid.NewGuid(), Target, path, [Frozen], DateTimeOffset.UtcNow, "maintainer", "Stabilize release", expiresAt, null, state);

    [Fact]
    public void EditOnSelectedFrozenDescendantIsBlocked()
    {
        Assert.True(StackFreezeScope.IsEditBlocked(Freeze(), Frozen, "module/file.cs"));
    }

    [Fact]
    public void EditOnUnselectedSiblingIsAllowed()
    {
        Assert.False(StackFreezeScope.IsEditBlocked(Freeze(), Sibling, "module/file.cs"));
    }

    [Fact]
    public void PathScopeNarrowsTheBlock()
    {
        var scoped = Freeze(path: "module");
        Assert.True(StackFreezeScope.IsEditBlocked(scoped, Frozen, "module/inside.cs"));
        // Outside the path scope, even a selected descendant is not blocked.
        Assert.False(StackFreezeScope.IsEditBlocked(scoped, Frozen, "other/outside.cs"));
    }

    [Fact]
    public void ThawedOrExpiredFreezeBlocksNothing()
    {
        Assert.False(StackFreezeScope.IsEditBlocked(Freeze(StackFreezeState.Thawed), Frozen, "a.cs"));
        Assert.False(StackFreezeScope.IsEditBlocked(Freeze(StackFreezeState.Expired), Frozen, "a.cs"));
    }

    [Fact]
    public void NewDescendantInheritsOnlyPerSelectedPolicy()
    {
        // A new descendant under the frozen domain inherits; one under an unselected sibling does not.
        Assert.True(StackFreezeScope.InheritsOnNewDescendant(Freeze(), [Target, Frozen, NewDescendant]));
        Assert.False(StackFreezeScope.InheritsOnNewDescendant(Freeze(), [Target, Sibling, NewDescendant]));
    }

    [Fact]
    public void EarlyThawRequiresManagement()
    {
        var withExpiry = Freeze(expiresAt: DateTimeOffset.UtcNow.AddDays(1));
        Assert.True(StackFreezeScope.RequiresManagementForEarlyThaw(withExpiry, DateTimeOffset.UtcNow));
        // Past the expiry it is no longer an early thaw.
        Assert.False(StackFreezeScope.RequiresManagementForEarlyThaw(withExpiry, DateTimeOffset.UtcNow.AddDays(2)));
    }

    [Fact]
    public void ExpiryWithQueuedWorkReportsReconcileRequired()
    {
        Assert.Equal(StackFreezeExpiryDecision.ReconcileRequired, StackFreezeScope.RevalidateOnExpiry(Freeze(), hasInProgressOrQueued: true));
        Assert.Equal(StackFreezeExpiryDecision.Expire, StackFreezeScope.RevalidateOnExpiry(Freeze(), hasInProgressOrQueued: false));
    }
}
