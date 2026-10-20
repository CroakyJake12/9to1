using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using System.Threading;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Focused regression for the generic CUI chromeless/icon appearance: a chromeless button retains
/// its transparent closed rest background across hover and press (no interaction paint) while a
/// normal button still paints its hover/pressed state. The real Selected background and keyboard
/// focus are unaffected (covered by the host-level picker test).
/// </summary>
public sealed class CuiChromelessAppearanceTests
{
    private const string Markup = """
        <Cui>
          <Container Type="Vertical Stack">
            <Button id="chromeless" Content="+" Width="44" Height="44" Background="Transparent" Chromeless="true" />
            <Button id="normal" Content="+" Width="44" Height="44" Background="Transparent" />
          </Container>
        </Cui>
        """;

    [Fact]
    public async Task Chromeless_button_keeps_transparent_background_on_hover_and_press()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root!);
            loader.WireBindings(surface);

            var window = new Window { Content = root, Width = 300, Height = 200 };
            window.Show(); Pump();
            try
            {
                var chromeless = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "chromeless");
                var normal = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "normal");

                // Provide concrete hover/pressed brushes so this focused test is independent of the
                // surrounding theme dictionary; the presentation subscribes to these resources.
                foreach (var button in new[] { chromeless, normal })
                {
                    button.Resources["CuiButtonHoverBrush"] = Brushes.Red;
                    button.Resources["CuiButtonPressedBrush"] = Brushes.Blue;
                    button.Resources["CuiButtonHoverForegroundBrush"] = Brushes.Black;
                    button.Resources["CuiButtonPressedForegroundBrush"] = Brushes.Black;
                }
                Pump();

                // Rest: both are authored transparent.
                Assert.True(IsEffectivelyTransparent(chromeless.Background));

                // Hover: the chromeless button stays transparent; the normal button paints hover.
                Hover(window, chromeless);
                Assert.True(chromeless.IsPointerOver);
                Assert.True(IsEffectivelyTransparent(chromeless.Background));

                Hover(window, normal);
                Assert.True(normal.IsPointerOver);
                Assert.False(IsEffectivelyTransparent(normal.Background));

                // Press: chromeless stays transparent while pressed. Each press is completed with a
                // matching release before moving to the next button so pointer capture never leaks
                // across controls in the headless session.
                PressReleasing(window, chromeless);
                Assert.True(IsEffectivelyTransparent(chromeless.Background));

                PressReleasing(window, normal);
                Assert.False(IsEffectivelyTransparent(normal.Background));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private static void Hover(Window window, Button button)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()) { }
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        Pump();
    }

    private static void PressReleasing(Window window, Button button)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()) { }
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        Pump();
        // Confirm the button actually entered its pressed state before we assert on its paint.
        Assert.True(button.IsPressed);
        window.MouseUp(point, MouseButton.Left);
        Pump();
    }

    private static bool IsEffectivelyTransparent(IBrush? brush)
    {
        if (brush is null) return true;
        if (brush.Opacity <= 0d) return true;
        return brush is ISolidColorBrush solid && solid.Color.A == 0;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
