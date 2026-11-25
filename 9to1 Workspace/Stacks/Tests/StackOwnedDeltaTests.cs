using HavenOS.Apps.Stacks;
using Xunit;

namespace HavenOS.Apps.Stacks.Tests;

/// <summary>ED-STACKS-001: displayed added/removed totals represent the domain's OWNED delta against its effective parent; inherited framework
/// changes do not inflate every child's totals; a known-identity rename is one owned rename, not an unrelated delete+add.</summary>
public sealed class StackOwnedDeltaTests
{
    private static StackResource Res(string content) => new(System.Text.Encoding.UTF8.GetBytes(content));

    [Fact]
    public void InheritedAncestorChangeContributesZeroToChildOwnedTotals()
    {
        // The parent changed shared.txt (inherited), but the child made no local edit.
        var parent = new Dictionary<string, StackResource?> { ["shared.txt"] = Res("v2") };
        var domain = new Dictionary<string, StackResource?> { ["shared.txt"] = Res("v2") };
        var local = new Dictionary<string, StackMutation>(); // no owned changes

        var delta = StackOwnedDeltaCalculator.Compute(parent, domain, local);
        Assert.Equal(0, delta.Added);
        Assert.Equal(0, delta.Deleted);
        Assert.Equal(0, delta.Modified);
    }

    [Fact]
    public void LocalEditCountsOnce()
    {
        var parent = new Dictionary<string, StackResource?> { ["a.txt"] = Res("base") };
        var domain = new Dictionary<string, StackResource?> { ["a.txt"] = Res("local edit") };
        var local = new Dictionary<string, StackMutation> { ["a.txt"] = new StackMutation(StackMutationKind.Upsert, "a.txt", Res("local edit")) };

        var delta = StackOwnedDeltaCalculator.Compute(parent, domain, local);
        Assert.Equal(1, delta.Modified);
        Assert.Equal(0, delta.Added);
    }

    [Fact]
    public void LocalAddCountsAsAdded()
    {
        var parent = new Dictionary<string, StackResource?>();
        var domain = new Dictionary<string, StackResource?> { ["new.txt"] = Res("x") };
        var local = new Dictionary<string, StackMutation> { ["new.txt"] = new StackMutation(StackMutationKind.Upsert, "new.txt", Res("x")) };

        Assert.Equal(1, StackOwnedDeltaCalculator.Compute(parent, domain, local).Added);
    }

    [Fact]
    public void KnownIdentityRenameReportsRenamedNotDeleteAdd()
    {
        var parent = new Dictionary<string, StackResource?> { ["old.txt"] = Res("x") };
        var domain = new Dictionary<string, StackResource?> { ["new.txt"] = Res("x") };
        var local = new Dictionary<string, StackMutation> { ["old.txt"] = new StackMutation(StackMutationKind.Rename, "old.txt", null, "new.txt") };

        var delta = StackOwnedDeltaCalculator.Compute(parent, domain, local);
        Assert.Equal(1, delta.Renamed);
        Assert.Equal(0, delta.Added);
        Assert.Equal(0, delta.Deleted);
    }

    [Fact]
    public void LocalDeleteCountsOnce()
    {
        var parent = new Dictionary<string, StackResource?> { ["gone.txt"] = Res("x") };
        var domain = new Dictionary<string, StackResource?> { ["gone.txt"] = null };
        var local = new Dictionary<string, StackMutation> { ["gone.txt"] = new StackMutation(StackMutationKind.Delete, "gone.txt") };

        Assert.Equal(1, StackOwnedDeltaCalculator.Compute(parent, domain, local).Deleted);
    }

    [Fact]
    public void InheritedOnlyDifferenceIsIgnoredEvenWhenTreesDiffer()
    {
        // Parent has framework.txt; the child inherited it (present in effective) but made no local mutation.
        var parent = new Dictionary<string, StackResource?> { ["framework.txt"] = Res("fw") };
        var domain = new Dictionary<string, StackResource?> { ["framework.txt"] = Res("fw") };
        var local = new Dictionary<string, StackMutation>();
        Assert.Equal(StackOwnedDelta.Empty, StackOwnedDeltaCalculator.Compute(parent, domain, local));
    }
}
