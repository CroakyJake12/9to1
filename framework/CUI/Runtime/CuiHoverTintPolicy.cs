using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// The authored hover-paint mode for a control, written from CUI as <c>HoverTint="None|Default|Outline"</c>.
///
/// Why this exists (owner direction, 31 October 2026): hover over a document block must be blank, showing only a
/// ghost accent caret at the pointer, and only container blocks such as a table take the accent outline. Before
/// this vocabulary entry the shared template attached <see cref="CuiHoverFeedback"/> to every
/// Button/TextBox/ComboBox/ListBoxItem/RangeBase/MenuItem/Thumb/NumericUpDown frame
/// (CuiControlTemplates.cs:99) and painted a wash whenever the pointer was over it, so the hover band kept
/// reappearing on surfaces that had asked for none. The only earlier opt-out was <c>Chromeless</c>, which also
/// removes the rest chrome, i.e. it is not a hover-only control.
/// </summary>
public static class CuiHoverTintPolicy
{
    public const string DefaultMode = "Default";
    public const string NoneMode = "None";
    public const string OutlineMode = "Outline";

    private static readonly ConditionalWeakTable<Control, Entry> Modes = new();

    /// <summary>Returns false for a value the vocabulary accepts; the caller turns that into a source diagnostic.</summary>
    public static bool IsKnownMode(string? mode) =>
        mode is null
        || mode.Equals(DefaultMode, StringComparison.OrdinalIgnoreCase)
        || mode.Equals(NoneMode, StringComparison.OrdinalIgnoreCase)
        || mode.Equals(OutlineMode, StringComparison.OrdinalIgnoreCase);

    public static void Set(Control control, string? mode)
    {
        if (!IsKnownMode(mode))
            throw new ArgumentException(
                "HoverTint must be Default, None or Outline.", nameof(mode));
        var normalized = string.IsNullOrWhiteSpace(mode) ? DefaultMode : mode.Trim();
        if (Modes.TryGetValue(control, out var existing))
            existing.Value = normalized;
        else
            Modes.GetValue(control, _ => new Entry(normalized));
    }

    public static string ModeOf(Control control) =>
        Modes.TryGetValue(control, out var entry) ? entry.Value : DefaultMode;

    /// <summary>False means the shared hover feedback must not paint a fill on this control.</summary>
    public static bool PaintsFill(Control control) => !ModeOf(control).Equals(NoneMode, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the control asked for an accent outline instead of a fill on hover.</summary>
    public static bool WantsOutline(Control control) => ModeOf(control).Equals(OutlineMode, StringComparison.OrdinalIgnoreCase);

    private sealed class Entry(string value)
    {
        public string Value = value;
    }
}
