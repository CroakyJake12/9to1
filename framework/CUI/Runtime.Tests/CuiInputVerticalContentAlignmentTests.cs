using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Owning coverage for the typed input VerticalContentAlignment capability: an authored
/// VerticalContentAlignment="Center" must centre the ACTUAL rendered text (the native TextPresenter)
/// inside the field, while ordinary inputs and multiline inputs keep their existing top/fill
/// behaviour. These are real rendered-bounds assertions, not source-string checks.
/// </summary>
public sealed class CuiInputVerticalContentAlignmentTests
{
    [Fact]
    public async Task Authored_VerticalContentAlignment_centres_actual_text_while_defaults_and_multiline_stay_top()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Input Type="Text" ID="centered" Height="32" Padding="12 4" FontSize="13" VerticalContentAlignment="Center" Value="One"/>
                  <Input Type="Text" ID="plain" Height="32" Padding="12 4" FontSize="13" Value="Two"/>
                  <Input Type="Text" ID="multiline" Height="60" Padding="12 4" FontSize="13" AcceptsReturn="True" Value="alpha&#10;beta"/>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics);
            var root = Assert.IsType<CuiContainerControl>(loaded.Root);
            loader.WireBindings(root);
            var centered = Assert.IsType<TextBox>(root.AuthoredChildren[0]);
            var plain = Assert.IsType<TextBox>(root.AuthoredChildren[1]);
            var multiline = Assert.IsType<TextBox>(root.AuthoredChildren[2]);
            var window = new Window { Content = root, Width = 420, Height = 260 };
            try
            {
                window.Show(); Pump(window);

                // The centred field: the actual presenter is a compact band whose vertical centre
                // coincides with the field's centre (Height 32 => centre 16).
                Assert.True(TextHeight(centered) < TextHeight(plain), "centre text must not be stretched full-height.");
                Assert.InRange(CenterY(centered), 16 - 2.5, 16 + 2.5);

                // Ordinary and multiline inputs keep their existing top-aligned fill behaviour.
                Assert.InRange(TopY(plain), 0, 6);
                Assert.InRange(TopY(multiline), 0, 6);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static double CenterY(TextBox input)
    { var presenter = Presenter(input); return presenter.TranslatePoint(new Point(0, presenter.Bounds.Height / 2), input)!.Value.Y; }
    private static double TopY(TextBox input) => Presenter(input).TranslatePoint(default, input)!.Value.Y;
    private static double TextHeight(TextBox input) => Presenter(input).Bounds.Height;
    private static TextPresenter Presenter(TextBox input) => input.GetVisualDescendants().OfType<TextPresenter>().Single();

    private static void Pump(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
}
