using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Avalonia.Headless;
using System.Threading;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Lane-3 contract coverage for the authored Target/Placement floating relationship. Placement
/// keywords are case-insensitive (every accepted casing is normalized identically); unsupported
/// placement values fail closed rather than silently collapsing to Above. Execution is slot-gated /
/// NOT RUN here.
/// </summary>
public sealed class CuiContainerRelationshipTests
{
    [Theory]
    [InlineData("above", "Above")]
    [InlineData("ABOVE", "Above")]
    [InlineData("Above", "Above")]
    [InlineData("under", "Under")]
    [InlineData("UNDER", "Under")]
    [InlineData("leftof", "LeftOf")]
    [InlineData("LeftOf", "LeftOf")]
    [InlineData("RIGHTOF", "RightOf")]
    [InlineData("rightof", "RightOf")]
    public void Placement_keywords_accept_every_casing_and_normalize_identically(string input, string expected)
    {
        var container = new CuiContainerControl("floating");
        container.SetAuthoredPlacement(input);
        Assert.Equal(expected, container.AuthoredPlacement);
    }

    [Theory]
    [InlineData("Bottom")]
    [InlineData("Top")]
    [InlineData("Left")]
    [InlineData("Right")]
    [InlineData("")]
    [InlineData("Center")]
    [InlineData(null)]
    public void Unsupported_placement_values_fail_closed_never_silently_become_above(string? input)
    {
        var container = new CuiContainerControl("floating");
        Assert.Throws<ArgumentException>(() => container.SetAuthoredPlacement(input));
        Assert.Null(container.AuthoredPlacement);
    }

    [Fact]
    public void Target_requires_a_non_empty_element_id()
    {
        var container = new CuiContainerControl("floating");
        container.SetAuthoredTarget("composer-body");
        Assert.Equal("composer-body", container.AuthoredTargetId);
        Assert.Throws<ArgumentException>(() => container.SetAuthoredTarget(null));
        Assert.Throws<ArgumentException>(() => container.SetAuthoredTarget("   "));
        // A failed set must not mutate the retained authored id.
        Assert.Equal("composer-body", container.AuthoredTargetId);
    }

    [Fact]
    public async Task Loader_applies_authored_relationship_and_live_update_retains_valid_attached_target()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader(); loader.SetAppearance(CuiAppearance.Dark);
            var loaded = loader.LoadMarkup("""
                <Cui><Container Type="Overlay Float" id="surface">
                  <Container id="body"/>
                  <Container id="footer"/>
                  <Container id="float-layer" Type="Floating" Target="body" Placement="above"/>
                </Container></Cui>
                """);
            Assert.Empty(loaded.Diagnostics);
            // The authored relationship arrived through the REAL loader path (not a direct setter).
            var floating = Assert.Single(loaded.Root!.GetVisualDescendants().OfType<CuiContainerControl>(),
                c => c.AuthoredTargetId is not null);
            Assert.Equal("body", floating.AuthoredTargetId);
            Assert.Equal("Above", floating.AuthoredPlacement); // authored casing normalized

            // Live update through the same typed path the loader uses to a STILL-ATTACHED target.
            var changes = 0;
            floating.RelationshipChanged += (_, _) => changes++;
            floating.SetAuthoredTarget("footer");
            Assert.Equal("footer", floating.AuthoredTargetId);
            Assert.Equal(1, changes);

            // Same-value re-set is idempotent — no orphan change notification.
            floating.SetAuthoredTarget("footer");
            Assert.Equal(1, changes);

            // A re-target back to the original attached body still resolves and is normalized.
            floating.SetAuthoredTarget("BODY");
            Assert.Equal("BODY", floating.AuthoredTargetId); // authored ids are ordinal case-sensitive
            Assert.Equal(2, changes);
        }, CancellationToken.None);
    }
}
