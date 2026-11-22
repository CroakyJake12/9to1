using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CakeOS.Cui.Themes;

/// <summary>Shared preset surfaces, including native frosted materials and opaque popup fallbacks.</summary>
internal static class CuiPresetSurfaceResources
{
    public static void Apply(IResourceDictionary resources, CuiPalette palette, CuiAccessibilitySettings settings)
    {
        if (settings.HighContrast) return;
        if (palette.Theme == CuiTheme.Bubble)
        {
            resources["CuiWorkspaceBrush"] = Gradient(30, (0, palette.TideColour), (1, palette.TideBase));
            resources["CuiWorkspaceMaterial"] = resources["CuiWorkspaceBrush"];
            return;
        }
        if (palette.Theme is not (CuiTheme.FrostedGlass or CuiTheme.Metallic or CuiTheme.Western)) return;
        var white = Colors.White;
        var black = Colors.Black;
        var dark = palette.TideBase.ToHsl().L < .5;
        if (palette.Theme == CuiTheme.FrostedGlass)
        {
            resources["CuiWorkspaceBrush"] = Gradient(40, (0, palette.TideColour), (1, palette.TideBase));
            resources["CuiWorkspaceMaterial"] = resources["CuiWorkspaceBrush"];
            var pane = new CuiMaterialBrush(palette.Panel, 1, 32);
            var floating = new CuiMaterialBrush(palette.Panel2, 1, 30);
            resources["CuiShellMaterial"] = pane;
            resources["CuiChromeMaterial"] = pane;
            resources["CuiSurfaceMaterial"] = pane;
            resources["CuiFloatingSurfaceMaterial"] = floating;
            resources["CuiPopupSurfaceBrush"] = floating;
            resources["CuiPopupOpaqueFallbackBrush"] = new SolidColorBrush(Composite(palette.Panel2, palette.TideBase));
            return;
        }
        switch (palette.Theme)
        {
            case CuiTheme.Metallic:
                resources["CuiWorkspaceBrush"] = Gradient(90, (0, palette.TideColour), (.5, palette.TideBase), (1, palette.TideColour));
                resources["CuiPanel2Brush"] = Gradient(90, (0, Mix(palette.Panel2, white, .06)), (.48, palette.Panel2), (1, Mix(palette.Panel2, black, .045)));
                resources["CuiButtonBrush"] = Metal(palette.Button);
                resources["CuiButtonHoverBrush"] = Metal(palette.ButtonHover);
                resources["CuiButtonPressedBrush"] = Metal(palette.ButtonPressed);
                break;
            case CuiTheme.Western:
                resources["CuiWorkspaceBrush"] = Gradient(30, (0, palette.TideColour), (1, palette.TideBase));
                resources["CuiPanel2Brush"] = Gradient(90, (0, palette.Panel2), (1, Mix(palette.Panel2, palette.Panel3, .18)));
                resources["CuiButtonBrush"] = Leather(palette.Button);
                resources["CuiButtonHoverBrush"] = Leather(palette.ButtonHover);
                resources["CuiButtonPressedBrush"] = Leather(palette.ButtonPressed);
                break;
        }

        // Material names are semantic surface roles. Only Professional opts in
        // to implicit Mica; these presets use the exact ordinary brush objects.
        resources["CuiWorkspaceMaterial"] = resources["CuiWorkspaceBrush"];
        resources["CuiShellMaterial"] = resources["CuiBackgroundBrush"];
        resources["CuiSurfaceMaterial"] = resources["CuiPanelBrush"];
        resources["CuiFloatingSurfaceMaterial"] = resources["CuiPanel2Brush"];
        resources["CuiPopupSurfaceBrush"] = Gradient(90,
            (0, Opaque(Mix(palette.Panel2, white, dark ? .04 : .12))), (1, Opaque(palette.Panel2)));
        // Opaque solid fallback for transparency negotiation, independent of the gradient surface.
        resources["CuiPopupOpaqueFallbackBrush"] = new SolidColorBrush(Opaque(palette.Panel2));
        ApplyForeground(resources, palette);

        IBrush Metal(Color colour) => Gradient(90, (0, Mix(colour, white, .10)), (.43, colour),
            (.47, Mix(colour, black, .08)), (1, Mix(colour, white, .035)));
        IBrush Leather(Color colour) => Gradient(90, (0, Mix(colour, white, .04)), (1, Mix(colour, black, .065)));
    }

