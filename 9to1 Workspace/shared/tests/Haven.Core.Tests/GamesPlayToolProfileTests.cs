using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>APP-PLAY-001: agents use permitted game information/APIs and a no-tools match exposes no outside assistance.
/// Proves no-tools denial, permitted-tool allow, out-of-profile denial and immutable profile identity.</summary>
public sealed class GamesPlayToolProfileTests
{
    [Fact]
    public void NoToolsMatchDeniesEveryCallWithExplicitReason()
    {
        var profile = PlayToolProfile.CreateNoTools("strict", "Strict Match");
        var result = profile.Authorize("web.search");
        Assert.Equal(PlayToolDecision.Denied, result.Decision);
        Assert.False(result.IsAllowed);
        Assert.Equal("PlayNoOutsideAssistance", result.Reason);
    }

    [Fact]
    public void PermittedToolIsAllowed()
    {
        var profile = PlayToolProfile.WithTools("assisted", "Assisted Match", ["calculator", "timer"]);
        Assert.True(profile.Authorize("calculator").IsAllowed);
        Assert.True(profile.Authorize("timer").IsAllowed);
    }

    [Fact]
    public void OutOfProfileToolIsDeniedNotThrown()
    {
        var profile = PlayToolProfile.WithTools("assisted", "Assisted Match", ["calculator"]);
        var result = profile.Authorize("web.search");
        Assert.Equal(PlayToolDecision.Denied, result.Decision);
        Assert.Equal("PlayToolNotPermitted", result.Reason);
    }

    [Fact]
    public void NoToolsProfileCannotDeclarePermittedTools()
    {
        // Declaring tools on a no-tools profile contradicts 'no outside assistance'.
        Assert.Throws<InvalidOperationException>(() => new PlayToolProfile("bad", "Bad", NoTools: true, ["calculator"]).Capture());
    }

    [Fact]
    public void DuplicatePermittedToolIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => PlayToolProfile.WithTools("dup", "Dup", ["calculator", "calculator"]));
    }

    [Fact]
    public void ProfileIdentityIsStableAndValidated()
    {
        var profile = PlayToolProfile.CreateNoTools("strict", "Strict Match");
        Assert.Equal("strict", profile.ProfileID);
        Assert.True(profile.NoTools);
        Assert.Throws<InvalidDataException>(() => new PlayToolProfile(" ", "Bad", false, []).Capture());
    }

    [Fact]
    public void EmptyToolIdIsRejected()
    {
        var profile = PlayToolProfile.WithTools("assisted", "Assisted Match", ["calculator"]);
        Assert.Throws<InvalidDataException>(() => profile.Authorize(" "));
    }
}
