using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiThemeRecoveryTests
{
    [Fact]
    public async Task Worker_thread_theme_failure_is_observed_and_same_revision_can_retry()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var source = new ThemeSource();
            using var preference = new CuiThemePreferenceContext(source);
            var failing = false;
            var registry = new CuiControlRegistry();
            registry.RegisterImageSourceResolver((_, _) => failing
                ? throw new InvalidOperationException("Transient renderer failure")
                : new DrawingImage(new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 24, 24)) }));
            using var loader = new CuiControlLoader(registry);
            loader.SetThemePreferenceContext(preference);
            var loaded = loader.LoadMarkup("<Image Source='icon:retry'/>");
            Assert.Empty(loaded.Diagnostics);
            var image = Assert.IsType<Image>(loaded.Root);
            var before = image.Source;
            failing = true;
            await Task.Run(() => source.Set(source.Current with { Name = "Glow", Revision = 2 }));
            Assert.Equal(1, preference.ObserverFailureCount);
            Assert.Same(before, image.Source);
            failing = false;
            await Task.Run(() => source.Set(source.Current));
            Assert.NotSame(before, image.Source);
            Assert.Null(preference.ObserverDiagnostic);
        }, CancellationToken.None);
    }

    [Fact]
    public void Failing_control_does_not_leave_other_controls_on_the_previous_theme_and_retries()
    {
        var source = new ThemeSource();
        using var preference = new CuiThemePreferenceContext(source);
        var failing = false;
        var registry = new CuiControlRegistry();
        registry.RegisterImageSourceResolver((_, target) =>
        {
            if (failing && target.Name == "first") throw new InvalidOperationException("Transient renderer failure");
            target.TryFindResource("CuiTextBrush", out var brush);
            return new DrawingImage(new GeometryDrawing { Brush = (IBrush?)brush, Geometry = new RectangleGeometry(new Rect(0, 0, 24, 24)) });
        });
        using var loader = new CuiControlLoader(registry);
        loader.SetThemePreferenceContext(preference);
        var loaded = loader.LoadMarkup("<Container Type='Horizontal Stack'><Image id='first' Source='icon:first'/><Image id='second' Source='icon:second'/></Container>");
        Assert.Empty(loaded.Diagnostics);
        var root = Assert.IsType<CuiContainerControl>(loaded.Root);
        var first = Assert.IsType<Image>(root.AuthoredChildren[0]);
        var second = Assert.IsType<Image>(root.AuthoredChildren[1]);
        var firstImage = first.Source;
        var secondImage = second.Source;
        failing = true;
        source.Set(source.Current with { Name = "Glow", Revision = 2 });
        Assert.Equal(1, preference.ObserverFailureCount);
        Assert.Same(firstImage, first.Source);
        Assert.NotSame(secondImage, second.Source);
        Assert.True(second.TryFindResource("CuiEffectiveThemeName", out var name));
        Assert.Equal("Glow", name);
        failing = false;
        source.Set(source.Current);
        Assert.NotSame(firstImage, first.Source);
        Assert.Null(preference.ObserverDiagnostic);
    }

    [Fact]
    public async Task Tooltip_uses_the_shared_floating_surface_and_keeps_the_owners_theme()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("<Theme Name='Bubble'><Button Content='Save' ToolTip='Save (Ctrl+S)'/></Theme>");
            Assert.Empty(diagnostics);
            var button = Assert.IsType<Button>(root);
            var tooltip = new ToolTip { Content = ToolTip.GetTip(button) };
            ToolTip.SetTip(button, tooltip);
            var window = new Window { Content = root, Width = 500, Height = 250 };
            window.Show();
            try
            {
                window.UpdateLayout();
                ToolTip.SetIsOpen(button, true);
                window.UpdateLayout();
                tooltip.ApplyTemplate();
                var frame = Assert.Single(tooltip.GetVisualChildren().OfType<Border>());
                Assert.True(button.TryFindResource("CuiPopupSurfaceBrush", out var background));
                Assert.Equal(((ISolidColorBrush)background!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(frame.Background).Color);
                Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(frame.Background).Color.A);
                Assert.True(button.TryFindResource("CuiPanel2Brush", out var panel));
                var panelColour = ((ISolidColorBrush)panel!).Color;
                Assert.Equal(Color.FromArgb(255, panelColour.R, panelColour.G, panelColour.B), ((ISolidColorBrush)frame.Background!).Color);
                Assert.True(frame.Padding.Left > 0 && frame.Padding.Top > 0);
                Assert.True(frame.CornerRadius.TopLeft > 0);
                Assert.Equal("Save (Ctrl+S)", tooltip.Content);
                ToolTip.SetIsOpen(button, false);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class ThemeSource : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } = new("Professional", 1, "theme-recovery", true, CuiAccessibilitySettings.Default);
        public event EventHandler? Changed;
        public void Set(CuiThemePreferenceSnapshot value) { Current = value; Changed?.Invoke(this, EventArgs.Empty); }
    }
}
