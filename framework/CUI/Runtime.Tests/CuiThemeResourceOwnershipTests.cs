using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiThemeResourceOwnershipTests
{
    [Fact]
    public void Scoped_palettes_keep_native_owners_and_mutable_gradient_aliases_isolated_during_live_refresh()
    {
        var source = new ThemeSource();
        using var preference = new CuiThemePreferenceContext(source);
        using var loader = new CuiControlLoader();
        loader.SetThemePreferenceContext(preference);
        var loaded = loader.LoadMarkup("""
            <Container Type="Horizontal Stack">
              <Button ID="first" Content="First" />
              <Button ID="second" Content="Second" />
              <Theme Name="Bubble"><Button ID="fixed" Content="Fixed" /></Theme>
            </Container>
            """);
        Assert.Empty(loaded.Diagnostics);
        var root = Assert.IsType<CuiContainerControl>(loaded.Root);
        var first = Assert.IsType<Button>(root.AuthoredChildren[0]);
        var second = Assert.IsType<Button>(root.AuthoredChildren[1]);
        var fixedScope = Assert.IsType<Button>(root.AuthoredChildren[2]);
        var firstProvider = Assert.Single(first.Resources.MergedDictionaries);
        var secondProvider = Assert.Single(second.Resources.MergedDictionaries);
        Assert.Same(first, firstProvider.Owner);
        Assert.Same(second, secondProvider.Owner);
        Assert.NotSame(firstProvider, secondProvider);

        var firstAccent = Resource<LinearGradientBrush>(first, "CuiAccentBrush");
        var secondAccent = Resource<LinearGradientBrush>(second, "CuiAccentBrush");
        Assert.Same(firstAccent, Resource<LinearGradientBrush>(first, "HavenAccentBrush"));
        Assert.Same(secondAccent, Resource<LinearGradientBrush>(second, "HavenAccentBrush"));
        Assert.NotSame(firstAccent, secondAccent);
        var originalSecond = secondAccent.GradientStops.Select(stop => (stop.Color, stop.Offset)).ToArray();
        firstAccent.GradientStops[0].Color = Colors.Magenta;
        firstAccent.GradientStops.Add(new GradientStop(Colors.Cyan, .123));
        Assert.Equal(originalSecond, secondAccent.GradientStops.Select(stop => (stop.Color, stop.Offset)).ToArray());

        // A local resource override remains local even though palette lookup is shared.
        first.Resources["CuiTextBrush"] = Brushes.Orange;
        Assert.Same(Brushes.Orange, first.Foreground);
        Assert.NotSame(Brushes.Orange, second.Foreground);
        source.Change("Glow");
        Assert.Null(firstProvider.Owner);
        Assert.Null(secondProvider.Owner);
        Assert.Same(first, Assert.Single(first.Resources.MergedDictionaries).Owner);
        Assert.Equal("Glow", Resource<string>(first, "CuiEffectiveThemeName"));
        Assert.Equal("Glow", Resource<string>(second, "CuiEffectiveThemeName"));
        Assert.Equal("Bubble", Resource<string>(fixedScope, "CuiEffectiveThemeName"));
        Assert.Same(Brushes.Orange, first.Foreground);
        var refreshedAccent = Resource<LinearGradientBrush>(first, "CuiAccentBrush");
        Assert.NotSame(firstAccent, refreshedAccent);
        Assert.Same(refreshedAccent, Resource<LinearGradientBrush>(first, "HavenAccentBrush"));
        Assert.DoesNotContain(refreshedAccent.GradientStops, stop => stop.Offset == .123);
        Assert.NotSame(refreshedAccent, Resource<LinearGradientBrush>(second, "CuiAccentBrush"));
    }

    private static T Resource<T>(Control control, string key)
    {
        Assert.True(control.Resources.TryGetResource(key, null, out var value));
        return Assert.IsAssignableFrom<T>(value);
    }

    private sealed class ThemeSource : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } = new("Professional", 1, "test-user", true, CuiAccessibilitySettings.Default);
        public event EventHandler? Changed;
        public void Change(string name)
        { Current = Current with { Name = name, Revision = Current.Revision + 1 }; Changed?.Invoke(this, EventArgs.Empty); }
    }
}
