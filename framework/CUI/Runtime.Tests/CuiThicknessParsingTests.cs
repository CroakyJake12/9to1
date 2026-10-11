using Avalonia;
using Avalonia.Controls;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiThicknessParsingTests
{
    [Theory]
    [InlineData("4", 4, 4, 4, 4)]
    [InlineData("4 8", 4, 8, 4, 8)]
    [InlineData("4,8", 4, 8, 4, 8)]
    [InlineData("1 2 3 4", 1, 2, 3, 4)]
    [InlineData("1,2,3,4", 1, 2, 3, 4)]
    [InlineData(" 1,  2 3,4 ", 1, 2, 3, 4)]
    public void Space_and_comma_separated_insets_reach_the_actual_control(string inset, double left, double top, double right, double bottom)
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup($"<Cui><Container Type=\"Overlay\" margin=\"{inset}\" padding=\"{inset}\" /></Cui>");
        Assert.Empty(diagnostics);
        var border = Assert.IsType<CuiContainerControl>(root);
        Assert.Equal(new Thickness(left, top, right, bottom), border.Margin);
        Assert.Equal(border.Margin, border.Padding);
    }
}
