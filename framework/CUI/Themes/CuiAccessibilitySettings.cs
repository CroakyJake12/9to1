using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using System.Globalization;

namespace CakeOS.Cui.Themes;

/// <summary>
/// Framework-level presentation preferences shared by CUI hosts. DisplayScale
/// is a multiplier and never changes domain or persisted application state.
/// </summary>
public sealed record CuiAccessibilitySettings
{
    public static CuiAccessibilitySettings Default { get; } = new();

    public bool ReduceMotion { get; init; }
    public bool HighContrast { get; init; }
    public double DisplayScale { get; init; } = 1d;
    public string? InterfaceFontFamilyOverride { get; init; }
    public string? CodeFontFamilyOverride { get; init; }

    public CuiAccessibilitySettings Validate()
    {
        if (!double.IsFinite(DisplayScale) || DisplayScale is < 0.5d or > 5d)
            throw new ArgumentOutOfRangeException(nameof(DisplayScale), DisplayScale,
                "Display scale must be finite and between 0.5 and 5.");
        return this;
    }

    public double EffectiveMotionScale(double themeScale)
    {
        Validate();
        if (!double.IsFinite(themeScale) || themeScale < 0d)
            throw new ArgumentOutOfRangeException(nameof(themeScale));
        return ReduceMotion ? 0d : themeScale;
    }
}

/// <summary>Shared font families and sizes used by CUI roles.</summary>
public static class CuiTypography
{
    public const string InterfaceFontFamily = "Montserrat, Segoe UI, Arial, sans-serif";
    public const string CodeFontFamily = "Cascadia Mono, Consolas, monospace";
    public const double BodySize = 14d;
    public const double CaptionSize = 12d;
    public const double HeadingSize = 24d;
    public const double CodeSize = 13d;

    public static string ResolveInterfaceFontFamily(string? preferredFamily = null) =>
        ResolveFamily(preferredFamily, InterfaceFontFamily);

    public static string ResolveCodeFontFamily(string? preferredFamily = null) =>
        ResolveFamily(preferredFamily, CodeFontFamily);

    private static string ResolveFamily(string? preferredFamily, string fallbacks)
    {
        if (string.IsNullOrWhiteSpace(preferredFamily))
            return fallbacks;

        var preferred = preferredFamily.Trim();
        if (preferred.Contains(','))
            throw new ArgumentException("A font override must be one family name; fallbacks are supplied by CUI.", nameof(preferredFamily));
        return string.Equals(preferred, "Montserrat", StringComparison.OrdinalIgnoreCase)
            ? fallbacks
            : $"{preferred}, {fallbacks}";
    }
}

/// <summary>
/// Resource lookup contract supplied by a CUI host. Returning null means that
/// a key is absent; callers can distinguish that from a valid empty string.
/// </summary>
public interface ICuiStringResources
{
    string? GetString(string key, CultureInfo culture);
}

/// <summary>Culture, formatting and direction information for a CUI surface.</summary>
public sealed record CuiLocalizationContext
{
    public CultureInfo Culture { get; }
    public FlowDirection FlowDirection => Culture.TextInfo.IsRightToLeft
        ? FlowDirection.RightToLeft
        : FlowDirection.LeftToRight;

    public CuiLocalizationContext(CultureInfo? culture = null)
    {
        Culture = culture ?? CultureInfo.CurrentUICulture;
    }

    public string? GetString(ICuiStringResources resources, string key)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return resources.GetString(key, Culture) ?? resources.GetString(key, CultureInfo.InvariantCulture);
    }

    public string FormatNumber(double value, string? format = null) => value.ToString(format, Culture);

    public string FormatDate(DateTime value, string? format = null) => value.ToString(format, Culture);

    public string FormatDate(DateTimeOffset value, string? format = null) => value.ToString(format, Culture);

    public void ApplyTo(Visual root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Visual.SetFlowDirection(root, FlowDirection);
    }
}

/// <summary>Typed semantics that CUI hosts can apply to their native control.</summary>
public sealed record CuiAccessibilitySemantics(
    string? AccessibleName = null,
    string? AccessibleDescription = null,
    AutomationControlType? Role = null,
    int? TabIndex = null,
    bool? Focusable = null,
    string? Shortcut = null)
{
    public void ApplyTo(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (AccessibleName is not null)
            AutomationProperties.SetName(control, AccessibleName);
        if (AccessibleDescription is not null)
            AutomationProperties.SetHelpText(control, AccessibleDescription);
        if (Role is not null)
            AutomationProperties.SetControlTypeOverride(control, Role);
        if (TabIndex is { } tabIndex)
            control.TabIndex = tabIndex;
        if (Focusable is { } focusable)
        {
            control.Focusable = focusable;
            control.IsTabStop = focusable;
        }
        if (!string.IsNullOrWhiteSpace(Shortcut))
            CuiAccessibilityProperties.SetShortcut(control, Shortcut);
    }
}

