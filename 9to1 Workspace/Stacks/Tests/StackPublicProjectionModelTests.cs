using HavenOS.Apps.Stacks;
using Xunit;

namespace HavenOS.Apps.Stacks.Tests;

/// <summary>ED-STACKS-005: public projection verifies reachable object/history content excludes private resources and secrets rather than hiding
/// them only in the browser, and corruption/missing infrastructure is not ordinary source deletion.</summary>
public sealed class StackPublicProjectionModelTests
{
    private static StackResource Public(string content) => new(System.Text.Encoding.UTF8.GetBytes(content), StackVisibility.Public);
    private static StackResource Private(string content) => new(System.Text.Encoding.UTF8.GetBytes(content), StackVisibility.Private);
    private static StackResource Secret() => new(System.Text.Encoding.UTF8.GetBytes("token=9to1:secret"), StackVisibility.Public);

    private static StackDomainRecord Domain(params (string path, StackResource? resource)[] entries)
    {
        var domain = new StackDomainRecord { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Name = "d", Kind = StackDomainKind.Branch };
        foreach (var entry in entries) domain.BaseTree[entry.path] = entry.resource;
        return domain;
    }

    [Fact]
    public void PrivateResourceAnywhereInHistoryBlocksPublication()
    {
        // A private resource inherited from an ancestor is still reachable and blocks publication.
        var ancestry = new[] { Domain(("secret.txt", Private("x"))), Domain(("readme.md", Public("hi"))) };
        var reachable = StackPublicProjectionModel.ComputeReachable(ancestry);
        Assert.False(StackPublicProjectionModel.IsSafeToPublish(reachable));
        Assert.Contains(StackPublicProjectionModel.FindViolations(reachable), v => v.Path == "secret.txt");
    }

    [Fact]
    public void AllPublicProjectionIsSafe()
    {
        var ancestry = new[] { Domain(("a.md", Public("a")), ("b.md", Public("b"))) };
        Assert.True(StackPublicProjectionModel.IsSafeToPublish(StackPublicProjectionModel.ComputeReachable(ancestry)));
    }

    [Fact]
    public void SecretMarkedContentBlocksPublicationEvenIfPublicVisibility()
    {
        var reachable = StackPublicProjectionModel.ComputeReachable([Domain(("config.txt", Secret()))]);
        Assert.False(StackPublicProjectionModel.IsSafeToPublish(reachable));
        Assert.Contains(StackPublicProjectionModel.FindViolations(reachable), v => v.Path == "config.txt");
    }

    [Fact]
    public void MissingStoreIsInfrastructureNotOrdinaryEmpty()
    {
        Assert.Equal(StackLoadFailureKind.InfrastructureMissing, StackPublicProjectionModel.ClassifyLoadFailure(fileMissing: true, manifestCorrupt: false));
        Assert.Equal(StackLoadFailureKind.Corrupt, StackPublicProjectionModel.ClassifyLoadFailure(false, manifestCorrupt: true));
        Assert.Equal(StackLoadFailureKind.OrdinaryEmpty, StackPublicProjectionModel.ClassifyLoadFailure(false, false));
    }

    [Fact]
    public void DescendantOverridesAncestorForSamePath()
    {
        var ancestry = new[] { Domain(("file.txt", Public("old"))), Domain(("file.txt", Public("new"))) };
        var reachable = StackPublicProjectionModel.ComputeReachable(ancestry);
        Assert.Equal("new", System.Text.Encoding.UTF8.GetString(reachable["file.txt"]!.Content));
    }
}
