using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiThemeAuthorityTests
{
    [Fact]
    public async Task Global_palette_refresh_publishes_complete_resources_without_per_token_notifications()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var initial = CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Professional);
            var next = CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Bubble);
            CuiThemeResourceApplier.Apply(initial);
            var resources = Application.Current!.Resources;
            var bundledResources = Assert.Single(resources.MergedDictionaries);
            var retainedBrush = resources["CuiTextBrush"];
            var sentinel = new object(); resources["OwnerResource"] = sentinel;
            var text = new TextBox { Text = "Keep my edit" };
            var window = new Window { Content = text, Width = 320, Height = 120 };
            try
            {
                window.Show(); window.UpdateLayout(); text.Focus(); text.SelectionStart = 5; text.SelectionEnd = 7;
                var notifications = 0;
                var partial = 0;
                text.ResourcesChanged += (_, _) =>
                {
                    ++notifications;
                    var theme = Assert.IsType<CuiTheme>(resources["CuiTheme"]);
                    var expected = theme == initial.Theme ? initial : next;
                    if (Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiTextBrush"]).Color != expected.Text)
                        ++partial;
                };
                var watch = System.Diagnostics.Stopwatch.StartNew();
                CuiThemeResourceApplier.Apply(next);
                System.Console.WriteLine($"Global theme: {notifications} notifications, {partial} partial palettes, {watch.Elapsed.TotalMilliseconds:F2} ms");
                // The application and its one bundled palette each publish once
                // on detach and once on reattach; no token adds a notification.
                Assert.Equal(4, notifications);
                Assert.Equal(0, partial);
                Assert.Same(resources, Application.Current.Resources);
                Assert.Same(Application.Current, ((IResourceProvider)resources).Owner);
                Assert.Same(bundledResources, Assert.Single(resources.MergedDictionaries));
                Assert.Same(Application.Current, bundledResources.Owner);
                Assert.Same(retainedBrush, resources["CuiTextBrush"]);
                Assert.Same(sentinel, resources["OwnerResource"]);
                Assert.Equal(next.Text, Assert.IsAssignableFrom<ISolidColorBrush>(retainedBrush).Color);
                Assert.Equal("Keep my edit", text.Text); Assert.Equal("my", text.SelectedText);
                Assert.True(text.IsKeyboardFocusWithin);
            }
            finally { window.Close(); CuiPropertyPresentation.Detach(text); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Accessibility_resource_change_does_not_flash_hover_background_through_base_style()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, _) = loader.LoadMarkup("<Page><Button Content='Stable hover'/></Page>");
            var window = new Window { Content = root, Width = 300, Height = 120 };
            try
            {
                window.Show(); using (var frame = window.CaptureRenderedFrame()) { }
                var button = Assert.Single(root!.GetVisualDescendants().OfType<Button>());
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                Assert.True(button.IsPointerOver);
                var before = button.Background;
                var observed = new List<IBrush?>();
                button.PropertyChanged += (_, change) => { if (change.Property == Button.BackgroundProperty) observed.Add(button.Background); };
                button.Resources["CuiFocusIndicatorThickness"] = 4d;
                using (var frame = window.CaptureRenderedFrame()) { }
                Assert.Same(before, button.Background);
                Assert.Empty(observed);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Focus_indicator_does_not_resize_controls_or_move_adjacent_targets()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("<Page><Container Type='Horizontal Stack'><Button Content='First'/><Button Content='Second'/></Container></Page>");
            Assert.NotNull(root);
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            var window = new Window { Content = root, Width = 500, Height = 160 };
            try
            {
                window.Show(); using (var frame = window.CaptureRenderedFrame()) { }
                var buttons = root!.GetVisualDescendants().OfType<Button>().ToArray();
                var before = buttons.Select(button => button.Bounds).ToArray();
                var widths = buttons.Select(button => button.BorderThickness).ToArray();
                Assert.True(buttons[0].Focus(NavigationMethod.Tab));
                using (var frame = window.CaptureRenderedFrame()) { }
                Assert.Equal(before, buttons.Select(button => button.Bounds).ToArray());
                Assert.Equal(widths, buttons.Select(button => button.BorderThickness).ToArray());
                var adorners = AdornerLayer.GetAdornerLayer(buttons[0])!;
                var ring = Assert.Single(adorners.Children.OfType<Border>());
                Assert.NotNull(ring.BorderBrush);
                Assert.True(ring.BorderThickness.Left >= 2);
                Assert.True(buttons[1].Focus(NavigationMethod.Pointer));
                using (var frame = window.CaptureRenderedFrame()) { }
                Assert.Equal(before, buttons.Select(button => button.Bounds).ToArray());
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public void Failed_theme_observer_cannot_block_other_views_and_can_retry_same_revision()
    {
        var source = new Source(new("Professional", 0, "local", false, new()));
        using var context = new CuiThemePreferenceContext(source);
        var fail = true;
        var firstCalls = 0;
        var healthyCalls = 0;
        context.Changed += (_, _) => { ++firstCalls; if (fail) throw new InvalidOperationException("private diagnostic"); };
        context.Changed += (_, _) => ++healthyCalls;
        var next = new CuiThemePreferenceSnapshot("Glow", 1, "local", false, new());
        source.Set(next);
        Assert.Equal("Glow", context.Current.Theme.Name);
        Assert.Equal(1, healthyCalls);
        Assert.Equal(1, context.ObserverFailureCount);
        Assert.Equal("CUI_THEME_OBSERVER_FAILED", context.ObserverDiagnostic);
        fail = false;
        source.Set(next);
        Assert.Equal(2, firstCalls);
        Assert.Equal(1, healthyCalls);
        Assert.Null(context.ObserverDiagnostic);
        source.Set(next with { Name = "Bubble", Revision = 2 });
        Assert.Equal(3, firstCalls);
        Assert.Equal(2, healthyCalls);
        source.Set(next); // A stale observation still cannot roll the scope back.
        Assert.Equal("Bubble", context.Current.Theme.Name);
    }

    [Fact]
    public void One_typed_cascade_restores_runtime_assignment_when_overlay_ends()
    {
        var store = new CuiEffectivePropertyStore(); store.Register("Opacity",typeof(double),1d);
        foreach(var (level,value) in new[] { (CuiPropertyLevel.Theme,.9), (CuiPropertyLevel.Definition,.8),
            (CuiPropertyLevel.Instance,.7), (CuiPropertyLevel.Region,.6), (CuiPropertyLevel.Overlay,.5) })
            store.Set("Opacity",value,level,level.ToString());
        Assert.Equal(.5,store.Resolve("Opacity").Value); Assert.Equal(CuiPropertyLevel.Overlay,store.Resolve("Opacity").Level);
        store.Set("Opacity",.3,CuiPropertyLevel.Instance,"Instance");
        Assert.Equal(.5,store.Resolve("Opacity").Value);
        store.Remove("Opacity",CuiPropertyLevel.Overlay,"Overlay"); Assert.Equal(.6,store.Resolve("Opacity").Value);
        store.Remove("Opacity",CuiPropertyLevel.Region,"Region"); Assert.Equal(.3,store.Resolve("Opacity").Value);
        Assert.Equal("Instance",store.Resolve("Opacity").Source);
        Assert.Throws<ArgumentException>(()=>store.Set("Opacity","wrong",CuiPropertyLevel.Instance,"Instance"));
    }

    [Fact]
    public void Equal_level_source_order_does_not_change_with_notification_timing()
    {
        var store=new CuiEffectivePropertyStore(); store.Register("Opacity",typeof(double),1d);
        store.Set("Opacity",.8,CuiPropertyLevel.Region,"early",1); store.Set("Opacity",.7,CuiPropertyLevel.Region,"later",2);
        store.Set("Opacity",.5,CuiPropertyLevel.Region,"early",1);
        Assert.Equal(.7,store.Resolve("Opacity").Value);
        store.Remove("Opacity",CuiPropertyLevel.Region,"later"); Assert.Equal(.5,store.Resolve("Opacity").Value);
    }

    [Fact]
    public void All_themes_publish_exact_retained_aliases_and_accessible_component_metrics()
    {
        foreach(var theme in new[] { CuiTheme.Professional,CuiTheme.Glow,CuiTheme.Bubble,CuiTheme.Playful,CuiTheme.Cinematic })
        {
            var resources=new ResourceDictionary();
            CuiThemeResourceApplier.ApplyToResources(resources,CuiSurfacePaletteCatalog.For("Home",CuiAppearance.Dark,theme),
                new() { InterfaceFontFamilyOverride="Atkinson Hyperlegible",DisplayScale=1.5,ReduceMotion=true });
            Assert.Contains("Atkinson Hyperlegible",Assert.IsType<FontFamily>(resources["CuiInterfaceFontFamily"]).Name);
            Assert.Equal(1.5,resources["CuiDisplayScale"]); Assert.Equal(0d,resources["CuiMotionDurationScale"]);
            Assert.True(Assert.IsType<double>(resources["CuiControlMinHeight"])>=51);
            foreach(var (legacy,canonical) in new[] { ("HavenTextPrimaryBrush","CuiTextBrush"),("HavenTextSecondaryBrush","CuiTextSoftBrush"),
                ("HavenTextMutedBrush","CuiMutedBrush"),("HavenBorderSubtleBrush","CuiLineBrush"),("HavenCardSurfaceBrush","CuiPanelBrush"),
                ("HavenOverlaySurfaceBrush","CuiOverlaySurfaceBrush"),("HavenShadowBrush","CuiShadowBrush"),
                ("HavenAccentPrimaryHoverBrush","CuiAccentPrimaryHoverBrush"),("HavenButtonTextPrimaryBrush","CuiButtonForegroundBrush") })
                Assert.Same(resources[canonical],resources[legacy]);
        }
    }

    [Fact]
    public void Home_source_custom_cache_fallback_and_scopes_remain_explicit()
    {
        var catalog=new CuiThemeDefinitionCatalog(); catalog.Add(new("Custom", "Glow",[new("Button",null,new Dictionary<string,object?> { ["CornerRadius"]=3d })]));
        var source=new Source(new("Custom",2,"profile-A",true,new() { ReduceMotion=true }));
        using var context=new CuiThemePreferenceContext(source,catalog);
        Assert.Equal("Custom",context.Current.Theme.Name); Assert.True(context.Current.FromCache); Assert.True(context.Current.Accessibility.ReduceMotion);
        Assert.Equal("Bubble",CuiThemeScope.Resolve("Bubble",catalog,context).Theme.Name);
        Assert.Equal("Custom",CuiThemeScope.Resolve("user_preference",catalog,context).Theme.Name);
        source.Set(new("Missing",3,"profile-A",true,new() { ReduceMotion=true }));
        Assert.True(context.Current.UsedFallback); Assert.Equal(CuiTheme.Professional,context.Current.Theme.BaseTheme); Assert.NotNull(context.Current.Diagnostic);
        source.Set(new("Glow",1,"profile-A",false,new())); Assert.True(context.Current.UsedFallback);
        source.Set(new("Bubble",1,"profile-B",false,new())); Assert.Equal("Bubble",context.Current.Theme.Name); Assert.Equal("profile-B",context.Current.ScopeId);
    }

    [Fact]
    public async Task Busy_gate_retheme_and_release_restore_enabled_presentation()
    {
        await using var session=HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(()=>
        {
            var button=new Button { Content="Save appearance" };
            var window=new Window { Content=button,Width=400,Height=200 };
            try
            {
                window.Show();window.UpdateLayout();CuiLifecycleProperties.Attach(button);
                CuiPropertyPresentation.Set(button,"Interactive",false,CuiPropertyLevel.Instance,"Settings busy");
                CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Glow);window.UpdateLayout();
                Assert.False(button.IsEffectivelyEnabled);Assert.True(button.Opacity<1);
                CuiPropertyPresentation.Set(button,"Interactive",true,CuiPropertyLevel.Instance,"Settings busy");
                Assert.True(button.IsEffectivelyEnabled);
                Assert.Equal(1d,button.Opacity);
                Assert.NotEqual("CUI interaction state",CuiPropertyPresentation.For(button).Resolve("Opacity").Source);
                using(var denied=CuiLifecycleProperties.RestrictInteraction(button))
                { Assert.False(button.IsEffectivelyEnabled);Assert.True(button.Opacity<1); }
                Assert.True(button.IsEffectivelyEnabled);Assert.Equal(1d,button.Opacity);
            }
            finally {window.Close();CuiLifecycleProperties.Detach(button);CuiPropertyPresentation.Detach(button);}
        },CancellationToken.None);
    }

    [Fact]
    public async Task Private_scrollbar_parts_are_compact_and_follow_real_scroll_position()
    {
        await using var session=HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(()=>
        {
            var scroll=new ScrollViewer { Content=new Border { Width=1000,Height=1800 },
                HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
            var window=new Window { Content=scroll,Width=400,Height=300 };
            try
            {
                window.Show();window.UpdateLayout();
                var bars=scroll.GetVisualDescendants().OfType<ScrollBar>().ToArray();
                var vertical=Assert.Single(bars,bar=>bar.Orientation==Avalonia.Layout.Orientation.Vertical);
                var horizontal=Assert.Single(bars,bar=>bar.Orientation==Avalonia.Layout.Orientation.Horizontal);
                Assert.InRange(vertical.Bounds.Width,10,14);Assert.InRange(horizontal.Bounds.Height,10,14);
                var track=Assert.Single(vertical.GetVisualDescendants().OfType<Track>());
                Assert.Equal(0,track.Thumb!.Bounds.Top);
                Assert.Equal(0,((ISolidColorBrush)track.IncreaseButton!.Background!).Color.A);
                Assert.Equal(new Thickness(0),track.IncreaseButton.Padding);Assert.Equal(0,track.IncreaseButton.MinHeight);
                scroll.Offset=new Vector(0,300);window.UpdateLayout();
                Assert.True(track.Thumb.Bounds.Top>0);Assert.Equal(300,vertical.Value);
                CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Glow);window.UpdateLayout();
                Assert.InRange(vertical.Bounds.Width,10,14);Assert.Equal(0,((ISolidColorBrush)track.IncreaseButton.Background!).Color.A);
                Assert.Equal(300,scroll.Offset.Y);
            }
            finally {window.Close();foreach(var control in scroll.GetVisualDescendants().OfType<Control>())CuiPropertyPresentation.Detach(control);}
        },CancellationToken.None);
    }

    [Fact]
    public async Task CUI_templates_render_and_edit_without_a_donor_theme_and_retheme_preserves_input()
    {
        await using var session=HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(()=>
        {
            Assert.DoesNotContain(Application.Current!.Styles,style=>style.GetType().Name is "FluentTheme" or "SimpleTheme");
            var text=new TextBox { Text="preserve this content",Width=320 };
            var button=new Button { Content="Action" };
            var check=new CheckBox { Content="Checked",IsChecked=true };
            var list=new ComboBox { ItemsSource=new[] {"One","Two"},SelectedIndex=0 };
            var root=new StackPanel { Children={button,text,check,list,new Slider { Value=25 },new NumericUpDown { Value=7 }} };
            var window=new Window { Content=root,Width=800,Height=600 };
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.NotNull(text.GetVisualDescendants().OfType<TextPresenter>().SingleOrDefault());
                Assert.True(button.Bounds.Width>0 && button.Bounds.Height>0);
                text.Focus(); text.SelectionStart=3; text.SelectionEnd=8;
                var selected=text.SelectedText;
                CuiPropertyPresentation.Set(button,"Background",Brushes.Red,CuiPropertyLevel.Instance,"authored");
                button.Background=Brushes.Blue;
                Assert.Equal(Brushes.Red,button.Background); // Competing native local value is not another cascade.
                foreach(var theme in new[] {CuiTheme.Glow,CuiTheme.Bubble,CuiTheme.Playful,CuiTheme.Cinematic,CuiTheme.Professional})
                {
                    CuiThemeScopeApplier.ApplyGlobalTheme(theme);
                    window.UpdateLayout(); Assert.Equal("preserve this content",text.Text); Assert.Equal(selected,text.SelectedText);
                    Assert.True(text.IsKeyboardFocusWithin); Assert.Same(Brushes.Red,button.Background);
                }
                window.KeyTextInput("typed"); Assert.Contains("typed",text.Text);
                list.IsDropDownOpen=true; window.UpdateLayout(); Assert.True(list.IsDropDownOpen); list.IsDropDownOpen=false;
                CuiPropertyPresentation.SetSelected(button,true); Assert.Equal(true,CuiPropertyPresentation.For(button).Resolve("Selected").Value);
            }
            finally { window.Close(); foreach(var control in root.GetVisualDescendants().OfType<Control>()) CuiPropertyPresentation.Detach(control); }
        },CancellationToken.None);
    }
    private sealed class Source(CuiThemePreferenceSnapshot current):ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; }=current;
        public event EventHandler? Changed;
        public void Set(CuiThemePreferenceSnapshot value) { Current=value; Changed?.Invoke(this,EventArgs.Empty); }
    }
}
