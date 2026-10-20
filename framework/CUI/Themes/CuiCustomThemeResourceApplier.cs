using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CakeOS.Cui.Themes;

public enum CuiFontRole { Main, PrimaryButton, SecondaryButton, TertiaryButton, Heading, Body, Code }
public enum CuiAccentRole { None, Primary, Secondary, Tertiary }

/// <summary>Backend conversion of a validated portable custom theme into existing shared CUI resources.</summary>
public static class CuiCustomThemeResourceApplier
{
    /// <summary>Remove custom-only tokens before the owning host reapplies its base palette.
    /// Fresh off-tree scope dictionaries do not need this; reused global dictionaries do.</summary>
    public static void ClearFromResources(IResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (var key in new[] { "CuiCustomTypography", "CuiCustomFontScale", "CuiButtonRoundnessPercent" }) resources.Remove(key);
        foreach (var role in new[] { "Primary", "Secondary", "Tertiary" })
            foreach (var suffix in new[] { "Fill", "ForegroundBrush", "PressedBrush", "Background", "HoverBackground", "PressedBackground" })
                resources.Remove($"CuiAccent{role}{suffix}");
    }

    public static void ApplyToResources(IResourceDictionary resources, CuiCustomTheme theme,
        CuiAccessibilitySettings? accessibility = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var value = theme.Validate();
        var settings = (accessibility ?? CuiAccessibilitySettings.Default).Validate();
        // Implicit Mica belongs only to the named Professional preset, not to
        // custom themes that use its structural base. Explicit colour backdrop
        // settings are rendered separately by the custom material adapter.
        resources["CuiWorkspaceMaterial"] = resources["CuiWorkspaceBrush"];
        resources["CuiShellMaterial"] = resources["CuiBackgroundBrush"];
        resources["CuiSurfaceMaterial"] = resources["CuiPanelBrush"];
        resources["CuiFloatingSurfaceMaterial"] = resources["CuiPanel2Brush"];
        if (resources["CuiPanel2Brush"] is ISolidColorBrush popup)
            resources["CuiPopupSurfaceBrush"] = new SolidColorBrush(Color.FromRgb(popup.Color.R, popup.Color.G, popup.Color.B)).ToImmutable();
        // Opaque solid fallback for transparency negotiation (never a material/transparent brush).
        resources["CuiPopupOpaqueFallbackBrush"] = resources["CuiPanel2Brush"] is ISolidColorBrush fallbackSource
            ? new SolidColorBrush(Color.FromRgb(fallbackSource.Color.R, fallbackSource.Color.G, fallbackSource.Color.B)).ToImmutable()
            : Brushes.Black;
        resources["CuiCustomTypography"] = value.Fonts;
        resources["CuiCustomFontScale"] = settings.DisplayScale;
        resources["CuiButtonRoundnessPercent"] = value.ButtonRoundnessPercent;
        resources["CuiInterfaceFontFamily"] = new FontFamily(CuiCustomThemeFonts.ResourceFamily(value.Fonts.Main.Family));
        resources["CuiFontWeightBody"] = (FontWeight)value.Fonts.Main.Weight;
        resources["CuiFontSizeBody"] = value.Fonts.Main.Size * settings.DisplayScale;
        resources["CuiFontFamilyInterface"] = CuiCustomThemeFonts.ResourceFamily(value.Fonts.Main.Family);
        resources["CuiFontFamilyCode"] = CuiCustomThemeFonts.ResourceFamily(value.Fonts.Code.Family);
        resources["CuiFontSizeHeading"] = value.Fonts.Heading.Size * settings.DisplayScale;
        resources["CuiFontSizeCode"] = value.Fonts.Code.Size * settings.DisplayScale;
        if (!settings.HighContrast)
        {
            ApplyFill(resources, "Primary", value.PrimaryAccent);
            ApplyFill(resources, "Secondary", value.SecondaryAccent);
            ApplyFill(resources, "Tertiary", value.TertiaryAccent);
            resources["CuiAccentBrush"] = resources["CuiAccentPrimaryBrush"];
            resources["HavenAccentBrush"] = resources["CuiAccentPrimaryBrush"];
            resources["PrimaryBrush"] = resources["CuiAccentPrimaryBrush"];
        }
        else
        {
            // Explicitly replace every custom role in a reused dictionary too.
            var panel = resources["CuiPanelBrush"] is ISolidColorBrush surface ? surface.Color : Colors.Black;
            var background = new SolidColorBrush(Color.FromRgb(panel.R, panel.G, panel.B)).ToImmutable();
            var foreground = resources["CuiTextBrush"] as IBrush ?? Brushes.White;
            foreach (var role in new[] { "Primary", "Secondary", "Tertiary" })
            {
                resources.Remove($"CuiAccent{role}Fill");
                resources[$"CuiAccent{role}Brush"] = background;
                resources[$"HavenAccent{role}Brush"] = background;
                resources[$"CuiAccent{role}HoverBrush"] = background;
                resources[$"CuiAccent{role}PressedBrush"] = background;
                resources[$"CuiAccent{role}ForegroundBrush"] = foreground;
                resources[$"CuiAccent{role}Background"] = background;
                resources[$"CuiAccent{role}HoverBackground"] = background;
                resources[$"CuiAccent{role}PressedBackground"] = background;
            }
            resources["CuiAccentBrush"] = background;
            resources["HavenAccentBrush"] = background;
            resources["PrimaryBrush"] = background;
        }
    }

