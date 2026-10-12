using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiCanonicalBoundaryRuntimeTests
{
    [Fact]
    public void Theme_refresh_never_exposes_a_partial_palette_to_resource_observers()
    {
        var source = new ThemeSource();
        using var preference = new CuiThemePreferenceContext(source);
        using var loader = new CuiControlLoader(); loader.SetThemePreferenceContext(preference);
        var loaded = loader.LoadMarkup("<Container Type=\"Overlay\"><Button Content=\"Action\"/></Container>");
        Assert.Empty(loaded.Diagnostics);
        var root = Assert.IsType<CuiContainerControl>(loaded.Root);
        Assert.True(root.TryFindResource("CuiTextBrush", out var initial));
        Assert.True(root.TryFindResource("HavenTextPrimaryBrush", out var initialAlias));
        Assert.Same(initial, initialAlias);
        var partial = 0;
        var notifications = 0;
        root.ResourcesChanged += (_, _) =>
        {
            ++notifications;
            root.TryFindResource("CuiTextBrush", out var canonical);
            root.TryFindResource("HavenTextPrimaryBrush", out var alias);
            if (!ReferenceEquals(canonical, alias)) ++partial;
        };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        source.Change("Glow");
        System.Console.WriteLine($"Theme observation: {notifications} notifications, {partial} partial palettes, {watch.Elapsed.TotalMilliseconds:F2} ms");
        Assert.True(notifications > 0);
        Assert.Equal(0, partial);
    }

    [Fact]
    public void Canonical_input_border_projects_typed_values()
    {
        using var loader = new CuiControlLoader();
        var loaded = loader.LoadMarkup("<Container Type=\"Overlay\"><Input Type=\"Text\" BorderWidth=\"0\" BorderColor=\"#123456\"/></Container>");
        Assert.Empty(loaded.Diagnostics);
        var container = Assert.IsType<CuiContainerControl>(loaded.Root);
        var input = Assert.IsType<TextBox>(container.AuthoredChildren.Single());
        Assert.Equal(new Thickness(0), input.BorderThickness);
        Assert.Equal(Color.Parse("#123456"), Assert.IsAssignableFrom<ISolidColorBrush>(input.BorderBrush).Color);
    }

    [Fact]
    public void Reorder_reconciles_original_cell_during_an_earlier_visibility_observer()
    {
        using var container = new CuiContainerControl("Vertical Stack");
        var first = new Button(); var second = new Button(); var reentered = false;
        first.PropertyChanged += (_, change) =>
        {
            if (change.Property == Control.IsVisibleProperty && first.IsVisible)
            { container.ReorderChildren([second, first]); reentered = true; }
        };
        container.AddChild(first); container.AddChild(second);
        var originalCell = container.Layout.Children[0];
        first.IsVisible = false; first.IsVisible = true;
        Assert.True(reentered); Assert.Same(originalCell, container.Layout.Children[1]);
        Assert.Equal(new Control[] { second, first }, container.AuthoredChildren);
    }
    [Fact]
    public void Text_color_is_the_specified_foreground_alias()
    {
        var registry = new CuiControlRegistry();
        Assert.True(registry.TryGetProperty("Color", out var property)); Assert.Equal("Foreground", property.Name);
        using var loader = new CuiControlLoader(registry);
        var loaded = loader.LoadMarkup("<Text Color=\"#123456\">Readable</Text>");
        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(Color.Parse("#123456"), Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<TextBlock>(loaded.Root).Foreground).Color);
    }
    [Fact]
    public void Typed_gradient_resources_survive_initial_load_and_live_theme_change()
    {
        var source = new ThemeSource();
        using var preference = new CuiThemePreferenceContext(source, new CuiThemeDefinitionCatalog());
        using var loader = new CuiControlLoader(); loader.SetThemePreferenceContext(preference);
        var loaded = loader.LoadMarkup("<Container Type=\"Overlay\" Background=\"{Resource CuiAccentSecondaryBrush}\"><Container ID=\"marker\" Type=\"Overlay\" Background=\"{Resource CuiAccentBrush}\"/></Container>");
        Assert.Empty(loaded.Diagnostics);
        var shell = Assert.IsType<CuiContainerControl>(loaded.Root);
        var marker = Assert.IsType<CuiContainerControl>(shell.AuthoredChildren.Single());
        Assert.IsType<LinearGradientBrush>(shell.Background);
        var first = Assert.IsType<LinearGradientBrush>(marker.Background);
        Assert.True(marker.Resources.TryGetResource("CuiAccentBrush", null, out var resource));
        Assert.Same(resource, marker.Background);
        source.Change("Glow");
        Assert.IsType<LinearGradientBrush>(marker.Background);
        Assert.NotSame(first, marker.Background);
        Assert.True(marker.Resources.TryGetResource("CuiAccentBrush", null, out resource));
        Assert.Same(resource, marker.Background);
        Assert.Equal(CuiPropertyLevel.Instance, CuiPropertyPresentation.For(marker).Resolve("Background").Level);
    }

    [Fact]
    public void Scoped_image_resolver_and_bound_vector_images_retain_typed_values()
    {
        var registry = new CuiControlRegistry();
        var resolved = new List<IImage>();
        registry.RegisterImageSourceResolver((key, target) =>
        {
            if (key != "icon:dashboard") return null;
            Assert.True(target.Resources.TryGetResource("CuiTextBrush", null, out var brush));
            var image = new DrawingImage { Drawing = new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 24, 24)), Brush = Assert.IsAssignableFrom<IBrush>(brush) } };
            resolved.Add(image); return image;
        });
        var source = new ThemeSource();
        using var preference = new CuiThemePreferenceContext(source, new CuiThemeDefinitionCatalog());
        using var loader = new CuiControlLoader(registry); loader.SetThemePreferenceContext(preference);
        var loaded = loader.LoadMarkup("<Image Source=\"icon:dashboard\" Width=\"24\" Height=\"24\"/>");
        Assert.Empty(loaded.Diagnostics);
        var image = Assert.IsType<Image>(loaded.Root);
        Assert.Same(resolved[0], image.Source);
        source.Change("Glow");
        Assert.Equal(2, resolved.Count); Assert.Same(resolved[1], image.Source);
        var state = new ImageState { Image = resolved[0] };
        using var bound = new CuiControlLoader(); bound.SetBindingContext(state);
        var boundResult = bound.LoadMarkup("<Image Source=\"{Binding Image}\"/>");
        Assert.Empty(boundResult.Diagnostics);
        var boundImage = Assert.IsType<Image>(boundResult.Root);
        Assert.Same(state.Image, boundImage.Source);
        state.Image = resolved[1]; state.Notify(); Assert.Same(state.Image, boundImage.Source);
        state.Image = null; state.Notify(); Assert.Null(boundImage.Source);
    }

    private sealed class ImageState : ICuiBindingContext, INotifyPropertyChanged
    {
        public IImage? Image;
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool TryGetValue(string path, out object? value) { value = Image; return path == nameof(Image); }
        public void Notify() => PropertyChanged?.Invoke(this, new(nameof(Image)));
    }
    [Theory]
    [InlineData("Grid")][InlineData("StackPanel")][InlineData("WrapPanel")][InlineData("ScrollViewer")][InlineData("TextBlock")][InlineData("Border")]
    public void Shipping_markup_loader_does_not_accept_donor_tags(string tag)
    {
        using var loader = new CuiControlLoader();
        var result = loader.LoadMarkup($"<Cui><{tag}/></Cui>");
        Assert.Null(result.Root);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUI045");
    }
    [Fact]
    public void Canonical_grid_uses_native_layout_with_real_padding_gap_and_placement()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup("""
          <Container Type="Grid" RowSizes="Auto 1fr" ColumnSizes="1fr 2fr" Padding="12" CellGap="8" CellMargin="3">
            <Text ID="body" Row="1" Column="1">Retained native layout</Text>
          </Container>
          """);
        Assert.Empty(diagnostics);
        var container = Assert.IsType<CuiContainerControl>(root);
        var grid = Assert.IsType<Grid>(container.Layout);
        Assert.Equal(new Thickness(12), container.Padding);
        Assert.Equal(2, grid.RowDefinitions.Count); Assert.Equal(2, grid.ColumnDefinitions.Count);
        Assert.Equal(8, grid.ColumnSpacing);
        var text = root!.GetLogicalDescendants().OfType<TextBlock>().Single();
        Assert.Equal(1, Grid.GetRow(text)); Assert.Equal(1, Grid.GetColumn(grid.Children.Single()));
        Assert.Equal(new Thickness(3), Assert.IsType<Border>(grid.Children.Single()).Padding);
        Assert.Equal(CuiPropertyLevel.Instance, CuiPropertyPresentation.For(container).Resolve("ColumnSizes").Level);
    }
    [Fact]
    public void Hidden_keeps_layout_and_input_while_active_and_interactive_restore_stored_values()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup("<Button Hidden=\"True\" Interactive=\"True\" Opacity=\"0.6\" Content=\"Hidden but active\"/>");
        Assert.Empty(diagnostics);
        var button = Assert.IsType<Button>(root);
        Assert.True(button.IsVisible); Assert.True(button.IsHitTestVisible); Assert.Equal(0, button.Opacity);
        CuiPropertyPresentation.Set(button, "Active", false, CuiPropertyLevel.Instance, "CUI authored instance");
        Assert.False(button.IsVisible);
        CuiPropertyPresentation.Set(button, "Hidden", false, CuiPropertyLevel.Instance, "CUI authored instance");
        Assert.False(button.IsVisible); Assert.Equal(.6, button.Opacity);
        CuiPropertyPresentation.Set(button, "Interactive", false, CuiPropertyLevel.Instance, "CUI authored instance");
        CuiPropertyPresentation.Set(button, "Active", true, CuiPropertyLevel.Instance, "CUI authored instance");
        Assert.True(button.IsVisible); Assert.False(button.IsHitTestVisible);
    }
    [Fact]
    public void Conditional_branch_rebuild_retains_explicit_inherited_theme()
    {
        var previous = CuiSurfacePaletteCatalog.ActiveTheme;
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Professional;
        try
        {
            var state = new BindingState();
            using var loader = new CuiControlLoader(); loader.SetBindingContext(state);
            var (root, diagnostics) = loader.LoadMarkup("<Theme Name=\"Glow\"><If Condition=\"{Binding Visible}\"><Container Type=\"Overlay\"><Text>Scoped</Text></Container></If></Theme>");
            Assert.Empty(diagnostics);
            state.Visible = true; state.Notify();
            var child = root!.GetLogicalDescendants().OfType<CuiContainerControl>().Single();
            Assert.True(child.Resources.TryGetResource("CuiTheme", null, out var resolvedTheme));
            Assert.Equal(CuiTheme.Glow, resolvedTheme);
        }
        finally { CuiSurfacePaletteCatalog.ActiveTheme = previous; }
    }
    [Fact]
    public void Borrowed_home_preference_rethemes_two_loaders_without_replacing_editor_or_content()
    {
        var catalog = new CuiThemeDefinitionCatalog();
        catalog.Add(new("Focused", "Professional", [new("Button", null, new Dictionary<string, object?> { ["Padding"] = "20", ["FontSize"] = 18d })]));
        var source = new ThemeSource();
        using var preference = new CuiThemePreferenceContext(source, catalog);
        using var first = new CuiControlLoader(); using var second = new CuiControlLoader();
        var state = new EditableState(); first.SetBindingContext(state);
        first.SetThemePreferenceContext(preference); second.SetThemePreferenceContext(preference);
        var a = first.LoadMarkup("<Theme Name=\"user_preference\"><Container Type=\"Vertical Stack\"><Input ID=\"editor\" Type=\"Text\" Value=\"{Binding Title}\"/><Button ID=\"action\" Content=\"Save\"/><Theme Name=\"Glow\"><Button ID=\"fixed\" Content=\"Fixed\"/></Theme></Container></Theme>");
        var b = second.LoadMarkup("<Theme Name=\"user_preference\"><Button Content=\"Other app\"/></Theme>");
        Assert.Empty(a.Diagnostics); Assert.Empty(b.Diagnostics);
        var editor = a.Root!.GetLogicalDescendants().OfType<TextBox>().Single();
        editor.Text = "Author content"; editor.CaretIndex = 3;
        var button = a.Root!.GetLogicalDescendants().OfType<Button>().Single(control => control.Name == "action");
        var fixedButton = a.Root!.GetLogicalDescendants().OfType<Button>().Single(control => control.Name == "fixed");
        source.Change("Focused");
        Assert.Same(editor, a.Root!.GetLogicalDescendants().OfType<TextBox>().Single());
        Assert.Equal("Author content", editor.Text); Assert.Equal("Author content", state.Title); Assert.Equal(3, editor.CaretIndex);
        Assert.Equal(new Thickness(20), button.Padding); Assert.Equal(new Thickness(20), Assert.IsType<Button>(b.Root).Padding);
        Assert.True(fixedButton.Resources.TryGetResource("CuiEffectiveThemeName", null, out var explicitName)); Assert.Equal("Glow", explicitName);
        source.Change("Missing custom theme");
        Assert.True(preference.Current.UsedFallback); Assert.NotNull(preference.Current.Diagnostic);
        Assert.Equal("Author content", editor.Text);
    }
    [Fact]
    public void Active_view_detaches_workspace_from_layout_and_accessibility_tree_then_restores_same_controls()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup("""
            <Container Type="Overlay">
              <Container ID="start" Type="Vertical Stack"><Text>Choose work</Text><Input Type="Text" Value="Search"/></Container>
              <Container ID="workspace" Type="Vertical Stack" Active="False"><Input ID="document" Type="Text" Value="Retained draft"/></Container>
            </Container>
            """);
        Assert.Empty(diagnostics);
        var host = Assert.IsType<CuiContainerControl>(root);
        var start = host.AuthoredChildren.Single(child => child.Name == "start");
        var workspace = Assert.IsType<CuiContainerControl>(host.AuthoredChildren.Single(child => child.Name == "workspace"));
        var editor = Assert.IsType<TextBox>(workspace.AuthoredChildren.Single()); editor.CaretIndex = 4;
        Assert.DoesNotContain(root!.GetLogicalDescendants(), value => ReferenceEquals(value, editor));
        Assert.Single(host.Layout.Children);
        CuiPropertyPresentation.Set(start, "Active", false, CuiPropertyLevel.Instance, "CUI authored Instance");
        CuiPropertyPresentation.Set(workspace, "Active", true, CuiPropertyLevel.Instance, "CUI authored Instance");
        Assert.Contains(root.GetLogicalDescendants(), value => ReferenceEquals(value, editor));
        Assert.DoesNotContain(root.GetLogicalDescendants(), value => ReferenceEquals(value, start));
        Assert.Single(host.Layout.Children); Assert.Equal("Retained draft", editor.Text); Assert.Equal(4, editor.CaretIndex);
        CuiPropertyPresentation.Set(workspace, "Active", false, CuiPropertyLevel.Instance, "CUI authored Instance");
        CuiPropertyPresentation.Set(start, "Active", true, CuiPropertyLevel.Instance, "CUI authored Instance");
        Assert.DoesNotContain(root.GetLogicalDescendants(), value => ReferenceEquals(value, editor));
        Assert.Equal("Retained draft", editor.Text);
    }
    [Fact]
    public void Capability_denial_survives_native_animation_and_restores_current_authored_gate()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup("<Button Content=\"Protected action\"/>"); Assert.Empty(diagnostics);
        var button = Assert.IsType<Button>(root);
        using var denial = CuiLifecycleProperties.RestrictInteraction(button);
        Assert.False(button.IsEnabled); Assert.False(button.IsHitTestVisible);
        using var competingNativeValue = button.SetValue(Control.IsEnabledProperty, true, BindingPriority.Animation);
        Assert.False(button.IsEnabled);
        CuiPropertyPresentation.Set(button, "Interactive", true, CuiPropertyLevel.Instance, "authored input");
        Assert.False(button.IsEnabled);
        CuiPropertyPresentation.Set(button, "Interactive", false, CuiPropertyLevel.Instance, "authored input");
        denial.Dispose();
        Assert.False(button.IsEnabled);
        CuiPropertyPresentation.Set(button, "Interactive", true, CuiPropertyLevel.Instance, "authored input");
        Assert.True(button.IsEnabled); Assert.True(button.IsHitTestVisible);
    }
    private sealed class EditableState : ICuiWritableBindingContext, INotifyPropertyChanged
    {
        public string Title = "Initial";
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool TryGetValue(string path, out object? value) { value = Title; return path == "Title"; }
        public bool TrySetValue(string path, object? value)
        { if (path != "Title" || value is not string text) return false; Title = text; PropertyChanged?.Invoke(this, new(nameof(Title))); return true; }
    }
    private sealed class ThemeSource : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } = new("Professional", 1, "test-user", true, CuiAccessibilitySettings.Default);
        public event EventHandler? Changed;
        public void Change(string name) { Current = Current with { Name = name, Revision = Current.Revision + 1 }; Changed?.Invoke(this, EventArgs.Empty); }
    }
    private sealed class BindingState : ICuiBindingContext, INotifyPropertyChanged
    {
        public bool Visible;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visible)));
        public bool TryGetValue(string path, out object? value) { value = Visible; return path == "Visible"; }
    }
}
