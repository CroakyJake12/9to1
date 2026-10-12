using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiCustomThemeTests
{
    [Fact]
    public void Portable_library_round_trips_canonically_and_copies_caller_stops()
    {
        var stops = new[] { new CuiCustomGradientStop(0, new("#123456", 20, 4)), new CuiCustomGradientStop(1, new("#ABCDEF", 40, 12)) };
        var theme = CuiCustomThemeContract.CreateDefault() with
        { PrimaryAccent = new(CuiCustomFillKind.Gradient, new("#123456"), new(CuiCustomGradientKind.Radial, stops, Radius: .7)) };
        var frozen = theme.Validate(); var original = CuiCustomThemeContract.SerializeLibrary([frozen]);
        stops[0] = new(0, new("#FFFFFF"));
        Assert.Equal("#123456", frozen.PrimaryAccent.Gradient!.Stops[0].Colour.Hex);
        var restored = Assert.Single(CuiCustomThemeContract.DeserializeLibrary(original));
        Assert.Equal(original, CuiCustomThemeContract.SerializeLibrary([restored]));
        Assert.Equal(12, restored.PrimaryAccent.Gradient!.Stops[1].Colour.BackgroundBlur);
        Assert.Equal(40, restored.PrimaryAccent.Gradient.Stops[1].Colour.TransparencyPercent);
    }

    [Fact]
    public void Invalid_untrusted_contracts_fail_without_catalog_mutation()
    {
        var theme = CuiCustomThemeContract.CreateDefault(); var catalog = new CuiThemeDefinitionCatalog();
        catalog.UpsertCustomTheme(theme); var original = catalog.Resolve(theme.Name).CustomFingerprint;
        foreach (var invalid in new[] {
            theme with { Name = "Professional" }, theme with { Id = "../account" },
            theme with { ButtonRoundnessPercent = double.NaN }, theme with { BaseTheme = (CuiTheme)999 },
            theme with { Fonts = theme.Fonts with { Code = new("RemoteFont", 400, 12) } },
            theme with { Fonts = theme.Fonts with { Main = new("Inter", 900, 12) } },
            theme with { PrimaryAccent = new(CuiCustomFillKind.SolidColour, new("url(secret)")) },
            theme with { PrimaryAccent = new(CuiCustomFillKind.SolidColour, new("#112233", 120)) },
            theme with { PrimaryAccent = new(CuiCustomFillKind.Gradient, new("#112233")) }
        }) Assert.ThrowsAny<ArgumentException>(() => catalog.UpsertCustomTheme(invalid));
        Assert.Equal(original, catalog.Resolve(theme.Name).CustomFingerprint);
        Assert.Throws<CuiCustomThemeException>(() => CuiCustomThemeContract.Deserialize(CuiCustomThemeContract.Serialize(theme).Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"script\":\"exec()\"")));
        Assert.Throws<CuiCustomThemeException>(() => CuiCustomThemeContract.SerializeLibrary([theme, theme with { Id = Guid.NewGuid().ToString("D") }]));
    }

    [Fact]
    public void Same_name_preview_changes_notify_without_fabricating_account_revisions()
    {
        var theme = CuiCustomThemeContract.CreateDefault(); var catalog = new CuiThemeDefinitionCatalog();
        var source = new Source(new("Professional", 8, "account:test", false, CuiAccessibilitySettings.Default));
        using var context = new CuiThemePreferenceContext(source, catalog); var changes = 0;
        context.Changed += (_, _) => ++changes;
        catalog.UpsertCustomTheme(theme); var first = context.ResolveScope(theme.Name).Theme.CustomFingerprint;
        catalog.UpsertCustomTheme(theme with { ButtonRoundnessPercent = 90 });
        Assert.Equal(2, changes); Assert.Equal(8, context.Current.Revision);
        Assert.NotEqual(first, context.ResolveScope(theme.Name).Theme.CustomFingerprint);
        catalog.RemoveCustomTheme(theme.Id); Assert.True(context.ResolveScope(theme.Name).UsedFallback);
    }

    [Fact]
    public void Account_observation_reconciles_library_without_dropping_private_preview()
    {
        var saved = CuiCustomThemeContract.CreateDefault("Saved"); var preview = CuiCustomThemeContract.CreateDefault("Preview");
        var catalog = new CuiThemeDefinitionCatalog(); catalog.UpsertCustomTheme(preview);
        var source = new Source(new(saved.Name, 1, "account:test", true, CuiAccessibilitySettings.Default,
            CustomThemesJson: CuiCustomThemeContract.SerializeLibrary([saved])));
        using var context = new CuiThemePreferenceContext(source, catalog);
        Assert.Equal(saved.Id, context.Current.Theme.CustomTheme!.Id);
        source.Publish(source.Current with { Revision = 2, Name = "Professional", CustomThemesJson = "[]" });
        Assert.False(catalog.TryResolve(saved.Name, out _)); Assert.True(catalog.TryResolve(preview.Name, out _));
        source.Publish(source.Current with { Revision = 1, Name = saved.Name, CustomThemesJson = CuiCustomThemeContract.SerializeLibrary([saved]) });
        Assert.False(catalog.TryResolve(saved.Name, out _)); Assert.Equal(2, context.Current.Revision);
    }

    [Fact]
    public async Task Real_CUI_controls_use_separate_font_and_accent_roles_and_size_relative_roundness()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("<Page><Container Type='Vertical Stack'>" +
                "<Button Content='Primary' AccentRole='Primary' Width='160' Height='40'/>" +
                "<Button Content='Secondary' AccentRole='Secondary' Width='160' Height='60'/>" +
                "<Text Text='Heading' FontRole='Heading'/><Text Text='Explicit' FontRole='Heading' FontSize='11'/>" +
                "</Container></Page>");
            Assert.Empty(diagnostics); Assert.NotNull(root);
            var theme = CuiCustomThemeContract.CreateDefault() with { ButtonRoundnessPercent = 100 };
            theme = theme with { Fonts = theme.Fonts with { PrimaryButton = new("Inter", 700, 18), SecondaryButton = new("Montserrat", 500, 13), Heading = new("Inter", 600, 31) } };
            // Exercise the shared projection directly; root owns the loader integration.
            foreach (var control in root!.GetVisualDescendants().Prepend(root).OfType<Control>())
                CuiCustomThemeResourceApplier.ApplyToResources(control.Resources, theme);
            var window = new Window { Content = root, Width = 400, Height = 400 };
            try
            {
                window.Show(); window.UpdateLayout();
                var buttons = root.GetVisualDescendants().OfType<Button>().ToArray();
                Assert.Equal(18, buttons[0].FontSize); Assert.Equal(13, buttons[1].FontSize);
                Assert.Equal(20, buttons[0].CornerRadius.TopLeft); Assert.Equal(30, buttons[1].CornerRadius.TopLeft);
                Assert.NotEqual(((ISolidColorBrush)buttons[0].Background!).Color, ((ISolidColorBrush)buttons[1].Background!).Color);
                Assert.Equal(31, root.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Heading").FontSize);
                Assert.Equal(11, root.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Explicit").FontSize);
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(buttons[0].FontFamily, weight: buttons[0].FontWeight), out var face));
                Assert.Equal("Inter", face!.FamilyName);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(CuiCustomGradientKind.Linear)] [InlineData(CuiCustomGradientKind.Radial)] [InlineData(CuiCustomGradientKind.Conic)]
    public void Gradient_resources_preserve_type_alpha_stops_and_orientation(CuiCustomGradientKind kind)
    {
        var gradient = new CuiCustomGradient(kind, [new(0, new("#123456", 20)), new(.4, new("#ABCDEF", 60)), new(1, new("#112233"))], 35, .3, .7, .8);
        var brush = CuiCustomThemeResourceApplier.CreateBrush(new(CuiCustomFillKind.Gradient, new("#123456"), gradient));
        var actual = Assert.IsAssignableFrom<IGradientBrush>(brush);
        Assert.Equal(3, actual.GradientStops.Count); Assert.Equal(102, actual.GradientStops[1].Color.A);
        Assert.Equal(.4, actual.GradientStops[1].Offset);
        Assert.True(kind switch { CuiCustomGradientKind.Linear => brush is ILinearGradientBrush, CuiCustomGradientKind.Radial => brush is IRadialGradientBrush, _ => brush is IConicGradientBrush });
    }

    [Theory]
    [InlineData(CuiCustomPatternKind.Stripes)] [InlineData(CuiCustomPatternKind.Checkerboard)] [InlineData(CuiCustomPatternKind.Dots)]
    public void Pattern_resources_contain_real_tiling_geometry(CuiCustomPatternKind kind)
    {
        var brush = Assert.IsType<DrawingBrush>(CuiCustomThemeResourceApplier.CreateBrush(new(CuiCustomFillKind.Pattern, new("#123456"),
            Pattern: new(kind, new("#ABCDEF", 25), new("#112233"), 6, 3, 20))));
        Assert.Equal(TileMode.Tile, brush.TileMode);
        Assert.Equal(kind == CuiCustomPatternKind.Checkerboard ? 3 : 2, Assert.IsType<DrawingGroup>(brush.Drawing).Children.Count);
        Assert.Equal(kind == CuiCustomPatternKind.Checkerboard ? 18 : 9, brush.DestinationRect.Rect.Width);
    }

    private sealed class Source(CuiThemePreferenceSnapshot current) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } = current;
        public event EventHandler? Changed;
        public void Publish(CuiThemePreferenceSnapshot next) { Current = next; Changed?.Invoke(this, EventArgs.Empty); }
    }
}