    private static void ApplyFill(IResourceDictionary resources, string role, CuiCustomFill fill)
    {
        var brush = CreateBrush(fill);
        resources[$"CuiAccent{role}Brush"] = brush;
        var backgroundBrush = CreateBackgroundBrush(fill);
        resources[$"CuiAccent{role}Background"] = backgroundBrush;
        resources[$"CuiAccent{role}HoverBackground"] = backgroundBrush;
        resources[$"CuiAccent{role}PressedBackground"] = backgroundBrush;
        resources[$"HavenAccent{role}Brush"] = brush;
        // A role keeps its authored fill through interaction; the shared focus,
        // pressed and disabled state contracts remain independently projected.
        resources[$"CuiAccent{role}HoverBrush"] = brush;
        resources[$"CuiAccent{role}PressedBrush"] = brush;
        resources[$"CuiAccent{role}Fill"] = fill;
        var background = resources["CuiPanelBrush"] is ISolidColorBrush panel ? panel.Color : Colors.Black;
        var colours = fill.Kind switch
        {
            CuiCustomFillKind.Gradient => fill.Gradient!.Stops.Select(stop => Colour(stop.Colour)),
            CuiCustomFillKind.Pattern => new[] { Colour(fill.Pattern!.Foreground), Colour(fill.Pattern.Background) },
            _ => new[] { Colour(fill.Colour) }
        };
        var samples = colours.Select(colour => Color.FromRgb(
            (byte)Math.Round(colour.R * colour.A / 255d + background.R * (1 - colour.A / 255d)),
            (byte)Math.Round(colour.G * colour.A / 255d + background.G * (1 - colour.A / 255d)),
            (byte)Math.Round(colour.B * colour.A / 255d + background.B * (1 - colour.A / 255d)))).ToArray();
        var ink = samples.Min(colour => CuiContrast.Ratio(Colors.Black, colour)) >= samples.Min(colour => CuiContrast.Ratio(Colors.White, colour)) ? Colors.Black : Colors.White;
        resources[$"CuiAccent{role}ForegroundBrush"] = new SolidColorBrush(ink).ToImmutable();
    }

    /// <summary>Backdrop effects belong to the owning surface, never foreground text or icons.</summary>
    public static IBrush CreateBackgroundBrush(CuiCustomFill fill)
    {
        var value = CuiCustomThemeContract.ValidateFill(fill);
        var hasBlur = value.Kind switch
        {
            CuiCustomFillKind.SolidColour => value.Colour.BackgroundBlur > 0,
            CuiCustomFillKind.Gradient => value.Gradient!.Stops.Any(stop => stop.Colour.BackgroundBlur > 0),
            CuiCustomFillKind.Pattern => value.Pattern!.Foreground.BackgroundBlur > 0 || value.Pattern.Background.BackgroundBlur > 0,
            _ => false
        };
        return hasBlur ? new CuiCustomMaterialBrush(value) : CreateBrush(value);
    }

