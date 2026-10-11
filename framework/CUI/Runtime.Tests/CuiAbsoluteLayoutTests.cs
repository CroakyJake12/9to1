using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiAbsoluteLayoutTests
{
    [Theory]
    [InlineData("X=\"500\" Y=\"500\"", 300, 75)]
    [InlineData("X-px=\"20.5\" Y-px=\"15.5\"", 220.5, 134.5)]
    [InlineData("X%=\"25\" Y%=\"75\"", 100, 75)]
    [InlineData("X=\"1200\" Y=\"-1200\"", 440, 330)]
    public async Task Coordinates_project_from_centre_with_positive_y_up(string coordinates, double x, double y)
    {
        await InWindow($"<Container Type=\"Absolute\"><Container Type=\"Overlay\" ID=\"target\" Width=\"20\" Height=\"10\" {coordinates}/></Container>",
            (window, root, target) => AssertCentre(target, root, x, y));
    }

    [Fact]
    public async Task Axes_use_independent_nearest_scopes_and_follow_resize()
    {
        await InWindow("""
            <Container Type="Overlay">
              <Container Type="Absolute" Width="200" Height="100" XInherited="true">
                <Container Type="Absolute" Width="100" Height="40" YInherited="true">
                  <Container Type="Overlay" ID="target" Width="20" Height="10" X="500" Y="500"/>
                </Container>
              </Container>
            </Container>
            """, (window, root, target) =>
            {
                AssertCentre(target, root, 250, 140);
                window.Width = 600; window.Height = 500;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Equal(new Size(600, 500), root.Bounds.Size);
                AssertCentre(target, root, 350, 240);
            });
    }

    [Theory]
    [InlineData("<Container Type=\"Overlay\"><Text X=\"1\">Invalid</Text></Container>")]
    [InlineData("<Container Type=\"Absolute\"><Text X=\"1\" X-px=\"2\">Invalid</Text></Container>")]
    [InlineData("<Container Type=\"Absolute\"><Text Y=\"NaN\">Invalid</Text></Container>")]
    public void Invalid_placements_return_load_diagnostics(string markup)
    {
        using var loader = new CuiControlLoader();
        var result = loader.LoadMarkup(markup);
        Assert.Null(result.Root); Assert.NotEmpty(result.Diagnostics);
    }

    private static async Task InWindow(string markup, Action<Window, Control, Control> verify)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup(markup); Assert.Empty(loaded.Diagnostics);
            var root = Assert.IsType<CuiContainerControl>(loaded.Root);
            var target = Assert.Single(root.GetVisualDescendants().OfType<Control>(), c => c.Name == "target");
            var window = new Window { Width = 400, Height = 300, Content = root };
            try { window.Show(); window.UpdateLayout(); verify(window, root, target); }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void AssertCentre(Control target, Control root, double x, double y)
    {
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), root);
        Assert.NotNull(centre);
        Assert.Equal(x, centre.Value.X, 2); Assert.Equal(y, centre.Value.Y, 2);
    }
}
