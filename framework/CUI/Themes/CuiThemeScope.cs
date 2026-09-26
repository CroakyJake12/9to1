// CUI DefaultTheme Scope — first-class CUI construct for theme scoping.
//
// <DefaultTheme = "Glow"> means: all elements inside this subtree use the Glow theme
// unless more-specific styling overrides it.
// <DefaultTheme = "Bubble"> forces Bubble for that subtree.
// "Default" resolves the currently configured global default theme.
// DefaultTheme is inheritable: nested scopes override, then restore the parent.

using Avalonia;
using Avalonia.Controls;

namespace CakeOS.Cui.Themes;

/// <summary>
/// Represents a theme scope in the CUI document tree. This is a virtual construct —
/// it does NOT create an Avalonia visual element. Instead, the CuiControlLoader uses
/// it to push/pop theme state during tree loading.
/// </summary>
public sealed class CuiThemeScope
{
    /// <summary>
    /// The theme name from the markup: "Default", "Glow", "Bubble", "Retro", "Playful", "Cinematic".
    /// </summary>
    public string ThemeName { get; }

    /// <summary>
    /// Resolved theme after interpreting "Default" → global configured theme.
    /// </summary>
    public CuiTheme ResolvedTheme { get; }

    /// <summary>
    /// Child scopes and components within this scope.
    /// </summary>
    public List<ScopeEntry> Entries { get; } = new();

    public CuiThemeScope(string themeName, CuiTheme resolvedTheme)
    {
        ThemeName = themeName;
        ResolvedTheme = resolvedTheme;
    }

    /// <summary>
    /// Resolves a theme name string to a CuiTheme enum value.
    /// "Default" resolves to the currently configured global theme (fallback: Glow).
    /// Null/empty also resolves to the global default.
    /// </summary>
    public static CuiTheme ResolveThemeName(string? themeName, CuiTheme globalDefault)
    {
        if (string.IsNullOrWhiteSpace(themeName))
            return globalDefault;

        return themeName.Trim() switch
        {
            "Default" or "default" => globalDefault,
            "Glow" or "glow" => CuiTheme.Glow,
            "Bubble" or "bubble" => CuiTheme.Bubble,
            "Retro" or "retro" => CuiTheme.Retro,
            "Playful" or "playful" => CuiTheme.Playful,
            "Cinematic" or "cinematic" => CuiTheme.Cinematic,
            "Professional" or "professional" => CuiTheme.Professional,
            _ => globalDefault // Unknown themes fall back to default, not crash
        };
    }
}

/// <summary>
/// An entry within a CuiThemeScope: either a nested scope or a component.
/// </summary>
public sealed class ScopeEntry
{
    public CuiThemeScope? NestedScope { get; }
    public CakeOS.Cui.CuiComponent? Component { get; }

    public ScopeEntry(CuiThemeScope nestedScope)
    {
        NestedScope = nestedScope;
    }

    public ScopeEntry(CakeOS.Cui.CuiComponent component)
    {
        Component = component;
    }
}

/// <summary>
/// Manages a stack of active theme scopes during CUI tree loading.
/// Push/pop operations track which theme applies at each level of the tree.
/// </summary>
public sealed class CuiThemeScopeStack
{
    private readonly Stack<CuiTheme> _stack = new();
    private readonly CuiTheme _globalDefault;

    public CuiThemeScopeStack(CuiTheme globalDefault = CuiTheme.Glow)
    {
        _globalDefault = globalDefault;
        _stack.Push(globalDefault);
    }

    /// <summary>The currently active theme at this point in the tree.</summary>
    public CuiTheme Current => _stack.Count > 0 ? _stack.Peek() : _globalDefault;

    /// <summary>Push a new theme scope onto the stack.</summary>
    public void Push(CuiTheme theme) => _stack.Push(theme);

    /// <summary>Pop the current scope, restoring the parent theme.</summary>
    public void Pop()
    {
        if (_stack.Count > 1)
            _stack.Pop();
    }
}

/// <summary>
/// Applies a theme scope to an Avalonia control by setting its local Resources
/// with the resolved palette for the scoped theme.
/// </summary>
public static class CuiThemeScopeApplier
{
    /// <summary>
    /// Applies the given theme to a control's local resource scope.
    /// Creates a ResourceDictionary with theme values and merges it into
    /// the control's resources, allowing theme values to cascade to children.
    /// </summary>
    public static void ApplyThemeToControl(
        Control control,
        CuiTheme theme,
        string surface = "Home",
        CuiAccessibilitySettings? accessibility = null,
        CuiLocalizationContext? localization = null)
    {
        var appearance = DetectAppearance();
        var palette = CuiSurfacePaletteCatalog.For(surface, appearance, theme);

        // Create a new ResourceDictionary with the theme resources
        var themeResources = new ResourceDictionary();
        CuiThemeResourceApplier.ApplyToResources(themeResources, palette, accessibility, localization);

        // Merge into the control's existing resources
        control.Resources.MergedDictionaries.Add(themeResources);
    }

    /// <summary>
    /// Applies the current global theme to Application.Current.Resources.
    /// Called once at startup and whenever the global theme changes.
    /// </summary>
    public static void ApplyGlobalTheme(
        CuiTheme theme,
        string surface = "Home",
        CuiAccessibilitySettings? accessibility = null,
        CuiLocalizationContext? localization = null)
    {
        CuiSurfacePaletteCatalog.ActiveTheme = theme;
        var appearance = DetectAppearance();
        var palette = CuiSurfacePaletteCatalog.For(surface, appearance, theme);
        CuiThemeResourceApplier.Apply(palette, accessibility, localization);
    }

    /// <summary>Detects the current appearance from application resources.</summary>
    public static CuiAppearance DetectAppearance()
    {
        var resources = Application.Current?.Resources;
        if (resources is not null && resources["CuiAppearance"] is string appearanceStr)
        {
            return appearanceStr switch
            {
                "SuperBright" => CuiAppearance.SuperBright,
                "Bright" => CuiAppearance.Bright,
                "Dark" => CuiAppearance.Dark,
                "SuperDark" => CuiAppearance.SuperDark,
                _ => CuiAppearance.SuperDark
            };
        }
        return CuiAppearance.SuperDark;
    }
}
