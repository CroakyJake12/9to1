using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiChoiceTests
{
    [Fact]
    public async Task Checkbox_uses_filled_app_accent_bold_vector_and_real_checked_state()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader(); loader.SetSurface("Boards", "app.boards");
            var loaded = loader.LoadMarkup("<Theme Name='Glow'><Input Type='Checkbox' Checked='False' Content='Complete'/></Theme>");
            Assert.Empty(loaded.Diagnostics);
            var check = Assert.IsType<CheckBox>(loaded.Root);
            var window = new Window { Content = check, Width = 300, Height = 90 };
            try
            {
                window.Show(); Pump(window);
                var box = check.GetVisualDescendants().OfType<Border>().Single(value => value.Name == "CuiChoiceBox");
                var tick = check.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(value => value.Name == "CuiChoiceTick");
                Assert.False(tick.IsVisible); Assert.NotNull(tick.Data); Assert.True(tick.StrokeThickness >= 2.5);
                check.Focus(); Press(window, Key.Space); Pump(window);
                Assert.True(check.IsChecked); Assert.True(tick.IsVisible);
                Assert.True(check.TryFindResource("CuiAccentPrimaryBrush", out var fill)); Assert.Same(fill, box.Background);
                Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(box.BorderBrush).Color.A);
                Assert.True(check.TryFindResource("CuiAccentPrimaryForegroundBrush", out var ink)); Assert.Same(ink, tick.Stroke);
                check.Resources["CuiAccentPrimaryBrush"] = Brushes.Green;
                check.Resources["CuiAccentPrimaryForegroundBrush"] = Brushes.Black;
                Assert.Same(Brushes.Green, box.Background); Assert.Same(Brushes.Black, tick.Stroke);
                check.IsThreeState = true; check.IsChecked = null; Pump(window);
                Assert.False(tick.IsVisible);
                Assert.True(check.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(value => value.Name == "CuiChoiceIndeterminate").IsVisible);
                check.Resources["CuiHighContrast"] = true;
                Assert.Same(check.BorderBrush, box.BorderBrush);
                Assert.True(check.IsKeyboardFocusWithin);
                CuiPropertyPresentation.Set(check, "IsEnabled", false, CuiPropertyLevel.Instance, "test disabled");
                Assert.False(check.IsEffectivelyEnabled); Assert.True(check.Opacity < 1);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    private static void Pump(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Press(Window window, Key key)
    { window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null); }
}