    public static IBrush CreateBrush(CuiCustomFill fill)
    {
        var value = CuiCustomThemeContract.ValidateFill(fill);
        return value.Kind switch
        {
            CuiCustomFillKind.SolidColour => new SolidColorBrush(Colour(value.Colour)).ToImmutable(),
            CuiCustomFillKind.Gradient => Gradient(value.Gradient!).ToImmutable(),
            CuiCustomFillKind.Pattern => Pattern(value.Pattern!),
            _ => throw new ArgumentOutOfRangeException(nameof(fill))
        };
    }

    public static Color Colour(CuiCustomColour value)
    {
        var colour = Color.Parse(value.Hex);
        return Color.FromArgb((byte)Math.Round(255 * (1 - value.TransparencyPercent / 100)), colour.R, colour.G, colour.B);
    }

    private static GradientBrush Gradient(CuiCustomGradient value)
    {
        var angle = value.Angle * Math.PI / 180;
        GradientBrush result = value.Kind switch
        {
            CuiCustomGradientKind.Linear => new LinearGradientBrush
            {
                StartPoint = new RelativePoint(.5 - Math.Cos(angle) / 2, .5 - Math.Sin(angle) / 2, RelativeUnit.Relative),
                EndPoint = new RelativePoint(.5 + Math.Cos(angle) / 2, .5 + Math.Sin(angle) / 2, RelativeUnit.Relative)
            },
            CuiCustomGradientKind.Radial => new RadialGradientBrush
            {
                Center = new RelativePoint(value.CenterX, value.CenterY, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(value.CenterX, value.CenterY, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(value.Radius, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(value.Radius, RelativeUnit.Relative)
            },
            CuiCustomGradientKind.Conic => new ConicGradientBrush
            {
                Center = new RelativePoint(value.CenterX, value.CenterY, RelativeUnit.Relative), Angle = value.Angle
            },
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
        foreach (var stop in value.Stops) result.GradientStops.Add(new GradientStop(Colour(stop.Colour), stop.Offset));
        return result;
    }

    private static DrawingBrush Pattern(CuiCustomPattern value)
    {
        var cell = value.Size + value.Spacing;
        var extent = value.Kind == CuiCustomPatternKind.Checkerboard ? cell * 2 : cell;
        var group = new DrawingGroup();
        var foreground = new SolidColorBrush(Colour(value.Foreground)).ToImmutable();
        var background = new GeometryGroup { FillRule = FillRule.EvenOdd };
        background.Children.Add(new RectangleGeometry(new Rect(0, 0, extent, extent)));
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(Colour(value.Background)).ToImmutable(), Geometry = background });
        switch (value.Kind)
        {
            case CuiCustomPatternKind.Stripes:
                group.Children.Add(new GeometryDrawing { Brush = foreground, Geometry = new RectangleGeometry(new Rect(0, 0, value.Size, extent)) });
                break;
            case CuiCustomPatternKind.Checkerboard:
                group.Children.Add(new GeometryDrawing { Brush = foreground, Geometry = new RectangleGeometry(new Rect(0, 0, value.Size, value.Size)) });
                group.Children.Add(new GeometryDrawing { Brush = foreground, Geometry = new RectangleGeometry(new Rect(cell, cell, value.Size, value.Size)) });
                break;
            case CuiCustomPatternKind.Dots:
                group.Children.Add(new GeometryDrawing { Brush = foreground,
                    Geometry = new EllipseGeometry(new Rect(value.Spacing / 2, value.Spacing / 2, value.Size, value.Size)) });
                break;
        }
        foreach (var drawing in group.Children.Skip(1).OfType<GeometryDrawing>())
            background.Children.Add(drawing.Geometry!.Clone());
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
            SourceRect = new RelativeRect(0, 0, extent, extent, RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(0, 0, extent, extent, RelativeUnit.Absolute),
            Transform = new RotateTransform(value.Angle), TransformOrigin = RelativePoint.Center
        };
    }
}
