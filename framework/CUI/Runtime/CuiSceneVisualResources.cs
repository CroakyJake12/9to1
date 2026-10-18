using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CakeOS.Cui.Themes;

namespace CakeOS.Cui.Runtime;

/// <summary>Maps canonical surface/appearance resources onto backend primitive templates without a second palette.</summary>
public static class CuiSceneVisualResources
{
    public static ResourceDictionary Create(string surface, CuiAppearance appearance)
        => Create(surface, appearance, null, null);

    internal static ResourceDictionary Create(string surface, CuiAppearance appearance,
        CuiThemeResolution? resolution, string? appId)
    {
        if (!Enum.IsDefined(appearance)) throw new ArgumentOutOfRangeException(nameof(appearance));
        var resources = new ResourceDictionary();
        var accent = resolution?.EffectiveAccent.ResolveColour(appId ?? string.Empty);
        var theme = resolution?.Theme.BaseTheme ?? CuiSurfacePaletteCatalog.ActiveTheme;
        CuiThemeResourceApplier.ApplyToResources(resources,
            CuiSurfacePaletteCatalog.For(surface, appearance, theme, accent), resolution?.Accessibility);
        if (resolution?.Theme.CustomTheme is { } custom)
            CuiCustomThemeResourceApplier.ApplyToResources(resources,
                CuiCustomThemeAccentResolver.Resolve(custom, accent), resolution.Accessibility);
        resources["CuiEffectiveThemeName"] = resolution?.Theme.Name ?? CuiThemeCatalog.Name(theme);
        resources["CuiThemeDiagnostic"] = resolution?.Diagnostic;
        resources["CuiAppearance"] = appearance.ToString();
        resources["CuiSceneThemeVariant"] = Variant(appearance);
        foreach (var (backend, semantic) in Aliases)
            resources[backend] = resources[semantic];
        return resources;
    }
    internal static string? AccentAppId(CuiNativeScene scene) =>
        scene.AppId.StartsWith("app.", StringComparison.Ordinal) ? scene.AppId :
        CuiAppAccentCatalog.Find(scene.AppId)?.AppId ?? CuiAppAccentCatalog.Find(scene.Surface)?.AppId;

    public static ThemeVariant Variant(CuiAppearance appearance) => appearance is CuiAppearance.Dark or CuiAppearance.SuperDark
        ? ThemeVariant.Dark : ThemeVariant.Light;
    private static readonly (string, string)[] Aliases =
    [
        ("ButtonBackground", "CuiButtonBrush"), ("ButtonBackgroundPointerOver", "CuiButtonHoverBrush"),
        ("ButtonBackgroundPressed", "CuiButtonPressedBrush"), ("ButtonForeground", "CuiButtonForegroundBrush"),
        ("ButtonForegroundPointerOver", "CuiButtonHoverForegroundBrush"), ("ButtonForegroundPressed", "CuiButtonPressedForegroundBrush"),
        ("TextControlForeground", "CuiTextBrush"), ("TextControlBackground", "CuiPanelBrush")
    ];
}
