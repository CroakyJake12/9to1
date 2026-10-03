using NineToOne.Cui.AI;
using Xunit;

public sealed class InvocationComposeTests
{
    [Fact] public void SearchAndInsertPreserveStableIdentityAtCaret()
    {
        var compose = new InvocationCompose();
        compose.SetText("Ask @jo please"); compose.UpdateCaret(7);
        var first = new InvocationResource(InvocationKind.Agent, "agent-1", "John", "r1", "person");
        var second = first with { CanonicalId = "agent-2" };
        Assert.True(compose.IsMenuOpen);
        Assert.Equal("jo", compose.Query);
        Assert.Equal(2, compose.Sections([first, second]).Single(s => s.Kind == InvocationKind.Agent).Resources.Count);
        compose.Insert(second, 7);
        Assert.Equal("Ask @[John] please", compose.Text);
        Assert.Equal("agent-2", Assert.Single(compose.Resolve()).Resource.CanonicalId);
    }
    [Fact] public void EditingTokenRevokesItsInvocationButEditingOutsidePreservesIt()
    {
        var compose = new InvocationCompose();
        compose.Insert(new(InvocationKind.File, "file-1", "Report", "v2"), 0);
        compose.Replace(0, 0, "Read ");
        Assert.Equal(5, Assert.Single(compose.Resolve()).Start);
        compose.Replace(8, 1, "x");
        Assert.Empty(compose.Resolve());
    }
    [Fact] public void ComputerUseRequiresExplicitTokenAndExcludesGameAndUnknownTargets()
    {
        var compose = new InvocationCompose();
        var app = new InvocationResource(InvocationKind.App, "editor", "Editor", InteractionPath: AppInteractionPath.ComputerUseRequired, Classification: AppClassification.OrdinaryApplication);
        compose.Insert(app, 0);
        Assert.Throws<InvalidOperationException>(() => compose.Resolve());
        compose.Insert(InvocationCompose.ComputerUse, compose.Text.Length);
        Assert.NotNull(compose.Resolve().Single(t => t.Resource.Kind == InvocationKind.System).ComputerUseInvocationId);
        foreach (var classification in new[] { AppClassification.Game, AppClassification.AntiCheatProtected, AppClassification.Unknown })
            Assert.False(InvocationCompose.IsEligible(app with { Classification = classification }));
    }
    [Fact] public void EmptyComposeAndTokenDeletionHaveNoHiddenState()
    {
        var compose = new InvocationCompose(); compose.UpdateCaret(0);
        Assert.False(compose.IsMenuOpen);
        var token = compose.Insert(InvocationCompose.ComputerUse, 0);
        compose.Remove(token.TokenId);
        Assert.Empty(compose.Text); Assert.Empty(compose.Resolve());
    }
}
