using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiOwnedTemplateTests
{
    [Theory]
    [InlineData(13d)]
    [InlineData(22d)]
    public async Task Button_content_is_vertically_centered_by_theme_while_authored_alignment_and_multiline_inputs_survive(double fontSize)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            foreach (var theme in Enum.GetValues<CuiTheme>())
            {
                using var loader = new CuiControlLoader();
                var (root, diagnostics) = loader.LoadMarkup($"""
                    <Theme Name="{CuiThemeCatalog.Name(theme)}"><Container Type="Vertical Stack">
                      <Button ID="ordinary" Height="64" Width="180" Padding="8" FontSize="{fontSize}">Spaces</Button>
                      <Button ID="symbol" Height="44" Width="44" Padding="8" FontSize="{fontSize}">+</Button>
                      <Button ID="exception" Height="64" Width="180" Padding="8" VerticalContentAlignment="Top" HorizontalContentAlignment="Left">Intentional top</Button>
                      <Container Type="Menu"><Button Type="Menu Item" ID="menu" Height="56" Content="Aa" /></Container>
                      <Input Type="Text" ID="draft" Height="90" AcceptsReturn="True" Value="alpha&#10;beta" />
                    </Container></Theme>
                    """);
                Assert.Empty(diagnostics);
                var window = new Window { Content = root, Width = 380, Height = 480 };
                try
                {
                    window.Show(); window.UpdateLayout();
                    var ordinary = root!.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ordinary");
                    var text = ordinary.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == "Spaces");
                    var position = text.TranslatePoint(default, ordinary)!.Value;
                    Assert.InRange(Math.Abs(position.Y + text.Bounds.Height / 2 - ordinary.Bounds.Height / 2), 0, 1);
                    Assert.InRange(Math.Abs(position.X + text.Bounds.Width / 2 - ordinary.Bounds.Width / 2), 0, 1);
                    var symbol = root!.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "symbol");
                    var glyph = symbol.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == "+");
                    var glyphPosition = glyph.TranslatePoint(default, symbol)!.Value;
                    Assert.InRange(Math.Abs(glyphPosition.X + glyph.Bounds.Width / 2 - symbol.Bounds.Width / 2), 0, 1);
                    Assert.InRange(Math.Abs(glyphPosition.Y + glyph.Bounds.Height / 2 - symbol.Bounds.Height / 2), 0, 1);
                    var effective = CuiPropertyPresentation.For(ordinary).Resolve("VerticalContentAlignment");
                    Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, effective.Value);
                    var exception = root!.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "exception");
                    Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, exception.VerticalContentAlignment);
                    Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, exception.HorizontalContentAlignment);
                    var menu = root!.GetVisualDescendants().OfType<CuiMenuItem>().Single();
                    Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, menu.VerticalContentAlignment);
                    var input = root!.GetVisualDescendants().OfType<TextBox>().Single();
                    Assert.NotEqual(Avalonia.Layout.VerticalAlignment.Center, input.VerticalContentAlignment);
                    Assert.Equal("alpha\nbeta", input.Text);
                    // A theme token change is live, and remains below an authored exception.
                    ordinary.Resources["CuiButtonVerticalContentAlignment"] = Avalonia.Layout.VerticalAlignment.Bottom;
                    exception.Resources["CuiButtonVerticalContentAlignment"] = Avalonia.Layout.VerticalAlignment.Bottom;
                    window.UpdateLayout();
                    Assert.Equal(Avalonia.Layout.VerticalAlignment.Bottom, ordinary.VerticalContentAlignment);
                    Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, exception.VerticalContentAlignment);
                }
                finally { window.Close(); }
            }
        }, CancellationToken.None);
    }

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
                Assert.Same(Brushes.Red, button.Background);
                var frame = Assert.Single(button.GetVisualDescendants().OfType<CuiMaterialBorder>());
                Assert.NotNull(frame.HoverTint);
                window.MouseDown(new Point(40, 40), Avalonia.Input.MouseButton.Left);
                window.MouseUp(new Point(40, 40), Avalonia.Input.MouseButton.Left);
                Assert.Equal(1, clicks);
                window.MouseMove(new Point(-10, -10));
                Assert.Same(Brushes.Red, button.Background);
                Assert.Null(frame.HoverTint);
            }
            finally { window.Close(); CuiPropertyPresentation.Detach(button); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Hover_wash_preserves_authored_white_gradient_and_material_backgrounds()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("<Theme Name='Glow'><Page><Button ID='authored' Content='Custom colour'/></Page></Theme>");
            Assert.Empty(diagnostics); Assert.NotNull(root);
            var button = Assert.Single(root!.GetVisualDescendants().OfType<Button>());
            var window = new Window { Content = root, Width = 300, Height = 120 };
            try
            {
                window.Show(); window.UpdateLayout();
                var frame = Assert.Single(button.GetVisualDescendants().OfType<CuiMaterialBorder>());
                var white = new SolidColorBrush(Colors.White);
                var ink = new SolidColorBrush(Colors.Black);
                CuiPropertyPresentation.Set(button, "Background", white, CuiPropertyLevel.Instance, "authored white");
                CuiPropertyPresentation.Set(button, "Foreground", ink, CuiPropertyLevel.Instance, "authored ink");
                CuiPropertyPresentation.Set(button, "AccentRole", CuiAccentRole.Primary, CuiPropertyLevel.Instance, "glow action");
                var glow = new DropShadowEffect { Color = Colors.Fuchsia, BlurRadius = 12, Opacity = .25 };
                button.Resources["CuiTheme"] = CuiTheme.Glow;
                button.Resources["CuiButtonMotionDuration"] = TimeSpan.Zero;
                button.Resources["CuiAccentPrimaryHoverEffect"] = glow;
                window.UpdateLayout();
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point); window.UpdateLayout();

                Assert.True(button.IsPointerOver);
                Assert.Same(white, button.Background);
                Assert.Same(ink, button.Foreground);
                Assert.Equal(Color.FromArgb(20, 0, 0, 0), frame.HoverTint);
                AssertGlow(frame.Effect);
                Assert.DoesNotContain(root.GetVisualDescendants().OfType<CuiMaterialBorder>(),
                    other => !ReferenceEquals(other, frame) && other.HoverTint is not null);

                white.Color = Colors.Black;
                Assert.Same(white, button.Background);
                Assert.Equal(Color.FromArgb(20, 255, 255, 255), frame.HoverTint);
                white.Color = Colors.White;
                Assert.Equal(Color.FromArgb(20, 0, 0, 0), frame.HoverTint);

                button.Resources["CuiHighContrast"] = true;
                Assert.Equal(Color.FromArgb(36, 0, 0, 0), frame.HoverTint);
                Assert.Same(white, button.Background);
                Assert.Null(frame.Effect);
                button.Resources["CuiHighContrast"] = false;
                Assert.NotNull(frame.HoverTint);
                AssertGlow(frame.Effect);

                var black = new SolidColorBrush(Colors.Black);
                CuiPropertyPresentation.Set(button, "Background", black, CuiPropertyLevel.Instance, "authored black");
                Assert.Same(black, button.Background);
                Assert.Equal(Color.FromArgb(20, 255, 255, 255), frame.HoverTint);

                var gradient = new LinearGradientBrush
                {
                    GradientStops = [new GradientStop(Colors.White, 0), new GradientStop(Colors.White, 1)]
                };
                CuiPropertyPresentation.Set(button, "Background", gradient, CuiPropertyLevel.Instance, "authored gradient");
                Assert.Same(gradient, button.Background);
                Assert.Equal(Color.FromArgb(20, 0, 0, 0), frame.HoverTint);
                gradient.GradientStops[0].Color = Colors.Black;
                Assert.Equal(Color.FromArgb(20, 255, 255, 255), frame.HoverTint);

                var material = new CuiMaterialBrush(Colors.White, .8, 4, .01);
                CuiPropertyPresentation.Set(button, "Background", material, CuiPropertyLevel.Instance, "authored material");
                Assert.Same(material, button.Background);
                Assert.NotNull(frame.HoverTint);

                var customMaterial = new CuiCustomMaterialBrush(new CuiCustomFill(
                    CuiCustomFillKind.SolidColour, new CuiCustomColour("#FFFFFF", 0, 4)));
                CuiPropertyPresentation.Set(button, "Background", customMaterial, CuiPropertyLevel.Instance, "authored custom material");
                Assert.Same(customMaterial, button.Background);
                Assert.NotNull(frame.HoverTint);

                window.MouseMove(new Point(-10, -10)); window.UpdateLayout();
                Assert.Null(frame.HoverTint);
                Assert.Same(customMaterial, button.Background);
                window.MouseMove(point); window.UpdateLayout();
                Assert.NotNull(frame.HoverTint);
                Assert.Same(customMaterial, button.Background);
            }
            finally { window.Close(); CuiPropertyPresentation.Detach(button); }

            static void AssertGlow(IEffect? effect)
            {
                var shadow = Assert.IsAssignableFrom<IDropShadowEffect>(effect);
                Assert.Equal(Colors.Fuchsia, shadow.Color);
                Assert.Equal(12d, shadow.BlurRadius);
                Assert.Equal(.25, shadow.Opacity);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Hover_surface_opt_in_paints_only_the_authored_container_group()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var root = new CuiContainerControl("Vertical Stack") { Width = 300, Height = 120 };
            var plain = new CuiContainerControl("Horizontal Stack") { Height = 48 };
            var group = new CuiContainerControl("Horizontal Stack") { Height = 48 };
            var title = new TextBlock { Text = "Workspace" };
            plain.Layout.Children.Add(new TextBlock { Text = "Plain container" });
            group.Layout.Children.Add(title);
            group.Classes.Add("cui-hover-surface");
            root.Layout.Children.Add(plain);
            root.Layout.Children.Add(group);
            foreach (var surface in new[] { root, plain, group }) CuiPropertyPresentation.Attach(surface);
            var authored = new SolidColorBrush(Colors.White);
            CuiPropertyPresentation.Set(group, "Background", authored, CuiPropertyLevel.Instance, "authored browser row");
            var window = new Window { Content = root };
            try
            {
                window.Show(); window.UpdateLayout();
                var point = title.TranslatePoint(new Point(2, 2), window)!.Value;
                window.MouseMove(point); window.UpdateLayout();
                Assert.True(group.IsPointerOver);
                Assert.Same(authored, group.Background);
                Assert.Equal(Color.FromArgb(20, 0, 0, 0), group.HoverTint);
                Assert.Null(plain.HoverTint);

                group.Classes.Remove("cui-hover-surface");
                Assert.Null(group.HoverTint);
                group.Classes.Add("cui-hover-surface");
                Assert.Equal(Color.FromArgb(20, 0, 0, 0), group.HoverTint);

                window.MouseMove(new Point(-10, -10)); window.UpdateLayout();
                Assert.Null(group.HoverTint);
                Assert.Same(authored, group.Background);
            }
            finally
            {
                window.Close();
                foreach (var surface in new[] { root, plain, group }) CuiPropertyPresentation.Detach(surface);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MenuItem_pressed_pseudo_class_suppresses_hover_wash_until_release()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("<Theme Name='Professional'><Container Type='Menu'><Button Type='Menu Item' ID='row' Content='Open'/></Container></Theme>");
            Assert.Empty(diagnostics); Assert.NotNull(root);
            var window = new Window { Content = root, Width = 280, Height = 100 };
            MenuItem? item = null;
            try
            {
                window.Show(); window.UpdateLayout();
                item = Assert.Single(root!.GetVisualDescendants().OfType<MenuItem>());
                var authored = new SolidColorBrush(Colors.White);
                CuiPropertyPresentation.Set(item, "Background", authored, CuiPropertyLevel.Instance, "authored menu row");
                Assert.True(item.TryFindResource("CuiMenuSelectionBrush", out var selectedBrush));
                var frame = Assert.Single(item.GetVisualDescendants().OfType<CuiMaterialBorder>());
                var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point); window.UpdateLayout();
                Assert.True(item.IsPointerOver);
                Assert.NotNull(frame.HoverTint);
                Assert.Same(authored, item.Background);

                window.MouseDown(point, Avalonia.Input.MouseButton.Left); window.UpdateLayout();
                Assert.Contains(":pressed", item.Classes);
                Assert.Null(frame.HoverTint);
                // Pointer press moves the MenuItem into its real selected state. Its CUI selection
                // brush is expected to win over the authored rest fill while hover feedback is off.
                Assert.True(item.IsSelected);
                Assert.Same(selectedBrush, item.Background);

                window.MouseUp(point, Avalonia.Input.MouseButton.Left); window.UpdateLayout();
                Assert.DoesNotContain(":pressed", item.Classes);
                Assert.NotNull(frame.HoverTint);
                Assert.True(item.IsSelected);
                Assert.Same(selectedBrush, item.Background);
            }
            finally { window.Close(); if (item is not null) CuiPropertyPresentation.Detach(item); }
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
    public async Task Floating_menu_uses_scoped_material_and_opaque_accessibility_fallback_without_replacing_items()
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
                var material = Assert.IsType<CuiMaterialBrush>(frame.Background);
                Assert.Equal(8, material.BlurRadius); Assert.Equal(.96, material.TintOpacity);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Professional).Text,
                    Assert.IsAssignableFrom<ISolidColorBrush>(item.Foreground).Color);
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
                foreach (var key in new[] { "CuiPopupPadding", "CuiControlBorderWidth", "CuiPopupShadow", "CuiPopupShadowMargin", "CuiPopupSurfaceBrush" })
                    owner.Resources[key] = resources[key];
                window.UpdateLayout();
                Assert.IsNotType<CuiMaterialBrush>(frame.Background);
                Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(frame.Background).Color.A);
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