/// <summary>Attached CUI metadata consumed by runtime input and semantic tooling.</summary>
public sealed class CuiAccessibilityProperties
{
    private CuiAccessibilityProperties() { }

    public static readonly AttachedProperty<string?> ShortcutProperty =
        AvaloniaProperty.RegisterAttached<CuiAccessibilityProperties, Control, string?>("Shortcut");

    public static void SetShortcut(Control control, string? value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(ShortcutProperty, value);
    }

    public static string? GetShortcut(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(ShortcutProperty);
    }
}

/// <summary>Contrast calculations and deterministic foreground correction.</summary>
public static class CuiContrast
{
    public static double Ratio(Color first, Color second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05d) / (darker + 0.05d);
    }

    public static Color EnsureForegroundContrast(Color foreground, Color background, double minimumRatio = 7d)
    {
        if (!double.IsFinite(minimumRatio) || minimumRatio is < 1d or > 21d)
            throw new ArgumentOutOfRangeException(nameof(minimumRatio));
        var opaqueBackground = Opaque(background);
        var opaqueForeground = Opaque(foreground);
        if (Ratio(opaqueForeground, opaqueBackground) >= minimumRatio)
            return opaqueForeground;

        var blackRatio = Ratio(Colors.Black, opaqueBackground);
        var target = blackRatio >= minimumRatio ? Colors.Black : Colors.White;
        var targetRatio = Ratio(target, opaqueBackground);
        if (targetRatio < minimumRatio)
            return targetRatio >= blackRatio ? target : Colors.Black;

        var low = 0d;
        var high = 1d;
        for (var i = 0; i < 24; i++)
        {
            var middle = (low + high) / 2d;
            var candidate = Blend(opaqueForeground, target, middle);
            if (Ratio(candidate, opaqueBackground) >= minimumRatio)
                high = middle;
            else
                low = middle;
        }
        return Blend(opaqueForeground, target, high);
    }

    private static double RelativeLuminance(Color colour)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045d ? value / 12.92d : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }

        return (0.2126d * Linear(colour.R)) + (0.7152d * Linear(colour.G)) + (0.0722d * Linear(colour.B));
    }

    private static Color Opaque(Color colour) => Color.FromArgb(255, colour.R, colour.G, colour.B);

    private static Color Blend(Color first, Color second, double amount) => Color.FromArgb(
        255,
        (byte)Math.Round(first.R + ((second.R - first.R) * amount)),
        (byte)Math.Round(first.G + ((second.G - first.G) * amount)),
        (byte)Math.Round(first.B + ((second.B - first.B) * amount)));
}

/// <summary>Applies high-contrast and motion preferences without changing theme identity.</summary>
public static class CuiAccessibilityPalette
{
    public static CuiPalette Resolve(CuiPalette palette, CuiAccessibilitySettings? settings = null)
    {
        var profile = (settings ?? CuiAccessibilitySettings.Default).Validate();
        if (!profile.HighContrast)
            return palette;

        var backgroundLuminance = (0.2126d * palette.TideBase.R) + (0.7152d * palette.TideBase.G) + (0.0722d * palette.TideBase.B);
        var isDark = backgroundLuminance < 127.5d;
        var background = isDark ? Colors.Black : Colors.White;
        var foreground = isDark ? Colors.White : Colors.Black;
        var panel2 = isDark ? Color.Parse("#FF171717") : Color.Parse("#FFF0F0F0");
        var panel3 = isDark ? Color.Parse("#FF252525") : Color.Parse("#FFE0E0E0");
        var focus = Color.Parse(isDark ? "#FFFFFF00" : "#FF0000A8");
        var accent = CuiContrast.EnsureForegroundContrast(palette.Accent, background, 4.5d);
        var secondary = CuiContrast.EnsureForegroundContrast(palette.AccentSecondary, background, 4.5d);
        var strong = CuiContrast.EnsureForegroundContrast(palette.AccentStrong, background, 4.5d);

        return palette with
        {
            TideBase = background,
            TideColour = background,
            Accent = accent,
            AccentSecondary = secondary,
            AccentStrong = strong,
            AccentSoft = panel2,
            AccentInk = foreground,
            Text = foreground,
            TextSoft = foreground,
            Muted = foreground,
            Muted2 = foreground,
            Panel = background,
            Panel2 = panel2,
            Panel3 = panel3,
            PanelHover = panel2,
            Line = foreground,
            LineStrong = foreground,
            Button = panel2,
            ButtonHover = panel3,
            ButtonPressed = panel3,
            Focus = focus,
            AccentBorder = accent,
            Attention = CuiContrast.EnsureForegroundContrast(palette.Attention, background, 4.5d),
            AttentionBorder = foreground
        };
    }
}
