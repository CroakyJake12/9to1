using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiSliderTests
{
    [Fact]
    public async Task Authored_range_and_live_value_move_real_thumb_while_pointer_and_keyboard_write_back()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var model = new CuiViewModel(); model.Set("Minimum", 40d); model.Set("Maximum", 400d);
            model.Set("Value", 130d); model.Set("Allowed", true);
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup("""
                <Input Type="Slider" Minimum="{Binding Minimum}" Maximum="{Binding Maximum}"
                  Value="{Binding Value, Mode=TwoWay}" Interactive="{Binding Allowed}" Width="320" Height="40"/>
                """);
            Assert.Empty(loaded.Diagnostics); var slider = Assert.IsType<Slider>(loaded.Root); loader.WireBindings(slider);
            var window = new Window { Content = slider, Width = 400, Height = 100 };
            try
            {
                window.Show(); Pump(window);
                var track = Assert.Single(slider.GetVisualDescendants().OfType<Track>());
                void AssertPosition(double value)
                {
                    Pump(window);
                    Assert.Equal(slider.Minimum, track.Minimum); Assert.Equal(slider.Maximum, track.Maximum);
                    Assert.Equal(value, slider.Value, 6); Assert.Equal(value, track.Value, 6);
                    var expected = (value - slider.Minimum) / (slider.Maximum - slider.Minimum);
                    var thumb = Assert.IsType<Thumb>(track.Thumb);
                    // Arrangement rounds to physical pixels; verify geometry within one pixel.
                    Assert.InRange(Math.Abs(thumb.Bounds.X - expected * (track.Bounds.Width - thumb.Bounds.Width)), 0, 1);
                }
                AssertPosition(130);
                model.Set("Value", 310d); AssertPosition(310);
                model.Set("Minimum", 100d); model.Set("Maximum", 500d); AssertPosition(310);
                model.Set("Value", 100d); AssertPosition(100);
                var thumb = Assert.IsType<Thumb>(track.Thumb);
                var thumbPoint = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
                var target = track.TranslatePoint(new Point(thumb.Bounds.Width / 2 + (track.Bounds.Width - thumb.Bounds.Width) * .8, track.Bounds.Height / 2), window)!.Value;
                window.MouseMove(thumbPoint); window.MouseDown(thumbPoint, MouseButton.Left);
                window.MouseMove(target); window.MouseUp(target, MouseButton.Left); Pump(window);
                AssertPosition(420); Assert.Equal(420d, Assert.IsType<double>(model.Get("Value")), 5);
                Assert.True(slider.Focus());
                Press(window, Key.Left); AssertPosition(419); Assert.Equal(419d, model.Get("Value"));
                Press(window, Key.Home); AssertPosition(100);
                Press(window, Key.End); AssertPosition(500);
                model.Set("Allowed", false); Pump(window);
                window.MouseDown(thumbPoint, MouseButton.Left); window.MouseUp(thumbPoint, MouseButton.Left); Pump(window);
                AssertPosition(500); Assert.Equal(500d, model.Get("Value"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    private static void Pump(Window window)
    { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
    private static void Press(Window window, Key key)
    { window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null); }
}
