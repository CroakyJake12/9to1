using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Exercises the ACTUAL Spaces failed-retirement decision and the canonical CUI
/// failure scene it presents. The decision keeps a clean retirement empty; a failed
/// retirement presents the SAME canonical unavailable CUI surface the rest of the shell
/// uses, with the exact retained messages surfaced as accessible user-facing text.</summary>
public sealed class SpacesBrowseAppContentSessionRetirementTests
{
    [Fact]
    public void Clean_retirement_requires_no_failure_presentation()
    {
        Assert.False(SpacesBrowseAppContentSession.RequiresFailedRetirementPresentation([]));
        Assert.True(SpacesBrowseAppContentSession.RequiresFailedRetirementPresentation(
            [new IOException("held chat close failure")]));
    }

    [Fact]
    public async Task Failed_retirement_presents_the_canonical_unavailable_CUI_with_the_exact_messages()
    {
        var first = new IOException("chat dispose failed");
        var second = new InvalidOperationException("spaces controller dispose failed");

        var scene = SpacesBrowseAppContentSession.BuildFailedRetirementScene([first, second]);

        Assert.Equal("spaces.retirement", scene.AppId);
        Assert.Equal("Spaces", scene.Title);
        Assert.Equal("Spaces", scene.Surface);

        var availability = await scene.Readiness.CheckAsync(CancellationToken.None);

        // The canonical host substitutes its own accessible availability text for a non-ready
        // scene, so this readiness handshake is what the user actually reads.
        Assert.Equal(CuiSceneAvailabilityState.Unavailable, availability.State);
        Assert.Equal("spaces-retirement-failed", availability.Code);
        Assert.NotEmpty(availability.Code);
        Assert.Contains("did not retire cleanly", availability.Message, StringComparison.Ordinal);
        Assert.Contains(first.Message, availability.Message, StringComparison.Ordinal);
        Assert.Contains(second.Message, availability.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_retirement_scene_refuses_an_empty_payload()
    {
        Assert.Throws<ArgumentException>(() =>
            SpacesBrowseAppContentSession.BuildFailedRetirementScene([]));
    }
}
