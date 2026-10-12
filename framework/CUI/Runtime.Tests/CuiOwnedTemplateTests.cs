using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiOwnedTemplateTests
{
    [Fact]
    public void Professional_preserves_four_distinct_brightness_surfaces()
    {
        var surfaces = Enum.GetValues<CuiAppearance>().Select(mode =>
            CuiSurfacePaletteCatalog.For("Home", mode, CuiTheme.Professional).TideBase).ToArray();
        Assert.Equal(4, surfaces.Distinct().Count());
    }

    [Fact]
    public void Professional_roles_are_neutral_and_inset_without_hiding_focus_or_high_contrast()
    {
        foreach (var appearance in Enum.GetValues<CuiAppearance>())
        {
            var palette = CuiSurfacePaletteCatalog.For("Home", appearance, CuiTheme.Professional);
            var resources = new ResourceDictionary();
            CuiThemeResourceApplier.ApplyToResources(resources, palette);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiControlBorderBrush"]).Color);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiPopupBorderBrush"]).Color);
            Assert.Equal(palette.TideColour, Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiWorkspaceBrush"]).Color);
            Assert.Equal(palette.TideBase, Assert.IsType<SolidColorBrush>(resources["CuiShellBrush"]).Color);
            Assert.NotEqual(palette.TideBase, palette.TideColour);
            Assert.True(CuiContrast.Ratio(palette.Text, palette.Panel) >= 4.5);
            Assert.True(CuiContrast.Ratio(palette.TextSoft, palette.Panel) >= 4.5);
            Assert.True(CuiContrast.Ratio(palette.Focus, palette.TideColour) >= 3);
            Assert.True(CuiContrast.Ratio(Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiChoiceBorderBrush"]).Color, palette.Panel) >= 3);
            foreach (var neutral in new[] { palette.Panel, palette.Panel2, palette.PanelHover, palette.ButtonHover, palette.ButtonPressed })
                Assert.True(Math.Max(neutral.R, Math.Max(neutral.G, neutral.B)) - Math.Min(neutral.R, Math.Min(neutral.G, neutral.B)) <= 8);
            CuiThemeResourceApplier.ApplyToResources(resources, palette, new CuiAccessibilitySettings { HighContrast = true });
            Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiControlBorderBrush"]).Color.A);
            Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiPopupBorderBrush"]).Color.A);
        }
    }

    [Fact]
    public async Task Validation_outline_is_discernible_through_focus_hover_and_contrast_and_keeps_resource_authority()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            foreach (var appearance in Enum.GetValues<CuiAppearance>())
            foreach (var highContrast in new[] { false, true })
            {
                var input = new TextBox { Text = "Invalid value", Width = 260 };
                var palette = CuiSurfacePaletteCatalog.For("Home", appearance, CuiTheme.Professional);
                var resources = new ResourceDictionary();
                CuiThemeResourceApplier.ApplyToResources(resources, palette, new CuiAccessibilitySettings { HighContrast = highContrast });
                input.Resources.MergedDictionaries.Add(resources);
                CuiPropertyPresentation.Attach(input);
                var window = new Window { Content = input, Background = new SolidColorBrush(palette.TideBase), Width = 320, Height = 100 };
                try
                {
                    window.Show(); window.UpdateLayout(); window.MouseMove(new Point(-10, -10));
                    var validOutline = input.BorderBrush;
                    var indicator = Assert.Single(input.GetVisualDescendants().OfType<Control>(), control => control.Name == "PART_ValidationIndicator");
                    Assert.False(indicator.IsVisible);
                    var beforeBounds = input.Bounds;
                    CuiInputValidationProperties.SetHasError(input, true);
                    var invalidOutline = Assert.IsAssignableFrom<ISolidColorBrush>(input.BorderBrush).Color;
                    var field = Assert.IsAssignableFrom<ISolidColorBrush>(input.Background).Color;
                    Assert.True(CuiContrast.Ratio(invalidOutline, field) >= 4.5, $"{appearance}/{highContrast}: field outline");
                    if (!highContrast) Assert.NotEqual(validOutline, input.BorderBrush);
                    Assert.True(indicator.IsVisible);
                    window.UpdateLayout(); Assert.Equal(beforeBounds, input.Bounds);
                    Assert.True(input.Focus(Avalonia.Input.NavigationMethod.Tab));
                    window.MouseMove(new Point(100, 40));
                    Assert.True(input.IsPointerOver);
                    Assert.Equal(invalidOutline, Assert.IsAssignableFrom<ISolidColorBrush>(input.BorderBrush).Color);
                    Assert.True(CuiContrast.Ratio(invalidOutline, Assert.IsAssignableFrom<ISolidColorBrush>(input.Background).Color) >= 3,
                        $"{appearance}/{highContrast}: hovered field outline");
                    input.Resources["CuiValidationBrush"] = Brushes.Magenta;
                    Assert.Same(Brushes.Magenta, input.BorderBrush);
                    input.Resources.Remove("CuiValidationBrush");
                    CuiInputValidationProperties.SetHasError(input, false);
                    Assert.False(indicator.IsVisible);
                    Assert.True(input.TryFindResource("CuiFocusBrush", out var focus));
                    Assert.Same(focus, input.BorderBrush);
                    Assert.Equal("Invalid value", input.Text);
                }
                finally { window.Close(); CuiPropertyPresentation.Detach(input); }
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Behaviour_subclass_cannot_inherit_a_competing_backend_template_or_state_style()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var donor = new FuncControlTemplate<BehaviourButton>((_, _) => new TextBlock { Text = "DONOR TEMPLATE" });
            var theme = new ControlTheme(typeof(BehaviourButton));
            theme.Setters.Add(new Setter(TemplatedControl.TemplateProperty, donor));
            theme.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Brushes.Magenta));
            var button = new BehaviourButton { Content = "CUI action", Theme = theme };
            button.Styles.Add(new Style(x => x.OfType<Button>().Class(":pointerover"))
                { Setters = { new Setter(TemplatedControl.BackgroundProperty, Brushes.Lime) } });
            CuiPropertyPresentation.Attach(button);
            CuiPropertyPresentation.Set(button, "Background", Brushes.Red, CuiPropertyLevel.Instance, "authored");
            var clicks = 0; button.Click += (_, _) => ++clicks;
            var window = new Window { Content = button, Width = 240, Height = 100 };
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.True(CuiControlTemplates.TryGet(typeof(BehaviourButton), out var owned));
                Assert.Same(owned, button.Template);
                Assert.DoesNotContain(button.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "DONOR TEMPLATE");
                Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "CUI action");
                button.Template = donor;
                Assert.Same(owned, button.Template);
                window.MouseMove(new Point(40, 40));
                Assert.True(button.IsPointerOver);
                Assert.NotEqual(Brushes.Lime, button.Background);
                window.MouseDown(new Point(40, 40), Avalonia.Input.MouseButton.Left);
                window.MouseUp(new Point(40, 40), Avalonia.Input.MouseButton.Left);
                Assert.Equal(1, clicks);
                window.MouseMove(new Point(-10, -10));
                Assert.Same(Brushes.Red, button.Background);
            }
            finally { window.Close(); CuiPropertyPresentation.Detach(button); }
        }, CancellationToken.None);
    }

    [Fact]
    public void Missing_ordinary_template_is_explicit_while_specialised_content_retains_its_owner()
    {
        var unsupported = new UnregisteredPrimitive();
        var error = Assert.Throws<InvalidOperationException>(() => CuiPropertyPresentation.Attach(unsupported));
        Assert.Contains("CUI_TEMPLATE_MISSING", error.Message);
        var specialised = new UnregisteredPrimitive();
        var original = new FuncControlTemplate<UnregisteredPrimitive>((_, _) => new Border { Background = Brushes.Gold });
        specialised.Template = original;
        CuiPropertyPresentation.Attach(specialised, ownsTemplate: false);
        Assert.Same(original, specialised.Template);
        CuiPropertyPresentation.Detach(specialised);
    }

    [Fact]
    public async Task Floating_menu_uses_opaque_scoped_chrome_and_reacts_to_accessibility_without_replacing_items()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader(); loader.SetAppearance(CuiAppearance.Dark);
            var loaded = loader.LoadMarkup("""
                <Theme Name="Professional"><Container Type="Menu" VerticalAlignment="Top"><Button Type="Menu Item" Content="File">
                  <Button Type="Menu Item" Content="Open" Shortcut="Ctrl+O"/>
                  <Button Type="Menu Item" Content="Unavailable" Interactive="False"/>
                </Button></Container></Theme>
                """);
            Assert.Empty(loaded.Diagnostics);
            var window = new Window { Content = loaded.Root, Width = 600, Height = 300 };
            try
            {
                window.Show(); window.UpdateLayout();
                var owner = Assert.Single(loaded.Root!.GetVisualDescendants().OfType<CuiMenuItem>());
                var compactBarHeight = owner.Bounds.Height;
                owner.IsSubMenuOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var item = owner.Items.OfType<CuiMenuItem>().First();
                var frame = item.GetVisualAncestors().OfType<Border>().First(border => border.BoxShadow.Count > 0);
                Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(frame.Background).Color.A);
                Assert.Equal(new CornerRadius(24), frame.CornerRadius);
                Assert.Equal(new Thickness(12), frame.Padding);
                Assert.True(item.Bounds.Height >= 40);
                Assert.True(owner.Bounds.Height < 40); // Floating rows never enlarge the compact menu bar.
                Assert.Equal(compactBarHeight, owner.Bounds.Height);
                var shadowBounds = frame.BoxShadow[0].TransformBounds(new Rect(0, 0, 1, 1));
                Assert.True(frame.Margin.Left >= -shadowBounds.X && frame.Margin.Top >= -shadowBounds.Y);
                Assert.True(frame.Margin.Right >= shadowBounds.Right - 1 && frame.Margin.Bottom >= shadowBounds.Bottom - 1);
                Assert.Equal(new Thickness(0), item.BorderThickness);
                Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(item.Background).Color);
                var disabled = owner.Items.OfType<CuiMenuItem>().Last();
                Assert.False(disabled.IsEffectivelyEnabled);
                Assert.True(disabled.Opacity < 1);
                var originalBounds = item.Bounds;
                var resources = new ResourceDictionary();
                CuiThemeResourceApplier.ApplyToResources(resources,
                    CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Professional),
                    new CuiAccessibilitySettings { HighContrast = true, DisplayScale = 1.5 });
                foreach (var key in new[] { "CuiPopupPadding", "CuiControlBorderWidth", "CuiPopupShadow", "CuiPopupShadowMargin" })
                    owner.Resources[key] = resources[key];
                window.UpdateLayout();
                Assert.Equal(new Thickness(18), frame.Padding);
                Assert.Equal(new Thickness(3), frame.BorderThickness);
                Assert.Equal(0, frame.BoxShadow.Count);
                Assert.Equal(new Thickness(0), frame.Margin);
                Assert.Same(item, owner.Items[0]);
                Assert.True(item.Bounds.Width > 0 && originalBounds.Width > 0);
                owner.IsSubMenuOpen = false;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class BehaviourButton : Button;
    private sealed class UnregisteredPrimitive : TemplatedControl;
}
