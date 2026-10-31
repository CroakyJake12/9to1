using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using System.Threading;
using Avalonia.Threading;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Regression for the authored hover opt-out the owner asked for on 31 October 2026: a block surface must be able
/// to say "hover paints nothing" without throwing away the control's normal chrome. Before this capability existed
/// the only blank-hover path was Chromeless, which also removes the rest chrome, and the shared CuiHoverFeedback
/// painted a wash on every Button/TextBox frame (framework/CUI/Runtime/CuiControlTemplates.cs:99), so the hover
/// band kept reappearing on Boards block rows.
/// </summary>
public sealed class CuiHoverTintOptOutTests
{
    private const string Markup = """
        <Cui>
          <Container Type="Vertical Stack">
            <Button id="plain" Content="block" Width="200" Height="34" Background="Transparent" />
            <Button id="quiet" Content="block" Width="200" Height="34" Background="Transparent" HoverTint="None" />
          </Container>
        </Cui>
        """;

    [Fact]
    public async Task HoverTint_None_leaves_the_hover_paint_off_while_default_still_washes()
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

            var window = new Window { Content = root, Width = 320, Height = 200 };
            window.Show();
            Pump();

            var plain = SingleButton(window, "plain");
            var quiet = SingleButton(window, "quiet");
            Pump();

            // Rest: the opt-out control keeps its authored chrome untouched.
            Assert.True(IsEffectivelyTransparent(quiet.Background));
            Assert.True(IsEffectivelyTransparent(plain.Background));

            Hover(window, plain);
            Assert.True(plain.IsPointerOver);
            var plainFrame = Assert.Single(plain.GetVisualDescendants().OfType<CuiMaterialBorder>());
            Assert.NotNull(plainFrame.HoverTint);          // default behaviour is unchanged

            Hover(window, quiet);
            Assert.True(quiet.IsPointerOver);
            var quietFrame = Assert.Single(quiet.GetVisualDescendants().OfType<CuiMaterialBorder>());
            Assert.Null(quietFrame.HoverTint);             // the authored opt-out is honoured
            Assert.True(IsEffectivelyTransparent(quiet.Background));

            window.Close();
            await Task.CompletedTask;
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HoverTint_rejects_a_value_that_is_not_a_hover_mode()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);

            var (root, diagnostics) = loader.LoadMarkup("""
                <Cui>
                  <Button id="bad" Content="block" HoverTint="Sometimes" />
                </Cui>
                """);
            Assert.NotEmpty(diagnostics);
            // Must be a value diagnostic, not the old unknown-property gate: that would also make this assertion
            // pass while the vocabulary entry was missing entirely.
            Assert.Contains(diagnostics, d => d.Message.Contains("HoverTint", StringComparison.Ordinal)
                && !d.Message.Contains("Unknown or forbidden", StringComparison.Ordinal));
            await Task.CompletedTask;
            return true;
        }, CancellationToken.None);
    }

    private static Button SingleButton(Window window, string id) =>
        Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == id);

    // Copied from CuiChromelessAppearanceTests: the headless window must lay out and render once before a
    // translated point is meaningful, and MouseMove is what actually sets IsPointerOver on the target.
    private static void Hover(Window window, Button button)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()) { }
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
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
