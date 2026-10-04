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
    {
        if (!Enum.IsDefined(appearance)) throw new ArgumentOutOfRangeException(nameof(appearance));
        var resources = new ResourceDictionary();
        CuiThemeResourceApplier.ApplyToResources(resources, CuiSurfacePaletteCatalog.For(surface, appearance));
        resources["CuiAppearance"] = appearance.ToString();
        foreach (var (backend, semantic) in Aliases)
            resources[backend] = resources[semantic];
        return resources;
    }
    public static ThemeVariant Variant(CuiAppearance appearance) => appearance is CuiAppearance.Dark or CuiAppearance.SuperDark
        ? ThemeVariant.Dark : ThemeVariant.Light;
    private static readonly (string, string)[] Aliases =
    [
        ("ButtonBackground", "CuiButtonBrush"), ("ButtonBackgroundPointerOver", "CuiButtonHoverBrush"),
        ("ButtonBackgroundPressed", "CuiButtonPressedBrush"), ("ButtonForeground", "CuiTextBrush"),
        ("ButtonForegroundPointerOver", "CuiTextBrush"), ("ButtonForegroundPressed", "CuiTextBrush"),
        ("TextControlForeground", "CuiTextBrush"), ("TextControlBackground", "CuiPanelBrush")
    ];
}
