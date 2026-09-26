// CUI Theme Resource Applier — ported from HavenUiResourceApplier.cs.
// Original source: 9to1 Workspace/shared/src/Haven.Desktop/HavenUI/Tokens/HavenUiResourceApplier.cs
//
// Applies one canonical CUI semantic palette to application resources.
// Existing resource names remain aliases while screens migrate to the clearer
// semantic names; both sets always resolve to the same colour values.

using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;

namespace CakeOS.Cui.Themes;

/// <summary>
/// Applies one canonical CUI semantic palette to application resources.
/// </summary>
public static class CuiThemeResourceApplier
{
    public static event EventHandler? PaletteChanged;

    /// <summary>
    /// Applies the given palette to Avalonia Application.Current.Resources.
    /// Sets all semantic brushes, corner radii, motion scale, and accent gradients.
    /// </summary>
    public static void Apply(
        CuiPalette palette,
        CuiAccessibilitySettings? accessibility = null,
        CuiLocalizationContext? localization = null)
    {
        var settings = (accessibility ?? CuiAccessibilitySettings.Default).Validate();
        palette = CuiAccessibilityPalette.Resolve(palette, settings);
        var isDark = Luminance(palette.Text) > Luminance(palette.Panel);
        var disabledText = WithAlpha(palette.Muted, 0x88);
        var overlay = WithAlpha(palette.Panel, isDark ? (byte)0xF2 : (byte)0xF7);
        var input = palette.Button;
        var success = Color.Parse(isDark ? "#FF76D7A0" : "#FF147A48");
        var information = palette.AccentSecondary;
        var expression = CuiThemeCatalog.Resolve(palette.Theme);
        var shadowBase = Color.Parse(isDark ? "#B8000000" : "#52000000");
        var shadow = WithAlpha(shadowBase, (byte)Math.Clamp(Math.Round(shadowBase.A * expression.ShadowOpacityScale), 0, 255));

        // === Semantic brushes (original Haven names) ===
        SetBrush("HavenBackgroundBrush", palette.TideBase);
        SetBrush("HavenTextBrush", palette.Text);
        SetBrush("HavenTextSoftBrush", palette.TextSoft);
        SetBrush("HavenMutedBrush", palette.Muted);
        SetBrush("HavenMuted2Brush", palette.Muted2);
        SetBrush("HavenPanelBrush", palette.Panel);
        SetBrush("HavenElevatedBrush", palette.Panel);
        SetBrush("HavenPanel2Brush", palette.Panel2);
        SetBrush("HavenPanel3Brush", palette.Panel3);
        SetBrush("HavenPanelHoverBrush", palette.PanelHover);
        SetBrush("HavenLineBrush", palette.Line);
        SetBrush("HavenLineStrongBrush", palette.LineStrong);
        SetBrush("HavenButtonBrush", palette.Button);
        SetBrush("HavenButtonHoverBrush", palette.ButtonHover);
        SetBrush("HavenButtonPressedBrush", palette.ButtonPressed);
        SetBrush("HavenFocusBrush", palette.Focus);

        var accents = palette.AccentPalette;
        ApplyAccentPaletteCore(accents);

        SetBrush("HavenAccentInkBrush", palette.AccentInk);
        SetBrush("HavenAccentSoftBrush", palette.AccentSoft);
        SetBrush("HavenBlueSoftBrush", palette.AccentSoft);
        SetBrush("HavenNubBrush", palette.AccentSecondary);
        SetBrush("HavenAccentBorderBrush", palette.AccentBorder);
        SetBrush("HavenAttentionBrush", palette.Attention);
        SetBrush("HavenAttentionBorderBrush", palette.AttentionBorder);

        // === CUI-prefixed semantic brushes (preferred for new code) ===
        SetBrush("CuiBackgroundBrush", palette.TideBase);
        SetBrush("CuiTextBrush", palette.Text);
        SetBrush("CuiTextSoftBrush", palette.TextSoft);
        SetBrush("CuiMutedBrush", palette.Muted);
        SetBrush("CuiPanelBrush", palette.Panel);
        SetBrush("CuiPanel2Brush", palette.Panel2);
        SetBrush("CuiPanel3Brush", palette.Panel3);
        SetBrush("CuiPanelHoverBrush", palette.PanelHover);
        SetBrush("CuiLineBrush", palette.Line);
        SetBrush("CuiLineStrongBrush", palette.LineStrong);
        SetBrush("CuiButtonBrush", palette.Button);
        SetBrush("CuiButtonHoverBrush", palette.ButtonHover);
        SetBrush("CuiButtonPressedBrush", palette.ButtonPressed);
        SetBrush("CuiFocusBrush", palette.Focus);
        SetBrush("CuiShadowBrush", shadow);
        SetBrush("CuiOverlaySurfaceBrush", overlay);
        SetBrush("CuiInputSurfaceBrush", input);
        SetBrush("CuiSuccessBrush", success);
        SetBrush("CuiInformationBrush", information);

        // === Aliased names for backward compatibility ===
        SetBrush("StrokeBrush", palette.LineStrong);
        SetBrush("SurfaceCardBrush", palette.Panel);
        SetBrush("TextPrimaryBrush", palette.Text);

        // === Theme personality: structural tokens ===
        SetCornerRadius("CuiControlRadius", CuiThemeExpression.BaseControlRadius * expression.ControlRadiusScale);
        SetCornerRadius("CuiCardRadius", CuiThemeExpression.BaseCardRadius * expression.CardRadiusScale);
        SetCornerRadius("CuiPopupRadius", CuiThemeExpression.BasePopupRadius * expression.PopupRadiusScale);
        SetCornerRadius("HavenControlRadius", CuiThemeExpression.BaseControlRadius * expression.ControlRadiusScale);
        SetCornerRadius("HavenCardRadius", CuiThemeExpression.BaseCardRadius * expression.CardRadiusScale);
        SetCornerRadius("HavenPopupRadius", CuiThemeExpression.BasePopupRadius * expression.PopupRadiusScale);

        var resources = Application.Current?.Resources;
        if (resources is not null)
        {
            var effectiveMotionScale = settings.EffectiveMotionScale(expression.MotionDurationScale);
            resources["CuiMotionDurationScale"] = effectiveMotionScale;
            resources["HavenMotionDurationScale"] = effectiveMotionScale;
            resources["CuiTheme"] = palette.Theme;
            resources["CuiAppearance"] = isDark ? "Dark" : "Bright";
            ApplyAccessibilityResources(resources, expression, settings, localization);
        }

        PaletteChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Applies a generated/App-scoped three-tier accent without replacing the
    /// rest of the current CUI appearance.
    /// </summary>
    public static void ApplyAccentPalette(CuiAccentPalette accents)
    {
        ApplyAccentPaletteCore(accents);
    }

    private static void ApplyAccentPaletteCore(CuiAccentPalette accents)
    {
        SetGradient("CuiAccentBrush", accents.Primary);
        SetGradient("CuiAccentPrimaryBrush", accents.Primary);
        SetGradient("CuiAccentSecondaryBrush", accents.Secondary);
        SetGradient("CuiAccentTertiaryBrush", accents.Tertiary);
        SetGradient("CuiAccentPrimaryHoverBrush", Shift(accents.Primary, accents.Secondary.Start, 0.16));
        SetGradient("CuiAccentSecondaryHoverBrush", Shift(accents.Secondary, accents.Primary.Start, 0.18));
        SetGradient("CuiAccentTertiaryHoverBrush", Shift(accents.Tertiary, accents.Secondary.Start, 0.70));
        SetGradient("CuiAccentPressedBrush", Shift(accents.Primary, accents.Tertiary.Middle, 0.22));
        SetColour("CuiAccentPrimaryColor", accents.Primary.Middle);
        SetColour("CuiAccentSecondaryColor", accents.Secondary.Middle);
        SetColour("CuiAccentTertiaryColor", accents.Tertiary.Middle);
        SetColour("CuiAccentPrimaryGlowColor", WithAlpha(accents.Primary.Start, 0xC8));
        SetColour("CuiAccentSecondaryGlowColor", WithAlpha(accents.Secondary.Start, 0xA8));
        SetColour("CuiAccentTertiaryGlowColor", WithAlpha(accents.Tertiary.End, 0x92));
        SetGradient("PrimaryBrush", accents.Primary);
        SetBrush("CuiAccentInkBrush", accents.Foreground);
        SetBrush("CuiAccentForegroundBrush", accents.Foreground);
        SetBrush("CuiAccentSoftBrush", accents.SoftSurface);

        // Haven legacy names
        SetGradient("HavenAccentBrush", accents.Primary);
        SetGradient("HavenAccentPrimaryBrush", accents.Primary);
        SetGradient("HavenAccentSecondaryBrush", accents.Secondary);
        SetGradient("HavenAccentTertiaryBrush", accents.Tertiary);
    }

    /// <summary>
    /// Pushes theme resources into a ResourceDictionary (e.g. a newly created one
    /// that will be merged into a control's resources). Used by DefaultTheme scoping.
    /// </summary>
    public static void ApplyToResources(
        ResourceDictionary resources,
        CuiPalette palette,
        CuiAccessibilitySettings? accessibility = null,
        CuiLocalizationContext? localization = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var settings = (accessibility ?? CuiAccessibilitySettings.Default).Validate();
        palette = CuiAccessibilityPalette.Resolve(palette, settings);
        var isDark = Luminance(palette.Text) > Luminance(palette.Panel);
        var expression = CuiThemeCatalog.Resolve(palette.Theme);
        var shadowBase = Color.Parse(isDark ? "#B8000000" : "#52000000");
        var shadow = WithAlpha(shadowBase, (byte)Math.Clamp(Math.Round(shadowBase.A * expression.ShadowOpacityScale), 0, 255));

        resources["CuiBackgroundBrush"] = new SolidColorBrush(palette.TideBase);
        resources["CuiTextBrush"] = new SolidColorBrush(palette.Text);
        resources["CuiTextSoftBrush"] = new SolidColorBrush(palette.TextSoft);
        resources["CuiMutedBrush"] = new SolidColorBrush(palette.Muted);
        resources["CuiPanelBrush"] = new SolidColorBrush(palette.Panel);
        resources["CuiPanel2Brush"] = new SolidColorBrush(palette.Panel2);
        resources["CuiPanel3Brush"] = new SolidColorBrush(palette.Panel3);
        resources["CuiPanelHoverBrush"] = new SolidColorBrush(palette.PanelHover);
        resources["CuiLineBrush"] = new SolidColorBrush(palette.Line);
        resources["CuiLineStrongBrush"] = new SolidColorBrush(palette.LineStrong);
        resources["CuiButtonBrush"] = new SolidColorBrush(palette.Button);
        resources["CuiButtonHoverBrush"] = new SolidColorBrush(palette.ButtonHover);
        resources["CuiButtonPressedBrush"] = new SolidColorBrush(palette.ButtonPressed);
        resources["CuiFocusBrush"] = new SolidColorBrush(palette.Focus);
        resources["CuiShadowBrush"] = new SolidColorBrush(shadow);
        resources["CuiControlRadius"] = new CornerRadius(Math.Max(0d, Math.Round(CuiThemeExpression.BaseControlRadius * expression.ControlRadiusScale)));
        resources["CuiCardRadius"] = new CornerRadius(Math.Max(0d, Math.Round(CuiThemeExpression.BaseCardRadius * expression.CardRadiusScale)));
        resources["CuiPopupRadius"] = new CornerRadius(Math.Max(0d, Math.Round(CuiThemeExpression.BasePopupRadius * expression.PopupRadiusScale)));
        resources["CuiMotionDurationScale"] = settings.EffectiveMotionScale(expression.MotionDurationScale);
        resources["CuiTheme"] = palette.Theme;
        ApplyAccessibilityResources(resources, expression, settings, localization);

        var accents = palette.AccentPalette;
        ApplyAccentToResources(resources, accents);
    }

    private static void ApplyAccessibilityResources(
        IResourceDictionary resources,
        CuiThemeExpression expression,
        CuiAccessibilitySettings settings,
        CuiLocalizationContext? localization)
    {
        resources["CuiFontFamilyInterface"] = CuiTypography.InterfaceFontFamily;
        resources["CuiFontFamilyCode"] = CuiTypography.CodeFontFamily;
        resources["CuiFontSizeBody"] = CuiTypography.BodySize * expression.TypographyScale * settings.DisplayScale;
        resources["CuiFontSizeCaption"] = CuiTypography.CaptionSize * expression.TypographyScale * settings.DisplayScale;
        resources["CuiFontSizeHeading"] = CuiTypography.HeadingSize * expression.TypographyScale * settings.DisplayScale;
        resources["CuiFontSizeCode"] = CuiTypography.CodeSize * expression.TypographyScale * settings.DisplayScale;
        resources["CuiSpacingScale"] = expression.SpacingScale * settings.DisplayScale;
        resources["CuiControlHeightScale"] = expression.ControlHeightScale * settings.DisplayScale;
        resources["CuiElevationScale"] = expression.ElevationScale;
        resources["CuiDisplayScale"] = settings.DisplayScale;
        resources["CuiReduceMotion"] = settings.ReduceMotion;
        resources["CuiHighContrast"] = settings.HighContrast;
        resources["CuiFocusIndicatorThickness"] = settings.HighContrast ? 3d : 2d;
        resources["CuiStateCommunication"] = "IconAndLabel";

        var culture = localization ?? new CuiLocalizationContext();
        resources["CuiCultureName"] = culture.Culture.Name;
        resources["CuiFlowDirection"] = culture.FlowDirection;
    }

    private static void ApplyAccentToResources(ResourceDictionary resources, CuiAccentPalette accents)
    {
        resources["CuiAccentBrush"] = CreateGradientBrush(accents.Primary);
        resources["CuiAccentPrimaryBrush"] = CreateGradientBrush(accents.Primary);
        resources["CuiAccentSecondaryBrush"] = CreateGradientBrush(accents.Secondary);
        resources["CuiAccentTertiaryBrush"] = CreateGradientBrush(accents.Tertiary);
        resources["CuiAccentPrimaryColor"] = accents.Primary.Middle;
        resources["CuiAccentSecondaryColor"] = accents.Secondary.Middle;
        resources["CuiAccentTertiaryColor"] = accents.Tertiary.Middle;
        resources["CuiAccentInkBrush"] = new SolidColorBrush(accents.Foreground);
        resources["CuiAccentForegroundBrush"] = new SolidColorBrush(accents.Foreground);
        resources["CuiAccentSoftBrush"] = new SolidColorBrush(accents.SoftSurface);
    }

    private static LinearGradientBrush CreateGradientBrush(CuiAccentGradient gradient) =>
        new()
        {
            StartPoint = gradient.StartPoint,
            EndPoint = gradient.EndPoint,
            GradientStops =
            [
                new GradientStop(gradient.Start, 0d),
                new GradientStop(gradient.Middle, 0.52d),
                new GradientStop(gradient.End, 1d)
            ]
        };

    private static void SetBrush(string key, Color colour)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;
        if (resources[key] is SolidColorBrush existing)
        {
            existing.Color = colour;
            return;
        }
        resources[key] = new SolidColorBrush(colour);
    }

