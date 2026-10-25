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
        CuiLocalizationContext? localization = null,
        CuiAppearance? appearanceOverride = null,
        CuiCustomTheme? customTheme = null)
    {
        if (appearanceOverride is { } appearance && !Enum.IsDefined(appearance))
            throw new ArgumentOutOfRangeException(nameof(appearanceOverride));
        var settings = (accessibility ?? CuiAccessibilitySettings.Default).Validate();
        var custom = customTheme?.Validate();
        palette = CuiAccessibilityPalette.Resolve(palette, settings);
        var application = Application.Current;
        var provider = application?.Resources as IResourceProvider;
        var ownsResources = application is not null && ReferenceEquals(provider?.Owner, application);
        // Keep the dictionary and legacy brush identities, but disconnect its
        // notification owner while updating the palette. Reattachment publishes
        // the completed palette instead of invalidating every control per token.
        if (ownsResources) provider!.RemoveOwner(application!);
        try
        {
            if (application is not null)
                CuiCustomThemeResourceApplier.ClearFromResources(application.Resources);
            ApplyCore(palette, settings, localization, appearanceOverride);
            if (application is not null)
            {
                if (custom is not null)
                    CuiCustomThemeResourceApplier.ApplyToResources(application.Resources, custom, settings);
                application.Resources["CuiEffectiveThemeName"] = custom?.Name ?? CuiThemeCatalog.Name(palette.Theme);
            }
        }
        finally
        {
            if (ownsResources) provider!.AddOwner(application!);
        }
        PaletteChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyCore(
        CuiPalette palette,
        CuiAccessibilitySettings settings,
        CuiLocalizationContext? localization,
        CuiAppearance? appearanceOverride)
    {
        var isDark = Luminance(palette.Text) > Luminance(palette.Panel);
        var disabledText = WithAlpha(palette.Muted, 0x88);
        var overlay = WithAlpha(palette.Panel, isDark ? (byte)0xF2 : (byte)0xF7);
        var input = InputSurface(palette);
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
        SetBrush("CuiMuted2Brush", palette.Muted2);
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
        SetBrush("CuiAttentionBrush", palette.Attention);
        SetBrush("CuiAttentionBorderBrush", palette.AttentionBorder);

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
            ApplyComponentResources(resources, palette, expression, settings);
            ApplyButtonForegroundResources(resources, palette);
            ApplyButtonRoleResources(resources, palette, settings);
            var effectiveMotionScale = settings.EffectiveMotionScale(expression.MotionDurationScale);
            resources["CuiMotionDurationScale"] = effectiveMotionScale;
            resources["HavenMotionDurationScale"] = effectiveMotionScale;
            resources["CuiTheme"] = palette.Theme;
            resources["CuiAppearance"] = appearanceOverride?.ToString() ?? (isDark ? "Dark" : "Bright");
            ApplyAccessibilityResources(resources, expression, settings, localization);
            ApplyLegacyScopeAliases(resources);
        }

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
        SetGradient("CuiAccentSecondaryHoverBrush", Shift(accents.Secondary, Colors.White, 0.08));
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
        resources["CuiMuted2Brush"] = new SolidColorBrush(palette.Muted2);
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
        resources["CuiOverlaySurfaceBrush"] = new SolidColorBrush(WithAlpha(palette.Panel, isDark ? (byte)0xF2 : (byte)0xF7));
        resources["CuiInputSurfaceBrush"] = new SolidColorBrush(InputSurface(palette));
        resources["CuiSuccessBrush"] = new SolidColorBrush(Color.Parse(isDark ? "#FF76D7A0" : "#FF147A48"));
        resources["CuiInformationBrush"] = new SolidColorBrush(palette.AccentSecondary);
        resources["CuiAttentionBrush"] = new SolidColorBrush(palette.Attention);
        resources["CuiAttentionBorderBrush"] = new SolidColorBrush(palette.AttentionBorder);
        resources["CuiControlRadius"] = new CornerRadius(Math.Max(0d, Math.Round(CuiThemeExpression.BaseControlRadius * expression.ControlRadiusScale)));
        resources["CuiCardRadius"] = new CornerRadius(Math.Max(0d, Math.Round(CuiThemeExpression.BaseCardRadius * expression.CardRadiusScale)));
        resources["CuiPopupRadius"] = new CornerRadius(Math.Max(0d, Math.Round(CuiThemeExpression.BasePopupRadius * expression.PopupRadiusScale)));
        resources["CuiMotionDurationScale"] = settings.EffectiveMotionScale(expression.MotionDurationScale);
        resources["CuiTheme"] = palette.Theme;
        ApplyAccessibilityResources(resources, expression, settings, localization);

        var accents = palette.AccentPalette;
        ApplyAccentToResources(resources, accents);
        ApplyComponentResources(resources, palette, expression, settings);
        ApplyButtonForegroundResources(resources, palette);
        ApplyButtonRoleResources(resources, palette, settings);
        ApplyLegacyScopeAliases(resources);
    }

    private static void ApplyComponentResources(IResourceDictionary resources, CuiPalette palette, CuiThemeExpression expression, CuiAccessibilitySettings settings)
    {
        var professional = palette.Theme == CuiTheme.Professional;
        resources["CuiInterfaceFontFamily"] = new FontFamily(CuiTypography.ResolveInterfaceFontFamily(settings.InterfaceFontFamilyOverride));
        var spacing = expression.SpacingScale * settings.DisplayScale;
        resources["CuiControlPadding"] = new Avalonia.Thickness((professional ? 12 : 14) * spacing, (professional ? 7 : 10) * spacing);
        resources["CuiControlMinHeight"] = (professional ? 34d : 40d * expression.ControlHeightScale) * settings.DisplayScale;
        resources["CuiButtonVerticalContentAlignment"] = Avalonia.Layout.VerticalAlignment.Center;
        resources["CuiButtonHorizontalContentAlignment"] = Avalonia.Layout.HorizontalAlignment.Center;
        resources["CuiControlBorderWidth"] = new Thickness((settings.HighContrast ? 2 : 1) * settings.DisplayScale);
        resources["CuiControlBorderBrush"] = (professional || palette.Theme == CuiTheme.Glow) && !settings.HighContrast
            ? Brushes.Transparent : resources["CuiLineBrush"];
        resources["CuiChoiceBorderBrush"] = new SolidColorBrush(CuiContrast.EnsureForegroundContrast(palette.LineStrong, palette.Panel, 3));
        resources["CuiInputRadius"] = new CornerRadius(professional
            ? (34d * settings.DisplayScale) / 2d // default single-line control → full capsule
            : CuiThemeExpression.BaseControlRadius * expression.ControlRadiusScale * settings.DisplayScale);
        resources["CuiWorkspaceBrush"] = new SolidColorBrush(professional ? palette.TideColour : palette.TideBase);
        var material = professional && !settings.HighContrast;
        var dark = Luminance(palette.Text) > Luminance(palette.Panel);
        var floating = Color.FromRgb(
            (byte)Math.Round(palette.Panel3.R + (255 - palette.Panel3.R) * (dark ? .055 : .70)),
            (byte)Math.Round(palette.Panel3.G + (255 - palette.Panel3.G) * (dark ? .055 : .70)),
            (byte)Math.Round(palette.Panel3.B + (255 - palette.Panel3.B) * (dark ? .055 : .70)));
        resources["CuiWorkspaceMaterial"] = material
            ? new CuiMaterialBrush(palette.TideColour, 1, 0, .022, tonalBase: true) : resources["CuiWorkspaceBrush"];
        resources["CuiShellMaterial"] = material
            ? new CuiMaterialBrush(palette.TideBase, 1, 0, .015, tonalBase: true) : resources["CuiBackgroundBrush"];
        resources["CuiSurfaceMaterial"] = material
            ? new CuiMaterialBrush(palette.Panel, .94, 8, .015) : resources["CuiPanelBrush"];
        resources["CuiFloatingSurfaceMaterial"] = material
            ? new CuiMaterialBrush(floating, .96, 8, .012) : resources["CuiPanel2Brush"];
        // Popup structure and menu rows belong to CUI, including scaling and
        // accessibility. Professional uses its strongly tinted backdrop surface;
        // other themes and high contrast retain the existing opaque material.
        resources["CuiPopupSurfaceBrush"] = material ? resources["CuiFloatingSurfaceMaterial"] : new SolidColorBrush(WithAlpha(palette.Panel2, 255));
        // Distinct guaranteed-opaque semantic fallback for transparency negotiation. This is a plain
        // opaque solid brush, never the material surface (whose ISolidColorBrush.Color is Transparent);
        // TopLevel paints it only when per-pixel alpha cannot be negotiated.
        resources["CuiPopupOpaqueFallbackBrush"] = new SolidColorBrush(WithAlpha(palette.Panel2, 255));
        resources["CuiPopupPadding"] = new Thickness((professional ? 12 : 8) * spacing);
        resources["CuiPopupBorderBrush"] = professional && !settings.HighContrast ? Brushes.Transparent : resources["CuiLineBrush"];
        resources["CuiMenuItemPadding"] = new Thickness(10 * spacing, 7 * spacing);
        resources["CuiMenuItemBorderWidth"] = new Thickness(0);
        resources["CuiMenuItemRadius"] = new CornerRadius(8 * settings.DisplayScale);
        resources["CuiFloatingMenuItemMinHeight"] = (professional ? 40d : 40d * expression.ControlHeightScale) * settings.DisplayScale;
        resources["CuiFloatingMenuItemPadding"] = new Thickness(10 * spacing, (professional ? 10 : 7) * spacing);
        resources["CuiMenuSelectionBrush"] = new SolidColorBrush(professional && !settings.HighContrast ? palette.PanelHover : palette.AccentSoft);
        resources["CuiChoiceRadius"] = new CornerRadius(5 * settings.DisplayScale);
        var shadowAlpha = (byte)Math.Clamp(Math.Round((Luminance(palette.Text) > Luminance(palette.Panel) ? 110 : 42) * expression.ShadowOpacityScale), 0, 255);
        var popupShadow = new BoxShadow
        {
            OffsetY = 8 * settings.DisplayScale, Blur = 24 * settings.DisplayScale,
            Spread = 0, Color = Color.FromArgb(shadowAlpha, 0, 0, 0)
        };
        resources["CuiPopupShadow"] = settings.HighContrast ? new BoxShadows() : new BoxShadows(popupShadow);
        var shadowBounds = settings.HighContrast ? new Rect(0, 0, 1, 1) : popupShadow.TransformBounds(new Rect(0, 0, 1, 1));
        var shadowMargin = new Thickness(Math.Max(0, -shadowBounds.X), Math.Max(0, -shadowBounds.Y),
            Math.Max(0, shadowBounds.Right - 1), Math.Max(0, shadowBounds.Bottom - 1));
        resources["CuiPopupShadowMargin"] = shadowMargin;
        resources["CuiPopupHorizontalOffset"] = -shadowMargin.Left;
        resources["CuiPopupVerticalOffset"] = 4 * spacing - shadowMargin.Top;
        resources["CuiSubmenuHorizontalOffset"] = 4 * spacing - shadowMargin.Left;
        resources["CuiSubmenuVerticalOffset"] = -shadowMargin.Top;
        resources["CuiDisabledOpacity"] = settings.HighContrast ? .7d : .5d;
        // Attention is a filled-surface role, not an error outline. Derive the
        // outline from its existing semantic border and ensure it remains
        // discernible against the actual field in light and dark appearances.
        resources["CuiValidationBrush"] = new SolidColorBrush(CuiContrast.EnsureForegroundContrast(
            palette.AttentionBorder, InputSurface(palette), 4.5));
        resources["CuiChoiceSize"] = 22d * settings.DisplayScale;
        resources["CuiThumbSize"] = 20d * settings.DisplayScale;
        resources["CuiScrollBarSize"] = 12d * settings.DisplayScale;
        resources["CuiTransparentBrush"] = Brushes.Transparent;
        resources["CuiScrollThumbBrush"] = new SolidColorBrush(palette.Muted);
        resources["CuiScrollThumbHoverBrush"] = new SolidColorBrush(palette.TextSoft);
        resources["CuiScrollThumbPressedBrush"] = new SolidColorBrush(palette.Text);
        resources["CuiScrollThumbRadius"] = new CornerRadius(6 * settings.DisplayScale);
        resources["CuiThumbRadius"] = new CornerRadius(10 * settings.DisplayScale);
        // Switch primitive: a themed rail, hover feedback and thumb. The checked rail reuses the
        // shared accent so per-app accent overrides apply to on-states without extra palette work.
        var switchTrack = WithAlpha(palette.LineStrong, dark ? (byte)0x59 : (byte)0x47);
        resources["CuiSwitchWidth"] = 40d * settings.DisplayScale;
        resources["CuiSwitchHeight"] = 22d * settings.DisplayScale;
        resources["CuiSwitchThumbSize"] = 18d * settings.DisplayScale;
        resources["CuiSwitchPad"] = 3d * settings.DisplayScale;
        resources["CuiSwitchVPad"] = 2d * settings.DisplayScale;
        resources["CuiSwitchTrackRadius"] = new CornerRadius(11d * settings.DisplayScale);
        resources["CuiSwitchThumbRadius"] = new CornerRadius(9d * settings.DisplayScale);
        resources["CuiSwitchTrackBrush"] = new SolidColorBrush(switchTrack);
        resources["CuiSwitchHoverBrush"] = new SolidColorBrush(WithAlpha(palette.LineStrong, dark ? (byte)0x73 : (byte)0x61));
        resources["CuiSwitchThumbBrush"] = new SolidColorBrush(Color.Parse("#FFFFFFFF"));
        // Slider rail: a fixed-height decorative rail, fully rounded at half its own height.
        var railHeight = 6d * settings.DisplayScale;
        resources["CuiRailHeight"] = railHeight;
        resources["CuiRailRadius"] = new CornerRadius(railHeight / 2);
        // Progress primitive: read-only determinate rail. The fill reuses CuiAccentBrush. Its track
        // radius is set by the template from the actual bar height so the ends are always full-round,
        // proportionate to each bar's real height (independent of the slider rail).
        resources["CuiProgressTrackBrush"] = new SolidColorBrush(WithAlpha(palette.LineStrong, (byte)0x50));
        resources["CuiProgressTrackRadius"] = new CornerRadius(6d * settings.DisplayScale);
        resources["CuiSpinnerButtonWidth"] = 24d * settings.DisplayScale;
        resources["CuiSpinnerButtonHeight"] = 17d * settings.DisplayScale;
        resources["CuiShellBrush"] = new SolidColorBrush(palette.TideBase);
        resources["CuiSelectionBrush"] = new SolidColorBrush(palette.AccentSoft);
        resources["CuiSelectionIndicatorBrush"] = new SolidColorBrush(palette.Accent);
        resources["CuiSelectionIndicatorRadius"] = new CornerRadius(2);
        CuiPresetSurfaceResources.Apply(resources, palette, settings);
        ApplyGlowSurfaceResources(resources, palette, settings);
    }

    private static void ApplyGlowSurfaceResources(IResourceDictionary resources, CuiPalette palette, CuiAccessibilitySettings settings)
    {
        if (palette.Theme != CuiTheme.Glow || settings.HighContrast) return;
        // Surface roles carry Glow's visible tonal gradients. Keep the solid
        // palette tokens as contrast/document-colour anchors; a surface does
        // not become a primary action and no outline or effect is introduced.
        var accent = palette.SurfaceAccent ?? palette.Accent;
        var strong = palette.SurfaceAccent is { } surface ? Blend(surface, Colors.Black, .25) : palette.AccentStrong;
        var secondary = palette.SurfaceAccent is { } tint ? Blend(tint, Colors.White, .22) : palette.AccentSecondary;
        IBrush Surface(Color colour, double strength) => CreateGradientBrush(new(
            Blend(colour, strong, .025),
            Blend(colour, accent, strength * .45),
            Blend(colour, secondary, strength),
            new RelativePoint(0, 0, RelativeUnit.Relative),
            new RelativePoint(1, 1, RelativeUnit.Relative)));
        resources["CuiShellMaterial"] = Surface(palette.TideBase, .09);
        resources["CuiWorkspaceMaterial"] = Surface(palette.TideBase, .14);
        resources["CuiSurfaceMaterial"] = Surface(palette.Panel, .12);
        resources["CuiFloatingSurfaceMaterial"] = Surface(palette.Panel2, .16);
    }

    private static Color InputSurface(CuiPalette palette) =>
        palette.Theme switch
        {
            CuiTheme.Professional => palette.TideColour,
            CuiTheme.Glow => palette.Panel3,
            _ => palette.Button
        };

    // Ordinary navigation and tools stay quiet. Accent roles are an explicit
    // action hierarchy, shared by global and descendant theme dictionaries.
    private static void ApplyButtonRoleResources(IResourceDictionary resources, CuiPalette palette, CuiAccessibilitySettings settings)
    {
        var glow = palette.Theme == CuiTheme.Glow && !settings.HighContrast;
        if (glow)
        {
            resources["CuiButtonBrush"] = new SolidColorBrush(palette.Panel);
            resources["CuiButtonHoverBrush"] = new SolidColorBrush(palette.PanelHover);
            resources["CuiButtonPressedBrush"] = new SolidColorBrush(palette.Panel3);
            foreach (var state in new[] { "", "Hover", "Pressed" })
                resources[$"CuiButton{state}ForegroundBrush"] = resources["CuiTextBrush"];
            // The third tier is a dark accent surface, rather than another
            // bright primary pill. Its label keeps a softer accent identity.
            var accent = palette.AccentPalette.Tertiary;
            resources["CuiAccentTertiaryBrush"] = CreateGradientBrush(new(
                Blend(palette.Panel, accent.Start, .18),
                Blend(palette.Panel, accent.Middle, .22),
                Blend(palette.Panel, accent.End, .18), accent.StartPoint, accent.EndPoint));
        }
        var glowPrimary = ReadableActionGradient(palette.AccentPalette.Primary, 5.5);
        var glowSecondary = ReadableActionGradient(Shift(glowPrimary, Colors.White, .12));
        foreach (var role in new[] { "Primary", "Secondary", "Tertiary" })
        {
            var softInk = palette.AccentSecondary;
            if (role == "Tertiary" && glow && resources["CuiAccentTertiaryBrush"] is IGradientBrush subdued)
                foreach (var stop in subdued.GradientStops)
                    softInk = CuiContrast.EnsureForegroundContrast(softInk, stop.Color, 4.5);
            resources[$"CuiAccent{role}ForegroundBrush"] = role == "Tertiary" && glow
                ? new SolidColorBrush(softInk) : glow ? Brushes.White : resources["CuiAccentForegroundBrush"];
            resources[$"CuiAccent{role}HoverForegroundBrush"] = glow ? Brushes.White : resources["CuiAccentForegroundBrush"];
            resources[$"CuiAccent{role}PressedForegroundBrush"] = glow ? Brushes.White : resources["CuiAccentForegroundBrush"];
            var gradient = role switch
            {
                "Primary" => glow ? glowPrimary : palette.AccentPalette.Primary,
                "Secondary" => glow ? glowSecondary : palette.AccentPalette.Secondary,
                _ => palette.AccentPalette.Tertiary
            };
            if (glow)
            {
                // Keep the requested white action label readable for bright
                // user accents too; custom authored fills are applied later.
                resources[$"CuiAccent{role}HoverBrush"] = CreateGradientBrush(ReadableActionGradient(
                    role == "Tertiary" ? Shift(gradient, palette.AccentSecondary, .70) : Shift(gradient, Colors.White, .08)));
                if (role != "Tertiary") resources[$"CuiAccent{role}Brush"] = CreateGradientBrush(ReadableActionGradient(gradient));
            }
            resources[$"CuiAccent{role}PressedBrush"] = CreateGradientBrush(glow
                ? ReadableActionGradient(Shift(gradient, Colors.White, .12))
                : Shift(gradient, palette.AccentStrong, .18));
            if (settings.HighContrast)
                foreach (var state in new[] { "", "Hover", "Pressed" })
                {
                    resources[$"CuiAccent{role}{state}Brush"] = resources["CuiPanelBrush"];
                    resources[$"CuiAccent{role}{state}ForegroundBrush"] = resources["CuiTextBrush"];
                }
            // Both global and scoped resources publish the same accent colours.
            var colour = gradient.Middle;
            resources[$"CuiAccent{role}GlowColor"] = WithAlpha(colour, 0x70);
            resources[$"HavenAccent{role}GlowColor"] = resources[$"CuiAccent{role}GlowColor"];
            resources[$"CuiAccent{role}HoverEffect"] = glow && !settings.ReduceMotion
                ? new DropShadowEffect { Color = colour, BlurRadius = 14, Opacity = .3, OffsetX = 0, OffsetY = 0 }
                : null;
        }
        resources["CuiAccentTextBrush"] = Brushes.Transparent;
        resources["CuiAccentTextHoverBrush"] = Brushes.Transparent;
        resources["CuiAccentTextPressedBrush"] = Brushes.Transparent;
        resources["CuiAccentTextForegroundBrush"] = settings.HighContrast ? resources["CuiTextBrush"]
            : new SolidColorBrush(CuiContrast.EnsureForegroundContrast(palette.AccentSecondary, palette.Panel, 4.5));
        resources["CuiAccentTextHoverForegroundBrush"] = settings.HighContrast ? resources["CuiTextBrush"]
            : new SolidColorBrush(CuiContrast.EnsureForegroundContrast(palette.Accent, palette.Panel, 4.5));
        resources["CuiAccentTextPressedForegroundBrush"] = resources["CuiAccentTextHoverForegroundBrush"];
        var negative = CuiAccentGradient.Horizontal(Color.Parse("#CF2537"), Color.Parse("#C82132"), Color.Parse("#A91629"));
        resources["CuiAccentNegativeBrush"] = CreateGradientBrush(negative);
        resources["CuiAccentNegativeHoverBrush"] = CreateGradientBrush(ReadableActionGradient(Shift(negative, Color.Parse("#F54758"), .28)));
        resources["CuiAccentNegativePressedBrush"] = CreateGradientBrush(ReadableActionGradient(Shift(negative, Color.Parse("#F54758"), .4)));
        foreach (var state in new[] { "", "Hover", "Pressed" })
        {
            resources[$"CuiAccentNegative{state}ForegroundBrush"] = settings.HighContrast ? resources["CuiTextBrush"] : Brushes.White;
            if (settings.HighContrast) resources[$"CuiAccentNegative{state}Brush"] = resources["CuiPanelBrush"];
        }
        resources["CuiAccentNegativeHoverEffect"] = glow && !settings.ReduceMotion
            ? new DropShadowEffect { Color = negative.Middle, BlurRadius = 14, Opacity = .3, OffsetX = 0, OffsetY = 0 }
            : null;
        resources["CuiButtonMotionDuration"] = TimeSpan.FromMilliseconds(180 * settings.EffectiveMotionScale(CuiThemeCatalog.Resolve(palette.Theme).MotionDurationScale));
    }

    private static CuiAccentGradient ReadableActionGradient(CuiAccentGradient gradient, double minimumRatio = 4.5) => gradient with
    {
        Start = CuiContrast.EnsureForegroundContrast(gradient.Start, Colors.White, minimumRatio),
        Middle = CuiContrast.EnsureForegroundContrast(gradient.Middle, Colors.White, minimumRatio),
        End = CuiContrast.EnsureForegroundContrast(gradient.End, Colors.White, minimumRatio)
    };

    private static void ApplyButtonForegroundResources(IResourceDictionary resources, CuiPalette palette)
    {
        // Alias the SAME text/accent brush, without introducing another colour
        // or modifying the palette's button surfaces or accent components.
        foreach (var (key, color) in new (string, Color)[]
        {
            ("CuiButtonForegroundBrush", palette.ButtonForeground),
            ("CuiButtonHoverForegroundBrush", palette.ButtonHoverForeground),
            ("CuiButtonPressedForegroundBrush", palette.ButtonPressedForeground)
        })
            resources[key] = resources[color == palette.Text ? "CuiTextBrush" : "CuiAccentInkBrush"];
        CuiPresetSurfaceResources.ApplyForeground(resources, palette);
        resources["HavenButtonForegroundBrush"] = resources["CuiButtonForegroundBrush"];
        resources["HavenButtonHoverForegroundBrush"] = resources["CuiButtonHoverForegroundBrush"];
        resources["HavenButtonPressedForegroundBrush"] = resources["CuiButtonPressedForegroundBrush"];
    }

    // Existing Haven primitive styles must resolve this SAME scoped palette,
    // rather than escaping to the application's bootstrap appearance.
    private static void ApplyLegacyScopeAliases(IResourceDictionary resources)
    {
        foreach (var (legacy, semantic) in new (string, string)[]
        {
            ("HavenBackgroundBrush", "CuiBackgroundBrush"),
            ("HavenBackgroundPrimaryBrush", "CuiBackgroundBrush"),
            ("HavenBackgroundSecondaryBrush", "CuiPanel2Brush"),
            ("HavenBackgroundElevatedBrush", "CuiPanelBrush"),
            ("HavenInputSurfaceBrush", "CuiInputSurfaceBrush"),
            ("HavenStateHoverBrush", "CuiButtonHoverBrush"),
            ("HavenStatePressedBrush", "CuiButtonPressedBrush"),
            ("HavenStateSelectedBrush", "CuiSelectionBrush"),
            ("HavenAttentionBrush", "CuiAttentionBrush"),
            ("HavenAttentionBorderBrush", "CuiAttentionBorderBrush"),
            ("HavenAccentForegroundBrush", "CuiAccentForegroundBrush"),
            ("HavenTextBrush", "CuiTextBrush"), ("HavenTextSoftBrush", "CuiTextSoftBrush"),
            ("HavenTextPrimaryBrush", "CuiTextBrush"), ("HavenTextSecondaryBrush", "CuiTextSoftBrush"),
            ("HavenTextMutedBrush", "CuiMutedBrush"), ("HavenBorderSubtleBrush", "CuiLineBrush"),
            ("HavenCardSurfaceBrush", "CuiPanelBrush"), ("HavenOverlaySurfaceBrush", "CuiOverlaySurfaceBrush"),
            ("HavenButtonTextPrimaryBrush", "CuiButtonForegroundBrush"), ("HavenButtonTextSecondaryBrush", "CuiTextSoftBrush"),
            ("HavenShadowBrush", "CuiShadowBrush"),
            ("HavenMutedBrush", "CuiMutedBrush"), ("HavenMuted2Brush", "CuiMuted2Brush"),
            ("HavenPanelBrush", "CuiPanelBrush"), ("HavenElevatedBrush", "CuiPanelBrush"),
            ("HavenPanel2Brush", "CuiPanel2Brush"), ("HavenPanel3Brush", "CuiPanel3Brush"),
            ("HavenPanelHoverBrush", "CuiPanelHoverBrush"),
            ("HavenLineBrush", "CuiLineBrush"), ("HavenLineStrongBrush", "CuiLineStrongBrush"),
            ("HavenButtonBrush", "CuiButtonBrush"), ("HavenButtonHoverBrush", "CuiButtonHoverBrush"),
            ("HavenButtonPressedBrush", "CuiButtonPressedBrush"), ("HavenFocusBrush", "CuiFocusBrush"),
            ("HavenAccentBrush", "CuiAccentBrush"), ("HavenAccentPrimaryBrush", "CuiAccentPrimaryBrush"),
            ("HavenAccentSecondaryBrush", "CuiAccentSecondaryBrush"),
            ("HavenAccentTertiaryBrush", "CuiAccentTertiaryBrush"),
            ("HavenAccentPrimaryHoverBrush", "CuiAccentPrimaryHoverBrush"),
            ("HavenAccentSecondaryHoverBrush", "CuiAccentSecondaryHoverBrush"),
            ("HavenAccentTertiaryHoverBrush", "CuiAccentTertiaryHoverBrush"), ("HavenAccentPressedBrush", "CuiAccentPressedBrush"),
            ("HavenAccentInkBrush", "CuiAccentInkBrush"),
            ("HavenAccentSoftBrush", "CuiAccentSoftBrush"), ("HavenBlueSoftBrush", "CuiAccentSoftBrush"),
            ("HavenControlRadius", "CuiControlRadius"), ("HavenCardRadius", "CuiCardRadius"),
            ("HavenPopupRadius", "CuiPopupRadius"), ("HavenMotionDurationScale", "CuiMotionDurationScale")
        })
            resources[legacy] = resources[semantic];
    }

    private static void ApplyAccessibilityResources(
        IResourceDictionary resources,
        CuiThemeExpression expression,
        CuiAccessibilitySettings settings,
        CuiLocalizationContext? localization)
    {
        resources["CuiFontFamilyInterface"] = CuiTypography.ResolveInterfaceFontFamily(settings.InterfaceFontFamilyOverride);
        resources["CuiFontFamilyCode"] = CuiTypography.ResolveCodeFontFamily(settings.CodeFontFamilyOverride);
        resources["CuiFontWeightBody"] = CuiTypography.BodyWeight;
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
        resources["CuiAccentPrimaryHoverBrush"] = CreateGradientBrush(Shift(accents.Primary, accents.Secondary.Start, 0.16));
        resources["CuiAccentSecondaryHoverBrush"] = CreateGradientBrush(Shift(accents.Secondary, Colors.White, 0.08));
        resources["CuiAccentTertiaryHoverBrush"] = CreateGradientBrush(Shift(accents.Tertiary, accents.Secondary.Start, 0.70));
        resources["CuiAccentPressedBrush"] = CreateGradientBrush(Shift(accents.Primary, accents.Tertiary.Middle, 0.22));
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
