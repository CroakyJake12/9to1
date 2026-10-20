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
}