    public static void ApplyForeground(IResourceDictionary resources, CuiPalette palette)
    {
        if (palette.Theme is not (CuiTheme.FrostedGlass or CuiTheme.Metallic or CuiTheme.Western)) return;
        var panel = Composite(palette.Panel, palette.TideBase);
        foreach (var state in new[] { "", "Hover", "Pressed" })
        {
            if (resources[$"CuiButton{state}Brush"] is not IGradientBrush brush) continue;
            var samples = brush.GradientStops.Select(stop => Composite(stop.Color, panel)).ToArray();
            var candidates = new[] { palette.Text, palette.AccentInk, Colors.Black, Colors.White };
            var ink = samples.Min(sample => CuiContrast.Ratio(palette.Text, sample)) >= 4.5 ? palette.Text :
                candidates.OrderByDescending(colour => samples.Min(sample => CuiContrast.Ratio(colour, sample))).First();
            if (brush is ILinearGradientBrush linear && samples.Any(sample => CuiContrast.Ratio(ink, sample) < 4.5))
            {
                var accessible = new LinearGradientBrush { StartPoint = linear.StartPoint, EndPoint = linear.EndPoint };
                foreach (var stop in linear.GradientStops)
                    accessible.GradientStops.Add(new GradientStop(ReadableSurface(stop.Color, panel, ink), stop.Offset));
                resources[$"CuiButton{state}Brush"] = accessible.ToImmutable();
            }
            resources[$"CuiButton{state}ForegroundBrush"] = new SolidColorBrush(ink).ToImmutable();
            resources[$"HavenButton{state}ForegroundBrush"] = resources[$"CuiButton{state}ForegroundBrush"];
        }
    }

    private static Color Composite(Color foreground, Color background) =>
        Mix(background, Opaque(foreground), foreground.A / 255d);

    private static Color ReadableSurface(Color colour, Color background, Color ink)
    {
        if (CuiContrast.Ratio(ink, Composite(colour, background)) >= 4.5) return colour;
        var target = CuiContrast.Ratio(ink, Colors.Black) >= CuiContrast.Ratio(ink, Colors.White) ? Colors.Black : Colors.White;
        // Bounded search keeps authored opacity and changes only the stop's
        // necessary tonal distance, measured after composition with the panel.
        var low = 0d; var high = 1d;
        for (var i = 0; i < 16; ++i)
        {
            var middle = (low + high) / 2;
            if (CuiContrast.Ratio(ink, Composite(Mix(colour, target, middle), background)) >= 4.5) high = middle;
            else low = middle;
        }
        return Mix(colour, target, high);
    }

    private static Color Opaque(Color value) => Color.FromRgb(value.R, value.G, value.B);

    private static Color Mix(Color from, Color to, double amount) => Color.FromArgb(from.A,
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    private static IBrush Gradient(double degrees, params (double Offset, Color Colour)[] stops)
    {
        var angle = degrees * Math.PI / 180;
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(.5 - Math.Cos(angle) / 2, .5 - Math.Sin(angle) / 2, RelativeUnit.Relative),
            EndPoint = new RelativePoint(.5 + Math.Cos(angle) / 2, .5 + Math.Sin(angle) / 2, RelativeUnit.Relative)
        };
        foreach (var (offset, colour) in stops) brush.GradientStops.Add(new GradientStop(colour, offset));
        return brush.ToImmutable();
    }
}