    private static void SetGradient(string key, CuiAccentGradient gradient)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        if (resources[key] is not LinearGradientBrush brush)
        {
            brush = CreateGradientBrush(gradient);
            resources[key] = brush;
        }
        else
        {
            while (brush.GradientStops.Count < 3)
                brush.GradientStops.Add(new GradientStop());
            while (brush.GradientStops.Count > 3)
                brush.GradientStops.RemoveAt(brush.GradientStops.Count - 1);

            brush.GradientStops[0].Color = gradient.Start;
            brush.GradientStops[0].Offset = 0d;
            brush.GradientStops[1].Color = gradient.Middle;
            brush.GradientStops[1].Offset = 0.52d;
            brush.GradientStops[2].Color = gradient.End;
            brush.GradientStops[2].Offset = 1d;
        }

        brush.StartPoint = gradient.StartPoint;
        brush.EndPoint = gradient.EndPoint;
    }

    private static void SetColour(string key, Color colour)
    {
        var resources = Application.Current?.Resources;
        if (resources is not null) resources[key] = colour;
    }

    private static void SetCornerRadius(string key, double radius)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;
        resources[key] = new CornerRadius(Math.Max(0d, Math.Round(radius)));
    }

    private static CuiAccentGradient Shift(CuiAccentGradient source, Color toward, double amount) =>
        new(
            Blend(source.Start, toward, amount),
            Blend(source.Middle, toward, amount),
            Blend(source.End, toward, amount),
            source.StartPoint,
            source.EndPoint);

    private static Color Blend(Color first, Color second, double secondWeight)
    {
        var weight = Math.Clamp(secondWeight, 0d, 1d);
        return Color.FromArgb(
            (byte)Math.Round(first.A + ((second.A - first.A) * weight)),
            (byte)Math.Round(first.R + ((second.R - first.R) * weight)),
            (byte)Math.Round(first.G + ((second.G - first.G) * weight)),
            (byte)Math.Round(first.B + ((second.B - first.B) * weight)));
    }

    private static Color WithAlpha(Color value, byte alpha) =>
        Color.FromArgb(alpha, value.R, value.G, value.B);

    private static double Luminance(Color colour) =>
        (0.2126d * colour.R) + (0.7152d * colour.G) + (0.0722d * colour.B);
}
