using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using SkiaSharp;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiMaterialTests
{
    [Fact]
    public void Material_descriptor_identity_is_distinct_from_transparent_and_other_materials()
    {
        var red = new CuiMaterialBrush(Colors.Red, 1, 8);
        var blue = new CuiMaterialBrush(Colors.Blue, 1, 8);
        Assert.False(Brushes.Transparent.Equals(red)); Assert.False(red.Equals(Brushes.Transparent));
        Assert.NotEqual(red, blue); Assert.Equal(red, new CuiMaterialBrush(Colors.Red, 1, 8));
    }

    [Fact]
    public void Materials_are_professional_only_with_exact_other_theme_and_contrast_fallbacks()
    {
        foreach (var theme in Enum.GetValues<CuiTheme>())
        foreach (var appearance in Enum.GetValues<CuiAppearance>())
        foreach (var contrast in new[] { false, true })
        foreach (var reducedMotion in new[] { false, true })
        {
            var resources = new ResourceDictionary();
            CuiThemeResourceApplier.ApplyToResources(resources, CuiSurfacePaletteCatalog.For("Home", appearance, theme),
                new CuiAccessibilitySettings { HighContrast = contrast, ReduceMotion = reducedMotion });
            foreach (var (material, fallback) in new[] { ("CuiShellMaterial", "CuiBackgroundBrush"), ("CuiWorkspaceMaterial", "CuiWorkspaceBrush"),
                         ("CuiSurfaceMaterial", "CuiPanelBrush"), ("CuiFloatingSurfaceMaterial", "CuiPanel2Brush") })
            {
                if (theme == CuiTheme.Glow && !contrast)
                {
                    if (appearance is CuiAppearance.Dark or CuiAppearance.SuperDark)
                    {
                        if (material is "CuiShellMaterial" or "CuiWorkspaceMaterial")
                            Assert.IsType<CuiGlowFieldBrush>(resources[material]);
                        else
                        {
                            var surface = Assert.IsAssignableFrom<ISolidColorBrush>(resources[material]);
                            Assert.Equal(material == "CuiSurfaceMaterial" ? 0xE0 : 0xFF, surface.Color.A);
                        }
                    }
                    else
                    {
                        var gradient = Assert.IsAssignableFrom<ILinearGradientBrush>(resources[material]);
                        Assert.True(gradient.GradientStops.Select(stop => stop.Color).Distinct().Count() > 1);
                        Assert.IsNotType<CuiMaterialBrush>(gradient);
                    }
                }
                else if (theme == CuiTheme.FrostedGlass && !contrast)
                {
                    if (material == "CuiWorkspaceMaterial") Assert.Same(resources[fallback], resources[material]);
                    else Assert.Equal(material == "CuiFloatingSurfaceMaterial" ? 30 : 32,
                        Assert.IsType<CuiMaterialBrush>(resources[material]).BlurRadius);
                }
                else if (theme != CuiTheme.Professional || contrast) Assert.Same(resources[fallback], resources[material]);
                else Assert.IsType<CuiMaterialBrush>(resources[material]);
            }
            if (theme == CuiTheme.Professional && !contrast)
            {
                Assert.Equal(0, Assert.IsType<CuiMaterialBrush>(resources["CuiWorkspaceMaterial"]).BlurRadius);
                Assert.Equal(0, Assert.IsType<CuiMaterialBrush>(resources["CuiShellMaterial"]).BlurRadius);
                Assert.Equal(8, Assert.IsType<CuiMaterialBrush>(resources["CuiFloatingSurfaceMaterial"]).BlurRadius);
                var floating = Assert.IsType<CuiMaterialBrush>(resources["CuiFloatingSurfaceMaterial"]);
                var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiTextBrush"]).Color;
                Assert.True(CuiContrast.Ratio(foreground, floating.Tint) >= 4.5);
            }
        }
    }

    [Fact]
    public async Task Real_backdrop_pixels_change_while_foreground_geometry_and_underlying_content_stay_sharp()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var behind = new Stripes(); var registry = new CuiControlRegistry(); registry.RegisterObjectRenderer("Stripes", _ => behind);
            var model = new CuiViewModel(); model.Set("Material", new CuiMaterialBrush(Colors.Black, 0, 6, 0)); model.Set("MaterialOpacity", 1d);
            using var loader = new CuiControlLoader(registry); loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup("""
                <Container Type="Overlay">
                  <Object Type="Stripes"/>
                  <Container ID="surface" Type="Overlay" Width="280" Height="160" HorizontalAlignment="Center" VerticalAlignment="Center"
                    Background="{Binding Material}" Opacity="{Binding MaterialOpacity}" CornerRadius="20">
                    <Container ID="foreground" Type="Overlay" Width="40" Height="20" Background="White" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Container>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics); Assert.NotNull(loaded.Root); loader.WireBindings(loaded.Root);
            var window = new Window { Width = 560, Height = 300, Content = loaded.Root };
            try
            {
                window.Show(); using var first = Capture(window);
                var sample = first.GetPixel(150, 100);
                Assert.InRange(sample.Red, (byte)25, (byte)230); Assert.InRange(sample.Blue, (byte)25, (byte)230);
                Assert.Equal(SKColors.Red, first.GetPixel(6, 100)); Assert.Equal(SKColors.White, first.GetPixel(280, 150));
                var bounds = behind.Bounds;
                behind.Flip = true; behind.InvalidateVisual(); using var changed = Capture(window);
                Assert.True(Math.Abs(sample.Red - changed.GetPixel(150, 100).Red) > 10);
                Assert.Equal(SKColors.Blue, changed.GetPixel(6, 100)); Assert.Equal(SKColors.White, changed.GetPixel(280, 150));
                Assert.Equal(bounds, behind.Bounds);
                window.Width = 640; window.Height = 360; using var resized = Capture(window);
                Assert.Equal(SKColors.White, resized.GetPixel(320, 180));
                Assert.InRange(resized.GetPixel(190, 130).Blue, (byte)25, (byte)230);
                var flat = Color.Parse("#FF202122"); model.Set("Material", new SolidColorBrush(flat));
                using var opaque = Capture(window);
                Assert.Equal(new SKColor(32, 33, 34), opaque.GetPixel(190, 130)); Assert.Equal(SKColors.White, opaque.GetPixel(320, 180));
                model.Set("Material", new CuiMaterialBrush(Colors.Lime, 1, 0));
                using var recoloured = Capture(window); Assert.Equal(SKColors.Lime, recoloured.GetPixel(190, 130));
                model.Set("MaterialOpacity", .5d);
                using var faded = Capture(window);
                var fadedPixel = faded.GetPixel(190, 130);
                Assert.InRange(fadedPixel.Red, (byte)125, (byte)130); Assert.InRange(fadedPixel.Green, (byte)125, (byte)130); Assert.Equal(0, fadedPixel.Blue);
                model.Set("Material", new CuiMaterialBrush(Colors.Lime, 1, 6));
                using var fadedBlur = Capture(window);
                var fadedBlurPixel = fadedBlur.GetPixel(190, 130);
                Assert.InRange(fadedBlurPixel.Green, (byte)125, (byte)130);
                Assert.InRange(fadedBlurPixel.Red, (byte)125, (byte)130); Assert.Equal(0, fadedBlurPixel.Blue);
                Assert.Single(loaded.Root.GetVisualDescendants().OfType<Control>(), control => control.Name == "foreground");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Glow_field_renders_continuously_through_nested_cui_surfaces_and_keeps_an_opaque_native_fallback(bool pageRoot)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var field = new CuiGlowFieldBrush(Color.Parse("#10151B"), Color.Parse("#506070"), Color.Parse("#465A70"));
            var fallback = new Border { Background = field };
            var registry = new CuiControlRegistry();
            registry.RegisterObjectRenderer("OrdinaryBorder", _ => fallback);
            var model = new CuiViewModel(); model.Set("Field", field); model.Set("NestedVisible", true);
            using var loader = new CuiControlLoader(registry); loader.SetBindingContext(model);
            var children = """
                  <Container id="nested-field" Type="Overlay" Background="{Binding Field}" IsVisible="{Binding NestedVisible}"
                    Width="180" Height="120" Margin="70 55 0 0" HorizontalAlignment="Left" VerticalAlignment="Top" CornerRadius="12" />
                  <Button id="nested-field-button" Background="{Binding Field}" IsVisible="{Binding NestedVisible}"
                    Width="88" Height="40" Margin="250 180 0 0" HorizontalAlignment="Left" VerticalAlignment="Top" BorderWidth="0" CornerRadius="8" />
                  <Object Type="OrdinaryBorder" Width="40" Height="30" Margin="10 10 0 0" HorizontalAlignment="Left" VerticalAlignment="Top" />
                """;
            var loaded = loader.LoadMarkup(pageRoot
                ? "<Page OwnsBackdrop=\"True\" Background=\"{Binding Field}\">" + children + "</Page>"
                : "<Container Type=\"Overlay\" Background=\"{Binding Field}\">" + children + "</Container>");
            Assert.Empty(loaded.Diagnostics); Assert.NotNull(loaded.Root); loader.WireBindings(loaded.Root);
            if (pageRoot)
            {
                var page = Assert.IsType<CuiMaterialPanel>(loaded.Root);
                Assert.True(loader.RootOwnsBackdrop);
                Assert.Equal(3, page.Children.Count); // The renderer is visual-only, outside authored child indices.
                var original = page.Children[2];
                var replacement = new Border { Width = 1, Height = 1, IsVisible = false };
                page.Children[2] = replacement;
                page.Children.Move(2, 0); page.Children.Move(0, 2);
                page.Children.RemoveAt(2); page.Children.Add(original);
                Assert.Same(original, page.Children[2]);
            }
            var window = new Window { Width = 560, Height = 300, Content = loaded.Root };
            try
            {
                window.Show();
                var nested = loaded.Root.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "nested-field");
                var button = loaded.Root.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "nested-field-button");
                foreach (var size in new[] { new Size(560, 300), new Size(720, 420), new Size(360, 620) })
                {
                    // Leave both nested surfaces visible while the ancestor resizes.
                    // Their fixed local bounds cannot hide a stale shared field cache.
                    window.Width = size.Width; window.Height = size.Height;
                    using var composite = Capture(window);
                    var cardPoint = nested.TranslatePoint(new Point(60, 40), window)!.Value;
                    var buttonPoint = button.TranslatePoint(new Point(10, 10), window)!.Value;
                    var fallbackPoint = fallback.TranslatePoint(new Point(15, 15), window)!.Value;
                    Assert.Equal(new SKColor(field.FieldTop.R, field.FieldTop.G, field.FieldTop.B), Pixel(composite, fallbackPoint));
                    model.Set("NestedVisible", false);
                    using var behind = Capture(window);
                    SamePixel(Pixel(behind, cardPoint), Pixel(composite, cardPoint));
                    SamePixel(Pixel(behind, buttonPoint), Pixel(composite, buttonPoint));
                    Assert.NotEqual(behind.GetPixel(200, 80), behind.GetPixel(200, 250));
                    if (Environment.GetEnvironmentVariable("CUI_EVIDENCE_DIRECTORY") is { Length: > 0 } evidence)
                    {
                        Directory.CreateDirectory(evidence);
                        using var data = composite.Encode(SKEncodedImageFormat.Png, 100);
                        using var output = File.Create(Path.Combine(evidence, $"glow-{(pageRoot ? "page" : "field")}-{size.Width:0}x{size.Height:0}.png"));
                        data.SaveTo(output);
                    }
                    model.Set("NestedVisible", true);
                    using var restored = Capture(window);
                    SamePixel(Pixel(behind, cardPoint), Pixel(restored, cardPoint));
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);

        static SKColor Pixel(SKBitmap bitmap, Point point) => bitmap.GetPixel((int)Math.Round(point.X), (int)Math.Round(point.Y));
        static void SamePixel(SKColor expected, SKColor actual)
        {
            Assert.InRange(Math.Abs(expected.Red - actual.Red), 0, 1);
            Assert.InRange(Math.Abs(expected.Green - actual.Green), 0, 1);
            Assert.InRange(Math.Abs(expected.Blue - actual.Blue), 0, 1);
            Assert.Equal(expected.Alpha, actual.Alpha);
        }
    }

    private static SKBitmap Capture(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        using var bytes = new MemoryStream(); frame.Save(bytes); return SKBitmap.Decode(bytes.ToArray());
    }

    private sealed class Stripes : Control
    {
        public bool Flip;
        public override void Render(DrawingContext context)
        {
            for (var x = 0; x < Bounds.Width; x += 12)
                context.FillRectangle((x / 12 + (Flip ? 1 : 0)) % 2 == 0 ? Brushes.Red : Brushes.Blue, new Rect(x, 0, 12, Bounds.Height));
        }
    }
}
