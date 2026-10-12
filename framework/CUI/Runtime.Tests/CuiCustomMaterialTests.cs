using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CakeOS.Cui.Themes;
using SkiaSharp;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiCustomMaterialTests
{
    [Fact]
    public void Descriptor_copies_stops_and_compares_actual_spatial_values()
    {
        var stops = new List<CuiCustomGradientStop> { new(0, new("#FF0000", 50, 0)), new(1, new("#0000FF", 50, 12)) };
        var fill = new CuiCustomFill(CuiCustomFillKind.Gradient, new("#000000"), new(CuiCustomGradientKind.Linear, stops));
        var material = new CuiCustomMaterialBrush(fill); var equal = new CuiCustomMaterialBrush(fill);
        stops[0] = new(0, new("#00FF00"));
        Assert.Equal(material, equal); Assert.Equal(material.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(material, new CuiCustomMaterialBrush(fill));
        Assert.False(Brushes.Transparent.Equals(material));
        Assert.Equal("#FF0000", material.Fill.Gradient!.Stops[0].Colour.Hex);
    }

    [Fact]
    public async Task Gradient_and_pattern_masks_use_each_colours_original_backdrop_and_keep_foreground_sharp()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var behind = new Stripes(); var registry = new CuiControlRegistry(); registry.RegisterObjectRenderer("Stripes", _ => behind);
            var model = new CuiViewModel(); model.Set("Material", new CuiMaterialBrush(Colors.Black, 0, 0));
            using var loader = new CuiControlLoader(registry); loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup("""
                <Container Type="Overlay"><Object Type="Stripes"/>
                  <Container ID="material" Type="Overlay" Width="300" Height="160" HorizontalAlignment="Center" VerticalAlignment="Center" Background="{Binding Material}">
                    <Container Type="Overlay" Width="30" Height="14" Background="White" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Container>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics); Assert.NotNull(loaded.Root); loader.WireBindings(loaded.Root);
            var window = new Window { Width = 400, Height = 240, Content = loaded.Root };
            var baselines = new Dictionary<int, SKBitmap>();
            try
            {
                window.Show();
                foreach (var sigma in new[] { 0, 4, 12 })
                { model.Set("Material", new CuiMaterialBrush(Colors.Black, 0, sigma)); baselines.Add(sigma, Capture(window)); }
                var points = new[] { new Pixel(58, 68), new Pixel(98, 82), new Pixel(148, 170), new Pixel(218, 65), new Pixel(278, 168), new Pixel(338, 82) };
                foreach (var kind in Enum.GetValues<CuiCustomGradientKind>())
                {
                    var gradient = new CuiCustomGradient(kind, [new(0, new("#000000", 100, 0)), new(.5, new("#000000", 100, 4)), new(1, new("#000000", 100, 12))], CenterX: .5, CenterY: .5, Radius: .5);
                    model.Set("Material", new CuiCustomMaterialBrush(new(CuiCustomFillKind.Gradient, new("#000000"), gradient)));
                    using var frame = Capture(window);
                    Assert.Equal(SKColors.White, frame.GetPixel(200, 120)); Assert.Equal(SKColors.Red, frame.GetPixel(6, 80));
                    foreach (var point in points)
                    {
                        var x = (point.X - 50 + .5) / 300; var y = (point.Y - 40 + .5) / 160;
                        var t = kind switch
                        {
                            CuiCustomGradientKind.Linear => x,
                            CuiCustomGradientKind.Radial => Math.Sqrt(Math.Pow((x - .5) / .5, 2) + Math.Pow((y - .5) / .5, 2)),
                            _ => (Math.Atan2(point.Y + .5 - 120, point.X + .5 - 200) / (2 * Math.PI) + .25 + 1) % 1
                        };
                        t = Math.Clamp(t, 0, 1);
                        var lower = t < .5 ? 0 : 4; var upper = t < .5 ? 4 : 12; var weight = t < .5 ? t * 2 : (t - .5) * 2;
                        var expected = baselines[lower].GetPixel(point.X, point.Y).Red * (1 - weight) + baselines[upper].GetPixel(point.X, point.Y).Red * weight;
                        Assert.True(Math.Abs(frame.GetPixel(point.X, point.Y).Red - expected) <= 5,
                            $"{kind} at {point}: {frame.GetPixel(point.X, point.Y).Red} vs weighted original {expected:F2}");
                    }
                }
                foreach (var kind in Enum.GetValues<CuiCustomPatternKind>())
                foreach (var angle in new[] { 0d, 30d })
                {
                    var pattern = new CuiCustomPattern(kind, new("#000000", 100, 12), new("#000000", 100, 0), 16, 12, angle);
                    model.Set("Material", new CuiCustomMaterialBrush(new(CuiCustomFillKind.Pattern, new("#000000"), Pattern: pattern)));
                    using var frame = Capture(window); var checkedPoints = 0; var foregroundPoints = 0; var backgroundPoints = 0;
                    for (var px = 66; px < 335; px += 11)
                    for (var py = 60; py < 180; py += 13)
                    {
                        if (px is >=180 and <=220 && py is >=105 and <=135) continue;
                        var radians = -angle * Math.PI / 180; var dx = px + .5 - 200; var dy = py + .5 - 120;
                        var x = dx * Math.Cos(radians) - dy * Math.Sin(radians) + 150;
                        var y = dx * Math.Sin(radians) + dy * Math.Cos(radians) + 80;
                        static double Mod(double a, double b) => (a % b + b) % b;
                        var mx = Mod(x, 28); var my = Mod(y, 28);
                        bool foreground;
                        if (kind == CuiCustomPatternKind.Dots)
                        {
                            var distance = Math.Sqrt(Math.Pow(mx - 14, 2) + Math.Pow(my - 14, 2));
                            if (Math.Abs(distance - 8) < 3) continue; foreground = distance < 8;
                        }
                        else
                        {
                            if (Math.Min(Math.Min(mx, 28 - mx), Math.Abs(mx - 16)) < 3) continue;
                            if (kind == CuiCustomPatternKind.Checkerboard && Math.Min(Math.Min(my, 28 - my), Math.Abs(my - 16)) < 3) continue;
                            foreground = mx < 16 && (kind == CuiCustomPatternKind.Stripes || my < 16 && (int)Mod(Math.Floor(x / 28), 2) == (int)Mod(Math.Floor(y / 28), 2));
                        }
                        var expected = baselines[foreground ? 12 : 0].GetPixel(px, py).Red;
                        Assert.True(Math.Abs(frame.GetPixel(px, py).Red - expected) <= 3,
                            $"{kind}/{angle} at {px},{py} region {foreground}: {frame.GetPixel(px, py).Red} vs original {expected}");
                        checkedPoints++; if (foreground) foregroundPoints++; else backgroundPoints++;
                    }
                    Assert.True(checkedPoints > 20 && foregroundPoints > 0 && backgroundPoints > 0);
                    Assert.Equal(SKColors.White, frame.GetPixel(200, 120));
                }
                // Each pattern colour owns its own transparency. Transparent foreground
                // reveals the filtered original backdrop, never the other pattern colour.
                var translucentPattern = new CuiCustomFill(CuiCustomFillKind.Pattern, new("#000000"), Pattern:
                    new(CuiCustomPatternKind.Stripes, new("#FF0000", 100, 4), new("#00FF00", 50, 0), 16, 12));
                model.Set("Material", new CuiCustomMaterialBrush(translucentPattern));
                using var translucent = Capture(window);
                Assert.True(Math.Abs(translucent.GetPixel(58, 80).Red - baselines[4].GetPixel(58, 80).Red) <= 2);
                Assert.Equal(0, translucent.GetPixel(58, 80).Green);
                Assert.InRange(translucent.GetPixel(72, 80).Green, (byte)127, (byte)129);
                Assert.InRange(translucent.GetPixel(72, 80).Red, (byte)126, (byte)128);
                behind.Flip = true; behind.InvalidateVisual(); using var flipped = Capture(window);
                Assert.True(Math.Abs(flipped.GetPixel(58, 80).Red - translucent.GetPixel(58, 80).Red) > 10);
                Assert.Equal(SKColors.White, flipped.GetPixel(200, 120));
                window.Width = 480; window.Height = 280; using var resized = Capture(window);
                Assert.Equal(SKColors.White, resized.GetPixel(240, 140));
                Assert.InRange(resized.GetPixel(112, 100).Green, (byte)127, (byte)129);
            }
            finally { foreach (var bitmap in baselines.Values) bitmap.Dispose(); window.Close(); }
        }, CancellationToken.None);
    }

    private readonly record struct Pixel(int X, int Y);
    private static SKBitmap Capture(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        using var bytes = new MemoryStream(); frame.Save(bytes); return SKBitmap.Decode(bytes.ToArray());
    }
    private sealed class Stripes : Control
    {
        public bool Flip;
        public override void Render(DrawingContext context)
        { for (var x = 0; x < Bounds.Width; x += 12) context.FillRectangle((x / 12 + (Flip ? 1 : 0)) % 2 == 0 ? Brushes.Red : Brushes.Blue, new Rect(x, 0, 12, Bounds.Height)); }
    }
}
