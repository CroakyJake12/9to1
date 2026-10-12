using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using SkiaSharp;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiPresetSurfaceTests
{
    private static readonly CuiTheme[] Presets = [CuiTheme.FrostedGlass, CuiTheme.Metallic, CuiTheme.Western];

    [Fact]
    public void New_presets_are_distinct_in_every_appearance_and_keep_app_accent_choices()
    {
        foreach (var appearance in Enum.GetValues<CuiAppearance>())
        {
            var palettes = Presets.Select(theme => CuiSurfacePaletteCatalog.For("Present", appearance, theme, null)).ToArray();
            Assert.Equal(3, palettes.Select(palette => palette.TideBase).Distinct().Count());
            Assert.Equal(3, palettes.Select(palette => palette.Panel).Distinct().Count());
            Assert.DoesNotContain(CuiSurfacePaletteCatalog.For("Present", appearance, CuiTheme.Glow, null).Panel,
                palettes.Select(palette => palette.Panel));
            foreach (var theme in Presets)
            foreach (var app in CuiAppAccentCatalog.Defaults)
            {
                var palette = CuiSurfacePaletteCatalog.For(app.Surface, appearance, theme, null);
                Assert.Equal(CuiSurfacePaletteCatalog.For(app.Surface, appearance, CuiTheme.Glow, null).Accent, palette.Accent);
                var selected = CuiSurfacePaletteCatalog.For(app.Surface, appearance, theme, "#247BAA");
                // Dark appearances already lift the chosen hue through the
                // shared accent derivation; a preset must preserve that policy.
                Assert.Equal(CuiSurfacePaletteCatalog.For(app.Surface, appearance, CuiTheme.Glow, "#247BAA").Accent, selected.Accent);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Present", appearance, theme, "#247BAA").AccentPalette, selected.AccentPalette);
            }
        }
        foreach (var theme in Presets)
            Assert.Equal(4, Enum.GetValues<CuiAppearance>().Select(appearance =>
                CuiSurfacePaletteCatalog.For("Present", appearance, theme, null).TideBase).Distinct().Count());
    }

    [Fact]
    public void Preset_text_and_control_states_remain_readable_over_real_composited_colours()
    {
        foreach (var theme in Presets)
        foreach (var appearance in Enum.GetValues<CuiAppearance>())
        foreach (var app in CuiAppAccentCatalog.Defaults)
        foreach (var accent in new string?[] { null, "#FFFFFF", "#000000", "#00FF00", "#0000FF" })
        {
            var palette = CuiSurfacePaletteCatalog.For(app.Surface, appearance, theme, accent);
            var resources = new ResourceDictionary(); CuiThemeResourceApplier.ApplyToResources(resources, palette);
            var panel = Composite(palette.Panel, palette.TideBase);
            AssertContrast(palette.Text, panel, 7, $"{theme}/{appearance} text");
            AssertContrast(palette.Muted, panel, 4.5, $"{theme}/{appearance} muted");
            AssertContrast(palette.Focus, panel, 3, $"{theme}/{appearance} focus");
            foreach (var state in new[] { "", "Hover", "Pressed" })
            {
                var ink = Assert.IsAssignableFrom<ISolidColorBrush>(resources[$"CuiButton{state}ForegroundBrush"]).Color;
                Assert.Same(resources[$"CuiButton{state}ForegroundBrush"], resources[$"HavenButton{state}ForegroundBrush"]);
                var brush = Assert.IsAssignableFrom<IGradientBrush>(resources[$"CuiButton{state}Brush"]);
                foreach (var stop in brush.GradientStops)
                    AssertContrast(ink, Composite(stop.Color, panel), 4.5, $"{theme}/{appearance}/{app.Surface}/{accent}/{state}@{stop.Offset}");
            }
        }
    }

    [Fact]
    public void High_contrast_replaces_every_preset_gradient_in_reused_resources()
    {
        var resources = new ResourceDictionary();
        foreach (var theme in Presets)
        foreach (var appearance in Enum.GetValues<CuiAppearance>())
        {
            var palette = CuiSurfacePaletteCatalog.For("Present", appearance, theme, null);
            CuiThemeResourceApplier.ApplyToResources(resources, palette);
            Assert.IsAssignableFrom<IGradientBrush>(resources["CuiButtonBrush"]);
            CuiThemeResourceApplier.ApplyToResources(resources, palette, new CuiAccessibilitySettings { HighContrast = true });
            foreach (var key in new[] { "CuiWorkspaceBrush", "CuiPanel2Brush", "CuiButtonBrush", "CuiButtonHoverBrush", "CuiButtonPressedBrush", "CuiPopupSurfaceBrush" })
            {
                var brush = Assert.IsAssignableFrom<ISolidColorBrush>(resources[key]);
                Assert.Equal(255, brush.Color.A);
            }
            var text = Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiTextBrush"]).Color;
            var panel = Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiPanelBrush"]).Color;
            AssertContrast(text, panel, 7, "High contrast text");
            Assert.Same(resources["CuiPanelBrush"], resources["CuiSurfaceMaterial"]);
        }
    }

    [Fact]
    public async Task Real_CUI_preset_controls_render_all_appearances_and_preserve_editing_and_actions()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var output = Environment.GetEnvironmentVariable("CUI_PRESET_SCREENSHOT_DIR");
            if (output is not null) Directory.CreateDirectory(output);
            foreach (var theme in Presets)
            foreach (var appearance in Enum.GetValues<CuiAppearance>())
            {
                var source = new Preferences(CuiThemeCatalog.Name(theme), appearance);
                using var context = new CuiThemePreferenceContext(source);
                using var loader = new CuiControlLoader(); loader.SetSurface("Present"); loader.SetThemePreferenceContext(context);
                var loaded = loader.LoadMarkup("""
                    <Page Background="{Resource CuiShellMaterial}">
                      <Container Type="Grid" RowSizes="Auto 1fr" Padding="20" CellGap="18">
                        <Container Type="Horizontal Stack" Spacing="12" Padding="14" Background="{Resource CuiFloatingSurfaceMaterial}" CornerRadius="{Resource CuiCardRadius}">
                          <Text Text="9to1 Present" FontSize="22"/>
                          <Button ID="action" Content="New slide"/>
                          <Button Content="Properties"/>
                          <Button Content="Unavailable" IsEnabled="false"/>
                        </Container>
                        <Container Type="Grid" Row="1" ColumnSizes="190 1fr" CellGap="18">
                          <Container Type="Vertical Stack" Padding="14" Spacing="16" Background="{Resource CuiSurfaceMaterial}" CornerRadius="{Resource CuiCardRadius}">
                            <Text Text="Slides" FontSize="18"/>
                            <Button Content="1 · Introduction" Background="{Resource CuiAccentPrimaryBrush}" Foreground="{Resource CuiAccentInkBrush}"/>
                            <Button Content="2 · Details"/>
                            <Text Text="Selected slide" Foreground="{Resource CuiMutedBrush}"/>
                            <Input ID="name" Type="Text" Value="Introduction"/>
                          </Container>
                          <Container Type="Grid" Column="1" RowSizes="1fr Auto" Padding="20" CellGap="16" Background="{Resource CuiWorkspaceMaterial}" CornerRadius="{Resource CuiCardRadius}">
                            <Container Type="Vertical Stack" VerticalAlignment="Center" Padding="22" Spacing="14" Background="{Resource CuiFloatingSurfaceMaterial}" CornerRadius="{Resource CuiCardRadius}">
                              <Text ID="heading" Text="A shared CUI surface" FontSize="24"/>
                              <Text Text="Readable type. Distinct materials. The same controls." Foreground="{Resource CuiTextSoftBrush}"/>
                              <Input Type="Text" Value="Editable content"/>
                              <Container Type="Horizontal Stack" Spacing="10">
                                <Button Content="Primary" Background="{Resource CuiAccentPrimaryBrush}" Foreground="{Resource CuiAccentInkBrush}"/>
                                <Button Content="Secondary"/>
                              </Container>
                            </Container>
                            <Container Type="Horizontal Stack" Row="1" Spacing="10" HorizontalAlignment="Center" Padding="10" Background="{Resource CuiFloatingSurfaceMaterial}" CornerRadius="{Resource CuiPopupRadius}">
                              <Button Content="Slide Notes"/><Button Content="100%"/>
                            </Container>
                          </Container>
                        </Container>
                      </Container>
                    </Page>
                    """);
                Assert.Empty(loaded.Diagnostics); Assert.NotNull(loaded.Root);
                var window = new Window { Width = 1000, Height = 650, Content = loaded.Root };
                try
                {
                    window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    var controls = loaded.Root!.GetVisualDescendants().OfType<Control>().ToArray();
                    var field = controls.OfType<TextBox>().Single(control => control.Name == "name");
                    field.Focus(); field.SelectAll(); window.KeyTextInput("Renamed slide");
                    Assert.Equal("Renamed slide", field.Text);
                    var button = controls.OfType<Button>().Single(control => control.Name == "action");
                    var clicked = 0; button.Click += (_, _) => ++clicked;
                    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
                    Assert.NotNull(point); window.MouseMove(point.Value); window.MouseDown(point.Value, MouseButton.Left);
                    window.MouseUp(point.Value, MouseButton.Left); Assert.Equal(1, clicked);
                    window.MouseMove(new Point(990, 640));
                    Assert.True(button.Bounds.Width > 50); Assert.True(field.Bounds.Height > 20);
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    using var bytes = new MemoryStream(); frame.Save(bytes);
                    using var bitmap = SKBitmap.Decode(bytes.ToArray());
                    Assert.Equal(1000, bitmap.Width); Assert.Equal(650, bitmap.Height);
                    Assert.NotEqual(bitmap.GetPixel(1, 1), bitmap.GetPixel(500, 300));
                    if (output is not null) frame.Save(Path.Combine(output, $"{theme}-{appearance}.png"));
                }
                finally { window.Close(); }
            }
        }, CancellationToken.None);
    }

    private static Color Composite(Color foreground, Color background)
    {
        var a = foreground.A / 255d;
        return Color.FromRgb((byte)Math.Round(foreground.R * a + background.R * (1 - a)),
            (byte)Math.Round(foreground.G * a + background.G * (1 - a)),
            (byte)Math.Round(foreground.B * a + background.B * (1 - a)));
    }
    private static void AssertContrast(Color ink, Color surface, double minimum, string context) =>
        Assert.True(CuiContrast.Ratio(ink, surface) >= minimum, $"{context}: {CuiContrast.Ratio(ink, surface):F3} < {minimum}");

    private sealed class Preferences(string name, CuiAppearance appearance) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 1, "account:preset-test", false, CuiAccessibilitySettings.Default, appearance);
        public event EventHandler? Changed { add { } remove { } }
    }
}
